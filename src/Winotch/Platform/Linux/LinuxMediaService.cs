namespace Winotch.Platform.Linux;

/// <summary>Stub media service for Linux. MPRIS D-Bus integration not yet implemented.</summary>
public sealed class LinuxMediaService : IMediaService
{
    public event EventHandler<MediaSnapshot?>? Changed;

    public void Start() { }
    public void Stop() { }
    public void Dispose() { }

    public Task PlayAsync() => Task.CompletedTask;
    public Task PauseAsync() => Task.CompletedTask;
    public Task SkipNextAsync() => Task.CompletedTask;
    public Task SkipPreviousAsync() => Task.CompletedTask;
    public Task SeekAsync(double positionSeconds) => Task.CompletedTask;
}
