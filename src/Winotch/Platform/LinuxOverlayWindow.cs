#if !WINDOWS
using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Winotch;

/// <summary>Linux overlay using X11 (Xlib) for X11 sessions. Creates an
/// override-redirect window with XShape for transparency and XFixes for
/// click-through input regions. Wayland support requires wlr-layer-shell
/// or xwayland.</summary>
public sealed partial class OverlayWindow
{
    private Timer? _frameTimer;
    private IntPtr _display;
    private IntPtr _window;
    private IntPtr _gc;
    private IntPtr _ximage;
    private int _width, _height;
    private double _nativeScale = 1.0;
    private (int Left, int Top) _screenOrigin;
    private SKBitmap? _bitmap;
    private SKSurface? _surface;
    private bool _visible;

    private void NativeInit()
    {
        // TODO: Detect Wayland vs X11
        // For now, assume X11
        InitX11();

        _frameTimer = new Timer(_ => OnFrameTick(), null, 0, 16); // ~60fps
        _shell.ApplyTheme();
    }

    private void InitX11()
    {
        _display = XLib.XOpenDisplay(IntPtr.Zero);
        if (_display == IntPtr.Zero)
            throw new InvalidOperationException("Cannot open X11 display");

        var screen = XLib.XDefaultScreen(_display);
        var root = XLib.XRootWindow(_display, screen);

        // Create override-redirect window (bypasses window manager)
        var attributes = new XSetWindowAttributes
        {
            override_redirect = true,
            colormap = XLib.XDefaultColormap(_display, screen),
            border_pixel = 0,
            background_pixel = 0,
        };

        _window = XLib.XCreateWindow(
            _display, root,
            0, 0, 100u, 100u, 0u,
            XLib.XDefaultDepth(_display, screen),
            1, // InputOutput
            IntPtr.Zero,
            (ulong)(CWOverrideRedirect | CWColormap | CWBorderPixel | CWBackPixel),
            ref attributes);

        // Set window type to desktop/overlay (hint to compositor)
        SetWindowType();

        // Create GC for drawing
        _gc = XLib.XCreateGC(_display, _window, 0, IntPtr.Zero);

        // Map the window (make it visible)
        XLib.XMapWindow(_display, _window);
        XLib.XFlush(_display);
    }

    private void SetWindowType()
    {
        // Set _NET_WM_WINDOW_TYPE to _NET_WM_WINDOW_TYPE_DOCK or UTILITY
        var typeAtom = XLib.XInternAtom(_display, "_NET_WM_WINDOW_TYPE", false);
        var dockAtom = XLib.XInternAtom(_display, "_NET_WM_WINDOW_TYPE_DOCK", false);

        XLib.XChangeProperty(
            _display, _window,
            typeAtom,
            XLib.XInternAtom(_display, "ATOM", false),
            32, PropModeReplace,
            ref dockAtom, 1);
    }

    private void NativeHide()
    {
        if (_window != IntPtr.Zero && _visible)
        {
            XLib.XUnmapWindow(_display, _window);
            XLib.XFlush(_display);
            _visible = false;
        }
    }

    private void NativeShow()
    {
        if (_window != IntPtr.Zero && !_visible)
        {
            XLib.XMapWindow(_display, _window);
            XLib.XFlush(_display);
            _visible = true;
        }
    }

