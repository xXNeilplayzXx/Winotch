namespace Winotch.Platform;

/// <summary>Snapshot of currently playing media.</summary>
public sealed record MediaSnapshot(
    string Title, string Artist, bool Playing,
    double PositionSeconds = 0, double DurationSeconds = 0,
    string? ArtBase64 = null, bool CanSeek = false)
{
    public string Key => $"{Title}|{Artist}";
}

/// <summary>Abstracts media playback monitoring and control.
/// Windows: SMTC (System Media Transport Controls)
/// Linux: MPRIS (D-Bus org.mpris.MediaPlayer2)</summary>
public interface IMediaService : IDisposable
{
    event EventHandler<MediaSnapshot?>? Changed;
    void Start();
    void Stop();
    Task PlayAsync();
    Task PauseAsync();
    Task SkipNextAsync();
    Task SkipPreviousAsync();
    Task SeekAsync(double positionSeconds);
}
