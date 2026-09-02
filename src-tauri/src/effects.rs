// Native window-effect control for the frosted-glass setting.
//
// A transparent Tauri window lets the desktop show through, but CSS
// `backdrop-filter` inside the webview can only blur content it already
// renders — it cannot sample the real desktop behind the window. To get the
// etched "frosted glass" look from designv2, we ask the OS (DWM) to blur the
// backdrop via Tauri's window effects API (SetWindowCompositionAttribute on
// Windows). Windows-only; off Windows this is a no-op so macOS dev builds and
// the preview page are unaffected.

use tauri::{utils::config::WindowEffectsConfig, Manager};
use tauri::window::EffectsBuilder;

/// Surface preset: which native effect to apply behind the island.
///
/// - `glass` → Blur (the desktop blurs through the translucent island glass).
/// - `frosted`→ Blur too (the island's own CSS tint makes it milkier/heavier).
/// - `solid` / anything else → no native effect, cleared (the island is opaque).
///
/// The designv2 frontend sends the active Surface preset so the OS window blur
/// always matches what the island shows. Native blur is what lets a transparent
/// window actually frost the real desktop (CSS backdrop-filter can't reach it).
#[tauri::command]
pub fn set_island_effect(app: tauri::AppHandle, effect: String) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        let effects: Option<WindowEffectsConfig> = match effect.as_str() {
            "glass" | "frosted" => Some(EffectsBuilder::new().effect(tauri::window::Effect::Blur).build()),
            _ => None,
        };
        for (_, win) in app.webview_windows() {
            if let Err(e) = win.set_effects(effects.clone()) {
                eprintln!("set_island_effect({effect}): {e}");
            }
        }
        Ok(())
    }
    #[cfg(not(target_os = "windows"))]
    {
        let _ = (&app, effect);
        Ok(())
    }
}
