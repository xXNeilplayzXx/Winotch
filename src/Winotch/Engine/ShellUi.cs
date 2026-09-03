using SkiaSharp;

namespace Winotch.Engine;

/// <summary>Visual tokens and low-level drawing helpers shared by the shell.</summary>
public static class ShellUi
{
    // Palette
    public static readonly Col PanelTop = Col.Rgba(0x1A, 0x1D, 0x24);
    public static readonly Col PanelBottom = Col.Rgba(0x10, 0x12, 0x17);
    public static readonly Col Card = Col.Rgba(0x1E, 0x22, 0x2A);
    public static readonly Col CardHover = Col.Rgba(0x26, 0x2B, 0x34);
    public static readonly Col Stroke = Col.Rgba(0x2E, 0x34, 0x40);
    public static readonly Col Text = Col.Rgba(0xF2, 0xF4, 0xF8);
    public static readonly Col Muted = Col.Rgba(0x9C, 0xA5, 0xB4);
    public static readonly Col Accent = Col.Rgba(0x50, 0xD6, 0xFF);
    public static readonly Col Ok = Col.Rgba(0x32, 0xD7, 0x4B);
    public static readonly Col Warn = Col.Rgba(0xFF, 0xB0, 0x4A);
    public static readonly Col Danger = Col.Rgba(0xFF, 0x6B, 0x6B);
    public static readonly Col Hairline = Col.Rgba(0x25, 0x29, 0x31);

    public static Col Fade(Col color, double alpha) =>
        color.WithAlpha((byte)Math.Clamp(Math.Round(color.A * alpha), 0, 255));

    public static SKPaint Fill(Col color)
    {
        return new SKPaint { Color = color.ToSk(), IsAntialias = true };
    }

    public static void FillRound(SKCanvas canvas, float x, float y, float w, float h, float radius, Col color)
    {
        using var paint = Fill(color);
        canvas.DrawRoundRect(x, y, w, h, radius, radius, paint);
    }

    public static void StrokeRound(SKCanvas canvas, float x, float y, float w, float h, float radius,
        float strokeWidth, Col color)
    {
        using var paint = new SKPaint
        {
            Color = color.ToSk(),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
        };
        canvas.DrawRoundRect(x, y, w, h, radius, radius, paint);
    }

    public static void FillCircle(SKCanvas canvas, float cx, float cy, float r, Col color)
    {
        using var paint = Fill(color);
        canvas.DrawCircle(cx, cy, r, paint);
    }

    public static void DrawGlyph(SKCanvas canvas, string glyph, float centerX, float centerY,
        float px, Col color, float alpha = 1)
    {
        TextKit.DrawCentered(canvas, glyph, centerX, centerY, px, Fade(color, alpha), TextKit.Icons);
    }

    public static SKPaint Gradient(float top, float bottom)
    {
        var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, top), new SKPoint(0, bottom),
            new[] { PanelTop.ToSk(), PanelBottom.ToSk() },
            new[] { 0f, 1f }, SKShaderTileMode.Clamp);
        return new SKPaint { Shader = shader, IsAntialias = true };
    }

    /// <summary>Shorten text to fit maxWidth, appending an ellipsis.</summary>
    public static string Ellipsize(string text, float maxWidth, float size, SKTypeface face)
    {
        if (TextKit.Measure(text, size, face) <= maxWidth) return text;
        const string ellipsis = "\u2026";
        var ellipsisWidth = TextKit.Measure(ellipsis, size, face);
        var budget = Math.Max(1, maxWidth - ellipsisWidth);
        for (var len = text.Length - 1; len > 0; len--)
        {
            var candidate = text[..len];
            if (TextKit.Measure(candidate, size, face) <= budget)
            {
                return candidate.TrimEnd() + ellipsis;
            }
        }
        return ellipsis;
    }
}

/// <summary>One interactive element (hit area + action) built during layout.</summary>
public sealed record HitButton(SKRect Bounds, Action OnClick)
{
    public bool Contains(Vec2 point) =>
        point.X >= Bounds.Left && point.X <= Bounds.Right &&
        point.Y >= Bounds.Top && point.Y <= Bounds.Bottom;
}
