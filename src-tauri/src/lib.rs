use tauri::Manager;

// Learn more about Tauri commands at https://tauri.app/develop/calling-rust/
#[tauri::command]
fn greet(name: &str) -> String {
    format!("Hello, {}! You've been greeted from Rust!", name)
}

/// Exit the app (used by the island's right-click "Close Winotch" menu).
#[tauri::command]
fn quit_app(app: tauri::AppHandle) {
    app.exit(0);
}

/// Hide every island window (right-click "Hide Winotch"). The tray icon is
/// how it comes back: click it or use its Show item.
#[tauri::command]
fn hide_island(app: tauri::AppHandle) {
    tray::hide_island(&app);
}

/// Restart the app (right-click "Restart Winotch" — handy after tweaks).
#[tauri::command]
fn restart_app(app: tauri::AppHandle) {
    app.restart();
}

mod calls;
mod geo;
mod launcher;
mod media;
mod mic;
mod monitors;
mod sizing;
mod stats;
mod theme;
mod tray;

#[cfg(target_os = "macos")]
mod macos;

/// Minimal MSVC C++ runtime shims for the statically-linked WebView2Loader
/// (built with MSVC). Its C++ code allocates with `new[]`/`delete[]`, which on
/// MSVC resolve to these mangled symbols; nothing else provides them on the
/// GNU toolchain. Backed by malloc/free so no extra CRT dependency is added.
#[cfg(target_os = "windows")]
mod cpp_runtime {
    use std::os::raw::c_void;

    extern "C" {
        fn malloc(size: usize) -> *mut c_void;
        fn free(ptr: *mut c_void);
    }

    fn op_new(size: usize) -> *mut c_void {
        let ptr = unsafe { malloc(size) };
        if ptr.is_null() {
            std::process::abort();
        }
        ptr
    }

    fn op_new_nothrow(size: usize) -> *mut c_void {
        // nothrow new returns null instead of throwing on OOM.
        unsafe { malloc(size) }
    }

    fn op_delete(ptr: *mut c_void) {
        if !ptr.is_null() {
            unsafe { free(ptr) };
        }
    }

    #[export_name = "??2@YAPEAX_K@Z"] // operator new(size_t)
    pub extern "C-unwind" fn operator_new(size: usize) -> *mut c_void {
        op_new(size)
    }

    #[export_name = "??_U@YAPEAX_K@Z"] // operator new[](size_t)
    pub extern "C-unwind" fn operator_new_array(size: usize) -> *mut c_void {
        op_new(size)
    }

    #[export_name = "??3@YAXPEAX@Z"] // operator delete(void*)
    pub extern "C-unwind" fn operator_delete(ptr: *mut c_void) {
        op_delete(ptr)
    }

    #[export_name = "??_V@YAXPEAX@Z"] // operator delete[](void*)
    pub extern "C-unwind" fn operator_delete_array(ptr: *mut c_void) {
        op_delete(ptr)
    }

    #[export_name = "??3@YAXPEAX_K@Z"] // operator delete(void*, size_t)
    pub extern "C-unwind" fn operator_delete_sized(ptr: *mut c_void, _size: usize) {
        op_delete(ptr)
    }

    #[export_name = "??_V@YAXPEAX_K@Z"] // operator delete[](void*, size_t)
    pub extern "C-unwind" fn operator_delete_array_sized(ptr: *mut c_void, _size: usize) {
        op_delete(ptr)
    }

    // The global `std::nothrow` constant the nothrow overloads take a ref to.
    #[export_name = "?nothrow@std@@3Unothrow_t@1@B"]
    pub static NOTHROW: u8 = 0;

    // MSVC /GS stack-cookie support: a zeroed cookie makes the save/compare
    // in the loader's guarded functions pass trivially.
    #[export_name = "__security_cookie"]
    pub static SECURITY_COOKIE: u64 = 0;

    #[export_name = "__security_check_cookie"]
    pub extern "C-unwind" fn security_check_cookie(_cookie: u64) {}

    // MSVC CRT thread-safe static-init machinery. `_Init_thread_epoch` is
    // only read; `_tls_guard` is `{ unsigned long _Thread_id; int _Init_flag; }`
    // with `_INITIALIZED == 2`, so the flag lives at byte offset 4.
    #[export_name = "_Init_thread_epoch"]
    pub static INIT_THREAD_EPOCH: u32 = 0;

