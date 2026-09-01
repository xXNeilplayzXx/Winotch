import { useEffect, useRef, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { getCurrentWindow } from "@tauri-apps/api/window";
import {
  animateIsland,
  showIsland,
  sizesForDisplay,
  type IslandShape,
} from "../lib/island";
import {
  clearMonitorCache,
  getMonitors,
  readMonitorPref,
  saveMonitorPref,
  type MonitorInfo,
  type MonitorPref,
} from "../lib/monitors";
import { readSizePref, saveSizePref, type IslandSize } from "../lib/size";
import { readThemePref, themeManager, type ThemePref } from "../lib/theme";
import { readWidgets, saveWidgets, type WidgetInstance } from "../lib/widgets";
import { WidgetStack } from "./WidgetStack";
import { Clock } from "./widgets/Clock";
import type { MediaState } from "./widgets/MediaWidget";

type Mode = "collapsed" | "expanded";

/** Pending settings values, staged by the controls until Apply is pressed. */
interface SettingsDraft {
  shape: IslandShape;
  monitor: MonitorPref;
  glass: boolean;
  theme: ThemePref;
  size: IslandSize;
}

/**
 * Resolve, against the already-loaded monitor list, the display this window
 * anchors to (mirrors `resolveActiveMonitor` in island.ts, but synchronous so
 * the settings can seed slider defaults from state it already holds).
 */
function monitorForPref(list: MonitorInfo[], pref: MonitorPref): MonitorInfo | null {
  if (pref === "all") {
    const m = /^island-(\d+)$/.exec(getCurrentWindow().label);
    const idx = m ? parseInt(m[1], 10) : 0;
    return list[Math.min(idx, list.length - 1)] ?? null;
  }
  if (pref.startsWith("mon:")) {
    return list.find((mm) => mm.device === pref.slice(4)) ?? null;
  }
  return list.find((mm) => mm.isPrimary) ?? list[0] ?? null;
}

/** Snap to the slider steps (width 10, height 5) so label and thumb agree. */
function snapToSliderSteps(size: IslandSize): IslandSize {
  return {
    width: Math.round(size.width / 10) * 10,
    height: Math.round(size.height / 5) * 5,
  };
}

/** The auto-derived collapsed size for a shape on the given display pref. */
function derivedCollapsedSize(
  shape: IslandShape,
  list: MonitorInfo[],
  pref: MonitorPref,
): IslandSize {
  const mon = monitorForPref(list, pref);
  const widthPoints = mon ? Math.round(mon.width / (mon.scale || 1)) : 1710;
  const sizes = sizesForDisplay(widthPoints);
  const size = shape === "notch" ? sizes.notchCollapsed : sizes.pillCollapsed;
  return snapToSliderSteps(size);
}

/** The collapsed size currently in effect: the custom pref, else derived. */
function currentCollapsedSize(shape: IslandShape, list: MonitorInfo[]): IslandSize {
  const custom = readSizePref();
  return custom
    ? snapToSliderSteps(custom)
    : derivedCollapsedSize(shape, list, readMonitorPref());
}

function MusicNote({ size = 20 }: { size?: number }) {
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
      <path d="M9 18V5l12-2v13" />
      <circle cx="6" cy="18" r="3" />
      <circle cx="18" cy="16" r="3" />
    </svg>
  );
}
function PlayIcon({ size = 14 }: { size?: number }) {
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} fill="currentColor">
      <path d="M8 5v14l11-7z" />
    </svg>
  );
}
function PauseIcon({ size = 14 }: { size?: number }) {
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} fill="currentColor">
      <path d="M7 5h4v14H7zM13 5h4v14h-4z" />
    </svg>
  );
}
function SettingsIcon() {
  return (
    <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
      <circle cx="12" cy="12" r="3" />
      <path d="M19.4 15a1.7 1.7 0 0 0 .34 1.87l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.7 1.7 0 0 0-1.87-.34 1.7 1.7 0 0 0-1 1.55V21a2 2 0 1 1-4 0v-.09a1.7 1.7 0 0 0-1-1.55 1.7 1.7 0 0 0-1.87.34l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.7 1.7 0 0 0-.34-1.87 1.7 1.7 0 0 0-1.55-1H3a2 2 0 1 1 0-4h.09a1.7 1.7 0 0 0 1.55-1 1.7 1.7 0 0 0-.34-1.87l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.7 1.7 0 0 0 1.87.34h.09a1.7 1.7 0 0 0 1-1.55V3a2 2 0 1 1 4 0v.09a1.7 1.7 0 0 0 1 1.55 1.7 1.7 0 0 0 1.87-.34l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.7 1.7 0 0 0-.34 1.87v.09a1.7 1.7 0 0 0 1.55 1H21a2 2 0 1 1 0 4h-.09a1.7 1.7 0 0 0-1.55 1z" />
    </svg>
  );
}

