// Glass opacity preference for the settings panel.
//
// Liquid-glass mode is translucent by design; the opacity slider scales the
// alpha of every --glass-bg gradient stop from 100 (the classic look) down to
// 10. The value is a percent (10-100) persisted under `winotch.glassOpacity`,
// and it is applied as the `--glass-opacity` CSS variable on <html> so the
// whole island re-skins instantly in both themes.

export const GLASS_OPACITY_KEY = "winotch.glassOpacity";
export const GLASS_OPACITY_DEFAULT = 100;
export const GLASS_OPACITY_MIN = 10;
export const GLASS_OPACITY_MAX = 100;

function clamp(v: number): number {
  if (Number.isNaN(v)) return GLASS_OPACITY_DEFAULT;
  return Math.max(GLASS_OPACITY_MIN, Math.min(GLASS_OPACITY_MAX, Math.round(v)));
}

export function readGlassOpacity(): number {
  try {
    const raw = localStorage.getItem(GLASS_OPACITY_KEY);
    if (raw !== null) return clamp(parseFloat(raw));
  } catch {
    /* storage unavailable; keep session-only */
  }
  return GLASS_OPACITY_DEFAULT;
}

export function saveGlassOpacity(percent: number) {
  try {
    localStorage.setItem(GLASS_OPACITY_KEY, String(clamp(percent)));
  } catch {
    /* storage unavailable; keep session-only */
  }
}

/** Apply the opacity to the live `--glass-bg` gradient (1 = classic look). */
export function applyGlassOpacity(percent: number) {
  document.documentElement.style.setProperty("--glass-opacity", String(clamp(percent) / 100));
}