using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using Winotch.Engine;
using Forms = System.Windows.Forms;

namespace Winotch;

/// <summary>The notch host: a borderless transparent window covering one
/// monitor's full bounds, painting the island with SkiaSharp and letting all
/// clicks pass through except where the island actually is.</summary>
public sealed class OverlayWindow : Window, IDisposable
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExAppWindow = 0x00040000;
    private const int WmNcHitTest = 0x0084;
    private const int WmMouseActivate = 0x0021;
    private const int HtTransparent = -1;
    private const int MaNoActivate = 3;

    private readonly SettingsService _settings;
    private readonly StartupService _startup;
    private readonly IslandShell _shell;
    private readonly SKElement _surface;
    private readonly MediaService _media = new();
    private readonly PriorityStatusTracker _status = new();
    private readonly ClipboardHistoryStore _clipboardStore = new();
    private readonly WeatherService _weather = new();
    private readonly SystemStatsSampler _statsSampler = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    private readonly DispatcherTimer _clipboardTimer;
    private TrayIconService? _tray;
    private SettingsWindow? _settingsWindow;
    private HwndSource? _source;
    private CancellationTokenSource? _wifiCts;
    private string? _lastToastKey = "";
    private bool _cursorHand;
    private double _lastSecond = -1;
    private double _cursorX = double.NaN, _cursorY = double.NaN;
    private int _lastWholeSecond = -1;
    private int _lastMinuteOfDay = -1;
    private string? _wifiName;
    private BatteryInfo _battery = new(100, false);

    public OverlayWindow(SettingsService settings, StartupService startup)
    {
        _settings = settings;
        _startup = startup;

        _shell = new IslandShell(settings);
        WireShell();

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        Title = "Winotch";

        _surface = new SKElement();
        _surface.PaintSurface += OnPaintSurface;
        Content = _surface;

        _media.Changed += OnMediaChanged;

        // Clipboard: read on the UI thread (WinForms clipboard needs STA).
        _clipboardTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(2000),
        };
        _clipboardTimer.Tick += (_, _) => PollClipboard();
        _clipboardTimer.Start();
    }

    // ------------------------------------------------------------ lifecycle

    public bool IsPaused => _shell.Paused;

    public void SetPaused(bool paused) => _shell.Paused = paused;

    public void QuitApplication()
    {
        Dispose();
        Application.Current.Shutdown();
    }

    public void OpenSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settings, _startup);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            _source = source;
            source.AddHook(WndProc);
        }

        var handle = new WindowInteropHelper(this).Handle;
        var style = (int)GetWindowLongPtr(handle, GwlExStyle);
        style |= WsExToolWindow | WsExNoActivate;
        style &= ~WsExAppWindow;
        SetWindowLongPtr(handle, GwlExStyle, (IntPtr)style);

        PositionOnPrimaryMonitor();
        StartFrameLoop();
        StartStatusPolling();
        StartWifiPolling();
        StartWeatherPolling();
        _media.Start();

        _tray = new TrayIconService(this, _settings, _startup);
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    public void Dispose()
    {
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
        _tray?.Dispose();
        _tray = null;
        _wifiCts?.Cancel();
        _media.Dispose();
        _weather.Dispose();
        _clipboardTimer.Stop();
        _surface.PaintSurface -= OnPaintSurface;
    }

    // --------------------------------------------------------------- shell

    private void WireShell()
    {
        _shell.OpenSettingsRequested += OpenSettingsWindow;
        _shell.QuitRequested += QuitApplication;
        _shell.MediaPrevRequested += () => _ = _media.PreviousAsync();
        _shell.MediaPlayPauseRequested += () => _ = _media.PlayPauseAsync();
        _shell.MediaNextRequested += () => _ = _media.NextAsync();
        _shell.CopyClipboardEntryRequested += CopyClipboardEntry;
        _shell.DeleteClipboardEntryRequested += DeleteClipboardEntry;
        _shell.ClearClipboardRequested += () =>
        {
            _clipboardStore.Clear();
            RefreshClipboardView();
        };
        _shell.NotesEditRequested += OpenNotesEditor;
        _shell.WeatherEditRequested += OpenWeatherEditor;
        _shell.LauncherEditRequested += OpenLauncherEditor;
        _shell.SystemMetricsEditRequested += OpenMetricsEditor;
        _shell.LaunchAppRequested += LaunchApp;
    }

    // ----------------------------------------------------- widget editing

    private void OpenNotesEditor()
    {
        var window = new NotesEditWindow(_settings.Current.Widgets.Notes);
        if (window.ShowDialog() == true && window.Result is { } text)
        {
            _settings.Update(s => s with { Widgets = s.Widgets with { Notes = text } });
        }
    }

    private void OpenWeatherEditor()
    {
        var window = new WeatherEditWindow(_settings.Current.Widgets.Weather.City);
        if (window.ShowDialog() == true && window.City is { } city)
        {
            _ = ResolveAndFetchWeatherAsync(city);
        }
    }

    private async Task ResolveAndFetchWeatherAsync(string city)
    {
        try
        {
            var found = await _weather.SearchAsync(city);
            if (found is not { } hit)
            {
                _shell.ShowToast(new ToastSpec("", "Weather", $"Couldn't find “{city}” — check the name and try again.", 5));
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
            _shell.ShowToast(new ToastSpec("", "Weather", "Lookup failed — are you offline?", 5));
        }
    }

    private void StartWeatherPolling()
    {
        _ = FetchWeatherAsync();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(15),
        };
        timer.Tick += async (_, _) => await FetchWeatherAsync();
        timer.Start();
    }

    private async Task FetchWeatherAsync()
    {
        var config = _settings.Current.Widgets.Weather;
        if (!config.HasLocation) return;
        try
        {
            var snapshot = await _weather.GetForecastAsync(config.City, config.Lat!.Value, config.Lon!.Value);
            if (snapshot is not null) _shell.SetWeather(snapshot);
        }
        catch
        {
            // Offline or endpoint hiccup — keep the last snapshot.
        }
    }

    private void OpenLauncherEditor()
    {
        var window = new LauncherEditWindow(_settings.Current.Widgets.LauncherApps);
        if (window.ShowDialog() == true && window.Result is { } apps)
        {
            _settings.Update(s => s with { Widgets = s.Widgets with { LauncherApps = apps } });
        }
    }

    private void OpenMetricsEditor()
    {
        var window = new MetricsEditWindow(_settings.Current.Widgets.SystemMetrics);
        if (window.ShowDialog() == true && window.Result is { } result)
        {
            _settings.Update(s => s with { Widgets = s.Widgets with { SystemMetrics = result } });
        }
    }

    private void LaunchApp(LauncherApp app)
    {
        try
        {
            Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true });
            _shell.ShowToast(new ToastSpec("", "Launched", app.Label, 3.2));
        }
        catch
        {
            _shell.ShowToast(new ToastSpec("", "Launch failed", $"Couldn't start “{app.Label}” — check the path in the launcher settings.", 5));
        }
    }

    private void OnMediaChanged(object? sender, MediaSnapshot? snapshot)
    {
        Dispatcher.Invoke(() =>
        {
            _shell.SetMedia(snapshot);
            if (snapshot is { } media && media.Playing && media.Title.Length > 0 &&
                !string.Equals(media.Key, _lastToastKey, StringComparison.Ordinal) &&
                _settings.Current.Toasts.MediaToastsEnabled)
            {
                _lastToastKey = media.Key;
                _shell.ShowToast(new ToastSpec("", media.Title, media.Artist, 4.5));
            }
        });
    }

    // ------------------------------------------------------------- polling

    private void StartStatusPolling()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(2000),
        };
        timer.Tick += (_, _) => PollBattery();
        timer.Start();
        PollBattery();
    }

    private void PollBattery()
    {
        _battery = SystemStatus.GetBattery();
        _shell.SetBattery(_battery);
        _shell.SetSystemStats(_statsSampler.Sample());
        FeedStatusTracker();
    }

    private void StartWifiPolling()
    {
        _wifiCts = new CancellationTokenSource();
        var token = _wifiCts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _wifiName = WifiProbe.ReadSsid();
                }
                catch
                {
                    _wifiName = null;
                }
                token.WaitHandle.WaitOne(10_000);
            }
        }, token);
    }

    private void FeedStatusTracker()
    {
        if (!_settings.Current.Features.PriorityAlertsEnabled) return;
        var snapshot = new PriorityStatusSnapshot(_battery, new WifiStatus(_wifiName), false, false);
        for (var i = 0; i < 4; i++)
        {
            if (_status.Next(snapshot) is { } alert) _shell.ShowStatusToast(alert);
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
                    RefreshClipboardView();
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
                    RefreshClipboardView();
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
                        RefreshClipboardView();
                    }
                }
            }
        }
        catch (System.Runtime.InteropServices.COMException) { }
        catch (ExternalException) { }
    }

    private void RefreshClipboardView() => _shell.SetClipboard(_clipboardStore.Items);

    private void CopyClipboardEntry(long id)
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
        catch (System.Runtime.InteropServices.COMException) { }
        catch (ExternalException) { }
    }

    private void DeleteClipboardEntry(long id)
    {
        _clipboardStore.Remove(id);
        RefreshClipboardView();
    }

    // ----------------------------------------------------------- frame loop

    private void StartFrameLoop()
    {
        CompositionTarget.Rendering += OnFrame;
        _lastSecond = -1;
    }

    private void StopFrameLoop()
    {
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (IsVisible is false) return;

        var now = _stopwatch.Elapsed.TotalSeconds;
        var delta = Math.Clamp(now - _lastSecond, 0, 0.1);
        _lastSecond = now;

        var (cursorDipX, cursorDipY) = CursorInLocalDip();
        var cursorMoved = Math.Abs(cursorDipX - _cursorX) > 0.5 ||
                          Math.Abs(cursorDipY - _cursorY) > 0.5;
        _cursorX = cursorDipX;
        _cursorY = cursorDipY;
        _shell.SetCursor(cursorDipX, cursorDipY);
        _shell.Tick(delta);

        var wantHand = _shell.AnyHoverButton;
        if (wantHand != _cursorHand)
        {
            _cursorHand = wantHand;
            Cursor = wantHand ? Cursors.Hand : Cursors.Arrow;
        }

        // Idle repaint suppression: repaint only when something visible
        // changed. A collapsed idle notch therefore costs ~0% CPU instead of
        // a full Skia redraw (gradient + shadow + text) every compositor
        // frame. Animations and live toasts keep full-rate repaints; hover,
        // data events, the per-second countdown/timeline, and the once-a-
        // minute pill clock drive the rest.
        var wholeSecond = (int)now;
        var secondChanged = wholeSecond != _lastWholeSecond;
        _lastWholeSecond = wholeSecond;

        var minuteOfDay = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
        var minuteChanged = minuteOfDay != _lastMinuteOfDay;
        _lastMinuteOfDay = minuteOfDay;

        var dirty =
            _shell.Animating || _shell.ToastsActive || _shell.ConsumeDataDirty() ||
            (cursorMoved && (_shell.IsExpanded || _shell.NearCursor(cursorDipX, cursorDipY))) ||
            (_shell.IsExpanded && secondChanged) ||
            (!_shell.IsExpanded && _shell.Focus.IsActive && secondChanged) ||
            (!_shell.IsExpanded && minuteChanged);

        if (dirty) _surface.InvalidateVisual();
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        var point = e.GetPosition(_surface);
        _shell.Click(point.X, point.Y);
    }

    private (double X, double Y) CursorInLocalDip()
    {
        var scale = CurrentDpi();
        var rect = WindowScreenRect();
        GetCursorPos(out var pt);
        return ((pt.X - rect.Left) / scale, (pt.Y - rect.Top) / scale);
    }

    private double CurrentDpi() =>
        PresentationSource.FromVisual(this)?.CompositionTarget.TransformToDevice.M11 ?? 1.0;

    private (int Left, int Top, int Right, int Bottom) WindowScreenRect()
    {
        GetWindowRect(new WindowInteropHelper(this).Handle, out var rect);
        return (rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    // ---------------------------------------------------------- placement

    private void PositionOnPrimaryMonitor()
    {
        var monitors = MonitorTargeting.CaptureScreens();
        var monitor = monitors.FirstOrDefault(m => m.IsPrimary);
        if (monitor.DeviceName is null && monitors.Count > 0) monitor = monitors[0];
        var scale = monitor.DpiScaleX;
        Left = monitor.LeftDip;
        Top = monitor.TopDip;
        Width = monitor.WidthDip;
        Height = monitor.Bounds.Height / scale;
    }

    // -------------------------------------------------------- window proc

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmNcHitTest)
        {
            var scale = CurrentDpi();
            var rect = WindowScreenRect();
            var xScreen = (short)((long)lParam & 0xFFFF);
            var yScreen = (short)(((long)lParam >> 16) & 0xFFFF);
        var dipX = (xScreen - rect.Left) / scale;
        var dipY = (yScreen - rect.Top) / scale;
            if (!_shell.IsInteractiveAt(dipX, dipY))
            {
                handled = true;
                return (IntPtr)HtTransparent;
            }
        }
        else if (message == WmMouseActivate)
        {
            handled = true;
            return (IntPtr)MaNoActivate;
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    // -------------------------------------------------------------- canvas

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var scale = (float)CurrentDpi();
        canvas.Scale(scale, scale);

        _shell.SetCanvas(ActualWidth, ActualHeight);
        _shell.Draw(canvas);
    }
}
