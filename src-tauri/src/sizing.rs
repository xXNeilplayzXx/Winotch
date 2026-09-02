// Backend-geometry commands for the settings panel (Apply button).
//
// The frontend drives most window geometry from JS (`showIsland` /
// `animateIsland` in island.ts, which use the per-window Tauri monitor API).
// These commands are the authoritative OS-level entry points Apply calls so
// the notch window always ends up at exactly the requested logical size and
// hugging the top-center of the requested display, even if the webview's own
// geometry math ever disagrees.

/// Clamp the island's logical (CSS-pixel) size to the settings slider range:
/// width 150–800, height 30–200.
fn clamp_size(width: f64, height: f64) -> (f64, f64) {
    (width.clamp(150.0, 800.0), height.clamp(30.0, 200.0))
}

/// Physically resize the notch window to the given logical dimensions.
#[tauri::command]
pub fn update_notch_size(window: tauri::Window, width: f64, height: f64) -> Result<(), String> {
    let (w, h) = clamp_size(width, height);
    window
        .set_size(tauri::Size::Logical(tauri::LogicalSize::new(w, h)))
        .map_err(|e| e.to_string())
}

/// Lock the notch window to the absolute top-center of the PRIMARY display:
/// horizontally centered on the monitor and flush against its absolute top
/// edge (y = monitor top, i.e. 0 for a primary display at the origin). The
/// frontend can call this any time to re-assert placement; the boot path in
/// lib.rs also calls it so the window never paints off-position.
#[tauri::command]
pub fn lock_to_primary_top_center(window: tauri::WebviewWindow) -> Result<(), String> {
    let monitors = window.available_monitors().map_err(|e| e.to_string())?;
    let mon = window
        .primary_monitor()
        .ok()
        .flatten()
        .or_else(|| monitors.first().cloned());
    let Some(mon) = mon else {
        return Err("no monitor available".to_string());
    };
    // Top-center of the display: x = monitor center - half the window width,
    // y = flush against the monitor's top edge.
    let pos = mon.position(); // PhysicalPosition<i32>
    let size = mon.size(); // PhysicalSize<u32>
    let cur = window.outer_size().map_err(|e| e.to_string())?;
    let x = pos.x + (size.width as i32 / 2) - (cur.width as i32 / 2);
    window
        .set_position(tauri::PhysicalPosition::new(x, pos.y))
        .map_err(|e| e.to_string())
}

/// Resize the notch window to the given logical (CSS-pixel) size and keep it
/// "attached" to wherever the notch currently sits on the desktop.
///
/// Called by the designv2 frontend whenever the island's rendered size changes
/// (collapse or expand). When `recenter` is true (position preset = default)
/// the window snaps back to the absolute top-center of the primary display;
/// otherwise the window's horizontal CENTER and TOP are preserved so the
/// expanded settings panel grows in place around the notch's live coordinates
/// (e.g. after the user dragged it with arrange mode) instead of teleporting
/// to a static screen edge.
#[tauri::command]
pub fn fit_island_to_content(
    window: tauri::Window,
    width: f64,
    height: f64,
    recenter: Option<bool>,
) -> Result<(), String> {
    // Guard against degenerate/zero sizes without clamping meaningfully.
    let w = width.max(120.0);
    let h = height.max(24.0);

    // Remember where the notch sits BEFORE resizing, so the panel can grow
    // around it. `center_x` is the island's on-screen horizontal center and
    // `top_y` its top edge — both in physical pixels.
    let cur = window.outer_size().map_err(|e| e.to_string())?;
    let cur_pos = window.outer_position().map_err(|e| e.to_string())?;
    let center_x = cur_pos.x + (cur.width as i32 / 2);
    let top_y = cur_pos.y;

    window
        .set_size(tauri::Size::Logical(tauri::LogicalSize::new(w, h)))
        .map_err(|e| e.to_string())?;

    let recenter = recenter.unwrap_or(false);
    let monitors = window.available_monitors().map_err(|e| e.to_string())?;
    let mon = window
        .primary_monitor()
        .ok()
        .flatten()
        .or_else(|| monitors.first().cloned());
    if let Some(mon) = mon {
        let pos = mon.position();
        let size = mon.size();
        let new = window.outer_size().map_err(|e| e.to_string())?;
        let (x, y) = if recenter {
            // Snap flush to top-center (position preset = default).
            (
                pos.x + (size.width as i32 / 2) - (new.width as i32 / 2),
                pos.y,
            )
        } else {
            // Preserve the notch's live center + top, clamped so the (now
            // larger) window never leaves the desktop.
            let x = (center_x - (new.width as i32 / 2))
                .clamp(pos.x, pos.x + size.width as i32 - new.width as i32);
            let y = top_y.clamp(pos.y, pos.y + size.height as i32 - new.height as i32);
            (x, y)
        };
        window
            .set_position(tauri::PhysicalPosition::new(x, y))
            .map_err(|e| e.to_string())?;
    }
    Ok(())
}

