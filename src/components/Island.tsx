import { useEffect, useRef, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { animateIsland, type IslandShape } from "../lib/island";
import { WidgetSlot } from "./WidgetSlot";
import { Clock } from "./widgets/Clock";

type Mode = "collapsed" | "expanded";

/** Snapshot of the active media session (Windows SMTC feed; null = idle). */
interface MediaState {
  title?: string;
  artist?: string;
  album?: string;
  art?: string;
  playing: boolean;
  position: number;
  duration: number;
}

/* ---- Small shared icons ---- */
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
function PrevIcon({ size = 13 }: { size?: number }) {
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} fill="currentColor">
      <path d="M6 6h2v12H6zM20 6l-9 6 9 6z" />
    </svg>
  );
}
function NextIcon({ size = 13 }: { size?: number }) {
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} fill="currentColor">
      <path d="M16 6h2v12h-2zM4 6l9 6-9 6z" />
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

function fmtTime(sec: number): string {
  if (!Number.isFinite(sec) || sec <= 0) return "0:00";
  const m = Math.floor(sec / 60);
  const s = Math.floor(sec % 60);
  return `${m}:${s.toString().padStart(2, "0")}`;
}

/** Live track card in the expanded panel: art, text, progress, transport. */
function NowPlaying({ media }: { media: MediaState }) {
  const dur = media.duration > 0 ? media.duration : 0;
  const pos = dur > 0 ? Math.min(Math.max(media.position, 0), dur) : 0;
  const progress = dur > 0 ? Math.min(1, pos / dur) : 0;

  return (
    <div className="np-card" data-playing={media.playing}>
      <div className="nowplaying">
        <div className="np-art">
          <div className="np-art-img">
            {media.art ? <img src={media.art} alt="" draggable={false} /> : <MusicNote />}
          </div>
        </div>
        <div className="np-text">
          <span className="np-title">{media.title}</span>
          <span className="np-artist">{media.artist || media.album || ""}</span>
        </div>
      </div>

      <div className="np-progress" aria-hidden>
        <span style={{ width: `${Math.round(progress * 100)}%` }} />
      </div>

      {dur > 0 && (
        <div className="np-times">
          <span>{fmtTime(pos)}</span>
          <span>{fmtTime(dur)}</span>
        </div>
      )}

      <div className="np-controls">
        <button type="button" className="np-ctl" title="Previous" onClick={mediaAct("media_previous")}>
          <PrevIcon />
        </button>
        <button
          type="button"
          className="np-ctl np-ctl-main"
          title={media.playing ? "Pause" : "Play"}
          onClick={mediaAct("media_play_pause")}
        >
          {media.playing ? <PauseIcon /> : <PlayIcon />}
        </button>
        <button type="button" className="np-ctl" title="Next" onClick={mediaAct("media_next")}>
          <NextIcon />
        </button>
      </div>
    </div>
  );
}

