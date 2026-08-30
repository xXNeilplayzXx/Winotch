// Window geometry + animation for the Freebuff island.
// "Logical" units are CSS pixels, which is what the webview measures.
// The native window is set in physical pixels, scaling by the monitor's scale factor
// so the pill renders at a consistent on-screen size across displays.
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

export const ISLAND = {
  collapsed: { width: 380, height: 58 } as Size,
  expanded: { width: 430, height: 320 } as Size,
};

/** Margin (logical px) kept between the top of the screen and the island. */
const TOP_MARGIN = 10;

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
  return {
    scale: mon.scaleFactor ?? 1,
    centerX: mon.position.x + mon.size.width / 2,
    top: mon.position.y,
  };
}

/**
 * Position the native window so it sits, centered, at the top of the screen
 * with the given logical size. Keeps the horizontal center fixed as size changes.
 */
async function applySize(size: Size): Promise<void> {
  const geo = await monitorGeometry();
  const pixelWidth = Math.round(size.width * geo.scale);
  const pixelHeight = Math.round(size.height * geo.scale);
  const x = Math.round(geo.centerX - pixelWidth / 2);
  const y = geo.top + Math.round(TOP_MARGIN * geo.scale);

  await appWindow.setPosition(new PhysicalPosition(x, y));
  await appWindow.setSize(new PhysicalSize(pixelWidth, pixelHeight));
}

/** Place the island collapsed, then reveal it. Returns when it is visible. */
export async function showIsland(): Promise<void> {
  await applySize(ISLAND.collapsed);
  await appWindow.show();
}

function easeInOutCubic(t: number): number {
  return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
}

let animationToken = 0;

/**
 * Tween the native window from its current size to `to`. Newer calls supersede any
 * in-flight animation, so rapid hover-in/hover-out stay smooth and never fight.
 */
export async function animateTo(to: Size, duration = 320): Promise<void> {
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