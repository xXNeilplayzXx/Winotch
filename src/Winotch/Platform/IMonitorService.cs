namespace Winotch.Platform;

/// <summary>Abstracts monitor enumeration and cursor/active window tracking.
/// Uses the app-model MonitorSnapshot (MonitorTargeting.cs) with NativeRect.
/// Windows: System.Windows.Forms.Screen, GetDpiForMonitor, GetCursorPos, GetForegroundWindow
/// Linux: XRandR, XInput, _NET_ACTIVE_WINDOW (X11); xdg-output, zwlr_output_management (Wayland)</summary>
public interface IMonitorService
{
    IReadOnlyList<MonitorSnapshot> GetAllMonitors();
    (int X, int Y) GetCursorPosition();
    (int X, int Y, int Width, int Height)? GetForegroundWindowRect();
    MonitorSnapshot? GetMonitorContainingPoint(int x, int y);
}
