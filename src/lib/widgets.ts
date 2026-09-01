// Widget configuration for the island's expanded panel.
//
// Users can add, remove, and reorder widgets (clock, media control, weather,
// app launcher). The layout is a persisted list of instances under
// `winotch.widgets`; each instance is one widget type with its own settings.

export type WidgetType = "clock" | "media" | "notes" | "weather" | "launcher" | "system";

export interface WeatherSettings {
  /** Display name, e.g. "Berlin, Germany". */
  city: string;
  lat?: number;
  lon?: number;
}

export interface LauncherApp {
  label: string;
  /** Command line: an exe path, a PATH-resolvable name, or `%VAR%`-containing path. */
  path: string;
}

export interface LauncherSettings {
  apps: LauncherApp[];
}

export interface NotesSettings {
  text: string;
}

export interface SystemSettings {
  /** Per-metric visibility toggles for the card and the collapsed mini bar.
   *  Undefined means the metric is shown (default). */
  cpu?: boolean;
  mem?: boolean;
  net?: boolean;
  temp?: boolean;
  batt?: boolean;
}

export interface WidgetInstance {
  id: string;
  type: WidgetType;
  weather?: WeatherSettings;
  launcher?: LauncherSettings;
  notes?: NotesSettings;
  system?: SystemSettings;
}

export const WIDGETS_KEY = "winotch.widgets";

export const WIDGET_TYPES: WidgetType[] = ["media", "clock", "notes", "weather", "launcher", "system"];

export const WIDGET_LABELS: Record<WidgetType, string> = {
  media: "Media control",
  clock: "Clock",
  notes: "Notes",
  weather: "Weather",
  launcher: "App launcher",
  system: "System stats",
};

export function uid(): string {
  try {
    return crypto.randomUUID();
  } catch {
    return `w${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`;
  }
}

/** Default first-run layout: media player on top, clock beneath. */
export function defaultWidgets(): WidgetInstance[] {
  return [{ id: uid(), type: "media" }, { id: uid(), type: "clock" }];
}

/** Load the persisted widget list, falling back to the default layout. */
export function readWidgets(): WidgetInstance[] {
  try {
    const raw = localStorage.getItem(WIDGETS_KEY);
    if (raw) {
      const arr: unknown = JSON.parse(raw);
      if (Array.isArray(arr)) {
        return arr.filter(
          (v): v is WidgetInstance =>
            !!v && typeof v === "object" && typeof (v as WidgetInstance).type === "string",
        );
      }
    }
  } catch {
    /* corrupted storage; fall through to default */
  }
  return defaultWidgets();
}

export function saveWidgets(list: WidgetInstance[]) {
  try {
    localStorage.setItem(WIDGETS_KEY, JSON.stringify(list));
  } catch {
    /* storage unavailable; keep session-only */
  }
}