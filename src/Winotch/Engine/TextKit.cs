using SkiaSharp;

namespace Winotch.Engine;

/// <summary>Cached typefaces and text drawing helpers. Fonts come from the OS —
/// Segoe UI Variable on Windows 11, Segoe UI on Windows 10, the Fluent / MDL2
/// icon fonts for glyphs, Segoe UI Emoji for status icons. Nothing bundled.</summary>
public static class TextKit
{
    private static readonly SKTypeface UiRegular = Pick("Segoe UI Variable Text", "Segoe UI", "Arial");
    private static readonly SKTypeface UiSemibold = Pick("Segoe UI Variable Text", "Segoe UI", "Arial",
        weight: SKFontStyleWeight.SemiBold);
    private static readonly SKTypeface UiBold = Pick("Segoe UI Variable Text", "Segoe UI", "Arial",
        weight: SKFontStyleWeight.Bold);
    private static readonly SKTypeface Symbols = Pick("Segoe Fluent Icons", "Segoe MDL2 Assets");
    private static readonly SKTypeface Mono = Pick("Cascadia Mono", "Consolas", "Courier New");
    private static readonly SKTypeface UiEmoji = Pick("Segoe UI Emoji", "Segoe UI Symbol", "Arial");

    public static SKTypeface Regular => UiRegular;
    public static SKTypeface Semibold => UiSemibold;
    public static SKTypeface Bold => UiBold;
    public static SKTypeface Icons => Symbols;
    public static SKTypeface Monospace => Mono;
    public static SKTypeface Emoji => UiEmoji;

    private static SKTypeface Pick(string primary, string fallback, string last,
        SKFontStyleWeight weight = SKFontStyleWeight.Normal)
    {
        foreach (var family in new[] { primary, fallback, last })
        {
            var face = TryFamily(family, weight);
            if (face is not null) return face;
        }
        return TryFamily("Arial", SKFontStyleWeight.Normal) ?? SKTypeface.Default;
    }

    private static SKTypeface? TryFamily(string family, SKFontStyleWeight weight)
    {
        var face = SKTypeface.FromFamilyName(family, weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        return face is not null && !string.IsNullOrEmpty(face.FamilyName) ? face : null;
    }

    public static float Measure(string text, float size, SKTypeface? face = null)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        using var paint = NewPaint(size, face ?? UiRegular, Col.White);
        return paint.MeasureText(text);
    }

    public static SKPaint NewPaint(float textSize, SKTypeface face, Col color) =>
        new()
        {
            Typeface = face,
            TextSize = textSize,
            Color = color.ToSk(),
            IsAntialias = true,
        };

    /// <summary>Draw text left-aligned; (x, y) is the top-left of the text box.</summary>
    public static void Draw(SKCanvas canvas, string text, float x, float y,
        float size, Col color, SKTypeface? face = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        using var paint = NewPaint(size, face ?? UiRegular, color);
        var baseline = y - paint.FontMetrics.Ascent;
        canvas.DrawText(text, x, baseline, paint);
    }

    /// <summary>Draw text centered horizontally on centerX and vertically on
    /// centerY (cap height centered, not full line box).</summary>
    public static void DrawCentered(SKCanvas canvas, string text, float centerX, float centerY,
        float size, Col color, SKTypeface? face = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        using var paint = NewPaint(size, face ?? UiRegular, color);
        var metrics = paint.FontMetrics;
        var width = paint.MeasureText(text);
        var baseline = centerY - (metrics.Ascent + metrics.Descent) / 2;
        canvas.DrawText(text, centerX - width / 2, baseline, paint);
    }

    public static float LineHeight(float size, SKTypeface? face = null)
    {
        using var paint = NewPaint(size, face ?? UiRegular, Col.White);
        var metrics = paint.FontMetrics;
        return metrics.Descent - metrics.Ascent;
    }
}
