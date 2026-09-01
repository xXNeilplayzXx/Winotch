// Display enumeration for the island's monitor picker and "All monitors" mode.
//
// The frontend picks which display the notch lives on (primary, a specific
// display, or ALL displays). For "all", the main window covers the first
// display and this module spawns one extra always-on-top island window per
// remaining display, each labelled `island-1..N` so its webview can anchor
// itself to the right display. Off Windows every entry point is a no-op and
// the frontend falls back to Tauri's own monitor API.

use serde::Serialize;

#[derive(Clone, Serialize)]
pub struct MonitorInfo {
    /// Windows device name, e.g. `\\.\DISPLAY1`.
    pub device: String,
    /// Friendly name reported by the display driver, e.g. "DELL U2723QE".
    pub name: String,
    pub is_primary: bool,
    /// Physical-pixel origin of the display's full rect in virtual screen space.
    pub x: i32,
    pub y: i32,
    pub width: u32,
    pub height: u32,
}

/// All active displays with friendly names (empty off Windows).
#[tauri::command]
pub fn get_monitors() -> Vec<MonitorInfo> {
    #[cfg(target_os = "windows")]
    {
        win::enumerate()
    }
    #[cfg(not(target_os = "windows"))]
    {
        Vec::new()
    }
}

/// Spawn one additional island window per extra display ("All monitors" mode).
/// The main window (label "main") covers the first display; extras are labelled
/// `island-1..N` and each frontend anchors itself via its window label.
#[tauri::command]
pub fn spawn_island_windows(app: tauri::AppHandle) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        win::spawn_island_windows(&app)?;
    }
    #[cfg(not(target_os = "windows"))]
    let _ = &app;
    Ok(())
}

/// Close every extra island window (`island-*`), keeping only the main window.
#[tauri::command]
pub fn close_island_windows(app: tauri::AppHandle) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        win::close_island_windows(&app);
    }
    #[cfg(not(target_os = "windows"))]
    let _ = &app;
    Ok(())
}

/// Custom collapsed island size carried by a config broadcast (logical px).
#[derive(Clone, Copy, Serialize, serde::Deserialize)]
pub struct ConfigSize {
    pub width: f64,
    pub height: f64,
}

/// Payload for keeping every island window in sync (shape, appearance,
/// widget layout, custom size). `widgets` is opaque JSON so the Rust side
/// doesn't need to know the frontend's widget model.
#[derive(Clone, Serialize, serde::Deserialize)]
pub struct ConfigBroadcast {
    pub shape: String,
    pub glass: bool,
    #[serde(default)]
    pub widgets: Option<serde_json::Value>,
    #[serde(default)]
    pub size: Option<ConfigSize>,
    /// Liquid-glass opacity percent (only meaningful in glass mode).
    #[serde(default, rename = "glassOpacity")]
    pub glass_opacity: Option<f64>,
    /// Accent preference: `system` (follow Windows) or an explicit hex.
    #[serde(default)]
    pub accent: Option<String>,
    /// Liquid-glass backdrop blur in px (settings "Glass Blur" slider).
    #[serde(default, rename = "glassBlur")]
    pub glass_blur: Option<f64>,
    /// Background gradient preset id (`none` disables it).
    #[serde(default)]
    pub gradient: Option<String>,
    /// What the collapsed strip shows while idle.
    #[serde(default, rename = "idleContent")]
    pub idle_content: Option<String>,
    /// Custom idle text (only used with `idleContent: "custom"`).
    #[serde(default, rename = "idleText")]
    pub idle_text: Option<String>,
    /// Whether the collapsed island can be dragged around by its webview.
    #[serde(default)]
    pub draggable: Option<bool>,
}

/// Broadcast a config change to ALL island windows, so multi-window mode stays
/// consistent (e.g. switching to Pill mode updates the notch on every display).
#[tauri::command]
pub fn broadcast_config(app: tauri::AppHandle, config: ConfigBroadcast) -> Result<(), String> {
    use tauri::Emitter;
    app.emit("winotch://config", config)
        .map_err(|e| e.to_string())
}

#[cfg(target_os = "windows")]
mod win {
    use super::MonitorInfo;
    use tauri::{Manager, WebviewUrl, WebviewWindowBuilder};
    use windows::core::PCWSTR;
    use windows::Win32::Foundation::{BOOL, LPARAM, RECT};
    use windows::Win32::Graphics::Gdi::{
        EnumDisplayDevicesW, EnumDisplayMonitors, GetMonitorInfoW, DISPLAY_DEVICEW, HDC, HMONITOR,
        MONITORINFOEXW,
    };
    use windows::Win32::UI::WindowsAndMessaging::MONITORINFOF_PRIMARY;

