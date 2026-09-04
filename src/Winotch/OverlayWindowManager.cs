using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Winotch.Engine;
using Forms = System.Windows.Forms;

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
    void RestartApplication();
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
    private readonly MediaService _media = new();
    private readonly PriorityStatusTracker _status = new();
    private readonly IPrivacyConsentStore _privacy = new RegistryPrivacyConsentStore();
    private readonly ClipboardHistoryStore _clipboardStore = new();
    private readonly WeatherService _weather = new();
    private readonly SystemStatsSampler _statsSampler = new();
    private readonly List<OverlayWindow> _windows = new();

    private readonly DispatcherTimer _clipboardTimer;
    private SettingsWindow? _settingsWindow;
    private TrayIconService? _tray;
    private CancellationTokenSource? _wifiCts;
    private string? _wifiName;
    private BatteryInfo _battery = new(100, false);
    private bool _lastMic, _lastCam;
    private string? _lastCallApp;
    private string? _lastToastKey = "";
    private string _lastMonitorLayout = "";
    private bool _disposed;

    public OverlayWindowManager(SettingsService settings, StartupService startup)
    {
        _settings = settings;
        _startup = startup;

        _theme.Refresh(settings.Current);
        DialogUi.Current = _theme.Current;
        _settings.Changed += OnSettingsChanged;
        _theme.ThemeChanged += OnThemeChanged;

        _media.Changed += OnMediaChanged;

        _clipboardTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(2000),
        };
        _clipboardTimer.Tick += (_, _) => PollClipboard();
    }

    // ------------------------------------------------------------- startup

    /// <summary>Push the Appearance font choice into the Skia text engine and
    /// the WPF dynamic resource every window's text inherits. Families absent
    /// from the machine fall back to the OS defaults inside TextKit.</summary>
    private void ApplyUiFont()
    {
        var family = FontChoice.FamilyOf(_settings.Current.Appearance.Font);
        TextKit.SetUiFamily(family);
        Application.Current.Resources["NotchUiFont"] = new FontFamily(family);
    }

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
    }

    private void StartTimers()
    {
        var status = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        status.Tick += (_, _) => PollBattery();
        status.Start();

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

        var privacy = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        privacy.Tick += (_, _) => PollPrivacy();
        privacy.Start();

        var layout = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        layout.Tick += (_, _) => RebuildIfLayoutChanged();
        layout.Start();

        var theme = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        theme.Tick += (_, _) => _theme.Refresh(_settings.Current);
        theme.Start();

        var weather = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(15) };
        weather.Tick += async (_, _) => await FetchWeatherAsync();
        weather.Start();

        _clipboardTimer.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _clipboardTimer.Stop();
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
        var monitors = MonitorTargeting.CaptureScreens();
        var appearance = _settings.Current.Appearance;
        var targets = appearance.Display == DisplayMode.FollowActive
            ? new[] { MonitorTargeting.SelectMonitor(monitors, MonitorTargeting.GetForegroundWindowRect(),
                MonitorTargeting.GetCursorPosition(), useCursorMonitor: true, null) }
            : MonitorTargeting.SelectTargets(monitors, appearance.Display, appearance.DisplayDeviceName);

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

    /// <summary>Follow-active tracks the monitor under the foreground window
    /// (falling back to the cursor monitor). Called from the layout poll so
    /// the single island hops between screens live.</summary>
    private void RebuildIfLayoutChanged()
    {
        var monitors = MonitorTargeting.CaptureScreens();
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
            var monitor = MonitorTargeting.SelectMonitor(monitors, MonitorTargeting.GetForegroundWindowRect(),
                MonitorTargeting.GetCursorPosition(), useCursorMonitor: true, null);
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

    public void OpenSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settings, _startup, OpenSetupWizard);
            _settingsWindow.ApplyTheme(_theme.Current);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void OpenSetupWizard()
    {
        var window = new SetupWindow(_settings);
        window.ShowDialog();
    }

    public void QuitApplication()
    {
        Dispose();
        Application.Current.Shutdown();
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
        try
        {
            Application.Current?.Dispatcher.Invoke(() =>
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

    public async Task MediaPlayPauseAsync() => await _media.PlayPauseAsync();
    public async Task MediaNextAsync() => await _media.NextAsync();
    public async Task MediaPreviousAsync() => await _media.PreviousAsync();

    private void PollBattery()
    {
        _battery = SystemStatus.GetBattery();
        var stats = _statsSampler.Sample();
        foreach (var window in _windows)
        {
            window.Shell.SetBattery(_battery);
            window.Shell.SetSystemStats(stats);
        }
        FeedStatusTracker();
    }

    private void PollPrivacy()
    {
        _lastMic = _privacy.IsMicrophoneActive();
        _lastCam = _privacy.IsCameraActive();
        foreach (var window in _windows) window.Shell.SetPrivacy(_lastMic, _lastCam);
        FeedStatusTracker();

        var app = CallDetector.FindCallApp();
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
            if (Forms.Clipboard.ContainsText())
            {
                var text = Forms.Clipboard.GetText();
                if (_clipboardStore.TryAdd("text", text, null, null, text, DateTimeOffset.Now))
                {
                    PushClipboard();
                }
                return;
            }

            if (Forms.Clipboard.ContainsFileDropList())
            {
                var files = Forms.Clipboard.GetFileDropList();
                var names = files.Cast<string>().ToArray();
                var fingerprint = string.Join('\n', names);
                if (_clipboardStore.TryAdd("files", null, names, null, fingerprint, DateTimeOffset.Now))
                {
                    PushClipboard();
                }
                return;
            }

            if (Forms.Clipboard.ContainsImage())
            {
                using var image = Forms.Clipboard.GetImage();
                if (image is not null)
                {
                    var thumb = ClipboardPng.ToPngBase64(image, out var bytes);
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
                    Forms.Clipboard.SetText(entry.Text ?? "");
                    break;
                case "files" when entry.Files is { Length: > 0 }:
                    var drop = new System.Collections.Specialized.StringCollection();
                    foreach (var file in entry.Files) drop.Add(file);
                    Forms.Clipboard.SetFileDropList(drop);
                    break;
                case "image" when entry.ThumbBase64 is { } b64:
                    using (var stream = new MemoryStream(Convert.FromBase64String(b64)))
                    using (var image = System.Drawing.Image.FromStream(stream))
                    {
                        Forms.Clipboard.SetImage(image);
                    }
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
        var window = new NotesEditWindow(_settings.Current.Widgets.Notes);
        if (window.ShowDialog() == true && window.Result is { } text)
        {
            _settings.Update(s => s with { Widgets = s.Widgets with { Notes = text } });
        }
    }

    public void OpenWeatherEditor()
    {
        var window = new WeatherEditWindow(_settings.Current.Widgets.Weather);
        if (window.ShowDialog() != true) return;
        if (window.Result is { } city)
        {
            _ = ResolveAndFetchWeatherAsync(city);
        }
        else if (window.UseWindowsLocation)
        {
            _ = ResolveWindowsLocationAsync();
        }
    }

    public void OpenLauncherEditor()
    {
        var window = new LauncherEditWindow(_settings.Current.Widgets.LauncherApps);
        if (window.ShowDialog() == true && window.Result is { } apps)
        {
            _settings.Update(s => s with { Widgets = s.Widgets with { LauncherApps = apps } });
        }
    }

    public void OpenMetricsEditor()
    {
        var window = new MetricsEditWindow(_settings.Current.Widgets.SystemMetrics);
        if (window.ShowDialog() == true && window.Result is { } result)
        {
            _settings.Update(s => s with { Widgets = s.Widgets with { SystemMetrics = result } });
        }
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
        catch
        {
            ShowToast(new ToastSpec("", "Weather", "Lookup failed — are you offline?", 5));
        }
    }

    /// <summary>Resolve the user's Windows location (WinRT geolocation) into a
    /// saved weather location, like the legacy "Use Windows location".</summary>
    private async Task ResolveWindowsLocationAsync()
    {
        try
        {
            var location = await WindowsLocation.ResolveAsync();
            if (location is null)
            {
                ShowToast(new ToastSpec("", "Weather",
                    "Windows location is unavailable — allow location in Settings → Privacy → Location, then retry.", 6));
                return;
            }
            var hit = await _weather.SearchByCoordinatesAsync(location.Lat, location.Lon);
            var city = hit?.Name ?? $"({location.Lat:0.##}, {location.Lon:0.##})";
            _settings.Update(s => s with
            {
                Widgets = s.Widgets with
                {
                    Weather = new WeatherWidgetConfig { City = city, Lat = location.Lat, Lon = location.Lon },
                },
            });
            await FetchWeatherAsync();
        }
        catch
        {
            ShowToast(new ToastSpec("", "Weather", "Location lookup failed.", 5));
        }
    }

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