import { motion } from "framer-motion";
import { invoke } from "@tauri-apps/api/core";

/** Snapshot of the active media session (Windows SMTC feed; null = idle). */
export interface MediaState {
  title?: string;
  artist?: string;
  album?: string;
  art?: string;
  playing: boolean;
  position: number;
  duration: number;
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

/**
 * Media-control widget: live track card with art, text, progress and
 * transport controls (Windows SMTC). Shows a quiet "nothing playing" state
 * when no session is active.
 */
export function MediaWidget({ media }: { media: MediaState | null }) {
  if (!media || !media.title) {
    return (
      <div className="panel-empty">
        <p>Nothing playing</p>
        <span>Start something and it shows up here</span>
      </div>
    );
  }

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
        <motion.button
          type="button"
          className="np-ctl"
          title="Previous"
          onClick={mediaAct("media_previous")}
          whileHover={{ scale: 1.15 }}
          whileTap={{ scale: 0.9 }}
          transition={{ type: "spring", stiffness: 500, damping: 30 }}
        >
          <PrevIcon />
        </motion.button>
        <motion.button
          type="button"
          className="np-ctl np-ctl-main"
          title={media.playing ? "Pause" : "Play"}
          onClick={mediaAct("media_play_pause")}
          whileHover={{ scale: 1.12 }}
          whileTap={{ scale: 0.92 }}
          transition={{ type: "spring", stiffness: 500, damping: 30 }}
        >
          {media.playing ? <PauseIcon /> : <PlayIcon />}
        </motion.button>
        <motion.button
          type="button"
          className="np-ctl"
          title="Next"
          onClick={mediaAct("media_next")}
          whileHover={{ scale: 1.15 }}
          whileTap={{ scale: 0.9 }}
          transition={{ type: "spring", stiffness: 500, damping: 30 }}
        >
          <NextIcon />
        </motion.button>
      </div>
    </div>
  );
}