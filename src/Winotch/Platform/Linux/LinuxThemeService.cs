using System.Diagnostics;
using Winotch.Engine;

namespace Winotch.Platform.Linux;

/// <summary>Theme detection using xdg-desktop-portal and gsettings fallback.</summary>
public sealed class LinuxThemeService : IThemeService
{
    public bool? AppsUseLightTheme()
    {
        // Try xdg-desktop-portal Settings via busctl
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "busctl",
                ArgumentList = { "--user", "call", "org.freedesktop.portal.Desktop",
                    "/org/freedesktop/portal/desktop", "org.freedesktop.portal.Settings",
                    "Read", "s", "org.freedesktop.appearance", "s", "color-scheme" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process == null) return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) return CheckGnomeTheme();

            // Parse: output contains "u 1" (dark) or "u 2" (light)
            if (output.Contains("u 1")) return false; // dark
            if (output.Contains("u 2")) return true;  // light
            return null;
        }
        catch
        {
            return CheckGnomeTheme();
        }
    }

    private static bool? CheckGnomeTheme()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "gsettings",
                ArgumentList = { "get", "org.gnome.desktop.interface", "gtk-theme" },
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process == null) return null;
            var output = process.StandardOutput.ReadToEnd().Trim().Trim('\'').ToLower();
            process.WaitForExit();
            return !output.Contains("dark");
        }
        catch
        {
            return null;
        }
    }

    public Col? OsAccent()
    {
        // Linux doesn't have a standard accent color API via simple CLI.
        // Return null to use the default accent.
        return null;
    }

    public bool? WallpaperIsLight()
    {
        // Would need to analyze wallpaper image
        return null;
    }
}