    private void NativeSetBounds(double left, double top, double width, double height)
    {
        _width = (int)width;
        _height = (int)height;
        _screenOrigin = ((int)left, (int)top);

        if (_window != IntPtr.Zero)
        {
            XLib.XMoveResizeWindow(_display, _window, (int)left, (int)top, (uint)_width, (uint)_height);

            // Recreate backing bitmap
            _bitmap?.Dispose();
            _surface?.Dispose();
            _bitmap = new SKBitmap(_width, _height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var surfaceInfo = new SKImageInfo(_width, _height, SKColorType.Bgra8888, SKAlphaType.Premul);
            _surface = SKSurface.Create(surfaceInfo);

            // Set input region (click-through except where island is)
            UpdateInputRegion();
            XLib.XFlush(_display);
        }
    }

    private void UpdateInputRegion()
    {
        // Get interactive regions from the shell in DIP coordinates
        var regions = _shell.GetInteractiveRegions();

        if (regions.Count == 0)
        {
            // No interactive regions - make entire window click-through
            var region = XFixes.XFixesCreateRegion(_display, IntPtr.Zero, 0);
            XFixes.XFixesSetWindowShapeRegion(_display, _window, ShapeBounding, 0, 0, region);
            XFixes.XFixesDestroyRegion(_display, region);
            return;
        }

        // Convert DIP rectangles to pixel coordinates and create XFixes rectangles
        var rectangles = new XRectangle[regions.Count];
        for (int i = 0; i < regions.Count; i++)
        {
            var rect = regions[i];
            rectangles[i] = new XRectangle
            {
                x = (short)(rect.Left * _nativeScale),
                y = (short)(rect.Top * _nativeScale),
                width = (ushort)((rect.Right - rect.Left) * _nativeScale),
                height = (ushort)((rect.Bottom - rect.Top) * _nativeScale),
            };
        }

        // Create region from rectangles
        var regionHandle = XFixes.XFixesCreateRegion(_display, rectangles, rectangles.Length);
        XFixes.XFixesSetWindowShapeRegion(_display, _window, ShapeBounding, 0, 0, regionHandle);
        XFixes.XFixesDestroyRegion(_display, regionHandle);
    }

    private void NativeSetCursor(bool hand)
    {
        // TODO: Set X11 cursor
        // XDefineCursor with appropriate cursor font
    }

    private void NativeInvalidate()
    {
        if (_window == IntPtr.Zero || _bitmap == null || _surface == null) return;

        // Render the shell to the bitmap
        var canvas = _surface.Canvas;
        OnPaintSurface(canvas, (float)_nativeScale, _width, _height);

        // Update input region to match current island state
        UpdateInputRegion();

        // Push to X11 window via XImage
        PushToX11();
    }

    private void PushToX11()
    {
        if (_bitmap == null) return;

        var pixels = _bitmap.GetPixels();
        var size = _bitmap.RowBytes * _height;

        // Create XImage from bitmap data
        var ximage = XLib.XCreateImage(
            _display,
            XLib.XDefaultVisual(_display, XLib.XDefaultScreen(_display)),
            (uint)XLib.XDefaultDepth(_display, XLib.XDefaultScreen(_display)),
            2, 0, pixels, (uint)_width, (uint)_height, 32, _bitmap.RowBytes);

        XLib.XPutImage(_display, _window, _gc, ximage, 0, 0, 0, 0, (uint)_width, (uint)_height);
        XLib.XFlush(_display);

        // Don't free the image data (it's owned by the bitmap)
        Marshal.FreeHGlobal(ximage);
    }

    private void NativeCaptureMouse()
    {
        // TODO: XGrabPointer for drag operations
    }

    private void NativeReleaseMouse()
    {
        // TODO: XUngrabPointer
    }

    private (double X, double Y) CursorInLocalDip()
    {
        try
        {
            var output = RunCommand("xdotool", "getmouselocation");
            if (output != null)
            {
                int cx = 0, cy = 0;
                foreach (var part in output.Split(' '))
                {
                    if (part.StartsWith("x:")) int.TryParse(part[2..], out cx);
                    else if (part.StartsWith("y:")) int.TryParse(part[2..], out cy);
                }

                return ((cx - _screenOrigin.Left) / _nativeScale,
                        (cy - _screenOrigin.Top) / _nativeScale);
            }
        }
        catch
        {
        }

        return (0, 0);
    }

    private (double Left, double Top) NativeScreenOrigin() => _screenOrigin;

    private void NativeCleanup()
    {
        _frameTimer?.Dispose();
        _frameTimer = null;

        _bitmap?.Dispose();
        _surface?.Dispose();

        if (_display != IntPtr.Zero)
        {
            if (_window != IntPtr.Zero)
            {
                XLib.XDestroyWindow(_display, _window);
                _window = IntPtr.Zero;
            }
            if (_gc != IntPtr.Zero)
            {
                XLib.XFreeGC(_display, _gc);
                _gc = IntPtr.Zero;
            }
            XLib.XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }
    }

    private static string? RunCommand(string fileName, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process == null) return null;

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : null;
    }

