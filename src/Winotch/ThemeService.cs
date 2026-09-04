using System.Globalization;
using System.Windows.Media;
using Microsoft.Win32;
using Winotch.Engine;

namespace Winotch;

/// <summary>A fully-resolved island palette for one render. Resolved from the
/// settings each poll by <see cref="ThemeService"/>, so the shell's visual
/// tokens (panel, text, accent, …) re-skin live when the user or Windows
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

    public Color WpfPanelTop => FromCol(PanelTop);
    public Color WpfPanelBottom => FromCol(PanelBottom);
    public Color WpfCard => FromCol(Card);
    public Color WpfCardHover => FromCol(CardHover);
    public Color WpfStroke => FromCol(Stroke);
    public Color WpfText => FromCol(Text);
    public Color WpfMuted => FromCol(Muted);
    public Color WpfAccent => FromCol(Accent);
    public Color WpfOk => FromCol(Ok);
    public Color WpfWarn => FromCol(Warn);
    public Color WpfDanger => FromCol(Danger);
    public Color WpfHairline => FromCol(Hairline);

    private static Color FromCol(Col c) => Color.FromArgb(c.A, c.R, c.G, c.B);
}

/// <summary>Reads the two Windows theme sources the legacy app used: the app
/// light/dark preference (Personalize\AppsUseLightTheme) and the DWM accent
/// (ColorizationColor). Pure registry reads, cheap enough to poll.</summary>
public interface IWindowsThemeSource
{
    /// <summary>True when Windows uses light app theme; null when unreadable.</summary>
    bool? AppsUseLightTheme();

    /// <summary>The Windows accent color (DWM ColorizationColor), alpha-stripped.</summary>
    System.Windows.Media.Color? OsAccent();
}

public sealed class WindowsTheme : IWindowsThemeSource
{
    public static readonly WindowsTheme Instance = new();

    private const string PersonalizePath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmPath = @"Software\Microsoft\Windows\DWM";

    public bool? AppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizePath);
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int number ? number != 0 : null;
        }
        catch
        {
            return null;
        }
    }

    public System.Windows.Media.Color? OsAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(DwmPath);
            var value = key?.GetValue("ColorizationColor");
            if (value is not int raw) return null;
            var argb = unchecked((uint)raw);
            return System.Windows.Media.Color.FromArgb(255, (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>A fake <see cref="IWindowsThemeSource"/> for tests.</summary>
public sealed class FakeWindowsThemeSource : IWindowsThemeSource
{
    public bool? Light { get; set; }
    public System.Windows.Media.Color? Accent { get; set; }

    public bool? AppsUseLightTheme() => Light;
    public System.Windows.Media.Color? OsAccent() => Accent;
}

/// <summary>Resolves <see cref="ShellTheme"/> from settings plus live Windows
/// state. Call <see cref="Refresh"/> whenever settings change and on a slow
/// timer so System/Folow-Os themes track Windows live.</summary>
public sealed class ThemeService
{
    public const string DefaultAccentHex = "#50D6FF";
    public const string LightDefaultAccentHex = "#0078D4";

    private static readonly string[] Swatches =
    {
        DefaultAccentHex, "#0078D4", "#32D74B", "#FFB04A", "#FF6B6B", "#FF4FB2", "#A78BFA", "#8A8FA3",
    };

    private readonly IWindowsThemeSource _os;

    public ThemeService(IWindowsThemeSource? os = null)
    {
        _os = os ?? WindowsTheme.Instance;
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
        ThemeMode.Color => false, // color keeps the dark base, like the legacy app
        _ => false,
    };

    public Col ResolveAccent(AppearanceSettings appearance, bool light)
    {
        var fallback = ParseHex(light ? LightDefaultAccentHex : DefaultAccentHex);
        return appearance.Accent switch
        {
            AccentMode.FollowOs => (_os.OsAccent() is { } os ? (Col?)ParseWpf(os) : null) ?? fallback,
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

    private static Col ParseWpf(Color color) => Col.Rgba(color.R, color.G, color.B);
}