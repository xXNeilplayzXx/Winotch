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

export const GLASS_BLUR_KEY = "winotch.glassBlur";
export const GLASS_BLUR_DEFAULT = 14;
export const GLASS_BLUR_MIN = 0;
export const GLASS_BLUR_MAX = 40;

function clampBlur(v: number): number {
  if (Number.isNaN(v)) return GLASS_BLUR_DEFAULT;
  const c = Math.max(GLASS_BLUR_MIN, Math.min(GLASS_BLUR_MAX, Math.round(v)));
  // Snap to even steps (the slider's step is 2) so label and thumb agree.
  return c - (c % 2);
}

export function readGlassBlur(): number {
  try {
    const raw = localStorage.getItem(GLASS_BLUR_KEY);
    if (raw !== null) return clampBlur(parseFloat(raw));
  } catch {
    /* storage unavailable; keep session-only */
  }
  return GLASS_BLUR_DEFAULT;
}

export function saveGlassBlur(px: number) {
  try {
    localStorage.setItem(GLASS_BLUR_KEY, String(clampBlur(px)));
  } catch {
    /* storage unavailable; keep session-only */
  }
}

/** Apply the blur to the live `--glass-blur` var (backdrop-filter's blur()). */
export function applyGlassBlur(px: number) {
  document.documentElement.style.setProperty("--glass-blur", `${clampBlur(px)}px`);
}