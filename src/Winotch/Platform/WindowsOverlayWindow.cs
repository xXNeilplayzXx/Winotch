#if WINDOWS
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
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
    private const int WmMouseActivate = 0x0021;
    private const int HtTransparent = -1;
    private const int HtClient = 1;
    private const int MaNoActivate = 3;

    private Window? _window;
    private OverlaySkiaControl? _surface;
    private IntPtr _hwnd;
    private IntPtr _oldWndProc;
    private WndProcDelegate? _wndProcDelegate;

    private void NativeInit()
    {
        _window = new Window
        {
            SystemDecorations = SystemDecorations.None,
            TransparencyLevelHint = WindowTransparencyLevel.Transparent,
            Background = Avalonia.Media.Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            CanResize = false,
            IsActive = false,
            Title = "Winotch",
        };

        _surface = new OverlaySkiaControl(this);
        _window.Content = _surface;

        _window.Opened += (_, _) =>
        {
            _hwnd = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (_hwnd != IntPtr.Zero)
            {
                var style = (int)GetWindowLongPtr(_hwnd, GwlExStyle);
                style |= WsExToolWindow | WsExNoActivate;
                style &= ~WsExAppWindow;
                SetWindowLongPtr(_hwnd, GwlExStyle, (IntPtr)style);

                // Subclass window procedure for click-through
                _wndProcDelegate = new WndProcDelegate(WndProc);
                _oldWndProc = GetWindowLongPtr(_hwnd, GwlWndProc);
                SetWindowLongPtr(_hwnd, GwlWndProc, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
            }
        };

        _window.PointerPressed += OnPointerPressed;
        _window.PointerMoved += OnPointerMoved;
        _window.PointerReleased += OnPointerReleased;

        _window.Show();
        _shell.ApplyTheme();
    }

    private IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmNcHitTest)
        {
            // Extract screen coordinates from lParam
            int xPos = (short)(lParam.ToInt32() & 0xFFFF);
            int yPos = (short)((lParam.ToInt32() >> 16) & 0xFFFF);

            // Convert to local DIP coordinates
            var scale = CurrentDpi();
            var rect = WindowScreenRect();
            var localX = (xPos - rect.Left) / scale;
            var localY = (yPos - rect.Top) / scale;

            // Check if this point is interactive
            if (!_shell.IsInteractiveAt(localX, localY))
            {
                return (IntPtr)HtTransparent;
            }
            return (IntPtr)HtClient;
        }
        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
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
            _window.Position = new PixelPosition((int)left, (int)top);
            _window.Width = width;
            _window.Height = height;
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
        Dispatcher.UIThread.Post(() => _surface?.InvalidateVisual());
    }

    private void NativeCaptureMouse()
    {
        Dispatcher.UIThread.Post(() => _surface?.CaptureMouse());
    }

    private void NativeReleaseMouse()
    {
        Dispatcher.UIThread.Post(() => _surface?.ReleaseMouseCapture());
    }

    private (double X, double Y) CursorInLocalDip()
    {
        var scale = CurrentDpi();
        var rect = WindowScreenRect();
        GetCursorPos(out var pt);
        return ((pt.X - rect.Left) / scale, (pt.Y - rect.Top) / scale);
    }

    private double CurrentDpi()
    {
        if (_window is null) return 1.0;
        var scaling = _window.RenderScaling;
        return scaling;
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
        // Restore old window procedure before cleanup
        if (_hwnd != IntPtr.Zero && _oldWndProc != IntPtr.Zero)
        {
            SetWindowLongPtr(_hwnd, GwlWndProc, _oldWndProc);
            _oldWndProc = IntPtr.Zero;
        }

        if (_window is not null)
        {
            _window.PointerPressed -= OnPointerPressed;
            _window.PointerMoved -= OnPointerMoved;
            _window.PointerReleased -= OnPointerReleased;
            _window.Close();
            _window = null;
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
}

/// <summary>Custom Avalonia control that renders via SkiaSharp.</summary>
internal sealed class OverlaySkiaControl : Avalonia.Controls.Control
{
    private readonly OverlayWindow _owner;

    public OverlaySkiaControl(OverlayWindow owner)
    {
        _owner = owner;
        Avalonia.Rendering.RendererBase.IsHitTestVisibleProperty.OverrideDefaultValue(typeof(OverlaySkiaControl), true);
    }

    public override void Render(Avalonia.Media.DrawingContext context)
    {
        var scale = (float)(_owner.TryGetVisualRoot()?.RenderScaling ?? 1.0);
        var width = Bounds.Width;
        var height = Bounds.Height;

        // Create a temporary SKSurface for this frame
        using var surface = SKSurface.Create(new SKImageInfo(
            (int)(width * scale), (int)(height * scale),
            SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        _owner.OnRender(canvas, scale, width, height);

        // Push the SKSurface as an image into Avalonia's drawing context
        using var image = surface.Snapshot();
        using var pixmap = image.ToPixmap();
        using var data = pixmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        using var bitmap = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, (int)(width * scale));
        if (bitmap is not null)
        {
            context.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height),
                new Rect(0, 0, width, height));
        }
    }
}
#endif
