// Extra appearance preferences from the settings panel: background gradients,
// drag-to-move, and what the collapsed strip shows while nothing is active.
// Each preference is a small persisted localStorage value with a safe default.

export const GRADIENT_KEY = "winotch.gradient";

export type GradientPref = "none" | "sunset" | "ocean" | "aurora" | "candy" | "graphite";

export const GRADIENTS: { id: GradientPref; label: string }[] = [
  { id: "none", label: "None" },
  { id: "sunset", label: "Sunset" },
  { id: "ocean", label: "Ocean" },
  { id: "aurora", label: "Aurora" },
  { id: "candy", label: "Candy" },
  { id: "graphite", label: "Graphite" },
];

const GRADIENT_IDS: string[] = GRADIENTS.map((g) => g.id);

export function readGradient(): GradientPref {
  try {
    const v = localStorage.getItem(GRADIENT_KEY);
    if (GRADIENT_IDS.includes(v ?? "")) return v as GradientPref;
  } catch {
    /* storage unavailable */
  }
  return "none";
}

export function saveGradient(pref: GradientPref) {
  try {
    localStorage.setItem(GRADIENT_KEY, GRADIENT_IDS.includes(pref) ? pref : "none");
  } catch {
    /* storage unavailable; keep session-only */
  }
}

export const DRAG_KEY = "winotch.draggable";

/** Whether drag-to-move is enabled. Opt-in: only an explicit "on" enables it. */
export function readDraggable(): boolean {
  try {
    return localStorage.getItem(DRAG_KEY) === "on";
  } catch {
    return false;
  }
}

export function saveDraggable(on: boolean) {
  try {
    localStorage.setItem(DRAG_KEY, on ? "on" : "off");
  } catch {
    /* storage unavailable; keep session-only */
  }
}

export const IDLE_KEY = "winotch.idleContent";
export const IDLE_TEXT_KEY = "winotch.idleText";

export type IdleContent = "brand" | "clock" | "custom" | "system" | "blank";

const IDLE_IDS = ["brand", "clock", "custom", "system", "blank"];

/** What the collapsed strip shows while idle: brand+clock, clock only, custom
 * text, live system-stats mini bar, or just the dot. */
export function readIdleContent(): IdleContent {
  try {
    const v = localStorage.getItem(IDLE_KEY);
    if (IDLE_IDS.includes(v ?? "")) return v as IdleContent;
  } catch {
    /* storage unavailable */
  }
  return "brand";
}

export function saveIdleContent(pref: IdleContent) {
  try {
    localStorage.setItem(IDLE_KEY, IDLE_IDS.includes(pref) ? pref : "brand");
  } catch {
    /* storage unavailable; keep session-only */
  }
}

export function readIdleText(): string {
  try {
    return localStorage.getItem(IDLE_TEXT_KEY) ?? "";
  } catch {
    return "";
  }
}

export function saveIdleText(text: string) {
  try {
    localStorage.setItem(IDLE_TEXT_KEY, text.slice(0, 40));
  } catch {
    /* storage unavailable; keep session-only */
  }
}