//! Tray icon + menu (tray-icon / muda). Handles are main-thread-only, so they
//! live in the App struct. Menu events are polled from the UI loop each frame.
//! Menu: Show Winotch / Start with Windows (check) / Quit.

use tray_icon::menu::{CheckMenuItem, Menu, MenuEvent, MenuId, MenuItem};
use tray_icon::{Icon, TrayIcon, TrayIconBuilder};

/// Main-thread tray state owned by the App.
pub struct Tray {
    _tray: TrayIcon,
    pub show_id: MenuId,
    pub autostart_id: MenuId,
    pub quit_id: MenuId,
    autostart_item: CheckMenuItem,
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

/// Build the tray. Call once from the main thread.
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

    let tray = TrayIconBuilder::new()
        .with_tooltip("Winotch")
        .with_icon(icon())
        .with_menu(Box::new(menu))
        .build()
        .expect("tray icon");

    Tray {
        _tray: tray,
        show_id: show.id().clone(),
        autostart_id: autostart.id().clone(),
        quit_id: quit.id().clone(),
        autostart_item: autostart,
    }
}

impl Tray {
    /// Reflect the autostart state into the tray checkbox.
    pub fn sync_autostart(&self, enabled: bool) {
        self.autostart_item.set_checked(enabled);
    }
}

/// Menu commands drained by the UI loop each frame.
pub enum Cmd { Show, Quit, ToggleAutostart }

/// Drain pending menu events into commands (id-matched against the tray).
pub fn poll(t: &Tray) -> Vec<Cmd> {
    let mut out = Vec::new();
    while let Ok(ev) = MenuEvent::receiver().try_recv() {
        if ev.id == t.show_id { out.push(Cmd::Show); }
        if ev.id == t.autostart_id { out.push(Cmd::ToggleAutostart); }
        if ev.id == t.quit_id { out.push(Cmd::Quit); }
    }
    out
}
