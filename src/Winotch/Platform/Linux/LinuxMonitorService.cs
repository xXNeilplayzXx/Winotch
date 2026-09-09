using System.Diagnostics;
using System.Text.RegularExpressions;
using Winotch;

namespace Winotch.Platform.Linux;

/// <summary>Monitor service for Linux using xrandr (X11) or parsing /sys for basic info.</summary>
public sealed class LinuxMonitorService : IMonitorService
{
    public IReadOnlyList<MonitorSnapshot> GetAllMonitors()
    {
        var monitors = new List<MonitorSnapshot>();
        try
        {
            var output = RunCommand("xrandr", "--query");
            if (output == null) return monitors;

            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"^(\S+)\s+connected\s+(?:primary\s+)?(\d+)x(\d+)\+(-?\d+)\+(-?\d+)");
                if (match.Success)
                {
                    var name = match.Groups[1].Value;
                    var width = int.Parse(match.Groups[2].Value);
                    var height = int.Parse(match.Groups[3].Value);
                    var x = int.Parse(match.Groups[4].Value);
                    var y = int.Parse(match.Groups[5].Value);
                    var isPrimary = line.Contains("primary");

                    monitors.Add(new MonitorSnapshot(
                        DeviceName: name,
                        Bounds: new NativeRect(x, y, x + width, y + height),
                        WorkingArea: new NativeRect(x, y, x + width, y + height),
                        IsPrimary: isPrimary,
                        DpiScaleX: 1.0,
                        DpiScaleY: 1.0
                    ));
                }
            }
        }
        catch
        {
        }

        return monitors;
    }

    public (int X, int Y) GetCursorPosition()
    {
        try
        {
            var output = RunCommand("xdotool", "getmouselocation");
            if (output != null)
            {
                var match = Regex.Match(output, @"x:(\d+)\s+y:(\d+)");
                if (match.Success)
                {
                    return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
                }
            }
        }
        catch
        {
        }

        return (0, 0);
    }

    public (int X, int Y, int Width, int Height)? GetForegroundWindowRect()
    {
        try
        {
            var windowId = RunCommand("xdotool", "getactivewindow")?.Trim();
            if (windowId == null) return null;

            var geo = RunCommand("xdotool", "getwindowgeometry", "--shell", windowId);
            if (geo == null) return null;

            int x = 0, y = 0, width = 0, height = 0;
            foreach (var line in geo.Split('\n'))
            {
                if (line.StartsWith("X=")) x = int.Parse(line.Substring(2));
                else if (line.StartsWith("Y=")) y = int.Parse(line.Substring(2));
                else if (line.StartsWith("WIDTH=")) width = int.Parse(line.Substring(6));
                else if (line.StartsWith("HEIGHT=")) height = int.Parse(line.Substring(7));
            }

            return (x, y, width, height);
        }
        catch
        {
        }

        return null;
    }

    public MonitorSnapshot? GetMonitorContainingPoint(int x, int y)
    {
        var monitors = GetAllMonitors();
        foreach (var monitor in monitors)
        {
            if (x >= monitor.Bounds.Left && x < monitor.Bounds.Right &&
                y >= monitor.Bounds.Top && y < monitor.Bounds.Bottom)
                return monitor;
        }
        return monitors.Count > 0 ? monitors[0] : null;
    }

    private static string? RunCommand(string fileName, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process == null) return null;

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : null;
    }
}
