import { useCallback, useEffect, useState } from "react";
import { listen } from "@tauri-apps/api/event";
import { invoke } from "@tauri-apps/api/core";
import { Island } from "./components/Island";
import { SetupWizard } from "./components/SetupWizard";
import type { IslandShape } from "./lib/island";
import {
  MONITOR_KEY,
  readMonitorPref,
  saveMonitorPref,
  type MonitorPref,
} from "./lib/monitors";
import { saveWidgets, type WidgetInstance } from "./lib/widgets";

type Stage = "loading" | "setup" | "ready";

/** Shape saved by a previous run, if any (the first-run gate). */
function readShape(): IslandShape | null {
  try {
    const v = localStorage.getItem("winotch.shape");
    return v === "pill" || v === "notch" ? v : null;
  } catch {
    return null;
  }
}

/** Has the user ever chosen a monitor (vs. the implicit default)? */
function monitorChosen(): boolean {
  try {
    return localStorage.getItem(MONITOR_KEY) !== null;
  } catch {
    return true;
  }
}

function App() {
  const [stage, setStage] = useState<Stage>("loading");
  const [shape, setShape] = useState<IslandShape | null>(() => readShape());
  const [monitorPref, setMonitorPref] = useState<MonitorPref>(() => readMonitorPref());

  const needsShape = shape === null;
  const needsMonitor = !monitorChosen();

  // First run (or a choice missing): resize the window for the setup wizard
  // before revealing it. The island is not mounted yet, so nothing flashes in
  // the tiny island window.
  useEffect(() => {
    if (stage !== "loading") return;
    if (!needsShape && !needsMonitor) {
      setStage("ready");
      return;
    }
    let cancelled = false;
    import("./lib/island")
      .then(({ showSetup }) => showSetup())
      .catch(() => {})
      .then(() => {
        if (!cancelled) setStage("setup");
      });
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [stage]);

  /** Reopen the setup wizard on demand (settings panel, context menu, or tray
   * menu). The wizard re-shows and re-sizes the window centered on screen;
   * finishing it returns the app to the island. */
  const rerunSetup = useCallback(() => {
    setStage("setup");
    void import("./lib/island")
      .then(({ showSetup }) => showSetup())
      .catch(() => {});
  }, []);

  // The system tray's "Rerun Setup…" item arrives as a backend event.
  useEffect(() => {
    let disposed = false;
    let unlisten: (() => void) | undefined;
    void listen("winotch://rerun-setup", () => rerunSetup())
      .then((u) => {
        if (disposed) u();
        else unlisten = u;
      })
      .catch(() => {});
    return () => {
      disposed = true;
      unlisten?.();
    };
  }, [rerunSetup]);

  // In All-monitors mode, make sure every display has an island window. This
  // runs in the main window AND every extra window; spawning is idempotent.
  useEffect(() => {
    if (stage === "ready" && monitorPref === "all") {
      void invoke("spawn_island_windows").catch(() => {});
    }
  }, [stage, monitorPref]);

  const finishSetup = (
    picked: IslandShape,
    monitor: MonitorPref,
    appearance: "glass" | "solid",
    widgets: WidgetInstance[],
  ) => {
    if (needsShape) {
      try {
        localStorage.setItem("winotch.shape", picked);
      } catch {
        /* storage unavailable; keep session-only */
      }
      setShape(picked);
    }
    try {
      // The island reads this as: anything but "off" means glass is on.
      localStorage.setItem("winotch.glass", appearance === "glass" ? "on" : "off");
    } catch {
      /* storage unavailable; keep session-only */
    }
    saveWidgets(widgets);
    saveMonitorPref(monitor);
    setMonitorPref(monitor);
    setStage("ready");
    if (monitor === "all") {
      void invoke("spawn_island_windows").catch(() => {});
    }
  };

  if (stage === "loading") return null;
  if (stage === "setup") {
    return (
      <SetupWizard
        requireShape={needsShape}
        initialShape={shape ?? undefined}
        onDone={finishSetup}
      />
    );
  }
  return <Island initialShape={shape ?? "notch"} onRerunSetup={rerunSetup} />;
}

export default App;