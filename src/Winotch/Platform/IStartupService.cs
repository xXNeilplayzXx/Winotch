namespace Winotch.Platform;

/// <summary>Abstracts auto-start on login.
/// Windows: Registry HKCU\Software\Microsoft\Windows\CurrentVersion\Run
/// Linux: XDG autostart .desktop file in ~/.config/autostart/</summary>
public interface IStartupService
{
    bool GetAutoStart();
    void SetAutoStart(bool enabled);
}
