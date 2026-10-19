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
    public Dictionary<string, MonitorProfile> MonitorProfiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<InboxNotification> NotificationInbox { get; init; } = new();
    public bool WindowsNotificationInboxEnabled { get; init; }
    public List<string> DismissedWindowsNotificationIds { get; init; } = new();

    public WinotchSettings Normalize() => this with
    {
        General = General is null ? new GeneralSettings() : General.Normalize(),
        Appearance = Appearance is null ? new AppearanceSettings() : Appearance.Normalize(),
        Toasts = Toasts is null ? new ToastSettings() : Toasts.Normalize(),
        Features = Features ?? new FeatureSettings(),
        Widgets = Widgets is null ? new WidgetSettings() : Widgets.Normalize(),
        MonitorProfiles = MonitorProfiles is null
            ? new Dictionary<string, MonitorProfile>(StringComparer.OrdinalIgnoreCase)
            : MonitorProfiles.Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value.Normalize(), StringComparer.OrdinalIgnoreCase),
        NotificationInbox = NotificationInboxStore.Normalize(NotificationInbox),
        DismissedWindowsNotificationIds = (DismissedWindowsNotificationIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal).TakeLast(2000).ToList(),
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

/// <summary>Which skin the notch wears. Both layouts are the classic pill at the
/// same size with the same hover panel — Classic paints the flat palette surface
/// and Glance paints the high-gloss glass treatment over it.</summary>
public enum ShellLayoutMode { Classic, Glance }

/// <summary>Look-and-feel settings ported from the original app: theme, accent,
/// glass/solid appearance with opacity + blur, background gradients, pill
/// size presets/sliders, idle content and display targeting.
/// Defaults preserve the current rewrite's dark solid design.</summary>
public sealed record AppearanceSettings
{
    public ThemeMode Theme { get; init; } = ThemeMode.Dark;
    public bool UseMonitorProfiles { get; init; }
    public AccentMode Accent { get; init; } = AccentMode.Default;
    public string AccentHex { get; init; } = "#50D6FF";
    public string Gradient { get; init; } = GradientSkin.None;
    public SurfaceMode Surface { get; init; } = SurfaceMode.Solid;

    /// <summary>The high-gloss glass treatment: the milled midnight capsule with
    /// its blue glow and halo, and the framed slate cards. Independent of
    /// <see cref="Surface"/> — the surface decides how translucent the shell is,
    /// this decides whether it is painted as glass or as its flat palette.</summary>
    public bool GlossyGlass { get; init; }

    /// <summary>Which skin the notch wears — the classic flat palette or the
    /// high-gloss glass. Paint only; the geometry is shared by both.</summary>
    public ShellLayoutMode ShellLayout { get; init; } = ShellLayoutMode.Classic;

    /// <summary>True when the notch paints the high-gloss material. Glance is
    /// that skin by definition; the toggle also lets a Classic notch wear it.</summary>
    public bool UsesGlossySkin => GlossyGlass || ShellLayout == ShellLayoutMode.Glance;
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
        ShellLayout = Enum.IsDefined(ShellLayout) ? ShellLayout : ShellLayoutMode.Classic,
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

    /// <summary>Settings sections the user left open. The settings window folds
    /// every section away by default and reopens the ones named here, so it
    /// comes back the way it was left.</summary>
    public List<string> OpenSettingsSections { get; init; } = new();

    /// <summary>Section ids that may be persisted — an id from a hand-edited
    /// settings file that isn't one of these is simply dropped.</summary>
    public static readonly string[] KnownSections =
    {
        "general", "appearance", "size", "display", "pill", "alerts", "widgets",
    };

    public GeneralSettings Normalize() => this with
    {
        OpenSettingsSections = (OpenSettingsSections ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim().ToLowerInvariant())
            .Where(KnownSections.Contains)
            .Distinct()
            .ToList(),
    };
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

/// <summary>Pure layout math for the expanded panel's two-column mode
/// (Horizontal open direction): visible cards in persisted order split into
/// columns balanced by total card height, order preserved within each
/// column so the list still reads left-to-right, top-to-bottom.</summary>
public static class PanelLayout
{
    /// <summary>A card the panel will actually draw: (id, height ≥ 0).</summary>
    public readonly record struct PanelCard(string Id, double Height);

