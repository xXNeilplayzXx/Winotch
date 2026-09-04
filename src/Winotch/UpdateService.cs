using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Winotch;

/// <summary>A newer release parsed from the GitHub "latest release" API.</summary>
public sealed record UpdateInfo(string Tag, string Version, string ZipUrl, long SizeBytes)
{
    /// <summary>True when this release is newer than the running build.</summary>
    public bool IsNewerThan(Version current) =>
        Version.TryParse(Version, out var other) && other > current;
}

/// <summary>Auto-updater: asks GitHub Releases for the latest Winotch, compares
/// it to the running build, downloads the win-x64 zip, stages the exe, and
/// hands the swap to a hidden PowerShell one-liner that replaces the file once
/// this process exits and relaunches it. A private repo or missing network
/// simply yields "couldn't check" — the feature needs the repo to be public.</summary>
public sealed class UpdateService
{
    private const string LatestApi = "https://api.github.com/repos/xXNeilplayzXx/Winotch/releases/latest";

    private static readonly HttpClient Http = CreateClient();
    private static readonly Regex ZipAssetRegex = new(@"^Winotch-v\d+\.\d+\.\d+-x64\.zip$", RegexOptions.IgnoreCase);

    /// <summary>The running build's version (assembly version from the csproj
    /// Version, e.g. 2.1.1.0).</summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public static string CurrentVersionText => VersionText(CurrentVersion);

    private static string VersionText(Version v) => $"v{v.Major}.{v.Minor}.{v.Build}";

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Winotch/" + CurrentVersionText);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>Query the latest release. Null when unreachable, not found, or
    /// the release has no matching win-x64 zip asset.</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await Http.GetAsync(LatestApi, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Parse(json);
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
        catch (JsonException) { return null; }
    }

    /// <summary>Parse the GitHub "latest release" JSON payload. Kept static and
    /// pure so the tests exercise it without the network.</summary>
    public static UpdateInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("tag_name", out var tagElement)) return null;
        var tag = tagElement.GetString();
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var version = tag.TrimStart('v', 'V');
        if (!root.TryGetProperty("assets", out var assets)) return null;

        string? url = null;
        long size = 0;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (name is null || !ZipAssetRegex.IsMatch(name)) continue;
            url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0;
            break;
        }
        return url is null ? null : new UpdateInfo(tag, version, url, size);
    }

    /// <summary>Download the release zip to <paramref name="destFile"/>, with
    /// 0-100 progress reporting.</summary>
    public async Task DownloadAsync(string zipUrl, string destFile,
        IProgress<int>? progress = null, CancellationToken ct = default)
    {
        using var response = await Http.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;
        var dir = Path.GetDirectoryName(destFile);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = File.Create(destFile);
        var buffer = new byte[81920];
        long read = 0;
        while (true)
        {
            var n = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (n == 0) break;
            await target.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            read += n;
            if (total > 0) progress?.Report((int)Math.Min(100, read * 100 / total));
        }
        progress?.Report(100);
    }

    /// <summary>Extract the staged exe from the downloaded zip (the release
    /// zip carries Winotch.exe at its root) into <paramref name="stageDir"/>.</summary>
    public static string StageExe(string zipPath, string stageDir)
    {
        Directory.CreateDirectory(stageDir);
        ZipFile.ExtractToDirectory(zipPath, stageDir, overwriteFiles: true);
        var staged = Path.Combine(stageDir, "Winotch.exe");
        if (!File.Exists(staged)) throw new InvalidDataException("The update zip has no Winotch.exe at its root.");
        return staged;
    }

    /// <summary>Schedule the staged exe to replace the running one and start
    /// it: a hidden PowerShell process waits for this process to exit, copies
    /// the new file over the running exe, removes the download, and relaunches.
    /// Call this right before quitting the app.</summary>
    public static void ApplyAndRestart(string stagedExe, string? cleanupFile = null)
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe)) return;
        var cleanup = string.IsNullOrEmpty(cleanupFile)
            ? ""
            : $"Remove-Item -Force -LiteralPath '{cleanupFile}' -ErrorAction SilentlyContinue; ";
        var script = "Start-Sleep -Seconds 2; " +
            $"Copy-Item -Force -LiteralPath '{stagedExe}' -Destination '{currentExe}'; " +
            cleanup +
            $"Start-Process -FilePath '{currentExe}'";
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{script}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try
        {
            Process.Start(psi);
        }
        catch
        {
            // The swap could not be scheduled (no PowerShell?) — stay running.
        }
    }
}
