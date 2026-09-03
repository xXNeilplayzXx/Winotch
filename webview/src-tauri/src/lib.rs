//! Winotch — Tauri v2 shell hosting the designv2 island UI.
//!
//! The page (webview/ui/index.html) is the full app: it renders the idle pill,
//! the widget deck, the settings accordion and the setup wizard, and drives the
//! OS through the commands below + the pushed events:
//!   media://state   — SMTC session updates
//!   winotch://theme — Windows light/dark + accent changes
//!
//! The window is transparent, frameless, shadowless and always-on-top, so the
//! island floats on the desktop with no visible box around it.

mod autostart;
mod clipboard;
mod config;
mod launcher;
mod media;
mod stats;
mod status;
mod theme;
mod tray;
mod weather;

use config::Config;
use serde::Serialize;
use std::sync::Mutex;
use tauri::{
    AppHandle, Emitter, Manager, PhysicalPosition, PhysicalSize, WebviewUrl, WebviewWindowBuilder,
};

/// Serialize window fit+move sequences so the event loop cannot reorder a
/// `set_size` behind a later `set_position` (both are async commands).
static POSITION_LOCK: Mutex<()> = Mutex::new(());

fn locked<T>(f: impl FnOnce() -> T) -> T {
    let _g = POSITION_LOCK.lock().unwrap_or_else(|p| p.into_inner());
    f()
}

/// The monitor whose bounds contain the given GLOBAL PHYSICAL point (the
/// virtual-screen coordinate space — primary monitor is usually at (0,0),
/// monitors to its left/above have negative offsets).
fn monitor_containing_at(window: &tauri::WebviewWindow, cx: f64, cy: f64) -> Option<tauri::Monitor> {
    let mons = window.available_monitors().ok()?;
    mons.into_iter().find(|m| {
        let p = m.position();
        let s = m.size();
        cx >= p.x as f64 && cx < (p.x + s.width as i32) as f64
            && cy >= p.y as f64 && cy < (p.y + s.height as i32) as f64
    })
}

/// The monitor whose bounds contain the window's center — the monitor a
/// dropped notch "belongs to", regardless of which monitor it started on.
fn containing_monitor(window: &tauri::WebviewWindow) -> Option<tauri::Monitor> {
    let pos = window.outer_position().ok()?;
    let size = window.outer_size().ok()?;
    monitor_containing_at(
        window,
        pos.x as f64 + size.width as f64 / 2.0,
        pos.y as f64 + size.height as f64 / 2.0,
    )
}

/// Top-center physical position of a monitor for a window of the given width.
fn top_center_for(m: &tauri::Monitor, win_w: u32) -> PhysicalPosition<i32> {
    let p = m.position();
    let s = m.size();
    PhysicalPosition::new(p.x + (s.width as i32 - win_w as i32) / 2, p.y)
}

// ---------- persisted window position ----------

/// The notch's position, persisted to disk so restarts land exactly where the
/// user left it — before the page has loaded (boot applies it in setup()).
/// Coordinates are GLOBAL PHYSICAL px (see above); mode mirrors the page's
/// presets: "default" (top-center of some monitor), "left", "right", "custom".
#[derive(Clone, Serialize, serde::Deserialize, Default)]
struct SavedPos {
    mode: String,
    x: i32,
    y: i32,
}

fn pos_file() -> std::path::PathBuf {
    config::data_dir().join("window_position.json")
}

fn load_saved_pos() -> Option<SavedPos> {
    std::fs::read_to_string(pos_file())
        .ok()
        .and_then(|s| serde_json::from_str(&s).ok())
}

fn save_saved_pos(pos: &SavedPos) {
    if let Ok(json) = serde_json::to_string(pos) {
        let _ = std::fs::write(pos_file(), json);
    }
}

/// Monitor list for the settings display picker. `is_primary` is true for the
/// monitor containing the (0,0) origin — Tauri's Monitor has no primary flag.
#[derive(Serialize)]
struct MonitorInfo {
    device: String,
    name: String,
    width: u32,
    height: u32,
    is_primary: bool,
}

fn monitor_infos(window: &tauri::WebviewWindow) -> Vec<MonitorInfo> {
    let mut out = Vec::new();
    if let Ok(mons) = window.available_monitors() {
        for m in mons {
            let name = m.name().cloned().unwrap_or_else(|| "Display".into());
            let size = m.size();
            let pos = m.position();
            out.push(MonitorInfo {
                device: name.clone(),
                name,
                width: size.width,
                height: size.height,
                is_primary: pos.x == 0 && pos.y == 0,
            });
        }
    }
    out
}

