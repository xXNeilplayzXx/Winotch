namespace Winotch.Platform.Linux;

/// <summary>Stub location service for Linux. GeoClue2 D-Bus integration not yet implemented.</summary>
public sealed class LinuxLocationService : ILocationService
{
    public Task<LocationSnapshot?> GetLocationAsync() => Task.FromResult<LocationSnapshot?>(null);
}
