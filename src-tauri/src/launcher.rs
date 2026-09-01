// App-launcher support for the "App launcher" widget.
//
// The widget stores launch strings — an exe path, a PATH-resolvable name like
// `chrome.exe`, or a path with `%VAR%` env placeholders — and this command
// launches them. CreateProcess resolves names through PATH; if the direct
// launch fails we fall back to `cmd /C start`, which also handles files/URIs
// with the default handler.

/// Expand `%VAR%` placeholders (case-insensitive; Windows env vars are
/// case-insensitive but `std::env::var` is not).
fn expand_env(input: &str) -> String {
    let mut out = input.to_string();
    loop {
        let Some(start) = out.find('%') else {
            break;
        };
        let Some(rel_end) = out[start + 1..].find('%') else {
            break;
        };
        let end = start + 1 + rel_end;
        let var = &out[start + 1..end];
        let value = std::env::var(var)
            .or_else(|_| std::env::var(var.to_uppercase()))
            .unwrap_or_default();
        if value.is_empty() {
            break; // unknown variable: leave as-is rather than loop forever
        }
        out.replace_range(start..=end, &value);
    }
    out
}

/// Launch an app by path or name. No-op guarantee: returns an error string.
#[tauri::command]
pub fn launch_app(path: String) -> Result<(), String> {
    let expanded = expand_env(&path);
    let trimmed = expanded.trim();
    if trimmed.is_empty() {
        return Err("empty launch target".into());
    }
    match std::process::Command::new(trimmed).spawn() {
        Ok(_) => Ok(()),
        Err(_) => {
            // Fallback: let the shell's `start` dispatch it (default handler,
            // also handles documents and URIs).
            std::process::Command::new("cmd")
                .args(["/C", "start", "", trimmed])
                .spawn()
                .map(|_| ())
                .map_err(|e| e.to_string())
        }
    }
}