#[tauri::command]
fn get_monitors(window: tauri::WebviewWindow) -> Vec<MonitorInfo> {
    monitor_infos(&window)
}

/// Resolve the notch (main) window from any caller — the settings window
/// drives the notch through these commands, so they must not act on the
/// window that happens to invoke them.
fn notch_window(app: &tauri::AppHandle) -> Option<tauri::WebviewWindow> {
    app.get_webview_window("main")
}

/// Move the notch window to a saved monitor (by name or "primary"), docked
/// top-center of it.
#[tauri::command]
fn move_notch_to_monitor(app: AppHandle, monitor_name: String) {
    let Some(window) = notch_window(&app) else { return };
    let mons = match window.available_monitors() {
        Ok(m) if !m.is_empty() => m,
        _ => return,
    };
    let target = mons
        .iter()
        .find(|m| m.name().map(|n| *n == monitor_name).unwrap_or(false))
        .or_else(|| mons.iter().find(|m| {
            let p = m.position();
            p.x == 0 && p.y == 0
        }))
        .cloned();
    let Some(m) = target else { return };
    let win = window.outer_size().unwrap_or(PhysicalSize::new(330u32, 36u32));
    let pos = top_center_for(&m, win.width);
    locked(|| {
        let _ = window.set_size(PhysicalSize::new(win.width, win.height));
        let _ = window.set_position(pos);
        save_saved_pos(&SavedPos { mode: "custom".into(), x: pos.x, y: pos.y });
    });
}

/// Grow the transparent window to hug the island's rendered size, optionally
/// re-centering it at the top-center of the monitor it's on (the "default"
/// position preset — monitor-aware: whichever display the notch currently
/// occupies).
///
/// When `recenter` is false (custom drag / left / right presets) the window's
/// horizontal CENTER is preserved across the resize: the island is centered
/// inside the window, so anchoring an edge instead would make the notch
/// visibly slide by half the growth on every expand/collapse ("resets toward
/// the default position" bug).
#[tauri::command]
fn fit_island_to_content(app: AppHandle, width: f64, height: f64, recenter: bool) {
    let Some(window) = notch_window(&app) else { return };
    locked(|| {
        let mon = containing_monitor(&window)
            .or_else(|| window.current_monitor().ok().flatten());
        let sf = mon.as_ref().map(|m| m.scale_factor()).unwrap_or(1.0);
        let w = ((width + 4.0).max(60.0) * sf).round() as u32;
        let h = ((height + 4.0).max(36.0) * sf).round() as u32;
        // Cap against the monitor's width so a huge deck never hangs off-screen.
        let w = mon.as_ref().map(|m| w.min(m.size().width)).unwrap_or(w);
        let old_pos = window.outer_position().ok();
        let old_size = window.outer_size().ok();
        let _ = window.set_size(PhysicalSize::new(w, h));
        if recenter {
            if let Some(m) = mon {
                let pos = top_center_for(&m, w);
                let _ = window.set_position(pos);
                save_saved_pos(&SavedPos { mode: "default".into(), x: pos.x, y: pos.y });
            }
        } else if let (Some(p), Some(s)) = (old_pos, old_size) {
            let center_x = p.x as f64 + s.width as f64 / 2.0;
            let new_x = (center_x - w as f64 / 2.0).round() as i32;
            let _ = window.set_position(PhysicalPosition::new(new_x, p.y));
        }
    });
}

/// Position presets and arrange-mode drags all land here. `anchor` is one of
/// "default" (top-center of the current monitor), "left" or "right" (edges of
/// the current monitor, in physical px — monitor-aware so a notch parked on a
/// secondary display snaps to THAT display's edges); x/y are GLOBAL PHYSICAL px
/// — logical CSS px * scale-factor — computed by the page so no DPI conversion
/// happens in the backend.
#[tauri::command]
fn move_island_window(app: AppHandle, anchor: Option<String>, x: Option<f64>, y: Option<f64>) {
    let Some(window) = notch_window(&app) else { return };
    locked(|| {
        if let Some(mode) = anchor {
            let m = containing_monitor(&window)
                .or_else(|| window.current_monitor().ok().flatten());
            if let Some(m) = m {
                let sf = m.scale_factor();
                let p = m.position();
                let s = m.size();
                let w = window.outer_size().map(|s| s.width).unwrap_or(334);
                let pos = match mode.as_str() {
                    "left" => PhysicalPosition::new(p.x + (24.0 * sf).round() as i32, p.y),
                    "right" => {
                        PhysicalPosition::new(p.x + s.width as i32 - w as i32 - (24.0 * sf).round() as i32, p.y)
                    }
                    _ => top_center_for(&m, w),
                };
                let _ = window.set_position(pos);
                save_saved_pos(&SavedPos { mode: mode.clone(), x: pos.x, y: pos.y });
            }
        } else if let (Some(x), Some(y)) = (x, y) {
            // Custom drags: global physical px, clamped to stay reachable.
            let _ = window.set_position(PhysicalPosition::new(x.round() as i32, y.round() as i32));
        }
    });
}

