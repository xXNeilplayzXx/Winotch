#if WINDOWS
using Windows.Devices.Geolocation;

namespace Winotch;

/// <summary>Windows location via the WinRT Geolocator (the legacy app's "Use
/// Windows location" weather flow). Requires location permission in Settings →
/// Privacy → Location; failures return null so the caller can explain.</summary>
public static class WindowsLocation
{
    public static async Task<(double Lat, double Lon)?> ResolveAsync()
    {
        try
        {
            var access = await Geolocator.RequestAccessAsync();
            if (access != GeolocationAccessStatus.Allowed) return null;
            var locator = new Geolocator { DesiredAccuracy = PositionAccuracy.Default };
            var position = await locator.GetGeopositionAsync();
            var coordinate = position?.Coordinate;
            if (coordinate is null) return null;
            return (coordinate.Latitude, coordinate.Longitude);
        }
        catch
        {
            return null;
        }
    }
    }
}
#endif