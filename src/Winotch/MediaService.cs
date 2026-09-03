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
/// play state differs.
///
/// Two failure modes are handled that commonly kill naive implementations:
/// - `GetCurrentSession()` returns the session the system *guesses* the user
///   wants — with several browser tabs or apps holding sessions that is often
///   a stale, paused one. We instead scan `GetSessions()` and prefer a session
///   that is actually Playing.
/// - A transient WinRT failure used to kill the loop for the whole app
///   session. The manager is now re-acquired with a retry backoff, so the
///   notch recovers on its own (e.g. after a fast user switch or a session
///   manager hiccup).
/// Every failure degrades to "no media" — the notch must survive apps locking
/// their sessions.</summary>
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
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                    _manager = manager;
                    if (manager is null)
                    {
                        await Task.Delay(3000, token);
                        continue;
                    }

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
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Transient failure — clear state and retry the manager.
                    _manager = null;
                    if (_hasLast)
                    {
                        _hasLast = false;
                        _lastKey = "";
                        Changed?.Invoke(this, null);
                    }
                    try { await Task.Delay(5000, token); }
                    catch (OperationCanceledException) { break; }
                }
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
        if (BestSession(_manager) is not { } session) return;
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
        if (BestSession(_manager) is { } session)
        {
            try { await session.TrySkipNextAsync(); } catch { }
        }
    }

    public async Task PreviousAsync()
    {
        if (BestSession(_manager) is { } session)
        {
            try { await session.TrySkipPreviousAsync(); } catch { }
        }
    }

    /// <summary>Prefer an actively playing session over the OS "current" one,
    /// which browsers and multi-app setups frequently report wrong.</summary>
    private static GlobalSystemMediaTransportControlsSession? BestSession(
        GlobalSystemMediaTransportControlsSessionManager? manager)
    {
        if (manager is null) return null;
        try
        {
            var playing = manager.GetSessions()
                .FirstOrDefault(s => IsPlaying(s));
            if (playing is not null) return playing;
        }
        catch { }
        try { return manager.GetCurrentSession(); }
        catch { return null; }
    }

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            return session.GetPlaybackInfo()?.PlaybackStatus ==
                   GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch { return false; }
    }

    private static async Task<MediaSnapshot?> ReadAsync(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        try
        {
            if (BestSession(manager) is not { } session) return null;
            var props = await session.TryGetMediaPropertiesAsync();
            if (props is null) return null;
            var title = props.Title ?? "";
            var artist = props.Artist ?? "";
            var info = session.GetPlaybackInfo();
            var playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            // Browsers sometimes expose a session with empty metadata; show
            // something useful instead of a blank card.
            var app = Safe(() => session.SourceAppUserModelId);
            if (title.Length == 0 && playing) title = "Now playing";
            if (artist.Length == 0 && app.Length > 0) artist = CleanAppName(app);

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

    private static string Safe(Func<string> getter)
    {
        try { return getter() ?? ""; }
        catch { return ""; }
    }

    /// <summary>"Microsoft.MSEdge" → "Microsoft Edge", "chrome.exe" → "Chrome".</summary>
    private static string CleanAppName(string id)
    {
        var name = id.Replace("Microsoft.", "", StringComparison.OrdinalIgnoreCase);
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name;
    }
}