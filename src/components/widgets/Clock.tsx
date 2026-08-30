import { useEffect, useState } from "react";

interface ClockProps {
  /** When expanded we also show the date; collapsed shows just the time. */
  expanded: boolean;
}

export function Clock({ expanded }: ClockProps) {
  const [now, setNow] = useState(() => new Date());

  useEffect(() => {
    const id = window.setInterval(() => setNow(new Date()), 1000);
    return () => window.clearInterval(id);
  }, []);

  const time = now.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });

  return (
    <div className={expanded ? "clock clock-expanded" : "clock"}>
      <span className="clock-time">{time}</span>
      {expanded && <span className="clock-date">{now.toLocaleDateString([], { weekday: "short", month: "short", day: "numeric" })}</span>}
    </div>
  );
}