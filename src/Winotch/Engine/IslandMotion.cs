namespace Winotch.Engine;

/// <summary>The island's motion language, kept in one place so the collapsed
/// pill and the expanded panel can never animate on different curves.
///
/// The numbers come from the Dynamic Island reference component: content that
/// arrives from 90% scale, eight DIPs high and five pixels of blur, and content
/// that leaves by drifting six DIPs up and shrinking back to 90% while staying
/// sharp. Expressed as a second-order spring that is a frequency plus a damping
/// <em>ratio</em>, stiffness 400 / damping 30 become <see cref="Frequency"/> and
/// <see cref="DampingRatio"/> — which is what <see cref="SecondOrderVec2"/>
/// takes.</summary>
public static class IslandMotion
{
    /// <summary>Reference spring stiffness (framer-motion units).</summary>
    public const double Stiffness = 400;

    /// <summary>Reference spring damping (framer-motion units).</summary>
    public const double Damping = 30;

    /// <summary>Content entering the island starts at this scale.</summary>
    public const double EnterScale = 0.9;

    /// <summary>Content entering the island starts this many DIPs high —
    /// negative because the layer arrives from above, not below.</summary>
    public const double EnterOffsetY = -8;

    /// <summary>The reference's <c>blur(5px)</c> on incoming content.</summary>
    public const double EnterBlurPx = 5;

    /// <summary>Content leaving the island ends at this scale.</summary>
    public const double ExitScale = 0.9;

    /// <summary>Content leaving the island ends this many DIPs high.</summary>
    public const double ExitOffsetY = -6;

    /// <summary>The reference's <c>blur(0px)</c> on outgoing content: it leaves
    /// sharp but fast, which is why the exit reads as a quick clip rather than a
    /// dissolve.</summary>
    public const double ExitBlurPx = 0;

    /// <summary>CSS blur radius to Skia sigma. CSS describes a Gaussian by its
    /// visible radius; Skia by its standard deviation, which is roughly half of
    /// it.</summary>
    public const double BlurSigmaPerPx = 0.5;

    /// <summary>Natural frequency in Hz that reproduces stiffness 400.</summary>
    public static double Frequency => Math.Sqrt(Stiffness) / (2 * Math.PI);

    /// <summary>Damping ratio that reproduces damping 30 — critically damped is
    /// 1, so this settles quickly with a small, deliberate overshoot.</summary>
    public static double DampingRatio => Damping / (2 * Math.Sqrt(Stiffness));
}

/// <summary>How one layer of island content should be drawn partway through a
/// change. <see cref="Progress"/> runs 0 → 1 for the layer's own open or close,
/// so both directions are described by the same four numbers and the effect
/// reverses cleanly if the pointer leaves mid-morph.</summary>
public readonly record struct ContentFrame(double Opacity, double Scale, double OffsetY, double BlurSigma)
{
    /// <summary>Content arriving: 90% scale, eight DIPs high and softly blurred
    /// at the start; settled, sharp and full size at the end.</summary>
    public static ContentFrame Entering(double progress)
    {
        var t = Easings.Clamp01(progress);
        var eased = Easings.EaseOutCubic(t);
        return new ContentFrame(
            Opacity: t,
            Scale: IslandMotion.EnterScale + (1 - IslandMotion.EnterScale) * eased,
            OffsetY: IslandMotion.EnterOffsetY * (1 - eased),
            BlurSigma: IslandMotion.EnterBlurPx * IslandMotion.BlurSigmaPerPx * (1 - eased));
    }

    /// <summary>Content leaving: full size and sharp at the start, then a touch
    /// smaller and six DIPs higher, sharp the whole way.</summary>
    public static ContentFrame Exiting(double progress)
    {
        var t = Easings.Clamp01(progress);
        var eased = Easings.EaseOutCubic(t);
        return new ContentFrame(
            Opacity: 1 - t,
            Scale: 1 - (1 - IslandMotion.ExitScale) * eased,
            OffsetY: IslandMotion.ExitOffsetY * eased,
            BlurSigma: IslandMotion.ExitBlurPx * IslandMotion.BlurSigmaPerPx * eased);
    }

    /// <summary>True when anything other than a straight opaque blit is
    /// needed — lets a settled island skip the layer cost entirely.</summary>
    public bool IsResting =>
        Opacity >= 0.999 && Math.Abs(Scale - 1) < 0.0005 &&
        Math.Abs(OffsetY) < 0.01 && BlurSigma < 0.01;
}
