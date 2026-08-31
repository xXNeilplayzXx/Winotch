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
 */
export function sizesForDisplay(widthPoints: number): IslandSizes {
  return {
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

async function geometry(): Promise<Geometry> {
  const mon = await currentMonitor();
  const scale = mon?.scaleFactor ?? 1;
  const centerX = mon ? mon.position.x + mon.size.width / 2 : 0;
  const top = mon ? mon.position.y : 0;
  const widthPoints = mon ? Math.round(mon.size.width / scale) : 1710;
  return {
    scale,
    top,
    centerX,
    sizes: sizesForDisplay(widthPoints),
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

function easeInOutCubic(t: number): number {
  return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
}

let animationToken = 0;

export interface AnimateOpts {
  shape: IslandShape;
  expanded: boolean;
  duration?: number;
}

/**
 * Tween the window between collapsed and expanded sizes, anchoring the
 * horizontal center to the island's current center (the trigger pill) so the
 * panel never drifts left/right relative to it.
 */
export async function animateIsland(opts: AnimateOpts): Promise<void> {
  const { shape, expanded, duration = 300 } = opts;
  const token = ++animationToken;
  const geo = await geometry();
  const target = targetState(shape, expanded, geo.sizes);

  // Anchor to the pill's own center, NOT the display center, so the panel stays
  // centered under the trigger even if the OS reports a different monitor or the
  // island was moved.
  const outer = await appWindow.outerPosition(); // PhysicalPosition
  const inner = await appWindow.innerSize(); // PhysicalSize
  const anchorXPhys = outer.x + inner.width / 2;

  const startLogical = {
    width: inner.width / geo.scale,
    height: inner.height / geo.scale,
    // Derive the current top offset from where the window actually is, so the
    // y-tween is correct no matter which shape/state produced it.
    topOffset: (outer.y - geo.top) / geo.scale,
  };

  return new Promise<void>((resolve) => {
    const step = async () => {
      if (token !== animationToken) {
        resolve(); // superseded by a newer animation
        return;
      }
      const t = Math.min(1, (performance.now() - (step as any)._start) / duration);
      const e = easeInOutCubic(t);
      await placeWindow(
        geo,
        anchorXPhys,
        {
          width: Math.round(startLogical.width + (target.size.width - startLogical.width) * e),
          height: Math.round(startLogical.height + (target.size.height - startLogical.height) * e),
        },
        startLogical.topOffset + (target.topOffset - startLogical.topOffset) * e,
      );
      if (t < 1) {
        requestAnimationFrame(step);
      } else {
        // Re-assert on-screen state after the move; macOS sometimes drops a moved
        // always-on-top window from the compositor otherwise.
        await appWindow.show();
        await appWindow.setFocus();
        resolve();
      }
    };
    (step as any)._start = performance.now();
    void step().catch(() => resolve());
  });
}