    #[export_name = "_Init_thread_header"]
    pub extern "C-unwind" fn init_thread_header(guard: *mut u8) -> i32 {
        let flag = unsafe { (guard as *mut i32).add(1) };
        let atomic = unsafe { core::sync::atomic::AtomicI32::from_ptr(flag) };
        use core::sync::atomic::Ordering;
        if atomic.load(Ordering::Acquire) == 2 {
            return 0; // already initialized
        }
        // Claim the init atomically; the winner performs it (returns 1).
        if atomic
            .compare_exchange(0, 2, Ordering::AcqRel, Ordering::Acquire)
            .is_ok()
        {
            1
        } else {
            0
        }
    }

    #[export_name = "_Init_thread_footer"]
    pub extern "C-unwind" fn init_thread_footer(_guard: *mut u8) {}

    #[export_name = "??2@YAPEAX_KAEBUnothrow_t@std@@@Z"] // operator new(size_t, nothrow_t const&)
    pub extern "C-unwind" fn operator_new_nothrow(size: usize, _nothrow: *const c_void) -> *mut c_void {
        op_new_nothrow(size)
    }

    #[export_name = "??3@YAXPEAXAEBUnothrow_t@std@@@Z"] // operator delete(void*, nothrow_t const&)
    pub extern "C-unwind" fn operator_delete_nothrow(ptr: *mut c_void, _nothrow: *const c_void) {
        op_delete(ptr)
    }
}

/// Windows-only: make sure the WebView2 Runtime is installed before Tauri
/// creates its webview. If it's missing, the Evergreen bootstrapper is
/// downloaded and installed silently on first run, so the app ships as a
/// single exe with no prerequisites.
#[cfg(target_os = "windows")]
mod webview2_runtime {
    use std::os::windows::process::CommandExt;
    use std::process::Command;
    use windows::core::PCWSTR;
    use windows::Win32::System::Registry::{
        RegCloseKey, RegOpenKeyExW, HKEY, HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE, KEY_READ,
    };
    use windows::Win32::UI::WindowsAndMessaging::{MessageBoxW, MB_ICONERROR, MB_OK};

    const CREATE_NO_WINDOW: u32 = 0x0800_0000;
    // Client GUID the WebView2 Runtime registers with EdgeUpdate once installed.
    const RUNTIME_CLIENT: &str = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
    const MACHINE_CLIENTS: &str = r"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\";
    const USER_CLIENTS: &str = r"SOFTWARE\Microsoft\EdgeUpdate\Clients\";
    const BOOTSTRAPPER_URL: &str = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    fn to_wide(s: &str) -> Vec<u16> {
        s.encode_utf16().chain(std::iter::once(0)).collect()
    }

    fn registry_key_exists(root: HKEY, path: &str) -> bool {
        let wide = to_wide(path);
        let mut key = HKEY::default();
        let status =
            unsafe { RegOpenKeyExW(root, PCWSTR(wide.as_ptr()), 0, KEY_READ, &mut key) };
        if status.is_ok() {
            let _ = unsafe { RegCloseKey(key) };
            true
        } else {
            false
        }
    }

    fn runtime_installed() -> bool {
        registry_key_exists(HKEY_LOCAL_MACHINE, &format!("{MACHINE_CLIENTS}{RUNTIME_CLIENT}"))
            || registry_key_exists(HKEY_CURRENT_USER, &format!("{USER_CLIENTS}{RUNTIME_CLIENT}"))
    }

    fn run_silent(cmd: &mut Command) {
        cmd.creation_flags(CREATE_NO_WINDOW);
        let _ = cmd.status();
    }

    /// Download the Evergreen bootstrapper to %TEMP% and install it silently
    /// (per-user unless elevated). Returns whether the runtime is now available.
    fn try_install_runtime() -> bool {
        let Some(temp) = std::env::temp_dir().to_str().map(String::from) else {
            return false;
        };
        let installer = format!(r"{temp}\MicrosoftEdgeWebview2Setup.exe");
        let mut download = Command::new("curl.exe");
        download
            .args(["-L", "-sS", "-o"])
            .arg(&installer)
            .arg(BOOTSTRAPPER_URL);
        run_silent(&mut download);
        if !std::path::Path::new(&installer).exists() {
            return false;
        }
        let mut install = Command::new(&installer);
        install.args(["/silent", "/install"]);
        run_silent(&mut install);
        runtime_installed()
    }

