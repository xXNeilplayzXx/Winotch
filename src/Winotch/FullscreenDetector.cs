namespace Winotch;

/// <summary>Decides whether the notch should hide because a foreground window
/// is fullscreen on a monitor. A window counts as fullscreen when it covers
/// at least 98% of the monitor's bounds — real fullscreen (F11, borderless
/// games, video) covers 100%, while a maximized window always leaves the
/// taskbar strip visible (≈2.2–4.4% of the area at common resolutions), so
/// the threshold separates the two without needing window-style flags.
///
/// Windows shell surfaces (the desktop Progman/WorkerW and the taskbars)
/// cover the whole monitor too but must never hide the notch, so they are
/// excluded by window class. Pure and platform-independent: callers supply
/// the foreground rect, its window class and the monitor bounds.</summary>
public static class FullscreenDetector
{
    /// <summary>Fraction of the monitor a window must cover to count as
    /// fullscreen. Verified by simulation: maximized windows top out at
    /// 97.8% (4K with a 48px taskbar); true fullscreen is 100%.</summary>
    public const double CoverageThreshold = 0.98;

    /// <summary>Win32 window classes owned by the shell itself. The desktop
    /// (Progman / WorkerW) spans the whole monitor and the taskbars
    /// (Shell_TrayWnd / Shell_SecondaryTrayWnd) are system surfaces — none
    /// of them mean "the user is in a fullscreen app".</summary>
    private static readonly string[] ShellWindowClasses =
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
    };

    /// <summary>True when the notch should hide: a foreground window exists,
    /// is not a shell surface, and covers ≥ <see cref="CoverageThreshold"/>
    /// of the given monitor. Evaluated per monitor so a fullscreen game on
    /// one screen only hides that screen's island.</summary>
    public static bool ShouldHide(
        (int X, int Y, int Width, int Height)? foregroundRect,
        string? foregroundWindowClass,
        NativeRect monitorBounds)
    {
        if (foregroundRect is not { } rect) return false;
        if (IsShellWindowClass(foregroundWindowClass)) return false;
        return Coverage(rect, monitorBounds) >= CoverageThreshold;
    }

    /// <summary>Case-insensitive shell-class check. Null/empty classes are
    /// not shell windows (Linux has no Win32 class concept and reports
    /// null; coverage alone drives the decision there).</summary>
    public static bool IsShellWindowClass(string? className)
    {
        if (string.IsNullOrEmpty(className)) return false;
        foreach (var shell in ShellWindowClasses)
        {
            if (string.Equals(className, shell, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Intersection area of the window rect and the monitor bounds,
    /// divided by the monitor area. 0 for a zero-area monitor (never hide on
    /// a degenerate bounds report). Windows slightly larger than the monitor
    /// (the -8px maximized overhang) clip to 1.0.</summary>
    public static double Coverage(
        (int X, int Y, int Width, int Height) windowRect, NativeRect monitorBounds)
    {
        var monitorArea = (long)monitorBounds.Width * monitorBounds.Height;
        if (monitorArea <= 0) return 0;

        var left = Math.Max(windowRect.X, monitorBounds.Left);
        var top = Math.Max(windowRect.Y, monitorBounds.Top);
        var right = Math.Min(windowRect.X + windowRect.Width, monitorBounds.Right);
        var bottom = Math.Min(windowRect.Y + windowRect.Height, monitorBounds.Bottom);
        if (right <= left || bottom <= top) return 0;

        var intersection = (long)(right - left) * (bottom - top);
        return (double)intersection / monitorArea;
    }
}