    /// <summary>Unscaled height of the empty clipboard card: text offset +
    /// wrapped hint lines + bottom pad, mirroring DrawClipboardWidget's empty
    /// state line for line. The card's box scales this exactly once by the
    /// widget's size factor; applying the factor here as well made the empty
    /// card scale by f², so it was cramped and clipping at Compact (0.72×) and
    /// oversized at Large (1.32×).</summary>
    public static double ClipboardEmptyCardHeight(int hintLineCount) =>
        44 + Math.Max(0, hintLineCount) * 15 + 12;

    /// <summary>Split visible cards into at most <paramref name="columns"/>
    /// columns whose total heights stay balanced: the boundary index is the
    /// one whose left/right height sums are closest to equal, so a tall card
    /// right after the midpoint moves to the second column instead of
    /// overshooting it. Order is preserved throughout.</summary>
    public static List<List<PanelCard>> SplitColumns(IEnumerable<PanelCard> cards, int columns = 2)
    {
        var visible = cards.Where(c => c.Height > 0).ToList();
        var result = Enumerable.Range(0, Math.Max(1, columns)).Select(_ => new List<PanelCard>()).ToList();
        if (visible.Count == 0) return result;
        if (columns <= 1 || visible.Count == 1)
        {
            result[0].AddRange(visible);
            return result;
        }
        var total = visible.Sum(c => c.Height);
        var bestIndex = 1;
        var bestDiff = double.MaxValue;
        double leftSum = 0;
        for (var k = 1; k < visible.Count; k++)
        {
            leftSum += visible[k - 1].Height;
            var diff = Math.Abs(total - 2 * leftSum);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestIndex = k;
            }
        }
        result[0].AddRange(visible.Take(bestIndex));
        result[1].AddRange(visible.Skip(bestIndex));
        return result;
    }
}

/// <summary>Greedy word-wrap for card text: splits a string into lines that
/// each measure within a budget. Pure and unit-testable — the caller supplies
/// the measure callback (TextKit passes real Skia measurement), so no Skia
/// types are needed here.</summary>
public static class TextLayout
{
    /// <summary>Wrap <paramref name="text"/> so no line exceeds
    /// <paramref name="maxWidth"/> (same unit as the measure callback).
    /// Words are never split mid-word: a single word longer than the budget
    /// gets a line of its own. Runs of spaces collapse; empty or
    /// whitespace-only input yields no lines; a non-positive budget returns
    /// the text as a single line rather than mangling it.</summary>
    public static List<string> Wrap(string text, float maxWidth, Func<string, float> measure)
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return lines;
        if (maxWidth <= 0) return [text];
        var current = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (measure(candidate) <= maxWidth || current.Length == 0)
            {
                current = candidate;
                continue;
            }
            lines.Add(current);
            current = word;
        }
        if (current.Length > 0) lines.Add(current);
        return lines;
    }
}

public sealed record ToastSettings
{
    public bool MediaToastsEnabled { get; init; } = true;
    public bool PriorityAlertsEnabled { get; init; } = true;
    /// <summary>Toast once per rain episode when precipitation starts within
    /// the next 30 minutes at the weather location.</summary>
    public bool RainAlertsEnabled { get; init; } = true;
    public ToastDurationScale DurationScale { get; init; } = ToastDurationScale.Normal;

    public ToastSettings Normalize() =>
        Enum.IsDefined(DurationScale) ? this : this with { DurationScale = ToastDurationScale.Normal };
}

/// <summary>Decides when the rain alert should fire: precipitation beginning
/// inside the next 30 minutes at the weather location, once per episode.
/// Pure and unit-testable — the manager feeds it the API's 15-minute buckets
/// (precipitation probability in %, mm per bucket) plus the previous alert
/// key, and gets the episode identity to toast (or null for silence).
///
/// An "episode" is the contiguous upcoming rainy stretch: when it starts and
/// everything before it stays dry, the evaluator names it by when the rain
/// begins ("in 25 min"). Re-alerting only when the dry gap between episodes
/// is long enough (&gt;= 30 min) keeps one shower from nagging repeatedly.</summary>
public static class RainAlertEvaluator
{
    /// <summary>Minimum probability (0-100) for a bucket to count as rain.</summary>
    public const double ProbabilityThreshold = 40;

    /// <summary>Dry minutes required between episodes before a new alert may
    /// fire; shorter gaps count as the same shower.</summary>
    public const int ReAlertGapMinutes = 30;

