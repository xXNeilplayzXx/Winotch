using System.Globalization;
using System.IO;
using SkiaSharp;
using Avalonia.Media;
using Winotch.Engine;
using Winotch.Platform;

namespace Winotch;

/// <summary>A fully-resolved island palette for one render. Resolved from the
/// settings each poll by <see cref="ThemeService"/>, so the shell's visual
/// tokens (panel, text, accent, …) re-skin live when the user or OS
/// changes the theme.</summary>
public sealed record ShellTheme(
    Col PanelTop, Col PanelBottom, Col Card, Col CardHover, Col Stroke,
    Col Text, Col Muted, Col Accent, Col Ok, Col Warn, Col Danger, Col Hairline,
    bool IsLight)
{
    public static ShellTheme Dark { get; } = new(
        Col.Rgba(0x1A, 0x1D, 0x24), Col.Rgba(0x10, 0x12, 0x17),
        Col.Rgba(0x1E, 0x22, 0x2A), Col.Rgba(0x26, 0x2B, 0x34),
        Col.Rgba(0x2E, 0x34, 0x40), Col.Rgba(0xF2, 0xF4, 0xF8),
        Col.Rgba(0x9C, 0xA5, 0xB4), Col.Rgba(0x50, 0xD6, 0xFF),
        Col.Rgba(0x32, 0xD7, 0x4B), Col.Rgba(0xFF, 0xB0, 0x4A),
        Col.Rgba(0xFF, 0x6B, 0x6B), Col.Rgba(0x25, 0x29, 0x31), false);

    public static ShellTheme Light { get; } = new(
        Col.Rgba(0xF3, 0xF5, 0xF9), Col.Rgba(0xE6, 0xEA, 0xF1),
        Col.Rgba(0xFF, 0xFF, 0xFF), Col.Rgba(0xE8, 0xEC, 0xF3),
        Col.Rgba(0xC9, 0xD1, 0xDD), Col.Rgba(0x14, 0x19, 0x22),
        Col.Rgba(0x5E, 0x6B, 0x7E), Col.Rgba(0x00, 0x78, 0xD4),
        Col.Rgba(0x0E, 0x8A, 0x5F), Col.Rgba(0xB4, 0x6A, 0x00),
        Col.Rgba(0xC4, 0x2B, 0x1C), Col.Rgba(0xD9, 0xDE, 0xE6), true);

    /// <summary>The dark palette with a custom accent substituted.</summary>
    public ShellTheme WithAccent(Col accent) => this with { Accent = accent };

    /// <summary>The palette with its panel/card surfaces tinted toward the
    /// accent — the "Colour" theme — while text/stroke stay readable.</summary>
    public ShellTheme WithAccentTint(Col accent) => this with
    {
        PanelTop = Col.Lerp(PanelTop, accent, 0.30),
        PanelBottom = Col.Lerp(PanelBottom, accent, 0.14),
        Card = Col.Lerp(Card, accent, 0.10),
        CardHover = Col.Lerp(CardHover, accent, 0.08),
        Hairline = Col.Lerp(Hairline, accent, 0.10),
    };

    public Color AvaloniaPanelTop => FromCol(PanelTop);
    public Color AvaloniaPanelBottom => FromCol(PanelBottom);
    public Color AvaloniaCard => FromCol(Card);
    public Color AvaloniaCardHover => FromCol(CardHover);
    public Color AvaloniaStroke => FromCol(Stroke);
    public Color AvaloniaText => FromCol(Text);
    public Color AvaloniaMuted => FromCol(Muted);
    public Color AvaloniaAccent => FromCol(Accent);
    public Color AvaloniaOk => FromCol(Ok);
    public Color AvaloniaWarn => FromCol(Warn);
    public Color AvaloniaDanger => FromCol(Danger);
    public Color AvaloniaHairline => FromCol(Hairline);

    private static Color FromCol(Col c) => c.ToAvalonia();
}

#if WINDOWS
/// <summary>Reads the two Windows theme sources: the app light/dark preference
/// (Personalize\AppsUseLightTheme) and the DWM accent (ColorizationColor).
/// Pure registry reads, cheap enough to poll.</summary>
public sealed class WindowsThemeSource : IThemeService
{
    public static readonly WindowsThemeSource Instance = new();

    private const string PersonalizePath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmPath = @"Software\Microsoft\Windows\DWM";

    public bool? AppsUseLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(PersonalizePath);
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int number ? number != 0 : null;
        }
        catch { return null; }
    }

    public Col? OsAccent()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(DwmPath);
            var value = key?.GetValue("ColorizationColor");
            if (value is not int raw) return null;
            var argb = unchecked((uint)raw);
            return Col.Rgba((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        }
        catch { return null; }
    }

    public bool? WallpaperIsLight()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            var path = key?.GetValue("WallPaper") as string;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

            using var stream = File.OpenRead(path);
            // Decode with Skia: the WPF imaging stack is not available in an
            // Avalonia-hosted process.
            using var bitmap = SKBitmap.Decode(stream);
            if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0) return null;

            // Sample a coarse grid: the average luminance is all this decides.
            const int grid = 32;
            var stepX = Math.Max(1, bitmap.Width / grid);
            var stepY = Math.Max(1, bitmap.Height / grid);
            double luminance = 0;
            double count = 0;
            for (var y = 0; y < bitmap.Height; y += stepY)
            {
                for (var x = 0; x < bitmap.Width; x += stepX)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.Alpha < 64) continue;
                    luminance += 0.2126 * pixel.Red + 0.7152 * pixel.Green + 0.0722 * pixel.Blue;
                    count++;
                }
            }
            return count == 0 ? null : luminance / count / 255.0 >= 0.5;
        }
        catch { return null; }
    }
}
#endif

