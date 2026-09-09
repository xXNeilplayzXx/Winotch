using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
#if WINDOWS
using FormsScreen = System.Windows.Forms.Screen;
#endif

namespace Winotch;

public readonly record struct MonitorSnapshot(
    string DeviceName, NativeRect Bounds, NativeRect WorkingArea,
    bool IsPrimary, double DpiScaleX, double DpiScaleY)
{
    public double LeftDip => Bounds.Left / DpiScaleX;
    public double TopDip => Bounds.Top / DpiScaleY;
    public double WidthDip => Bounds.Width / DpiScaleX;
    public double WorkAreaTopDip => WorkingArea.Top / DpiScaleY;
    public double WorkAreaBottomDip => WorkingArea.Bottom / DpiScaleY;
    public double WorkAreaRightDip => WorkingArea.Right / DpiScaleX;

    public bool Contains(Point point) =>
        point.X >= Bounds.Left && point.X < Bounds.Right &&
        point.Y >= Bounds.Top && point.Y < Bounds.Bottom;
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct NativeRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

#if WINDOWS
/// <summary>Multi-monitor targeting: pick the monitor that should own the
/// notch — the one holding the foreground window's center, else the cursor
/// monitor when on the desktop, else the last-known monitor, else primary.</summary>
public static class MonitorTargeting
{
    private const uint MonitorDefaultToNearest = 2;
    private const int EffectiveDpi = 0;

    public static IReadOnlyList<MonitorSnapshot> CaptureScreens() =>
        FormsScreen.AllScreens.Select(FromScreen).ToArray();

    public static Point GetCursorPosition() =>
        GetCursorPos(out var point) ? new Point(point.X, point.Y) : Point.Empty;

    public static Rectangle? GetForegroundWindowRect()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        if (!GetWindowRect(hwnd, out var rect)) return null;
        return new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height);
    }

    /// <summary>Win32 window class of the foreground window, used to exclude
    /// shell surfaces (desktop/taskbar) from fullscreen detection. Null when
    /// there is no foreground window or the class can't be read.</summary>
    public static string? GetForegroundWindowClassName()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        var buffer = new StringBuilder(256);
        return GetClassName(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : null;
    }

    public static IReadOnlyList<MonitorSnapshot> SelectTargets(
        IReadOnlyList<MonitorSnapshot> monitors, DisplayMode mode, string? deviceName)
    {
        if (mode == DisplayMode.All)
        {
            return monitors;
        }
        if (mode == DisplayMode.FollowActive)
        {
            return new[] { PrimaryOrFirst(monitors) };
        }
        var single = SelectSingle(monitors, mode, deviceName);
        return single is null ? Array.Empty<MonitorSnapshot>() : new[] { single.Value };
    }

    public static MonitorSnapshot? SelectSingle(
        IReadOnlyList<MonitorSnapshot> monitors, DisplayMode mode, string? deviceName)
    {
        if (monitors.Count == 0) return null;
        return mode switch
        {
            DisplayMode.Named => FindByDeviceName(monitors, deviceName) ?? PrimaryOrFirst(monitors),
            DisplayMode.FollowActive => SelectMonitor(monitors, GetForegroundWindowRect(),
                GetCursorPosition(), useCursorMonitor: true, null),
            _ => PrimaryOrFirst(monitors),
        };
    }

    public static MonitorSnapshot SelectMonitor(
        IReadOnlyList<MonitorSnapshot> monitors,
        Rectangle? foregroundRect,
        Point cursorPosition,
        bool useCursorMonitor,
        string? lastMonitorDeviceName)
    {
        if (monitors.Count == 0)
        {
            throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        }

        if (foregroundRect is { } rect)
        {
            var center = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
            if (FirstContaining(monitors, center) is { } byCenter) return byCenter;
            if (LargestOverlap(monitors, rect) is { } byOverlap) return byOverlap;
        }

        if (useCursorMonitor && FirstContaining(monitors, cursorPosition) is { } byCursor)
        {
            return byCursor;
        }

        return FindByDeviceName(monitors, lastMonitorDeviceName) ?? PrimaryOrFirst(monitors);
    }

    public static MonitorSnapshot FromScreen(FormsScreen screen)
    {
        var monitor = MonitorFromPoint(
            new NativePoint(screen.Bounds.Left + screen.Bounds.Width / 2,
                screen.Bounds.Top + screen.Bounds.Height / 2),
            MonitorDefaultToNearest);
        var (dx, dy) = GetDpiScale(monitor);
        return new MonitorSnapshot(screen.DeviceName,
            new NativeRect(screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Right, screen.Bounds.Bottom),
            new NativeRect(screen.WorkingArea.Left, screen.WorkingArea.Top,
                screen.WorkingArea.Right, screen.WorkingArea.Bottom),
            screen.Primary, dx, dy);
    }

    private static MonitorSnapshot? FirstContaining(IReadOnlyList<MonitorSnapshot> monitors, Point point)
    {
        foreach (var monitor in monitors)
        {
            if (monitor.Contains(point)) return monitor;
        }
        return null;
    }

    private static MonitorSnapshot? LargestOverlap(IReadOnlyList<MonitorSnapshot> monitors, Rectangle rect)
    {
        MonitorSnapshot? selected = null;
        long selectedArea = 0;
        foreach (var monitor in monitors)
        {
            var area = IntersectionArea(monitor.Bounds, ToNativeRect(rect));
            if (area > selectedArea)
            {
                selected = monitor;
                selectedArea = area;
            }
        }
        return selectedArea == 0 ? null : selected;
    }

    private static MonitorSnapshot? FindByDeviceName(IReadOnlyList<MonitorSnapshot> monitors, string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return null;
        foreach (var monitor in monitors)
        {
            if (string.Equals(monitor.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                return monitor;
            }
        }
        return null;
    }

    private static MonitorSnapshot PrimaryOrFirst(IReadOnlyList<MonitorSnapshot> monitors)
    {
        foreach (var monitor in monitors)
        {
            if (monitor.IsPrimary) return monitor;
        }
        return monitors[0];
    }

    private static long IntersectionArea(NativeRect first, NativeRect second)
    {
        var left = Math.Max(first.Left, second.Left);
        var top = Math.Max(first.Top, second.Top);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);
        if (right <= left || bottom <= top) return 0;
        return (long)(right - left) * (bottom - top);
    }

    private static NativeRect ToNativeRect(Rectangle rect) =>
        new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    private static (double X, double Y) GetDpiScale(IntPtr monitor)
    {
        if (monitor == IntPtr.Zero ||
            GetDpiForMonitor(monitor, EffectiveDpi, out var dpiX, out var dpiY) != 0)
        {
            return (1, 1);
        }
        return (dpiX / 96d, dpiY / 96d);
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);
}
#endif

