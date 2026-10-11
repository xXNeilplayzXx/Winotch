using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using Winotch.Engine;

namespace Winotch;

/// <summary>The notch host: a borderless transparent window covering one
/// monitor's full bounds, painting the island with SkiaSharp and letting all
/// clicks pass through except where the island actually is. Pure surface —
/// services (media, clipboard, weather, status, tray) live in the manager.</summary>
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
    private readonly ThemeService _themeService;
    private readonly OverlayWindowManager _manager;
    private readonly IslandShell _shell;
    private readonly SKElement _surface;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    private HwndSource? _source;
    private bool _cursorHand;
    private double _lastSecond = -1;
    private double _cursorX = double.NaN, _cursorY = double.NaN;
    private int _lastWholeSecond = -1;
    private int _lastMinuteOfDay = -1;

    // Drag-to-move state.
    private bool _dragActive;
    private bool _dragging;
    private bool _seekDrag;
    private bool _volumeDrag;
    private System.Windows.Point _dragStart;
    private double _offsetDragStartX, _offsetDragStartY;

    // Widget drag-to-reorder state.
    private string? _dragWidgetId;
    private int _lastWidgetIndex = -1;

    public OverlayWindow(SettingsService settings, ThemeService themeService,
        StartupService startup, OverlayWindowManager manager)
    {
        _settings = settings;
        _themeService = themeService;
        _manager = manager;

        _shell = new IslandShell(settings, themeService);
        WireShell();

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        Title = "Winotch";

        _surface = new SKElement();
        _surface.PaintSurface += OnPaintSurface;
        Content = _surface;
    }

    public IslandShell Shell => _shell;

    public MonitorSnapshot Monitor { get; private set; }

    public HwndSource? Source => _source;

    public bool IsPaused => _shell.Paused;

    public void SetPaused(bool paused) => _shell.Paused = paused;

    public bool IsHidden => !IsVisible;

    public void HideNotch() => Hide();

    public void ShowNotch()
    {
        Show();
        _shell.ApplyTheme();
    }

    // ------------------------------------------------------------ lifecycle

    /// <summary>Target this window at a monitor: position the full-screen
    /// overlay over its bounds and keep the monitor for hit-testing math.</summary>
    public void AssignMonitor(MonitorSnapshot monitor)
    {
        Monitor = monitor;
        _shell.SetMonitorDeviceName(monitor.DeviceName);
        var scale = monitor.DpiScaleX;
        Left = monitor.LeftDip;
        Top = monitor.TopDip;
        Width = monitor.WidthDip;
        // Guard against zero or negative DPI scale to prevent division by zero
        Height = scale > 0.001 ? monitor.Bounds.Height / scale : monitor.Bounds.Height;
        _shell.ApplyTheme();
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

        _shell.ApplyTheme();
        StartFrameLoop();
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
        _surface.PaintSurface -= OnPaintSurface;
        StopFrameLoop();
    }

    // --------------------------------------------------------------- shell

    private void WireShell()
    {
        _shell.OpenSettingsRequested += _manager.OpenSettingsWindow;
        _shell.QuitRequested += _manager.QuitApplication;
        _shell.HideRequested += _manager.HideNotch;
        _shell.RestartRequested += _manager.RestartApplication;
        _shell.MediaPrevRequested += () => _ = SafeAsync(_manager.MediaPreviousAsync());
        _shell.MediaPlayPauseRequested += () => _ = SafeAsync(_manager.MediaPlayPauseAsync());
        _shell.MediaNextRequested += () => _ = SafeAsync(_manager.MediaNextAsync());
        _shell.MediaSeekRequested += s => _ = SafeAsync(_manager.MediaSeekAsync(s));
        _shell.VolumeChangedRequested += _manager.MediaVolumeChanged;
        _shell.VolumeMuteRequested += _manager.MediaVolumeMuteToggled;
        _shell.CopyClipboardEntryRequested += _manager.CopyClipboardEntry;
        _shell.MarkInboxReadRequested += _manager.MarkInboxRead;
        _shell.ClearInboxRequested += _manager.ClearInbox;
        _shell.QuickActionRequested += action => _manager.ExecuteQuickAction(action, Monitor.DeviceName);
        _shell.FocusStateChangedRequested += state => _manager.SetFocusState(Monitor.DeviceName, state);
        _shell.DeleteClipboardEntryRequested += _manager.DeleteClipboardEntry;
        _shell.ClearClipboardRequested += _manager.ClearClipboard;
        _shell.NotesEditRequested += () => _manager.OpenNotesEditor(Monitor.DeviceName);
        _shell.WeatherEditRequested += () => _manager.OpenWeatherEditor(Monitor.DeviceName);
        _shell.LauncherEditRequested += () => _manager.OpenLauncherEditor(Monitor.DeviceName);
        _shell.FocusCustomRequested += () => _manager.OpenFocusEditor(Monitor.DeviceName);
        _shell.SystemMetricsEditRequested += () => _manager.OpenMetricsEditor(Monitor.DeviceName);
        _shell.CardSizeEditRequested += id => _manager.OpenCardSizeEditor(Monitor.DeviceName, id);
        _shell.CalendarEditRequested += () => _manager.OpenCalendarEditor(Monitor.DeviceName);
        _shell.CalendarSourceEditRequested += () => _manager.OpenSettingsWindow();
        _shell.LaunchAppRequested += _manager.LaunchApp;
    }

    /// <summary>Wraps an async Task to catch and log any exceptions, preventing
    /// unhandled exceptions in fire-and-forget event handlers from crashing the app.</summary>
    private static async Task SafeAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Winotch Error] Async event handler failed: {ex.Message}");
        }
    }

    // ----------------------------------------------------------- frame loop

    private void StartFrameLoop()
    {
        System.Windows.Media.CompositionTarget.Rendering += OnFrame;
        _lastSecond = -1;
    }

    private void StopFrameLoop()
    {
        System.Windows.Media.CompositionTarget.Rendering -= OnFrame;
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
            (_shell.IsExpanded && minuteChanged) ||
            // Per-second repaints only while something on screen is actually
            // live; an idle expanded panel (clock-only) repaints once a minute.
            (_shell.IsExpanded && secondChanged && _shell.NeedsLiveUpdates) ||
            (!_shell.IsExpanded && _shell.Focus.IsActive && secondChanged) ||
            (!_shell.IsExpanded && _shell.Call is { OnCall: true } && secondChanged) ||
            // The now-playing chip's progress hairline advances with the track.
            (!_shell.IsExpanded && _shell.MediaProgressLive && secondChanged) ||
            (!_shell.IsExpanded && minuteChanged);

        if (dirty) _surface.InvalidateVisual();
    }

    // ----------------------------------------------------------- mouse

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        var point = e.GetPosition(_surface);
        if (_shell.IsCollapsedPillAt(point.X, point.Y))
        {
            _dragActive = true;
            _dragging = false;
            _dragStart = point;
            _offsetDragStartX = _shell.OffsetX;
            _offsetDragStartY = _shell.OffsetY;
            _shell.Dragging = true;
            _surface.CaptureMouse();
            return;
        }

        // Drag-to-seek / volume on the Now playing card — checked before
        // drag-to-reorder so those strips act as controls, not card handles.
        if (_shell.MediaSeekHit(point.X, point.Y))
        {
            _seekDrag = true;
            _shell.BeginSeek(point.X, point.Y);
            _surface.CaptureMouse();
            return;
        }
        if (_shell.MediaVolumeHit(point.X, point.Y))
        {
            _volumeDrag = true;
            _shell.BeginVolumeDrag(point.X, point.Y);
            _surface.CaptureMouse();
            return;
        }

        // Drag-to-reorder: grabbing a widget card (expanded panel).
        var widgetId = _shell.WidgetCardAt(point.X, point.Y);
        if (widgetId is not null)
        {
            _dragWidgetId = widgetId;
            _dragActive = true;
            _dragging = false;
            _dragStart = point;
            _lastWidgetIndex = -1;
            _surface.CaptureMouse();
        }
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        var point = e.GetPosition(_surface);
        if (_seekDrag)
        {
            _shell.UpdateSeek(point.X, point.Y);
            return;
        }
        if (_volumeDrag)
        {
            _shell.UpdateVolumeDrag(point.X, point.Y);
            return;
        }
        if (!_dragActive) return;
        if (!_dragging &&
            (Math.Abs(point.X - _dragStart.X) > 4 || Math.Abs(point.Y - _dragStart.Y) > 4))
        {
            _dragging = true;
        }
        if (!_dragging) return;

        if (_dragWidgetId is not null)
        {
            var target = _shell.WidgetIndexAt(point.X, point.Y);
            if (target >= 0 && target != _lastWidgetIndex)
            {
                _lastWidgetIndex = target;
                _shell.MoveWidgetTo(_dragWidgetId, target);
            }
            return;
        }

        _shell.SetOffset(
            _offsetDragStartX + point.X - _dragStart.X,
            _offsetDragStartY + point.Y - _dragStart.Y);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        var point = e.GetPosition(_surface);
        if (_seekDrag || _volumeDrag)
        {
            if (_seekDrag) _shell.EndSeek();
            else _shell.EndVolumeDrag();
            _seekDrag = false;
            _volumeDrag = false;
            _surface.ReleaseMouseCapture();
            return;
        }
        if (_dragActive)
        {
            var wasDragging = _dragging;
            var widgetId = _dragWidgetId;
            _dragActive = false;
            _dragging = false;
            _dragWidgetId = null;
            _shell.Dragging = false;
            _surface.ReleaseMouseCapture();
            if (wasDragging)
            {
                if (widgetId is null)
                {
                    // Pill drag: persist the resting position (already live).
                    _shell.CommitPosition();
                }
                // Widget drag already persisted each move via MoveWidgetTo.
                return;
            }
        }
        _shell.Click(point.X, point.Y);
    }

    protected override void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseRightButtonUp(e);
        var point = e.GetPosition(_surface);
        if (_shell.IsExpanded)
        {
            _shell.OpenContextMenu(point.X, point.Y);
        }
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
        var (left, top, _, _) = WindowScreenRect();
        _shell.SetScreenContext(left, top, scale);
        canvas.Scale(scale, scale);

        _shell.SetCanvas(ActualWidth, ActualHeight);
        _shell.Draw(canvas);
    }
}