import { useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import type { LauncherApp, LauncherSettings } from "../../lib/widgets";

interface LauncherWidgetProps {
  settings?: LauncherSettings;
  edit: boolean;
  onSave: (s: LauncherSettings) => void;
}

/** Best-effort common apps; paths with %VARS% are expanded in Rust, the rest
 *  resolve via PATH when launched with CreateProcess. */
const QUICK_APPS: LauncherApp[] = [
  { label: "Chrome", path: "chrome.exe" },
  { label: "Edge", path: "msedge.exe" },
  { label: "Terminal", path: "wt.exe" },
  { label: "VS Code", path: "code.exe" },
  { label: "Notepad", path: "notepad.exe" },
  { label: "Calculator", path: "calc.exe" },
  { label: "Paint", path: "mspaint.exe" },
  { label: "Spotify", path: "%APPDATA%\\Spotify\\Spotify.exe" },
  { label: "Steam", path: "C:\\Program Files (x86)\\Steam\\steam.exe" },
];

function initial(s: string) {
  return s.trim().charAt(0).toUpperCase() || "•";
}

export function LauncherWidget({ settings, edit, onSave }: LauncherWidgetProps) {
  const apps = settings?.apps ?? [];
  const [label, setLabel] = useState("");
  const [path, setPath] = useState("");
  const [err, setErr] = useState(false);
  const [launchErr, setLaunchErr] = useState(false);

  if (edit) {
    return (
      <div className="widget-edit">
        {apps.length > 0 && (
          <div className="launcher-list">
            {apps.map((a, i) => (
              <div className="launcher-row" key={`${a.path}-${i}`}>
                <span className="launcher-row-name">{a.label}</span>
                <span className="launcher-row-path">{a.path}</span>
                <button
                  type="button"
                  className="widget-btn widget-btn-danger"
                  title="Remove"
                  onClick={() => {
                    const next = apps.filter((_, j) => j !== i);
                    onSave({ apps: next });
                  }}
                >
                  ✕
                </button>
              </div>
            ))}
          </div>
        )}

        <div className="widget-edit-row">
          <input
            className="widget-input widget-input-sm"
            placeholder="Name (optional)"
            value={label}
            onChange={(e) => setLabel(e.target.value)}
          />
          <input
            className="widget-input"
            placeholder="App path or name, e.g. chrome.exe"
            value={path}
            onChange={(e) => {
              setPath(e.target.value);
              setErr(false);
            }}
            onKeyDown={(e) => e.key === "Enter" && addApp()}
          />
          <button type="button" className="widget-btn" onClick={addApp}>
            Add
          </button>
        </div>
        {err && <p className="widget-err">Enter an app path or name first.</p>}

        <div className="launcher-quick">
          {QUICK_APPS.map((a) => (
            <button
              key={a.path}
              type="button"
              className="launcher-chip"
              onClick={() => {
                const next = [...apps, { label: a.label, path: a.path }];
                onSave({ apps: next });
              }}
            >
              + {a.label}
            </button>
          ))}
        </div>
      </div>
    );
  }

  if (apps.length === 0) {
    return (
      <div className="widget-edit">
        <p className="widget-edit-hint">No apps yet. Open the ⚙ on this card to add some.</p>
      </div>
    );
  }

  return (
    <div className="launcher" data-err={launchErr || undefined}>
      {apps.map((a, i) => (
        <button
          type="button"
          key={`${a.path}-${i}`}
          className="launcher-app"
          title={a.path}
          onClick={() => {
            setLaunchErr(false);
            void invoke("launch_app", { path: a.path })
              .then(() => {})
              .catch(() => setLaunchErr(true));
          }}
        >
          <span className="launcher-app-icon">{initial(a.label)}</span>
          <span className="launcher-app-label">{a.label}</span>
        </button>
      ))}
      {launchErr && <span className="widget-err">Couldn't launch “{apps[0]?.label}”.</span>}
    </div>
  );

  function addApp() {
    const p = path.trim();
    if (!p) {
      setErr(true);
      return;
    }
    const next = [...apps, { label: label.trim() || p.split(/[\\/]/).pop() || p, path: p }];
    onSave({ apps: next });
    setLabel("");
    setPath("");
    setErr(false);
  }
}