namespace Winotch.Platform;

/// <summary>Snapshot of geographic location.</summary>
public sealed record LocationSnapshot(
    double Latitude,
    double Longitude);

/// <summary>Abstracts geolocation.
/// Windows: WinRT Geolocator
/// Linux: GeoClue2 D-Bus or freedesktop portal Location</summary>
public interface ILocationService
{
    Task<LocationSnapshot?> GetLocationAsync();
}
