import { useEffect } from "react";
import type { SystemSettings } from "../../lib/widgets";
import {
  startSystemStatsPolling,
  stopSystemStatsPolling,
  useSystemStats,
} from "../../lib/systemStats";

function CpuIcon({ size = 13 }: { size?: number }) {
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <rect x="5" y="5" width="14" height="14" rx="2" />
      <rect x="9.5" y="9.5" width="5" height="5" rx="1" />
      <path d="M9 3v2M15 3v2M9 19v2M15 19v2M3 9h2M3 15h2M19 9h2M19 15h2" />
    </svg>
  );
}

function MemoryIcon({ size = 13 }: { size?: number }) {
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <rect x="3" y="8" width="18" height="10" rx="2" />
      <path d="M7 11v4M11 11v4M15 11v4M19 11v4" />
    </svg>
  );
}

function NetIcon({ size = 13 }: { size?: number }) {
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M8 12V6m0 0L4 10m4-4l4 4" />
      <path d="M16 12v6m0 0l4-4m-4 4l-4-4" />
    </svg>
  );
}

function TempIcon({ size = 13 }: { size?: number }) {
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M10 13.5V5a2 2 0 1 1 4 0v8.5a4 4 0 1 1-4 0z" />
    </svg>
  );
}

function BatteryIcon({ size = 13, charging }: { size?: number; charging?: boolean }) {
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <rect x="2" y="8" width="17" height="8" rx="2" />
      <path d="M22 11v2" />
      {charging ? (
        <path d="M9.5 9l-2 3h3l-2 3" fill="currentColor" stroke="none" />
      ) : (
        <rect x="4.5" y="10" width="8" height="4" rx="1" fill="currentColor" stroke="none" />
      )}
    </svg>
  );
}

function formatRate(bytesPerSec: number): string {
  if (!Number.isFinite(bytesPerSec) || bytesPerSec <= 0) return "0 B/s";
  if (bytesPerSec < 1024) return `${bytesPerSec.toFixed(0)} B/s`;
  if (bytesPerSec < 1024 * 1024) return `${(bytesPerSec / 1024).toFixed(0)} KB/s`;
  return `${(bytesPerSec / (1024 * 1024)).toFixed(1)} MB/s`;
}

function StatRow({
  icon,
  label,
  value,
  pct,
  hint,
}: {
  icon: React.ReactNode;
  label: string;
  value: string;
  pct?: number;
  hint?: string;
}) {
  return (
    <div className="sys-row">
      <span className="sys-row-icon" aria-hidden>
        {icon}
      </span>
      <div className="sys-row-main">
        <div className="sys-row-head">
          <span className="sys-row-label">{label}</span>
          <span className="sys-row-right">
            {hint && <span className="sys-row-hint">{hint}</span>}
            <span className="sys-row-value">{value}</span>
          </span>
        </div>
        {pct !== undefined && (
          <div className="sys-bar" aria-hidden>
            <span style={{ width: `${Math.round(Math.min(100, Math.max(0, pct)))}%` }} />
          </div>
        )}
      </div>
    </div>
  );
}

/** Expanded panel card: CPU, RAM and battery with slim live progress bars. */
export function SystemWidget({
  settings,
  edit,
  onSave,
}: {
  settings?: SystemSettings;
  edit?: boolean;
  onSave?: (s: SystemSettings) => void;
}) {
  const stats = useSystemStats();

  // One poller keeps every System widget (and the collapsed mini bar) fed.
  useEffect(() => {
    startSystemStatsPolling();
    return () => stopSystemStatsPolling();
  }, []);

  if (edit) {
    const metrics: { key: keyof SystemSettings; label: string }[] = [
      { key: "cpu", label: "CPU" },
      { key: "mem", label: "Memory" },
      { key: "net", label: "Network" },
      { key: "temp", label: "Temperature" },
      { key: "batt", label: "Battery" },
    ];
    const patch = (key: keyof SystemSettings, value: boolean) => {
      onSave?.({ ...(settings ?? {}), [key]: value });
    };
    return (
      <div className="widget-edit">
        <p className="widget-edit-hint">Which metrics to show</p>
        {metrics.map((m) => {
          const on = settings?.[m.key] !== false;
          return (
            <div className="settings-row" key={m.key}>
              <span className="settings-label">{m.label}</span>
              <div className="segmented">
                <button
                  type="button"
                  className={on ? "seg-active" : ""}
                  onClick={() => patch(m.key, true)}
                >
                  On
                </button>
                <button
                  type="button"
                  className={on ? "" : "seg-active"}
                  onClick={() => patch(m.key, false)}
                >
                  Off
                </button>
              </div>
            </div>
          );
        })}
      </div>
    );
  }

  if (!stats) return null;

  const memPct = stats.memTotalGb > 0 ? (stats.memUsedGb / stats.memTotalGb) * 100 : 0;
  const cpu = Math.round(stats.cpu);
  const battery = stats.battery;

  return (
    <div className="sys-card">
      {settings?.cpu !== false && (
        <StatRow
          icon={<CpuIcon />}
          label="CPU"
          value={`${cpu}%`}
          pct={cpu}
        />
      )}
      {settings?.mem !== false && (
        <StatRow
          icon={<MemoryIcon />}
          label="Memory"
          value={`${stats.memUsedGb.toFixed(1)} / ${stats.memTotalGb.toFixed(0)} GB`}
          pct={memPct}
        />
      )}
      {settings?.net !== false && stats.network && (
        <StatRow
          icon={<NetIcon />}
          label="Network"
          value={`↓ ${formatRate(stats.network.downBytesPerSec)}  ↑ ${formatRate(stats.network.upBytesPerSec)}`}
        />
      )}
      {settings?.temp !== false && stats.tempC != null && (
        <StatRow
          icon={<TempIcon />}
          label="Temperature"
          value={`${Math.round(stats.tempC)}°C`}
        />
      )}
      {settings?.batt !== false && battery && battery.pct < 255 && (
        <StatRow
          icon={<BatteryIcon charging={battery.charging} />}
          label="Battery"
          value={`${battery.pct}%`}
          pct={battery.pct}
          hint={battery.charging ? "Charging" : undefined}
        />
      )}
    </div>
  );
}

/**
 * Collapsed live mini bar: compact CPU / RAM / battery chips that replace the
 * idle brand+clock the moment a System widget is part of the layout (call and
 * media still take priority). Same poller as the card above.
 */
export function SysMiniBar({ settings }: { settings?: SystemSettings }) {
  const stats = useSystemStats();
  if (!stats) return null;

  const battery = stats.battery;
  const memPct = stats.memTotalGb > 0 ? Math.round((stats.memUsedGb / stats.memTotalGb) * 100) : 0;

  return (
    <div className="sys-mini">
      {settings?.cpu !== false && (
        <span className="sysm-chip" title={`CPU ${Math.round(stats.cpu)}%`}>
          <CpuIcon size={11} />
          {Math.round(stats.cpu)}%
        </span>
      )}
      {settings?.mem !== false && (
        <span className="sysm-chip" title={`Memory ${memPct}%`}>
          <MemoryIcon size={11} />
          {memPct}%
        </span>
      )}
      {settings?.batt !== false && battery && battery.pct < 255 && (
        <span
          className={`sysm-chip sysm-chip-bat${battery.charging ? "" : ""}`}
          data-charging={battery.charging || undefined}
          title={battery.charging ? `Battery ${battery.pct}% — charging` : `Battery ${battery.pct}%`}
        >
          <BatteryIcon size={11} charging={battery.charging} />
          {battery.pct}%
        </span>
      )}
    </div>
  );
}