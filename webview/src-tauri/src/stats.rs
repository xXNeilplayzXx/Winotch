//! System-stats snapshot — ported from the Tauri backend (src-tauri/src/stats.rs),
//! minus the tauri::command wrapper. Same degradation guarantees.

use serde::Serialize;
use std::sync::{Mutex, OnceLock};
use std::time::Instant;

#[derive(Clone, Serialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct BatteryStats {
    pub pct: u8,
    pub charging: bool,
}

#[derive(Clone, Serialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct NetworkStats {
    pub down_bytes_per_sec: f64,
    pub up_bytes_per_sec: f64,
}

#[derive(Clone, Serialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct SystemStats {
    pub cpu: f32,
    pub mem_used_gb: f32,
    pub mem_total_gb: f32,
    pub network: Option<NetworkStats>,
    pub temp_c: Option<f32>,
    pub battery: Option<BatteryStats>,
}

struct SysState {
    sys: sysinfo::System,
    networks: sysinfo::Networks,
    components: sysinfo::Components,
    nets: Vec<(String, u64, u64)>,
    at: Instant,
}

static STATE: OnceLock<Mutex<SysState>> = OnceLock::new();

fn state() -> &'static Mutex<SysState> {
    STATE.get_or_init(|| Mutex::new(SysState {
        sys: sysinfo::System::new_all(),
        networks: sysinfo::Networks::new_with_refreshed_list(),
        components: sysinfo::Components::new_with_refreshed_list(),
        nets: Vec::new(),
        at: Instant::now(),
    }))
}

fn is_virtual_interface(name: &str) -> bool {
    let n = name.to_ascii_lowercase();
    n.starts_with("lo") || n.contains("loopback") || n.contains("teredo")
        || n.contains("isatap") || n.contains("veth") || n.contains("docker")
        || n.contains("bridge")
}

fn network_throughput(state: &mut SysState) -> Option<NetworkStats> {
    let now = Instant::now();
    let dt = now.duration_since(state.at).as_secs_f64().max(0.001);
    let mut d_down = 0.0f64;
    let mut d_up = 0.0f64;
    let mut cur: Vec<(String, u64, u64)> = Vec::new();
    for (name, data) in state.networks.iter() {
        if is_virtual_interface(name) { continue; }
        let (r, t) = (data.received(), data.transmitted());
        cur.push((name.to_string(), r, t));
        if let Some((_, pr, pt)) = state.nets.iter().find(|(n, _, _)| n == name) {
            d_down += r.saturating_sub(*pr) as f64;
            d_up += t.saturating_sub(*pt) as f64;
        }
    }
    let had_previous = !state.nets.is_empty();
    state.nets = cur;
    state.at = now;
    had_previous.then_some(NetworkStats {
        down_bytes_per_sec: d_down / dt,
        up_bytes_per_sec: d_up / dt,
    })
}

fn cpu_temperature(components: &sysinfo::Components) -> Option<f32> {
    if components.is_empty() { return None; }
    let preferred = components.iter().find(|c| {
        let l = c.label().to_ascii_lowercase();
        l.contains("cpu") || l.contains("package") || l.contains("tctl")
            || l.contains("tdie") || l.contains("soc") || l.contains("die")
    });
    preferred.and_then(|c| c.temperature())
        .or_else(|| components.iter().filter_map(|c| c.temperature()).reduce(f32::max))
        .filter(|t| t.is_finite() && *t > 0.0 && *t < 200.0)
}

fn battery_status() -> Option<BatteryStats> {
    #[cfg(target_os = "windows")]
    {
        use windows::Win32::System::Power::{GetSystemPowerStatus, SYSTEM_POWER_STATUS};
        let mut status = SYSTEM_POWER_STATUS::default();
        if !unsafe { GetSystemPowerStatus(&mut status) }.is_ok() {
            return None;
        }
        if status.BatteryLifePercent == 255 { return None; }
        Some(BatteryStats {
            pct: status.BatteryLifePercent,
            charging: status.ACLineStatus == 1,
        })
    }
    #[cfg(target_os = "macos")]
    {
        let Ok(out) = std::process::Command::new("pmset").args(["-g", "batt"]).output() else {
            return None;
        };
        let text = String::from_utf8_lossy(&out.stdout);
        let line = text.lines().find(|l| l.contains('%'))?;
        let pct: u8 = line.split('%').next()?
            .rsplit(|c: char| !c.is_ascii_digit())
            .find(|s| !s.is_empty())?.parse().ok()?;
        Some(BatteryStats { pct: pct.min(100), charging: text.contains("charging") || text.contains("charged") })
    }
    #[cfg(not(any(target_os = "windows", target_os = "macos")))]
    { None }
}

/// Current machine snapshot. Never fails: unusable readings become zeros/None.
pub fn poll() -> SystemStats {
    let mut guard = state().lock().unwrap_or_else(|p| p.into_inner());
    {
        let sys = &mut guard.sys;
        sys.refresh_cpu_usage();
        sys.refresh_memory();
    }
    guard.networks.refresh(true);
    guard.components.refresh(true);
    let gib = 1024.0 * 1024.0 * 1024.0;
    SystemStats {
        cpu: guard.sys.global_cpu_usage(),
        mem_used_gb: guard.sys.used_memory() as f32 / gib,
        mem_total_gb: guard.sys.total_memory() as f32 / gib,
        network: network_throughput(&mut guard),
        temp_c: cpu_temperature(&guard.components),
        battery: battery_status(),
    }
}
