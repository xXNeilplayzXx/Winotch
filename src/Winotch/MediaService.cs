using Windows.Media.Control;

namespace Winotch;

public sealed record MediaSnapshot(
    string Title, string Artist, bool Playing,
    double PositionSeconds = 0, double DurationSeconds = 0)
{
    public string Key => $"{Title}|{Artist}";
}

/// <summary>Watches the current Windows media session (SMTC). A background
/// loop polls every second and raises Changed only when the track identity or
/// play state differs. Every failure degrades to "no media" — the notch must
/// survive apps locking their sessions.</summary>
public sealed class MediaService : IDisposable
{
    public event EventHandler<MediaSnapshot?>? Changed;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private CancellationTokenSource? _cts;
    private string _lastKey = "";
    private bool _lastPlaying;
    private bool _hasLast;

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager = manager;
                while (!token.IsCancellationRequested)
                {
                    var snapshot = await ReadAsync(manager);
                    if (snapshot is null)
                    {
                        if (_hasLast)
                        {
                            _hasLast = false;
                            _lastKey = "";
                            Changed?.Invoke(this, null);
                        }
                    }
                    else if (!_hasLast || snapshot.Key != _lastKey || snapshot.Playing != _lastPlaying)
                    {
                        _hasLast = true;
                        _lastKey = snapshot.Key;
                        _lastPlaying = snapshot.Playing;
                        Changed?.Invoke(this, snapshot);
                    }
                    await Task.Delay(1000, token);
                }
            }
            catch (OperationCanceledException) { }
            catch
            {
                _manager = null;
                Changed?.Invoke(this, null);
            }
        }, token);
    }

    public void Dispose() => Stop();

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public async Task PlayPauseAsync()
    {
        if (_manager?.GetCurrentSession() is not { } session) return;
        try
        {
            var info = session.GetPlaybackInfo();
            var playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            if (playing) await session.TryPauseAsync();
            else await session.TryPlayAsync();
        }
        catch { }
    }

    public async Task NextAsync()
    {
        if (_manager?.GetCurrentSession() is { } session)
        {
            try { await session.TrySkipNextAsync(); } catch { }
        }
    }

    public async Task PreviousAsync()
    {
        if (_manager?.GetCurrentSession() is { } session)
        {
            try { await session.TrySkipPreviousAsync(); } catch { }
        }
    }

    private static async Task<MediaSnapshot?> ReadAsync(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        try
        {
            if (manager.GetCurrentSession() is not { } session) return null;
            var props = await session.TryGetMediaPropertiesAsync();
            if (props is null) return null;
            var title = props.Title ?? "";
            var artist = props.Artist ?? "";
            var info = session.GetPlaybackInfo();
            var playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            double position = 0, duration = 0;
            try
            {
                var timeline = session.GetTimelineProperties();
                position = timeline.Position.TotalSeconds;
                duration = timeline.EndTime.TotalSeconds;
            }
            catch { }

            return new MediaSnapshot(title, artist, playing, position, duration);
        }
        catch
        {
            return null;
        }
    }
}
