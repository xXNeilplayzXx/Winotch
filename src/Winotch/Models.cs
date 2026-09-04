using System.IO;

namespace Winotch;

/// <summary>User settings, persisted as JSON under %LOCALAPPDATA%\Winotch.</summary>
public sealed record WinotchSettings
{
    public static WinotchSettings Defaults => new();

    public GeneralSettings General { get; init; } = new();
    public AppearanceSettings Appearance { get; init; } = new();
    public ToastSettings Toasts { get; init; } = new();
    public FeatureSettings Features { get; init; } = new();
    public WidgetSettings Widgets { get; init; } = new();

    public WinotchSettings Normalize() => this with
    {
        General = General ?? new GeneralSettings(),
        Appearance = Appearance is null ? new AppearanceSettings() : Appearance.Normalize(),
        Toasts = Toasts is null ? new ToastSettings() : Toasts.Normalize(),
        Features = Features ?? new FeatureSettings(),
        Widgets = Widgets is null ? new WidgetSettings() : Widgets.Normalize(),
    };
}

/// <summary>How the shell is themed: a fixed palette, or one that follows the
/// Windows app theme live (System) or a custom accent (Color).</summary>
public enum ThemeMode { Dark, Light, System, Color, Background }

/// <summary>What drives the accent color: the built-in per-theme accent, a
/// custom hex, or the live Windows accent (DWM ColorizationColor).</summary>
public enum AccentMode { Default, Custom, FollowOs }

/// <summary>How the island surface is painted: Solid is opaque, Glass is
/// translucent with the backdrop blur (when enabled), Acrylic forces a heavy
/// blur with a soft accent-tinted frost.</summary>
public enum SurfaceMode { Solid, Glass, Acrylic }

/// <summary>Which monitor(s) own the notch. All spawns one island per screen.</summary>
public enum DisplayMode { Main, Named, All, FollowActive }

/// <summary>What the collapsed pill shows when nothing else is demanding the
/// strip (no media, no call, no focus timer). Auto keeps the current default
/// layout (LED + clock + battery); the rest are the legacy idle options.</summary>
public enum IdleContentMode { Auto, BrandClock, ClockOnly, Custom, DotOnly }

/// <summary>Look-and-feel settings ported from the original app: theme, accent,
/// glass/solid appearance with opacity + blur, background gradients, pill
/// size presets/sliders, idle content, drag-to-move and display targeting.
/// Defaults preserve the current rewrite's dark solid design.</summary>
public sealed record AppearanceSettings
{
    public ThemeMode Theme { get; init; } = ThemeMode.Dark;
    public AccentMode Accent { get; init; } = AccentMode.Default;
    public string AccentHex { get; init; } = "#50D6FF";
    public string Gradient { get; init; } = GradientSkin.None;
    public SurfaceMode Surface { get; init; } = SurfaceMode.Solid;
    public int Opacity { get; init; } = 65;
    public int Blur { get; init; } = 14;
    public bool BlurEnabled { get; init; } = true;

    /// <summary>UI typeface id (see <see cref="FontChoice"/>). The default
    /// follows the user-requested change away from the OS Segoe stack; pick
    /// another (or the original look) in Appearance settings. Also accepts
    /// "system:&lt;Family&gt;" for any installed font and "custom:&lt;path&gt;"
    /// for imported .ttf/.otf files.</summary>
    public string Font { get; init; } = FontChoice.Aptos;

    /// <summary>Paths of font files the user imported (Appearance → Font →
    /// Import). Resolved via <see cref="FontChoice"/>; files live under
    /// %LOCALAPPDATA%\Winotch\Fonts.</summary>
    public List<string> CustomFonts { get; init; } = new();

    /// <summary>When true the pill uses Width/Height; otherwise the pill
    /// auto-sizes to its content (the rewrite's default behavior).</summary>
    public bool CustomSize { get; init; }
    public int Width { get; init; } = 330;
    public int Height { get; init; } = 44;
    public string SizePreset { get; init; } = "standard";

    public IdleContentMode IdleContent { get; init; } = IdleContentMode.Auto;
    public string CustomIdleText { get; init; } = "";
    public bool DragToMove { get; init; } = true;

    /// <summary>In side mode (pill attached to the left/right edge), render
    /// the pill's text with upright letters stacked top-to-bottom instead of
    /// rotated 90° along the edge.</summary>
    public bool VerticalText { get; init; }

    public DisplayMode Display { get; init; } = DisplayMode.Main;
    public string DisplayDeviceName { get; init; } = "";

