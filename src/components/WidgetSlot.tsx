import type { ReactNode } from "react";

/**
 * WidgetSlot is the docking point for feature widgets. Future modules
 * (Now Playing, clipboard history, timers...) will render here.
 */
export function WidgetSlot({ children }: { children?: ReactNode }) {
  return <>{children}</>;
}