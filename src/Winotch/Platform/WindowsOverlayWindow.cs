#if WINDOWS
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;

namespace Winotch;

/// <summary>Windows overlay: a transparent Avalonia Window with Win32 interop
/// for click-through, custom SkiaSharp rendering, and Avalonia's render loop.
/// All Win32-specific behavior lives here.</summary>
public sealed partial class OverlayWindow
{
    private const int GwlExStyle = -20;
    private const int GwlWndProc = -4;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExAppWindow = 0x00040000;
    private const int WmNcHitTest = 0x0084;
    private const int WmNcDestroy = 0x0082;

    private const int HtTransparent = -1;
    private const int HtClient = 1;

    /// <summary>Frame cadence for the shell tick (about 60 fps), matching the
    /// Linux host. Without this the island never animates, never repaints and
    /// never reacts to the cursor.</summary>
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16);

    private Window? _window;
    private OverlaySkiaControl? _surface;
    private DispatcherTimer? _frameTimer;
    private IntPtr _hwnd;
    private IntPtr _oldWndProc;
    private WndProcDelegate? _wndProcDelegate;

    private void NativeInit()
    {
        _window = new Window
        {
            SystemDecorations = SystemDecorations.None,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Mica, WindowTransparencyLevel.Transparent },
            Background = Avalonia.Media.Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            CanResize = false,
            Title = "Winotch",
        };

        _surface = new OverlaySkiaControl(this);
        _window.Content = _surface;
        HostWindow = _window;

        _window.Opened += (_, _) =>
        {
            _hwnd = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (_hwnd != IntPtr.Zero)
            {
                var style = (int)GetWindowLongPtr(_hwnd, GwlExStyle);
                style |= WsExToolWindow | WsExNoActivate;
                style &= ~WsExAppWindow;
                SetWindowLongPtr(_hwnd, GwlExStyle, (IntPtr)style);

                // Subclass only when the window's current procedure is known:
                // chaining to a null pointer is an access violation, and an
                // access violation kills the process with nothing on screen.
                var previous = GetWindowLongPtr(_hwnd, GwlWndProc);
                if (previous != IntPtr.Zero)
                {
                    _wndProcDelegate = new WndProcDelegate(WndProc);
                    _oldWndProc = previous;
                    SetWindowLongPtr(_hwnd, GwlWndProc, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
                }
                else
                {
                    AppLog.Write("Overlay click-through unavailable: could not read the window procedure.");
                }
            }

            NativeInvalidate();
        };

        _window.PointerPressed += OnPointerPressed;
        _window.PointerMoved += OnPointerMoved;
        _window.PointerReleased += OnPointerReleased;

        _window.Show();
        StartFrameLoop();
        _shell.ApplyTheme();
    }

    /// <summary>Drive the shell from the Avalonia dispatcher: a tick updates
    /// cursor/hover/animation state and requests a repaint when anything
    /// changed. Runs on the UI thread so the shell is never mutated while a
    /// frame is being built.</summary>
    private void StartFrameLoop()
    {
        if (_frameTimer is not null) return;

        _frameTimer = new DispatcherTimer { Interval = FrameInterval };
        _frameTimer.Tick += (_, _) =>
        {
            try
            {
                OnFrameTick();
            }
            catch (Exception ex)
            {
                // A bad frame must never take the whole app down.
                AppLog.Report("overlay frame", ex);
            }
        };
        _frameTimer.Start();
        AppLog.Write("overlay frame loop started (16 ms cadence)");
    }

    private void StopFrameLoop()
    {
        _frameTimer?.Stop();
        _frameTimer = null;
    }

    private IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmNcHitTest)
        {
            try
            {
                int xPos = (short)(lParam.ToInt32() & 0xFFFF);
                int yPos = (short)((lParam.ToInt32() >> 16) & 0xFFFF);

                var scale = CurrentDpi();
                var rect = WindowScreenRect();
                var localX = (xPos - rect.Left) / scale;
                var localY = (yPos - rect.Top) / scale;

                return _shell.IsInteractiveAt(localX, localY) ? (IntPtr)HtClient : (IntPtr)HtTransparent;
            }
            catch (Exception ex)
            {
                // Never let a hit-test failure escape into the native message
                // pump: the window defaults to the desktop behaviour instead.
                AppLog.Report("overlay hit test", ex);
                return (IntPtr)HtTransparent;
            }
        }

        if (msg == WmNcDestroy)
        {
            var original = UnhookWndProc(hWnd);
            return original != IntPtr.Zero
                ? CallWindowProc(original, hWnd, msg, wParam, lParam)
                : DefWindowProc(hWnd, msg, wParam, lParam);
        }

        return _oldWndProc != IntPtr.Zero
            ? CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam)
            : DefWindowProc(hWnd, msg, wParam, lParam);
    }

    /// <summary>Put the window's original procedure back, so no later message can
    /// jump into our delegate once the window is going away, and return it so
    /// the current message can still be chained on.</summary>
    private IntPtr UnhookWndProc(IntPtr hWnd)
    {
        var original = _oldWndProc;
        if (original == IntPtr.Zero) return IntPtr.Zero;

        _oldWndProc = IntPtr.Zero;
        if (hWnd != IntPtr.Zero && IsWindow(hWnd))
        {
            SetWindowLongPtr(hWnd, GwlWndProc, original);
        }
        return original;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var p = e.GetPosition(_surface);
        if (e.GetCurrentPoint(_window).Properties.IsLeftButtonPressed)
            HandleMouseDown(p.X, p.Y);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var p = e.GetPosition(_surface);
        HandleMouseMove(p.X, p.Y);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var p = e.GetPosition(_surface);
        if (e.InitialPressMouseButton == MouseButton.Right)
            HandleRightClick(p.X, p.Y);
        else
            HandleMouseUp(p.X, p.Y);
    }

    internal void OnRender(SKCanvas canvas, float scale, double width, double height)
    {
        OnPaintSurface(canvas, scale, width, height);
    }

    private void NativeHide() => Dispatcher.UIThread.Post(() => _window?.Hide());
    private void NativeShow() => Dispatcher.UIThread.Post(() => _window?.Show());

    private void NativeSetBounds(double left, double top, double width, double height)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_window is null) return;
            _window.Position = new PixelPoint((int)left, (int)top);
            _window.Width = width;
            _window.Height = height;
            _surface?.InvalidateVisual();
        });
    }

    private void NativeSetCursor(bool hand)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_window is null) return;
            _window.Cursor = hand ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow);
        });
    }

    private void NativeInvalidate()
    {
        if (Dispatcher.UIThread.CheckAccess()) _surface?.InvalidateVisual();
        else Dispatcher.UIThread.Post(() => _surface?.InvalidateVisual());
    }

    private void NativeCaptureMouse()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_surface is not null)
                _surface.Focus();
        });
    }

    private void NativeReleaseMouse()
    {
        Dispatcher.UIThread.Post(() => { });
    }

    private (double X, double Y) CursorInLocalDip()
    {
        var scale = CurrentDpi();
        var rect = WindowScreenRect();
        GetCursorPos(out var pt);
        return ((pt.X - rect.Left) / scale, (pt.Y - rect.Top) / scale);
    }

    internal double CurrentDpi()
    {
        if (_window is null) return 1.0;
        var scaling = _window.RenderScaling;
        return scaling > 0.001 ? scaling : 1.0;
    }

    private (int Left, int Top, int Right, int Bottom) WindowScreenRect()
    {
        if (_hwnd == IntPtr.Zero) return (0, 0, 0, 0);
        GetWindowRect(_hwnd, out var rect);
        return (rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    private (double Left, double Top) NativeScreenOrigin()
    {
        var rect = WindowScreenRect();
        return (rect.Left, rect.Top);
    }

    private void NativeCleanup()
    {
        StopFrameLoop();

        UnhookWndProc(_hwnd);
        _hwnd = IntPtr.Zero;

        if (_window is not null)
        {
            _window.PointerPressed -= OnPointerPressed;
            _window.PointerMoved -= OnPointerMoved;
            _window.PointerReleased -= OnPointerReleased;
            _window.Close();
            _window = null;
            HostWindow = null;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

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

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);
}

/// <summary>Custom Avalonia control that renders the island with SkiaSharp into a
/// reusable writeable bitmap. The Skia surface is installed directly on the
/// bitmap's framebuffer, so a frame costs one draw pass — no per-frame bitmap
/// allocation, copy or PNG round-trip.</summary>
internal sealed class OverlaySkiaControl : Avalonia.Controls.Control
{
    private readonly OverlayWindow _owner;

    private WriteableBitmap? _frame;
    private int _pixelWidth;
    private int _pixelHeight;

    public OverlaySkiaControl(OverlayWindow owner)
    {
        _owner = owner;
    }

    public override void Render(Avalonia.Media.DrawingContext context)
    {
        // Before the first layout pass (and for a collapsed or zero-size window)
        // there is nothing valid to paint into: a zero-sized Skia surface cannot
        // be created, and that used to take the process down with it.
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width < 1 || height < 1) return;

        var scale = _owner.CurrentDpi();
        var pixelWidth = (int)Math.Ceiling(width * scale);
        var pixelHeight = (int)Math.Ceiling(height * scale);
        if (pixelWidth < 1 || pixelHeight < 1) return;

        var frame = EnsureFrame(pixelWidth, pixelHeight);
        if (frame is null) return;

        try
        {
            using (var locked = frame.Lock())
            {
                var info = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var surface = SKSurface.Create(info, locked.Address, locked.RowBytes);
                if (surface is null) return;

                var canvas = surface.Canvas;
                _owner.OnRender(canvas, (float)scale, width, height);
                canvas.Flush();
            }
        }
        catch (Exception ex)
        {
            // Keep the app alive and the previous frame on screen; the island is
            // repainted on the next tick.
            AppLog.Report("overlay paint", ex);
            return;
        }

        context.DrawImage(frame, new Rect(0, 0, pixelWidth, pixelHeight), new Rect(0, 0, width, height));
    }

    /// <summary>The frame buffer is only recreated when the pixel size actually
    /// changes (monitor move / DPI change / expand animation on Linux), not once
    /// per frame.</summary>
    private WriteableBitmap? EnsureFrame(int pixelWidth, int pixelHeight)
    {
        if (_frame is not null && _pixelWidth == pixelWidth && _pixelHeight == pixelHeight)
        {
            return _frame;
        }

        _frame?.Dispose();
        _frame = null;
        _pixelWidth = 0;
        _pixelHeight = 0;

        try
        {
            _frame = new WriteableBitmap(
                new PixelSize(pixelWidth, pixelHeight),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
            _pixelWidth = pixelWidth;
            _pixelHeight = pixelHeight;
            // Proof that the overlay reached the renderer at a usable size; the
            // island draws into this buffer from now on.
            AppLog.Write($"overlay frame buffer {pixelWidth}x{pixelHeight} px");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Winotch] Overlay buffer {pixelWidth}x{pixelHeight} unavailable: {ex.Message}");
            _frame = null;
        }

        return _frame;
    }
}
#endif
