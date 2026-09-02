import { useLayoutEffect, useRef, useState } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { uid, WIDGET_LABELS, WIDGET_TYPES } from "../lib/widgets";
import type {
  LauncherSettings,
  NotesSettings,
  SystemSettings,
  WeatherSettings,
  WidgetInstance,
  WidgetType,
} from "../lib/widgets";
import { Clock } from "./widgets/Clock";
import { MediaWidget, type MediaState } from "./widgets/MediaWidget";
import { NotesWidget } from "./widgets/NotesWidget";
import { WeatherWidget } from "./widgets/WeatherWidget";
import { LauncherWidget } from "./widgets/LauncherWidget";
import { SystemWidget } from "./widgets/SystemWidget";

interface WidgetStackProps {
  widgets: WidgetInstance[];
  media: MediaState | null;
  /** Called whenever the layout changes (add/remove/reorder/settings). */
  onChange: (next: WidgetInstance[]) => void;
  /** Setup-wizard mode: every configurable widget starts open for editing. */
  configureAll?: boolean;
}

function GearIcon({ size = 11 }: { size?: number }) {
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
      <circle cx="12" cy="12" r="3" />
      <path d="M19.4 15a1.7 1.7 0 0 0 .34 1.87l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.7 1.7 0 0 0-1.87-.34 1.7 1.7 0 0 0-1 1.55V21a2 2 0 1 1-4 0v-.09a1.7 1.7 0 0 0-1-1.55 1.7 1.7 0 0 0-1.87.34l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.7 1.7 0 0 0 .34-1.87 1.7 1.7 0 0 0-1.55-1H3a2 2 0 1 1 0-4h.09a1.7 1.7 0 0 0 1.55-1 1.7 1.7 0 0 0-.34-1.87l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.7 1.7 0 0 0 1.87.34h.09a1.7 1.7 0 0 0 1-1.55V3a2 2 0 1 1 4 0v.09a1.7 1.7 0 0 0 1 1.55 1.7 1.7 0 0 0 1.87-.34l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.7 1.7 0 0 0-.34 1.87v.09a1.7 1.7 0 0 0 1.55 1H21a2 2 0 1 1 0 4h-.09a1.7 1.7 0 0 0-1.55 1z" />
    </svg>
  );
}

/**
 * The island's widget grid. Renders every widget as a card with add (＋ menu),
 * reorder (↑/↓), remove (✕), and — for weather / app launcher — inline
 * settings (⚙). Shared with the setup wizard so widgets can be arranged
 * BEFORE the island launches.
 */
