using System.Diagnostics;
using System.Text;

namespace Winotch;

/// <summary>Reads the connected Wi-Fi network name.
/// Windows: netsh wlan show interfaces
/// Linux: nmcli -t -f active,ssid dev wifi (or iwgetid as fallback)
/// Best-effort: returns null when there is no Wi-Fi, no connection, or the
/// tool is unavailable.</summary>
public static class WifiProbe
{
    public static string? ReadSsid()
    {
#if WINDOWS
        return ReadSsidWindows();
#else
        return ReadSsidLinux();
#endif
    }

#if WINDOWS
    private static string? ReadSsidWindows()
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using var process = Process.Start(psi);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000)) return null;

            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.Trim();
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var key = line[..colon].Trim();
                if (!string.Equals(key, "SSID", StringComparison.OrdinalIgnoreCase)) continue;
                var value = line[(colon + 1)..].Trim();
                return value.Length > 0 ? value : null;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
#endif

#if !WINDOWS
    private static string? ReadSsidLinux()
    {
        // Try nmcli first (NetworkManager)
        try
        {
            var psi = new ProcessStartInfo("nmcli", "-t -f active,ssid dev wifi")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(psi);
            if (process is not null)
            {
                var output = process.StandardOutput.ReadToEnd();
                if (process.WaitForExit(3000) && process.ExitCode == 0)
                {
                    foreach (var line in output.Split('\n'))
                    {
                        // Format: "yes:MyNetwork" or "no:"
                        if (line.StartsWith("yes:"))
                        {
                            var ssid = line[4..].Trim();
                            if (ssid.Length > 0) return ssid;
                        }
                    }
                }
            }
        }
        catch { }

        // Fallback: iwgetid
        try
        {
            var psi = new ProcessStartInfo("iwgetid", "-r")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(psi);
            if (process is not null)
            {
                var output = process.StandardOutput.ReadToEnd().Trim();
                if (process.WaitForExit(3000) && process.ExitCode == 0 && output.Length > 0)
                    return output;
            }
        }
        catch { }

        return null;
    }
#endif
}
