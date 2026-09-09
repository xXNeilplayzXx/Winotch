using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Winotch.Platform.Linux;

/// <summary>Launcher service parsing .desktop files from standard locations.</summary>
public sealed class LinuxLauncherService : ILauncherService
{
    private static readonly string[] DesktopFileDirs = new[]
    {
        "/usr/share/applications",
        "/usr/local/share/applications",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/applications")
    };

    public IReadOnlyList<LaunchableApp> ScanApplications()
    {
        var apps = new List<LaunchableApp>();
        var seen = new HashSet<string>();

        foreach (var dir in DesktopFileDirs)
        {
            if (!Directory.Exists(dir))
                continue;

            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.desktop", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var app = ParseDesktopFile(file);
                        if (app != null && seen.Add(app.Name))
                        {
                            apps.Add(app);
                        }
                    }
                    catch
                    {
                        // Skip invalid desktop files
                    }
                }
            }
            catch
            {
                // Skip inaccessible directories
            }
        }

        return apps.OrderBy(a => a.Name).ToList();
    }

    private static LaunchableApp? ParseDesktopFile(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        string? name = null;
        string? exec = null;
        string? icon = null;
        bool noDisplay = false;
        bool hidden = false;

        bool inDesktopEntry = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed == "[Desktop Entry]")
            {
                inDesktopEntry = true;
                continue;
            }
            else if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                inDesktopEntry = false;
                continue;
            }

            if (!inDesktopEntry)
                continue;

            if (trimmed.StartsWith("Name="))
            {
                name = trimmed.Substring(5);
            }
            else if (trimmed.StartsWith("Exec="))
            {
                exec = trimmed.Substring(5);
                // Remove field codes like %f, %u, etc.
                exec = Regex.Replace(exec, @"%[fFuUdDnNickvm]", "").Trim();
            }
            else if (trimmed.StartsWith("Icon="))
            {
                icon = trimmed.Substring(5);
            }
            else if (trimmed == "NoDisplay=true")
            {
                noDisplay = true;
            }
            else if (trimmed == "Hidden=true")
            {
                hidden = true;
            }
            else if (trimmed == "Type=Application")
            {
                // Expected, continue
            }
        }

        // Skip if no name, no exec, or marked as hidden/no-display
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(exec) || noDisplay || hidden)
            return null;

        // Resolve icon path
        string? iconPath = null;
        if (!string.IsNullOrEmpty(icon))
        {
            iconPath = ResolveIconPath(icon);
        }

        return new LaunchableApp(
            Name: name,
            ExecutablePath: exec,
            IconPath: iconPath
        );
    }

    private static string? ResolveIconPath(string iconName)
    {
        // If it's already an absolute path, return it
        if (Path.IsPathRooted(iconName) && File.Exists(iconName))
            return iconName;

        // Search in standard icon directories
        var extensions = new[] { ".png", ".svg", ".xpm" };
        var iconDirs = new[]
        {
            "/usr/share/icons/hicolor/48x48/apps",
            "/usr/share/icons/hicolor/64x64/apps",
            "/usr/share/icons/hicolor/128x128/apps",
            "/usr/share/icons/hicolor/scalable/apps",
            "/usr/share/pixmaps"
        };

        foreach (var dir in iconDirs)
        {
            if (!Directory.Exists(dir))
                continue;

            // Try exact name first
            var fullPath = Path.Combine(dir, iconName);
            if (File.Exists(fullPath))
                return fullPath;

            // Try with extensions
            foreach (var ext in extensions)
            {
                fullPath = Path.Combine(dir, iconName + ext);
                if (File.Exists(fullPath))
                    return fullPath;
            }
        }

        return null;
    }
}
