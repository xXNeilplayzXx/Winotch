namespace Winotch.Platform;

/// <summary>Platform-specific theme data source. Returns raw values that
/// <see cref="Winotch.ThemeService"/> combines with user settings to produce
/// the resolved <see cref="Winotch.ShellTheme"/>.</summary>
public interface IThemeService
{
    bool? AppsUseLightTheme();
    Engine.Col? OsAccent();
    bool? WallpaperIsLight();
}
