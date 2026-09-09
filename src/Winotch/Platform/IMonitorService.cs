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
    /// <summary>Win32 window class of the foreground window (shell surfaces
    /// like Progman/Shell_TrayWnd are excluded from fullscreen hiding).
    /// Always null on Linux, which has no Win32 class concept.</summary>
    string? GetForegroundWindowClassName();
    MonitorSnapshot? GetMonitorContainingPoint(int x, int y);
}
