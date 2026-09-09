using System.Drawing;

namespace Winotch.Platform.Windows;

/// <summary>Windows monitor service using System.Windows.Forms.Screen and Win32 APIs.</summary>
public sealed class WindowsMonitorService : IMonitorService
{
    public IReadOnlyList<MonitorSnapshot> GetAllMonitors()
    {
        return MonitorTargeting.CaptureScreens();
    }

    public (int X, int Y) GetCursorPosition()
    {
        var point = MonitorTargeting.GetCursorPosition();
        return (point.X, point.Y);
    }

    public (int X, int Y, int Width, int Height)? GetForegroundWindowRect()
    {
        var rect = MonitorTargeting.GetForegroundWindowRect();
        if (rect is null) return null;
        return (rect.Value.X, rect.Value.Y, rect.Value.Width, rect.Value.Height);
    }

    public string? GetForegroundWindowClassName() => MonitorTargeting.GetForegroundWindowClassName();

    public MonitorSnapshot? GetMonitorContainingPoint(int x, int y)
    {
        var monitors = GetAllMonitors();
        var point = new Point(x, y);
        foreach (var monitor in monitors)
        {
            if (monitor.Contains(point)) return monitor;
        }
        return null;
    }
}
