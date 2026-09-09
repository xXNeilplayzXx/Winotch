using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Winotch.Engine;
using Winotch.Platform;
#if WINDOWS
using Winotch.Platform.Windows;
#else
using Winotch.Platform.Linux;
#endif

namespace Winotch;

/// <summary>The app's host surface: what the tray and windows call for
/// app-level operations. The manager owns one or more overlay windows.</summary>
public interface INotchHost
{
    bool IsPaused { get; }
    void SetPaused(bool paused);
    bool IsHidden { get; }
    void HideNotch();
    void ShowNotch();
    void OpenSettingsWindow();
    void OpenSetupWizard();
    void CheckForUpdates(bool manual = false);
    void RestartApplication();
    void FactoryReset();
    void QuitApplication();
}

/// <summary>Conductor for the whole app: owns the shared services (media,
/// clipboard, weather, status, privacy), the per-monitor overlay windows, the
/// settings window and the tray. Polls display layout so "All monitors" keeps
/// one island per screen and Follow-active tracks the foreground app.</summary>
public sealed class OverlayWindowManager : INotchHost, IDisposable
{
    private readonly SettingsService _settings;
    private readonly StartupService _startup;
    private readonly ThemeService _theme = new();
    private readonly IMediaService _media;
    private readonly PriorityStatusTracker _status = new();
    private readonly IPrivacyConsentStore _privacy;
    private readonly ClipboardHistoryStore _clipboardStore = new();
    private readonly WeatherService _weather = new();
    private readonly IBatteryService _batteryService;
    private readonly ISystemVolumeService _volumeService;
    private readonly ISystemStatsService _statsService;
    private readonly IClipboardService _clipboardService;
    private readonly ILocationService _locationService;
    private readonly IMonitorService _monitorService;
    private readonly IScreenCapture _screenCapture;
    private readonly List<OverlayWindow> _windows = new();
    private readonly UpdateService _updateService = new();

    private readonly Timer _clipboardTimer;
    private readonly List<Timer> _timers = new();
    private SettingsWindow? _settingsWindow;
    private TrayIconService? _tray;
    private CancellationTokenSource? _wifiCts;
    private string? _wifiName;
    private BatteryInfo _battery = new(100, false);
    private bool _lastMic, _lastCam;
    private string? _lastCallApp;
    private string? _lastToastKey = "";
    private MediaSnapshot? _currentMedia;
    private string _lastMonitorLayout = "";
    private DisplayMode _lastDisplayMode;
    private string _lastDisplayDevice = "";
    private bool _disposed;
    private bool _updateCheckInFlight;

    public OverlayWindowManager(SettingsService settings, StartupService startup)
    {
        _settings = settings;
        _startup = startup;

        // Initialize platform-specific services
#if WINDOWS
        _privacy = new RegistryPrivacyConsentStore();
        _batteryService = new WindowsBatteryService();
        _volumeService = new WindowsSystemVolumeService();
        _statsService = new WindowsSystemStatsService();
        _clipboardService = new WindowsClipboardService();
        _locationService = new WindowsLocationService();
        _monitorService = new WindowsMonitorService();
        _screenCapture = new WindowsScreenCapture();
        _media = new MediaService();
#else
        _privacy = new LinuxPrivacyConsentStore();
        _batteryService = new LinuxBatteryService();
        _volumeService = new LinuxSystemVolumeService();
        _statsService = new LinuxSystemStatsService();
        _clipboardService = new LinuxClipboardService();
        _locationService = new LinuxLocationService();
        _monitorService = new LinuxMonitorService();
        _screenCapture = new LinuxScreenCapture();
        _media = new LinuxMediaService();
#endif

        _theme.Refresh(settings.Current);
        DialogUi.Current = _theme.Current;
        _lastDisplayMode = settings.Current.Appearance.Display;
        _lastDisplayDevice = settings.Current.Appearance.DisplayDeviceName;
        _settings.Changed += OnSettingsChanged;
        _theme.ThemeChanged += OnThemeChanged;

        _media.Changed += OnMediaChanged;

        _clipboardTimer = new Timer(_ => PollClipboard(), null, TimeSpan.FromMilliseconds(2000), TimeSpan.FromMilliseconds(2000));
    }