    /// <summary>Evaluates the bucket arrays (times as unix seconds UTC) and
    /// returns the alert key to fire now, or null to stay quiet. The key
    /// encodes the episode's start bucket so the caller can dedupe.
    ///
    /// Logic: if the bucket containing "now" is already rainy, stay quiet
    /// (that is rain in progress, not rain starting). Otherwise the first
    /// rainy bucket that starts after now is the episode start — dry buckets
    /// before it are the expected lead-in. Fire only when that start lies
    /// within 30 minutes.</summary>
    public static string? Evaluate(
        long[] times, double[] probability, double[] mm, long nowUnix, string? lastKey)
    {
        // The bucket containing "now" (may be absent when now sits in a gap).
        for (var i = 0; i < times.Length; i++)
        {
            if (times[i] <= nowUnix && nowUnix < times[i] + 900 &&
                IsRainy(probability[i], mm[i]))
            {
                return null; // already raining
            }
            if (times[i] > nowUnix) break;
        }

        // First rainy bucket that starts after now = the episode start.
        for (var i = 0; i < times.Length; i++)
        {
            if (times[i] <= nowUnix) continue; // past or current bucket
            if (!IsRainy(probability[i], mm[i])) continue;
            if (times[i] - nowUnix > 30 * 60) return null; // first rain is too far out
            var key = $"rain-{times[i]}";
            // Once per event: skip if the previous alert names the same
            // episode start, or a recent one began shortly before it.
            if (lastKey == key) return null;
            if (lastKey is { } prev && long.TryParse(prev.AsSpan(5), out var prevUnix) &&
                times[i] - prevUnix < ReAlertGapMinutes * 60)
            {
                return null;
            }
            return key;
        }
        return null;
    }

    private static bool IsRainy(double percentProbability, double mmPerBucket) =>
        percentProbability >= ProbabilityThreshold || mmPerBucket > 0.05;
}

public sealed record FeatureSettings
{
    public bool ClipboardHistoryEnabled { get; init; } = true;
    public bool FocusTimerEnabled { get; init; } = true;
    public bool PriorityAlertsEnabled { get; init; } = true;
    /// <summary>When enabled, suppress media/rain feedback while a focus phase is running.</summary>
    public bool QuietDuringFocus { get; init; }
    public bool FollowActiveMonitor { get; init; } = true;

    // Volume / brightness HUD: the level keys are claimed (so Windows' own
    // volume OSD never appears) and the pill morphs into a slider.
    public bool VolumeHudEnabled { get; init; } = true;
    public bool BrightnessHudEnabled { get; init; } = true;

    // Media keys are mirrored rather than claimed: the pill shows which key was
    // pressed and what is playing, and Windows keeps handling the key itself.
    public bool MediaHudEnabled { get; init; } = true;

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
    public static readonly string[] Known = { "clock", "system", "media", "notes", "weather", "launcher", "focus", "clipboard", "calendar", "actions", "inbox" };
    public static readonly string[] DefaultEnabled = { "clock", "system", "media", "notes", "weather", "launcher", "focus", "clipboard", "calendar", "actions", "inbox" };

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
        ["calendar"] = "Calendar & next meeting",
        ["actions"] = "Quick actions",
        ["inbox"] = "Notification inbox",
    };

    public List<string> Enabled { get; init; } = new(DefaultEnabled);
    public string Notes { get; init; } = "";
    public WeatherWidgetConfig Weather { get; init; } = new();
    public List<LauncherApp> LauncherApps { get; init; } = new();
    public SystemMetricsConfig SystemMetrics { get; init; } = new();
    public CalendarWidgetConfig Calendar { get; init; } = new();
    public List<string> QuickActions { get; init; } = new(QuickActionCatalog.DefaultEnabled);
    public bool QuietMode { get; init; }

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
            Calendar = Calendar is null ? new CalendarWidgetConfig() : Calendar.Normalize(),
            QuickActions = (QuickActions ?? []).Where(QuickActionCatalog.Known.Contains).Distinct(StringComparer.Ordinal).ToList(),
            QuietMode = QuietMode,
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

public sealed record InboxNotification(
    string Id, DateTimeOffset CreatedAt, string Title, string Body, string Glyph = "\uE7BA", bool Read = false);

/// <summary>Reusable, local quick actions. Only known IDs are persisted/executed.</summary>
public static class QuickActionCatalog
{
    public static readonly string[] Known = { "settings", "focus", "quiet", "inbox", "hide" };
    public static readonly string[] DefaultEnabled = { "settings", "focus", "quiet", "inbox" };
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        ["settings"] = "Settings",
        ["focus"] = "Start focus",
        ["quiet"] = "Toggle quiet",
        ["inbox"] = "Notifications",
        ["hide"] = "Hide pill",
    };
}

