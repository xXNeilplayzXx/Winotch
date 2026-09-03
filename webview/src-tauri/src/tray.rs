//! Tray icon + menu for the WebView shell (tray-icon / muda, main-thread only).
//! Menu: Show Winotch / Start with Windows (check) / Quit. The Tray handle is
//! stored in Tauri's managed state; menu events are drained globally via poll().

use tray_icon::menu::{CheckMenuItem, Menu, MenuEvent, MenuId, MenuItem};
use tray_icon::{Icon, TrayIconBuilder};

pub struct Tray {
    #[allow(dead_code)]
    show_id: MenuId,
    #[allow(dead_code)]
    autostart_id: MenuId,
    #[allow(dead_code)]
    quit_id: MenuId,
}

/// A 32x32 tray icon drawn in code: dark rounded tile + accent LED dot.
fn icon() -> Icon {
    let size = 32usize;
    let mut rgba = vec![0u8; size * size * 4];
    let (r_led, g_led, b_led) = (79u8, 195u8, 255u8);
    for y in 0..size {
        for x in 0..size {
            let cx = (x as f32 - 15.5).abs();
            let cy = (y as f32 - 15.5).abs();
            let inside_sq = cx.max(cy) <= 14.0;
            let corner = cx > 10.0 && cy > 10.0 && ((cx - 10.0).powi(2) + (cy - 10.0).powi(2)).sqrt() > 4.0;
            let i = (y * size + x) * 4;
            if inside_sq && !corner {
                let on_led = ((x as f32 - 16.0).powi(2) + (y as f32 - 16.0).powi(2)).sqrt() <= 6.0;
                if on_led {
                    rgba[i] = r_led; rgba[i + 1] = g_led; rgba[i + 2] = b_led; rgba[i + 3] = 255;
                } else {
                    rgba[i] = 16; rgba[i + 1] = 21; rgba[i + 2] = 31; rgba[i + 3] = 255;
                }
            }
        }
    }
    Icon::from_rgba(rgba, size as u32, size as u32).expect("tray icon")
}

/// Build the tray. Call once from the main thread (Tauri setup).
pub fn init(startup_enabled: bool) -> Tray {
    let show = MenuItem::with_id(MenuId::new("winotch-show"), "Show Winotch", true, None);
    let autostart = CheckMenuItem::with_id(
        MenuId::new("winotch-autostart"),
        "Start with Windows",
        true,
        startup_enabled,
        None,
    );
    let quit = MenuItem::with_id(MenuId::new("winotch-quit"), "Quit Winotch", true, None);

    let menu = Menu::new();
    menu.append_items(&[&show, &autostart]).ok();
    let _ = menu.append(&tray_icon::menu::PredefinedMenuItem::separator());
    menu.append(&quit).ok();

    let _tray = TrayIconBuilder::new()
        .with_tooltip("Winotch")
        .with_icon(icon())
        .with_menu(Box::new(menu))
        .build()
        .expect("tray icon");

    Tray {
        show_id: show.id().clone(),
        autostart_id: autostart.id().clone(),
        quit_id: quit.id().clone(),
    }
}

pub enum Cmd { Show, Quit, ToggleAutostart }

/// Drain pending menu events into commands (matched by menu id string).
pub fn poll() -> Vec<Cmd> {
    let mut out = Vec::new();
    while let Ok(ev) = MenuEvent::receiver().try_recv() {
        if ev.id().0 == "winotch-show" { out.push(Cmd::Show); }
        if ev.id().0 == "winotch-autostart" { out.push(Cmd::ToggleAutostart); }
        if ev.id().0 == "winotch-quit" { out.push(Cmd::Quit); }
    }
    out
}