/// Snap-zone height in logical px: a notch dropped with its top edge within
/// this distance of a monitor's top edge magnetizes flush to it (NotchDrop
/// style). Farther drops float where they were released.
const SNAP_ZONE_LOGICAL: f64 = 40.0;

/// Finalize an arrange drag (pressed Done). If the notch was dropped near the
/// top edge of the monitor it's on, snap it flush to that edge (top-center,
/// the classic docked notch). Otherwise leave it floating where it was
/// dropped. Returns the final PHYSICAL position + mode so the page can persist
/// it; the backend's saved position is also updated here.
#[derive(Serialize)]
struct Docked {
    x: i32,
    y: i32,
    mode: String,
}

#[tauri::command]
fn dock_island_to_monitor(app: AppHandle) -> Docked {
    let Some(window) = notch_window(&app) else { return Docked { x: 0, y: 0, mode: "custom".into() } };
    locked(|| {
        let win_w = window.outer_size().map(|s| s.width).unwrap_or(334);
        let win_h = window.outer_size().map(|s| s.height).unwrap_or(40);
        let cur = window.outer_position().unwrap_or(PhysicalPosition::new(0, 0));
        let mon = containing_monitor(&window)
            .or_else(|| window.current_monitor().ok().flatten());

        let (pos, mode) = if let Some(m) = mon {
            let p = m.position();
            let s = m.size();
            let sf = m.scale_factor();
            let zone = SNAP_ZONE_LOGICAL * sf;
            let top_gap = (cur.y - p.y) as f64;
            // Within the zone (or already above the edge) -> magnet up.
            if top_gap <= zone {
                let pos = top_center_for(&m, win_w);
                (pos, "default")
            } else {
                // Floating drop: keep the release point, but clamp so the
                // whole window stays inside the monitor.
                let max_x = p.x + s.width as i32 - win_w as i32;
                let max_y = p.y + s.height as i32 - win_h as i32;
                let x = cur.x.clamp(p.x, max_x.max(p.x));
                let y = cur.y.clamp(p.y, max_y.max(p.y));
                (PhysicalPosition::new(x, y), "custom")
            }
        } else {
            // No monitor contains the center (off-screen): leave it put.
            (cur, "custom")
        };

        let _ = window.set_position(pos);
        // Record the COMPUTED position (set_position is async; reading it back
        // could race and persist a stale/old spot).
        save_saved_pos(&SavedPos { mode: mode.into(), x: pos.x, y: pos.y });
        Docked { x: pos.x, y: pos.y, mode: mode.into() }
    })
}

/// Native backdrop blur behind the transparent window ("solid" clears it).
#[tauri::command]
fn set_island_effect(app: AppHandle, effect: String) {
    let Some(window) = notch_window(&app) else { return };
    use tauri::window::{Color, Effect, EffectsBuilder};
    let build = |fx: Effect| {
        EffectsBuilder::new()
            .effect(fx)
            .radius(18.0)
            .color(Color(0, 0, 0, 0))
            .build()
    };
    let cfg = match effect.as_str() {
        "frosted" => Some(build(Effect::Acrylic)),
        "glass" => Some(build(Effect::Mica)),
        _ => None,
    };
    let _ = window.set_effects(cfg);
}

#[tauri::command]
fn system_stats() -> stats::SystemStats {
    stats::poll()
}

#[tauri::command]
fn media_get_state() -> Option<media::MediaState> {
    media::get()
}

#[tauri::command]
fn media_play_pause() { media::play_pause() }
#[tauri::command]
fn media_next() { media::next() }
#[tauri::command]
fn media_previous() { media::previous() }

#[derive(Serialize)]
struct CallState { on_call: bool }

