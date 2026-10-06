using System.Runtime.InteropServices;

namespace Winotch;

/// <summary>Monitor brightness through DDC/CI (dxva2), read-only.
///
/// Winotch mirrors brightness rather than claiming its keys: Windows defines no
/// virtual key for brightness, and on most laptops the brightness keys are
/// handled by the embedded controller and the maker's own utility before
/// Windows ever sees a keystroke — nothing an app can hook. Reading the level
/// instead means the HUD appears whenever brightness moves, whatever moved it,
/// at the cost of the poll interval and of the maker's own OSD still showing.
///
/// Null (the field is unavailable) is a normal outcome: virtual machines,
/// desktop monitors behind certain adapters and some internal panels don't
/// answer DDC/CI at all.</summary>
public static class BrightnessService
{
    private const uint MonitorDefaultToPrimary = 1;

    private static readonly object Gate = new();
    private static IntPtr _monitor;
    private static uint _minimum;
    private static uint _maximum;

    /// <summary>Brightness as 0..100, or null when the monitor won't report it.
    /// A failed read drops the cached handle so the next call reopens it — the
    /// display may have been unplugged or switched.</summary>
    public static int? Level
    {
        get
        {
            lock (Gate)
            {
                if (_monitor == IntPtr.Zero && !Open()) return null;
                if (!GetMonitorBrightness(_monitor, out _minimum, out var current, out _maximum))
                {
                    Close();
                    return null;
                }
                if (_maximum <= _minimum) return null;
                var percent = (current - _minimum) / (double)(_maximum - _minimum) * 100;
                return (int)Math.Round(Math.Clamp(percent, 0, 100));
            }
        }
    }

    /// <summary>Drop the cached monitor handle (shutdown, or a display change).</summary>
    public static void Release()
    {
        lock (Gate) Close();
    }

    private static bool Open()
    {
        try
        {
            var hMonitor = MonitorFromPoint(new Point { X = 0, Y = 0 }, MonitorDefaultToPrimary);
            if (hMonitor == IntPtr.Zero) return false;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out var count) || count == 0) return false;

            var physical = new PhysicalMonitor[count];
            if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physical)) return false;

            // The primary display's first physical monitor is the one the pill
            // sits on; release the rest so the handles don't leak on multi-head
            // setups.
            _monitor = physical[0].Handle;
            for (var i = 1; i < physical.Length; i++) DestroyPhysicalMonitor(physical[i].Handle);
            return _monitor != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    private static void Close()
    {
        var monitor = _monitor;
        _monitor = IntPtr.Zero;
        if (monitor == IntPtr.Zero) return;
        try
        {
            DestroyPhysicalMonitor(monitor);
        }
        catch
        {
            // Already gone.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public IntPtr Handle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitor[] physical);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorBrightness(IntPtr monitor, out uint minimum, out uint current, out uint maximum);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitor(IntPtr monitor);
}
