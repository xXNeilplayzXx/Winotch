using SkiaSharp;

namespace Winotch.Engine;

/// <summary>The Dynamic Island design tokens, in one place so the shell geometry
/// and its ink can never drift apart.
///
/// These are the reference component's numbers verbatim: a 32px corner radius on
/// the expanded shell, a 126x37 compact pill, and an opaque near-black body with
/// no border, rim or top highlight. Winotch's own gradient/glass treatments stay
/// available as <see cref="AppearanceSettings.Surface"/> and
/// <see cref="AppearanceSettings.UsesGlossySkin"/>; this is what the default
/// Classic skin paints.</summary>
public static class IslandDesign
{
    /// <summary>Corner radius of the expanded shell (reference: RADIUS = 32).
    /// The collapsed pill keeps a full-height radius, which the reference gets
    /// for free because its 37px pill is shorter than its 32px radius.</summary>
    public const double PanelRadius = 32;

    /// <summary>Reference compact pill: PILL_WIDTH = 126.</summary>
    public const double PillWidth = 126;

    /// <summary>Reference compact pill: PILL_HEIGHT = 37.</summary>
    public const double PillHeight = 37;

    /// <summary>The reference body colour. Tailwind's <c>bg-foreground</c> in a
    /// dark palette is effectively black; a hair of blue keeps it from reading as
    /// a hole punched in the desktop.</summary>
    public static readonly SKColor Ink = new(0x0A, 0x0A, 0x0C);
}
