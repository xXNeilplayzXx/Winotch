// Centralized theme management.
//
// One ThemeManager owns the whole UI theme: the user's preference (Light,
// Dark, or System Default — persisted in `winotch.theme`) plus the live
// Windows values (system theme + accent). It applies everything as CSS custom
// properties on <html> ([data-theme] + --accent, --accent-underline, ...), so
// every element — notch, settings, flyouts, popups, wizard — re-skins the
// instant anything changes, with no restart and no hardcoded colors in
// components. Rust emits `winotch://theme` whenever Windows appearance or the
// accent color changes; the manager also listens for it via a DOM event with
// the same name (browser previews / tests can simulate a system change).

import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";

export type ThemePref = "light" | "dark" | "system" | "color";
export type EffectiveTheme = "light" | "dark";

export interface SystemTheme {
  theme: EffectiveTheme;
  accent: [number, number, number];
}

export const THEME_KEY = "winotch.theme";

/** Accent preference: follow the live Windows accent, or a custom hex color. */
export const ACCENT_KEY = "winotch.accent";
export const ACCENT_SYSTEM = "system";

/** Fallback values (old brand gold) until the real Windows values arrive. */
const DEFAULT_ACCENT: [number, number, number] = [233, 185, 73];

const HEX_RE = /^#[0-9a-f]{6}$/i;

function parseHex(v: string): [number, number, number] | null {
  const m = HEX_RE.exec(v);
  if (!m) return null;
  return [
    parseInt(m[0].slice(1, 3), 16),
    parseInt(m[0].slice(3, 5), 16),
    parseInt(m[0].slice(5, 7), 16),
  ];
}

function isValidAccent(v: unknown): v is string {
  return v === ACCENT_SYSTEM || (typeof v === "string" && HEX_RE.test(v));
}

export function readThemePref(): ThemePref {
  try {
    const v = localStorage.getItem(THEME_KEY);
    if (v === "light" || v === "dark" || v === "system" || v === "color") return v;
  } catch {
    /* storage unavailable */
  }
  return "system";
}

export function saveThemePref(pref: ThemePref) {
  try {
    localStorage.setItem(THEME_KEY, pref);
  } catch {
    /* storage unavailable; keep session-only */
  }
}

/** The persisted accent: `system` (follow Windows) or a `#rrggbb` hex. */
export function readAccentPref(): string {
  try {
    const v = localStorage.getItem(ACCENT_KEY);
    if (isValidAccent(v)) return v === ACCENT_SYSTEM ? v : v.toLowerCase();
  } catch {
    /* storage unavailable */
  }
  return ACCENT_SYSTEM;
}

export function saveAccentPref(accent: string) {
  try {
    localStorage.setItem(ACCENT_KEY, isValidAccent(accent) ? accent.toLowerCase() : ACCENT_SYSTEM);
  } catch {
    /* storage unavailable; keep session-only */
  }
}

function rgb(c: [number, number, number]): string {
  return `rgb(${c[0]}, ${c[1]}, ${c[2]})`;
}

function rgba(c: [number, number, number], a: number): string {
  return `rgba(${c[0]}, ${c[1]}, ${c[2]}, ${a})`;
}

/**
 * The small underline beneath an active taskbar icon is the Windows accent
 * shown semi-transparent over the taskbar surface. Windows exposes the accent
 * (DWM ColorizationColor / UISettings) but not the pre-blended underline, so
 * we derive it: blend the accent ~35% into the taskbar surface color for the
 * current theme. On dark taskbars that keeps the accent vivid; on light
 * taskbars it lightens toward the surface, matching what Windows renders.
 */
function deriveUnderline(accent: [number, number, number], theme: EffectiveTheme): string {
  const surface = theme === "dark" ? [0, 0, 0] : [243, 243, 243];
  const a = 0.35;
  const mixed = accent.map((v, i) => Math.round(surface[i]! + (v - surface[i]!) * a)) as [
    number,
    number,
    number,
  ];
  return rgb(mixed);
}

class ThemeManager {
  pref: ThemePref = "system";
  systemTheme: EffectiveTheme = "dark";
  /** Live accent reported by Windows (used while accentPref is `system`). */
  systemAccent: [number, number, number] = DEFAULT_ACCENT;
  /** `system` (follow Windows) or an explicit `#rrggbb` hex. */
  accentPref: string = ACCENT_SYSTEM;
  private wired = false;

  /** The accent in effect: the custom hex when set, else the Windows one. */
  private effectiveAccent(): [number, number, number] {
    return parseHex(this.accentPref) ?? this.systemAccent;
  }

  /** Set the effective theme + Windows values onto <html> as CSS variables. */
  private apply() {
    const root = document.documentElement;
    // "color" is the customize-accent mode; backgrounds keep following the OS.
    const theme: EffectiveTheme =
      this.pref === "system" || this.pref === "color" ? this.systemTheme : this.pref;
    const accent = this.effectiveAccent();
    root.dataset.theme = theme;
    root.style.colorScheme = theme;
    root.style.setProperty("--accent", rgb(accent));
    root.style.setProperty("--accent-underline", deriveUnderline(accent, theme));
    root.style.setProperty("--accent-soft", rgba(accent, 0.16));
    root.style.setProperty("--accent-soft-hover", rgba(accent, 0.28));
    root.style.setProperty("--accent-strong", rgba(accent, 0.35));
    root.style.setProperty("--accent-wash", rgba(accent, 0.09));
  }

  /** Absorb a system snapshot (theme and/or accent) and re-apply. */
  handleSystem(s: SystemTheme) {
    if (s.theme === "light" || s.theme === "dark") this.systemTheme = s.theme;
    if (Array.isArray(s.accent) && s.accent.length === 3) {
      this.systemAccent = [s.accent[0], s.accent[1], s.accent[2]] as [number, number, number];
    }
    this.apply();
  }

  /** Switch the user-facing theme; persisted so it survives restarts. */
  setPref(pref: ThemePref) {
    this.pref = pref;
    saveThemePref(pref);
    this.apply();
  }

  /**
   * Set the accent: `system` follows the live Windows accent, any `#rrggbb`
   * hex overrides it everywhere (persisted, applied in every theme mode).
   */
  setAccentPref(accent: string) {
    this.accentPref = isValidAccent(accent) ? accent.toLowerCase() : ACCENT_SYSTEM;
    saveAccentPref(this.accentPref);
    this.apply();
  }

  /**
   * One-time startup: apply the stored preference immediately (so there is no
   * wrong-theme flash), then start following Windows values live.
   */
  init() {
    if (this.wired) return;
    this.wired = true;
    this.pref = readThemePref();
    this.accentPref = readAccentPref();
    this.apply();

    // Current snapshot from Rust (converges every window on launch).
    void invoke<SystemTheme>("get_system_theme")
      .then((s) => this.handleSystem(s))
      .catch(() => {});

    // Live updates: Rust's theme watcher emits when Windows appearance or the
    // accent changes. The DOM event lets browser previews simulate the same.
    void listen<SystemTheme>("winotch://theme", (e) => this.handleSystem(e.payload)).catch(
      () => {},
    );
    window.addEventListener("winotch://theme", ((e: CustomEvent<SystemTheme>) =>
      this.handleSystem(e.detail)) as EventListener);
  }
}

/** App-wide singleton — import and call `themeManager.init()` at startup. */
export const themeManager = new ThemeManager();