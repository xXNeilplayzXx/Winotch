import { useEffect, useState } from "react";
import type { WeatherSettings } from "../../lib/widgets";

/** WMO weather code → emoji + short label. */
function mapWeather(code: number): { emoji: string; label: string } {
  if (code === 0) return { emoji: "☀️", label: "Clear" };
  if (code === 1) return { emoji: "🌤️", label: "Mostly clear" };
  if (code === 2) return { emoji: "⛅", label: "Partly cloudy" };
  if (code === 3) return { emoji: "☁️", label: "Overcast" };
  if (code === 45 || code === 48) return { emoji: "🌫️", label: "Fog" };
  if (code >= 51 && code <= 57) return { emoji: "🌦️", label: "Drizzle" };
  if (code >= 61 && code <= 67) return { emoji: "🌧️", label: "Rain" };
  if (code >= 71 && code <= 77) return { emoji: "❄️", label: "Snow" };
  if (code >= 80 && code <= 82) return { emoji: "🌦️", label: "Showers" };
  if (code === 85 || code === 86) return { emoji: "🌨️", label: "Snow showers" };
  if (code >= 95) return { emoji: "⛈️", label: "Thunderstorm" };
  return { emoji: "🌡️", label: "Unknown" };
}

interface WeatherData {
  temp: number;
  feels: number;
  max: number;
  min: number;
  emoji: string;
  label: string;
}

interface WeatherWidgetProps {
  settings?: WeatherSettings;
  edit: boolean;
  onSave: (s: WeatherSettings) => void;
  /** Called after a successful save so the card can leave edit mode. */
  onEditDone?: () => void;
}

const clamp = (n: number) => Math.round(n);

export function WeatherWidget({ settings, edit, onSave, onEditDone }: WeatherWidgetProps) {
  const [data, setData] = useState<WeatherData | null>(null);
  const [failed, setFailed] = useState(false);
  const [cityInput, setCityInput] = useState(settings?.city ?? "");
  const [geoErr, setGeoErr] = useState(false);

  const s = settings?.lat != null && settings?.lon != null ? settings : null;

  useEffect(() => {
    if (edit || !s) return;
    let disposed = false;
    setFailed(false);
    setData(null);
    const url =
      `https://api.open-meteo.com/v1/forecast?latitude=${s.lat}&longitude=${s.lon}` +
      `&current=temperature_2m,weather_code,apparent_temperature` +
      `&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1`;
    fetch(url)
      .then((r) => r.json())
      .then((j: { current?: Record<string, number>; daily?: Record<string, number[]> }) => {
        if (disposed) return;
        const cur = j.current ?? {};
        const daily = j.daily ?? {};
        const w = mapWeather(cur.weather_code ?? 0);
        setData({
          temp: cur.temperature_2m ?? 0,
          feels: cur.apparent_temperature ?? cur.temperature_2m ?? 0,
          max: daily.temperature_2m_max?.[0] ?? 0,
          min: daily.temperature_2m_min?.[0] ?? 0,
          emoji: w.emoji,
          label: w.label,
        });
      })
      .catch(() => {
        if (!disposed) setFailed(true);
      });
    return () => {
      disposed = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [edit, s?.lat, s?.lon]);

  if (edit) {
    return (
      <div className="widget-edit">
        <p className="widget-edit-hint">City / location</p>
        <div className="widget-edit-row">
          <input
            className="widget-input"
            value={cityInput}
            placeholder={settings?.city ?? "e.g. Berlin"}
            onChange={(e) => {
              setCityInput(e.target.value);
              setGeoErr(false);
            }}
            onKeyDown={(e) => e.key === "Enter" && saveCity()}
          />
          <button type="button" className="widget-btn" onClick={saveCity}>
            Save
          </button>
        </div>
        {geoErr && <p className="widget-err">Couldn't find that place — try another name.</p>}
      </div>
    );
  }

  if (!settings || s == null) {
    return (
      <div className="widget-edit">
        <p className="widget-edit-hint">
          No location set yet. Open the ⚙ on this card and enter a city.
        </p>
      </div>
    );
  }

  return (
    <div className="weather">
      <span className="weather-emoji">{data ? data.emoji : "🌡️"}</span>
      <div className="weather-main">
        <span className="weather-temp">{data ? `${clamp(data.temp)}°` : "—"}</span>
        <span className="weather-city">{failed ? "Offline" : settings.city}</span>
      </div>
      <div className="weather-side">
        {data && (
          <>
            <span className="weather-label">{data.label}</span>
            <span className="weather-range">
              H {clamp(data.max)}° L {clamp(data.min)}° · feels {clamp(data.feels)}°
            </span>
          </>
        )}
      </div>
    </div>
  );

  async function saveCity() {
    const q = cityInput.trim();
    if (!q) return;
    setGeoErr(false);
    try {
      const r = await fetch(
        `https://geocoding-api.open-meteo.com/v1/search?name=${encodeURIComponent(q)}&count=1&language=en&format=json`,
      );
      const j: { results?: { name: string; country?: string; latitude: number; longitude: number }[] } =
        await r.json();
      const hit = j.results?.[0];
      if (hit) {
        const city = `${hit.name}${hit.country ? `, ${hit.country}` : ""}`;
        onSave({ city, lat: hit.latitude, lon: hit.longitude });
        setCityInput(city);
        onEditDone?.();
      } else {
        setGeoErr(true);
      }
    } catch {
      setGeoErr(true);
    }
  }
}