    // ------------------------------------------------------------- startup

    /// <summary>Push the Appearance font choice into the Skia text engine.</summary>
    private void ApplyUiFont()
    {
        var fontId = _settings.Current.Appearance.Font;
        var family = FontChoice.FamilyOf(fontId);
        var customFile = FontChoice.CustomFile(fontId);
        TextKit.SetUiFamily(family, customFile);
#if WINDOWS
        Application.Current!.Resources["NotchUiFont"] = WpfFontFamily(fontId, family);
#endif
    }

#if WINDOWS
    /// <summary>The WPF font for a font id: a plain family for curated /
    /// system fonts, or a file-relative FontFamily for an imported .ttf/.otf
    /// (the family name embedded in the file drives the WPF face).</summary>
    private static System.Windows.Media.FontFamily WpfFontFamily(string fontId, string family)
    {
        var customFile = FontChoice.CustomFile(fontId);
        if (customFile is null)
        {
            return new System.Windows.Media.FontFamily(family);
        }
        var directory = Path.GetDirectoryName(customFile) ?? "";
        return new System.Windows.Media.FontFamily(new Uri(directory + Path.DirectorySeparatorChar, UriKind.Absolute),
            $"./{Path.GetFileName(customFile)}#{family}");
    }
#endif

    public void Start()
    {
        ApplyUiFont();
        BuildWindows();
        StartTimers();
        PollBattery();
        PollPrivacy();
        _ = FetchWeatherAsync();
        PollClipboard();
        _media.Start();
        _ = CheckRainAlertAsync();

        if (_settings.Current.General.AutoCheckUpdates)
        {
            // Let the shell settle and the release hit the network after a beat.
            Timer? check = null;
            check = new Timer(_ =>
            {
                check?.Dispose();
                Dispatcher.UIThread.Post(() => CheckForUpdates(manual: false));
            }, null, TimeSpan.FromSeconds(6), Timeout.InfiniteTimeSpan);
        }
    }

