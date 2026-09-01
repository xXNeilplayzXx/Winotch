// Monitor enumeration + persistence for the island's display picker.
//
// On Windows, friendly names come from a small Rust command (Win32 display
// enumeration); scale factors come from Tauri's own monitor API, matched by
// rect. Off Windows / outside Tauri the command is empty and we fall back to
// Tauri's `availableMonitors()`, then to a synthetic single display, so the
// app never shows a blank monitor list.

import { invoke } from "@tauri-apps/api/core";
import { availableMonitors, primaryMonitor } from "@tauri-apps/api/window";

export interface MonitorInfo {
  /** Windows device name, e.g. `\\.\DISPLAY1`. */
  device: string;
  /** Friendly display-driver name, e.g. "DELL U2723QE". */
  name: string;
  isPrimary: boolean;
  /** Physical-pixel origin of the display's full rect in virtual space. */
  x: number;
  y: number;
  width: number;
  height: number;
  scale: number;
}

/** Which display the island lives on: primary, all, or one specific device. */
export type MonitorPref = "primary" | "all" | `mon:${string}`;

export const MONITOR_KEY = "winotch.monitor";

export function readMonitorPref(): MonitorPref {
  try {
    const v = localStorage.getItem(MONITOR_KEY);
    if (v === "all" || (typeof v === "string" && v.startsWith("mon:"))) {
      return v as MonitorPref;
    }
  } catch {
    /* storage unavailable */
  }
  return "primary";
}

export function saveMonitorPref(pref: MonitorPref) {
  try {
    localStorage.setItem(MONITOR_KEY, pref);
  } catch {
    /* storage unavailable; keep session-only */
  }
}

/** Rust-side monitor row (device/friendly names; empty when unavailable). */
interface RustMonitor {
  device: string;
  name: string;
  is_primary: boolean;
  x: number;
  y: number;
  width: number;
  height: number;
}

interface ApiMonitor {
  name: string | null;
  position: { x: number; y: number };
  size: { width: number; height: number };
  scaleFactor: number;
}

let cache: MonitorInfo[] | null = null;
let cachePromise: Promise<MonitorInfo[]> | null = null;

export function clearMonitorCache() {
  cache = null;
  cachePromise = null;
}

function sameRect(a: { x: number; y: number; width: number; height: number }, b: ApiMonitor) {
  return (
    a.x === b.position.x &&
    a.y === b.position.y &&
    a.width === b.size.width &&
    a.height === b.size.height
  );
}

/** All active displays, best-effort names, primary first when known. */
export async function getMonitors(): Promise<MonitorInfo[]> {
  if (cache) return cache;
  if (!cachePromise) {
    cachePromise = (async () => {
      let rust: RustMonitor[] = [];
      try {
        rust = (await invoke<RustMonitor[]>("get_monitors")) ?? [];
      } catch {
        /* not inside Tauri (browser preview) */
      }

      let api: ApiMonitor[] = [];
      try {
        api = await availableMonitors();
      } catch {
        /* not inside Tauri */
      }
      if (api.length === 0) {
        api = [];
      }

      let result: MonitorInfo[];
      const fallbackScale = 1;
      if (rust.length > 0) {
        const byRectCache = new Map<string, ApiMonitor>();
        for (const m of rust) {
          const a = byRectCache.get(key(m));
          if (a) continue;
          const hit = api.find((a2) => sameRect(m, a2));
          if (hit) byRectCache.set(key(m), hit);
        }
        result = rust.map((m) => {
          const keyStr = key(m);
          const a = byRectCache.get(keyStr);
          return {
            device: m.device,
            name: m.name || m.device,
            isPrimary: m.is_primary,
            x: m.x,
            y: m.y,
            width: m.width,
            height: m.height,
            scale: a?.scaleFactor ?? fallbackScale,
          };
        });
      } else if (api.length > 0) {
        let primary: { x: number; y: number; width: number; height: number } | null = null;
        try {
          const p = await primaryMonitor();
          if (p) {
            primary = {
              x: p.position.x,
              y: p.position.y,
              width: p.size.width,
              height: p.size.height,
            };
          }
        } catch {
          /* no primary info */
        }
        result = api.map((a, i) => ({
          device: a.name || `display-${i + 1}`,
          name: a.name || `Display ${i + 1}`,
          isPrimary: primary != null && sameRect(primary, a),
          x: a.position.x,
          y: a.position.y,
          width: a.size.width,
          height: a.size.height,
          scale: a.scaleFactor,
        }));
      } else {
        // Everything failed (plain browser): one synthetic display so the app
        // is still usable and the monitor picker step simply never appears.
        result = [
          {
            device: "display-1",
            name: "Main display",
            isPrimary: true,
            x: 0,
            y: 0,
            width: 1920,
            height: 1080,
            scale: 1,
          },
        ];
      }

      // Primary first keeps the default (index 0) predictable in All mode.
      result.sort((a, b) => Number(b.isPrimary) - Number(a.isPrimary));
      cache = result;
      return result;
    })();
  }
  return cachePromise;
}

function key(m: { x: number; y: number; width: number; height: number }) {
  return `${m.x},${m.y},${m.width},${m.height}`;
}

/**
 * The monitor a monitor-pref resolves to (null = let the caller fall back to
 * the window's current monitor, i.e. "primary" semantics).
 */
export async function resolveMonitor(pref: MonitorPref): Promise<MonitorInfo | null> {
  const monitors = await getMonitors();
  if (monitors.length === 0) return null;
  if (pref === "all") return monitors[0] ?? null;
  if (pref.startsWith("mon:")) {
    return monitors.find((m) => m.device === pref.slice(4)) ?? null;
  }
  // "primary" → the primary display if we can identify it.
  return monitors.find((m) => m.isPrimary) ?? monitors[0] ?? null;
}