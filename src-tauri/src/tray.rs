// System-tray integration: the island has no taskbar entry, so when it is
// hidden (right-click → Hide Winotch) the tray icon is how it comes back —
// left-click toggles visibility, the menu offers Show / Hide / Quit.

use tauri::menu::{Menu, MenuItem, PredefinedMenuItem};
use tauri::tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent};
use tauri::{AppHandle, Emitter, Manager};

const SHOW_ID: &str = "show-winotch";
const HIDE_ID: &str = "hide-winotch";
const RERUN_SETUP_ID: &str = "rerun-setup-winotch";
const QUIT_ID: &str = "quit-winotch";

pub fn setup(app: &AppHandle) -> tauri::Result<()> {
    let show = MenuItem::with_id(app, SHOW_ID, "Show Winotch", true, None::<&str>)?;
    let hide = MenuItem::with_id(app, HIDE_ID, "Hide Winotch", true, None::<&str>)?;
    let rerun_setup = MenuItem::with_id(app, RERUN_SETUP_ID, "Rerun Setup…", true, None::<&str>)?;
    let quit = MenuItem::with_id(app, QUIT_ID, "Quit", true, None::<&str>)?;
    let separator = PredefinedMenuItem::separator(app)?;
    let menu = Menu::with_items(app, &[&show, &hide, &separator, &rerun_setup, &separator, &quit])?;

    let Some(icon) = app.default_window_icon().cloned() else {
        return Ok(()); // no bundled icon; tray skipped
    };

    let tray = TrayIconBuilder::new()
        .icon(icon)
        .menu(&menu)
        .show_menu_on_left_click(false)
        .tooltip("Winotch")
        .on_menu_event(|app, event| match event.id.as_ref() {
            SHOW_ID => show_island(app),
            HIDE_ID => hide_island(app),
            // The frontend reopens the wizard (it re-shows and re-sizes the
            // window itself), so just ask every webview to do it.
            RERUN_SETUP_ID => {
                let _ = app.emit("winotch://rerun-setup", ());
            }
            QUIT_ID => app.exit(0),
            _ => {}
        })
        .build(app)?;

    // Left-clicking the tray icon toggles the island's visibility.
    tray.on_tray_icon_event(|tray, event| {
        if let TrayIconEvent::Click {
            button: MouseButton::Left,
            button_state: MouseButtonState::Up,
            ..
        } = event
        {
            let app = tray.app_handle();
            match app
                .get_webview_window("main")
                .and_then(|w| w.is_visible().ok())
            {
                Some(true) => hide_island(app),
                _ => show_island(app),
            }
        }
    });

    Ok(())
}

/// Every island window: the main one plus any `island-*` extras.
pub fn hide_island(app: &AppHandle) {
    for (label, win) in app.webview_windows() {
        if label == "main" || label.starts_with("island-") {
            let _ = win.hide();
        }
    }
}

/// Bring every island window back, focusing the main one.
pub fn show_island(app: &AppHandle) {
    if let Some(main) = app.get_webview_window("main") {
        let _ = main.show();
        let _ = main.set_focus();
    }
    for (label, win) in app.webview_windows() {
        if label.starts_with("island-") {
            let _ = win.show();
        }
    }
}