/**
 * Mini now-playing bar that REPLACES the collapsed pill content the moment a
 * media session appears — the pill becomes a live activity widget without any
 * interaction. Reverts to brand/clock when the session goes away.
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
          <NextIcon size={10} />
        </button>
      </div>
      <span className="np-mini-progress" style={{ width: `${Math.round(progress * 100)}%` }} />
    </>
  );
}

export function Island() {
  const [mode, setMode] = useState<Mode>("collapsed");
  const [shape, setShape] = useState<IslandShape>("notch");
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [media, setMedia] = useState<MediaState | null>(null);
  const [glass, setGlass] = useState<boolean>(() => {
    try {
      return localStorage.getItem("winotch.glass") !== "off";
    } catch {
      return true;
    }
  });
  const modeRef = useRef<Mode>("collapsed");

  // Place the island collapsed for the current shape, then reveal on mount.
  useEffect(() => {
    import("../lib/island")
      .then(({ showIsland }) => showIsland(shape))
      .catch((err) => console.error("island positioning failed", err));
    // eslint-disable-next-line react-hooks/exhaustive-deps
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

  const changeGlass = (next: boolean) => {
    setGlass(next);
    try {
      localStorage.setItem("winotch.glass", next ? "on" : "off");
    } catch {
      /* storage unavailable; keep session-only */
    }
  };

  const enter = () => {
    if (modeRef.current === "expanded") return;
    modeRef.current = "expanded";
    setMode("expanded");
    // Carry the settings-open bonus so a re-expansion while settings is still
    // active keeps the taller window and nothing overflows.
    void animateIsland({ shape, expanded: true, settingsOpen });
  };

  const leave = () => {
    if (modeRef.current === "collapsed") return;
    modeRef.current = "collapsed";
    setMode("collapsed");
    void animateIsland({ shape, expanded: false });
  };

  const changeShape = (next: IslandShape) => {
    setShape(next);
    setSettingsOpen(false);
    // Re-shape immediately in BOTH states: the CSS class flips the container
    // layout (carved notch vs floating capsule) at once, and the window animates
    // to the new geometry — a fresh silhouette when collapsed, a re-anchor
    // (notch flush to the top, pill floating just below) when expanded.
    const expanded = modeRef.current === "expanded";
    // changeShape closes the settings panel, so drop its height bonus too.
    void animateIsland({ shape: next, expanded, settingsOpen: false });
  };

  const toggleSettings = (e: React.MouseEvent) => {
    e.stopPropagation();
    const next = !settingsOpen;
    setSettingsOpen(next);
    // Grow the expanded window while settings are visible so nothing overflows.
    void animateIsland({ shape, expanded: true, settingsOpen: next });
  };

  const mediaLive = Boolean(media?.playing);

  return (
    <div
      className={`island shape-${shape} mode-${mode}${glass ? " glass" : ""}${
        mediaLive ? " media-live" : ""
      }`}
      onMouseEnter={enter}
      onMouseLeave={leave}
    >
      <header className="island-strip">
        {media?.title ? (
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
              <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                <circle cx="12" cy="12" r="3" />
                <path d="M19.4 15a1.7 1.7 0 0 0 .34 1.87l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.7 1.7 0 0 0-1.87-.34 1.7 1.7 0 0 0-1 1.55V21a2 2 0 1 1-4 0v-.09a1.7 1.7 0 0 0-1-1.55 1.7 1.7 0 0 0-1.87.34l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.7 1.7 0 0 0 .34-1.87 1.7 1.7 0 0 0-1.55-1H3a2 2 0 1 1 0-4h.09a1.7 1.7 0 0 0 1.55-1 1.7 1.7 0 0 0-.34-1.87l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.7 1.7 0 0 0 1.87.34h.09a1.7 1.7 0 0 0 1-1.55V3a2 2 0 1 1 4 0v.09a1.7 1.7 0 0 0 1 1.55 1.7 1.7 0 0 0 1.87-.34l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.7 1.7 0 0 0-.34 1.87v.09a1.7 1.7 0 0 0 1.55 1H21a2 2 0 1 1 0 4h-.09a1.7 1.7 0 0 0-1.55 1z" />
              </svg>
            </button>
            <span className="panel-title">{shape === "notch" ? "Notch mode" : "Pill mode"}</span>
          </div>
        </div>

        {settingsOpen && (
          <div className="settings">
            <div className="settings-row">
              <span className="settings-label">Mode</span>
              <div className="segmented">
                <button
                  type="button"
                  className={shape === "notch" ? "seg-active" : ""}
                  onClick={(e) => {
                    e.stopPropagation();
                    changeShape("notch");
                  }}
                >
                  Notch
                </button>
                <button
                  type="button"
                  className={shape === "pill" ? "seg-active" : ""}
                  onClick={(e) => {
                    e.stopPropagation();
                    changeShape("pill");
                  }}
                >
                  Pill
                </button>
              </div>
            </div>
            <div className="settings-row">
              <span className="settings-label">Appearance</span>
              <div className="segmented">
                <button
                  type="button"
                  className={!glass ? "seg-active" : ""}
                  onClick={(e) => {
                    e.stopPropagation();
                    changeGlass(false);
                  }}
                >
                  Solid
                </button>
                <button
                  type="button"
                  className={glass ? "seg-active" : ""}
                  onClick={(e) => {
                    e.stopPropagation();
                    changeGlass(true);
                  }}
                >
                  Glass
                </button>
              </div>
            </div>
          </div>
        )}

        <WidgetSlot>
          {media?.title ? (
            <NowPlaying media={media} />
          ) : (
            <div className="panel-empty">
              <p>Nothing playing</p>
              <span>Start something and it shows up here</span>
            </div>
          )}
        </WidgetSlot>
      </section>
    </div>
  );
}
