//! Persisted configuration + the settings draft used by the deck.

use serde::{Deserialize, Serialize};
use std::path::PathBuf;

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(default)]
pub struct Config {
    pub width: u32,
    pub idle_content: String,   // "clock" | "brand" | "stats" | "custom"
    pub custom_text: String,
    pub glass: bool,
    pub frosted: bool,
    pub opacity: u32,           // 10..=100
    pub blur: u32,              // 4..=40 (advisory in native; alpha does the work)
    pub gradient: String,       // "none"|"sunset"|"ocean"|"aurora"|"candy"|"graphite"
    pub theme: String,          // "light"|"dark"|"system"|"color"
    pub accent: [u8; 3],
    pub pos_mode: String,       // "default"|"left"|"right"|"free"
    pub pos_x: f32,
    pub widgets: Vec<String>,   // widget ids in display order
    pub sys_cpu: bool,
    pub sys_mem: bool,
    pub sys_bat: bool,
    pub sys_net: bool,
    pub sys_temp: bool,
    pub weather_city: String,
    pub launchers: Vec<String>,
    pub notes: String,
    /// True until the first successful save — drives the first-run wizard.
    #[serde(skip)]
    pub config_is_fresh: bool,
    /// Mirrors the registry Run-key state (source of truth is the registry).
    #[serde(skip)]
    pub autostart: bool,
    /// true = notch (square top corners), false = floating pill (all corners rounded)
    pub shape_notch: bool,
}

impl Default for Config {
    fn default() -> Self {
        Self {
            width: 330,
            idle_content: "clock".into(),
            custom_text: String::new(),
            glass: true,
            frosted: false,
            opacity: 65,
            blur: 14,
            gradient: "none".into(),
            theme: "system".into(),
            accent: [79, 195, 255],
            pos_mode: "default".into(),
            pos_x: 0.0,
            widgets: vec![
                "clock".into(), "system".into(), "media".into(),
                "weather".into(), "notes".into(), "launcher".into(),
            ],
            sys_cpu: true,
            sys_mem: true,
            sys_bat: true,
            sys_net: false,
            sys_temp: false,
            weather_city: String::new(),
            launchers: Vec::new(),
            notes: String::new(),
            config_is_fresh: true,
            autostart: false,
            shape_notch: true,
        }
    }
}

fn config_path() -> PathBuf {
    let dir = dirs_home().join(".winotch");
    std::fs::create_dir_all(&dir).ok();
    dir.join("config.json")
}

fn dirs_home() -> PathBuf {
    #[cfg(target_os = "windows")]
    { std::env::var("APPDATA").map(PathBuf::from).unwrap_or_else(|_| PathBuf::from(".")) }
    #[cfg(not(target_os = "windows"))]
    { std::env::var("HOME").map(PathBuf::from).unwrap_or_else(|_| PathBuf::from(".")) }
}

impl Config {
    pub fn load() -> Self {
        match std::fs::read_to_string(config_path())
            .ok()
            .and_then(|s| serde_json::from_str::<Config>(&s).ok())
        {
            Some(mut c) => {
                c.config_is_fresh = false;
                c
            }
            None => Config::default(),
        }
    }
    pub fn save(&mut self) {
        self.config_is_fresh = false;
        if let Ok(json) = serde_json::to_string_pretty(self) {
            let _ = std::fs::write(config_path(), json);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Contract: every user-visible setting survives a save/load round trip
    /// (the app persists between runs; losing a field is a data-loss bug).
    #[test]
    fn round_trip_preserves_all_settings() {
        let c = Config {
            width: 420,
            idle_content: "custom".into(),
            custom_text: "hello world".into(),
            glass: true,
            frosted: true,
            opacity: 55,
            blur: 18,
            gradient: "ocean".into(),
            theme: "color".into(),
            accent: [255, 193, 77],
            pos_mode: "free".into(),
            pos_x: 137.5,
            widgets: vec!["media".into(), "clock".into(), "weather".into()],
            sys_cpu: true,
            sys_mem: false,
            sys_bat: true,
            sys_net: false,
            sys_temp: true,
            weather_city: "Delhi".into(),
            launchers: vec!["C:\\apps\\chrome.exe".into()],
            notes: "line one\nline two".into(),
            config_is_fresh: true,
            autostart: true,
            shape_notch: false,
        };
        let json = serde_json::to_string(&c).expect("serialize");
        let back: Config = serde_json::from_str(&json).expect("deserialize");
        assert_eq!(back.width, 420);
        assert_eq!(back.idle_content, "custom");
        assert_eq!(back.custom_text, "hello world");
        assert!(back.glass && back.frosted);
        assert_eq!((back.opacity, back.blur), (55, 18));
        assert_eq!(back.gradient, "ocean");
        assert_eq!(back.theme, "color");
        assert_eq!(back.accent, [255, 193, 77]);
        assert_eq!(back.pos_mode, "free");
        assert_eq!(back.pos_x, 137.5);
        assert_eq!(back.widgets, vec!["media", "clock", "weather"]);
        assert_eq!(
            (back.sys_cpu, back.sys_mem, back.sys_bat, back.sys_net, back.sys_temp),
            (true, false, true, false, true)
        );
        assert_eq!(back.weather_city, "Delhi");
        assert_eq!(back.launchers, vec!["C:\\apps\\chrome.exe"]);
        assert_eq!(back.notes, "line one\nline two");
        assert!(!back.shape_notch);
        // autostart is deliberately NOT persisted (registry is the source of
        // truth) — a stale JSON mirror could contradict the registry on boot.
        assert!(!json.contains("autostart"), "autostart must stay out of the config file");
        assert!(!json.contains("config_is_fresh"), "fresh flag is runtime-only");
    }

    /// Contract: a truncated/corrupt config file falls back to defaults
    /// instead of failing to boot.
    #[test]
    fn corrupt_json_yields_defaults() {
        let c: Result<Config, _> = serde_json::from_str("{ not json ");
        assert!(c.is_err(), "corrupt file must not parse");
        // load() maps parse failure onto Config::default()
        assert_eq!(Config::default().theme, "system");
    }

    /// Contract: unknown/absent fields (older or newer config versions)
    /// still deserialize — serde defaults keep forward compatibility.
    #[test]
    fn partial_json_uses_defaults() {
        let c: Config = serde_json::from_str(r#"{"theme":"light"}"#).expect("partial config parses");
        assert_eq!(c.theme, "light");
        assert!(c.glass, "untouched fields keep their default");
    }

    /// Contract: widget order is user-controlled and preserved verbatim —
    /// the reorder buttons in settings depend on exact ordering surviving.
    #[test]
    fn widget_order_round_trips() {
        let order: Vec<String> = ["weather", "launcher", "notes", "system", "media", "clock"]
            .iter().map(|s| s.to_string()).collect();
        let json = serde_json::to_string(&Config { widgets: order.clone(), ..Default::default() }).unwrap();
        let back: Config = serde_json::from_str(&json).unwrap();
        assert_eq!(back.widgets, order);
    }
}
