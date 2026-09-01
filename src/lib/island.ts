// Window geometry + animation for the Winotch island.
// "Logical" units are CSS pixels, which the webview measures. Island sizes are
// derived from the current display's width so it scales sensibly on big/other
// monitors, then converted to physical pixels via the monitor's scale factor.
//
// Anchoring: initial placement centers on the display, but every expand/collapse
// animation anchors to the island's OWN current center (the collapsed pill is
// the "trigger"). This keeps the floating panel/popover centered under its
// trigger even when the OS reports a different monitor or the island was moved,
// which is the Windows tray-popover behavior we want.
import {
  getCurrentWindow,
  currentMonitor,
  PhysicalPosition,
  PhysicalSize,
} from "@tauri-apps/api/window";
import {
  getMonitors,
  readMonitorPref,
  resolveMonitor,
  type MonitorInfo,
} from "./monitors";
import { readSizePref, type IslandSize } from "./size";

const appWindow = getCurrentWindow();

export interface Size {
  width: number;
  height: number;
}

export type IslandShape = "pill" | "notch";

export interface IslandSizes {
  pillCollapsed: Size;
  notchCollapsed: Size;
  expanded: Size;
}

function clamp(v: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, v));
}

/**
 * Sizes as fractions of the display width (points), clamped so the island is
 * never comically large on a big monitor nor minuscule on a small one.
 *
 * When a custom size is set (settings sliders), it overrides the collapsed
 * size in BOTH shapes; the expanded panel always grows to at least match the
 * custom width/height so a wide or tall notch never animates into a smaller
 * window.
 */
export function sizesForDisplay(widthPoints: number, custom?: IslandSize | null): IslandSizes {
  const sizes: IslandSizes = {
    pillCollapsed: {
      width: clamp(Math.round(widthPoints * 0.16), 240, 420),
      height: clamp(Math.round(widthPoints * 0.032), 44, 84),
    },
    notchCollapsed: {
      width: clamp(Math.round(widthPoints * 0.12), 260, 460),
      height: clamp(Math.round(widthPoints * 0.036), 52, 104),
    },
    expanded: {
      width: clamp(Math.round(widthPoints * 0.18), 300, 480),
      height: clamp(Math.round(widthPoints * 0.14), 240, 340),
    },
  };
  if (custom && custom.width > 0 && custom.height > 0) {
    sizes.pillCollapsed = { width: custom.width, height: custom.height };
    sizes.notchCollapsed = { width: custom.width, height: custom.height };
    sizes.expanded.width = Math.max(sizes.expanded.width, custom.width);
    sizes.expanded.height = Math.max(sizes.expanded.height, custom.height);
  }
  return sizes;
}

/**
 * Vertical placement: BOTH shapes hug the absolute top edge of the display
 * (offset 0) so the widget reads as physical hardware — a real notch or a
 * Dynamic Island. No margin, no gap; the window's top border is the monitor's
 * first pixel row. On Windows (no menu bar) that is the true screen top.
 */
const SHAPE_TOP_OFFSET: Record<IslandShape, number> = { notch: 0, pill: 0 };

/** Window geometry for a given shape/state. */
function targetState(
  shape: IslandShape,
  expanded: boolean,
  sizes: IslandSizes,
): { size: Size; topOffset: number } {
  if (expanded) {
    return { size: sizes.expanded, topOffset: SHAPE_TOP_OFFSET[shape] };
  }
  return shape === "notch"
    ? { size: sizes.notchCollapsed, topOffset: SHAPE_TOP_OFFSET.notch }
    : { size: sizes.pillCollapsed, topOffset: SHAPE_TOP_OFFSET.pill };
}

interface Geometry {
  scale: number;
  top: number;
  /** Horizontal center of the current display, in physical pixels. */
  centerX: number;
  sizes: IslandSizes;
}

/**
 * Window label determines which display a window anchors to in All-monitors
 * mode: main = index 0, extra windows are `island-1..N` (their label suffix).
 */
function windowIslandIndex(): number | null {
  const m = /^island-(\d+)$/.exec(getCurrentWindow().label);
  return m ? parseInt(m[1], 10) : null;
}

/** Resolve the display this window should anchor to, per the saved pref. */
async function resolveActiveMonitor(): Promise<MonitorInfo | null> {
  const pref = readMonitorPref();
  if (pref === "all") {
    const monitors = await getMonitors();
    const idx = windowIslandIndex() ?? 0;
    return monitors[Math.min(idx, monitors.length - 1)] ?? null;
  }
  return resolveMonitor(pref);
}

async function geometry(): Promise<Geometry> {
  const target = await resolveActiveMonitor();
  const mon = target
    ? {
        name: target.name,
        scaleFactor: target.scale,
        size: { width: target.width, height: target.height },
        position: { x: target.x, y: target.y },
      }
    : await currentMonitor().catch(() => null);
  if (!mon) {
    return { scale: 1, top: 0, centerX: 0, sizes: sizesForDisplay(1710, readSizePref()) };
  }
  const scale = mon.scaleFactor ?? 1;
  const centerX = mon.position.x + mon.size.width / 2;
  const top = mon.position.y;
  const widthPoints = Math.round(mon.size.width / scale);
  return {
    scale,
    top,
    centerX,
    sizes: sizesForDisplay(widthPoints, readSizePref()),
  };
}

/**
 * Position and size the window so it is centered on `anchorXPhys` (a physical
 * horizontal center) at `topOffsetLogical` below the display's top edge. Used
 * with the display center for first placement and with the island's own center
 * during animations.
 */
