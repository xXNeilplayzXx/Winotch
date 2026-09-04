using System.IO;

namespace Winotch;

/// <summary>Searches Windows for installed applications by resolving Start
/// Menu shortcuts (.lnk) to their target executables. The setup wizard uses
/// this to offer the apps actually installed on this PC for the launcher
/// widget instead of a hardcoded guess list. Store (UWP) apps do not expose
/// .lnk files and are therefore not listed; any scan failure yields an
/// empty list, never an exception.</summary>
public static class LauncherScanner
{
    /// <summary>Maximum entries returned — the setup picker stays snappy and
    /// anything rarer can still be added by hand from the widget settings.</summary>
    public const int MaxResults = 48;

    /// <summary>Resolves Start Menu shortcuts (all users + current user) into
    /// launcher entries: duplicates collapse by target path, unlikely entries
    /// (uninstallers, installers, documentation) are filtered out.</summary>
    public static List<LauncherApp> ScanInstalledApps()
    {
        var results = new List<LauncherApp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return results; // non-Windows or locked down

        var shell = Activator.CreateInstance(shellType);
        if (shell is null) return results;
        try
        {
            foreach (var file in ShortcutFiles())
            {
                try
                {
                    dynamic shortcut = ((dynamic)shell).CreateShortcut(file);
                    var target = (string)(shortcut.TargetPath ?? "");
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (target.Length == 0 || !IsLikelyApp(name, target)) continue;
                    if (!seen.Add(target)) continue;
                    results.Add(new LauncherApp(name, target));
                    if (results.Count >= MaxResults) break;
                }
                catch
                {
                    // Unreadable or non-file shortcut — skip it.
                }
            }
        }
        finally
        {
            if (shell is IDisposable disposable) disposable.Dispose();
        }
        return results;
    }

    /// <summary>Heuristic for "an app the user would actually launch": the
    /// target must be an existing .exe and neither the shortcut name nor the
    /// target may look like an uninstaller, installer, updater or doc entry.</summary>
    public static bool IsLikelyApp(string shortcutName, string targetPath)
    {
        if (!targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        if (!File.Exists(targetPath)) return false;
        foreach (var bad in new[] { "uninstall", "uninst", "setup", "installer", "update", "help", "readme", "license", "crash" })
        {
            if (shortcutName.Contains(bad, StringComparison.OrdinalIgnoreCase)) return false;
            if (Path.GetFileName(targetPath).Contains(bad, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static IEnumerable<string> ShortcutFiles()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
        };
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories);
            }
            catch
            {
                continue;
            }
            foreach (var file in files) yield return file;
        }
    }
}