    private const int CWOverrideRedirect = 1 << 9;
    private const int CWColormap = 1 << 13;
    private const int CWBorderPixel = 1 << 11;
    private const int CWBackPixel = 1 << 1;
    private const int PropModeReplace = 0;
    private const int ShapeBounding = 0;
}

#region X11 P/Invoke

internal static partial class XLib
{
    private const string LibX11 = "libX11.so.6";

    [DllImport(LibX11)] public static extern IntPtr XOpenDisplay(IntPtr display);
    [DllImport(LibX11)] public static extern void XCloseDisplay(IntPtr display);
    [DllImport(LibX11)] public static extern int XDefaultScreen(IntPtr display);
    [DllImport(LibX11)] public static extern IntPtr XRootWindow(IntPtr display, int screen);
    [DllImport(LibX11)] public static extern IntPtr XDefaultColormap(IntPtr display, int screen);
    [DllImport(LibX11)] public static extern int XDefaultDepth(IntPtr display, int screen);
    [DllImport(LibX11)] public static extern IntPtr XDefaultVisual(IntPtr display, int screen);

    [DllImport(LibX11)]
    public static extern IntPtr XCreateWindow(
        IntPtr display, IntPtr parent,
        int x, int y, uint width, uint height, uint border_width, int depth,
        int c_class, IntPtr visual, ulong valuemask, ref XSetWindowAttributes attributes);

    [DllImport(LibX11)] public static extern int XMapWindow(IntPtr display, IntPtr window);
    [DllImport(LibX11)] public static extern int XUnmapWindow(IntPtr display, IntPtr window);
    [DllImport(LibX11)] public static extern int XDestroyWindow(IntPtr display, IntPtr window);
    [DllImport(LibX11)] public static extern int XMoveResizeWindow(IntPtr display, IntPtr window, int x, int y, uint width, uint height);
    [DllImport(LibX11)] public static extern int XFlush(IntPtr display);

    [DllImport(LibX11)] public static extern IntPtr XCreateGC(IntPtr display, IntPtr window, ulong valuemask, IntPtr values);
    [DllImport(LibX11)] public static extern int XFreeGC(IntPtr display, IntPtr gc);

    [DllImport(LibX11)] public static extern IntPtr XInternAtom(IntPtr display, string atom_name, bool only_if_exists);

    [DllImport(LibX11)]
    public static extern int XChangeProperty(
        IntPtr display, IntPtr window, IntPtr property, IntPtr type,
        int format, int mode, ref IntPtr data, int nelements);

    [DllImport(LibX11)]
    public static extern IntPtr XCreateImage(
        IntPtr display, IntPtr visual, uint depth, int format, int offset,
        IntPtr data, uint width, uint height, int bitmap_pad, int bytes_per_line);

    [DllImport(LibX11)]
    public static extern int XPutImage(
        IntPtr display, IntPtr drawable, IntPtr gc, IntPtr image,
        int src_x, int src_y, int dest_x, int dest_y, uint width, uint height);
}

[StructLayout(LayoutKind.Sequential)]
internal struct XSetWindowAttributes
{
    public IntPtr background_pixmap;
    public ulong background_pixel;
    public IntPtr border_pixmap;
    public ulong border_pixel;
    public int bit_gravity;
    public int win_gravity;
    public int backing_store;
    public ulong backing_planes;
    public ulong backing_pixel;
    public bool save_under;
    public IntPtr event_mask;
    public long do_not_propagate_mask;
    public bool override_redirect;
    public IntPtr colormap;
    public IntPtr cursor;
}

internal static class XFixes
{
    private const string LibXfixes = "libXfixes.so.3";

    [DllImport(LibXfixes)]
    public static extern IntPtr XFixesCreateRegion(IntPtr display, [In] XRectangle[] rectangles, int count);

    [DllImport(LibXfixes)]
    public static extern IntPtr XFixesCreateRegion(IntPtr display, IntPtr rectangles, int count);

    [DllImport(LibXfixes)] public static extern void XFixesDestroyRegion(IntPtr display, IntPtr region);
    [DllImport(LibXfixes)] public static extern void XFixesSetWindowShapeRegion(IntPtr display, IntPtr window, int shapeType, int x, int y, IntPtr region);
}

[StructLayout(LayoutKind.Sequential)]
internal struct XRectangle
{
    public short x;
    public short y;
    public ushort width;
    public ushort height;
}

#endregion
#endif
