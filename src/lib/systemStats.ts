// System-stats poller (CPU, RAM, battery).
//
// One background poller runs while any System widget is mounted (the expanded
// panel stays mounted even when collapsed, so a single interval keeps the
// collapsed mini bar fresh). Both the widget card and the mini bar read the
// latest snapshot through `useSystemStats`, which never triggers more than one
// invoke per tick regardless of how many components subscribe.

import { invoke } from "@tauri-apps/api/core";
import { useSyncExternalStore } from "react";

export interface SystemNetworkStats {
  /** Download throughput, bytes/sec. */
  downBytesPerSec: number;
  /** Upload throughput, bytes/sec. */
  upBytesPerSec: number;
}

export interface SystemStats {
  /** CPU usage, 0..100. */
  cpu: number;
  /** Used RAM, in GB. */
  memUsedGb: number;
  /** Total RAM, in GB. */
  memTotalGb: number;
  /** Network throughput; null during the first poll while a baseline forms. */
  network: SystemNetworkStats | null;
  /** CPU temperature, °C; null when no sensor is available. */
  tempC: number | null;
  /** Battery snapshot; null on desktops without a battery. */
  battery: { pct: number; charging: boolean } | null;
}

const POLL_MS = 2000;

let cached: SystemStats | null = null;
let timer: number | null = null;
let busy = false;
const listeners = new Set<() => void>();

function emit() {
  listeners.forEach((l) => l());
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

async function poll() {
  if (busy) return;
  busy = true;
  try {
    const next = await invoke<SystemStats>("system_stats");
    cached = next;
    emit();
  } catch {
    // Keep the last good snapshot; the app is unusable offline anyway.
  } finally {
    busy = false;
  }
}

/** Start the shared poller (idempotent). Called while a System widget exists. */
export function startSystemStatsPolling() {
  if (timer !== null) return;
  void poll();
  timer = window.setInterval(() => void poll(), POLL_MS);
}

/** Stop the shared poller (idempotent). */
export function stopSystemStatsPolling() {
  if (timer === null) return;
  window.clearInterval(timer);
  timer = null;
}

/** Latest snapshot (or null before the first successful poll). */
export function useSystemStats(): SystemStats | null {
  useSyncExternalStore(subscribe, () => cached ?? null);
  return cached;
}