/** Send a transport command to the active SMTC session (fire-and-forget). */
function mediaAct(cmd: "media_play_pause" | "media_next" | "media_previous") {
  return (e: React.MouseEvent) => {
    e.stopPropagation();
    void invoke(cmd).catch(() => {});
  };
}

/**
 * Mini now-playing bar that REPLACES the collapsed pill content the moment a
 * media session appears — the pill becomes a live activity widget without any
 * interaction. Reverts to brand/clock when the session goes away. Only active
 * while the media widget is part of the layout.
 */
function MiniNowPlaying({ media }: { media: MediaState }) {
  const dur = media.duration > 0 ? media.duration : 0;
  const pos = dur > 0 ? Math.min(Math.max(media.position, 0), dur) : 0;
  const progress = dur > 0 ? Math.min(1, pos / dur) : 0;

  return (
    <>
      <div className="np-mini" data-playing={media.playing}>
        <span className="island-dot" />
        <span className="npm-art" aria-hidden>
          {media.art ? <img src={media.art} alt="" draggable={false} /> : <MusicNote size={14} />}
        </span>
        <span className="npm-text">
          <span className="npm-title">{media.title}</span>
          <span className="npm-artist">{media.artist || media.album || ""}</span>
        </span>
      </div>
      <div className="npm-controls">
        <button
          type="button"
          className="np-ctl np-ctl-sm np-ctl-main"
          title={media.playing ? "Pause" : "Play"}
          onClick={mediaAct("media_play_pause")}
        >
          {media.playing ? <PauseIcon size={11} /> : <PlayIcon size={11} />}
        </button>
        <button type="button" className="np-ctl np-ctl-xs" title="Next" onClick={mediaAct("media_next")}>
          <svg viewBox="0 0 24 24" width={10} height={10} fill="currentColor">
            <path d="M16 6h2v12h-2zM4 6l9 6-9 6z" />
          </svg>
        </button>
      </div>
      <span className="np-mini-progress" style={{ width: `${Math.round(progress * 100)}%` }} />
    </>
  );
}

