using SkiaSharp;

namespace Winotch.Engine;

/// <summary>Visual tokens and low-level drawing helpers shared by the shell.
/// The palette is resolved per frame from <see cref="Theme"/> (set by the host
/// from the current settings), so the island re-skins live when the theme,
/// accent or Windows appearance changes.</summary>
public static class ShellUi
{
    private static ShellTheme _theme = ShellTheme.Dark;

    /// <summary>The palette used for the current render. The host (overlay
    /// window / shell) assigns the resolved theme before drawing each frame.</summary>
    public static ShellTheme Theme
    {
        get => _theme;
        set => _theme = value ?? ShellTheme.Dark;
    }

    public static Col PanelTop => _theme.PanelTop;
    public static Col PanelBottom => _theme.PanelBottom;
    public static Col Card => _theme.Card;
    public static Col CardHover => _theme.CardHover;
    public static Col Stroke => _theme.Stroke;
    public static Col Text => _theme.Text;
    public static Col Muted => _theme.Muted;
    public static Col Accent => _theme.Accent;
    public static Col OnAccent => _theme.OnAccent;
    public static Col Ok => _theme.Ok;
    public static Col Warn => _theme.Warn;
    public static Col Danger => _theme.Danger;
    public static Col Hairline => _theme.Hairline;

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

    /// <summary>Solid vertical body gradient, alpha-tinted by the given factor
    /// (1 = opaque solid, 0.x = translucent glass).</summary>
    public static SKPaint Gradient(float top, float bottom, double alpha = 1)
    {
        var a = (byte)Math.Clamp(Math.Round(255 * alpha), 0, 255);
        var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, top), new SKPoint(0, bottom),
            new[] { PanelTop.WithAlpha(a).ToSk(), PanelBottom.WithAlpha(a).ToSk() },
            new[] { 0f, 1f }, SKShaderTileMode.Clamp);
        return new SKPaint { Shader = shader, IsAntialias = true };
    }

    /// <summary>Diagonal translucent wash for a gradient skin, drawn over the
    /// body so the tint sits under the content but above the glass blur.</summary>
    public static void DrawGradientSkin(SKCanvas canvas, SKRect rect, string gradientId)
    {
        var stops = GradientSkin.Stops(gradientId);
        if (stops.Length == 0) return;

        var colors = new SKColor[stops.Length];
        var positions = new float[stops.Length];
        for (var i = 0; i < stops.Length; i++)
        {
            var (r, g, b, alpha) = stops[i];
            colors[i] = new SKColor(r, g, b, (byte)Math.Clamp(Math.Round(255 * alpha), 0, 255));
            positions[i] = stops.Length == 1 ? 0f : (float)i / (stops.Length - 1);
        }

        var shader = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Bottom),
            colors, positions, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader, IsAntialias = true };
        canvas.DrawRect(rect, paint);
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