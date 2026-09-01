import { useEffect, useState } from "react";
import type { IslandShape } from "../lib/island";
import {
  getMonitors,
  type MonitorInfo,
  type MonitorPref,
} from "../lib/monitors";
import { readThemePref, themeManager, type ThemePref } from "../lib/theme";
import { readWidgets, type WidgetInstance } from "../lib/widgets";
import { WidgetStack } from "./WidgetStack";

type Appearance = "glass" | "solid";

interface SetupWizardProps {
  /** False when the user already picked a shape on an earlier run. */
  requireShape: boolean;
  initialShape?: IslandShape;
  onDone: (shape: IslandShape, monitor: MonitorPref, appearance: Appearance, widgets: WidgetInstance[]) => void;
}

function key(m: MonitorInfo) {
  return `${m.x},${m.y},${m.width},${m.height}`;
}

function fmtResolution(m: MonitorInfo) {
  return `${m.width} × ${m.height}`;
}

/** Positions monitors as proportionate rects for the custom layout picker. */
function layoutRects(monitors: MonitorInfo[]) {
  const minX = Math.min(...monitors.map((m) => m.x));
  const minY = Math.min(...monitors.map((m) => m.y));
  const maxRight = Math.max(...monitors.map((m) => m.x + m.width));
  const maxBottom = Math.max(...monitors.map((m) => m.y + m.height));
  const w = maxRight - minX;
  const h = maxBottom - minY;
  const scale = Math.min(232 / w, 128 / h);
  return monitors.map((m) => ({
    m,
    left: (m.x - minX) * scale,
    top: (m.y - minY) * scale,
    width: Math.max(34, m.width * scale),
    height: Math.max(20, m.height * scale),
  }));
}

/**
 * First-run setup wizard — two compact pages that never scroll:
 *
 *   Page 1 · Look & feel — theme (Light/Dark/System Default), type
 *   (Notch vs Pill), and feel (Glass vs Solid).
 *
 *   Page 2 · Widgets & the rest — which display(s) the island lives on
 *   (when more than one is present) and the widget layout, arranged before
 *   the island launches. Everything is changeable later from the settings.
 */
