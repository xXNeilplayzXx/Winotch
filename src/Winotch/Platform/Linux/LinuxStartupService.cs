namespace Winotch.Platform.Linux;

/// <summary>Startup service using XDG autostart .desktop files.</summary>
public sealed class LinuxStartupService : IStartupService
{
    private const string DesktopFileName = "winotch.desktop";
    private const string DesktopFileContent = @"[Desktop Entry]
Type=Application
Name=Winotch
Comment=Dynamic Island for Linux
Exec={0}
Terminal=false
X-GNOME-Autostart-enabled=true
";

    private static string AutostartDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "autostart");

    private static string DesktopFilePath =>
        Path.Combine(AutostartDir, DesktopFileName);

    public bool GetAutoStart()
    {
        return File.Exists(DesktopFilePath);
    }

    public void SetAutoStart(bool enabled)
    {
        if (enabled)
        {
            // Ensure autostart directory exists
            Directory.CreateDirectory(AutostartDir);

            // Get executable path
            var execPath = GetExecutablePath();
            var content = string.Format(DesktopFileContent, execPath);
            File.WriteAllText(DesktopFilePath, content);
        }
        else
        {
            if (File.Exists(DesktopFilePath))
            {
                File.Delete(DesktopFilePath);
            }
        }
    }

    private static string GetExecutablePath()
    {
        // Use /proc/self/exe to get current executable path
        try
        {
            var link = File.ReadAllText("/proc/self/exe");
            if (!string.IsNullOrEmpty(link))
                return link;
        }
        catch
        {
            // Fall back to Assembly location
        }

        return System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "winotch";
    }
}
