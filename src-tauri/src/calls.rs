// Call activity detection for the island.
//
// On Windows this enumerates the system audio sessions (the same source the
// volume mixer uses) and watches for *active* sessions belonging to known
// communication apps (Zoom, Teams, Discord, Slack, ...). While a call is
// running, the app keeps its mic-capture stream in the Active state, so the
// watcher can flag "in a call" without polling every app. On other platforms
// every entry point is a no-op, so the island never shows the call pill.

use serde::Serialize;
use std::sync::{Mutex, OnceLock};

#[derive(Clone, Serialize, Default, PartialEq)]
pub struct CallState {
    pub on_call: bool,
    /// Human-readable name of the app hosting the call, e.g. "Zoom".
    #[serde(skip_serializing_if = "Option::is_none")]
    pub app: Option<String>,
}

/// Last snapshot we emitted, so the frontend can pull initial state on load.
/// Only the Windows watcher writes it, hence the allow on other platforms.
#[cfg_attr(not(target_os = "windows"), allow(dead_code))]
static LAST: OnceLock<Mutex<Option<CallState>>> = OnceLock::new();

#[cfg_attr(not(target_os = "windows"), allow(dead_code))]
fn set_last(state: Option<CallState>) {
    let cell = LAST.get_or_init(|| Mutex::new(None));
    if let Ok(mut guard) = cell.lock() {
        *guard = state;
    }
}

/// Pull the currently known call state (null when not on a call).
#[tauri::command]
pub fn call_get_state() -> Option<CallState> {
    LAST.get()
        .and_then(|m| m.lock().ok())
        .and_then(|g| g.clone())
}

/// Start the call watcher. No-op off Windows.
pub fn spawn(app: tauri::AppHandle) {
    #[cfg(target_os = "windows")]
    win::spawn(app);
    #[cfg(not(target_os = "windows"))]
    let _ = app;
}

#[cfg(target_os = "windows")]
mod win {
    use super::{set_last, CallState};
    use std::time::Duration;
    use tauri::Emitter;
    use windows::core::{Interface, PWSTR};
    use windows::Win32::Foundation::{CloseHandle, HANDLE};
    use windows::Win32::Media::Audio::{
        eConsole, eRender, AudioSessionStateActive, IMMDeviceEnumerator, IAudioSessionControl2,
        IAudioSessionManager2, MMDeviceEnumerator,
    };
    use windows::Win32::System::Com::{
        CoCreateInstance, CoInitializeEx, CLSCTX_ALL, COINIT_MULTITHREADED,
    };
    use windows::Win32::System::Threading::{
        OpenProcess, QueryFullProcessImageNameW, PROCESS_NAME_FORMAT,
        PROCESS_QUERY_LIMITED_INFORMATION,
    };

    /// Known communication apps: exe name (lowercase) → display label. Kept
    /// conservative on purpose — browsers are excluded so normal video playback
    /// never triggers the call pill (web calls in Chrome/Edge are not detected).
    const CALL_APPS: &[(&str, &str)] = &[
        ("zoom.exe", "Zoom"),
        ("zoomrooms.exe", "Zoom Rooms"),
        ("teams.exe", "Teams"),
        ("ms-teams.exe", "Teams"),
        ("msteams.exe", "Teams"),
        ("microsoftteams.exe", "Teams"),
        ("discord.exe", "Discord"),
        ("discordptb.exe", "Discord"),
        ("discordcanary.exe", "Discord"),
        ("slack.exe", "Slack"),
        ("skype.exe", "Skype"),
        ("whatsapp.exe", "WhatsApp"),
        ("telegram.exe", "Telegram"),
        ("wechat.exe", "WeChat"),
        ("weixin.exe", "WeChat"),
        ("g2m.exe", "GoTo Meeting"),
        ("g2mstart.exe", "GoTo Meeting"),
        ("bluejeans.exe", "BlueJeans"),
        ("webexmta.exe", "Webex"),
        ("meetingcenter.exe", "Webex"),
        ("ciscowebexstart.exe", "Webex"),
        ("googlemeet.exe", "Google Meet"),
        ("phonelink.exe", "Phone Link"),
        ("viber.exe", "Viber"),
        ("line.exe", "LINE"),
        ("signal.exe", "Signal"),
        ("session.exe", "Session"),
        ("element.exe", "Element"),
        ("mumble.exe", "Mumble"),
        ("teamspeak3.exe", "TeamSpeak"),
    ];

