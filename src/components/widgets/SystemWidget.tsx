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
  label,
  value,
  pct,
  hot,
}: {
  label: string;
  value: string;
  pct?: number;
  hot?: boolean;
}) {
  return (
    <div className={`sys-row${hot ? " hot" : ""}`}>
      <span className="sys-label">{label}</span>
      <span className="sys-bar" aria-hidden>
        <i style={{ width: `${Math.round(Math.min(100, Math.max(0, pct ?? 0)))}%` }} />
      </span>
      <span className="sys-value">{value}</span>
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
    <div className="sys-rows">
      {settings?.cpu !== false && <StatRow label="CPU" value={`${cpu}%`} pct={cpu} hot={cpu > 82} />}
      {settings?.temp !== false && stats.tempC != null && (
        <StatRow label="TEMP" value={`${Math.round(stats.tempC)}°C`} pct={((stats.tempC - 35) / 55) * 100} hot={stats.tempC > 75} />
      )}
      {settings?.mem !== false && (
        <StatRow label="MEM" value={`${stats.memUsedGb.toFixed(1)} ${stats.memTotalGb.toFixed(0)}`} pct={memPct} />
      )}
      {settings?.batt !== false && battery && battery.pct < 255 && (
        <StatRow label="BAT" value={`${battery.pct}%${battery.charging ? "⚡" : ""}`} pct={battery.pct} hot={battery.pct < 20} />
      )}
      {settings?.net !== false && stats.network && (
        <StatRow
          label="NET"
          value={`↓ ${formatRate(stats.network.downBytesPerSec)} ↑ ${formatRate(stats.network.upBytesPerSec)}`}
          pct={45}
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