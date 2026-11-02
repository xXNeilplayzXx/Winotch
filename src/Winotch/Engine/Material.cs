using SkiaSharp;

namespace Winotch.Engine;

/// <summary>The floating surfaces — the things that hover over the desktop
/// instead of living inside the island: toasts, the context and add menus, the
/// LED tooltip.
///
/// They follow the Apple material model rather than the island's ink slab: no
/// drawn frame, no even rim. A surface is a wide soft shadow that lifts it off
/// the wallpaper, a body a step above the desktop, and one light-catching edge
/// along the top that fades out by the bottom. Drawing a full outline around
/// them — which is what the old HUD cards did — reads as a wireframe around a
/// rectangle rather than as a pane of glass, and it is the single biggest
/// reason those cards looked like system chrome instead of a floating layer.</summary>
public static class Material
{
    /// <summary>A notification card: the largest floating surface, so it carries
    /// the roundest corners and the deepest shadow.</summary>
    public const float ToastRadius = 18f;

    /// <summary>Menus sit closer to their anchor and read as a lighter
    /// material, so they lift less and keep a tighter corner.</summary>
    public const float MenuRadius = 13f;

    /// <summary>Tooltips are the lightest thing on screen.</summary>
    public const float TooltipRadius = 11f;

    /// <summary>Paint a floating surface: lift, body, top light, one edge.
    /// <paramref name="lift"/> scales the shadow for surfaces that should read
    /// as heavier (a toast over a card) or lighter (a tooltip).</summary>
    public static void DrawFloatingSurface(SKCanvas canvas, SKRect rect, float radius,
        double alpha = 1, float lift = 1f)
    {
        if (alpha <= 0.01 || rect.Width <= 0 || rect.Height <= 0) return;
        lift = Math.Clamp(lift, 0f, 2f);
        radius = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2f);

        // 1. The lift. Wide and soft with almost no offset: an offset shadow
        //    reads as a second border, and this shadow is the only thing
        //    separating the material from a bright or busy wallpaper.
        var sigma = Math.Max(5f, rect.Height * 0.16f) * lift;
        using (var shadow = new SKPaint
        {
            Color = new SKColor(0, 0, 0, (byte)Math.Clamp(Math.Round(120 * alpha), 0, 255)),
            IsAntialias = true,
        })
        {
            shadow.ImageFilter = SKImageFilter.CreateDropShadow(
                0, sigma * 0.30f, sigma, sigma,
                new SKColor(0, 0, 0, (byte)Math.Clamp(Math.Round(165 * alpha), 0, 255)));
            canvas.DrawRoundRect(rect, radius, radius, shadow);
        }

        // 2. The body. Kept close to opaque on purpose: Apple's thin materials
        //    are translucent because the system blurs what is behind them, and
        //    Skia cannot blur the desktop from here without a live capture — a
        //    half-transparent fill without that blur is just muddy text.
        using (var body = ShellUi.Fill(ShellUi.Fade(ShellUi.Card, alpha)))
        {
            canvas.DrawRoundRect(rect, radius, radius, body);
        }

        canvas.Save();
        using (var path = new SKPath())
        {
            path.AddRoundRect(rect, radius, radius);
            canvas.ClipPath(path, SKClipOperation.Intersect, antialias: true);

            // 3. The light. A translucent pane catches the room along its top
            //    edge and has lost it by the middle. ShellUi.Text is light in
            //    the dark theme and dark in the light one, so the same call
            //    lights the top edge correctly in both.
            var lit = ShellUi.Text;
            using (var sheenShader = SKShader.CreateLinearGradient(
                new SKPoint(rect.Left, rect.Top),
                new SKPoint(rect.Left, rect.Top + rect.Height * 0.55f),
                new[] { ShellUi.Fade(lit, 0.10 * alpha).ToSk(), ShellUi.Fade(lit, 0).ToSk() },
                new[] { 0f, 1f }, SKShaderTileMode.Clamp))
            using (var sheen = new SKPaint { Shader = sheenShader, IsAntialias = true })
            {
                canvas.DrawRect(rect, sheen);
            }

            canvas.Restore();
        }

        // 4. One edge, brightest where the light lands. A stroke that keeps the
        //    same weight all the way round is what makes a surface look drawn
        //    on rather than laid over.
        using (var edgeShader = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Left, rect.Bottom),
            new[] { ShellUi.Fade(ShellUi.Text, 0.20 * alpha).ToSk(), ShellUi.Fade(ShellUi.Text, 0.05 * alpha).ToSk() },
            new[] { 0f, 1f }, SKShaderTileMode.Clamp))
        using (var edge = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Shader = edgeShader,
            IsAntialias = true,
        })
        {
            canvas.DrawRoundRect(rect, radius, radius, edge);
        }
    }

    /// <summary>A full-radius groove with a level poured into it — the shape
    /// every Apple level control uses (volume, brightness, playback position).
    /// <paramref name="fill01"/> is clamped, and a non-zero level always shows
    /// at least one bar-height of fill: a bar that can shrink to nothing reads
    /// as broken rather than as quiet.</summary>
    public static void DrawLevelBar(SKCanvas canvas, float left, float top, float width, float height,
        double fill01, Col track, double trackAlpha, Col fill, double fillAlpha)
    {
        if (width <= 0 || height <= 0) return;
        var radius = height / 2;
        if (trackAlpha > 0.01)
        {
            ShellUi.FillRound(canvas, left, top, width, height, radius, ShellUi.Fade(track, trackAlpha));
        }

        var level = Math.Clamp(fill01, 0, 1);
        if (level <= 0.001 || fillAlpha <= 0.01) return;
        var filled = Math.Max(height, width * (float)level);
        ShellUi.FillRound(canvas, left, top, Math.Min(width, filled), height, radius,
            ShellUi.Fade(fill, fillAlpha));
    }
}