    /// Full Path of the process with `pid` → lowercase exe name (e.g. "zoom.exe").
    fn process_exe(pid: u32) -> Option<String> {
        if pid == 0 {
            return None;
        }
        let handle: HANDLE = unsafe { OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid) }
            .ok()?;
        if handle.is_invalid() {
            return None;
        }
        let mut buf = [0u16; 520];
        let mut len = buf.len() as u32;
        let ok = unsafe {
            QueryFullProcessImageNameW(
                handle,
                PROCESS_NAME_FORMAT(0),
                PWSTR(buf.as_mut_ptr()),
                &mut len,
            )
            .is_ok()
        };
        unsafe {
            let _ = CloseHandle(handle);
        }
        if !ok || len == 0 {
            return None;
        }
        let path = String::from_utf16_lossy(&buf[..len as usize]);
        std::path::Path::new(&path)
            .file_name()
            .and_then(|f| f.to_str())
            .map(|s| s.to_ascii_lowercase())
    }

    /// Label of the app currently running an *active* audio session, if any.
    /// A call app's mic stream keeps its session in the Active state for the
    /// whole call even during silence, which is what makes this reliable.
    fn active_call_app() -> Option<&'static str> {
        let enumerator: IMMDeviceEnumerator =
            unsafe { CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL) }.ok()?;
        let device = unsafe { enumerator.GetDefaultAudioEndpoint(eRender, eConsole) }.ok()?;
        let manager: IAudioSessionManager2 = unsafe { device.Activate(CLSCTX_ALL, None) }.ok()?;
        let sessions = unsafe { manager.GetSessionEnumerator() }.ok()?;
        let count = unsafe { sessions.GetCount() }.ok()?;
        for i in 0..count {
            let Ok(control) = (unsafe { sessions.GetSession(i) }) else {
                continue;
            };
            let Ok(control) = control.cast::<IAudioSessionControl2>() else {
                continue;
            };
            let Ok(state) = (unsafe { control.GetState() }) else {
                continue;
            };
            if state != AudioSessionStateActive {
                continue;
            }
            let Ok(pid) = (unsafe { control.GetProcessId() }) else {
                continue;
            };
            let Some(exe) = process_exe(pid) else {
                continue;
            };
            for (name, label) in CALL_APPS {
                if exe == *name {
                    return Some(label);
                }
            }
        }
        None
    }

    /// How many consecutive polls a call app must hold its session active
    /// before the pill appears (kills one-off notification blips), and how
    /// many misses before it disappears (tolerates brief enumeration gaps).
    const CONFIRM_POLLS: u8 = 2;
    const END_POLLS: u8 = 3;

    pub fn spawn(app: tauri::AppHandle) {
        std::thread::spawn(move || {
            // WinRT activation needs an initialized apartment on this thread.
            unsafe {
                let _ = CoInitializeEx(None, COINIT_MULTITHREADED);
            }

            let mut shown: Option<CallState> = None;
            let mut last_app: Option<String> = None;
            let mut on_streak: u8 = 0;
            let mut none_streak: u8 = 0;

            loop {
                let detected = active_call_app().map(|l| l.to_string());
                let next = if let Some(app_label) = &detected {
                    none_streak = 0;
                    on_streak = if last_app.as_deref() == Some(app_label.as_str()) {
                        on_streak.saturating_add(1)
                    } else {
                        1
                    };
                    last_app = Some(app_label.clone());
                    if on_streak >= CONFIRM_POLLS || shown.is_some() {
                        Some(CallState {
                            on_call: true,
                            app: last_app.clone(),
                        })
                    } else {
                        None
                    }
                } else {
                    on_streak = 0;
                    last_app = None;
                    none_streak += 1;
                    // Brief gaps keep the pill; only a sustained absence ends it.
                    if none_streak >= END_POLLS {
                        None
                    } else {
                        shown.clone()
                    }
                };

                if next != shown {
                    set_last(next.clone());
                    let _ = app.emit("call://state", next.clone());
                    shown = next;
                }

                std::thread::sleep(Duration::from_millis(800));
            }
        });
    }
}