#[tauri::command]
fn call_get_state() -> CallState {
    CallState { on_call: false }
}

#[derive(Serialize)]
struct MicState { on: bool }

#[tauri::command]
fn mic_get_state() -> MicState {
    MicState { on: false }
}

/// OS location -> {lat, lon}; Err(text) is surfaced by the page as a toast.
#[derive(Serialize)]
struct Geo { lat: f64, lon: f64 }

#[tauri::command]
fn weather_geo() -> Result<Geo, String> {
    weather::os_geo().map(|(lat, lon)| Geo { lat, lon })
}

#[tauri::command]
fn launch_app(path: String) -> Result<(), String> {
    launcher::launch(&path)
}

#[tauri::command]
fn hide_island(app: AppHandle) {
    if let Some(w) = notch_window(&app) {
        let _ = w.hide();
    }
}

#[tauri::command]
fn restart_app(app: AppHandle) {
    app.restart();
}

#[tauri::command]
fn quit_app(app: AppHandle) {
    app.exit(0);
}

/// Current OS theme + Windows accent. Shape matches the page's setWinTheme().
#[derive(Serialize, Clone)]
struct WinTheme {
    theme: String,
    accent: [u8; 3],
}

fn current_win_theme() -> WinTheme {
    let cfg = Config { theme: "system".into(), ..Default::default() };
    WinTheme {
        theme: if theme::is_light(&cfg) { "light".into() } else { "dark".into() },
        accent: theme::os_accent().unwrap_or([79, 195, 255]),
    }
}

fn show_from_tray(handle: &AppHandle) {
    if let Some(w) = handle.get_webview_window("main") {
        let _ = w.show();
        let _ = w.set_focus();
    }
}

/// Pushed on boot and whenever the OS theme changes (the page listens on
/// `winotch://theme` and also uses this call's return value directly).
#[tauri::command]
fn get_system_theme(app: AppHandle) -> WinTheme {
    let t = current_win_theme();
    let _ = app.emit("winotch://theme", &t);
    t
}

/// Apply a persisted position at boot. "custom" restores the exact pixel
/// spot; "left"/"right"/"default" re-derive from the monitor containing the
/// saved spot (so a monitor that moved/arranged still gets the right edge or
/// top-center).
fn apply_saved_pos(window: &tauri::WebviewWindow, saved: &SavedPos, win_w: u32) {
    let pos = match saved.mode.as_str() {
        "left" | "right" | "default" => {
            let m = monitor_containing_at(window, saved.x as f64, saved.y as f64);
            match m {
                Some(m) => match saved.mode.as_str() {
                    "left" => {
                        let p = m.position();
                        let sf = m.scale_factor();
                        PhysicalPosition::new(p.x + (24.0 * sf).round() as i32, p.y)
                    }
                    "right" => {
                        let p = m.position();
                        let s = m.size();
                        let sf = m.scale_factor();
                        PhysicalPosition::new(
                            p.x + s.width as i32 - win_w as i32 - (24.0 * sf).round() as i32,
                            p.y,
                        )
                    }
                    _ => top_center_for(&m, win_w),
                },
                // Monitor no longer present: default to primary top-center.
                None => window
                    .primary_monitor()
                    .ok()
                    .flatten()
                    .map(|m| top_center_for(&m, win_w))
                    .unwrap_or(PhysicalPosition::new(0, 0)),
            }
        }
        _ => PhysicalPosition::new(saved.x, saved.y),
    };
    let _ = window.set_position(pos);
}

/// Expose the persisted position so the page can sync its state.pos with the
/// backend (single source of truth across restarts).
#[tauri::command]
fn get_saved_position() -> Option<SavedPos> {
    load_saved_pos()
}

