using SkiaSharp;

namespace Winotch.Engine;

/// <summary>8-bit RGBA color plus helpers. The default value is transparent black.</summary>
public readonly record struct Col(byte R, byte G, byte B, byte A = 255)
{
    public static readonly Col Transparent = new(0, 0, 0, 0);
    public static readonly Col White = new(255, 255, 255);
    public static readonly Col Black = new(0, 0, 0);

    public static Col Rgba(byte r, byte g, byte b, byte a = 255) => new(r, g, b, a);

    public Col WithAlpha(byte a) => new(R, G, B, a);

    public static Col Lerp(Col from, Col to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Col(
            (byte)Math.Round(from.R + (to.R - from.R) * t),
            (byte)Math.Round(from.G + (to.G - from.G) * t),
            (byte)Math.Round(from.B + (to.B - from.B) * t),
            (byte)Math.Round(from.A + (to.A - from.A) * t));
    }

    public SKColor ToSk() => new(R, G, B, A);
}