#if !WINDOWS
/// <summary>Linux stub for MonitorTargeting. Uses LinuxMonitorService for screen capture.</summary>
public static class MonitorTargeting
{
    public static IReadOnlyList<MonitorSnapshot> CaptureScreens()
    {
        var service = new Winotch.Platform.Linux.LinuxMonitorService();
        return service.GetAllMonitors();
    }

    public static System.Drawing.Point GetCursorPosition()
    {
        var service = new Winotch.Platform.Linux.LinuxMonitorService();
        var (x, y) = service.GetCursorPosition();
        return new System.Drawing.Point(x, y);
    }

    public static System.Drawing.Rectangle? GetForegroundWindowRect()
    {
        var service = new Winotch.Platform.Linux.LinuxMonitorService();
        var rect = service.GetForegroundWindowRect();
        if (rect == null) return null;
        var (x, y, w, h) = rect.Value;
        return new System.Drawing.Rectangle(x, y, w, h);
    }

    public static IReadOnlyList<MonitorSnapshot> SelectTargets(
        IReadOnlyList<MonitorSnapshot> monitors, DisplayMode mode, string? deviceName)
    {
        if (mode == DisplayMode.All) return monitors;
        if (monitors.Count == 0) return Array.Empty<MonitorSnapshot>();
        return new[] { monitors[0] };
    }

    public static MonitorSnapshot? SelectSingle(
        IReadOnlyList<MonitorSnapshot> monitors, DisplayMode mode, string? deviceName)
    {
        if (monitors.Count == 0) return null;
        return monitors[0];
    }

    public static MonitorSnapshot SelectMonitor(
        IReadOnlyList<MonitorSnapshot> monitors,
        System.Drawing.Rectangle? foregroundRect,
        System.Drawing.Point cursorPosition,
        bool useCursorMonitor,
        string? lastMonitorDeviceName)
    {
        if (monitors.Count == 0) throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        return monitors[0];
    }
}
#endif