/// Open (or focus) the standalone Settings window. Settings live in their own
/// normal window — the notch pill never has to grow to fit the deck. The page
/// loads the same index.html with `?view=settings` and renders the deck
/// full-page instead of the island.
#[tauri::command]
fn open_settings(app: AppHandle) {
    if let Some(w) = app.get_webview_window("settings") {
        let _ = w.show();
        let _ = w.set_focus();
        return;
    }
    let win = match WebviewWindowBuilder::new(
        &app,
        "settings",
        WebviewUrl::App("index.html?view=settings".into()),
    )
    .title("Winotch Settings")
    .inner_size(500.0, 740.0)
    .min_inner_size(440.0, 560.0)
    .resizable(true)
    .build()
    {
        Ok(w) => w,
        Err(e) => {
            eprintln!("open_settings: {e}");
            return;
        }
    };
    // Park it on the monitor the notch currently occupies, centered and a
    // third of the way down — it never lands on top of the pill itself.
    let m = app
        .get_webview_window("main")
        .and_then(|m| m.current_monitor().ok().flatten());
    if let Some(m) = m {
        let sf = m.scale_factor();
        let p = m.position();
        let s = m.size();
        let (w, h) = (500.0 * sf, 740.0 * sf);
        let x = p.x as f64 + ((s.width as f64 - w) / 2.0).max(0.0);
        let y = p.y as f64 + ((s.height as f64 - h) / 3.0).max(0.0);
        let _ = win.set_position(PhysicalPosition::new(x.round() as i32, y.round() as i32));
    }
}

pub fn run() {
    let cfg = Config::load();
    media::spawn();

    tauri::Builder::default()
        .manage(Mutex::new(cfg))
        .setup(move |app| {
            // Priority status alerts (battery/Wi-Fi/mic-cam) and the in-memory
            // clipboard history — both push events the page renders as
            // compact toasts / the clipboard widget. Ported from the
            // reference C# Winotch.
            status::spawn(app.handle().clone());
            clipboard::spawn(app.handle().clone());
            // The notch window: fully transparent, no frame, no shadow, no
            // taskbar entry, always on top. Sized to the collapsed pill; the
            // page grows it via fit_island_to_content as panels open.
            let win = WebviewWindowBuilder::new(app, "main", WebviewUrl::App("index.html".into()))
                .title("Winotch")
                .decorations(false)
                .shadow(false)
                .resizable(false)
                .always_on_top(true)
                .skip_taskbar(true)
                .focused(false)
                .visible(false) // position FIRST, then show — no flash at (0,0)
                .inner_size(330.0, 36.0)
                .transparent(true)
                .build()?;

            // Re-apply the persisted position BEFORE the page loads, so the
            // notch never flashes at the primary monitor's top-center.
            let win_w = win.outer_size().map(|s| s.width).unwrap_or(334);
            if let Some(saved) = load_saved_pos() {
                apply_saved_pos(&win, &saved, win_w);
            } else if let Ok(Some(m)) = win.primary_monitor() {
                let _ = win.set_position(top_center_for(&m, win_w));
            }
            let _ = win.show();

            // Tray: Show / Start with Windows / Quit. Built on the main thread
            // (tray-icon requirement). The TrayIcon handle must stay alive for
            // the icon to remain — it leaks here deliberately (one per process).
            let startup = autostart::enabled();
            std::mem::forget(tray::init(startup));

            let _ = get_system_theme(app.handle().clone());

            // Watcher: drains tray menu commands and pushes live OS theme.
            // (Menu events from tray-icon arrive on a global channel; the menu
            // itself was built on the main thread above.)
            let handle = app.handle().clone();
            std::thread::spawn(move || {
                let mut last_theme = String::new();
                loop {
                    for cmd in tray::poll() {
                        match cmd {
                            tray::Cmd::Show => show_from_tray(&handle),
                            tray::Cmd::Quit => handle.exit(0),
                            tray::Cmd::ToggleAutostart => {
                                let next = !autostart::enabled();
                                if autostart::set(next).is_ok() {
                                    if let Ok(mut c) = handle.state::<Mutex<Config>>().lock() {
                                        c.autostart = next;
                                        c.save();
                                    }
                                }
                            }
                        }
                    }
                    let t = current_win_theme();
                    if t.theme != last_theme {
                        last_theme = t.theme.clone();
                        let _ = handle.emit("winotch://theme", &t);
                    }
                    std::thread::sleep(std::time::Duration::from_millis(500));
                }
            });

            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            get_monitors,
            move_notch_to_monitor,
            open_settings,
            fit_island_to_content,
            move_island_window,
            dock_island_to_monitor,
            get_saved_position,
            set_island_effect,
            system_stats,
            media_get_state,
            media_play_pause,
            media_next,
            media_previous,
            call_get_state,
            mic_get_state,
            weather_geo,
            launch_app,
            hide_island,
            restart_app,
            quit_app,
            get_system_theme,
            clipboard::get_clipboard_history,
            clipboard::clipboard_copy,
            clipboard::clipboard_delete,
            clipboard::clipboard_clear,
        ])
        .run(tauri::generate_context!())
        .expect("error while running Winotch");
}
