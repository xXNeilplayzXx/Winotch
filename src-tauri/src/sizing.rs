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