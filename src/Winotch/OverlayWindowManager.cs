using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
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
    Task CheckForUpdatesAsync(bool manual = false);
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
    private readonly MediaService _media = new();
    private readonly PriorityStatusTracker _status = new();
    private readonly IPrivacyConsentStore _privacy = new RegistryPrivacyConsentStore();
    private readonly ClipboardHistoryStore _clipboardStore = new();
    private readonly WeatherService _weather = new();
    private readonly SystemStatsSampler _statsSampler = new();
    private readonly List<OverlayWindow> _windows = new();
    private readonly UpdateService _updateService = new();
    private readonly KeyboardHudHook _hudHook = new();

    private readonly DispatcherTimer _clipboardTimer;
    private SettingsWindow? _settingsWindow;
    private TrayIconService? _tray;
    private CancellationTokenSource? _wifiCts;
    private CancellationTokenSource? _brightnessCts;
    private int? _lastBrightness;
    private string? _wifiName;
    private BatteryInfo _battery = new(100, false);
    private bool _lastMic, _lastCam;
    private string? _lastCallKey;
    private string? _lastToastKey = "";
    private string _lastMonitorLayout = "";
    private DisplayMode _lastDisplayMode;
    private string _lastDisplayDevice = "";
    private bool _disposed;
    private bool _updateCheckInFlight;

    public OverlayWindowManager(SettingsService settings, StartupService startup)
    {
        _settings = settings;
        _startup = startup;

        _theme.Refresh(settings.Current);
        DialogUi.Current = _theme.Current;
        _lastDisplayMode = settings.Current.Appearance.Display;
        _lastDisplayDevice = settings.Current.Appearance.DisplayDeviceName;
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
        var fontId = _settings.Current.Appearance.Font;
        var family = FontChoice.FamilyOf(fontId);
        var customFile = FontChoice.CustomFile(fontId);
        TextKit.SetUiFamily(family, customFile);
        Application.Current.Resources["NotchUiFont"] = WpfFontFamily(fontId, family);
        // The icon face the other renderer uses for its own glyphs; resolved
        // here so the pill and the settings window can never disagree.
        IconFontWpf.Apply();
    }

    /// <summary>The WPF font for a font id: a plain family for curated /
    /// system fonts, or a file-relative FontFamily for an imported .ttf/.otf
    /// (the family name embedded in the file drives the WPF face).</summary>
    private static FontFamily WpfFontFamily(string fontId, string family)
    {
        var customFile = FontChoice.CustomFile(fontId);
        if (customFile is null)
        {
            return new FontFamily(family);
        }
        var directory = Path.GetDirectoryName(customFile) ?? "";
        return new FontFamily(new Uri(directory + Path.DirectorySeparatorChar, UriKind.Absolute),
            $"./{Path.GetFileName(customFile)}#{family}");
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
        // Volume keys are claimed from the moment the app runs (the hook only
        // decides per keypress, so toggling the setting needs no reinstall).
        _hudHook.Claim = ClaimVolumeKey;
        // Media keys are watched, never claimed — see ObserveKey.
        _hudHook.Observed = ObserveKey;
        _hudHook.Install();
        StartBrightnessMirror();
        _ = CheckRainAlertAsync();

        if (_settings.Current.General.AutoCheckUpdates)
        {
            // Let the shell settle and the release hit the network after a beat.
            var check = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(6) };
            check.Tick += (_, _) =>
            {
                check.Stop();
                _ = CheckForUpdatesAsync(manual: false);
            };
            check.Start();
        }
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

        // Rain watch: probe precipitation every 5 minutes; the evaluator only
        // fires when rain *starts* within 30 minutes, so the 5-minute cadence
        // can't miss the beginning of an episode.
        var rain = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(5) };
        rain.Tick += async (_, _) => await CheckRainAlertAsync();
        rain.Start();

        _clipboardTimer.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _clipboardTimer.Stop();
        _wifiCts?.Cancel();
        _brightnessCts?.Cancel();
        _hudHook.Dispose();
        BrightnessService.Release();
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
    public async Task CheckForUpdatesAsync(bool manual = false)
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
            var accepted = window.ShowDialog() == true;
            _settings.Update(x => x with
            {
                General = x.General with { LastPromptedUpdate = info.Tag },
            });
            if (accepted && window.StagedExe is { } staged)
            {
                if (UpdateService.ApplyAndRestart(staged, window.DownloadPath))
                {
                    QuitApplication();
                }
                else
                {
                    ShowToast(new ToastSpec("", "Update couldn't start",
                        "Winotch couldn't launch its updater. Quit and start Winotch, then try again.", 7));
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Winotch Error] CheckForUpdates failed: {ex.Message}");
            if (manual)
            {
                ShowToast(new ToastSpec("", "Update check error",
                    $"An unexpected error occurred: {ex.Message}", 6));
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
                () => _ = CheckForUpdatesAsync(manual: true));
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
        // Launching the new instance while this one is still alive makes it
        // lose the single-instance mutex and exit at once, so the relaunch is
        // deferred until this process is gone.
        if (!UpdateService.RelaunchAfterExit(executable))
        {
            try
            {
                Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
            }
            catch
            {
                // Launch failed — stay running.
            }
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
                    _settings.Current.Toasts.MediaToastsEnabled && !IsQuietDuringFocus)
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

    private bool IsQuietDuringFocus => FocusFeedbackPolicy.ShouldSuppress(
        _settings.Current.Features.QuietDuringFocus, _windows.Select(window => window.Shell.Focus));

    public async Task MediaPlayPauseAsync() => await _media.PlayPauseAsync();
    public async Task MediaNextAsync() => await _media.NextAsync();
    public async Task MediaPreviousAsync() => await _media.PreviousAsync();
    public async Task MediaSeekAsync(double seconds) => await _media.SeekAsync(seconds);

    public void MediaVolumeChanged(float level01) => SystemVolume.SetLevel(level01);
    public void MediaVolumeMuteToggled() => SystemVolume.SetMuted(!SystemVolume.Muted);

    // -------------------------------------------------------- level HUDs

    /// <summary>The volume/brightness HUD switch: how often the brightness
    /// mirror reads the monitor. DDC/CI is slow, so this is deliberately
    /// gentle; the cost is that a brightness keypress shows the HUD a beat or
    /// two late.</summary>
    private const int BrightnessPollMilliseconds = 1500;

    /// <summary>Decide whether Winotch claims a volume key. This runs inside
    /// the keyboard hook, which must return immediately, so it only decides:
    /// the level is applied on the dispatcher once the hook has returned and
    /// the key is already swallowed.</summary>
    private bool ClaimVolumeKey(int virtualKey)
    {
        var hudEnabled = _settings.Current.Features.VolumeHudEnabled;
        var awake = !IsPaused && !IsHidden;
        if (!HudKeys.TakesOver(virtualKey, hudEnabled, awake, SystemVolume.Available)) return false;

        var action = HudKeys.Classify(virtualKey);
        try
        {
            Application.Current?.Dispatcher.BeginInvoke(() => ApplyVolumeKey(action));
        }
        catch
        {
            // Shutting down — let the key through rather than swallow it.
            return false;
        }
        return true;
    }

    /// <summary>Apply a claimed volume key and show the HUD. Mute toggles; the
    /// level keys step and unmute, the way Windows' own keys do.</summary>
    private void ApplyVolumeKey(HudKeyAction action)
    {
        if (action == HudKeyAction.VolumeMute)
        {
            SystemVolume.SetMuted(!SystemVolume.Muted);
        }
        else
        {
            if (HudKeys.Unmutes(action) && SystemVolume.Muted) SystemVolume.SetMuted(false);
            SystemVolume.SetLevel(HudKeys.Step(SystemVolume.Level01, action));
        }

        // Push the new level now so the Now playing slider tracks the keys
        // instead of waiting for the next poll.
        var level = SystemVolume.Level01;
        var muted = SystemVolume.Muted;
        var available = SystemVolume.Available;
        foreach (var window in _windows) window.Shell.SetVolume(level, muted, available);
        ShowHud(new HudSpec(HudKind.Volume, level, muted));
    }

    private void ShowHud(HudSpec spec)
    {
        foreach (var window in _windows) window.Shell.ShowHud(spec);
    }

    /// <summary>Media keys, mirrored onto the pill. Winotch never claims these:
    /// the app that owns the session still receives the key and Windows'
    /// handling is untouched, so a failure here can only mean no chip. This runs
    /// inside the keyboard hook, so it only classifies and posts.</summary>
    private void ObserveKey(int virtualKey, bool isDown)
    {
        if (!isDown) return;
        var action = MediaKeys.Classify(virtualKey);
        if (action == MediaKeyAction.None) return;
        if (!MediaKeys.Observes(virtualKey, _settings.Current.Features.MediaHudEnabled,
                !IsPaused && !IsHidden)) return;
        try
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                // The focus state may have changed between the hook and the UI
                // dispatch; check again before emitting transient feedback.
                if (IsQuietDuringFocus) return;
                foreach (var window in _windows) window.Shell.ShowTransport(action);
            });
        }
        catch
        {
            // Shutting down — the key already worked; only the chip is lost.
        }
    }

    private void ShowGlance(GlanceSpec spec)
    {
        foreach (var window in _windows) window.Shell.ShowGlance(spec);
    }

    /// <summary>Which status alerts belong on the pill instead of in a toast.
    /// Charging is a glance — you see it happen and the battery chip already
    /// carries the level — while camera, microphone, Wi-Fi and low battery stay
    /// toasts, because those are worth a full sentence.</summary>
    private static GlanceSpec? GlanceFor(PriorityStatusAlert alert) =>
        alert.PillGlance
            ? new GlanceSpec(alert.Icon, alert.Title, alert.Body, 3.6,
                alert.BatteryPercent is { } percent ? percent / 100.0 : null)
            : null;

    private static GlanceSpec? GlanceFor(ClipboardEntry? entry) => entry is null
        ? null
        : new GlanceSpec("\U0001F4CB", "Copied", entry.Preview(44), 2.8);


    /// <summary>Mirror the monitor's brightness onto the pill. Brightness can't
    /// be claimed the way volume is — Windows defines no brightness virtual key
    /// and on most laptops the keys are handled by the maker's utility before
    /// Windows sees them — so the HUD appears on any brightness change we can
    /// read, including changes made in Windows Settings. DDC/CI calls block, so
    /// this stays off the UI thread, like the Wi-Fi watcher above.</summary>
    private void StartBrightnessMirror()
    {
        _brightnessCts = new CancellationTokenSource();
        var token = _brightnessCts.Token;
        _ = Task.Run(() =>
        {
            try
            {
                // The first read only records where brightness started; a HUD
                // at launch would be noise.
                _lastBrightness = BrightnessService.Level;
                while (!token.IsCancellationRequested)
                {
                    token.WaitHandle.WaitOne(BrightnessPollMilliseconds);
                    if (token.IsCancellationRequested) break;
                    var level = BrightnessService.Level;
                    if (level == _lastBrightness) continue;
                    _lastBrightness = level;
                    if (level is { } percent) PostBrightnessHud(percent);
                }
            }
            catch
            {
                // A monitor that won't answer just means no brightness HUD.
            }
        }, token);
    }

    private void PostBrightnessHud(int percent)
    {
        try
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (!_settings.Current.Features.BrightnessHudEnabled) return;
                if (IsPaused || IsHidden) return;
                ShowHud(new HudSpec(HudKind.Brightness, percent / 100.0));
            });
        }
        catch
        {
            // Shutting down.
        }
    }

    private void PollBattery()
    {
        _battery = SystemStatus.GetBattery();
        var stats = _statsSampler.Sample();
        // System volume is pushed alongside battery so the Now playing card's
        // slider reflects keyboard/mixer changes within ~2s.
        var volumeLevel = SystemVolume.Level01;
        var volumeMuted = SystemVolume.Muted;
        var volumeAvailable = SystemVolume.Available;
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

        // The key (app or web service) only changes when the call source does,
        // so the chip's timer restarts exactly once per call.
        var match = CallDetector.FindCallApp(_lastMic || _lastCam);
        if (!string.Equals(match?.Key, _lastCallKey, StringComparison.OrdinalIgnoreCase))
        {
            _lastCallKey = match?.Key;
            var state = match is null
                ? null
                : new CallState(true, match.Display, DateTimeOffset.UtcNow);
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
            if (GlanceFor(alert) is { } glance)
            {
                ShowGlance(glance);
                continue;
            }
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

    private bool _clipboardWarm;

    private void PushClipboard()
    {
        foreach (var window in _windows) window.Shell.SetClipboard(_clipboardStore.Items);
        // The first push is whatever was already on the clipboard when Winotch
        // started — a chip for that would be noise, not feedback.
        if (!_clipboardWarm)
        {
            _clipboardWarm = true;
            return;
        }
        if (GlanceFor(_clipboardStore.Items.FirstOrDefault()) is { } glance) ShowGlance(glance);
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
        var widgets = _settings.Current.Widgets;
        var window = new NotesEditWindow(widgets.Notes, widgets.SizeFor("notes"));
        if (window.ShowDialog() == true && window.Result is { } text)
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
        if (window.ShowDialog() != true) return;
        // Both outcome paths below update settings themselves, so the card
        // size choice is saved here first.
        _settings.Update(s => s with { Widgets = s.Widgets.WithSize("weather", window.Scale) });
        if (window.City is { } city)
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
        var widgets = _settings.Current.Widgets;
        var window = new LauncherEditWindow(widgets.LauncherApps, widgets.SizeFor("launcher"));
        if (window.ShowDialog() == true && window.Result is { } apps)
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
        if (window.ShowDialog() != true || window.Result is not { } result) return;
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
        if (window.ShowDialog() == true && window.Result is { } result)
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
        if (window.ShowDialog() != true) return;
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

    /// <summary>Resolve the user's Windows location (WinRT geolocation) into a
    /// saved weather location, like the legacy "Use Windows location".</summary>
    private async Task ResolveWindowsLocationAsync()
    {
        try
        {
            var location = await WindowsLocation.ResolveAsync();
            if (location is not { } loc)
            {
                ShowToast(new ToastSpec("", "Weather",
                    "Windows location is unavailable — allow location in Settings → Privacy → Location, then retry.", 6));
                return;
            }
            var hit = await _weather.SearchByCoordinatesAsync(loc.Lat, loc.Lon);
            var city = hit?.Name ?? $"({loc.Lat:0.##}, {loc.Lon:0.##})";
            _settings.Update(s => s with
            {
                Widgets = s.Widgets with
                {
                    Weather = new WeatherWidgetConfig { City = city, Lat = loc.Lat, Lon = loc.Lon },
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
        if (!_settings.Current.Toasts.RainAlertsEnabled || IsQuietDuringFocus) return;
        var config = _settings.Current.Widgets.Weather;
        if (!config.HasLocation) return;
        try
        {
            var probe = await _weather.GetPrecipitationProbeAsync(config.Lat!.Value, config.Lon!.Value);
            if (probe is null || IsQuietDuringFocus) return;
            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var key = RainAlertEvaluator.Evaluate(
                probe.Times, probe.Probability, probe.Mm, nowUnix, _lastRainAlertKey);
            if (key is null || key == _lastRainAlertKey || IsQuietDuringFocus) return;
            // The key is "rain-<start unix>"; convert back to a friendly delay.
            var startUnix = long.Parse(key.AsSpan(5), System.Globalization.CultureInfo.InvariantCulture);
            var minutes = Math.Max(1, (int)Math.Round((startUnix - nowUnix) / 60.0));
            var when = minutes <= 1 ? "starting now" : $"in about {minutes} min";
            // A glance, not a toast: rain in half an hour wants awareness, not
            // a sentence you have to dismiss.
            if (IsQuietDuringFocus) return;
            _lastRainAlertKey = key;
            ShowGlance(new GlanceSpec("\U0001F327\uFE0F", "Rain soon",
                $"At {config.City} {when}", 5));
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