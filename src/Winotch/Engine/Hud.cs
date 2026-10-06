namespace Winotch.Engine;

/// <summary>What a HUD is showing. Brightness is mirror-only (Winotch can't
/// claim the brightness keys), volume is driven by the keys we intercept.</summary>
public enum HudKind { Volume, Brightness }

/// <summary>A HUD reading: the volume or brightness level, 0..1.</summary>
public readonly record struct HudSpec(HudKind Kind, double Level01, bool Muted = false);

/// <summary>The pill's transient level display, with its own hold and fade
/// timeline — the same shape as <see cref="Toast"/>, so the pill can morph
/// into a slider without a second window. A fresh keystroke refreshes the
/// live HUD instead of restarting it, so holding a volume key slides the bar
/// and keeps it on screen.</summary>
public sealed class Hud
{
    private const double FadeIn = 0.10;
    private const double FadeOut = 0.26;
    private const double Hold = 1.05;

    private FadeTimeline _timeline = new(FadeIn, Hold, FadeOut);

    public Hud(HudSpec spec)
    {
        Spec = spec;
        Fill = spec.Level01;
    }

    public HudSpec Spec { get; private set; }

    /// <summary>0..1 bar fill. It eases toward the target so a keypress slides
    /// the bar instead of jumping.</summary>
    public double Fill { get; private set; }

    public bool Muted => Spec.Muted;
    public double Level01 => Spec.Level01;
    public double Percent => Math.Round(Spec.Level01 * 100);

    /// <summary>True once the hold and fade are over — the pill goes back to
    /// its normal content.</summary>
    public bool Expired => _timeline.Expired;

    /// <summary>0 = hidden, 1 = fully visible.</summary>
    public double Opacity => _timeline.Opacity;

    /// <summary>Fold a repeat reading into the live HUD: the level moves, the
    /// clock restarts, and the bar keeps animating from where it was.</summary>
    public void Refresh(HudSpec spec)
    {
        Spec = spec;
        _timeline.Restart();
    }

    public void Tick(double deltaTime)
    {
        _timeline.Tick(deltaTime);
        Fill += (Spec.Level01 - Fill) * Math.Min(1, deltaTime * 14);
        if (Math.Abs(Fill - Spec.Level01) < 0.002) Fill = Spec.Level01;
    }
}
