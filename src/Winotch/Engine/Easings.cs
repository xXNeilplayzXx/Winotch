namespace Winotch.Engine;

public static class Easings
{
    public static double Clamp01(double t) => t < 0 ? 0 : t > 1 ? 1 : t;

    public static double EaseOutCubic(double t)
    {
        t = 1 - Clamp01(t);
        return 1 - t * t * t;
    }

    public static double EaseOutQuint(double t)
    {
        t = 1 - Clamp01(t);
        return 1 - t * t * t * t * t;
    }

    public static double EaseInCubic(double t)
    {
        t = Clamp01(t);
        return t * t * t;
    }

    public static double EaseInOutCubic(double t)
    {
        t = Clamp01(t);
        return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }
}
