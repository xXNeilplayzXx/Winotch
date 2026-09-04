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
}
