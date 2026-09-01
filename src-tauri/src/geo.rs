// Windows location lookup for the weather widget.
//
// "Use Windows location" asks the OS where the device is (the same source the
// Weather app uses) and hands the lat/lon to the frontend, which then talks to
// the Open-Meteo weather API directly. Off Windows — and whenever Windows
// location access is disabled — the command returns a friendly error and the
// widget keeps the manually-entered city.

use serde::Serialize;

#[derive(Clone, Serialize)]
pub struct GeoResult {
    pub lat: f64,
    pub lon: f64,
}

/// Current device position. Errors explain WHY (so the widget can guide the
/// user to Settings → Privacy → Location) instead of failing silently.
#[tauri::command]
pub fn weather_geo() -> Result<GeoResult, String> {
    #[cfg(target_os = "windows")]
    {
        win::current_position()
    }
    #[cfg(not(target_os = "windows"))]
    {
        Err("Windows location is not available on this platform".into())
    }
}

#[cfg(target_os = "windows")]
mod win {
    use super::GeoResult;
    use windows::Devices::Geolocation::{Geolocator, GeolocationAccessStatus};

    pub fn current_position() -> Result<GeoResult, String> {
        let geolocator = Geolocator::new().map_err(|e| e.to_string())?;
        // RequestAccessAsync is an associated function on Geolocator (static).
        let status = Geolocator::RequestAccessAsync()
            .map_err(|e| e.to_string())?
            .get()
            .map_err(|e| e.to_string())?;
        if status != GeolocationAccessStatus::Allowed {
            return Err(
                "Windows location access is off — allow it in Settings → Privacy → Location"
                    .into(),
            );
        }
        let position = geolocator
            .GetGeopositionAsync()
            .map_err(|e| e.to_string())?
            .get()
            .map_err(|e| e.to_string())?;
        // Geocoordinate's Latitude/Longitude are deprecated API; route through
        // Point → Geopoint → BasicGeoposition instead (both are live).
        let coordinate = position.Coordinate().map_err(|e| e.to_string())?;
        let point = coordinate.Point().map_err(|e| e.to_string())?;
        let pos = point.Position().map_err(|e| e.to_string())?;
        Ok(GeoResult {
            lat: pos.Latitude,
            lon: pos.Longitude,
        })
    }
}