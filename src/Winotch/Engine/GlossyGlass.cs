using SkiaSharp;

namespace Winotch.Engine;

/// <summary>The high-gloss glass tokens: the midnight capsule and the cool-slate
/// cards. Kept apart from <see cref="ShellTheme"/> on purpose — the theme is the
/// app's flat palette (text, accents, panel fills) resolved from Windows, while
/// these are the material colors of the glass treatment, and they are opaque here
/// with the alpha applied at draw time so one palette serves the pill and the
/// panel cards at any translucency.</summary>
public sealed record GlassPalette(
    Col CapsuleTop, Col CapsuleMid, Col CapsuleBottom, Col CapsuleGlow,
    Col CapsuleEdge, Col CapsuleRim, Col CapsuleBloom, Col Halo,
    Col CardFillTop, Col CardFillBottom, Col CardStroke, Col CardSheen)
{
    /// <summary>The design's midnight glass. The capsule is nearly black at the
    /// top and turns deep blue only in its last fifth — the blue in the design
    /// reads bright because it pools low and because the halo glows outside the
    /// shape, not because the body is a saturated blue. The cards sit a touch
    /// above the backdrop and stay nearly flat top to bottom, so they read as
    /// framed panes of slate rather than as a dark wash that fades out.</summary>
    public static GlassPalette Dark { get; } = new(
        CapsuleTop: Col.Rgba(0x0A, 0x0D, 0x1A),
        CapsuleMid: Col.Rgba(0x08, 0x13, 0x24),
        CapsuleBottom: Col.Rgba(0x0A, 0x1C, 0x3C),
        CapsuleGlow: Col.Rgba(0x3C, 0x8C, 0xF0),
        CapsuleEdge: Col.White,
        CapsuleRim: Col.Rgba(0x7F, 0xB4, 0xFF),
        // The bloom under the capsule is much more saturated than the glow
        // inside it: measured off the design, the light pooling on the wallpaper
        // is a deep royal blue, not the pale blue-white of the rim.
        CapsuleBloom: Col.Rgba(0x0F, 0x4A, 0xD8),
        Halo: Col.Rgba(0x5A, 0x9B, 0xE0),
        CardFillTop: Col.Rgba(0x3A, 0x41, 0x4E),
        CardFillBottom: Col.Rgba(0x2B, 0x30, 0x3A),
        CardStroke: Col.Rgba(0xE8, 0xEE, 0xF8),
        CardSheen: Col.White);

    /// <summary>Frosted daylight glass — the same geometry and highlights, with
    /// the capsule lit rather than deep, so the light theme keeps the material
    /// instead of turning into a gray box.</summary>
    public static GlassPalette Light { get; } = new(
        CapsuleTop: Col.Rgba(0xF7, 0xFA, 0xFE),
        CapsuleMid: Col.Rgba(0xE2, 0xEB, 0xF8),
        CapsuleBottom: Col.Rgba(0xC4, 0xD8, 0xF4),
        CapsuleGlow: Col.Rgba(0x5B, 0x9B, 0xEC),
        CapsuleEdge: Col.White,
        CapsuleRim: Col.Rgba(0x3F, 0x7C, 0xD0),
        CapsuleBloom: Col.Rgba(0x4A, 0x86, 0xE0),
        Halo: Col.Rgba(0x7A, 0xA8, 0xE0),
        CardFillTop: Col.Rgba(0xFF, 0xFF, 0xFF),
        CardFillBottom: Col.Rgba(0xE9, 0xEE, 0xF6),
        CardStroke: Col.White,
        CardSheen: Col.White);

    public static GlassPalette For(bool light) => light ? Light : Dark;

    /// <summary>The glass lightly tinted toward the user's accent — enough that
    /// the glow and the sparkline share the accent's family, never enough to lose
    /// the midnight base (the design's identity is the depth, not the hue).</summary>
    public GlassPalette WithAccent(Col accent) => this with
    {
        CapsuleGlow = Col.Lerp(CapsuleGlow, accent, 0.35),
        CapsuleRim = Col.Lerp(CapsuleRim, accent, 0.45),
        CapsuleBloom = Col.Lerp(CapsuleBloom, accent, 0.30),
        Halo = Col.Lerp(Halo, accent, 0.40),
        CapsuleBottom = Col.Lerp(CapsuleBottom, accent, 0.12),
    };
}

