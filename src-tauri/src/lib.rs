use tauri::Manager;

// Learn more about Tauri commands at https://tauri.app/develop/calling-rust/
#[tauri::command]
fn greet(name: &str) -> String {
    format!("Hello, {}! You've been greeted from Rust!", name)
}

mod media;

#[cfg(target_os = "macos")]
mod macos;

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .invoke_handler(tauri::generate_handler![
            greet,
            media::media_get_state,
            media::media_play_pause,
            media::media_next,
            media::media_previous,
        ])
        .setup(|app| {
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
            // Start the platform media watcher (SMTC on Windows; no-op elsewhere).
            media::spawn(app.handle().clone());
            // The transparent always-on-top window is only composited while the
            // app is active, so bring it to the foreground on launch (macOS).
            #[cfg(target_os = "macos")]
            let _ = app.show();
            Ok(())
        })
        .on_window_event(|window, event| {
            // When another app takes focus, re-assert the island is composited
            // (a macOS quirk can drop always-on-top windows otherwise). This
            // must not steal keyboard focus back — show() only, never set_focus.
            if let tauri::WindowEvent::Focused(false) = event {
                let _ = window.show();
            }
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");

}