async function placeWindow(
  geo: Geometry,
  anchorXPhys: number,
  size: Size,
  topOffsetLogical: number,
): Promise<void> {
  const pixelWidth = Math.round(size.width * geo.scale);
  const pixelHeight = Math.round(size.height * geo.scale);
  const x = Math.round(anchorXPhys - pixelWidth / 2);
  const y = geo.top + Math.round(topOffsetLogical * geo.scale);

  await appWindow.setPosition(new PhysicalPosition(x, y));
  await appWindow.setSize(new PhysicalSize(pixelWidth, pixelHeight));
}

/** Place the island collapsed, centered on the display, then reveal + focus. */
export async function showIsland(shape: IslandShape): Promise<void> {
  const geo = await geometry();
  const { size, topOffset } = targetState(shape, false, geo.sizes);
  await placeWindow(geo, geo.centerX, size, topOffset);
  await appWindow.show();
  await appWindow.setFocus();
}

/** Size of the first-run setup wizard window (logical CSS pixels). */
export const SETUP_WIDTH = 480;
export const SETUP_HEIGHT = 600;

/**
 * Resize and center the window for the first-run setup wizard, positioned in
 * the upper third of the current display so it reads as a dialog rather than
 * hardware. Runs BEFORE the island takes over the window.
 */
export async function showSetup(): Promise<void> {
  const mon = await currentMonitor();
  const scale = mon?.scaleFactor ?? 1;
  const centerX = mon ? mon.position.x + mon.size.width / 2 : 0;
  const top = mon ? mon.position.y : 0;
  const displayH = mon ? mon.size.height : 1080;
  const pixelW = Math.round(SETUP_WIDTH * scale);
  const pixelH = Math.round(SETUP_HEIGHT * scale);
  const x = Math.round(centerX - pixelW / 2);
  const y = Math.round(top + Math.max(0, (displayH - pixelH) / 3));
  await appWindow.setPosition(new PhysicalPosition(x, y));
  await appWindow.setSize(new PhysicalSize(pixelW, pixelH));
  await appWindow.show();
  await appWindow.setFocus();
}

function easeInOutCubic(t: number): number {
  return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
}

let animationToken = 0;

export interface AnimateOpts {
  shape: IslandShape;
  expanded: boolean;
  /** Settings panel open — the expanded window needs extra vertical room. */
  settingsOpen?: boolean;
  /** Extra expanded height for stacked widget cards, in logical CSS px. */
  extraHeight?: number;
}

/** Extra logical height for the expanded window while the settings are open. */
export const SETTINGS_HEIGHT_BONUS = 210;

/**
 * The window is a transparent ENVELOPE that is large enough for the island in
 * its fully expanded state — it must be, because the DOM (motion.div) owns the
 * visible morph now. We never tween physical window geometry per frame (that
 * stutters on Windows); instead this function moves the envelope in ONE call:
 *
 * - Expand: grow the envelope to the expanded size immediately (invisible —
 *   the webview is transparent), then the React motion.div springs open.
 * - Collapse: the DOM springs shut first (the caller keeps the envelope until
 *   then, so the closing panel is never clipped), and after a short settle
 *   delay the envelope shrinks back to the collapsed size — also one call.
 */
export async function animateIsland(opts: AnimateOpts): Promise<void> {
  const { shape, expanded } = opts;
  const token = ++animationToken;
  const geo = await geometry();
  const base = targetState(shape, expanded, geo.sizes);
  const target = {
    size: { ...base.size },
    topOffset: base.topOffset,
  };
  // Reserve room for the settings panel so its content can never overflow the
  // window (which is how a scrollbar could ever appear).
  if (opts.settingsOpen && expanded) {
    target.size.height += SETTINGS_HEIGHT_BONUS;
  }
  // Reserve room for stacked widget cards (each card beyond the first).
  if (opts.extraHeight && expanded) {
    target.size.height += opts.extraHeight;
  }

  // Anchor to the pill's own center, NOT the display center, so the panel stays
  // centered under the trigger even if the OS reports a different monitor or the
  // island was moved.
  const outer = await appWindow.outerPosition(); // PhysicalPosition
  const inner = await appWindow.innerSize(); // PhysicalSize
  const anchorXPhys = outer.x + inner.width / 2;
  const place = () =>
    placeWindow(geo, anchorXPhys, target.size, target.topOffset);

  if (expanded) {
    // Grow the transparent envelope first; the DOM spring follows.
    await place();
  } else {
    // Let the DOM spring collapse first (see module comment), then settle the
    // window back to the collapsed envelope in a single call. Token-guarded so
    // a rapid re-expand cancels the pending shrink.
    window.setTimeout(() => {
      if (token !== animationToken) return;
      void place().catch(() => {});
    }, COLLAPSE_SETTLE_MS);
  }
}

/** How long the DOM collapse spring runs before the envelope shrinks (ms). */
export const COLLAPSE_SETTLE_MS = 420;

/**
 * Logical (CSS-pixel) size the DOM island should take in a given state — the
 * single source of truth shared by the motion.div in Island.tsx and the window
 * envelope above, so the visible element and the OS window always agree.
 */
export function logicalTargetSize(
  shape: IslandShape,
  expanded: boolean,
  geo: { widthPoints: number; custom: IslandSize | null },
  opts: { settingsOpen?: boolean; extraHeight?: number } = {},
): Size {
  const sizes = sizesForDisplay(geo.widthPoints, geo.custom);
  const base = targetState(shape, expanded, sizes);
  if (expanded) {
    base.size.height += (opts.settingsOpen ? SETTINGS_HEIGHT_BONUS : 0) + (opts.extraHeight ?? 0);
  }
  return base.size;
}