/// Move the notch window so it hugs the absolute top-center of the named
/// display (offset 0, flush with the monitor's first pixel row). `monitor_name`
/// is `"primary"`, a Windows device name like `\\.\DISPLAY1`, or a friendly
/// driver name.
#[tauri::command]
pub fn move_notch_to_monitor(window: tauri::Window, monitor_name: String) -> Result<(), String> {
    let monitors = window.available_monitors().map_err(|e| e.to_string())?;
    let primary = window.primary_monitor().ok().flatten();
    let target = if monitor_name.eq_ignore_ascii_case("primary") {
        primary.or_else(|| monitors.first().cloned())
    } else {
        let needle = monitor_name.to_lowercase();
        monitors
            .iter()
            .find(|m| {
                m.name()
                    .map(|n| n.eq_ignore_ascii_case(&monitor_name))
                    .unwrap_or(false)
            })
            .or_else(|| {
                monitors.iter().find(|m| {
                    m.name()
                        .map(|n| n.to_lowercase().contains(&needle))
                        .unwrap_or(false)
                })
            })
            .cloned()
    };

    let Some(target) = target else {
        return Err(format!("monitor not found: {monitor_name}"));
    };

    // Center the window's current width on the display's horizontal center and
    // sit it flush against the display's top edge.
    let pos = target.position(); // PhysicalPosition<i32>
    let size = target.size(); // PhysicalSize<u32>
    let cur = window.outer_size().map_err(|e| e.to_string())?;
    let x = pos.x + (size.width as i32 / 2) - (cur.width as i32 / 2);
    window
        .set_position(tauri::PhysicalPosition::new(x, pos.y))
        .map_err(|e| e.to_string())
}
/// Move the notch window to an exact logical (CSS-pixel) point on the primary
/// display's coordinate space, or snap it to a preset anchor.
///
/// Called by the designv2 arrange mode: dragging the pill sends `{ x, y }` in
/// the page's CSS pixels (which are logical points relative to the primary
/// monitor's top-left), and the position presets send `{ anchor: "left" }` /
/// `{ anchor: "right" }`. Keeping all window geometry in Rust means one
/// authoritative DPI-correct conversion path, no JS↔Rust position drift.
#[tauri::command]
pub fn move_island_window(
    window: tauri::Window,
    x: Option<f64>,
    y: Option<f64>,
    anchor: Option<String>,
) -> Result<(), String> {
    let monitors = window.available_monitors().map_err(|e| e.to_string())?;
    let mon = window
        .primary_monitor()
        .ok()
        .flatten()
        .or_else(|| monitors.first().cloned())
        .ok_or_else(|| "no monitor available".to_string())?;
    let mon_pos = mon.position(); // PhysicalPosition<i32>
    let mon_size = mon.size(); // PhysicalSize<u32>
    let scale = mon.scale_factor();
    let cur = window.outer_size().map_err(|e| e.to_string())?; // physical px

    let (phys_x, phys_y) = if let (Some(x), Some(y)) = (x, y) {
        // Exact point: CSS px relative to the primary monitor's top-left,
        // converted to physical pixels, clamped so the window stays on the
        // desktop with a small margin.
        let w = cur.width as f64;
        let h = cur.height as f64;
        let max_x = (mon_size.width as f64 - w - 8.0).max(8.0);
        let max_y = (mon_size.height as f64 - h - 8.0).max(8.0);
        let lx = x.clamp(8.0, max_x);
        let ly = y.clamp(8.0, max_y);
        (
            mon_pos.x as f64 + lx * scale,
            mon_pos.y as f64 + ly * scale,
        )
    } else {
        // Preset anchors: top-left / top-right corners with an 8px inset.
        let w = cur.width as f64;
        match anchor.as_deref() {
            Some("left") => (
                mon_pos.x as f64 + 8.0,
                mon_pos.y as f64 + 8.0,
            ),
            Some("right") => (
                mon_pos.x as f64 + mon_size.width as f64 - w - 8.0,
                mon_pos.y as f64 + 8.0,
            ),
            Some(other) => return Err(format!("unknown anchor: {other}")),
            None => return Err("provide x/y or an anchor".into()),
        }
    };

    window
        .set_position(tauri::PhysicalPosition::new(
            phys_x.round() as i32,
            phys_y.round() as i32,
        ))
        .map_err(|e| e.to_string())
}