    /// <summary>Drag-position offset of the pill from the monitor's top-center,
    /// in DIPs. Persisted so the notch remembers where the user left it.</summary>
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }

    /// <summary>Legacy glass flag, kept so old settings files (and tests)
    /// that only wrote "Glass" still migrate onto the Surface enum.</summary>
    public bool Glass { get; init; }

    public bool IsGlass => Surface != SurfaceMode.Solid;

    public AppearanceSettings Normalize() => this with
    {
        AccentHex = TryParseHex(AccentHex) ?? "#50D6FF",
        Gradient = GradientSkin.IsKnown(Gradient) ? Gradient : GradientSkin.None,
        // Surface is authoritative; the legacy Glass flag migrates only when
        // no Surface was persisted (old settings files predate the enum).
        Surface = Enum.IsDefined(Surface)
            ? (Glass && Surface == SurfaceMode.Solid ? SurfaceMode.Glass : Surface)
            : (Glass ? SurfaceMode.Glass : SurfaceMode.Solid),
        Opacity = Math.Clamp(Opacity, 10, 100),
        Blur = Math.Clamp(Blur, 0, 40),
        Font = FontChoice.IsKnown(Font) ? Font : FontChoice.Aptos,
        CustomFonts = (CustomFonts ?? new List<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList(),
        Width = Math.Clamp(Width, 150, 800),
        Height = Math.Clamp(Height, 30, 200),
        OffsetX = Math.Clamp(OffsetX, -600, 600),
        OffsetY = Math.Clamp(OffsetY, 0, 400),
        CustomIdleText = CustomIdleText ?? "",
        DisplayDeviceName = DisplayDeviceName ?? "",
    };

    private static string? TryParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var trimmed = hex.Trim();
        if (trimmed[0] == '#') trimmed = trimmed[1..];
        return uint.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out _)
            && trimmed.Length is 6 or 8
            ? "#" + trimmed[..Math.Min(6, trimmed.Length)].ToUpperInvariant()
            : null;
    }
}

/// <summary>Named pill sizes (legacy presets) mapped onto the width slider range.</summary>
public static class SizePresets
{
    public static readonly (string Id, string Label, int Width, int Height)[] All =
    {
        ("compact", "Compact", 180, 40),
        ("standard", "Standard", 220, 45),
        ("wide", "Wide", 420, 48),
    };

    public static (string Id, string Label, int Width, int Height)? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        foreach (var preset in All)
        {
            if (string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase)) return preset;
        }
        return null;
    }

    public static (string Id, string Label, int Width, int Height) Closest(double width, double height)
    {
        (string Id, string Label, int Width, int Height) best = All[1];
        var bestDistance = double.MaxValue;
        foreach (var preset in All)
        {
            var distance = Math.Abs(preset.Width - width) + Math.Abs(preset.Height - height) * 3;
            if (distance < bestDistance)
            {
                best = preset;
                bestDistance = distance;
            }
        }
        return best;
    }
}

/// <summary>Background gradient skins (the legacy names, tuned to sit under
/// the current dark design). None disables the overlay.</summary>
public static class GradientSkin
{
    public const string None = "none";
    public const string Sunset = "sunset";
    public const string Ocean = "ocean";
    public const string Aurora = "aurora";
    public const string Candy = "candy";
    public const string Graphite = "graphite";
    public const string Peach = "peach";
    public const string Mint = "mint";
    public const string Berry = "berry";
    public const string Midnight = "midnight";
    public const string Forest = "forest";

    public static readonly string[] Known =
        { None, Sunset, Ocean, Aurora, Candy, Graphite, Peach, Mint, Berry, Midnight, Forest };

    public static readonly Dictionary<string, string> Labels = new()
    {
        [None] = "None",
        [Sunset] = "Sunset",
        [Ocean] = "Ocean",
        [Aurora] = "Aurora",
        [Candy] = "Candy",
        [Graphite] = "Graphite",
        [Peach] = "Peach",
        [Mint] = "Mint",
        [Berry] = "Berry",
        [Midnight] = "Midnight",
        [Forest] = "Forest",
    };

    public static bool IsKnown(string? id) =>
        !string.IsNullOrWhiteSpace(id) && Known.Contains(id);

