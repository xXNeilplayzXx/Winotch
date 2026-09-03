//! Priority status alerts — battery, Wi-Fi, microphone/camera use.
//! Ported from the reference C# Winotch (PriorityStatus.cs): a watcher thread
//! samples a snapshot, and every EDGE change emits a `winotch://status` event
//! that the page renders as a compact toast below the notch (charger connect
//! carries the green battery-fill flourish). No-op off Windows.

use serde::Serialize;
use tauri::AppHandle;

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct StatusAlert {
    pub icon: &'static str,
    pub title: String,
    pub body: String,
    pub percent: Option<i32>,
    pub flourish: bool,
}

impl StatusAlert {
    fn low_battery(percent: i32) -> Self {
        Self { icon: "🔋", title: "Low battery".into(), body: format!("{percent}% remaining"), percent: Some(percent), flourish: false }
    }
    fn charger_connected(percent: i32) -> Self {
        Self { icon: "⚡", title: "Charger connected".into(), body: format!("{percent}% and charging"), percent: Some(percent), flourish: true }
    }
    fn charger_disconnected(percent: i32) -> Self {
        Self { icon: "🔋", title: "Charger disconnected".into(), body: format!("{percent}% battery"), percent: Some(percent), flourish: false }
    }
    fn wifi_lost(name: &str) -> Self {
        Self { icon: "📡", title: "Wi-Fi disconnected".into(), body: format!("{name} connection lost"), percent: None, flourish: false }
    }
    fn wifi_reconnected(name: &str) -> Self {
        Self { icon: "📶", title: "Wi-Fi reconnected".into(), body: format!("Connected to {name}"), percent: None, flourish: false }
    }
    fn microphone_active() -> Self {
        Self { icon: "🎤", title: "Microphone active".into(), body: "An app is using your microphone".into(), percent: None, flourish: false }
    }
    fn camera_active() -> Self {
        Self { icon: "📷", title: "Camera active".into(), body: "An app is using your camera".into(), percent: None, flourish: false }
    }
}

#[derive(Clone, PartialEq, Default)]
struct Snapshot {
    percent: i32,
    charging: bool,
    wifi: Option<String>,
    mic: bool,
    cam: bool,
}

const LOW_BATTERY: i32 = 20;

struct Tracker {
    last: Option<Snapshot>,
}

impl Tracker {
    fn new() -> Self { Self { last: None } }

    /// Edge-triggered: returns the alerts for this snapshot (none if nothing
    /// changed). The first snapshot only reports camera/mic/low-battery — the
    /// same quiet start the reference uses, so boot never spams charger/Wi-Fi.
    fn next(&mut self, cur: &Snapshot) -> Vec<StatusAlert> {
        let mut out = Vec::new();
        match &self.last {
            None => {
                if cur.cam { out.push(StatusAlert::camera_active()); }
                if cur.mic { out.push(StatusAlert::microphone_active()); }
                if is_low_battery(cur) { out.push(StatusAlert::low_battery(cur.percent)); }
            }
            Some(last) => {
                if !last.cam && cur.cam { out.push(StatusAlert::camera_active()); }
                if !last.mic && cur.mic { out.push(StatusAlert::microphone_active()); }
                if is_low_battery(cur) && !is_low_battery(last) {
                    out.push(StatusAlert::low_battery(cur.percent));
                }
                if !last.charging && cur.charging {
                    out.push(StatusAlert::charger_connected(cur.percent));
                } else if last.charging && !cur.charging {
                    out.push(StatusAlert::charger_disconnected(cur.percent));
                }
                match (&last.wifi, &cur.wifi) {
                    (Some(name), None) => out.push(StatusAlert::wifi_lost(name)),
                    (Some(_), Some(cur_name)) if cur_name != last.wifi.as_deref().unwrap() => {
                        out.push(StatusAlert::wifi_reconnected(cur_name));
                    }
                    _ => {}
                }
            }
        }
        self.last = Some(cur.clone());
        out
    }
}

fn is_low_battery(s: &Snapshot) -> bool {
    !s.charging && s.percent <= LOW_BATTERY
}

/// Start the watcher thread. No-op off Windows.
pub fn spawn(app: AppHandle) {
    #[cfg(target_os = "windows")]
    win::spawn(app);
    #[cfg(not(target_os = "windows"))]
    { let _ = app; }
}

#[cfg(target_os = "windows")]
mod win {
    use super::{Snapshot, StatusAlert, Tracker};
    use std::time::Duration;
    use tauri::{AppHandle, Emitter};
    use windows::core::{PCWSTR, PWSTR};
    use windows::Win32::System::Power::{GetSystemPowerStatus, SYSTEM_POWER_STATUS};
    use windows::Win32::System::Registry::{
        RegCloseKey, RegEnumKeyExW, RegOpenKeyExW, RegQueryValueExW, HKEY, HKEY_CURRENT_USER,
        KEY_READ, REG_DWORD, REG_QWORD, REG_VALUE_TYPE,
    };

