namespace Winotch.Engine;

/// <summary>A transient notification card shown under the island.</summary>
public readonly record struct ToastSpec(
    string Icon, string Title, string Body,
    double Seconds = 4.5,
    int? BatteryPercent = null,
    bool Flourish = false);

/// <summary>A live toast with its own fade timeline. Rendered by the island
/// shell below the shell geometry; never resizes an OS window.</summary>
public sealed class Toast
{
    private const double FadeIn = 0.16;
    private const double FadeOut = 0.30;

    private double _age;

    public Toast(ToastSpec spec)
    {
        Spec = spec;
    }

    public ToastSpec Spec { get; }
    public double Remaining => Math.Max(0, Spec.Seconds - _age);

    public bool Expired => _age > Spec.Seconds + FadeOut;

    /// <summary>0 = hidden, 1 = fully visible.</summary>
    public double Opacity
    {
        get
        {
            if (_age < FadeIn) return Easings.EaseOutCubic(_age / FadeIn);
            if (_age > Spec.Seconds) return 1 - Easings.EaseOutCubic((_age - Spec.Seconds) / FadeOut);
            return 1;
        }
    }

    /// <summary>0..1 flourish fill once the toast is past its fade-in.</summary>
    public double FlourishFill
    {
        get
        {
            if (!Spec.Flourish || Spec.BatteryPercent is null) return 0;
            var t = Math.Clamp((_age - FadeIn) / 1.15, 0, 1);
            return Easings.EaseOutCubic(t) * (Spec.BatteryPercent.Value / 100.0);
        }
    }

    public void Tick(double deltaTime) => _age += deltaTime;
}
