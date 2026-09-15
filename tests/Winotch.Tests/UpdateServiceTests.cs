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

    /// <summary>Every Winotch build ships as a GitHub prerelease, and the
    /// /releases/latest endpoint hides prereleases. The list payload must
    /// therefore yield the newest build by version, or the app would never
    /// offer an update again.</summary>
    private const string ReleasesPayload = """
    [
      {
        "tag_name": "v2.3.4",
        "draft": false,
        "prerelease": true,
        "assets": [
          {
            "name": "Winotch-v2.3.4-x64.zip",
            "size": 86000000,
            "browser_download_url": "https://github.com/xXNeilplayzXx/Winotch/releases/download/v2.3.4/Winotch-v2.3.4-x64.zip"
          },
          {
            "name": "Winotch-v2.3.4-linux-x64.zip",
            "size": 40000000,
            "browser_download_url": "https://github.com/xXNeilplayzXx/Winotch/releases/download/v2.3.4/Winotch-v2.3.4-linux-x64.zip"
          }
        ]
      },
      {
        "tag_name": "v2.3.3",
        "draft": false,
        "prerelease": false,
        "assets": [
          {
            "name": "Winotch-v2.3.3-x64.zip",
            "size": 82000000,
            "browser_download_url": "https://github.com/xXNeilplayzXx/Winotch/releases/download/v2.3.3/Winotch-v2.3.3-x64.zip"
          }
        ]
      }
    ]
    """;

    [Fact]
    public void ParseLatest_PicksTheNewestPrerelease()
    {
        var info = UpdateService.ParseLatest(ReleasesPayload);
        Assert.NotNull(info);
        Assert.Equal("2.3.4", info!.Version);
        Assert.EndsWith("/Winotch-v2.3.4-x64.zip", info.ZipUrl);
        Assert.Equal(86000000, info.SizeBytes);
        Assert.True(info.IsNewerThan(new Version(2, 3, 3, 0)));
    }

    /// <summary>A draft the maintainer has not published yet, and a published
    /// tag with no Windows zip at all: neither may hide a usable build.</summary>
    private const string UnusableNewerReleasesPayload = """
    [
      {
        "tag_name": "v2.4.0",
        "draft": true,
        "prerelease": true,
        "assets": [
          {
            "name": "Winotch-v2.4.0-x64.zip",
            "browser_download_url": "https://github.com/xXNeilplayzXx/Winotch/releases/download/v2.4.0/Winotch-v2.4.0-x64.zip"
          }
        ]
      },
      {
        "tag_name": "v2.3.9-docs",
        "draft": false,
        "prerelease": true,
        "assets": [
          {
            "name": "Winotch-v2.3.9-linux-x64.zip",
            "browser_download_url": "https://github.com/xXNeilplayzXx/Winotch/releases/download/v2.3.9-docs/Winotch-v2.3.9-linux-x64.zip"
          }
        ]
      },
      {
        "tag_name": "v2.3.4",
        "draft": false,
        "prerelease": true,
        "assets": [
          {
            "name": "Winotch-v2.3.4-x64.zip",
            "browser_download_url": "https://github.com/xXNeilplayzXx/Winotch/releases/download/v2.3.4/Winotch-v2.3.4-x64.zip"
          }
        ]
      }
    ]
    """;

    [Fact]
    public void ParseLatest_SkipsUnusableReleasesInsteadOfGoingSilent()
    {
        var info = UpdateService.ParseLatest(UnusableNewerReleasesPayload);
        Assert.NotNull(info);
        Assert.Equal("2.3.4", info!.Version);
        Assert.EndsWith("/Winotch-v2.3.4-x64.zip", info.ZipUrl);
    }

    [Fact]
    public void ParseLatest_AcceptsASingleReleaseObject()
    {
        var info = UpdateService.ParseLatest(LatestPayload);
        Assert.NotNull(info);
        Assert.Equal("2.1.2", info!.Version);
    }

    [Fact]
    public void ParseLatest_ReturnsNullForGarbage()
    {
        Assert.Null(UpdateService.ParseLatest("not json at all"));
        Assert.Null(UpdateService.ParseLatest("[]"));
        Assert.Null(UpdateService.ParseLatest("{"));
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
