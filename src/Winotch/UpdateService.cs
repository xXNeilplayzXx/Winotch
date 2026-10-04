using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Winotch;

/// <summary>A newer release parsed from the GitHub "latest release" API.</summary>
public sealed record UpdateInfo(string Tag, string Version, string ZipUrl, long SizeBytes)
{
    /// <summary>True when this release is newer than the running build.</summary>
    public bool IsNewerThan(System.Version current) =>
        System.Version.TryParse(Version, out var other) && other > current;
}

/// <summary>Auto-updater: asks GitHub Releases for the latest Winotch, compares
/// it to the running build, downloads the win-x64 zip, stages the exe, and
/// hands the swap to a hidden PowerShell helper that waits for this process to
/// exit, copies the new build over the running exe, and relaunches it. A
/// private repo or missing network simply yields "couldn't check" — the feature
/// needs the repo to be public.</summary>
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
        try
        {
            return TryParse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static UpdateInfo? TryParse(string json)
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
    /// 0-100 progress reporting. When <paramref name="expectedBytes"/> is known
    /// (the release asset size) a truncated download is rejected, so a partial
    /// zip can never be staged as an update.</summary>
    public async Task DownloadAsync(string zipUrl, string destFile, long expectedBytes = 0,
        IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(destFile);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        try
        {
            using var response = await Http.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            var expected = expectedBytes > 0 ? expectedBytes : total;

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
            await target.FlushAsync(ct).ConfigureAwait(false);

            if (expected > 0 && read != expected)
                throw new InvalidDataException($"The update download was incomplete ({read} of {expected} bytes).");

            progress?.Report(100);
        }
        catch
        {
            TryDeleteFile(destFile);
            throw;
        }
    }

    /// <summary>Extract the staged exe from the downloaded zip (the release
    /// zip carries Winotch.exe at its root) into <paramref name="stageDir"/>.</summary>
    public static string StageExe(string zipPath, string stageDir)
    {
        // Start from a clean folder so leftover bytes from an earlier attempt
        // can never be mistaken for the new build.
        if (Directory.Exists(stageDir)) Directory.Delete(stageDir, recursive: true);
        Directory.CreateDirectory(stageDir);
        ZipFile.ExtractToDirectory(zipPath, stageDir, overwriteFiles: true);

        var staged = Path.Combine(stageDir, "Winotch.exe");
        if (!File.Exists(staged) || !LooksLikeExecutable(staged))
            throw new InvalidDataException("The update zip does not contain a valid Winotch.exe.");
        return staged;
    }

    /// <summary>Cheap sanity check that a file is a real Windows executable: it
    /// must be a plausible size and start with the PE "MZ" signature, so an
    /// HTML error page or a truncated download is never staged.</summary>
    internal static bool LooksLikeExecutable(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < 1024) return false;
            var header = new byte[2];
            return stream.Read(header, 0, 2) == 2 && header[0] == (byte)'M' && header[1] == (byte)'Z';
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Schedule the staged exe to replace the running one and start
    /// it, then return true once the helper process is running. Call this right
    /// before quitting the app. Returns false if the helper could not start —
    /// in that case the app should stay running rather than quit into nothing.
    ///
    /// The helper waits for this process to actually exit before copying (the
    /// running image is locked until then) and retries the copy, so a slow
    /// shutdown can no longer leave the old build in place. The previous
    /// implementation used a fixed two-second sleep: when shutdown took longer
    /// the copy failed, the download was still deleted, and the app relaunched
    /// the *old* exe — i.e. the update looked like it succeeded but changed
    /// nothing.</summary>
    public static bool ApplyAndRestart(string stagedExe, string? cleanupFile = null)
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe)) return false;

        var logPath = UpdateLogPath();
        var script = BuildSwapScript(stagedExe, currentExe, Environment.ProcessId, cleanupFile, logPath);
        return StartHiddenPowerShell(script, logPath);
    }

    /// <summary>Schedule a fresh launch of <paramref name="executable"/> once
    /// this process has exited. Starting the new instance first makes it lose
    /// the single-instance mutex race and quit immediately, so the relaunch
    /// waits for our PID to disappear.</summary>
    public static bool RelaunchAfterExit(string executable)
    {
        if (string.IsNullOrEmpty(executable)) return false;

        var directory = Path.GetDirectoryName(executable) ?? "";
        var logPath = UpdateLogPath();
        var script = $$"""
            $ErrorActionPreference = 'Continue'
            try { $p = Get-Process -Id {{Environment.ProcessId}} -ErrorAction SilentlyContinue; if ($p) { [void]$p.WaitForExit(60000) } } catch {}
            Start-Process -FilePath {{Quote(executable)}} -WorkingDirectory {{Quote(directory)}}
            """;
        return StartHiddenPowerShell(script, logPath);
    }

    /// <summary>Build the PowerShell helper that performs a safe in-place swap.
    /// Kept pure (no process launch) so the wait/retry contract is testable.</summary>
    internal static string BuildSwapScript(string stagedExe, string currentExe, int processId,
        string? cleanupFile, string logPath)
    {
        var directory = Path.GetDirectoryName(currentExe) ?? "";
        var stageDir = Path.GetDirectoryName(stagedExe) ?? "";

        var cleanup = new StringBuilder();
        if (!string.IsNullOrEmpty(stageDir))
            cleanup.Append($"Remove-Item -Force -Recurse -LiteralPath {Quote(stageDir)} -ErrorAction SilentlyContinue; ");
        if (!string.IsNullOrEmpty(cleanupFile))
            cleanup.Append($"Remove-Item -Force -LiteralPath {Quote(cleanupFile)} -ErrorAction SilentlyContinue; ");

        return $$"""
            $ErrorActionPreference = 'Stop'
            function Write-Log($m) { try { Add-Content -LiteralPath {{Quote(logPath)}} -Value ((Get-Date -Format s) + ' ' + $m) -ErrorAction SilentlyContinue } catch {} }
            Write-Log 'update helper started'
            try { $p = Get-Process -Id {{processId}} -ErrorAction SilentlyContinue; if ($p) { [void]$p.WaitForExit(60000) } } catch {}
            $ok = $false
            for ($i = 0; $i -lt 120 -and -not $ok; $i++) {
                try {
                    Copy-Item -Force -LiteralPath {{Quote(stagedExe)}} -Destination {{Quote(currentExe)}} -ErrorAction Stop
                    $ok = $true
                } catch {
                    Write-Log ('copy attempt ' + $i + ' failed: ' + $_.Exception.Message)
                    Start-Sleep -Milliseconds 500
                }
            }
            if ($ok) { Write-Log 'swap succeeded'; {{cleanup}} } else { Write-Log 'swap failed after retries' }
            Start-Process -FilePath {{Quote(currentExe)}} -WorkingDirectory {{Quote(directory)}}
            Write-Log 'relaunched'
            """;
    }

    private static string UpdateLogPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Winotch", "Updates", "update.log");

    private static bool StartHiddenPowerShell(string script, string logPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        // Base64 of UTF-16 handles all quoting for us, so paths with spaces or
        // apostrophes can't corrupt the script.
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try
        {
            Process.Start(psi);
            return true;
        }
        catch
        {
            // No PowerShell, or it refused to start — the caller keeps running.
            return false;
        }
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort — a stale download is harmless.
        }
    }
}
