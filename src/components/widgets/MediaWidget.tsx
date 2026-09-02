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
 * Media-control widget: compact deck card with title/artist, hairline progress
 * and pill transport buttons (Windows SMTC). Shows a quiet mono "nothing
 * playing" state when no session is active.
 */
export function MediaWidget({ media }: { media: MediaState | null }) {
  if (!media || !media.title) {
    return (
      <div className="widget-empty">
        <b>Nothing playing</b>
        <br />
        Start something and it shows up here
      </div>
    );
  }

  const dur = media.duration > 0 ? media.duration : 0;
  const pos = dur > 0 ? Math.min(Math.max(media.position, 0), dur) : 0;
  const progress = dur > 0 ? Math.min(1, pos / dur) : 0;

  return (
    <div className="media-body" data-playing={media.playing}>
      <div className="media-meta">
        <div className="media-title">{media.title}</div>
        <div className="media-artist">{media.artist || media.album || ""}</div>
      </div>

      <div className="media-progress" aria-hidden>
        <span className="media-time">{fmtTime(pos)}</span>
        <div className="media-bar">
          <i style={{ width: `${Math.round(progress * 100)}%` }} />
        </div>
        <span className="media-time">{fmtTime(dur)}</span>
      </div>

      <div className="media-ctl">
        <button type="button" className="mbtn" title="Previous" onClick={mediaAct("media_previous")}>
          ⏮
        </button>
        <button
          type="button"
          className="mbtn main"
          title={media.playing ? "Pause" : "Play"}
          onClick={mediaAct("media_play_pause")}
        >
          {media.playing ? "❚❚" : "▶"}
        </button>
        <button type="button" className="mbtn" title="Next" onClick={mediaAct("media_next")}>
          ⏭
        </button>
      </div>
    </div>
  );
}