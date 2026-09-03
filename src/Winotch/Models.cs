namespace Winotch;

/// <summary>User settings, persisted as JSON under %LOCALAPPDATA%\Winotch.</summary>
public sealed record WinotchSettings
{
    public static WinotchSettings Defaults => new();

    public GeneralSettings General { get; init; } = new();
    public ToastSettings Toasts { get; init; } = new();
    public FeatureSettings Features { get; init; } = new();

    public WinotchSettings Normalize() => this with
    {
        General = General ?? new GeneralSettings(),
        Toasts = Toasts is null ? new ToastSettings() : Toasts.Normalize(),
        Features = Features ?? new FeatureSettings(),
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
