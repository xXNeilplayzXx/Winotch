import { useEffect, useState } from "react";

interface ClockProps {
  /** When expanded (deck widget card) we render the big instrument readout
   *  with a blinking colon; collapsed (island strip) keeps the small clock. */
  expanded: boolean;
}

export function Clock({ expanded }: ClockProps) {
  const [now, setNow] = useState(() => new Date());

  useEffect(() => {
    const id = window.setInterval(() => setNow(new Date()), 1000);
    return () => window.clearInterval(id);
  }, []);

  // Big deck readout: leading hour, blinking colon, trailing minute (+ am/pm).
  if (expanded) {
    const [lead, tail] = now
      .toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", hour12: true })
      .split(":");
    const date = now.toLocaleDateString([], { weekday: "long", month: "long", day: "numeric" });
    return (
      <div className="clock-widget">
        <span className="big">
          <span className="lead">{lead}</span>
          <span className="colon" aria-hidden>
            :
          </span>
          <span className="tail">{tail}</span>
        </span>
        <span className="date">{date}</span>
      </div>
    );
  }

  const time = now.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });

  return (
    <div className="clock">
      <span className="clock-time">{time}</span>
    </div>
  );
}