    /// Called before the Tauri builder starts. No-op when the runtime is
    /// already present (the normal case on Windows 11 / up-to-date Windows 10).
    pub fn ensure() {
        if runtime_installed() || try_install_runtime() {
            return;
        }
        let title = to_wide("Winotch needs the WebView2 Runtime");
        let body = to_wide(
            "Winotch requires the Microsoft Edge WebView2 Runtime, but it could not be \
             installed automatically.\n\nPlease install it from \
             https://developer.microsoft.com/microsoft-edge/webview2/ and restart Winotch.",
        );
        unsafe {
            MessageBoxW(
                None,
                PCWSTR(body.as_ptr()),
                PCWSTR(title.as_ptr()),
                MB_OK | MB_ICONERROR,
            );
        }
    }
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    // Ensure the WebView2 Runtime exists before the webview is created; the
    // exe otherwise downloads and installs it on first run (single-file app).
    #[cfg(target_os = "windows")]
    webview2_runtime::ensure();

    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .invoke_handler(tauri::generate_handler![
            greet,
            calls::call_get_state,
            media::media_get_state,
            mic::mic_get_state,
            geo::weather_geo,
            media::media_play_pause,
            media::media_next,
            media::media_previous,
            monitors::get_monitors,
            monitors::spawn_island_windows,
            monitors::close_island_windows,
            monitors::broadcast_config,
            launcher::launch_app,
            sizing::update_notch_size,
            sizing::move_notch_to_monitor,
            stats::system_stats,
            quit_app,
            hide_island,
            restart_app,
            theme::get_system_theme,
        ])
        .setup(|app| {
            // System tray: lets the user hide/show the island or quit, and
            // restores it after a right-click "Hide Winotch".
            let _ = tray::setup(app.handle());
            // macOS clamps ordinary windows below the menu bar; override that so
            // the island can sit flush at Y=0 like real notch hardware, and pin
            // it to the highest window layer so it floats above every app.
            #[cfg(target_os = "macos")]
            if let Some(win) = app.get_webview_window("main") {
                if let Ok(ns) = win.ns_window() {
                    macos::allow_top_edge(ns);
                    macos::float_above_all(ns);
                }
            }
            // Windows WebView2 can composite an opaque default background on
            // top of a transparent window, which paints grey/black corners
            // around the rounded island. Pin the webview background to fully
            // transparent on every island window; together with decorations:
            // false + shadow: false (config + monitors.rs) only the rounded
            // pill itself remains visible.
            #[cfg(target_os = "windows")]
            for (_, w) in app.webview_windows() {
                let _ = w.set_background_color(Some(tauri::window::Color(0, 0, 0, 0)));
            }
            // Start the platform media watcher (SMTC on Windows; no-op elsewhere).
            media::spawn(app.handle().clone());
            // Start the call-activity watcher (audio sessions on Windows).
            calls::spawn(app.handle().clone());
            // Start the mic-in-use watcher (capture meters on Windows).
            mic::spawn(app.handle().clone());
            // Watch Windows theme/accent changes and emit them live.
            theme::spawn(app.handle().clone());
            // The transparent always-on-top window is only composited while the
            // app is active, so bring it to the foreground on launch (macOS).
            #[cfg(target_os = "macos")]
            let _ = app.show();
            Ok(())
        })
        .on_window_event(|_window, _event| {
            // macOS only: when another app takes focus, re-assert the island is
            // composited (a macOS quirk can drop always-on-top windows
            // otherwise). Never un-hide a window the user hid via the tray —
            // and never run on Windows, where this same handler was undoing
            // "Hide Winotch" as soon as the hidden window lost focus.
            #[cfg(target_os = "macos")]
            if let tauri::WindowEvent::Focused(false) = _event {
                if _window.is_visible().unwrap_or(false) {
                    let _ = _window.show();
                }
            }
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");

}
