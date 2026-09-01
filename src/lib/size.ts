// Custom island sizing, persisted across runs.
//
// The collapsed notch/pill is normally derived from the display width (see
// sizesForDisplay in island.ts). The settings panel lets the user override it
// with explicit logical (CSS-pixel) dimensions, stored under `winotch.size`
// as `{ width, height }`. `null` = keep deriving from the display.

export interface IslandSize {
  width: number;
  height: number;
}

export const SIZE_KEY = "winotch.size";

/** Slider bounds (kept in sync with the `input[type=range]` attributes). */
export const SIZE_MIN_WIDTH = 150;
export const SIZE_MAX_WIDTH = 800;
export const SIZE_MIN_HEIGHT = 30;
export const SIZE_MAX_HEIGHT = 200;

function clamp(v: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, Math.round(v)));
}

/** Round and bound a custom size to the slider range. */
export function clampSize(size: IslandSize): IslandSize {
  return {
    width: clamp(size.width, SIZE_MIN_WIDTH, SIZE_MAX_WIDTH),
    height: clamp(size.height, SIZE_MIN_HEIGHT, SIZE_MAX_HEIGHT),
  };
}

/** The persisted custom size, or null when the size is auto-derived. */
export function readSizePref(): IslandSize | null {
  try {
    const raw = localStorage.getItem(SIZE_KEY);
    if (raw) {
      const v: unknown = JSON.parse(raw);
      if (
        !!v &&
        typeof v === "object" &&
        typeof (v as IslandSize).width === "number" &&
        typeof (v as IslandSize).height === "number"
      ) {
        return clampSize(v as IslandSize);
      }
    }
  } catch {
    /* corrupted storage; fall through to derived */
  }
  return null;
}

export function saveSizePref(size: IslandSize | null) {
  try {
    if (size) {
      localStorage.setItem(SIZE_KEY, JSON.stringify(clampSize(size)));
    } else {
      localStorage.removeItem(SIZE_KEY);
    }
  } catch {
    /* storage unavailable; keep session-only */
  }
}