    fn battery() -> (i32, bool) {
        let mut s = SYSTEM_POWER_STATUS::default();
        if unsafe { GetSystemPowerStatus(&mut s) }.is_err() || s.BatteryLifePercent == 255 {
            return (100, false);
        }
        (s.BatteryLifePercent as i32, s.ACLineStatus == 1)
    }

    fn wifi_name() -> Option<String> {
        let out = std::process::Command::new("netsh")
            .args(["wlan", "show", "interfaces"])
            .output()
            .ok()?;
        let text = String::from_utf8_lossy(&out.stdout);
        for line in text.lines() {
            let line = line.trim();
            if let Some(rest) = line.strip_prefix("SSID") {
                let rest = rest.trim_start_matches([':', ' ']);
                let name = rest.trim().trim_matches('"').trim();
                if !name.is_empty() && !name.eq_ignore_ascii_case("ssid") {
                    return Some(name.to_string());
                }
            }
        }
        None
    }

    /// Privacy-use edge: LastUsedTimeStart > 0 while LastUsedTimeStop == 0 —
    /// mirrors PriorityStatusService.IsActivePrivacyUse. Walks the per-app
    /// subkeys of the consent store, like the reference.
    fn capability_active(path: &[u16]) -> bool {
        let mut key = HKEY::default();
        let ok = unsafe {
            RegOpenKeyExW(HKEY_CURRENT_USER, PCWSTR(path.as_ptr()), 0, KEY_READ, &mut key)
        };
        if ok.0 != 0 || key.is_invalid() {
            return false;
        }
        let out = key_active(&key) || any_subkey_active(&key);
        unsafe { let _ = RegCloseKey(key); }
        out
    }

    fn key_active(key: &HKEY) -> bool {
        let start = read_qword(key, &utf16("LastUsedTimeStart"));
        let stop = read_qword(key, &utf16("LastUsedTimeStop"));
        start > 0 && stop == 0
    }

    fn any_subkey_active(key: &HKEY) -> bool {
        let mut idx = 0u32;
        loop {
            let mut name = vec![0u16; 260];
            let mut len = name.len() as u32;
            let res = unsafe {
                RegEnumKeyExW(
                    *key,
                    idx,
                    PWSTR(name.as_mut_ptr()),
                    &mut len,
                    None,
                    PWSTR::null(),
                    None,
                    None,
                )
            };
            if res.0 != 0 { break; }
            name.truncate(len as usize);
            let mut child = HKEY::default();
            let opened = unsafe { RegOpenKeyExW(*key, PCWSTR(name.as_ptr()), 0, KEY_READ, &mut child) };
            if opened.0 == 0 && !child.is_invalid() {
                let active = key_active(&child) || any_subkey_active(&child);
                unsafe { let _ = RegCloseKey(child); }
                if active { return true; }
            }
            idx += 1;
        }
        false
    }

    fn read_qword(key: &HKEY, name: &[u16]) -> u64 {
        let mut buf = [0u8; 8];
        let mut len = buf.len() as u32;
        let mut ty = REG_VALUE_TYPE(0);
        let res = unsafe {
            RegQueryValueExW(
                *key,
                PCWSTR(name.as_ptr()),
                None,
                Some(&mut ty),
                Some(buf.as_mut_ptr()),
                Some(&mut len),
            )
        };
        if res.0 != 0 { return 0; }
        match ty {
            REG_QWORD => u64::from_le_bytes(buf),
            REG_DWORD => u32::from_le_bytes(buf[..4].try_into().unwrap()) as u64,
            _ => 0,
        }
    }

    fn utf16(s: &str) -> Vec<u16> { s.encode_utf16().chain(std::iter::once(0)).collect() }

    fn consent_path(cap: &str) -> Vec<u16> {
        let p = format!(
            r"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{cap}"
        );
        utf16(&p)
    }

    pub fn spawn(app: AppHandle) {
        std::thread::spawn(move || {
            let mut tracker = Tracker::new();
            let mut cycle: u32 = 0;
            loop {
                let (percent, charging) = battery();
                let wifi = if cycle % 4 == 0 { wifi_name() } else { tracker.last.as_ref().and_then(|l| l.wifi.clone()) };
                let mic = capability_active(&consent_path("microphone"));
                let cam = capability_active(&consent_path("webcam"));
                let snap = Snapshot { percent, charging, wifi, mic, cam };
                for alert in tracker.next(&snap) {
                    let _ = app.emit("winotch://status", &alert);
                }
                cycle += 1;
                std::thread::sleep(Duration::from_millis(1500));
            }
        });
    }

}