export function WidgetStack({ widgets, media, onChange, configureAll = false }: WidgetStackProps) {
  const [editingId, setEditingId] = useState<string | null>(null);
  const [addOpen, setAddOpen] = useState(false);
  /** Cards mid-removal: they fade/scales out before dropping out of the list. */
  const [leaving, setLeaving] = useState<Set<string>>(new Set());

  // FLIP reorder/remove animation: positions captured before a layout change,
  // then every card that moved animates from its old spot to the new one (the
  // swapped/remaining cards slide too, so the whole list stays fluid).
  const listRef = useRef<HTMLDivElement | null>(null);
  const flipBefore = useRef<Map<string, { top: number; left: number }> | null>(null);

  const captureRects = (): Map<string, { top: number; left: number }> => {
    const map = new Map<string, { top: number; left: number }>();
    listRef.current?.querySelectorAll<HTMLElement>("[data-wid]").forEach((el) => {
      const r = el.getBoundingClientRect();
      map.set(el.dataset.wid ?? "", { top: r.top, left: r.left });
    });
    return map;
  };

  // Runs right after the DOM reflects a (potential) reorder; no-op otherwise.
  useLayoutEffect(() => {
    const before = flipBefore.current;
    if (!before) return;
    flipBefore.current = null;
    const after = captureRects();
    listRef.current?.querySelectorAll<HTMLElement>("[data-wid]").forEach((el) => {
      const key = el.dataset.wid ?? "";
      const oldPos = before.get(key);
      const newPos = after.get(key);
      if (!oldPos || !newPos || (oldPos.top === newPos.top && oldPos.left === newPos.left)) {
        return;
      }
      const dx = oldPos.left - newPos.left;
      const dy = oldPos.top - newPos.top;
      // Drop any still-running animation from a previous reorder, then slide.
      el.getAnimations().forEach((a) => a.cancel());
      el.animate(
        [{ transform: `translate(${dx}px, ${dy}px)` }, { transform: "translate(0, 0)" }],
        { duration: 380, easing: "cubic-bezier(0.22, 1, 0.36, 1)" },
      );
      // Brief accent flash so the cards that just moved feel "picked up".
      el.animate(
        // Theme tokens so the flash follows the current accent + theme.
        [
          { backgroundColor: "var(--accent-soft)" },
          { backgroundColor: "var(--card-bg)" },
        ],
        { duration: 560, easing: "ease-out" },
      );
    });
  });

  const availableTypes = WIDGET_TYPES.filter((t) => !widgets.some((w) => w.type === t));

  const update = (next: WidgetInstance[]) => onChange(next);

  const addWidget = (type: WidgetType) => {
    if (widgets.some((w) => w.type === type)) return;
    update([...widgets, { id: uid(), type }]);
    setAddOpen(false);
  };

  const removeWidget = (id: string) => {
    if (leaving.has(id)) return;
    // Capture positions, mark the card as leaving (it fades/scales out via
    // CSS), then drop it from the list — the FLIP effect above slides the
    // remaining cards up into the gap, so removal looks continuous.
    flipBefore.current = captureRects();
    setLeaving((prev) => new Set(prev).add(id));
    if (editingId === id) setEditingId(null);
    // Keep in step with the .widget-card-leaving transition (0.28s in CSS).
    window.setTimeout(() => {
      setLeaving((prev) => {
        const next = new Set(prev);
        next.delete(id);
        return next;
      });
      update(widgets.filter((w) => w.id !== id));
    }, 280);
  };

  const moveWidget = (id: string, dir: -1 | 1) => {
    const i = widgets.findIndex((w) => w.id === id);
    const j = i + dir;
    if (i < 0 || j < 0 || j >= widgets.length) return;
    // Capture pre-move positions so the cards can animate to their new spots.
    flipBefore.current = captureRects();
    const next = [...widgets];
    [next[i], next[j]] = [next[j], next[i]];
    update(next);
  };

  const saveSettings = (
    id: string,
    patch: {
      weather?: WeatherSettings;
      launcher?: LauncherSettings;
      notes?: NotesSettings;
      system?: SystemSettings;
    },
  ) => {
    update(widgets.map((w) => (w.id === id ? { ...w, ...patch } : w)));
    // Give the edited card a tiny pop so the save is visible inline.
    listRef.current
      ?.querySelector<HTMLElement>(`[data-wid="${id}"]`)
      ?.animate(
        [{ transform: "scale(0.985)" }, { transform: "scale(1)" }],
        { duration: 280, easing: "cubic-bezier(0.22, 1, 0.36, 1)" },
      );
  };

  // Spring shared by card entrances (staggered) and control taps.
  const spring = { type: "spring", stiffness: 380, damping: 30 } as const;

  return (
    <div className="panel-widgets" ref={listRef}>
      <div className="widget-stack-head">
        {/* Instrument LED: deck-style accent marker for the widgets header. */}
        <span className="led" aria-hidden />
        <span className="widget-label">Widgets</span>
        <motion.button
          type="button"
          className={`icon-btn ${addOpen ? "icon-btn-active" : ""}`}
          title="Add widget"
          onClick={() => setAddOpen((v) => !v)}
          whileHover={{ scale: 1.08 }}
          whileTap={{ scale: 0.88 }}
          transition={spring}
        >
          <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round">
            <path d="M12 5v14M5 12h14" />
          </svg>
        </motion.button>
      </div>

      {addOpen && (
        <div className="add-menu">
          {availableTypes.length === 0 ? (
            <span className="add-menu-empty">All widgets added</span>
          ) : (
            availableTypes.map((t) => (
              <motion.button
                type="button"
                key={t}
                onClick={() => addWidget(t)}
                whileHover={{ x: 3 }}
                whileTap={{ scale: 0.97 }}
                transition={spring}
              >
                <span className="add-menu-plus">＋</span>
                {WIDGET_LABELS[t]}
              </motion.button>
            ))
          )}
        </div>
      )}

      {widgets.length === 0 && (
        <div className="panel-empty">
          <p>No widgets</p>
          <span>Press ＋ to add one</span>
        </div>
      )}

      <AnimatePresence>
        {widgets.map((w, i) => {
          const editable =
            w.type === "weather" || w.type === "launcher" || w.type === "notes" || w.type === "system";
          const edit = configureAll || editingId === w.id;
          return (
            <motion.div
              className={`widget${leaving.has(w.id) ? " widget-removing" : ""}`}
              key={w.id}
              data-wid={w.id}
              data-tauri-drag-region="false"
              initial={{ opacity: 0, y: 10, scale: 0.97 }}
              animate={{ opacity: 1, y: 0, scale: 1 }}
              exit={{ opacity: 0, scale: 0.95, transition: { duration: 0.18, ease: "easeIn" } }}
              transition={{ ...spring, delay: i * 0.055 }}
            >
            <div className="widget-head">
              <span className="led" aria-hidden />
              <span className="widget-title">{WIDGET_LABELS[w.type]}</span>
              <div className="widget-actions">
                {editable && !configureAll && (
                  <motion.button
                    type="button"
                    className={`widget-act${editingId === w.id ? " active" : ""}`}
                    title="Settings"
                    onClick={() => setEditingId(editingId === w.id ? null : w.id)}
                    whileTap={{ scale: 0.85 }}
                    transition={spring}
                  >
                    <GearIcon />
                  </motion.button>
                )}
                <motion.button
                  type="button"
                  className="widget-act"
                  title="Move up"
                  disabled={i === 0}
                  onClick={() => moveWidget(w.id, -1)}
                  whileTap={{ scale: 0.85 }}
                  transition={spring}
                >
                  ↑
                </motion.button>
                <motion.button
                  type="button"
                  className="widget-act"
                  title="Move down"
                  disabled={i === widgets.length - 1}
                  onClick={() => moveWidget(w.id, 1)}
                  whileTap={{ scale: 0.85 }}
                  transition={spring}
                >
                  ↓
                </motion.button>
                <motion.button
                  type="button"
                  className="widget-act widget-act-remove"
                  title="Remove widget"
                  onClick={() => removeWidget(w.id)}
                  whileTap={{ scale: 0.85 }}
                  transition={spring}
                >
                  ✕
                </motion.button>
              </div>
            </div>
            <div className="widget-body">
              {w.type === "media" && <MediaWidget media={media} />}
              {w.type === "clock" && <Clock expanded />}
              {w.type === "weather" && (
                <WeatherWidget
                  settings={w.weather}
                  edit={edit}
                  onSave={(weather) => saveSettings(w.id, { weather })}
                  onEditDone={configureAll ? undefined : () => setEditingId(null)}
                />
              )}
              {w.type === "launcher" && (
                <LauncherWidget
                  settings={w.launcher}
                  edit={edit}
                  onSave={(launcher) => saveSettings(w.id, { launcher })}
                />
              )}
              {w.type === "notes" && (
                <NotesWidget
                  settings={w.notes}
                  edit={edit}
                  onSave={(notes) => saveSettings(w.id, { notes })}
                />
              )}
              {w.type === "system" && (
                <SystemWidget
                  settings={w.system}
                  edit={edit}
                  onSave={(system) =>
                    saveSettings(w.id, { system: { ...(w.system ?? {}), ...system } })
                  }
                />
              )}
            </div>
            </motion.div>
          );
        })}
      </AnimatePresence>
    </div>
  );
}