/// <summary>Painters for the high-gloss glass treatment: the milled midnight
/// capsule with its halo and the framed slate cards.
///
/// Self-contained on purpose: these take a <see cref="GlassPalette"/> and plain
/// geometry rather than reaching for the app palette, so the material can be
/// rendered with nothing but a canvas.</summary>
public static class GlossyGlass
{
    /// <summary>Card corner radius from the design.</summary>
    public const float CardRadius = 16f;

    private static SKColor Sk(Col color, double alpha = 1) =>
        new(color.R, color.G, color.B,
            (byte)Math.Clamp(Math.Round(255 * alpha * (color.A / 255.0)), 0, 255));

    /// <summary>Everything the capsule throws onto the desktop: a whisper of a
    /// rim glow, and the light that pools below it.
    ///
    /// The design is specific about this — the glass is dark, and the only light
    /// it spills sits under its bottom edge, a blue bloom that peaks a little way
    /// out and dies within a fifth of the capsule's height. An even halo all the
    /// way round would read as a neon outline instead.</summary>
    public static void DrawCapsuleHalo(SKCanvas canvas, SKRect rect, float radius,
        GlassPalette palette, double strength = 1)
    {
        if (strength <= 0 || rect.Width <= 0) return;

        // Just a hint of a rim, all the way round: in the design the wall beside
        // and above the capsule keeps its own colour, so anything stronger than
        // a whisper here reads as a neon outline.
        using (var filter = SKImageFilter.CreateBlur(6f, 6f, SKShaderTileMode.Clamp))
        using (var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 6f,
            Color = Sk(palette.Halo, 0.03 * strength),
            IsAntialias = true,
            ImageFilter = filter,
        })
        {
            canvas.DrawRoundRect(rect, radius, radius, paint);
        }