export function SetupWizard({ requireShape, initialShape, onDone }: SetupWizardProps) {
  const [page, setPage] = useState<1 | 2>(1);
  const [monitors, setMonitors] = useState<MonitorInfo[]>([]);
  const [shape, setShape] = useState<IslandShape | null>(initialShape ?? null);
  const [monitor, setMonitor] = useState<MonitorPref | null>(null);
  const [customOpen, setCustomOpen] = useState(false);
  const [appearance, setAppearance] = useState<Appearance>(() => {
    try {
      return localStorage.getItem("winotch.glass") === "off" ? "solid" : "glass";
    } catch {
      return "glass";
    }
  });
  const [themePref, setThemePref] = useState<ThemePref>(() => readThemePref());
  // Widgets can be arranged before the island ever launches; existing users
  // see their current layout and can tweak it further.
  const [widgets, setWidgets] = useState<WidgetInstance[]>(() => readWidgets());

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

  const multi = monitors.length > 1;
  const shapeDone = !requireShape || shape !== null;
  const monitorDone = !multi || monitor !== null;

  const setTheme = (next: ThemePref) => {
    setThemePref(next);
    themeManager.setPref(next);
  };

  return (
    <div className="setup setup--paged">
      <div className="setup-head">
        <span className="setup-logo">W</span>
        <h1>Winotch</h1>
        <p>Pick how your island should look. You can change it any time.</p>
      </div>

      {page === 1 ? (
        <>
          <h2 className="setup-section">Quick presets</h2>
          <div className="setup-chips">
            <button
              type="button"
              className={`setup-chip${
                shape === "notch" && appearance === "glass" && themePref === "dark"
                  ? " setup-picked"
                  : ""
              }`}
              onClick={() => {
                setTheme("dark");
                setShape("notch");
                setAppearance("glass");
              }}
            >
              Classic Notch
            </button>
            <button
              type="button"
              className={`setup-chip${
                shape === "notch" && appearance === "solid" && themePref === "system"
                  ? " setup-picked"
                  : ""
              }`}
              onClick={() => {
                setTheme("system");
                setShape("notch");
                setAppearance("solid");
              }}
            >
              Solid Notch
            </button>
            <button
              type="button"
              className={`setup-chip${
                shape === "pill" && appearance === "glass" && themePref === "light"
                  ? " setup-picked"
                  : ""
              }`}
              onClick={() => {
                setTheme("light");
                setShape("pill");
                setAppearance("glass");
              }}
            >
              Light Pill
            </button>
          </div>

          <h2 className="setup-section">Theme</h2>
          <div className="segmented segmented-full">
            <button
              type="button"
              className={themePref === "light" ? "seg-active" : ""}
              onClick={() => setTheme("light")}
            >
              Light
            </button>
            <button
              type="button"
              className={themePref === "dark" ? "seg-active" : ""}
              onClick={() => setTheme("dark")}
            >
              Dark
            </button>
            <button
              type="button"
              className={themePref === "system" ? "seg-active" : ""}
              onClick={() => setTheme("system")}
            >
              System Default
            </button>
          </div>

          <h2 className="setup-section">Type</h2>
          <div className="setup-grid">
            <button
              type="button"
              className={`setup-tile setup-tile-type${shape === "notch" ? " setup-picked" : ""}`}
              onClick={() => setShape("notch")}
            >
              <span className="setup-preview-mini setup-preview-mini-notch" aria-hidden>
                <span className="setup-cut" />
              </span>
              <strong>Notch</strong>
              <small>Carved into the top edge</small>
            </button>
            <button
              type="button"
              className={`setup-tile setup-tile-type${shape === "pill" ? " setup-picked" : ""}`}
              onClick={() => setShape("pill")}
            >
              <span className="setup-preview-mini setup-preview-mini-pill" aria-hidden>
                <span className="setup-cap" />
              </span>
              <strong>Pill</strong>
              <small>A floating capsule</small>
            </button>
          </div>

          <h2 className="setup-section">Feel</h2>
          <div className="segmented segmented-full">
            <button
              type="button"
              className={appearance === "glass" ? "seg-active" : ""}
              onClick={() => setAppearance("glass")}
            >
              Glass
            </button>
            <button
              type="button"
              className={appearance === "solid" ? "seg-active" : ""}
              onClick={() => setAppearance("solid")}
            >
              Solid
            </button>
          </div>

          <button
            type="button"
            className="setup-cta"
            disabled={!shapeDone}
            onClick={() => shapeDone && setPage(2)}
          >
            Continue
          </button>
        </>
      ) : (
        <>
          {multi && (
            <>
              <h2 className="setup-section">Where should it live?</h2>
              <div className="setup-chips">
                <button
                  type="button"
                  className={`setup-chip${monitor === "primary" ? " setup-picked" : ""}`}
                  onClick={() => {
                    setMonitor("primary");
                    setCustomOpen(false);
                  }}
                >
                  Main monitor
                </button>
                {monitors
                  .filter((m) => !m.isPrimary)
                  .map((m) => (
                    <button
                      key={m.device}
                      type="button"
                      className={`setup-chip${monitor === `mon:${m.device}` ? " setup-picked" : ""}`}
                      onClick={() => {
                        setMonitor(`mon:${m.device}`);
                        setCustomOpen(false);
                      }}
                    >
                      {m.name}
                    </button>
                  ))}
                <button
                  type="button"
                  className={`setup-chip${monitor === "all" ? " setup-picked" : ""}`}
                  onClick={() => {
                    setMonitor("all");
                    setCustomOpen(false);
                  }}
                >
                  All monitors
                </button>
                <button
                  type="button"
                  className={`setup-chip${customOpen ? " setup-picked" : ""}`}
                  onClick={() => setCustomOpen((v) => !v)}
                >
                  Custom setup
                </button>
              </div>
              {customOpen && (
                <div className="setup-map">
                  {layoutRects(monitors).map(({ m, left, top, width, height }) => (
                    <button
                      key={key(m)}
                      type="button"
                      className={`setup-map-mon${monitor === `mon:${m.device}` ? " setup-picked" : ""}`}
                      style={{ left, top, width, height }}
                      title={`${m.name} — ${fmtResolution(m)}`}
                      onClick={() => setMonitor(`mon:${m.device}`)}
                    >
                      <span>{m.isPrimary ? "Main" : m.name}</span>
                    </button>
                  ))}
                </div>
              )}
            </>
          )}

          <h2 className="setup-section">Widgets</h2>
          <WidgetStack widgets={widgets} media={null} configureAll onChange={setWidgets} />

          <div className="setup-foot">
            <button type="button" className="setup-back" onClick={() => setPage(1)}>
              ← Back
            </button>
            <button
              type="button"
              className="setup-cta"
              disabled={!monitorDone}
              onClick={() =>
                monitorDone && shape && onDone(shape, monitor ?? "primary", appearance, widgets)
              }
            >
              Get started
            </button>
          </div>
        </>
      )}
    </div>
  );
}