    /// <summary>Three translucent stops (start / mid / end) plus an overall
    /// intensity multiplier, drawn as a diagonal wash over the island body.</summary>
    public static (byte R, byte G, byte B, double Alpha)[] Stops(string id) => id switch
    {
        Sunset => new[] { ((byte)255, (byte)122, (byte)89, 0.20), ((byte)239, (byte)68, (byte)68, 0.06), ((byte)168, (byte)85, (byte)247, 0.10) },
        Ocean => new[] { ((byte)14, (byte)165, (byte)233, 0.18), ((byte)37, (byte)99, (byte)235, 0.06), ((byte)6, (byte)182, (byte)212, 0.10) },
        Aurora => new[] { ((byte)16, (byte)185, (byte)129, 0.16), ((byte)37, (byte)99, (byte)235, 0.05), ((byte)139, (byte)92, (byte)246, 0.09) },
        Candy => new[] { ((byte)236, (byte)72, (byte)153, 0.18), ((byte)168, (byte)85, (byte)247, 0.06), ((byte)59, (byte)130, (byte)246, 0.09) },
        Graphite => new[] { ((byte)148, (byte)163, (byte)184, 0.12), ((byte)100, (byte)116, (byte)139, 0.05), ((byte)71, (byte)85, (byte)105, 0.10) },
        Peach => new[] { ((byte)255, (byte)183, (byte)77, 0.20), ((byte)255, (byte)107, (byte)107, 0.07), ((byte)236, (byte)72, (byte)153, 0.08) },
        Mint => new[] { ((byte)52, (byte)211, (byte)153, 0.16), ((byte)16, (byte)185, (byte)129, 0.06), ((byte)45, (byte)212, (byte)191, 0.10) },
        Berry => new[] { ((byte)168, (byte)85, (byte)247, 0.20), ((byte)236, (byte)72, (byte)153, 0.08), ((byte)99, (byte)102, (byte)241, 0.10) },
        Midnight => new[] { ((byte)51, (byte)65, (byte)92, 0.22), ((byte)30, (byte)41, (byte)59, 0.08), ((byte)71, (byte)85, (byte)105, 0.12) },
        Forest => new[] { ((byte)22, (byte)101, (byte)52, 0.18), ((byte)45, (byte)212, (byte)191, 0.05), ((byte)16, (byte)185, (byte)129, 0.10) },
        _ => Array.Empty<(byte, byte, byte, double)>(),
    };
}

/// <summary>Curated UI typefaces for the island and app text (icons, emoji
/// and the mono faces stay fixed). Ids are persisted in settings; the family
/// is the installed name handed to Skia and WPF.</summary>
public static class FontChoice
{
    public const string Aptos = "aptos";
    public const string SegoeUiVariable = "segoe-ui-variable";
    public const string SegoeUi = "segoe-ui";
    public const string Bahnschrift = "bahnschrift";

    public static readonly (string Id, string Label)[] Known =
    {
        (Aptos, "Aptos"),
        (SegoeUiVariable, "Segoe UI Variable"),
        (SegoeUi, "Segoe UI"),
        (Bahnschrift, "Bahnschrift"),
    };

    public const string SystemPrefix = "system:";
    public const string CustomPrefix = "custom:";

    public static string SystemId(string familyName) => SystemPrefix + familyName;
    public static string CustomId(string filePath) => CustomPrefix + filePath;

    /// <summary>Map a setting id to the installed family name. The Segoe UI
    /// Variable id resolves to the "Text" optical family the OS registers;
    /// "system:&lt;family&gt;" passes the family through; "custom:&lt;path&gt;"
    /// resolves the family from the imported font file.</summary>
    public static string FamilyOf(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "Aptos";
        if (id.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return id[SystemPrefix.Length..];
        }
        if (id.StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return CustomFamily(id[CustomPrefix.Length..]) ?? "Aptos";
        }
        return id switch
        {
            Aptos => "Aptos",
            SegoeUiVariable => "Segoe UI Variable Text",
            SegoeUi => "Segoe UI",
            Bahnschrift => "Bahnschrift",
            _ => "Aptos",
        };
    }

    /// <summary>The font file path for a custom font id, else null.</summary>
    public static string? CustomFile(string? id) =>
        id is not null && id.StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase)
            ? id[CustomPrefix.Length..]
            : null;

    public static bool IsKnown(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (Known.Any(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase))) return true;
        if (id.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return id.Length > SystemPrefix.Length;
        }
        if (id.StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var file = id[CustomPrefix.Length..];
            return !string.IsNullOrWhiteSpace(file) && File.Exists(file);
        }
        return false;
    }

    /// <summary>Family name embedded in an imported font file, or null when
    /// the file cannot be read (the caller falls back to the default).</summary>
    public static string? CustomFamily(string filePath)
    {
        try
        {
            using var face = SkiaSharp.SKTypeface.FromFile(filePath);
            return face is null || string.IsNullOrEmpty(face.FamilyName) ? null : face.FamilyName;
        }
        catch
        {
            return null;
        }
    }
}

