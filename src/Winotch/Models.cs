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
public enum ThemeMode { Dark, Light, System, Color }

/// <summary>What drives the accent color: the built-in per-theme accent, a
/// custom hex, or the live Windows accent (DWM ColorizationColor).</summary>
public enum AccentMode { Default, Custom, FollowOs }

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
    public bool Glass { get; init; }
    public int Opacity { get; init; } = 65;
    public int Blur { get; init; } = 14;

    /// <summary>When true the pill uses Width/Height; otherwise the pill
    /// auto-sizes to its content (the rewrite's default behavior).</summary>
    public bool CustomSize { get; init; }
    public int Width { get; init; } = 330;
    public int Height { get; init; } = 44;
    public string SizePreset { get; init; } = "standard";

    public IdleContentMode IdleContent { get; init; } = IdleContentMode.Auto;
    public string CustomIdleText { get; init; } = "";
    public bool DragToMove { get; init; } = true;

    public DisplayMode Display { get; init; } = DisplayMode.Main;
    public string DisplayDeviceName { get; init; } = "";

    /// <summary>Drag-position offset of the pill from the monitor's top-center,
    /// in DIPs. Persisted so the notch remembers where the user left it.</summary>
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }

    public AppearanceSettings Normalize() => this with
    {
        AccentHex = TryParseHex(AccentHex) ?? "#50D6FF",
        Gradient = GradientSkin.IsKnown(Gradient) ? Gradient : GradientSkin.None,
        Opacity = Math.Clamp(Opacity, 10, 100),
        Blur = Math.Clamp(Blur, 0, 40),
        Width = Math.Clamp(Width, 150, 800),
        Height = Math.Clamp(Height, 30, 200),
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
        ("compact", "Compact", 260, 40),
        ("standard", "Standard", 330, 44),
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

    public static readonly string[] Known = { None, Sunset, Ocean, Aurora, Candy, Graphite };

    public static readonly Dictionary<string, string> Labels = new()
    {
        [None] = "None",
        [Sunset] = "Sunset",
        [Ocean] = "Ocean",
        [Aurora] = "Aurora",
        [Candy] = "Candy",
        [Graphite] = "Graphite",
    };

    public static bool IsKnown(string? id) =>
        !string.IsNullOrWhiteSpace(id) && Known.Contains(id);

    /// <summary>Three translucent stops (start / mid / end) plus an overall
    /// intensity multiplier, drawn as a diagonal wash over the island body.</summary>
    public static (byte R, byte G, byte B, double Alpha)[] Stops(string id) => id switch
    {
        Sunset => new[] { (255, 122, 89, 0.20), (239, 68, 68, 0.06), (168, 85, 247, 0.10) },
        Ocean => new[] { (14, 165, 233, 0.18), (37, 99, 235, 0.06), (6, 182, 212, 0.10) },
        Aurora => new[] { (16, 185, 129, 0.16), (37, 99, 235, 0.05), (139, 92, 246, 0.09) },
        Candy => new[] { (236, 72, 153, 0.18), (168, 85, 247, 0.06), (59, 130, 246, 0.09) },
        Graphite => new[] { (148, 163, 184, 0.12), (100, 116, 139, 0.05), (71, 85, 105, 0.10) },
        _ => Array.Empty<(byte, byte, byte, double)>(),
    };
}

public sealed record GeneralSettings
{
    public bool Use24HourClock { get; init; }
    public bool ShowDate { get; init; } = true;
    public bool StartWithWindows { get; init; }
    public NotchStyle Style { get; init; } = NotchStyle.Notch;
}

/// <summary>How the shell attaches to the screen: Notch hugs the top edge
/// (the screen clips the pill's top cap), Island floats below it.</summary>
public enum NotchStyle { Notch, Island }

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

    public WidgetSettings Normalize()
    {
        var enabled = (Enabled ?? [])
            .Where(Known.Contains)
            .Distinct()
            .ToList();
        if (enabled.Count == 0) enabled = new List<string>(DefaultEnabled);
        return this with
        {
            Enabled = enabled,
            Notes = Notes ?? "",
            Weather = Weather is null ? new WeatherWidgetConfig() : Weather,
            LauncherApps = LauncherApps ?? [],
            SystemMetrics = SystemMetrics is null ? new SystemMetricsConfig() : SystemMetrics,
        };
    }
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
