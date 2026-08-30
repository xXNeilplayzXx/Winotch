import { useEffect, useRef, useState } from "react";
import type { CSSProperties } from "react";
import { animateTo, EXPANDED, SHAPES, type IslandShape } from "../lib/island";
import { WidgetSlot } from "./WidgetSlot";
import { Clock } from "./widgets/Clock";

type Mode = "collapsed" | "expanded";

export function Island() {
  const [mode, setMode] = useState<Mode>("collapsed");
  const [shape, setShape] = useState<IslandShape>("notch");
  const [roundness, setRoundness] = useState(70);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const modeRef = useRef<Mode>("collapsed");

  // Place the island collapsed for the current shape, then reveal on mount.
  useEffect(() => {
    import("../lib/island")
      .then(({ showIsland }) => showIsland(shape))
      .catch((err) => console.error("island positioning failed", err));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const enter = () => {
    if (modeRef.current === "expanded") return;
    modeRef.current = "expanded";
    setMode("expanded");
    void animateTo(EXPANDED, 300);
  };

  const leave = () => {
    if (modeRef.current === "collapsed") return;
    modeRef.current = "collapsed";
    setMode("collapsed");
    void animateTo(SHAPES[shape].collapsed, 260);
  };

  const changeShape = (next: IslandShape) => {
    setShape(next);
    setSettingsOpen(false);
    // If we're collapsed right now, snap to the new shape immediately.
    if (modeRef.current === "collapsed") {
      void animateTo(SHAPES[next].collapsed, 260);
    }
  };

  const radiusPx = Math.round(10 + (roundness / 100) * 60);
  const style = {
    "--pill-radius": `${radiusPx}px`,
  } as CSSProperties;

  return (
    <div
      className={`island shape-${shape} mode-${mode}`}
      style={style}
      onMouseEnter={enter}
      onMouseLeave={leave}
    >
      <header className="island-strip">
        <div className="island-brand">
          <span className="island-dot" />
          <span className="island-name">Winotch</span>
        </div>
        <div className="island-strip-slot">
          <WidgetSlot>
            <Clock expanded={mode === "expanded"} />
          </WidgetSlot>
        </div>
      </header>

      <section className="island-panel">
        <div className="panel-head">
          <div className="panel-head-left">
            <button
              type="button"
              className={`icon-btn ${settingsOpen ? "icon-btn-active" : ""}`}
              title="Settings"
              onClick={(e) => {
                e.stopPropagation();
                setSettingsOpen((v) => !v);
              }}
            >
              <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                <circle cx="12" cy="12" r="3" />
                <path d="M19.4 15a1.7 1.7 0 0 0 .34 1.87l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.7 1.7 0 0 0-1.87-.34 1.7 1.7 0 0 0-1 1.55V21a2 2 0 1 1-4 0v-.09a1.7 1.7 0 0 0-1-1.55 1.7 1.7 0 0 0-1.87.34l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.7 1.7 0 0 0 .34-1.87 1.7 1.7 0 0 0-1.55-1H3a2 2 0 1 1 0-4h.09a1.7 1.7 0 0 0 1.55-1 1.7 1.7 0 0 0-.34-1.87l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.7 1.7 0 0 0 1.87.34h.09a1.7 1.7 0 0 0 1-1.55V3a2 2 0 1 1 4 0v.09a1.7 1.7 0 0 0 1 1.55 1.7 1.7 0 0 0 1.87-.34l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.7 1.7 0 0 0-.34 1.87v.09a1.7 1.7 0 0 0 1.55 1H21a2 2 0 1 1 0 4h-.09a1.7 1.7 0 0 0-1.55 1z" />
              </svg>
            </button>
            <span className="panel-title">{shape === "notch" ? "Notch mode" : "Pill mode"}</span>
          </div>
          <span className="panel-badge">Winotch</span>
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
              <span className="settings-label">Roundness</span>
              <input
                type="range"
                min={0}
                max={100}
                value={roundness}
                onChange={(e) => {
                  e.stopPropagation();
                  setRoundness(Number(e.currentTarget.value));
                }}
              />
              <span className="settings-value">{roundness}</span>
            </div>
          </div>
        )}

        <WidgetSlot>
          <div className="panel-empty">
            <p>Your widgets dock here.</p>
            <span>
              {shape === "notch" ? "Notch" : "Pill"} mode · more widgets coming
            </span>
          </div>
        </WidgetSlot>
      </section>
    </div>
  );
}