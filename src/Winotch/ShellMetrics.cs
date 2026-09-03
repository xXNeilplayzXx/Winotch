namespace Winotch;

public enum ShellMode { Mini, Expanded }

/// <summary>Shell geometry in device-independent pixels. The window is larger
/// than the visible shell so drop shadows have room; toasts hang below.</summary>
public readonly record struct ShellGeometry(
    double Width, double ShellHeight, double WindowHeight, double Left, double Top = 0);

public static class ShellMetrics
{
    public const double MiniWidth = 300;
    public const double MiniShellHeight = 52;
    public const double MiniWindowHeight = 64;

    public const double ExpandedWidth = 380;
    public const double ExpandedPanelHeight = 460;

    public const double StatusToastWidth = 320;
    public const double StatusToastHeight = 62;

    public static double CenterLeft(double screenWidth, double width) =>
        Math.Max(0, (screenWidth - width) / 2);

    public static ShellGeometry ForMode(ShellMode mode, double screenWidth, double top = 0)
    {
        if (mode == ShellMode.Mini)
        {
            return new ShellGeometry(
                Math.Min(MiniWidth, screenWidth), MiniShellHeight, MiniWindowHeight,
                CenterLeft(screenWidth, Math.Min(MiniWidth, screenWidth)), top);
        }

        var width = Math.Min(ExpandedWidth, screenWidth);
        var shellHeight = MiniShellHeight + ExpandedPanelHeight;
        return new ShellGeometry(width, shellHeight, shellHeight + 12, CenterLeft(screenWidth, width), top);
    }

    /// <summary>Place a geometry centered at the top of a monitor's working
    /// area, clamped so it never leaves the monitor.</summary>
    public static ShellGeometry PlaceOnMonitor(ShellGeometry geometry, MonitorSnapshot monitor)
    {
        var left = monitor.LeftDip + geometry.Left;
        var minLeft = monitor.LeftDip;
        var maxLeft = monitor.LeftDip + Math.Max(0, monitor.WidthDip - geometry.Width);
        return geometry with
        {
            Left = Math.Clamp(left, minLeft, maxLeft),
            Top = monitor.WorkAreaTopDip,
        };
    }
}
