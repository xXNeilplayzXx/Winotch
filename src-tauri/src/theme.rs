// Windows theme + accent integration for the island's Theme system.
//
// The frontend has three choices — Light, Dark, System Default. For "System
// Default" (and for the accent underline under every theme) we need the real
// Windows values, live: this module reads the current app theme
// (HKCU\...\Themes\Personalize\AppsUseLightTheme) and accent color
// (HKCU\...\DWM\ColorizationColor, the same COLORREF the taskbar uses) and
// watches both keys with RegNotifyChangeKeyValue, so toggling Windows
// appearance or the accent color is emitted to every window instantly as
// `winotch://theme` — no restart, no startup-only snapshot. Off Windows this
// is a no-op and the frontend falls back to its defaults.

use serde::Serialize;
#[cfg(target_os = "windows")]
use tauri::Emitter;

#[derive(Clone, Serialize)]
pub struct SystemTheme {
    /// "light" or "dark" — the current Windows app theme.
    pub theme: String,
    /// Windows accent color as [r, g, b].
    pub accent: [u8; 3],
}

fn fallback() -> SystemTheme {
    // Old brand gold, used only when the registry is unreadable.
    SystemTheme { theme: "dark".into(), accent: [233, 185, 73] }
}

/// Immediate current values. Emits them too, so every window converges even if
/// the watcher's initial snapshot wasn't heard.
#[tauri::command]
pub fn get_system_theme(app: tauri::AppHandle) -> SystemTheme {
    #[cfg(target_os = "windows")]
    {
        if let Some(st) = win::read_current() {
            let _ = app.emit("winotch://theme", st.clone());
            return st;
        }
    }
    let _ = &app;
    fallback()
}

/// Watch Windows theme/accent changes and emit `winotch://theme` forever.
pub fn spawn(app: tauri::AppHandle) {
    #[cfg(target_os = "windows")]
    win::spawn_watcher(app);
    #[cfg(not(target_os = "windows"))]
    let _ = app;
}

#[cfg(target_os = "windows")]
mod win {
    use super::SystemTheme;
    use tauri::Emitter;
    use windows::core::PCWSTR;
    use windows::Win32::System::Registry::{
        RegCloseKey, RegNotifyChangeKeyValue, RegOpenKeyExW, RegQueryValueExW, HKEY,
        HKEY_CURRENT_USER, KEY_NOTIFY, KEY_READ, REG_NOTIFY_CHANGE_LAST_SET, REG_VALUE_TYPE,
        REG_DWORD,
    };

    const PERSONALIZE: &str = r"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    const DWM: &str = r"SOFTWARE\Microsoft\Windows\DWM";
    const APPS_USE_LIGHT: &str = "AppsUseLightTheme";
    const COLORIZATION: &str = "ColorizationColor";

    fn to_wide(s: &str) -> Vec<u16> {
        s.encode_utf16().chain(std::iter::once(0)).collect()
    }

    fn read_dword(root: HKEY, path: &str, name: &str) -> Option<u32> {
        let path_w = to_wide(path);
        let name_w = to_wide(name);
        let mut key = HKEY::default();
        if unsafe {
            RegOpenKeyExW(root, PCWSTR(path_w.as_ptr()), 0, KEY_READ | KEY_NOTIFY, &mut key)
        }
        .is_ok()
        {
            let mut value: u32 = 0;
            let mut ty = REG_VALUE_TYPE(0);
            let mut size = std::mem::size_of::<u32>() as u32;
            let status = unsafe {
                RegQueryValueExW(
                    key,
                    PCWSTR(name_w.as_ptr()),
                    None,
                    Some(&mut ty),
                    Some(&mut value as *mut u32 as *mut u8),
                    Some(&mut size),
                )
            };
            let _ = unsafe { RegCloseKey(key) };
            if status.is_ok() && ty == REG_DWORD {
                return Some(value);
            }
        }
        None
    }

    /// Snapshot theme + accent from the registry in one pass.
    pub fn read_current() -> Option<SystemTheme> {
        let light = read_dword(HKEY_CURRENT_USER, PERSONALIZE, APPS_USE_LIGHT)?;
        let color = read_dword(HKEY_CURRENT_USER, DWM, COLORIZATION)?;
        // DWM ColorizationColor is 0xAABBGGRR — the low 24 bits are B, G, R.
        Some(SystemTheme {
            theme: if light != 0 { "light".into() } else { "dark".into() },
            accent: [((color >> 16) & 0xFF) as u8, ((color >> 8) & 0xFF) as u8, (color & 0xFF) as u8],
        })
    }

    /// Block until the given registry key changes, then re-read + emit.
    fn watch_loop(app: &tauri::AppHandle, path: &str) {
        let path_w = to_wide(path);
        let mut key = HKEY::default();
        if unsafe {
            RegOpenKeyExW(
                HKEY_CURRENT_USER,
                PCWSTR(path_w.as_ptr()),
                0,
                KEY_READ | KEY_NOTIFY,
                &mut key,
            )
        }
        .is_err()
        {
            return;
        }
        loop {
            // One-shot notification; re-armed at the top of each iteration.
            let _ =
                unsafe { RegNotifyChangeKeyValue(key, false, REG_NOTIFY_CHANGE_LAST_SET, None, false) };
            if let Some(st) = read_current() {
                let _ = app.emit("winotch://theme", st);
            }
        }
    }

    pub fn spawn_watcher(app: tauri::AppHandle) {
        // Initial snapshot so windows start correct even if the webview mounts
        // after this thread began.
        if let Some(st) = read_current() {
            let _ = app.emit("winotch://theme", st);
        }
        // Two threads: theme key and accent key notify independently.
        let a = app.clone();
        std::thread::spawn(move || watch_loop(&a, PERSONALIZE));
        let b = app.clone();
        std::thread::spawn(move || watch_loop(&b, DWM));
    }
}