    fn wide_to_string(w: &[u16]) -> String {
        let end = w.iter().position(|&c| c == 0).unwrap_or(w.len());
        String::from_utf16_lossy(&w[..end])
    }

    struct Collector {
        items: Vec<MonitorInfo>,
    }

    unsafe extern "system" fn enum_proc(
        hmonitor: HMONITOR,
        _hdc: HDC,
        _clip: *mut RECT,
        lparam: LPARAM,
    ) -> BOOL {
        let collector = &mut *(lparam.0 as *mut Collector);
        let mut info: MONITORINFOEXW = unsafe { std::mem::zeroed() };
        info.monitorInfo.cbSize = std::mem::size_of::<MONITORINFOEXW>() as u32;
        if unsafe { GetMonitorInfoW(hmonitor, &mut info.monitorInfo) }.as_bool() {
            let r = info.monitorInfo.rcMonitor;
            collector.items.push(MonitorInfo {
                device: wide_to_string(&info.szDevice),
                name: friendly_name(&info.szDevice),
                is_primary: (info.monitorInfo.dwFlags & MONITORINFOF_PRIMARY) != 0,
                x: r.left,
                y: r.top,
                width: (r.right - r.left) as u32,
                height: (r.bottom - r.top) as u32,
            });
        }
        BOOL(1)
    }

    /// Friendly monitor name from the display driver (e.g. "DELL U2723QE").
    /// Falls back to the device name's final token when unavailable.
    fn friendly_name(device: &[u16]) -> String {
        let mut dd: DISPLAY_DEVICEW = unsafe { std::mem::zeroed() };
        dd.cb = std::mem::size_of::<DISPLAY_DEVICEW>() as u32;
        if unsafe { EnumDisplayDevicesW(PCWSTR(device.as_ptr()), 0, &mut dd, 0) }.as_bool() {
            let s = wide_to_string(&dd.DeviceString);
            if !s.is_empty() {
                return s;
            }
        }
        let raw = wide_to_string(device);
        raw.split('#').last().unwrap_or(&raw).to_string()
    }

    pub fn enumerate() -> Vec<MonitorInfo> {
        let mut collector = Collector { items: Vec::new() };
        unsafe {
            let _ = EnumDisplayMonitors(
                None,
                None,
                Some(enum_proc),
                LPARAM(&mut collector as *mut Collector as isize),
            );
        }
        // Stable-sort primary first so index 0 is always the main display in
        // both this list and the frontend's sorted copy (All-monitors mode
        // relies on index 0 = the main window's own display).
        collector.items.sort_by_key(|m| !m.is_primary);
        collector.items
    }

    pub fn spawn_island_windows(app: &tauri::AppHandle) -> Result<(), String> {
        let monitors = enumerate();
        for (i, mon) in monitors.iter().enumerate().skip(1) {
            let label = format!("island-{i}");
            if app.get_webview_window(&label).is_some() {
                continue;
            }
            // Geometry is applied by the frontend (showIsland) after mount; the
            // size here is just a placeholder so the window exists before its
            // webview asks for one.
            WebviewWindowBuilder::new(app, label, WebviewUrl::App("index.html".into()))
                .title("Winotch")
                .inner_size(mon.width as f64, mon.height as f64)
                .resizable(false)
                .decorations(false)
                .transparent(true)
                .shadow(false)
                .always_on_top(true)
                .skip_taskbar(true)
                .visible(false)
                .build()
                .map_err(|e| e.to_string())?;
            // Same opaque-background guard as the main window (see lib.rs): pin
            // the webview background to fully transparent so the OS never
            // paints corners behind the rounded island.
            if let Some(win) = app.get_webview_window(&label) {
                let _ = win.set_background_color(tauri::window::Color(0, 0, 0, 0));
            }
        }
        Ok(())
    }

    pub fn close_island_windows(app: &tauri::AppHandle) {
        let labels: Vec<String> = app
            .webview_windows()
            .keys()
            .filter(|l| l.starts_with("island-"))
            .cloned()
            .collect();
        for label in labels {
            if let Some(w) = app.get_webview_window(&label) {
                let _ = w.close();
            }
        }
    }
}