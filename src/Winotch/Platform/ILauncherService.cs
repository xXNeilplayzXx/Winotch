namespace Winotch.Platform;

/// <summary>Represents a launchable application.</summary>
public sealed record LaunchableApp(
    string Name,
    string ExecutablePath,
    string? IconPath = null);

/// <summary>Abstracts application launcher scanning.
/// Windows: Start Menu .lnk shortcuts via WScript.Shell
/// Linux: .desktop files in /usr/share/applications/ and ~/.local/share/applications/</summary>
public interface ILauncherService
{
    IReadOnlyList<LaunchableApp> ScanApplications();
}