/// <summary>A fake <see cref="IThemeService"/> for tests.</summary>
public sealed class FakeThemeSource : IThemeService
{
    public bool? Light { get; set; }
    public Col? Accent { get; set; }
    public bool? Wallpaper { get; set; }

    public bool? AppsUseLightTheme() => Light;
    public Col? OsAccent() => Accent;
    public bool? WallpaperIsLight() => Wallpaper;
}

/// <summary>Resolves <see cref="ShellTheme"/> from settings plus live OS
/// state. Call <see cref="Refresh"/> whenever settings change and on a slow
/// timer so System/Follow-Os themes track the OS live.</summary>
public sealed class ThemeService
{
    public const string DefaultAccentHex = "#50D6FF";
    public const string LightDefaultAccentHex = "#0078D4";

    private static readonly string[] Swatches =
    {
        DefaultAccentHex, "#0078D4", "#32D74B", "#FFB04A", "#FF6B6B", "#FF4FB2", "#A78BFA", "#8A8FA3",
    };

    private readonly IThemeService _os;

    public ThemeService() : this(CreatePlatformProvider()) { }

    public ThemeService(IThemeService os)
    {
        _os = os;
    }

    /// <summary>Create the platform-appropriate theme data provider.</summary>
    public static IThemeService CreatePlatformProvider()
    {
#if WINDOWS
        return WindowsThemeSource.Instance;
#else
        return new LinuxThemeSource();
#endif
    }

    public static IReadOnlyList<string> AccentSwatches => Swatches;

    public ShellTheme Current { get; private set; } = ShellTheme.Dark;

    public event EventHandler? ThemeChanged;

    public ShellTheme Refresh(WinotchSettings settings)
    {
        var appearance = settings.Appearance;
        var light = ResolveLight(appearance);
        var baseTheme = light ? ShellTheme.Light : ShellTheme.Dark;
        var accent = ResolveAccent(appearance, light);
        var resolved = baseTheme.WithAccent(accent);
        if (appearance.Theme == ThemeMode.Color)
        {
            resolved = resolved.WithAccentTint(accent);
        }
        if (resolved != Current)
        {
            Current = resolved;
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
        return Current;
    }

    public bool ResolveLight(AppearanceSettings appearance) => appearance.Theme switch
    {
        ThemeMode.Light => true,
        ThemeMode.System => _os.AppsUseLightTheme() ?? false,
        ThemeMode.Background => _os.WallpaperIsLight() ?? false,
        ThemeMode.Color => false, // colour keeps the dark base, like the legacy app
        _ => false,
    };

    public Col ResolveAccent(AppearanceSettings appearance, bool light)
    {
        var fallback = ParseHex(light ? LightDefaultAccentHex : DefaultAccentHex);
        return appearance.Accent switch
        {
            AccentMode.FollowOs => _os.OsAccent() ?? fallback,
            AccentMode.Custom => ParseHex(appearance.AccentHex),
            _ => fallback,
        };
    }

    /// <summary>DefaultAccentHex parsed once, without recursion (ParseHex used
    /// to call itself to build this fallback, which never terminated).</summary>
    private static readonly Col DefaultAccentFallback = ParseHexCore(DefaultAccentHex);

    public static Col ParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return DefaultAccentFallback;
        var trimmed = hex.Trim();
        if (trimmed[0] == '#') trimmed = trimmed[1..];
        if (trimmed.Length is 6 or 8 &&
            uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            // 6-digit RRGGBB, or 8-digit RRGGBBAA where the trailing alpha
            // byte is dropped (accents are always opaque).
            var shift = trimmed.Length == 8 ? 8 : 0;
            return Col.Rgba((byte)(value >> (16 + shift)), (byte)(value >> (8 + shift)), (byte)(value >> shift));
        }
        return DefaultAccentFallback;
    }

    private static Col ParseHexCore(string hex)
    {
        var trimmed = hex[0] == '#' ? hex[1..] : hex;
        uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value);
        return Col.Rgba((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }
}

/// <summary>Linux theme source: returns defaults (dark theme, blue accent).
/// The actual detection is done by LinuxThemeService (Phase 2) for the
/// platform layer; this adapter provides the minimal data ThemeService needs.</summary>
internal sealed class LinuxThemeSource : IThemeService
{
    public bool? AppsUseLightTheme() => false;
    public Col? OsAccent() => null; // Use default accent
    public bool? WallpaperIsLight() => null;
}
