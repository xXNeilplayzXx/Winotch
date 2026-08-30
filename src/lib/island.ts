// Window geometry + animation for the Winotch island.
// "Logical" units are CSS pixels, which the webview measures. Island sizes are
// derived from the current display's width so it scales up on large (e.g. second)
// monitors instead of staying a fixed compact size, then converted to physical
// pixels via the monitor's scale factor.
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
      width: clamp(Math.round(widthPoints * 0.2), 320, 520),
      height: clamp(Math.round(widthPoints * 0.16), 260, 420),
    },
  };
}

/**
 * How far (logical points) below the top edge of the display to place the
 * island. Zero hugs the very top edge so the island reads as attached to the
 * top / a Dynamic Island. On Windows (no menu bar) this is the true top of the
 * screen.
 */
const TOP_OFFSET = 0;

interface Geometry {
  scale: number;
  centerX: number;
  top: number;
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
    centerX,
    top,
    sizes: sizesForDisplay(widthPoints),
  };
}

async function applySize(geo: Geometry, size: Size): Promise<void> {
  const pixelWidth = Math.round(size.width * geo.scale);
  const pixelHeight = Math.round(size.height * geo.scale);
  const x = Math.round(geo.centerX - pixelWidth / 2);
  const y = geo.top + Math.round(TOP_OFFSET * geo.scale);

  await appWindow.setPosition(new PhysicalPosition(x, y));
  await appWindow.setSize(new PhysicalSize(pixelWidth, pixelHeight));
}

/** Place the island collapsed for the given shape, then reveal and focus it. */
export async function showIsland(shape: IslandShape): Promise<void> {
  const geo = await geometry();
  const size =
    shape === "notch" ? geo.sizes.notchCollapsed : geo.sizes.pillCollapsed;
  await applySize(geo, size);
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

/** Tween the native window between the collapsed and expanded sizes, centered. */
export async function animateIsland(opts: AnimateOpts): Promise<void> {
  const { shape, expanded, duration = 300 } = opts;
  const token = ++animationToken;
  const geo = await geometry();
  const target =
    expanded === false
      ? shape === "notch"
        ? geo.sizes.notchCollapsed
        : geo.sizes.pillCollapsed
      : geo.sizes.expanded;

  const current = await appWindow.innerSize(); // PhysicalSize
  const startLogical = {
    width: current.width / geo.scale,
    height: current.height / geo.scale,
  };

  return new Promise<void>((resolve) => {
    const step = async () => {
      if (token !== animationToken) {
        resolve(); // superseded by a newer animation
        return;
      }
      const t = Math.min(1, (performance.now() - (step as any)._start) / duration);
      const e = easeInOutCubic(t);
      await applySize(geo, {
        width: Math.round(startLogical.width + (target.width - startLogical.width) * e),
        height: Math.round(startLogical.height + (target.height - startLogical.height) * e),
      });
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