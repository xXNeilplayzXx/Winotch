namespace Winotch.Platform.Windows;

/// <summary>Windows location service using WinRT Geolocator.</summary>
public sealed class WindowsLocationService : ILocationService
{
    public async Task<LocationSnapshot?> GetLocationAsync()
    {
        var result = await WindowsLocation.ResolveAsync();
        if (result is null) return null;
        return new LocationSnapshot(result.Lat, result.Lon);
    }
}
