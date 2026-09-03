namespace Winotch.Tests;

public class ShellMetricsTests
{
    [Fact]
    public void Mini_IsCenteredAndNarrow()
    {
        var g = ShellMetrics.ForMode(ShellMode.Mini, 1920);
        Assert.Equal(ShellMetrics.MiniWidth, g.Width);
        Assert.Equal((1920 - ShellMetrics.MiniWidth) / 2, g.Left);
    }

    [Fact]
    public void Mini_FitsNarrowScreen()
    {
        var g = ShellMetrics.ForMode(ShellMode.Mini, 200);
        Assert.Equal(200, g.Width);
        Assert.Equal(0, g.Left);
    }

    [Fact]
    public void Expanded_IsTallerThanMini()
    {
        var mini = ShellMetrics.ForMode(ShellMode.Mini, 1920);
        var expanded = ShellMetrics.ForMode(ShellMode.Expanded, 1920);
        Assert.True(expanded.WindowHeight > mini.WindowHeight);
        Assert.True(expanded.Width >= mini.Width);
    }

    [Fact]
    public void PlaceOnMonitor_ClampsToMonitorBounds()
    {
        var monitor = new MonitorSnapshot("\\\\.\\DISPLAY1",
            new NativeRect(0, 0, 1920, 1080), new NativeRect(0, 0, 1920, 1040),
            true, 1.0, 1.0);
        var g = ShellMetrics.ForMode(ShellMode.Mini, 1920);
        var placed = ShellMetrics.PlaceOnMonitor(g, monitor);
        Assert.Equal(0, placed.Left);
        Assert.Equal(0, placed.Top);

        // Far-right monitor with a negative virtual offset
        var right = new MonitorSnapshot("\\\\.\\DISPLAY2",
            new NativeRect(1920, 0, 3840, 1080), new NativeRect(1920, 0, 3840, 1040),
            false, 1.0, 1.0);
        var placedRight = ShellMetrics.PlaceOnMonitor(g, right);
        Assert.InRange(placedRight.Left, 1920, 1920 + 1920 - ShellMetrics.MiniWidth);
    }
}
