using Winotch;

namespace Winotch.Tests;

/// <summary>The launcher scanner's "is this an app a user would launch"
/// heuristic: existing .exe targets pass, uninstallers / installers /
/// updater shortcuts and targets are filtered out.</summary>
public class LauncherScannerTests : IDisposable
{
    private readonly string _dir;
    private readonly string _exe;

    public LauncherScannerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"winotch-scan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _exe = Path.Combine(_dir, "app.exe");
        File.WriteAllText(_exe, "not really an exe, just an existing file");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void ExistingExe_Passes()
    {
        Assert.True(LauncherScanner.IsLikelyApp("My App", _exe));
    }

    [Fact]
    public void MissingTarget_Fails()
    {
        Assert.False(LauncherScanner.IsLikelyApp("My App", Path.Combine(_dir, "ghost.exe")));
    }

    [Fact]
    public void NonExeTarget_Fails()
    {
        var doc = Path.Combine(_dir, "manual.pdf");
        File.WriteAllText(doc, "x");
        Assert.False(LauncherScanner.IsLikelyApp("Manual", doc));
    }

    [Theory]
    [InlineData("Uninstall My App")]
    [InlineData("my app setup")]
    [InlineData("My App Installer")]
    [InlineData("My App Update")]
    [InlineData("Help")]
    public void UnlikelyShortcutNames_Fail(string name)
    {
        Assert.False(LauncherScanner.IsLikelyApp(name, _exe));
    }

    [Theory]
    [InlineData("uninstall.exe")]
    [InlineData("updater.exe")]
    [InlineData("crashpad_handler.exe")]
    public void UnlikelyTargetNames_Fail(string targetName)
    {
        var path = Path.Combine(_dir, targetName);
        File.WriteAllText(path, "x");
        Assert.False(LauncherScanner.IsLikelyApp("My App", path));
    }
}
