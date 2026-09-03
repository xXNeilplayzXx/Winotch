namespace Winotch;

/// <summary>User settings, persisted as JSON under %LOCALAPPDATA%\Winotch.</summary>
public sealed record WinotchSettings
{
    public static WinotchSettings Defaults => new();

    public GeneralSettings General { get; init; } = new();
    public ToastSettings Toasts { get; init; } = new();
    public FeatureSettings Features { get; init; } = new();
    public WidgetSettings Widgets { get; init; } = new();

    public WinotchSettings Normalize() => this with
    {
        General = General ?? new GeneralSettings(),
        Toasts = Toasts is null ? new ToastSettings() : Toasts.Normalize(),
        Features = Features ?? new FeatureSettings(),
        Widgets = Widgets is null ? new WidgetSettings() : Widgets.Normalize(),
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