public sealed record GeneralSettings
{
    public bool Use24HourClock { get; init; }
    public bool ShowDate { get; init; } = true;
    public bool ShowDateOnPill { get; init; }
    public ClockLayout Layout { get; init; } = ClockLayout.Standard;
    public bool StartWithWindows { get; init; }
    public NotchStyle Style { get; init; } = NotchStyle.Notch;
    public PillEdge Edge { get; init; } = PillEdge.Top;

    /// <summary>How the expanded panel unfolds: Vertical grows downward from
    /// the pill; Horizontal blooms outward from the pill's center with the
    /// height following the width.</summary>
    public ExpandDirection OpenDirection { get; init; } = ExpandDirection.Vertical;

    /// <summary>Look for a newer build on GitHub when the app starts.</summary>
    public bool AutoCheckUpdates { get; init; } = true;

    /// <summary>Release tag the user was last offered and dismissed, so the
    /// automatic check doesn't nag about the same version twice.</summary>
    public string LastPromptedUpdate { get; init; } = "";
}

/// <summary>How the expanded panel header and clock widget stack the date:
/// Standard puts the date off to the side, Stacked centers it under the time.</summary>
public enum ClockLayout { Standard, Stacked }

/// <summary>How the shell attaches to the screen: Notch hugs the top edge
/// (the screen clips the pill's top cap), Island floats below it.</summary>
public enum NotchStyle { Notch, Island }

/// <summary>Which edge the pill hangs from. Top keeps the horizontal pill;
/// Left / Right rotate it 90 degrees into a slim vertical capsule that hugs
/// that side of the screen.</summary>
public enum PillEdge { Top, Left, Right }

/// <summary>The character of the notch's expand / collapse animation.</summary>
public enum ExpandDirection { Vertical, Horizontal }

public sealed record ToastSettings
{
    public bool MediaToastsEnabled { get; init; } = true;
    public bool PriorityAlertsEnabled { get; init; } = true;
    public ToastDurationScale DurationScale { get; init; } = ToastDurationScale.Normal;

    public ToastSettings Normalize() =>
        Enum.IsDefined(DurationScale) ? this : this with { DurationScale = ToastDurationScale.Normal };
}

public sealed record FeatureSettings
{
    public bool ClipboardHistoryEnabled { get; init; } = true;
    public bool FocusTimerEnabled { get; init; } = true;
    public bool PriorityAlertsEnabled { get; init; } = true;
    public bool FollowActiveMonitor { get; init; } = true;

    // Which live states may appear as chips on the collapsed pill.
    public bool ShowCallsOnPill { get; init; } = true;
    public bool ShowMediaOnPill { get; init; } = true;
    public bool ShowFocusOnPill { get; init; } = true;
    public bool ShowBatteryOnPill { get; init; } = true;
}

/// <summary>Which widget cards the expanded panel shows, in display order.
/// Same set as the original webview app: clock, system stats, now playing,
/// notes, weather, app launcher, focus timer, clipboard.</summary>
public sealed record WidgetSettings
{
    public static readonly string[] Known = { "clock", "system", "media", "notes", "weather", "launcher", "focus", "clipboard" };
    public static readonly string[] DefaultEnabled = Known;

    public static readonly Dictionary<string, string> Labels = new()
    {
        ["clock"] = "Clock",
        ["system"] = "System stats",
        ["media"] = "Now playing",
        ["notes"] = "Notes",
        ["weather"] = "Weather",
        ["launcher"] = "App launcher",
        ["focus"] = "Focus timer",
        ["clipboard"] = "Clipboard",
    };

    public List<string> Enabled { get; init; } = new(DefaultEnabled);
    public string Notes { get; init; } = "";
    public WeatherWidgetConfig Weather { get; init; } = new();
    public List<LauncherApp> LauncherApps { get; init; } = new();
    public SystemMetricsConfig SystemMetrics { get; init; } = new();

    /// <summary>Per-card size of each widget, keyed by widget id. Widgets with
    /// no entry render at Standard — each card is sized from its own ⚙.</summary>
    public Dictionary<string, WidgetScale> Sizes { get; init; } = new();

    /// <summary>Legacy single card size for every widget (≤ 2.1.x builds).
    /// Kept so old settings files deserialize; Normalize migrates it into
    /// Sizes once, then it stays Standard.</summary>
    public WidgetScale Scale { get; init; } = WidgetScale.Standard;

    // Persisted focus-timer preset (minutes), editable from the focus card.
    public int FocusMinutes { get; init; } = 25;
    public int BreakMinutes { get; init; } = 5;
    public bool FocusAutoCycle { get; init; }