        // The under-glow, which is where the design puts almost all of its
        // light: a blue bloom on the wall below the capsule. Measured off the
        // design it is tight and flat-topped — full brightness from the centre
        // out to about a third of the capsule's width, gone past half of it, and
        // gone just as fast downwards, so the whole bloom fits in the eighth of
        // a height under the bottom edge. A plain radial pool would spread too
        // far in both directions; the plateau stops carry the flat top.
        var poolX = Math.Max(1f, rect.Width * 0.50f);
        var poolY = Math.Max(1f, rect.Height * 0.10f);
        canvas.Save();
        canvas.Translate(rect.MidX, rect.Bottom + rect.Height * 0.01f);
        canvas.Scale(1f, poolY / poolX);
        using (var pool = SKShader.CreateRadialGradient(
            new SKPoint(0, 0), poolX,
            [Sk(palette.CapsuleBloom, 0.58 * strength), Sk(palette.CapsuleBloom, 0.52 * strength),
                Sk(palette.CapsuleBloom, 0)],
            [0f, 0.82f, 1f], SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = pool, IsAntialias = true })
        {
            canvas.DrawCircle(0, 0, poolX, paint);
        }
        canvas.Restore();
    }

    /// <summary>The capsule body: the midnight gradient, the glow pooled at the
    /// bottom, and the top reflection that sells it as glass.</summary>
    public static void DrawCapsuleBody(SKCanvas canvas, SKRect rect, float radius,
        GlassPalette palette, double alpha = 1)
    {
        if (alpha <= 0 || rect.Width <= 0 || rect.Height <= 0) return;

        using var path = new SKPath();
        path.AddRoundRect(rect, radius, radius);

        // 1. The deep body: near-black at the rim, midnight through the middle,
        //    and blue only in the last fifth, where the light pools.
        using (var shader = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Left, rect.Bottom),
            [Sk(palette.CapsuleTop, alpha), Sk(palette.CapsuleMid, alpha),
                Sk(palette.CapsuleBottom, alpha)],
            [0f, 0.55f, 1f], SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader, IsAntialias = true })
        {
            canvas.DrawPath(path, paint);
        }

        canvas.Save();
        canvas.ClipPath(path, SKClipOperation.Intersect, antialias: true);

        // 2. The blue glow, pooled low — the "midnight-blue glow" the capsule is
        //    built around. It is squashed vertically so it hugs the bottom edge:
        //    a circular pool of this width would still be bright at the middle
        //    of a capsule this shallow, and the design keeps the middle dark.
        var glowX = Math.Max(1f, rect.Width * 0.36f);
        var glowY = Math.Max(1f, rect.Height * 0.30f);
        canvas.Save();
        canvas.Translate(rect.MidX, rect.Bottom);
        canvas.Scale(1f, glowY / glowX);
        using (var glowShader = SKShader.CreateRadialGradient(
            new SKPoint(0, 0), glowX,
            [Sk(palette.CapsuleGlow, 0.78 * alpha), Sk(palette.CapsuleGlow, 0.30 * alpha),
                Sk(palette.CapsuleGlow, 0)],
            [0f, 0.45f, 1f], SKShaderTileMode.Clamp))
        using (var glowPaint = new SKPaint { Shader = glowShader, IsAntialias = true })
        {
            canvas.DrawPath(path, glowPaint);
        }
        canvas.Restore();

        // 3. The reflection the design leads with: a bright band just under the
        //    top rim, gone by the middle. Short and strong rather than a long
        //    gentle wash — a wash this shallow is what lets the rim itself stay
        //    the brightest thing on the glass.
        var bandTop = rect.Top + rect.Height * 0.015f;
        var bandBottom = rect.Top + rect.Height * 0.09f;
        using (var bandShader = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, bandTop), new SKPoint(rect.Left, bandBottom),
            [Sk(Col.White, 0.36 * alpha), Sk(Col.White, 0)],
            [0f, 1f], SKShaderTileMode.Clamp))
        using (var bandPaint = new SKPaint { Shader = bandShader, IsAntialias = true })
        {
            canvas.DrawPath(path, bandPaint);
        }

        // 4. The lit bottom: the design's capsule is brightest exactly where the
        //    glow meets its own edge, a fine blue line running the width of the
        //    shape just inside the stroke. Without it the blue reads as a puddle
        //    the capsule is sitting in rather than light inside the glass.
        var lipInset = Math.Max(1.2f, rect.Height * 0.014f);
        var lip = new SKRect(rect.Left + lipInset, rect.Top + lipInset,
            rect.Right - lipInset, rect.Bottom - lipInset);
        using (var lipFilter = SKImageFilter.CreateBlur(1.1f, 1.1f, SKShaderTileMode.Clamp))
        using (var lipShader = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top + rect.Height * 0.60f), new SKPoint(rect.Left, rect.Bottom),
            [Sk(palette.CapsuleGlow, 0), Sk(palette.CapsuleGlow, 0.85 * alpha)],
            [0f, 1f], SKShaderTileMode.Clamp))
        using (var lipPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1.1f, rect.Height * 0.013f),
            Shader = lipShader,
            IsAntialias = true,
            ImageFilter = lipFilter,
        })
        {
            canvas.DrawRoundRect(lip, radius - lipInset, radius - lipInset, lipPaint);
        }

        canvas.Restore();
    }

    /// <summary>The milled edge: the crisp outer stroke, the softer inner one,
    /// and the brighter light-blue rim along the bottom where the glow spills
    /// over the edge.</summary>
    public static void DrawCapsuleEdge(SKCanvas canvas, SKRect rect, float radius,
        GlassPalette palette, double alpha = 1)
    {
        if (alpha <= 0 || rect.Width <= 0) return;

        // A hairline of contact shadow just outside the body, so the milled rim
        // reads as a solid edge rather than as a glow bleeding off the glass.
        using (var contact = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1.1f, rect.Height * 0.016f),
            Color = Sk(Col.Black, 0.24 * alpha),
            IsAntialias = true,
        })
        {
            canvas.DrawRoundRect(rect, radius, radius, contact);
        }

        // The rim is lit, not dark. The design's capsule catches light all round
        // its upper arc and turns blue where the glow meets the bottom edge, so
        // one gradient down the vertical axis carries both — a single flat
        // stroke cannot be bright at the top and blue at the bottom.
        var rimWidth = Math.Max(1.2f, rect.Height * 0.015f);
        using (var rimShader = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Left, rect.Bottom),
            [Sk(palette.CapsuleEdge, 0.78 * alpha), Sk(palette.CapsuleEdge, 0.22 * alpha),
                Sk(palette.CapsuleRim, 0.95 * alpha)],
            [0f, 0.60f, 1f], SKShaderTileMode.Clamp))
        using (var rim = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = rimWidth,
            Shader = rimShader,
            IsAntialias = true,
        })
        {
            canvas.DrawRoundRect(rect, radius, radius, rim);
        }

        // Milled glass catches the room at its ends as well as along the top, and
        // a vertical gradient cannot say that. A second, horizontal stroke adds
        // the bright arcs down the left and right — which is what makes the ends
        // read as polished rather than as a flat silhouette.
        using (var endShader = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Top),
            [Sk(palette.CapsuleEdge, 0.55 * alpha), Sk(palette.CapsuleEdge, 0),
                Sk(palette.CapsuleEdge, 0.55 * alpha)],
            [0f, 0.5f, 1f], SKShaderTileMode.Clamp))
        using (var ends = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = rimWidth * 0.9f,
            Shader = endShader,
            IsAntialias = true,
        })
        {
            canvas.DrawRoundRect(rect, radius, radius, ends);
        }

        var inset = 2.1f;
        var inner = new SKRect(rect.Left + inset, rect.Top + inset, rect.Right - inset, rect.Bottom - inset);
        using (var innerPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Color = Sk(palette.CapsuleEdge, 0.10 * alpha),
            IsAntialias = true,
        })
        {
            canvas.DrawRoundRect(inner, radius - inset, radius - inset, innerPaint);
        }
    }

    /// <summary>The whole capsule in one call — halo first, so it sits under the
    /// body.</summary>
    public static void DrawCapsule(SKCanvas canvas, SKRect rect, float radius,
        GlassPalette palette, double alpha = 1)
    {
        DrawCapsuleHalo(canvas, rect, radius, palette, alpha);
        DrawCapsuleBody(canvas, rect, radius, palette, alpha);
        DrawCapsuleEdge(canvas, rect, radius, palette, alpha);
    }

    /// <summary>A framed slate glass card: translucent body, the 1px cool-white
    /// stroke, and the soft sheen across the top third. <paramref name="hover"/>
    /// lifts the body and the stroke for the card under the cursor.</summary>
    public static void DrawCard(SKCanvas canvas, SKRect rect, float radius,
        GlassPalette palette, double alpha = 1, double hover = 0)
    {
        if (alpha <= 0 || rect.Width <= 0 || rect.Height <= 0) return;
        hover = Math.Clamp(hover, 0, 1);

        using var path = new SKPath();
        path.AddRoundRect(rect, radius, radius);

        var lift = 0.05 * hover;
        using (var shader = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Left, rect.Bottom),
            [Sk(palette.CardFillTop, (0.9 + lift) * alpha), Sk(palette.CardFillBottom, (0.9 + lift) * alpha)],
            [0f, 1f], SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader, IsAntialias = true })
        {
            canvas.DrawPath(path, paint);
        }

        canvas.Save();
        canvas.ClipPath(path, SKClipOperation.Intersect, antialias: true);
        using (var sheenShader = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Left, rect.Top + rect.Height * 0.45f),
            [Sk(palette.CardSheen, 0.05 * alpha), Sk(palette.CardSheen, 0.0)],
            [0f, 1f], SKShaderTileMode.Clamp))
        using (var sheen = new SKPaint { Shader = sheenShader, IsAntialias = true })
        {
            canvas.DrawRect(rect, sheen);
        }
        canvas.Restore();

        // The 1px stroke is brightest along the top and fades toward the bottom,
        // the way a lit rim on a pane does; a flat stroke reads as a wire frame.
        using (var strokeShader = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Left, rect.Bottom),
            [Sk(palette.CardStroke, (0.30 + 0.22 * hover) * alpha),
                Sk(palette.CardStroke, (0.09 + 0.14 * hover) * alpha)],
            [0f, 1f], SKShaderTileMode.Clamp))
        using (var stroke = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Shader = strokeShader,
            IsAntialias = true,
        })
        {
            canvas.DrawRoundRect(rect, radius, radius, stroke);
        }

        // The lit edge the design leads with sits along the top: a hairline
        // brighter than the surrounding stroke, between the corner radii.
        using var topEdge = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.2f,
            Color = Sk(palette.CardStroke, (0.40 + 0.24 * hover) * alpha),
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
        };
        canvas.DrawLine(rect.Left + radius, rect.Top + 0.6f, rect.Right - radius, rect.Top + 0.6f, topEdge);
    }
}
