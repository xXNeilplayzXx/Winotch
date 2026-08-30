import { useEffect, useRef, useState } from "react";
import { animateTo, ISLAND } from "../lib/island";
import { WidgetSlot } from "./WidgetSlot";
import { Clock } from "./widgets/Clock";

type Mode = "collapsed" | "expanded";

export function Island() {
  const [mode, setMode] = useState<Mode>("collapsed");
  const [ready, setReady] = useState(false);
  const modeRef = useRef<Mode>("collapsed");

  // Position collapsed and reveal the window once, on mount.
  useEffect(() => {
    let cancelled = false;
    import("../lib/island")
      .then(({ showIsland }) => showIsland())
      .then(() => {
        if (!cancelled) setReady(true);
      })
      .catch((err) => console.error("island show failed", err));
    return () => {
      cancelled = true;
    };
  }, []);

  const enter = () => {
    if (modeRef.current === "expanded") return;
    modeRef.current = "expanded";
    setMode("expanded");
    void animateTo(ISLAND.expanded, 320);
  };

  const leave = () => {
    if (modeRef.current === "collapsed") return;
    modeRef.current = "collapsed";
    setMode("collapsed");
    void animateTo(ISLAND.collapsed, 280);
  };

  return (
    <div
      className={`island ${mode} ${ready ? "island-ready" : ""}`}
      onMouseEnter={enter}
      onMouseLeave={leave}
    >
      <header className="island-strip">
        <div className="island-brand" aria-hidden>
          <span className="island-dot" />
          <span className="island-name">Freebuff</span>
        </div>
        <div className="island-strip-slot">
          <WidgetSlot>
            <Clock expanded={mode === "expanded"} />
          </WidgetSlot>
        </div>
      </header>

      <section className="island-panel">
        <WidgetSlot>
          <div className="panel-head">
            <span className="panel-title">Freebuff Desktop</span>
            <span className="panel-badge">v0.1</span>
          </div>
          <div className="panel-empty">
            <p>Your widgets dock here.</p>
            <span>Now Playing · Clipboard · Timers coming next</span>
          </div>
        </WidgetSlot>
      </section>
    </div>
  );
}