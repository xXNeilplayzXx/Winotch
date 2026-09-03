//! Weather — Open-Meteo current conditions, geolocated on Windows via the OS
//! location service; manual city (geocoded) everywhere / as fallback.

use serde::Deserialize;

#[derive(Clone, Deserialize)]
pub struct Weather {
    pub temp_c: f64,
    pub code: u16,
    pub is_day: bool,
    pub wind_kmh: f64,
    pub humidity: u8,
}

#[derive(Deserialize)]
struct OmResp {
    current: OmCurrent,
}
#[derive(Deserialize)]
struct OmCurrent {
    temperature_2m: f64,
    weather_code: u16,
    is_day: u8,
    wind_speed_10m: f64,
    relative_humidity_2m: u8,
}

#[derive(Deserialize)]
struct GeoResp {
    results: Option<Vec<GeoResult>>,
}
#[derive(Deserialize)]
struct GeoResult {
    latitude: f64,
    longitude: f64,
    name: String,
}

/// City name -> (lat, lon, display name) via Open-Meteo's geocoder.
pub fn geocode(city: &str) -> Option<(f64, f64, String)> {
    let url = format!(
        "https://geocoding-api.open-meteo.com/v1/search?name={}&count=1",
        urlencode(city)
    );
    let resp: GeoResp = ureq::get(&url).call().ok()?.into_json().ok()?;
    let r = resp.results?.into_iter().next()?;
    Some((r.latitude, r.longitude, r.name))
}

/// OS location (Windows only). Returns Err with a friendly reason.
pub fn os_geo() -> Result<(f64, f64), String> {
    #[cfg(target_os = "windows")]
    {
        use windows::Devices::Geolocation::{Geolocator, GeolocationAccessStatus};
        let geolocator = Geolocator::new().map_err(|e| e.to_string())?;
        let status = Geolocator::RequestAccessAsync().map_err(|e| e.to_string())?.get().map_err(|e| e.to_string())?;
        if status != GeolocationAccessStatus::Allowed {
            return Err("Windows location access is off — allow it in Settings > Privacy > Location".into());
        }
        let position = geolocator.GetGeopositionAsync().map_err(|e| e.to_string())?.get().map_err(|e| e.to_string())?;
        let coordinate = position.Coordinate().map_err(|e| e.to_string())?;
        let point = coordinate.Point().map_err(|e| e.to_string())?;
        let pos = point.Position().map_err(|e| e.to_string())?;
        Ok((pos.Latitude, pos.Longitude))
    }
    #[cfg(not(target_os = "windows"))]
    {
        Err("OS location is only available on Windows".into())
    }
}

pub fn fetch(lat: f64, lon: f64) -> Option<Weather> {
    let url = format!(
        "https://api.open-meteo.com/v1/forecast?latitude={:.4}&longitude={:.4}&current=temperature_2m,weather_code,is_day,wind_speed_10m,relative_humidity_2m",
        lat, lon
    );
    let resp: OmResp = ureq::get(&url).call().ok()?.into_json().ok()?;
    Some(Weather {
        temp_c: resp.current.temperature_2m,
        code: resp.current.weather_code,
        is_day: resp.current.is_day != 0,
        wind_kmh: resp.current.wind_speed_10m,
        humidity: resp.current.relative_humidity_2m,
    })
}

fn urlencode(s: &str) -> String {
    let mut out = String::new();
    for b in s.bytes() {
        match b {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => out.push(b as char),
            _ => out.push_str(&format!("%{:02X}", b)),
        }
    }
    out
}

/// WMO weather code -> emoji
pub fn emoji(code: u16, is_day: bool) -> &'static str {
    match code {
        0 => if is_day { "☀️" } else { "🌙" },
        1..=2 => if is_day { "🌤️" } else { "☁️" },
        3 => "☁️",
        45 | 48 => "🌫️",
        51..=57 => "🌦️",
        61..=65 | 80..=82 => "🌧️",
        71..=77 | 85 | 86 => "🌨️",
        95..=99 => "⛈️",
        _ => "🌡️",
    }
}
