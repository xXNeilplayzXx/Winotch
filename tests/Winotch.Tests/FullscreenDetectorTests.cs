using Winotch;

namespace Winotch.Tests;

/// <summary>The notch hides per monitor when the foreground window covers
/// ≥98% of that monitor, except for shell surfaces (desktop/taskbar), which
/// also span 100% but never mean "the user is in a fullscreen app".
/// Numbers mirror the python3 boundary simulation: maximized windows leave
/// the taskbar strip (≤97.8% at 4K/48px) and must not trigger a hide.</summary>
public class FullscreenDetectorTests
{
    private static readonly NativeRect Primary1080 = new(0, 0, 1920, 1080);
    private static readonly NativeRect Second1080 = new(1920, 0, 3840, 1080);
    private static readonly NativeRect Monitor4K = new(0, 0, 3840, 2160);

    // ------------------------------------------------------- coverage math

    [Fact]
    public void Coverage_FullscreenIsOne()
    {
        Assert.Equal(1.0, FullscreenDetector.Coverage((0, 0, 1920, 1080), Primary1080));
    }

    [Fact]
    public void Coverage_MaximizedWithTaskbar_BelowThreshold()
    {
        // 1080p with a 48px taskbar: 1920*1032 / 1920*1080 ≈ 0.9556.
        var c = FullscreenDetector.Coverage((0, 0, 1920, 1032), Primary1080);
        Assert.True(c < FullscreenDetector.CoverageThreshold);
    }

    [Fact]
    public void Coverage_Maximized4KWith48pxTaskbar_JustBelowThreshold()
    {
        // The closest call from the simulation: 3840*2112 / 3840*2160 ≈ 0.9778.
        var c = FullscreenDetector.Coverage((0, 0, 3840, 2112), Monitor4K);
        Assert.Equal(0.97777, c, 4);
        Assert.True(c < FullscreenDetector.CoverageThreshold);
    }

    [Fact]
    public void Coverage_OverhangingWindow_ClipsToOne()
    {
        // Windows' -8px maximized overhang extends past every monitor edge.
        var c = FullscreenDetector.Coverage((-8, -8, 1936, 1096), Primary1080);
        Assert.Equal(1.0, c);
    }

    [Fact]
    public void Coverage_NoOverlap_IsZero()
    {
        Assert.Equal(0.0, FullscreenDetector.Coverage((5000, 5000, 1920, 1080), Primary1080));
    }

    [Fact]
    public void Coverage_ZeroAreaMonitor_NeverDivides()
    {
        Assert.Equal(0.0, FullscreenDetector.Coverage((0, 0, 100, 100), new NativeRect(0, 0, 0, 0)));
    }

    // ------------------------------------------------------- hide decision

    [Fact]
    public void ShouldHide_F11Fullscreen_Hides()
    {
        Assert.True(FullscreenDetector.ShouldHide((0, 0, 1920, 1080), "Chrome_WidgetWin_1", Primary1080));
    }

    [Fact]
    public void ShouldHide_BorderlessGame_Hides()
    {
        Assert.True(FullscreenDetector.ShouldHide((0, 0, 3840, 2160), "UnigineEngineWindowClass", Monitor4K));
    }

    [Fact]
    public void ShouldHide_MaximizedWindow_StaysVisible()
    {
        Assert.False(FullscreenDetector.ShouldHide((0, 0, 1920, 1032), "CabinetWClass", Primary1080));
    }

    [Fact]
    public void ShouldHide_NoForegroundWindow_StaysVisible()
    {
        Assert.False(FullscreenDetector.ShouldHide(null, null, Primary1080));
    }

    // ------------------------------------------------- shell-class exclusions

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("shell_traywnd")] // case-insensitive
    public void ShouldHide_ShellSurfaces_NeverHide(string windowClass)
    {
        // The desktop spans the whole monitor but must not hide the notch.
        Assert.True(FullscreenDetector.IsShellWindowClass(windowClass));
        Assert.False(FullscreenDetector.ShouldHide((0, 0, 1920, 1080), windowClass, Primary1080));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsShellWindowClass_NullOrEmpty_IsFalse(string? windowClass)
    {
        Assert.False(FullscreenDetector.IsShellWindowClass(windowClass));
    }

    [Fact]
    public void ShouldHide_LinuxNullClass_DecidesOnCoverageAlone()
    {
        // Linux reports no Win32 class; coverage alone drives the decision.
        Assert.True(FullscreenDetector.ShouldHide((0, 0, 1920, 1080), null, Primary1080));
        Assert.False(FullscreenDetector.ShouldHide((0, 0, 1920, 1032), null, Primary1080));
    }

    // ------------------------------------------------------- per-monitor

    [Fact]
    public void ShouldHide_FullscreenOnSecondMonitor_OnlyThatMonitorHides()
    {
        var gameOnSecond = (1920, 0, 1920, 1080);
        Assert.True(FullscreenDetector.ShouldHide(gameOnSecond, "SDL_app", Second1080));
        Assert.False(FullscreenDetector.ShouldHide(gameOnSecond, "SDL_app", Primary1080));
    }
}