    private void StartTimers()
    {
        void UICallback(Action action) => Dispatcher.UIThread.Post(action);

        var status = new Timer(_ => UICallback(PollBattery), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        _timers.Add(status);

        _wifiCts = new CancellationTokenSource();
        var token = _wifiCts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try { _wifiName = WifiProbe.ReadSsid(); }
                catch { _wifiName = null; }
                token.WaitHandle.WaitOne(10_000);
            }
        }, token);

        var privacy = new Timer(_ => UICallback(PollPrivacy), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        _timers.Add(privacy);

        var layout = new Timer(_ => UICallback(RebuildIfLayoutChanged), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        _timers.Add(layout);

        var theme = new Timer(_ => UICallback(() => _theme.Refresh(_settings.Current)), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        _timers.Add(theme);

        var weather = new Timer(_ => UICallback(async () => await FetchWeatherAsync()), null, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));
        _timers.Add(weather);

        // Rain watch: probe precipitation every 5 minutes; the evaluator only
        // fires when rain *starts* within 30 minutes, so the 5-minute cadence
        // can't miss the beginning of an episode.
        var rain = new Timer(_ => UICallback(async () => await CheckRainAlertAsync()), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        _timers.Add(rain);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _clipboardTimer.Dispose();
        foreach (var timer in _timers) timer.Dispose();
        _timers.Clear();
        _wifiCts?.Cancel();
        foreach (var window in _windows.ToArray()) window.Dispose();
        _windows.Clear();
        _tray?.Dispose();
        _tray = null;
        _media.Dispose();
        _weather.Dispose();
        _settings.Changed -= OnSettingsChanged;
        _theme.ThemeChanged -= OnThemeChanged;
        _media.Changed -= OnMediaChanged;
    }

    // ------------------------------------------------------------- windows

    private void BuildWindows()
    {
        var monitors = _monitorService.GetAllMonitors();
        var appearance = _settings.Current.Appearance;
        IReadOnlyList<MonitorSnapshot> targets;

        if (appearance.Display == DisplayMode.FollowActive)
        {
            var monitor = SelectActiveMonitor(monitors);
            targets = new[] { monitor };
        }
        else if (appearance.Display == DisplayMode.All)
        {
            targets = monitors;
        }
        else if (appearance.Display == DisplayMode.Named)
        {
            var primary = monitors.FirstOrDefault(m => m.IsPrimary);
            var named = monitors.FirstOrDefault(m => string.Equals(m.DeviceName, appearance.DisplayDeviceName, StringComparison.OrdinalIgnoreCase));
            var monitor = !string.IsNullOrEmpty(appearance.DisplayDeviceName) && named.DeviceName != null
                ? named
                : primary.DeviceName != null ? primary : monitors[0];
            targets = new[] { monitor };
        }
        else
        {
            var primary = monitors.FirstOrDefault(m => m.IsPrimary);
            targets = new[] { primary.DeviceName != null ? primary : monitors[0] };
        }

        var desired = targets.Select(t => t.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var i = _windows.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(_windows[i].Monitor.DeviceName))
            {
                _windows[i].Close();
                _windows.RemoveAt(i);
            }
        }

        foreach (var target in targets)
        {
            var existing = _windows.FirstOrDefault(w =>
                string.Equals(w.Monitor.DeviceName, target.DeviceName, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                var window = new OverlayWindow(_settings, _theme, _startup, this);
                window.AssignMonitor(target);
                window.Show();
                _windows.Add(window);
            }
            else
            {
                existing.AssignMonitor(target);
            }
        }

        RecreateTray();
    }

    /// <summary>Select the monitor for FollowActive mode: foreground window center,
    /// else cursor monitor, else primary.</summary>
    private MonitorSnapshot SelectActiveMonitor(IReadOnlyList<MonitorSnapshot> monitors)
    {
        if (monitors.Count == 0) throw new ArgumentException("No monitors available");

        var fgRect = _monitorService.GetForegroundWindowRect();
        if (fgRect is { } rect)
        {
            var centerX = rect.X + rect.Width / 2;
            var centerY = rect.Y + rect.Height / 2;
            var containing = _monitorService.GetMonitorContainingPoint(centerX, centerY);
            if (containing.HasValue) return containing.Value;
        }

        var cursor = _monitorService.GetCursorPosition();
        var cursorMonitor = _monitorService.GetMonitorContainingPoint(cursor.X, cursor.Y);
        if (cursorMonitor.HasValue) return cursorMonitor.Value;

        var primary = monitors.FirstOrDefault(m => m.IsPrimary);
        return primary.DeviceName != null ? primary : monitors[0];
    }

    /// <summary>Follow-active tracks the monitor under the foreground window
    /// (falling back to the cursor monitor). Called from the layout poll so
    /// the single island hops between screens live.</summary>
    private void RebuildIfLayoutChanged()
    {
        var monitors = _monitorService.GetAllMonitors();
        var layoutKey = string.Join(",", monitors.Select(m => $"{m.DeviceName}:{m.Bounds.Left},{m.Bounds.Top},{m.Bounds.Width},{m.Bounds.Height}"));
        if (layoutKey != _lastMonitorLayout)
        {
            _lastMonitorLayout = layoutKey;
            BuildWindows();
            return;
        }

        // Follow-active: keep the single window on the active monitor.
        if (_settings.Current.Appearance.Display == DisplayMode.FollowActive && _windows.Count == 1)
        {
            var monitor = SelectActiveMonitor(monitors);
            if (!string.Equals(_windows[0].Monitor.DeviceName, monitor.DeviceName, StringComparison.OrdinalIgnoreCase))
            {
                _windows[0].AssignMonitor(monitor);
            }
        }
    }

    private void OnSettingsChanged(object? sender, WinotchSettings settings)
    {
        ApplyUiFont();
        _theme.Refresh(settings);
        foreach (var window in _windows) window.Shell.ApplyTheme();
        DialogUi.Current = _theme.Current;
        _settingsWindow?.ApplyTheme(_theme.Current);
        RebuildIfLayoutChanged();

        // Display targeting is not a layout change — rebuilding on the mode /
        // device change is what actually moves the island(s) (item: the
        // display settings did nothing before).
        if (settings.Appearance.Display != _lastDisplayMode ||
            !string.Equals(settings.Appearance.DisplayDeviceName, _lastDisplayDevice,
                StringComparison.OrdinalIgnoreCase))
        {
            _lastDisplayMode = settings.Appearance.Display;
            _lastDisplayDevice = settings.Appearance.DisplayDeviceName;
            BuildWindows();
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        foreach (var window in _windows) window.Shell.ApplyTheme();
        DialogUi.Current = _theme.Current;
        // A real palette change re-skins the settings window's rows.
        _settingsWindow?.ApplyTheme(_theme.Current, rebuild: true);
    }

    private void RecreateTray()
    {
        _tray?.Dispose();
        _tray = null;
        var primary = _windows.FirstOrDefault(w => w.Monitor.IsPrimary) ?? _windows.FirstOrDefault();
        if (primary is null) return;
        _tray = new TrayIconService(this, primary, _settings, _startup);
    }

    // ---------------------------------------------------------- INotchHost

    public bool IsPaused => _windows.Any(w => w.IsPaused);

    public void SetPaused(bool paused)
    {
        foreach (var window in _windows) window.SetPaused(paused);
    }

    public bool IsHidden => _windows.All(w => w.IsHidden);

    public void HideNotch()
    {
        foreach (var window in _windows) window.HideNotch();
    }

    public void ShowNotch()
    {
        foreach (var window in _windows) window.ShowNotch();
    }

    /// <summary>Enter pill drag mode from Settings: make sure the notch is
    /// visible, then switch every island to its Done / Reset drag pill.</summary>
    public void EnterDragMode()
    {
        ShowNotch();
        foreach (var window in _windows) window.Shell.EnterDragMode();
    }

    /// <summary>Check GitHub for a newer release. Automatic checks stay
    /// silent unless an update is found; manual checks always report the
    /// outcome as a toast.</summary>
    public async void CheckForUpdates(bool manual = false)
    {
        if (_updateCheckInFlight) return;
        _updateCheckInFlight = true;
        try
        {
            var info = await _updateService.CheckAsync();
            if (info is null)
            {
                if (manual)
                {
                    ShowToast(new ToastSpec("", "Update check failed",
                        "Couldn't reach GitHub — check your connection (the repo must be public for auto-updates).", 6));
                }
                return;
            }
            if (!info.IsNewerThan(UpdateService.CurrentVersion))
            {
                if (manual)
                {
                    ShowToast(new ToastSpec("", "Up to date",
                        $"You're running the latest version ({UpdateService.CurrentVersionText}).", 4.5));
                }
                return;
            }
            if (!manual && _settings.Current.General.LastPromptedUpdate == info.Tag) return;

            var window = new UpdateWindow(info);
            window.ShowDialog(null!).GetAwaiter().GetResult();
            _settings.Update(x => x with
            {
                General = x.General with { LastPromptedUpdate = info.Tag },
            });
            if (window.StagedExe is { } staged)
            {
                UpdateService.ApplyAndRestart(staged, window.DownloadPath);
                QuitApplication();
            }
        }
        finally
        {
            _updateCheckInFlight = false;
        }
    }

    public void OpenSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settings, _startup, OpenSetupWizard, FactoryReset, EnterDragMode,
                () => CheckForUpdates(manual: true));
            _settingsWindow.ApplyTheme(_theme.Current);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void OpenSetupWizard()
    {
        var window = new SetupWindow(_settings);
        window.ShowDialog(null!);
    }

    public void QuitApplication()
    {
        Dispose();
        Environment.Exit(0);
    }

    /// <summary>Wipe every setting back to defaults, delete the settings file
    /// so the setup wizard greets the next launch, and restart clean.</summary>
    public void FactoryReset()
    {
        _settings.Update(_ => WinotchSettings.Defaults);
        try
        {
            var path = SettingsService.DefaultPath;
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        RestartApplication();
    }

    public void RestartApplication()
    {
        var executable = Environment.ProcessPath ?? StartupService.CurrentExecutablePath();
        try
        {
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
        }
        catch
        {
            // Launch failed — stay running.
        }
        QuitApplication();
    }

    // ------------------------------------------------------- media + data

    private void OnMediaChanged(object? sender, MediaSnapshot? snapshot)
    {
        _currentMedia = snapshot;
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var window in _windows) window.Shell.SetMedia(snapshot);
                if (snapshot is { Playing: true } media && media.Title.Length > 0 &&
                    !string.Equals(media.Key, _lastToastKey, StringComparison.Ordinal) &&
                    _settings.Current.Toasts.MediaToastsEnabled)
                {
                    _lastToastKey = media.Key;
                    var spec = new ToastSpec("", media.Title, media.Artist, 4.5);
                    foreach (var window in _windows) window.Shell.ShowToast(spec);
                }
            });
        }
        catch
        {
            // App is shutting down or the dispatcher is gone — drop the event.
        }
    }

    public async Task MediaPlayPauseAsync()
    {
        // Toggle based on current playback state
        var currentMedia = _currentMedia;
        if (currentMedia?.Playing == true)
            await _media.PauseAsync();
        else
            await _media.PlayAsync();
    }
    public async Task MediaNextAsync() => await _media.SkipNextAsync();
    public async Task MediaPreviousAsync() => await _media.SkipPreviousAsync();
    public async Task MediaSeekAsync(double seconds) => await _media.SeekAsync(seconds);

    public void MediaVolumeChanged(float level01) => _volumeService.SetLevel(level01);
    public void MediaVolumeMuteToggled() => _volumeService.SetMuted(!_volumeService.IsMuted);

    private void PollBattery()
    {
        var batterySnapshot = _batteryService.GetSnapshot();
        _battery = batterySnapshot is not null
            ? new BatteryInfo((int)batterySnapshot.ChargePercent, batterySnapshot.IsCharging)
            : new BatteryInfo(100, false);

        var resourceSnapshot = _statsService.GetSnapshot();
        var stats = new SystemStatsSnapshot(
            CpuPercent: resourceSnapshot.CpuUsagePercent,
            MemoryPercent: resourceSnapshot.MemoryTotalBytes > 0
                ? (double)resourceSnapshot.MemoryUsedBytes / resourceSnapshot.MemoryTotalBytes * 100
                : 0,
            NetDownKbps: 0, // Would need to calculate from deltas
            NetUpKbps: 0);

        // System volume is pushed alongside battery so the Now playing card's
        // slider reflects keyboard/mixer changes within ~2s.
        var volumeLevel = _volumeService.Level01;
        var volumeMuted = _volumeService.IsMuted;
        var volumeAvailable = _volumeService.IsAvailable;
        foreach (var window in _windows)
        {
            window.Shell.SetBattery(_battery);
            window.Shell.SetSystemStats(stats);
            window.Shell.SetVolume(volumeLevel, volumeMuted, volumeAvailable);
        }
        FeedStatusTracker();
    }

    private void PollPrivacy()
    {
        _lastMic = _privacy.IsMicrophoneActive();
        _lastCam = _privacy.IsCameraActive();
        foreach (var window in _windows) window.Shell.SetPrivacy(_lastMic, _lastCam);
        FeedStatusTracker();

        var app = CallDetector.FindCallApp(_lastMic || _lastCam);
        if (!string.Equals(app, _lastCallApp, StringComparison.OrdinalIgnoreCase))
        {
            _lastCallApp = app;
            var state = app is null
                ? null
                : new CallState(true, CallDetector.DisplayName(app), DateTimeOffset.UtcNow);
            foreach (var window in _windows) window.Shell.SetCall(state);
        }
    }

    private void FeedStatusTracker()
    {
        if (!_settings.Current.Features.PriorityAlertsEnabled) return;
        var snapshot = new PriorityStatusSnapshot(_battery, new WifiStatus(_wifiName), _lastMic, _lastCam);
        for (var i = 0; i < 4; i++)
        {
            if (_status.Next(snapshot) is not { } alert) continue;
            foreach (var window in _windows) window.Shell.ShowStatusToast(alert);
        }
    }

    private void PollClipboard()
    {
        if (!_settings.Current.Features.ClipboardHistoryEnabled) return;
        try
        {
            if (_clipboardService.ContainsText())
            {
                var text = _clipboardService.GetText();
                if (text is not null && _clipboardStore.TryAdd("text", text, null, null, text, DateTimeOffset.Now))
                {
                    PushClipboard();
                }
                return;
            }

            if (_clipboardService.ContainsFiles())
            {
                var files = _clipboardService.GetFiles();
                if (files is { Length: > 0 })
                {
                    var fingerprint = string.Join('\n', files);
                    if (_clipboardStore.TryAdd("files", null, files, null, fingerprint, DateTimeOffset.Now))
                    {
                        PushClipboard();
                    }
                }
                return;
            }

            if (_clipboardService.ContainsImage())
            {
                var thumb = _clipboardService.GetImageAsPngBase64(out var bytes);
                if (thumb is not null)
                {
                    var fingerprint = Convert.ToHexString(
                        System.Security.Cryptography.SHA256.HashData(bytes));
                    if (_clipboardStore.TryAdd("image", null, null, thumb, fingerprint, DateTimeOffset.Now))
                    {
                        PushClipboard();
                    }
                }
            }
        }
        catch (COMException) { }
        catch (ExternalException) { }
    }

    private void PushClipboard()
    {
        foreach (var window in _windows) window.Shell.SetClipboard(_clipboardStore.Items);
    }

    public void CopyClipboardEntry(long id)
    {
        var entry = _clipboardStore.Items.FirstOrDefault(i => i.Id == id);
        if (entry is null) return;
        try
        {
            switch (entry.Kind)
            {
                case "text":
                    _clipboardService.SetText(entry.Text ?? "");
                    break;
                case "files" when entry.Files is { Length: > 0 }:
                    _clipboardService.SetFiles(entry.Files);
                    break;
                case "image" when entry.ThumbBase64 is { } b64:
                    _clipboardService.SetImage(Convert.FromBase64String(b64));
                    break;
            }
        }
        catch (COMException) { }
        catch (ExternalException) { }
    }

    public void DeleteClipboardEntry(long id)
    {
        _clipboardStore.Remove(id);
        PushClipboard();
    }

    public void ClearClipboard()
    {
        _clipboardStore.Clear();
        PushClipboard();
    }

    // ----------------------------------------------------- widget editors

    public void OpenNotesEditor()
    {
        var widgets = _settings.Current.Widgets;
        var window = new NotesEditWindow(widgets.Notes, widgets.SizeFor("notes"));
        if (window.ShowDialog<bool?>(null!).GetAwaiter().GetResult() == true && window.Result is { } text)
        {
            var scale = window.Scale;
            _settings.Update(s => s with
            {
                Widgets = s.Widgets.WithSize("notes", scale) with { Notes = text },
            });
        }
    }

    public void OpenWeatherEditor()
    {
        var widgets = _settings.Current.Widgets;
        var window = new WeatherEditWindow(widgets.Weather, widgets.SizeFor("weather"));
        if (window.ShowDialog<bool?>(null!).GetAwaiter().GetResult() != true) return;
        // Both outcome paths below update settings themselves, so the card
        // size choice is saved here first.
        _settings.Update(s => s with { Widgets = s.Widgets.WithSize("weather", window.Scale) });
        if (window.City is { } city)
        {
            _ = ResolveAndFetchWeatherAsync(city);
        }
        else if (window.UseSystemLocation)
        {
            _ = ResolveWindowsLocationAsync();
        }
    }

    public void OpenLauncherEditor()
    {
        var widgets = _settings.Current.Widgets;
        var window = new LauncherEditWindow(widgets.LauncherApps, widgets.SizeFor("launcher"));
        if (window.ShowDialog<bool?>(null!).GetAwaiter().GetResult() == true && window.Result is { } apps)
        {
            var scale = window.Scale;
            _settings.Update(s => s with
            {
                Widgets = s.Widgets.WithSize("launcher", scale) with { LauncherApps = apps },
            });
        }
    }

    public void OpenFocusEditor()
    {
        var widgets = _settings.Current.Widgets;
        var window = new FocusEditWindow(widgets.FocusMinutes, widgets.BreakMinutes,
            widgets.FocusAutoCycle, widgets.SizeFor("focus"));
        if (window.ShowDialog<bool?>(null!).GetAwaiter().GetResult() != true || window.Result is not { } result) return;
        var scale = window.Scale;
        _settings.Update(s => s with
        {
            Widgets = s.Widgets.WithSize("focus", scale) with
            {
                FocusMinutes = result.FocusMinutes,
                BreakMinutes = result.BreakMinutes,
                FocusAutoCycle = result.AutoCycle,
            },
        });
    }

    public void OpenMetricsEditor()
    {
        var widgets = _settings.Current.Widgets;
        var window = new MetricsEditWindow(widgets.SystemMetrics, widgets.SizeFor("system"));
        if (window.ShowDialog<bool?>(null!).GetAwaiter().GetResult() == true && window.Result is { } result)
        {
            var scale = window.Scale;
            _settings.Update(s => s with
            {
                Widgets = s.Widgets.WithSize("system", scale) with { SystemMetrics = result },
            });
        }
    }

    /// <summary>Size-only editor for cards without their own settings (clock,
    /// now playing, clipboard).</summary>
    public void OpenCardSizeEditor(string id)
    {
        var widgets = _settings.Current.Widgets;
        var window = new CardSizeWindow(WidgetSettings.Labels[id], widgets.SizeFor(id));
        if (window.ShowDialog<bool?>(null!).GetAwaiter().GetResult() != true) return;
        _settings.Update(s => s with { Widgets = s.Widgets.WithSize(id, window.Scale) });
    }

    public void LaunchApp(LauncherApp app)
    {
        try
        {
            Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true });
            ShowToast(new ToastSpec("", "Launched", app.Label, 3.2));
        }
        catch
        {
            ShowToast(new ToastSpec("", "Launch failed", $"Couldn't start “{app.Label}” — check the path in the launcher settings.", 5));
        }
    }

    private void ShowToast(ToastSpec spec)
    {
        foreach (var window in _windows) window.Shell.ShowToast(spec);
    }

    // ------------------------------------------------------------- weather

    private async Task ResolveAndFetchWeatherAsync(string city)
    {
        try
        {
            var found = await _weather.SearchAsync(city);
            if (found is not { } hit)
            {
                ShowToast(new ToastSpec("", "Weather", $"Couldn't find “{city}” — check the name and try again.", 5));
                return;
            }
            _settings.Update(s => s with
            {
                Widgets = s.Widgets with
                {
                    Weather = new WeatherWidgetConfig { City = hit.Name, Lat = hit.Lat, Lon = hit.Lon },
                },
            });
            await FetchWeatherAsync();
        }
        catch (Exception ex)
        {
            ShowToast(new ToastSpec("", "Weather", WeatherFailureText(ex), 6));
        }
    }

    /// <summary>Resolve the user's location (via platform geolocation service)
    /// into a saved weather location, like the legacy "Use Windows location".</summary>
    private async Task ResolveWindowsLocationAsync()
    {
        try
        {
            var location = await _locationService.GetLocationAsync();
            if (location is not { } loc)
            {
                var message = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "Windows location is unavailable — allow location in Settings → Privacy → Location, then retry."
                    : "Location is unavailable — check your system location settings and permissions, then retry.";
                ShowToast(new ToastSpec("", "Weather", message, 6));
                return;
            }
            var hit = await _weather.SearchByCoordinatesAsync(loc.Latitude, loc.Longitude);
            var city = hit?.Name ?? $"({loc.Latitude:0.##}, {loc.Longitude:0.##})";
            _settings.Update(s => s with
            {
                Widgets = s.Widgets with
                {
                    Weather = new WeatherWidgetConfig { City = city, Lat = loc.Latitude, Lon = loc.Longitude },
                },
            });
            await FetchWeatherAsync();
        }
        catch
        {
            ShowToast(new ToastSpec("", "Weather", "Location lookup failed.", 5));
        }
    }

    /// <summary>Turn a weather lookup failure into an actionable toast. The
    /// old catch-all blamed the connection for every error, hiding blockers,
    /// proxies and bad responses behind an "are you offline?" message.</summary>
    private static string WeatherFailureText(Exception ex)
    {
        switch (ex)
        {
            case HttpRequestException:
            case TaskCanceledException:
                return "Can't reach the weather service — check your connection, then try again.";
            case JsonException:
            case FormatException:
                return "The weather service sent something unexpected — a VPN, firewall or proxy may be blocking it.";
            default:
                return $"Weather lookup failed ({ex.GetType().Name}).";
        }
    }

    /// <summary>Probes 15-minute precipitation buckets at the weather
    /// location and toasts once per rain episode that starts within the next
    /// 30 minutes. The evaluator suppresses repeats; this only tracks the
    /// last fired key across probes.</summary>
    private async Task CheckRainAlertAsync()
    {
        if (!_settings.Current.Toasts.RainAlertsEnabled) return;
        var config = _settings.Current.Widgets.Weather;
        if (!config.HasLocation) return;
        try
        {
            var probe = await _weather.GetPrecipitationProbeAsync(config.Lat!.Value, config.Lon!.Value);
            if (probe is null) return;
            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var key = RainAlertEvaluator.Evaluate(
                probe.Times, probe.Probability, probe.Mm, nowUnix, _lastRainAlertKey);
            if (key is null || key == _lastRainAlertKey) return;
            _lastRainAlertKey = key;
            // The key is "rain-<start unix>"; convert back to a friendly delay.
            var startUnix = long.Parse(key.AsSpan(5), System.Globalization.CultureInfo.InvariantCulture);
            var minutes = Math.Max(1, (int)Math.Round((startUnix - nowUnix) / 60.0));
            var when = minutes <= 1 ? "starting now" : $"in about {minutes} min";
            ShowToast(new ToastSpec("\U0001F327\uFE0F", "Rain soon",
                $"Precipitation at {config.City} is expected {when}. Grab a jacket.", 6));
        }
        catch
        {
            // Probe failure is silent — the alert must never become noise.
        }
    }

    private string? _lastRainAlertKey;

    private async Task FetchWeatherAsync()
    {
        var config = _settings.Current.Widgets.Weather;
        if (!config.HasLocation) return;
        try
        {
            var snapshot = await _weather.GetForecastAsync(config.City, config.Lat!.Value, config.Lon!.Value);
            if (snapshot is not null)
            {
                foreach (var window in _windows) window.Shell.SetWeather(snapshot);
            }
        }
        catch
        {
            // Offline or endpoint hiccup — keep the last snapshot.
        }
    }
}