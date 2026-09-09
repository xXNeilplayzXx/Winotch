using System.Diagnostics;

namespace Winotch.Platform.Linux;

/// <summary>Volume service using PulseAudio (pactl) or PipeWire.</summary>
public sealed class LinuxSystemVolumeService : ISystemVolumeService
{
    public float Level01
    {
        get
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "pactl",
                    ArgumentList = { "get-sink-volume", "@DEFAULT_SINK@" },
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return 0.5f;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // Parse output like "Volume: front-left: 32768 /  50% / -18.06 dB"
                var match = System.Text.RegularExpressions.Regex.Match(output, @"(\d+)%");
                if (match.Success)
                {
                    return int.Parse(match.Groups[1].Value) / 100f;
                }
            }
            catch
            {
                // pactl not available or failed
            }

            return 0.5f; // Default
        }
    }

    public bool IsMuted
    {
        get
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "pactl",
                    ArgumentList = { "get-sink-mute", "@DEFAULT_SINK@" },
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                var output = process.StandardOutput.ReadToEnd().ToLower();
                process.WaitForExit();

                return output.Contains("yes");
            }
            catch
            {
                return false;
            }
        }
    }

    public bool IsAvailable => true; // Assume available if pactl exists

    public void SetLevel(float level01)
    {
        try
        {
            var percent = (int)(level01 * 100);
            var psi = new ProcessStartInfo
            {
                FileName = "pactl",
                ArgumentList = { "set-sink-volume", "@DEFAULT_SINK@", $"{percent}%" },
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            process?.WaitForExit();
        }
        catch
        {
            // pactl failed
        }
    }

    public void SetMuted(bool muted)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pactl",
                ArgumentList = { "set-sink-mute", "@DEFAULT_SINK@", muted ? "1" : "0" },
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            process?.WaitForExit();
        }
        catch
        {
            // pactl failed
        }
    }
}
