// Window geometry + animation for the Winotch island.
// "Logical" units are CSS pixels, which is what the webview measures.
// The native window is set in physical pixels, scaling by the monitor's scale factor
// so the island renders at a consistent on-screen size across displays.
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

/** Collapsed geometry per mode. Both open to the same expanded panel on hover. */
export const SHAPES: Record<IslandShape, { collapsed: Size }> = {
  pill: { collapsed: { width: 380, height: 58 } },
  notch: { collapsed: { width: 112, height: 84 } },
};

/** The panel size once expanded. */
export const EXPANDED: Size = { width: 420, height: 320 };

/**
 * How far (logical points) below the top edge of the display to place the
 * island. Sits just under the macOS menu bar/notch (~34pt) so it stays visible
 * instead of hiding inside a real notch. Windows has no menu bar, so this small
 * offset just hugs the top edge there.
 */
const TOP_OFFSET = 56;

interface MonitorGeometry {
  scale: number;
  centerX: number;
  top: number;
}

async function monitorGeometry(): Promise<MonitorGeometry> {
  const mon = await currentMonitor();
  if (!mon) {
    return { scale: 1, centerX: 0, top: 0 };
  }
  // Work-area coordinates come back inconsistently on macOS, so anchor to the
  // true top edge of the display instead and offset below the menu bar.
  return {
    scale: mon.scaleFactor ?? 1,
    centerX: mon.position.x + mon.size.width / 2,
    top: mon.position.y,
  };
}

async function applySize(size: Size): Promise<void> {
  const geo = await monitorGeometry();
  const pixelWidth = Math.round(size.width * geo.scale);
  const pixelHeight = Math.round(size.height * geo.scale);
  const x = Math.round(geo.centerX - pixelWidth / 2);
  const y = geo.top + Math.round(TOP_OFFSET * geo.scale);

  await appWindow.setPosition(new PhysicalPosition(x, y));
  await appWindow.setSize(new PhysicalSize(pixelWidth, pixelHeight));
}

/** Place the island collapsed for the given shape, then reveal and focus it. */
export async function showIsland(shape: IslandShape): Promise<void> {
  await applySize(SHAPES[shape].collapsed);
  await appWindow.show();
  await appWindow.setFocus();
}

function easeInOutCubic(t: number): number {
  return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
}

let animationToken = 0;

/** Tween the native window from its current size to `to`, keeping it centered. */
export async function animateTo(to: Size, duration = 300): Promise<void> {
  const token = ++animationToken;
  const geo = await monitorGeometry();
  const current = await appWindow.innerSize(); // PhysicalSize
  const startLogical = {
    width: current.width / geo.scale,
    height: current.height / geo.scale,
  };

  return new Promise<void>((resolve) => {
    const step = async () => {
      if (token !== animationToken) {
        return; // superseded by a newer animation
      }
      const t = Math.min(1, (performance.now() - (step as any)._start) / duration);
      const e = easeInOutCubic(t);
      await applySize({
        width: Math.round(startLogical.width + (to.width - startLogical.width) * e),
        height: Math.round(startLogical.height + (to.height - startLogical.height) * e),
      });
      if (t < 1) {
        requestAnimationFrame(step);
      } else {
        resolve();
      }
    };
    (step as any)._start = performance.now();
    step();
  });
}