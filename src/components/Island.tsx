import { useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { getCurrentWindow } from "@tauri-apps/api/window";
import {
  animateIsland,
  showIsland,
  logicalTargetSize,
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
import { SysMiniBar } from "./widgets/SystemWidget";
import {
  applyGlassBlur,
  applyGlassOpacity,
  readGlassBlur,
  readGlassOpacity,
  saveGlassBlur,
  saveGlassOpacity,
  GLASS_BLUR_DEFAULT,
  GLASS_BLUR_MAX,
  GLASS_BLUR_MIN,
  GLASS_OPACITY_DEFAULT,
} from "../lib/glass";
import {
  GRADIENTS,
  readGradient,
  readIdleContent,
  readIdleText,
  saveGradient,
  saveIdleContent,
  saveIdleText,
  type GradientPref,
  type IdleContent,
} from "../lib/customize";
import {
  ACCENT_SYSTEM,
  readAccentPref,
  readThemePref,
  themeManager,
  type ThemePref,
} from "../lib/theme";
import { readWidgets, saveWidgets, type WidgetInstance } from "../lib/widgets";
import { WidgetStack } from "./WidgetStack";
import { Clock } from "./widgets/Clock";
import type { MediaState } from "./widgets/MediaWidget";

type Mode = "collapsed" | "expanded";

/**
 * Apply-feedback motion kinds. The pulse after pressing Apply mirrors the
 * setting that changed: size → grow/shrink, shape → tilt, theme/accent →
 * shimmer, glass → fade; anything else → the base zoom-out puff.
 */
type ApplyAnimKind = "zoom" | "grow" | "shrink" | "morph" | "pulse" | "fade";

/** Preset accent colors for the Color theme picker. */
const ACCENT_PRESETS = [
  "#e9b949", // brand gold
  "#2f6fed", // blue
  "#10b981", // green
  "#06b6d4", // cyan
  "#8b5cf6", // violet
  "#ec4899", // pink
  "#ef4444", // red
  "#f97316", // orange
];

/** Quick size presets for the settings sliders (one click applies both axes). */
const SIZE_PRESETS = [
  { label: "Compact", width: 240, height: 60 },
  { label: "Standard", width: 300, height: 75 },
  { label: "Wide", width: 460, height: 95 },
];

/**
 * Shared spring feel for the island morph. Deliberately a soft, slow spring so
 * the notch "breathes" open and closed like a Dynamic Island instead of
 * snapping or feeling rushed (stiffness/damping tuned down from 300/30 to
 * 170/24 for a noticeably slower settle).
 */
const MORPH_SPRING = { type: "spring", stiffness: 170, damping: 24 } as const;

/** Quick micro-interaction spring for buttons and controls. */
const TAP_SPRING = { type: "spring", stiffness: 500, damping: 30 } as const;

/** Pending settings values, staged by the controls until Apply is pressed. */
interface SettingsDraft {
  shape: IslandShape;
  monitor: MonitorPref;
  glass: boolean;
  /** Liquid-glass opacity percent (only meaningful in glass mode). */
  glassOpacity: number;
  /** Liquid-glass backdrop blur in px (only meaningful in glass mode). */
  glassBlur: number;
  /** Background gradient skin (`none` disables it). */
  gradient: GradientPref;
  /** What the collapsed strip shows while idle. */
  idleContent: IdleContent;
  idleText: string;
  theme: ThemePref;
  /** `system` (follow Windows) or an explicit `#rrggbb` accent hex. */
  accent: string;
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

/** Shape of the Rust call://state payload (calls.rs). */
interface CallState {
  on_call: boolean;
  app?: string | null;
}

/** Shape of the Rust mic://state payload (mic.rs). */
interface MicState {
  on: boolean;
}

function MicIcon({ size = 14 }: { size?: number }) {
  return (
    <svg
      viewBox="0 0 24 24"
      width={size}
      height={size}
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <path d="M12 1a3 3 0 0 0-3 3v8a3 3 0 0 0 6 0V4a3 3 0 0 0-3-3z" />
      <path d="M19 10v2a7 7 0 0 1-14 0v-2" />
      <line x1="12" y1="19" x2="12" y2="23" />
    </svg>
  );
}

/** Elapsed call time as `m:ss` (or `h:mm:ss` past an hour). */
function formatCallTime(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000));
  const s = total % 60;
  const m = Math.floor(total / 60) % 60;
  const h = Math.floor(total / 3600);
  const pad = (n: number) => String(n).padStart(2, "0");
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`;
}

/**
 * Collapsed live pill shown while a call is detected: green mic, the hosting
 * app and a ticking elapsed timer. Replaces the now-playing/brand content for
 * the duration of the call, no interaction required.
 */
/**
 * Collapsible settings group — a labeled header with a chevron that hides or
 * shows its rows. Keeps the panel scannable instead of one flat list.
 */
function SettingsSection({
  title,
  subtitle,
  open,
  onToggle,
  children,
}: {
  title: string;
  subtitle?: string;
  open: boolean;
  onToggle: () => void;
  children: ReactNode;
}) {
  return (
    <div className="settings-section">
      <motion.button
        type="button"
        className="settings-section-head"
        aria-expanded={open}
        onClick={(e) => {
          e.stopPropagation();
          onToggle();
        }}
        whileTap={{ scale: 0.98 }}
        transition={TAP_SPRING}
      >
        <span className="settings-section-titles">
          <span className="settings-section-title">{title}</span>
          {subtitle && <span className="settings-section-sub">{subtitle}</span>}
        </span>
        <svg
          className={`settings-chevron${open ? "" : " settings-chevron-closed"}`}
          viewBox="0 0 24 24"
          width="12"
          height="12"
          fill="none"
          stroke="currentColor"
          strokeWidth="2.5"
          strokeLinecap="round"
          strokeLinejoin="round"
          aria-hidden
        >
          <path d="M6 9l6 6 6-6" />
        </svg>
      </motion.button>
      {open && <div className="settings-section-body">{children}</div>}
    </div>
  );
}

function CallPill({ app, elapsedMs }: { app: string; elapsedMs: number }) {
  return (
    <div className="call-mini">
      <span className="callm-icon" aria-hidden>
        <MicIcon size={14} />
      </span>
      <span className="callm-text">
        <span className="callm-app">{app}</span>
        <span className="callm-status">On call · {formatCallTime(elapsedMs)}</span>
      </span>
      <span className="callm-live" aria-hidden title="In a call" />
    </div>
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
  const [call, setCall] = useState<CallState | null>(null);
  const [micOn, setMicOn] = useState(false);
  // Ticking clock behind the call pill's elapsed timer.
  const [callNow, setCallNow] = useState(0);
  const callStartRef = useRef<number | null>(null);
  const [glass, setGlass] = useState<boolean>(() => {
    try {
      return localStorage.getItem("winotch.glass") !== "off";
    } catch {
      return true;
    }
  });
  const [themePref, setThemePref] = useState<ThemePref>(() => readThemePref());
  // Liquid-glass opacity percent; applies live as a CSS variable.
  const [glassOpacity, setGlassOpacity] = useState<number>(() => readGlassOpacity());
  // Liquid-glass backdrop blur px; applies live as a CSS variable.
  const [glassBlur, setGlassBlur] = useState<number>(() => readGlassBlur());
  // Background gradient skin.
  const [gradient, setGradient] = useState<GradientPref>(() => readGradient());
  // What the collapsed strip shows while idle.
  const [idleContent, setIdleContent] = useState<IdleContent>(() => readIdleContent());
  const [idleText, setIdleText] = useState<string>(() => readIdleText());
  // Which settings sections are expanded (default: all of them).
  const [sectionOpen, setSectionOpen] = useState<Record<string, boolean>>({
    shape: true,
    size: true,
    appearance: true,
    theme: true,
    idle: true,
  });
  // Brief "Applied ✓" confirmation on the Apply button after a save.
  const [appliedFlash, setAppliedFlash] = useState(false);
  const flashTimer = useRef<number | null>(null);
  // Root element, targeted by the Apply feedback animation below.
  const islandRef = useRef<HTMLDivElement | null>(null);
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
  const callLive = Boolean(call?.on_call);
  // The collapsed bar becomes a live CPU/RAM/battery mini readout when the
  // "When idle" setting is System stats (call and media still take priority).
  // Per-metric visibility comes from the first System widget's settings.
  const showSysMini = idleContent === "system";
  const sysSettings = widgets.find((w) => w.type === "system")?.system;
  // Status-dot state: grey idle, red mic in use, green on call, blue both.
  const dotState: "both" | "mic" | "call" | "idle" =
    micOn && callLive ? "both" : micOn ? "mic" : callLive ? "call" : "idle";

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

  // Live call feed: pull initial state, then follow call://state events.
  // Off Windows the watcher is a no-op, so this stays off.
  useEffect(() => {
    let disposed = false;
    let unlisten: (() => void) | undefined;
    void invoke<CallState | null>("call_get_state")
      .then((s) => {
        if (!disposed) setCall(s ?? null);
      })
      .catch(() => {});
    void listen<CallState | null>("call://state", (e) => {
      if (!disposed) setCall(e.payload ?? null);
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

  // Live mic-in-use feed: pull initial state, then follow mic://state events.
  // Off Windows the watcher is a no-op, so the dot stays grey.
  useEffect(() => {
    let disposed = false;
    let unlisten: (() => void) | undefined;
    void invoke<MicState | null>("mic_get_state")
      .then((s) => {
        if (!disposed) setMicOn(s?.on ?? false);
      })
      .catch(() => {});
    void listen<boolean>("mic://state", (e) => {
      if (!disposed) setMicOn(Boolean(e.payload));
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

  // Tick the elapsed timer once a second while the call pill is showing;
  // the start instant is anchored the first time the call is seen.
  useEffect(() => {
    if (!call?.on_call) {
      callStartRef.current = null;
      setCallNow(0);
      return;
    }
    if (callStartRef.current === null) callStartRef.current = Date.now();
    setCallNow(Date.now());
    const id = window.setInterval(() => setCallNow(Date.now()), 1000);
    return () => window.clearInterval(id);
  }, [call?.on_call]);

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
      glassOpacity?: number;
      glassBlur?: number | null;
      gradient?: GradientPref;
      idleContent?: IdleContent;
      idleText?: string;
      accent?: string;
    }>("winotch://config", (e) => {
      if (disposed) return;
      const p = e.payload;
      if (p.widgets) setWidgets(p.widgets);
      if (p.size !== undefined) setAppliedSize(p.size ?? null);
      if (p.glassOpacity !== undefined) setGlassOpacity(p.glassOpacity);
      if (p.glassBlur !== undefined && p.glassBlur !== null) setGlassBlur(p.glassBlur);
      if (p.gradient !== undefined) setGradient(p.gradient);
      if (p.idleContent !== undefined) setIdleContent(p.idleContent);
      if (p.idleText !== undefined) setIdleText(p.idleText);
      if (p.accent !== undefined) themeManager.setAccentPref(p.accent);
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

  // Re-skin the island the instant glass opacity/blur changes (both themes).
  useLayoutEffect(() => {
    applyGlassOpacity(glassOpacity);
    applyGlassBlur(glassBlur);
  }, [glassOpacity, glassBlur]);

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
    // active keeps the taller window and nothing overflows. The visible morph
    // is driven by the motion.div spring; the window envelope is sized in ONE
    // shot (no per-frame window resizing).
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
      glassOpacity,
      glassBlur,
      gradient,
      idleContent,
      idleText,
      theme: themePref,
      accent: readAccentPref(),
      size: currentCollapsedSize(shape, monitors),
    });
  };

  /**
   * Persist and apply a concrete settings object, then re-place the OS window
   * when the geometry (shape / size / monitor) changed. Every change applies
   * IN PLACE: the island stays expanded and the settings panel stays open, and
   * the motion.div simply springs to the new geometry under the cursor, so
   * Apply never kicks the user out of the notch.
   */
  const commitSettings = (d: SettingsDraft, opts?: { resetSizeToAuto?: boolean }) => {
    const {
      monitor,
      shape: shapeChanged,
      glass: glassChanged,
      opacity: opacityChanged,
      blur: blurChanged,
      gradient: gradientChanged,
      idle: idleChanged,
      theme: themeChanged,
      accent: accentChanged,
      monitorChanged,
      size: sizeChanged,
    } = diffDraft(d, opts);

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
    if (opacityChanged) {
      saveGlassOpacity(d.glassOpacity);
      setGlassOpacity(d.glassOpacity); // effect re-skins --glass-opacity
    }
    if (blurChanged) {
      saveGlassBlur(d.glassBlur);
      setGlassBlur(d.glassBlur); // effect re-skins --glass-blur
    }
    if (gradientChanged) {
      saveGradient(d.gradient);
      setGradient(d.gradient);
    }
    if (idleChanged) {
      saveIdleContent(d.idleContent);
      saveIdleText(d.idleText);
      setIdleContent(d.idleContent);
      setIdleText(d.idleText);
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
    if (accentChanged) themeManager.setAccentPref(d.accent);

    const reposition = shapeChanged || sizeChanged || monitorChanged;
    if (reposition) {
      // Apply in place: keep the island expanded and the settings panel open,
      // and simply re-place the window envelope to the new geometry. The
      // motion.div springs to the new size under the cursor (driven by the
      // state updates above), and the panel stays open so tweaks can continue.
      if (monitor === "all") {
        void invoke("spawn_island_windows").catch(() => {});
      } else {
        void invoke("close_island_windows").catch(() => {});
      }
      clearMonitorCache();
      void (async () => {
        if (monitor !== "all") {
          // Move the window to the chosen display (backend geometry).
          await invoke("move_notch_to_monitor", {
            monitorName: monitor === "primary" ? "primary" : monitor.slice(4),
          }).catch(() => {});
        }
        // Size the envelope to the new expanded geometry in one shot (the
        // webview is transparent, so this is invisible); the DOM spring
        // morphs in place. The settings-open bonus keeps the panel unclipped.
        // Note: update_notch_size is deliberately NOT called here — it would
        // physically shrink the window to the collapsed size and clip the
        // panel; the collapsed size is re-asserted when the user leaves.
        await animateIsland({
          shape: d.shape,
          expanded: true,
          settingsOpen: true,
          extraHeight: widgetExtra,
        }).catch(() => {});
      })();
    } else {
      // Theme/appearance-only change: nothing to move. Keep the panel open
      // (and the draft — it now equals the applied config).
    }

    broadcastConfig({
      shape: d.shape,
      glass: d.glass,
      glassOpacity: d.glassOpacity,
      glassBlur: d.glassBlur,
      gradient: d.gradient,
      idleContent: d.idleContent,
      idleText: d.idleText,
      accent: d.accent,
      size: opts?.resetSizeToAuto ? null : { width: d.size.width, height: d.size.height },
    });
    // Keep the seeded draft aligned with what was just applied (monitor
    // normalization, size snap) so a follow-up Apply is a true no-op.
    setDraft({ ...d, monitor, size: { ...d.size } });
  };

  /**
   * One-shot feedback on Apply: a quick zoom-out puff on the island content
   * whose motion matches the setting that changed (see ApplyAnimKind). Uses the
   * Web Animations API so it never fights the CSS mount animation on .island.
   */
  const playApplyAnimation = (kind: ApplyAnimKind) => {
    const el = islandRef.current;
    if (!el) return;
    const dur = 520;
    const ease = "cubic-bezier(0.22, 1, 0.36, 1)";
    const sets: Record<ApplyAnimKind, Keyframe[]> = {
      // Base zoom-out: content breathes down, then settles back.
      zoom: [
        { transform: "scale(0.98)", opacity: 1 },
        { transform: "scale(0.965)", opacity: 0.88 },
        { transform: "scale(1)", opacity: 1 },
      ],
      // Size increased: springy grow from smaller.
      grow: [
        { transform: "scale(0.93)", opacity: 0.9 },
        { transform: "scale(1.012)", opacity: 1 },
        { transform: "scale(1)" },
      ],
      // Size decreased: squash in, then breathe out at the new size.
      shrink: [
        { transform: "scale(1.02)" },
        { transform: "scale(0.965)", opacity: 0.9 },
        { transform: "scale(1)", opacity: 1 },
      ],
      // Shape switched (pill ↔ notch): a quick depth tilt.
      morph: [
        { transform: "perspective(320px) rotateX(0deg) scale(1)" },
        { transform: "perspective(320px) rotateX(8deg) scale(0.97)", opacity: 0.95 },
        { transform: "perspective(320px) rotateX(0deg) scale(1)" },
      ],
      // Theme/accent changed: brightness shimmer.
      pulse: [
        { filter: "brightness(1)" },
        { filter: "brightness(1.18)" },
        { filter: "brightness(1)" },
      ],
      // Glass appearance changed: soft fade through the surface.
      fade: [
        { opacity: 0.9 },
        { opacity: 0.82 },
        { opacity: 1 },
      ],
    };
    el.animate(sets[kind], { duration: dur, easing: ease });
  };

  /** Apply the staged draft (the settings panel's Apply button). */
  const applySettings = () => {
    if (!draft) return;
    const d = diffDraft(draft);
    // Current size read BEFORE commit (commit writes the new size to storage).
    const cur = currentCollapsedSize(shape, monitors);
    commitSettings(draft);
    // Confirmation flash so it's obvious the save went through.
    setAppliedFlash(true);
    if (flashTimer.current !== null) window.clearTimeout(flashTimer.current);
    flashTimer.current = window.setTimeout(() => setAppliedFlash(false), 1400);
    // Correlated feedback: the motion mirrors the change being applied.
    // d.size is a boolean change-flag; compare the staged draft's actual
    // dimensions against the applied size to pick grow vs. shrink.
    const kind: ApplyAnimKind = d.shape
      ? "morph"
      : d.size
        ? draft.size.width * draft.size.height >= cur.width * cur.height
          ? "grow"
          : "shrink"
        : d.theme || d.accent
          ? "pulse"
          : d.glass || d.opacity || d.blur || d.gradient
            ? "fade"
            : "zoom";
    playApplyAnimation(kind);
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
      glassOpacity: GLASS_OPACITY_DEFAULT,
      glassBlur: GLASS_BLUR_DEFAULT,
      gradient: "none",
      idleContent: "brand",
      idleText: "",
      theme: "system",
      accent: ACCENT_SYSTEM,
      size: derivedCollapsedSize("notch", monitors, "primary"),
    };
    setDraft(defaults);
    commitSettings(defaults, { resetSizeToAuto: true });
    // Restoring defaults is a reset: give the whole island a zoom-out puff.
    playApplyAnimation("zoom");
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

  /**
   * Compare a staged draft against the currently applied config — the single
   * source of truth for what Apply changes and whether there are unsaved edits.
   */
  const diffDraft = (d: SettingsDraft, opts?: { resetSizeToAuto?: boolean }) => {
    // Normalize a stale draft monitor (e.g. a display unplugged while the
    // panel was open) back to a selectable value before comparing/persisting.
    const monitor = monitorSelectValue(d.monitor) as MonitorPref;
    const prevCustom = readSizePref();
    return {
      monitor,
      shape: d.shape !== shape,
      glass: d.glass !== glass,
      opacity: d.glassOpacity !== glassOpacity,
      blur: d.glassBlur !== glassBlur,
      gradient: d.gradient !== gradient,
      idle: d.idleContent !== idleContent || d.idleText !== idleText,
      theme: d.theme !== themePref,
      accent: d.accent !== readAccentPref(),
      monitorChanged: monitor !== readMonitorPref(),
      // Resetting to auto clears the custom size, so it's only a "change" when
      // a custom size is currently stored; otherwise compare values as usual.
      size: opts?.resetSizeToAuto
        ? prevCustom !== null
        : !prevCustom || prevCustom.width !== d.size.width || prevCustom.height !== d.size.height,
    };
  };

  /** True while the staged draft differs from what's currently applied. */
  const dirty = useMemo(() => {
    if (!draft) return false;
    const d = diffDraft(draft);
    return (
      d.shape ||
      d.glass ||
      d.opacity ||
      d.blur ||
      d.gradient ||
      d.idle ||
      d.drag ||
      d.theme ||
      d.accent ||
      d.monitorChanged ||
      d.size
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [draft, shape, glass, glassOpacity, glassBlur, gradient, idleContent, idleText, themePref, monitors]);

  /** Send the current (or overridden) config to every island window. */
  const broadcastConfig = (override?: {
    shape?: IslandShape;
    glass?: boolean;
    widgets?: WidgetInstance[];
    size?: IslandSize | null;
    glassOpacity?: number;
    glassBlur?: number | null;
    gradient?: GradientPref;
    idleContent?: IdleContent;
    idleText?: string;
    accent?: string;
  }) => {
    void invoke("broadcast_config", {
      config: {
        shape: override?.shape ?? shape,
        glass: override?.glass ?? glass,
        widgets: override?.widgets ?? widgets,
        size: override?.size !== undefined ? override.size : appliedSize,
        glassOpacity: override?.glassOpacity ?? glassOpacity,
        glassBlur: override?.glassBlur ?? glassBlur,
        gradient: override?.gradient ?? gradient,
        idleContent: override?.idleContent ?? idleContent,
        idleText: override?.idleText ?? idleText,
        accent: override?.accent ?? readAccentPref(),
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
        glassOpacity,
        glassBlur,
        gradient,
        idleContent,
        idleText,
        theme: themePref,
        accent: readAccentPref(),
        size: currentCollapsedSize(shape, monitors),
      });
    }
    // Grow the expanded window while settings are visible so nothing overflows.
    void animateIsland({ shape, expanded: true, settingsOpen: next, extraHeight: widgetExtra });
  };

  // DOM-driven sizing (single source of truth with the window envelope): the
  // OS window is a transparent envelope large enough for the expanded state;
  // the motion.div below springs between these logical sizes. Widths derive
  // from the same display the envelope anchors to, so the element and window
  // always agree and nothing clips or leaves a gap.
  const islandMon = monitorForPref(monitors, readMonitorPref());
  const islandWidthPts = islandMon ? Math.round(islandMon.width / islandMon.scale) : 1710;
  const collapsedPx = currentCollapsedSize(shape, monitors);
  const expandedPx = logicalTargetSize(
    shape,
    true,
    { widthPoints: islandWidthPts, custom: readSizePref() },
    { settingsOpen, extraHeight: widgetExtra },
  );
  const islandRadius =
    shape === "notch"
      ? mode === "expanded"
        ? "0 0 30px 30px"
        : "0 0 22px 22px"
      : mode === "expanded"
        ? "32px"
        : "999px";

  return (
    <div className="island-shell">
      <motion.div
        ref={islandRef}
        className={`island shape-${shape} mode-${mode}${glass ? " glass" : ""}${
          mediaLive ? " media-live" : ""
        }${callLive ? " call-live" : ""}`}
        initial={false}
        animate={{
          width: mode === "collapsed" ? collapsedPx.width : expandedPx.width,
          height: mode === "collapsed" ? collapsedPx.height : expandedPx.height,
          borderRadius: islandRadius,
        }}
        transition={MORPH_SPRING}
        onMouseEnter={enter}
        onMouseLeave={leave}
        onContextMenu={(e) => {
          e.preventDefault();
          // Right-click menu only shows while the panel is expanded; on the small
          // collapsed bar a right-click does nothing (the tray icon still has
          // Show / Hide / Quit).
          if (modeRef.current !== "expanded") return;
          // Keep the menu fully inside the small island window (menus can now
          // have three items, so reserve ~130px of height).
          setCtxMenu({
            x: Math.max(0, Math.min(e.clientX, window.innerWidth - 180)),
            y: Math.max(0, Math.min(e.clientY, window.innerHeight - 130)),
          });
        }}
      >
      {gradient !== "none" && <div className={`island-grad grad-${gradient}`} aria-hidden />}
      <header className="island-strip">
        {/* Status dot: always visible. Grey idle, red mic in use, green on a
            call, blue on a call with the mic live. */}
        <span
          className="island-dot"
          data-mic={micOn ? "on" : "off"}
          data-call={callLive ? "on" : "off"}
          title={
            dotState === "both"
              ? "On a call — microphone live"
              : dotState === "mic"
                ? "Microphone in use"
                : dotState === "call"
                  ? "On a call"
                  : "Idle"
          }
        />
        <div className="island-strip-body">
          {callLive ? (
            <CallPill
              app={call?.app ?? "Call"}
              elapsedMs={Math.max(0, callNow - (callStartRef.current ?? callNow))}
            />
          ) : mediaLive && media?.title ? (
            <MiniNowPlaying media={media} />
          ) : showSysMini ? (
            <SysMiniBar settings={sysSettings} />
          ) : (
            <>
              <div className="island-brand">
                {idleContent === "brand" && <span className="island-name">Winotch</span>}
                {idleContent === "custom" && (
                  <span className="island-custom-text">{idleText.trim() || "—"}</span>
                )}
              </div>
              <div className="island-strip-slot">
                {(idleContent === "brand" || idleContent === "clock") && (
                  <Clock expanded={mode === "expanded"} />
                )}
                {idleContent === "custom" && idleText.trim() === "" && (
                  <span className="island-custom-text is-muted">No text set — open settings</span>
                )}
              </div>
            </>
          )}
        </div>
      </header>

      <AnimatePresence>
        {mode === "expanded" && (
          <motion.section
            key="panel"
            className="island-panel"
            initial={{ opacity: 0, y: 14, scale: 0.97 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 10, scale: 0.98, transition: { duration: 0.16, ease: "easeIn" } }}
            transition={{ type: "spring", stiffness: 320, damping: 30 }}
          >
        <div className="panel-head">
          <div className="panel-head-left">
            <motion.button
              type="button"
              className={`icon-btn ${settingsOpen ? "icon-btn-active" : ""}`}
              title="Settings"
              onClick={toggleSettings}
              whileHover={{ scale: 1.08 }}
              whileTap={{ scale: 0.88 }}
              transition={TAP_SPRING}
            >
              <SettingsIcon />
            </motion.button>
            {settingsOpen && <span className="panel-title">Settings</span>}
          </div>
        </div>

        <AnimatePresence>
          {settingsOpen && draft && (
            <motion.div
              key="settings"
              className="settings"
              initial={{ opacity: 0, scale: 0.95, y: -8 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              exit={{ opacity: 0, scale: 0.96, y: -6, transition: { duration: 0.15, ease: "easeIn" } }}
              transition={{ type: "spring", stiffness: 400, damping: 30 }}
            >
            <p className="settings-tip">
              Changes are staged until you press <strong>Apply</strong> —{" "}
              <strong>Reset</strong> discards them.
            </p>
            <div className="settings-scroll">
              <SettingsSection
                title="Shape"
                subtitle="Notch or pill, and which screen"
                open={sectionOpen.shape}
                onToggle={() => setSectionOpen((s) => ({ ...s, shape: !s.shape }))}
              >
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
            <div className="settings-row">
              <span className="settings-label">Display</span>
              {monitors.length > 1 ? (
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
              ) : (
                <span className="settings-static">Main monitor</span>
              )}
            </div>
              </SettingsSection>
              <SettingsSection
                title="Size"
                subtitle="Exactly how wide and tall"
                open={sectionOpen.size}
                onToggle={() => setSectionOpen((s) => ({ ...s, size: !s.size }))}
              >
            <div className="settings-row">
              <span className="settings-label">Size preset</span>
              <div className="settings-chips">
                {SIZE_PRESETS.map((p) => (
                  <button
                    key={p.label}
                    type="button"
                    className={`preset-chip${
                      draft.size.width === p.width && draft.size.height === p.height ? " on" : ""
                    }`}
                    onClick={(e) => {
                      e.stopPropagation();
                      setDraft({ ...draft, size: { width: p.width, height: p.height } });
                    }}
                  >
                    {p.label}
                  </button>
                ))}
              </div>
            </div>
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
              </SettingsSection>
              <SettingsSection
                title="Appearance"
                subtitle="Surface, glass and gradients"
                open={sectionOpen.appearance}
                onToggle={() => setSectionOpen((s) => ({ ...s, appearance: !s.appearance }))}
              >
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
              <span className="settings-label">Gradient</span>
              <div className="settings-chips">
                {GRADIENTS.map((g) => (
                  <button
                    key={g.id}
                    type="button"
                    className={`preset-chip${draft.gradient === g.id ? " on" : ""}`}
                    onClick={(e) => {
                      e.stopPropagation();
                      setDraft({ ...draft, gradient: g.id });
                    }}
                  >
                    {g.label}
                  </button>
                ))}
              </div>
            </div>
            {draft.glass && (
              <div className="settings-row settings-row-slider">
                <div className="settings-slider-head">
                  <span className="settings-label">Glass opacity</span>
                  <span className="settings-slider-value">{draft.glassOpacity}%</span>
                </div>
                <input
                  type="range"
                  min="10"
                  max="100"
                  step="5"
                  value={draft.glassOpacity}
                  onChange={(e) =>
                    setDraft({ ...draft, glassOpacity: Number(e.target.value) })
                  }
                />
              </div>
            )}
            {draft.glass && (
              <div className="settings-row settings-row-slider">
                <div className="settings-slider-head">
                  <span className="settings-label">Glass blur</span>
                  <span className="settings-slider-value">{draft.glassBlur}px</span>
                </div>
                <input
                  type="range"
                  min={GLASS_BLUR_MIN}
                  max={GLASS_BLUR_MAX}
                  step="2"
                  value={draft.glassBlur}
                  onChange={(e) => setDraft({ ...draft, glassBlur: Number(e.target.value) })}
                />
              </div>
            )}
              </SettingsSection>
              <SettingsSection
                title="Theme"
                subtitle="Light, dark and accent colors"
                open={sectionOpen.theme}
                onToggle={() => setSectionOpen((s) => ({ ...s, theme: !s.theme }))}
              >
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
                <button
                  type="button"
                  title="Customize the accent colors"
                  className={draft.theme === "color" ? "seg-active" : ""}
                  onClick={(e) => {
                    e.stopPropagation();
                    setDraft({ ...draft, theme: "color" });
                  }}
                >
                  Color
                </button>
              </div>
            </div>
            {draft.theme === "color" && (
              <div className="settings-row settings-row-slider">
                <div className="settings-slider-head">
                  <span className="settings-label">Accent color</span>
                  <span className="settings-slider-value">
                    {draft.accent === ACCENT_SYSTEM ? "Windows accent" : draft.accent.toUpperCase()}
                  </span>
                </div>
                <div className="settings-swatches">
                  {ACCENT_PRESETS.map((hex) => (
                    <button
                      key={hex}
                      type="button"
                      title={hex}
                      className={`settings-swatch${draft.accent === hex ? " settings-swatch-active" : ""}`}
                      style={{ background: hex }}
                      onClick={(e) => {
                        e.stopPropagation();
                        setDraft({ ...draft, accent: hex });
                      }}
                    />
                  ))}
                  <label className="settings-swatch settings-swatch-custom" title="Custom color">
                    <input
                      type="color"
                      value={draft.accent === ACCENT_SYSTEM ? ACCENT_PRESETS[0] : draft.accent}
                      onChange={(e) => setDraft({ ...draft, accent: e.target.value })}
                    />
                  </label>
                </div>
                <button
                  type="button"
                  className={`settings-accent-system${
                    draft.accent === ACCENT_SYSTEM ? " settings-accent-system-active" : ""
                  }`}
                  onClick={(e) => {
                    e.stopPropagation();
                    setDraft({ ...draft, accent: ACCENT_SYSTEM });
                  }}
                >
                  Use Windows accent
                </button>
              </div>
            )}
              </SettingsSection>
              <SettingsSection
                title="Idle"
                subtitle="What the bar shows when nothing is happening"
                open={sectionOpen.idle}
                onToggle={() => setSectionOpen((s) => ({ ...s, idle: !s.idle }))}
              >
            <div className="settings-row">
              <span className="settings-label">When idle</span>
              <select
                className="settings-select"
                value={draft.idleContent}
                onChange={(e) =>
                  setDraft({ ...draft, idleContent: e.target.value as IdleContent })
                }
              >
                <option value="brand">Brand + clock</option>
                <option value="clock">Clock only</option>
                <option value="custom">Custom text</option>
                <option value="system">System stats</option>
                <option value="blank">Nothing (dot only)</option>
              </select>
            </div>
            {draft.idleContent === "custom" && (
              <div className="settings-row">
                <span className="settings-label">Idle text</span>
                <input
                  className="settings-text-input"
                  value={draft.idleText}
                  maxLength={40}
                  placeholder="e.g. Lunch at 1"
                  onChange={(e) => setDraft({ ...draft, idleText: e.target.value })}
                />
              </div>
            )}
              </SettingsSection>
            </div>
            <div className="settings-actions">
              <motion.button
                type="button"
                className="settings-btn"
                disabled={!dirty}
                onClick={(e) => {
                  e.stopPropagation();
                  resetDraft();
                }}
                whileHover={{ scale: 1.03 }}
                whileTap={{ scale: 0.97 }}
                transition={TAP_SPRING}
              >
                Reset
              </motion.button>
              <motion.button
                type="button"
                className={`settings-btn settings-btn-primary${dirty ? " settings-btn-dirty" : ""}`}
                disabled={!dirty}
                onClick={(e) => {
                  e.stopPropagation();
                  applySettings();
                }}
                whileHover={{ scale: 1.03 }}
                whileTap={{ scale: 0.97 }}
                transition={TAP_SPRING}
              >
                {appliedFlash ? "Applied ✓" : "Apply"}
              </motion.button>
            </div>
            <motion.button
              type="button"
              className="settings-reset-default"
              onClick={(e) => {
                e.stopPropagation();
                applyDefaults();
              }}
              whileTap={{ scale: 0.97 }}
              transition={TAP_SPRING}
            >
              Reset to default
            </motion.button>
            </motion.div>
          )}
        </AnimatePresence>

        <WidgetStack widgets={widgets} media={media} onChange={updateWidgetList} />
          </motion.section>
        )}
      </AnimatePresence>

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
            className="ctx-item"
            onClick={() => {
              setCtxMenu(null);
              void invoke("restart_app").catch(() => {});
            }}
          >
            <svg viewBox="0 0 24 24" width="11" height="11" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M21 12a9 9 0 1 1-3-6.7" />
              <path d="M21 3v6h-6" />
            </svg>
            Restart Winotch
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
      </motion.div>
    </div>
  );
}