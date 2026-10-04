using Winotch;

namespace Winotch.Tests;


public class UpdateServiceTests
{
    private const string LatestPayload = """
    {
      "tag_name": "v2.1.2",
      "name": "Winotch v2.1.2",
      "assets": [
        {
          "name": "Winotch-v2.1.2-x64.zip",
          "size": 76095876,
          "browser_download_url": "https://github.com/xXNeilplayzXx/Winotch/releases/download/v2.1.2/Winotch-v2.1.2-x64.zip"
        },
        {
          "name": "Source code (zip)",
          "size": 1234,
          "browser_download_url": "https://github.com/xXNeilplayzXx/Winotch/archive/refs/tags/v2.1.2.zip"
        }
      ]
    }
    """;

    [Fact]
    public void Parse_ExtractsTagVersionZipAndSize()
    {
        var info = UpdateService.Parse(LatestPayload);
        Assert.NotNull(info);
        Assert.Equal("v2.1.2", info!.Tag);
        Assert.Equal("2.1.2", info.Version);
        Assert.EndsWith("/Winotch-v2.1.2-x64.zip", info.ZipUrl);
        Assert.Equal(76095876, info.SizeBytes);
    }

    [Fact]
    public void Parse_IgnoresReleasesWithoutTheX64Zip()
    {
        var payload = LatestPayload.Replace("Winotch-v2.1.2-x64.zip", "Source code (zip)");
        Assert.Null(UpdateService.Parse(payload));
    }

    [Fact]
    public void Parse_ReturnsNullForGarbage()
    {
        Assert.Null(UpdateService.Parse("not json at all"));
        Assert.Null(UpdateService.Parse("{}"));
    }

    [Fact]
    public void IsNewer_ComparesAgainstTheRunningVersion()
    {
        var current = new Version(2, 1, 1, 0);
        var info = UpdateService.Parse(LatestPayload)!;

        Assert.True(info.IsNewerThan(current));

        var same = UpdateService.Parse(LatestPayload.Replace("v2.1.2", "v2.1.1"))!;
        Assert.False(same.IsNewerThan(current));
        Assert.True(same.IsNewerThan(new Version(2, 1, 0, 0)));
    }

    [Fact]
    public void SwapScript_WaitsForTheOldProcessAndRetriesTheCopy()
    {
        var script = UpdateService.BuildSwapScript(
            @"C:\Up\Winotch.exe", @"C:\Apps\Winotch.exe", 4242, @"C:\Up\v9.9.9.zip", @"C:\Up\update.log");

        // The old build must be released before the copy, or the swap silently
        // relaunches the previous version.
        Assert.Contains("WaitForExit", script);
        Assert.Contains("Get-Process -Id 4242", script);
        Assert.Contains(@"Copy-Item -Force -LiteralPath 'C:\Up\Winotch.exe'", script);
        Assert.Contains(@"-Destination 'C:\Apps\Winotch.exe'", script);
        Assert.Contains("Start-Sleep -Milliseconds 500", script);
        Assert.Contains(@"Start-Process -FilePath 'C:\Apps\Winotch.exe'", script);
        Assert.DoesNotContain("Start-Sleep -Seconds", script);
    }

    [Fact]
    public void SwapScript_CleansUpOnlyAfterASuccessfulSwap()
    {
        var script = UpdateService.BuildSwapScript(
            @"C:\Up\v9.9.9\Winotch.exe", @"C:\Apps\Winotch.exe", 7, @"C:\Up\v9.9.9.zip", @"C:\Up\update.log");

        Assert.Contains("if ($ok) { Write-Log 'swap succeeded'", script);
        Assert.Contains(@"Remove-Item -Force -Recurse -LiteralPath 'C:\Up\v9.9.9'", script);
        Assert.Contains(@"Remove-Item -Force -LiteralPath 'C:\Up\v9.9.9.zip'", script);
    }

    [Fact]
    public void SwapScript_EscapesApostrophesInPaths()
    {
        var script = UpdateService.BuildSwapScript(
            @"C:\O'Brien\Winotch.exe", @"C:\Apps\Winotch.exe", 1, null, @"C:\Up\update.log");

        Assert.Contains(@"'C:\O''Brien\Winotch.exe'", script);
    }

    [Fact]
    public void LooksLikeExecutable_RejectsNonPeContent()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            File.WriteAllText(path, "<html>not an update</html>");
            Assert.False(UpdateService.LooksLikeExecutable(path));

            File.WriteAllText(path, new string('x', 4096));
            Assert.False(UpdateService.LooksLikeExecutable(path));
        }
        finally { File.Delete(path); }

        Assert.False(UpdateService.LooksLikeExecutable(Path.Combine(Path.GetTempPath(), "winotch-missing-98831.exe")));
    }

    [Fact]
    public void LooksLikeExecutable_AcceptsAPeImage()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var bytes = new byte[4096];
            bytes[0] = (byte)'M';
            bytes[1] = (byte)'Z';
            File.WriteAllBytes(path, bytes);
            Assert.True(UpdateService.LooksLikeExecutable(path));
        }
        finally { File.Delete(path); }
    }
}