export function Island({ initialShape = "notch" }: { initialShape?: IslandShape }) {
  const [mode, setMode] = useState<Mode>("collapsed");
  const [shape, setShape] = useState<IslandShape>(initialShape);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [widgets, setWidgets] = useState<WidgetInstance[]>(() => {
    const list = readWidgets();
    // First run / upgrade: persist the default layout so it syncs across windows.
    try {
      if (localStorage.getItem("winotch.widgets") === null) saveWidgets(list);
    } catch {
      /* storage unavailable */
    }
    return list;
  });
  const [media, setMedia] = useState<MediaState | null>(null);
  const [glass, setGlass] = useState<boolean>(() => {
    try {
      return localStorage.getItem("winotch.glass") !== "off";
    } catch {
      return true;
    }
  });
  const [themePref, setThemePref] = useState<ThemePref>(() => readThemePref());
  // Custom collapsed size (settings sliders); null = derive from the display.
  const [appliedSize, setAppliedSize] = useState<IslandSize | null>(() => readSizePref());
  // Staged settings values, only applied (and persisted) when Apply is pressed.
  const [draft, setDraft] = useState<SettingsDraft | null>(null);
  const modeRef = useRef<Mode>("collapsed");
  const [monitors, setMonitors] = useState<MonitorInfo[]>([]);
  const [ctxMenu, setCtxMenu] = useState<{ x: number; y: number } | null>(null);

  // Right-click menu: close when clicking anywhere outside it (or pressing
  // Escape), so it never lingers over the webview.
  useEffect(() => {
    if (!ctxMenu) return;
    const onPointerDown = (e: PointerEvent) => {
      if (!(e.target as HTMLElement | null)?.closest?.(".ctx-menu")) setCtxMenu(null);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setCtxMenu(null);
    };
    window.addEventListener("pointerdown", onPointerDown);
    window.addEventListener("keydown", onKey);
    return () => {
      window.removeEventListener("pointerdown", onPointerDown);
      window.removeEventListener("keydown", onKey);
    };
  }, [ctxMenu]);

  /** Extra expanded-window height so several widget cards never overflow. */
  const widgetExtra = widgets.length > 1 ? Math.min((widgets.length - 1) * 52, 208) : 0;

  const hasMediaWidget = widgets.some((w) => w.type === "media");
  const mediaLive = Boolean(media?.playing) && hasMediaWidget;

  // Place the island collapsed for the current shape, then reveal on mount.
  useEffect(() => {
    import("../lib/island")
      .then(({ showIsland }) => showIsland(shape))
      .catch((err) => console.error("island positioning failed", err));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Display list for the settings picker (hidden on single-display systems).
  useEffect(() => {
    let disposed = false;
    void getMonitors()
      .then((m) => {
        if (!disposed) setMonitors(m);
      })
      .catch(() => {});
    return () => {
      disposed = true;
    };
  }, []);

  // Live media feed: pull initial state, then follow media://state events.
  // Off Windows the watcher is a no-op, so this stays on the empty state.
  useEffect(() => {
    let disposed = false;
    let unlisten: (() => void) | undefined;
    void invoke<MediaState | null>("media_get_state")
      .then((s) => {
        if (!disposed) setMedia(s ?? null);
      })
      .catch(() => {});
    void listen<MediaState | null>("media://state", (e) => {
      if (!disposed) setMedia(e.payload ?? null);
    })
      .then((u) => {
        if (disposed) u();
        else unlisten = u;
      })
      .catch(() => {});
    return () => {
      disposed = true;
      unlisten?.();
    };
  }, []);

  // Cross-window sync for multi-monitor mode: when shape/appearance/widgets
  // change on one island (Rust relays it to EVERY window), the others conform.
  // The emitting window is expanded and skips the animation (it already did).
  useEffect(() => {
    let disposed = false;
    let unlisten: (() => void) | undefined;
    void listen<{
      shape: IslandShape;
      glass: boolean;
      widgets?: WidgetInstance[];
      size?: IslandSize | null;
    }>("winotch://config", (e) => {
      if (disposed) return;
      const p = e.payload;
      if (p.widgets) setWidgets(p.widgets);
      if (p.size !== undefined) setAppliedSize(p.size ?? null);
      setShape(p.shape);
      setGlass(p.glass);
      if (modeRef.current === "collapsed") {
        void animateIsland({ shape: p.shape, expanded: false });
      }
    })
      .then((u) => {
        if (disposed) u();
        else unlisten = u;
      })
      .catch(() => {});
    return () => {
      disposed = true;
      unlisten?.();
    };
  }, []);

  // Grow/shrink the expanded window when the widget count changes.
  const firstWidgetsRender = useRef(true);
  useEffect(() => {
    if (firstWidgetsRender.current) {
      firstWidgetsRender.current = false;
      return;
    }
    if (modeRef.current === "expanded") {
      void animateIsland({ shape, expanded: true, settingsOpen, extraHeight: widgetExtra });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [widgets.length]);

  const enter = () => {
    if (modeRef.current === "expanded") return;
    modeRef.current = "expanded";
    setMode("expanded");
    // Carry the settings-open bonus so a re-expansion while settings is still
    // active keeps the taller window and nothing overflows.
    void animateIsland({ shape, expanded: true, settingsOpen, extraHeight: widgetExtra });
  };

  const leave = () => {
    if (modeRef.current === "collapsed") return;
    modeRef.current = "collapsed";
    setMode("collapsed");
    void animateIsland({ shape, expanded: false });
  };

  /** Discard the draft and re-seed it from the currently applied config. */
  const resetDraft = () => {
    if (!draft) return;
    setDraft({
      shape,
      monitor: readMonitorPref(),
      glass,
      theme: themePref,
      size: currentCollapsedSize(shape, monitors),
    });
  };

  /**
   * Persist and apply a concrete settings object, then re-place the OS window
   * when the geometry (shape / size / monitor) changed. Theme and appearance
   * apply in place; geometry changes collapse the island so the new bar is
   * immediately visible, exactly like the old instant-apply shape switch.
   */
  const commitSettings = (d: SettingsDraft, opts?: { resetSizeToAuto?: boolean }) => {
    // Normalize a stale draft monitor (e.g. a display unplugged while the
    // panel was open) back to a selectable value before persisting it.
    const monitor = monitorSelectValue(d.monitor) as MonitorPref;
    const shapeChanged = d.shape !== shape;
    const glassChanged = d.glass !== glass;
    const themeChanged = d.theme !== themePref;
    const monitorChanged = monitor !== readMonitorPref();
    const prevCustom = readSizePref();
    // Resetting to auto clears the custom size, so it's only a "change" when
    // a custom size is currently stored; otherwise compare values as usual.
    const sizeChanged = opts?.resetSizeToAuto
      ? prevCustom !== null
      : !prevCustom || prevCustom.width !== d.size.width || prevCustom.height !== d.size.height;

    // Everything is persisted under its existing key / manager.
    if (shapeChanged) {
      setShape(d.shape);
      try {
        localStorage.setItem("winotch.shape", d.shape);
      } catch {
        /* storage unavailable; keep session-only */
      }
    }
    if (glassChanged) {
      setGlass(d.glass);
      try {
        localStorage.setItem("winotch.glass", d.glass ? "on" : "off");
      } catch {
        /* storage unavailable; keep session-only */
      }
    }
    if (opts?.resetSizeToAuto) {
      saveSizePref(null);
      setAppliedSize(null);
    } else {
      saveSizePref({ width: d.size.width, height: d.size.height });
      setAppliedSize({ width: d.size.width, height: d.size.height });
    }
    saveMonitorPref(monitor);
    setThemePref(d.theme);
    if (themeChanged) themeManager.setPref(d.theme);

    const reposition = shapeChanged || sizeChanged || monitorChanged;
    if (reposition) {
      // Collapse so the user immediately sees the new bar size/shape/monitor.
      modeRef.current = "collapsed";
      setMode("collapsed");
      setSettingsOpen(false);
      setDraft(null);
      if (monitor === "all") {
        void invoke("spawn_island_windows").catch(() => {});
      } else {
        void invoke("close_island_windows").catch(() => {});
      }
      clearMonitorCache();
      void showIsland(d.shape)
        .then(() => {
          if (monitor !== "all") {
            // Re-assert placement on the chosen display (backend geometry).
            void invoke("move_notch_to_monitor", {
              monitorName: monitor === "primary" ? "primary" : monitor.slice(4),
            }).catch(() => {});
          }
          // Re-assert the exact logical size (backend geometry).
          void invoke("update_notch_size", {
            width: d.size.width,
            height: d.size.height,
          }).catch(() => {});
        })
        .catch(() => {});
    } else {
      // Theme/appearance-only change: nothing to move. Keep the panel open
      // (and the draft — it now equals the applied config).
    }

    broadcastConfig({
      shape: d.shape,
      glass: d.glass,
      size: opts?.resetSizeToAuto ? null : { width: d.size.width, height: d.size.height },
    });
    // Keep the seeded draft aligned with what was just applied (monitor
    // normalization, size snap) so a follow-up Apply is a true no-op.
    setDraft({ ...d, monitor, size: { ...d.size } });
  };

  /** Apply the staged draft (the settings panel's Apply button). */
  const applySettings = () => {
    if (!draft) return;
    commitSettings(draft);
  };

  /**
   * Restore factory defaults immediately: Notch mode, auto-derived size,
   * primary monitor, Glass appearance, System Default theme.
   */
  const applyDefaults = () => {
    const defaults: SettingsDraft = {
      shape: "notch",
      monitor: "primary",
      glass: true,
      theme: "system",
      size: derivedCollapsedSize("notch", monitors, "primary"),
    };
    setDraft(defaults);
    commitSettings(defaults, { resetSizeToAuto: true });
  };

  /** Current pref, rewritten so it always matches a <select> option. */
  const monitorSelectValue = (pref: MonitorPref): string => {
    if (pref === "all") return "all";
    if (pref.startsWith("mon:")) {
      const dev = pref.slice(4);
      if (monitors.some((m) => m.device === dev)) return pref;
    }
    return "primary";
  };

  /** Send the current (or overridden) config to every island window. */
  const broadcastConfig = (override?: {
    shape?: IslandShape;
    glass?: boolean;
    widgets?: WidgetInstance[];
    size?: IslandSize | null;
  }) => {
    void invoke("broadcast_config", {
      config: {
        shape: override?.shape ?? shape,
        glass: override?.glass ?? glass,
        widgets: override?.widgets ?? widgets,
        size: override?.size !== undefined ? override.size : appliedSize,
      },
    }).catch(() => {});
  };

  const updateWidgetList = (next: WidgetInstance[]) => {
    setWidgets(next);
    saveWidgets(next);
    broadcastConfig({ widgets: next });
  };

  const toggleSettings = (e: React.MouseEvent) => {
    e.stopPropagation();
    const next = !settingsOpen;
    setSettingsOpen(next);
    if (next) {
      // Seed the draft from the currently applied configuration so sliders and
      // selects always open showing the real values.
      setDraft({
        shape,
        monitor: readMonitorPref(),
        glass,
        theme: themePref,
        size: currentCollapsedSize(shape, monitors),
      });
    }
    // Grow the expanded window while settings are visible so nothing overflows.
    void animateIsland({ shape, expanded: true, settingsOpen: next, extraHeight: widgetExtra });
  };

  return (
    <div
      className={`island shape-${shape} mode-${mode}${glass ? " glass" : ""}${
        mediaLive ? " media-live" : ""
      }`}
      onMouseEnter={enter}
      onMouseLeave={leave}
      onContextMenu={(e) => {
        e.preventDefault();
        // Keep the menu fully inside the small island window (menus can now
        // have two items, so reserve ~84px of height).
        setCtxMenu({
          x: Math.max(0, Math.min(e.clientX, window.innerWidth - 180)),
          y: Math.max(0, Math.min(e.clientY, window.innerHeight - 84)),
        });
      }}
    >
      <header className="island-strip">
        {mediaLive && media?.title ? (
          <MiniNowPlaying media={media} />
        ) : (
          <>
            <div className="island-brand">
              <span className="island-dot" />
              <span className="island-name">Winotch</span>
            </div>
            <div className="island-strip-slot">
              <Clock expanded={mode === "expanded"} />
            </div>
          </>
        )}
      </header>

      <section className="island-panel">
        <div className="panel-head">
          <div className="panel-head-left">
            <button
              type="button"
              className={`icon-btn ${settingsOpen ? "icon-btn-active" : ""}`}
              title="Settings"
              onClick={toggleSettings}
            >
              <SettingsIcon />
            </button>
            <span className="panel-title">{shape === "notch" ? "Notch mode" : "Pill mode"}</span>
          </div>
        </div>

        {settingsOpen && draft && (
          <div className="settings">
            <div className="settings-row">
              <span className="settings-label">Mode</span>
              <div className="segmented">
                <button
                  type="button"
                  className={draft.shape === "notch" ? "seg-active" : ""}
                  onClick={(e) => {
                    e.stopPropagation();
                    setDraft({ ...draft, shape: "notch" });
                  }}
                >
                  Notch
                </button>
                <button
                  type="button"
                  className={draft.shape === "pill" ? "seg-active" : ""}
                  onClick={(e) => {
                    e.stopPropagation();
                    setDraft({ ...draft, shape: "pill" });
                  }}
                >
                  Pill
                </button>
              </div>
            </div>
            {monitors.length > 1 && (
              <div className="settings-row">
                <span className="settings-label">Display</span>
                <select
                  className="settings-select"
                  value={monitorSelectValue(draft.monitor)}
                  onChange={(e) =>
                    setDraft({ ...draft, monitor: e.target.value as MonitorPref })
                  }
                >
                  {monitors.map((m) => (
                    <option key={m.device} value={m.isPrimary ? "primary" : `mon:${m.device}`}>
                      {m.isPrimary ? "Main monitor" : m.name}
                    </option>
                  ))}
                  <option value="all">All monitors</option>
                </select>
              </div>
            )}
            <div className="settings-row settings-row-slider">
              <div className="settings-slider-head">
                <span className="settings-label">Notch Width</span>
                <span className="settings-slider-value">{draft.size.width}px</span>
              </div>
              <input
                type="range"
                min="150"
                max="800"
                step="10"
                value={draft.size.width}
                onChange={(e) =>
                  setDraft({ ...draft, size: { ...draft.size, width: Number(e.target.value) } })
                }
              />
            </div>
            <div className="settings-row settings-row-slider">
              <div className="settings-slider-head">
                <span className="settings-label">Notch Height</span>
                <span className="settings-slider-value">{draft.size.height}px</span>
              </div>
              <input
                type="range"
                min="30"
                max="200"
                step="5"
                value={draft.size.height}
                onChange={(e) =>
                  setDraft({ ...draft, size: { ...draft.size, height: Number(e.target.value) } })
                }
              />
            </div>
            <div className="settings-row">
              <span className="settings-label">Appearance</span>
              <div className="segmented">
                <button
                  type="button"
                  className={!draft.glass ? "seg-active" : ""}
                  onClick={(e) => {
                    e.stopPropagation();
                    setDraft({ ...draft, glass: false });
                  }}
                >
                  Solid
                </button>
                <button
                  type="button"
                  className={draft.glass ? "seg-active" : ""}
                  onClick={(e) => {
                    e.stopPropagation();
                    setDraft({ ...draft, glass: true });
                  }}
                >
                  Glass
                </button>
              </div>
            </div>
            <div className="settings-row">
              <span className="settings-label">Theme</span>
              <div className="segmented segmented-3">
                <button
                  type="button"
                  title="Force the light theme everywhere"
                  className={draft.theme === "light" ? "seg-active" : ""}
                  onClick={(e) => {
                    e.stopPropagation();
                    setDraft({ ...draft, theme: "light" });
                  }}
                >
                  Light
                </button>
                <button
                  type="button"
                  title="Force the dark theme everywhere"
                  className={draft.theme === "dark" ? "seg-active" : ""}
                  onClick={(e) => {
                    e.stopPropagation();
                    setDraft({ ...draft, theme: "dark" });
                  }}
                >
                  Dark
                </button>
                <button
                  type="button"
                  title="Follow the Windows theme, live"
                  className={draft.theme === "system" ? "seg-active" : ""}
                  onClick={(e) => {
                    e.stopPropagation();
                    setDraft({ ...draft, theme: "system" });
                  }}
                >
                  System Default
                </button>
              </div>
            </div>
            <div className="settings-actions">
              <button
                type="button"
                className="settings-btn"
                onClick={(e) => {
                  e.stopPropagation();
                  resetDraft();
                }}
              >
                Reset
              </button>
              <button
                type="button"
                className="settings-btn settings-btn-primary"
                onClick={(e) => {
                  e.stopPropagation();
                  applySettings();
                }}
              >
                Apply
              </button>
            </div>
            <button
              type="button"
              className="settings-reset-default"
              onClick={(e) => {
                e.stopPropagation();
                applyDefaults();
              }}
            >
              Reset to default
            </button>
          </div>
        )}

        <WidgetStack widgets={widgets} media={media} onChange={updateWidgetList} />
      </section>

      {ctxMenu && (
        <div
          className="ctx-menu"
          style={{ left: ctxMenu.x, top: Math.max(0, Math.min(ctxMenu.y, window.innerHeight - 84)) }}
          onContextMenu={(e) => e.preventDefault()}
        >
          <button
            type="button"
            className="ctx-item"
            onClick={() => {
              setCtxMenu(null);
              void invoke("hide_island").catch(() => {});
            }}
          >
            <svg viewBox="0 0 24 24" width="11" height="11" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24" />
              <path d="M1 1l22 22" />
            </svg>
            Hide Winotch
          </button>
          <button
            type="button"
            className="ctx-item ctx-item-danger"
            onClick={() => {
              setCtxMenu(null);
              void invoke("quit_app").catch(() => {});
            }}
          >
            <svg viewBox="0 0 24 24" width="11" height="11" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
              <path d="M18 6 6 18M6 6l12 12" />
            </svg>
            Close Winotch
          </button>
        </div>
      )}
    </div>
  );
}