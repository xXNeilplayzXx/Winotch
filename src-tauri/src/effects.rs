// Native window-effect control for the frosted-glass setting.
//
// HISTORY: glass/frosted used to apply the DWM Blur accent (blur-behind).
// That effect processes the ENTIRE rectangular window — it cannot follow the
// island's rounded corners — so it painted a dark frosted RECTANGLE around
// the pill: the "black box around the notch". The native blur is therefore
// never applied anymore; the glass/frosted look comes from the island's own
// CSS tint over the fully transparent window, so only the rounded pill is
// ever visible. This command is kept as a no-op-clear so the frontend's
// syncNativeEffect stays wired and any effect a future Windows compositor
// quirk leaves behind gets explicitly cleared.

use tauri::{utils::config::WindowEffectsConfig, Manager};

/// Surface preset: which native effect to apply behind the island.
///
/// Always `None`: see the module comment — every native window effect
/// (Blur/Acrylic/…) paints the whole rectangular window and reads as a black
/// box around the rounded pill. Glass/frosted are pure CSS tints now. The
/// command still clears whatever effect might be set, so switching surfaces
/// can never leave a stale blur rectangle behind.
#[tauri::command]
pub fn set_island_effect(app: tauri::AppHandle, effect: String) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        for (_, win) in app.webview_windows() {
            if let Err(e) = win.set_effects(Option::<WindowEffectsConfig>::None) {
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
