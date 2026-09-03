//! Start-with-Windows: HKCU\...\CurrentVersion\Run on Windows; LaunchAgent
//! symlink is deliberately NOT used on macOS (dev convenience only — no-op).

#[cfg(target_os = "windows")]
pub fn enabled() -> bool {
    use windows::core::PCWSTR;
    use windows::Win32::System::Registry::*;
    let path: &str = r"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    let to_wide = |s: &str| s.encode_utf16().chain(std::iter::once(0)).collect::<Vec<u16>>();
    let path_w = to_wide(path);
    let name_w = to_wide("Winotch");
    let mut key = HKEY::default();
    if unsafe { RegOpenKeyExW(HKEY_CURRENT_USER, PCWSTR(path_w.as_ptr()), 0, KEY_READ, &mut key) }.is_ok() {
        let mut ty = REG_VALUE_TYPE(0);
        let mut buf = [0u8; 1024];
        let mut size = buf.len() as u32;
        let status = unsafe {
            RegQueryValueExW(key, PCWSTR(name_w.as_ptr()), None, Some(&mut ty),
                Some(buf.as_mut_ptr()), Some(&mut size))
        };
        let _ = unsafe { RegCloseKey(key) };
        return status.is_ok();
    }
    false
}

#[cfg(target_os = "windows")]
pub fn set(on: bool) -> Result<(), String> {
    use windows::core::PCWSTR;
    use windows::Win32::Foundation::ERROR_SUCCESS;
    use windows::Win32::System::Registry::*;
    let path: &str = r"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    let to_wide = |s: &str| s.encode_utf16().chain(std::iter::once(0)).collect::<Vec<u16>>();
    let path_w = to_wide(path);
    let name_w = to_wide("Winotch");
    let mut key = HKEY::default();
    let opened = unsafe {
        RegOpenKeyExW(HKEY_CURRENT_USER, PCWSTR(path_w.as_ptr()), 0, KEY_SET_VALUE | KEY_QUERY_VALUE, &mut key)
    };
    if opened != ERROR_SUCCESS {
        return Err(format!("registry open failed: {opened:?}"));
    }
    let result = if on {
        let exe = match std::env::current_exe() {
            Ok(e) => e,
            Err(e) => { let _ = unsafe { RegCloseKey(key) }; return Err(e.to_string()); }
        };
        let value = format!("\"{}\"", exe.display());
        let mut value_w = to_wide(&value);
        value_w.pop(); // drop the NUL; RegSetValueExW takes the byte length
        let bytes = value_w.as_ptr() as *const u8;
        let len = value_w.len() * 2;
        unsafe { RegSetValueExW(key, PCWSTR(name_w.as_ptr()), 0, REG_SZ, Some(std::slice::from_raw_parts(bytes, len))) }
    } else {
        unsafe { RegDeleteValueW(key, PCWSTR(name_w.as_ptr())) }
    };
    let _ = unsafe { RegCloseKey(key) };
    // "file not found" while deleting = already off, which is success for us.
    if result == ERROR_SUCCESS || (!on && result == windows::Win32::Foundation::ERROR_FILE_NOT_FOUND) {
        Ok(())
    } else {
        Err(format!("registry write failed: {result:?}"))
    }
}

#[cfg(not(target_os = "windows"))]
pub fn enabled() -> bool { false }

#[cfg(not(target_os = "windows"))]
pub fn set(_on: bool) -> Result<(), String> {
    Err("autostart is only implemented on Windows".into())
}