/// <summary>Snapshot of the full settings groups customized per monitor.</summary>
public sealed record MonitorProfile(
    GeneralSettings General,
    AppearanceSettings Appearance,
    ToastSettings Toasts,
    FeatureSettings Features,
    WidgetSettings Widgets)
{
    public MonitorProfile Normalize() => this with
    {
        General = General is null ? new GeneralSettings() : General.Normalize(),
        Appearance = Appearance is null ? new AppearanceSettings() : Appearance.Normalize(),
        Toasts = Toasts is null ? new ToastSettings() : Toasts.Normalize(),
        Features = Features ?? new FeatureSettings(),
        Widgets = Widgets is null ? new WidgetSettings() : Widgets.Normalize(),
    };

    public static MonitorProfile Capture(WinotchSettings settings) =>
        new(settings.General, settings.Appearance with { Display = DisplayMode.All, DisplayDeviceName = "" },
            settings.Toasts, settings.Features, settings.Widgets);

    public WinotchSettings ApplyTo(WinotchSettings settings) => settings with
    {
        General = General,
        Appearance = Appearance with
        {
            UseMonitorProfiles = settings.Appearance.UseMonitorProfiles,
            Display = settings.Appearance.Display,
            DisplayDeviceName = settings.Appearance.DisplayDeviceName,
        },
        Toasts = Toasts,
        Features = Features,
        Widgets = Widgets,
    };
}

public static class MonitorProfileStore
{
    public static WinotchSettings Resolve(WinotchSettings settings, string deviceName) =>
        settings.Appearance.UseMonitorProfiles && settings.MonitorProfiles.TryGetValue(deviceName, out var profile)
            ? profile.ApplyTo(settings) with
            {
                Appearance = profile.Appearance with
                {
                    UseMonitorProfiles = true,
                    Display = settings.Appearance.Display,
                    DisplayDeviceName = settings.Appearance.DisplayDeviceName,
                },
            }
            : settings;

    public static WinotchSettings Save(WinotchSettings settings, string deviceName, WinotchSettings scoped) =>
        string.IsNullOrWhiteSpace(deviceName) ? settings : settings with
        {
            MonitorProfiles = new Dictionary<string, MonitorProfile>(settings.MonitorProfiles, StringComparer.OrdinalIgnoreCase)
            {
                [deviceName] = MonitorProfile.Capture(scoped),
            },
        };
}

public static class NotificationInboxStore
{
    public const int MaximumEntries = 100;

    public static string WindowsNotificationId(string appId, uint notificationId, DateTimeOffset createdAt) =>
        $"windows:{appId}:{notificationId}:{createdAt.UtcTicks}";

    public static List<InboxNotification> Add(IEnumerable<InboxNotification> current, InboxNotification item) =>
        current.Where(existing => existing.Id != item.Id).Prepend(item)
            .OrderByDescending(existing => existing.CreatedAt).Take(MaximumEntries).ToList();

    public static List<InboxNotification> MergeSystemNotifications(
        IEnumerable<InboxNotification> current, IEnumerable<InboxNotification> systemNotifications,
        IEnumerable<string>? dismissedIds = null)
    {
        var priorSystem = current.Where(item => item.Id.StartsWith("windows:", StringComparison.Ordinal))
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        var dismissed = (dismissedIds ?? []).ToHashSet(StringComparer.Ordinal);
        var fresh = Normalize(systemNotifications)
            .Where(item => !dismissed.Contains(item.Id))
            .Select(item => priorSystem.TryGetValue(item.Id, out var previous) ? item with { Read = previous.Read } : item);
        return current.Where(item => !item.Id.StartsWith("windows:", StringComparison.Ordinal))
            .Concat(fresh).OrderByDescending(item => item.CreatedAt).Take(MaximumEntries).ToList();
    }

    public static List<string> DismissSystemNotifications(IEnumerable<string> current, IEnumerable<string> ids) =>
        current.Concat(ids).Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal).TakeLast(2000).ToList();

    public static List<InboxNotification> MarkRead(IEnumerable<InboxNotification> current, string id) =>
        current.Select(item => item.Id == id ? item with { Read = true } : item).ToList();

    public static List<InboxNotification> Clear(IEnumerable<InboxNotification> _) => new();

    public static List<InboxNotification> Normalize(IEnumerable<InboxNotification>? notifications) =>
        (notifications ?? []).Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Id) &&
            !string.IsNullOrWhiteSpace(item.Title))
            .OrderByDescending(item => item.CreatedAt).Take(MaximumEntries).ToList();
}

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
