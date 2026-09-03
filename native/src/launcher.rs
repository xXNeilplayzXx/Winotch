//! App launcher — ported from src-tauri/src/launcher.rs (expand_env + spawn).

/// Expand `%VAR%` placeholders (case-insensitive).
pub fn expand_env(input: &str) -> String {
    let mut out = input.to_string();
    loop {
        let Some(start) = out.find('%') else { break };
        let Some(rel_end) = out[start + 1..].find('%') else { break };
        let end = start + 1 + rel_end;
        let var = &out[start + 1..end];
        let value = std::env::var(var)
            .or_else(|_| std::env::var(var.to_uppercase()))
            .unwrap_or_default();
        if value.is_empty() { break }
        out.replace_range(start..=end, &value);
    }
    out
}

pub fn launch(path: &str) -> Result<(), String> {
    let expanded = expand_env(path);
    let trimmed = expanded.trim();
    if trimmed.is_empty() { return Err("empty launch target".into()); }
    match std::process::Command::new(trimmed).spawn() {
        Ok(_) => Ok(()),
        Err(_) => {
            #[cfg(target_os = "windows")]
            {
                std::process::Command::new("cmd").args(["/C", "start", "", trimmed])
                    .spawn().map(|_| ()).map_err(|e| e.to_string())
            }
            #[cfg(not(target_os = "windows"))]
            {
                std::process::Command::new("open").arg(trimmed)
                    .spawn().map(|_| ()).map_err(|e| e.to_string())
            }
        }
    }
}
