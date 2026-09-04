using SkiaSharp;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Winotch;

public sealed record MediaSnapshot(
    string Title, string Artist, bool Playing,
    double PositionSeconds = 0, double DurationSeconds = 0,
    string? ArtBase64 = null, bool CanSeek = false)
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
    private string _lastArtKey = "";
    private string? _lastArt;

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
                                _lastArtKey = "";
                                _lastArt = null;
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
                        _lastArtKey = "";
                        _lastArt = null;
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

    /// <summary>Seek the current session to an absolute position, in seconds.
    /// Only meaningful when the session reports IsPlaybackPositionEnabled.</summary>
    public async Task SeekAsync(double seconds)
    {
        if (BestSession(_manager) is { } session)
        {
            try
            {
                await session.TryChangePlaybackPositionAsync(
                    (long)(seconds * TimeSpan.TicksPerSecond));
            }
            catch { }
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

    private async Task<MediaSnapshot?> ReadAsync(GlobalSystemMediaTransportControlsSessionManager manager)
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

            // Album art is re-derivable on every 1s poll; decode it only once
            // per track identity (the base64 is byte-identical across polls).
            var artKey = $"{title}|{artist}";
            string? art;
            if (string.Equals(artKey, _lastArtKey, StringComparison.Ordinal))
            {
                art = _lastArt;
            }
            else
            {
                art = await DecodeArtworkAsync(props.Thumbnail);
                _lastArtKey = artKey;
                _lastArt = art;
            }
            var canSeek = info?.Controls?.IsPlaybackPositionEnabled ?? false;
            return new MediaSnapshot(title, artist, playing, position, duration, art, canSeek);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Read the SMTC album-art thumbnail and downscale it to a small
    /// PNG base64 the shell can draw (capped so a huge artwork never bloats
    /// memory). Null when the app exposes no art or it cannot be decoded.</summary>
    private static async Task<string?> DecodeArtworkAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null) return null;
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            var size = stream.Size;
            if (size <= 0 || size > 4_000_000) return null;

            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)size);
            var bytes = new byte[(long)size];
            reader.ReadBytes(bytes);

            using var data = SKData.CreateCopy(bytes);
            using var decoded = SKBitmap.Decode(data);
            if (decoded is null) return null;

            const int maxDim = 96;
            var scale = Math.Min(1.0, (double)maxDim / Math.Max(decoded.Width, decoded.Height));
            if (scale >= 1.0)
            {
                using var image = SKImage.FromBitmap(decoded);
                using var png = image.Encode(SKEncodedImageFormat.Png, 80);
                return Convert.ToBase64String(png.ToArray());
            }

            var info = new SKImageInfo(
                Math.Max(1, (int)Math.Round(decoded.Width * scale)),
                Math.Max(1, (int)Math.Round(decoded.Height * scale)),
                SKColorType.Rgba8888, SKAlphaType.Premul);
            using var resized = decoded.Resize(info, SKFilterQuality.Medium);
            if (resized is null) return null;
            using var image2 = SKImage.FromBitmap(resized);
            using var png2 = image2.Encode(SKEncodedImageFormat.Png, 80);
            return Convert.ToBase64String(png2.ToArray());
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