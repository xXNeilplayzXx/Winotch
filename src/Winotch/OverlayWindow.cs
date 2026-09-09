using System.Diagnostics;
using SkiaSharp;
using Winotch.Engine;

namespace Winotch;

/// <summary>The notch host: a borderless transparent window covering one
/// monitor's full bounds, painting the island with SkiaSharp and letting all
/// clicks pass through except where the island actually is. The native
/// windowing is platform-specific (Win32 on Windows, X11/Wayland on Linux).</summary>
public sealed partial class OverlayWindow : IDisposable
{
    private readonly SettingsService _settings;
    private readonly ThemeService _themeService;
    private readonly OverlayWindowManager _manager;
    private readonly IslandShell _shell;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

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
    private (double X, double Y) _dragStart;
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
        NativeInit();
    }

    public IslandShell Shell => _shell;
    public MonitorSnapshot Monitor { get; private set; }
    public bool IsPaused => _shell.Paused;
    public bool IsHidden { get; private set; }

    public void SetPaused(bool paused) => _shell.Paused = paused;

    public void HideNotch()
    {
        IsHidden = true;
        NativeHide();
    }

    public void ShowNotch()
    {
        IsHidden = false;
        _shell.ApplyTheme();
        NativeShow();
    }

    /// <summary>Target this window at a monitor: position the full-screen
    /// overlay over its bounds and keep the monitor for hit-testing math.</summary>
    public void AssignMonitor(MonitorSnapshot monitor)
    {
        Monitor = monitor;
        var scale = monitor.DpiScaleX;
        NativeSetBounds(monitor.LeftDip, monitor.TopDip, monitor.WidthDip, monitor.Bounds.Height / scale);
        _shell.ApplyTheme();
    }

    public void Dispose()
    {
        NativeCleanup();
    }

    public void Close()
    {
        Dispose();
    }

    public void Show()
    {
        ShowNotch();
    }

    // --------------------------------------------------------------- shell

    private void WireShell()
    {
        _shell.OpenSettingsRequested += _manager.OpenSettingsWindow;
        _shell.QuitRequested += _manager.QuitApplication;
        _shell.HideRequested += _manager.HideNotch;
        _shell.RestartRequested += _manager.RestartApplication;
        _shell.MediaPrevRequested += () => _ = _manager.MediaPreviousAsync();
        _shell.MediaPlayPauseRequested += () => _ = _manager.MediaPlayPauseAsync();
        _shell.MediaNextRequested += () => _ = _manager.MediaNextAsync();
        _shell.MediaSeekRequested += s => _ = _manager.MediaSeekAsync(s);
        _shell.VolumeChangedRequested += _manager.MediaVolumeChanged;
        _shell.VolumeMuteRequested += _manager.MediaVolumeMuteToggled;
        _shell.CopyClipboardEntryRequested += _manager.CopyClipboardEntry;
        _shell.DeleteClipboardEntryRequested += _manager.DeleteClipboardEntry;
        _shell.ClearClipboardRequested += _manager.ClearClipboard;
        _shell.NotesEditRequested += _manager.OpenNotesEditor;
        _shell.WeatherEditRequested += _manager.OpenWeatherEditor;
        _shell.LauncherEditRequested += _manager.OpenLauncherEditor;
        _shell.FocusCustomRequested += _manager.OpenFocusEditor;
        _shell.SystemMetricsEditRequested += _manager.OpenMetricsEditor;
        _shell.CardSizeEditRequested += _manager.OpenCardSizeEditor;
        _shell.LaunchAppRequested += _manager.LaunchApp;
    }

    // ----------------------------------------------------------- frame loop

    private void OnFrameTick()
    {
        if (IsHidden) return;

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
            NativeSetCursor(wantHand);
        }

        // Idle repaint suppression
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
            (_shell.IsExpanded && secondChanged && _shell.NeedsLiveUpdates) ||
            (!_shell.IsExpanded && _shell.Focus.IsActive && secondChanged) ||
            (!_shell.IsExpanded && _shell.Call is { OnCall: true } && secondChanged) ||
            (!_shell.IsExpanded && minuteChanged);

        if (dirty) NativeInvalidate();
    }

    // ----------------------------------------------------------- input

    private void HandleMouseDown(double x, double y)
    {
        if (_shell.DragToMoveEnabled && _shell.IsCollapsedPillAt(x, y))
        {
            _dragActive = true;
            _dragging = false;
            _dragStart = (x, y);
            _offsetDragStartX = _shell.OffsetX;
            _offsetDragStartY = _shell.OffsetY;
            _shell.Dragging = true;
            NativeCaptureMouse();
            return;
        }

        if (_shell.MediaSeekHit(x, y))
        {
            _seekDrag = true;
            _shell.BeginSeek(x, y);
            NativeCaptureMouse();
            return;
        }
        if (_shell.MediaVolumeHit(x, y))
        {
            _volumeDrag = true;
            _shell.BeginVolumeDrag(x, y);
            NativeCaptureMouse();
            return;
        }

        var widgetId = _shell.WidgetCardAt(x, y);
        if (widgetId is not null)
        {
            _dragWidgetId = widgetId;
            _dragActive = true;
            _dragging = false;
            _dragStart = (x, y);
            _lastWidgetIndex = -1;
            NativeCaptureMouse();
        }
    }

    private void HandleMouseMove(double x, double y)
    {
        if (_seekDrag) { _shell.UpdateSeek(x, y); return; }
        if (_volumeDrag) { _shell.UpdateVolumeDrag(x, y); return; }
        if (!_dragActive) return;
        if (!_dragging &&
            (Math.Abs(x - _dragStart.X) > 4 || Math.Abs(y - _dragStart.Y) > 4))
        {
            _dragging = true;
        }
        if (!_dragging) return;

        if (_dragWidgetId is not null)
        {
            var target = _shell.WidgetIndexAt(x, y);
            if (target >= 0 && target != _lastWidgetIndex)
            {
                _lastWidgetIndex = target;
                _shell.MoveWidgetTo(_dragWidgetId, target);
            }
            return;
        }

        _shell.SetOffset(
            _offsetDragStartX + x - _dragStart.X,
            _offsetDragStartY + y - _dragStart.Y);
    }

    private void HandleMouseUp(double x, double y)
    {
        if (_seekDrag || _volumeDrag)
        {
            if (_seekDrag) _shell.EndSeek();
            else _shell.EndVolumeDrag();
            _seekDrag = false;
            _volumeDrag = false;
            NativeReleaseMouse();
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
            NativeReleaseMouse();
            if (wasDragging)
            {
                if (widgetId is null)
                {
                    _settings.Update(s => s with
                    {
                        Appearance = s.Appearance with
                        {
                            OffsetX = _shell.OffsetX,
                            OffsetY = _shell.OffsetY,
                        },
                    });
                }
                return;
            }
        }
        _shell.Click(x, y);
    }

    private void HandleRightClick(double x, double y)
    {
        if (_shell.IsExpanded)
        {
            _shell.OpenContextMenu(x, y);
        }
    }

    // ----------------------------------------------------------- canvas

    private void OnPaintSurface(SKCanvas canvas, float scale, double actualWidth, double actualHeight)
    {
        canvas.Clear(SKColors.Transparent);
        var (screenLeft, screenTop) = NativeScreenOrigin();
        _shell.SetScreenContext((int)screenLeft, (int)screenTop, scale);
        canvas.Scale(scale, scale);
        _shell.SetCanvas(actualWidth, actualHeight);
        _shell.Draw(canvas);
    }
}
