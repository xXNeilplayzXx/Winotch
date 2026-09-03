//! Theme resolution: light/dark/system/color + Windows accent (registry port).

use crate::config::Config;

#[derive(Clone, Copy, PartialEq, Debug)]
pub struct Palette {
    pub ink: egui_color::INK,
    pub panel: egui_color::PANEL,
    pub text: egui_color::TEXT,
    pub text2: egui_color::TEXT2,
    pub text3: egui_color::TEXT3,
    pub accent: [u8; 3],
    pub led_strong: [u8; 3],
    pub on_accent: [u8; 3],
    pub ok: [u8; 3],
    pub alarm: [u8; 3],
    pub warn: [u8; 3],
}

/// Placeholder module-namespace so the struct above reads cleanly.
pub mod egui_color {
    pub type INK = [u8; 3];
    pub type PANEL = [u8; 3];
    pub type TEXT = [u8; 3];
    pub type TEXT2 = [u8; 3];
    pub type TEXT3 = [u8; 3];
}

pub const DARK: Palette = Palette {
    ink: [10, 13, 20],
    panel: [16, 21, 31],
    text: [233, 237, 244],
    text2: [154, 167, 187],
    text3: [93, 107, 128],
    accent: [79, 195, 255],
    led_strong: [124, 212, 255],
    on_accent: [6, 18, 28],
    ok: [61, 220, 151],
    alarm: [255, 107, 107],
    warn: [255, 193, 77],
};

pub const LIGHT: Palette = Palette {
    ink: [238, 241, 246],
    panel: [255, 255, 255],
    text: [16, 21, 31],
    text2: [74, 85, 104],
    text3: [139, 150, 168],
    accent: [0, 120, 212],
    led_strong: [0, 103, 184],
    on_accent: [255, 255, 255],
    ok: [14, 138, 95],
    alarm: [196, 43, 28],
    warn: [180, 106, 0],
};

pub fn is_light(cfg: &Config) -> bool {
    match cfg.theme.as_str() {
        "light" => true,
        "dark" => false,
        "system" => system_is_light(),
        _ => false, // "color" keeps dark base with custom accent
    }
}

#[cfg(target_os = "windows")]
fn system_is_light() -> bool {
    use windows::core::PCWSTR;
    use windows::Win32::System::Registry::{
        RegCloseKey, RegOpenKeyExW, RegQueryValueExW, HKEY, HKEY_CURRENT_USER,
        KEY_READ, REG_VALUE_TYPE, REG_DWORD,
    };
    const PERSONALIZE: &str = r"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    let to_wide = |s: &str| s.encode_utf16().chain(std::iter::once(0)).collect::<Vec<u16>>();
    let path_w = to_wide(PERSONALIZE);
    let name_w = to_wide("AppsUseLightTheme");
    let mut key = HKEY::default();
    if unsafe { RegOpenKeyExW(HKEY_CURRENT_USER, PCWSTR(path_w.as_ptr()), 0, KEY_READ, &mut key) }.is_ok() {
        let mut value: u32 = 0;
        let mut ty = REG_VALUE_TYPE(0);
        let mut size = std::mem::size_of::<u32>() as u32;
        let status = unsafe {
            RegQueryValueExW(key, PCWSTR(name_w.as_ptr()), None, Some(&mut ty),
                Some(&mut value as *mut u32 as *mut u8), Some(&mut size))
        };
        let _ = unsafe { RegCloseKey(key) };
        if status.is_ok() && ty == REG_DWORD {
            return value != 0;
        }
    }
    false
}

#[cfg(not(target_os = "windows"))]
fn system_is_light() -> bool {
    // macOS: match the system theme roughly via AppleInterfaceStyle presence.
    std::process::Command::new("defaults")
        .args(["read", "-g", "AppleInterfaceStyle"])
        .output()
        .map(|o| o.stdout.starts_with(b"Light"))
        .unwrap_or(false) // absent = dark
}

/// Windows accent color (DWM ColorizationColor), if readable.
#[cfg(target_os = "windows")]
pub fn os_accent() -> Option<[u8; 3]> {
    use windows::core::PCWSTR;
    use windows::Win32::System::Registry::{
        RegCloseKey, RegOpenKeyExW, RegQueryValueExW, HKEY, HKEY_CURRENT_USER,
        KEY_READ, REG_VALUE_TYPE, REG_DWORD,
    };
    const DWM: &str = r"SOFTWARE\Microsoft\Windows\DWM";
    let to_wide = |s: &str| s.encode_utf16().chain(std::iter::once(0)).collect::<Vec<u16>>();
    let path_w = to_wide(DWM);
    let name_w = to_wide("ColorizationColor");
    let mut key = HKEY::default();
    if unsafe { RegOpenKeyExW(HKEY_CURRENT_USER, PCWSTR(path_w.as_ptr()), 0, KEY_READ, &mut key) }.is_ok() {
        let mut value: u32 = 0;
        let mut ty = REG_VALUE_TYPE(0);
        let mut size = std::mem::size_of::<u32>() as u32;
        let status = unsafe {
            RegQueryValueExW(key, PCWSTR(name_w.as_ptr()), None, Some(&mut ty),
                Some(&mut value as *mut u32 as *mut u8), Some(&mut size))
        };
        let _ = unsafe { RegCloseKey(key) };
        if status.is_ok() && ty == REG_DWORD {
            return Some([
                ((value >> 16) & 0xFF) as u8,
                ((value >> 8) & 0xFF) as u8,
                (value & 0xFF) as u8,
            ]);
        }
    }
    None
}

#[cfg(not(target_os = "windows"))]
pub fn os_accent() -> Option<[u8; 3]> { None }

/// Effective palette for the current config.
pub fn resolve(cfg: &Config) -> Palette {
    let mut p = if is_light(cfg) { LIGHT } else { DARK };
    if cfg.theme == "color" {
        p.accent = cfg.accent;
    }
    p
}