    /// <summary>The card size for a widget id; Standard when that widget has
    /// no explicit choice.</summary>
    public WidgetScale SizeFor(string id) =>
        Sizes is not null && Sizes.TryGetValue(id, out var scale) ? scale : WidgetScale.Standard;

    /// <summary>Copy with the given widget's card size set; passing Standard
    /// removes the entry so the card falls back to Standard. Returns this
    /// unchanged when the size already matches.</summary>
    public WidgetSettings WithSize(string id, WidgetScale scale)
    {
        if (SizeFor(id) == scale) return this;
        var sizes = new Dictionary<string, WidgetScale>(Sizes ?? new());
        if (scale == WidgetScale.Standard) sizes.Remove(id);
        else sizes[id] = scale;
        return this with { Sizes = sizes };
    }

    public WidgetSettings Normalize()
    {
        var enabled = (Enabled ?? [])
            .Where(Known.Contains)
            .Distinct()
            .ToList();
        if (enabled.Count == 0) enabled = new List<string>(DefaultEnabled);

        // Keep only known widget ids with defined scales.
        var sizes = new Dictionary<string, WidgetScale>();
        foreach (var (id, scale) in Sizes ?? new())
        {
            if (Known.Contains(id) && Enum.IsDefined(scale)) sizes[id] = scale;
        }
        // One-time migration: the pre-2.2 build sized every card from a single
        // Scale. Seed each enabled card from it and clear the legacy value.
        if (sizes.Count == 0 && Scale != WidgetScale.Standard)
        {
            foreach (var id in enabled) sizes[id] = Scale;
        }
        return this with
        {
            Enabled = enabled,
            Notes = Notes ?? "",
            Weather = Weather is null ? new WeatherWidgetConfig() : Weather,
            LauncherApps = LauncherApps ?? [],
            SystemMetrics = SystemMetrics is null ? new SystemMetricsConfig() : SystemMetrics,
            Sizes = sizes,
            Scale = WidgetScale.Standard,
            FocusMinutes = Math.Clamp(FocusMinutes, 1, 180),
            BreakMinutes = Math.Clamp(BreakMinutes, 1, 60),
        };
    }
}

/// <summary>How large widget cards render inside the expanded panel.</summary>
public enum WidgetScale { Compact, Standard, Large }

public static class WidgetScaleExtensions
{
    public static double Factor(this WidgetScale scale) => scale switch
    {
        WidgetScale.Compact => 0.85,
        WidgetScale.Large => 1.15,
        _ => 1.0,
    };

    public static string Label(this WidgetScale scale) => scale switch
    {
        WidgetScale.Compact => "Compact",
        WidgetScale.Large => "Large",
        _ => "Standard",
    };
}

/// <summary>Persisted weather widget location (open-meteo coordinates).</summary>
public sealed record WeatherWidgetConfig
{
    public string City { get; init; } = "";
    public double? Lat { get; init; }
    public double? Lon { get; init; }
    public bool HasLocation => Lat is not null && Lon is not null;
}

public sealed record LauncherApp(string Label, string Path);

/// <summary>Which live metrics the System stats card shows.</summary>
public sealed record SystemMetricsConfig
{
    public bool Cpu { get; init; } = true;
    public bool Memory { get; init; } = true;
    public bool Battery { get; init; } = true;
    public bool Network { get; init; } = true;
    public int Count => (Cpu ? 1 : 0) + (Memory ? 1 : 0) + (Battery ? 1 : 0) + (Network ? 1 : 0);
}

/// <summary>Live weather shown by the weather card (fetched, not persisted).</summary>
public sealed record WeatherSnapshot(string Emoji, string Label, double Temp, double Feels, double Max, double Min, string City);

/// <summary>One sample of the live system stats shown by the System card.</summary>
public sealed record SystemStatsSnapshot(double CpuPercent, double MemoryPercent, double NetUpKbps, double NetDownKbps);

public enum ToastDurationScale { Short, Normal, Long }

public static class ToastDurationScaleExtensions
{
    public static TimeSpan ApplyTo(this ToastDurationScale scale, TimeSpan duration) =>
        TimeSpan.FromMilliseconds(Math.Round(duration.TotalMilliseconds * scale.Multiplier(), MidpointRounding.AwayFromZero));

    public static double Multiplier(this ToastDurationScale scale) => scale switch
    {
        ToastDurationScale.Short => 0.75,
        ToastDurationScale.Normal => 1.0,
        ToastDurationScale.Long => 1.5,
        _ => 1.0,
    };
}
