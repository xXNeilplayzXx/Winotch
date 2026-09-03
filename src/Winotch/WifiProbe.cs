using System.Diagnostics;
using System.Text;

namespace Winotch;

/// <summary>Reads the connected Wi-Fi network name via <c>netsh wlan show
/// interfaces</c>. Best-effort: returns null when there is no Wi-Fi, no
/// connection, or netsh is unavailable (wired-only machines).</summary>
public static class WifiProbe
{
    public static string? ReadSsid()
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
}
