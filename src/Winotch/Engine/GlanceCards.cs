using SkiaSharp;

namespace Winotch.Engine;

/// <summary>One glance card's content, already formatted. The deck only lays
/// out and paints strings, so the shell keeps every decision about *what* the
/// values mean — and the deck can be rendered from a plain fixture with no
/// services behind it.</summary>
public readonly record struct GlanceWeather(string City, string Temp, string Condition, string Glyph);

/// <summary>A track for the deck's media card. <paramref name="Glyph"/> is the
/// transport mark the card offers — the shell picks it from the playback state,
/// so the deck never has to know which icon a paused track wears.</summary>
public sealed record GlanceMedia(
    string Title, string Artist, string Elapsed, string Total, string Glyph, SKBitmap? Art);

public readonly record struct GlanceFocus(
    string Label, string Time, double Progress01, bool Active, string Glyph, Col Accent);

public readonly record struct GlanceStats(
    string Title, string PrimaryLabel, string PrimaryValue,
    string SecondaryLabel, string SecondaryValue, IReadOnlyList<double> History);

/// <summary>What the deck draws. A null card is not drawn at all — the shell
/// passes null for a widget the user switched off, and a populated card (with an
/// empty-state string) for one that is on but has nothing to show yet, so the
/// frame stays whole instead of losing a corner.</summary>
public sealed record GlanceCardData(
    GlanceWeather? Weather,
    GlanceMedia? Media,
    GlanceFocus? Focus,
    GlanceStats? Stats);

/// <summary>The interactive parts of the deck, in canvas DIP space. The shell
/// turns these into hit buttons — the deck does not know what a click means.</summary>
public readonly record struct GlanceHits(
    LayoutRect? WeatherCard,
    LayoutRect? MediaToggle,
    LayoutRect? FocusToggle,
    LayoutRect? SystemCard);

/// <summary>Colors and type the deck paints with, resolved by the shell from the
/// live theme so the glass follows light/dark and the accent.</summary>
public readonly record struct GlanceStyle(
    GlassPalette Glass, Col Text, Col Muted, Col Hairline, Col Accent)
{
    public static GlanceStyle For(bool light, Col accent) => light
        ? new GlanceStyle(GlassPalette.Light.WithAccent(accent), Col.Rgba(0x14, 0x19, 0x22),
            Col.Rgba(0x5E, 0x6B, 0x7E), Col.Rgba(0xD9, 0xDE, 0xE6), accent)
        : new GlanceStyle(GlassPalette.Dark.WithAccent(accent), Col.Rgba(0xF2, 0xF4, 0xF8),
            Col.Rgba(0x9C, 0xA5, 0xB4), Col.Rgba(0x25, 0x29, 0x31), accent);
}

/// <summary>The four framed cards of the glance layout: weather and now playing
/// across the top, focus and system stats below. Every card is painted from its
/// own rect and a <see cref="GlanceStyle"/>, and every size and position below is
/// a fraction of that rect — the design's own proportions, measured off the
/// mockup — so the composition holds together at any canvas size.
///
/// Same contract as the rest of the engine: nothing here reads settings, the
/// clock or the cursor — the shell hands in formatted values and a hover point.
/// </summary>
public static class GlanceCards
{
    // The design's card metrics, as fractions of the card's own box. The deck
    // runs anywhere from a laptop panel to a 4K display, so nothing is written
    // in points: a card is the same composition at 190px wide as at 300px, and
    // the type grows with it instead of shrinking into the corner.
    private const float PadFraction = 0.11f;

    /// <summary>The design keeps a smaller inset on the right than on the left —
    /// text columns run close to the card's edge, which is what lets a track
    /// title and its readout sit on one line.</summary>
    private const float RightPadFraction = 0.07f;

    /// <summary>How far a line's ink starts below the top of its text box, as a
    /// fraction of the font size. <see cref="TextKit.Draw"/> takes the top of the
    /// box, but the design's measurements are of the ink, and the two differ by
    /// about this much for any UI face.</summary>
    private const float InkOffset = 0.12f;

    /// <summary>Segoe UI sets about a tenth wider than the face the design was
    /// drawn in, so every measured type size is pulled in by this factor — the
    /// fractions below stay the design's own, and this is the one place the font
    /// swap is accounted for. Without it a track title that fits the mockup
    /// ellipsizes in the deck.</summary>
    private const float FaceWidthFactor = 0.9f;

    public static GlanceHits Draw(
        SKCanvas canvas, GlanceDeck deck, GlanceCardData data, GlanceStyle style,
        double cursorX, double cursorY, double alpha = 1)
    {
        var weather = deck.Weather.Rect;
        var media = deck.Media.Rect;
        var focus = deck.Focus.Rect;
        var stats = deck.System.Rect;
        var hits = new GlanceHits(weather, null, null, stats);
        if (alpha <= 0.02) return hits;

        if (data.Weather is { } weatherData)
        {
            DrawFrame(canvas, weather, style, Hover(weather, cursorX, cursorY), alpha);
            DrawWeather(canvas, weather, weatherData, style, alpha);
        }
        if (data.Media is { } mediaData)
        {
            DrawFrame(canvas, media, style, Hover(media, cursorX, cursorY), alpha);
            hits = hits with { MediaToggle = DrawMedia(canvas, media, mediaData, style, alpha) };
        }
        if (data.Focus is { } focusData)
        {
            DrawFrame(canvas, focus, style, Hover(focus, cursorX, cursorY), alpha);
            hits = hits with { FocusToggle = DrawFocus(canvas, focus, focusData, style, alpha) };
        }
        if (data.Stats is { } statsData)
        {
            DrawFrame(canvas, stats, style, Hover(stats, cursorX, cursorY), alpha);
            DrawStats(canvas, stats, statsData, style, alpha);
        }
        return hits;
    }

    private static double Hover(LayoutRect rect, double x, double y) =>
        rect.Contains(x, y) ? 1 : 0;

    private static void DrawFrame(SKCanvas canvas, LayoutRect rect, GlanceStyle style,
        double hover, double alpha) =>
        GlossyGlass.DrawCard(canvas, Sk(rect), GlossyGlass.CardRadius, style.Glass, alpha, hover);

    /// <summary>Location, temperature, condition and the condition glyph — the
    /// city names the card, the reading is the headline.</summary>
    private static void DrawWeather(SKCanvas canvas, LayoutRect rect, GlanceWeather data,
        GlanceStyle style, double alpha)
    {
        var w = (float)rect.Width;
        var h = (float)rect.Height;
        var pad = Pad(rect);
        var left = (float)rect.X + pad;
        var top = (float)rect.Y;

        if (data.Glyph.Length > 0)
        {
            // The condition mark is a real illustration in the design — a fifth
            // of the card wide, up in the top right corner.
            TextKit.DrawCentered(canvas, data.Glyph, (float)rect.X + w * 0.80f, top + h * 0.30f,
                w * 0.20f, Fade(style.Text, alpha), TextKit.Emoji);
        }

        var citySize = TypeSize(w, 0.074f);
        var city = TextKit.Ellipsize(data.City, w - pad - w * 0.34f, citySize, TextKit.Semibold);
        TextKit.Draw(canvas, city, left, Ink(top + h * 0.17f, citySize), citySize,
            Fade(style.Text, alpha), TextKit.Semibold);

        if (data.Temp.Length > 0)
        {
            var tempSize = TypeSize(w, 0.21f);
            var inkTop = top + h * 0.46f;
            var temp = TextKit.Ellipsize(data.Temp, w - pad * 2, tempSize, TextKit.Semibold);
            TextKit.Draw(canvas, temp, left, Ink(inkTop, tempSize), tempSize,
                Fade(style.Text, alpha), TextKit.Semibold);
            var tempWidth = TextKit.Measure(temp, tempSize, TextKit.Semibold);
            var degreeSize = TypeSize(w, 0.125f);
            TextKit.Draw(canvas, "\u00B0", left + tempWidth + w * 0.008f, Ink(inkTop, degreeSize), degreeSize,
                Fade(style.Muted, alpha), TextKit.Semibold);
        }

        var conditionSize = TypeSize(w, 0.066f);
        var condition = TextKit.Ellipsize(data.Condition, w - pad * 2, conditionSize);
        TextKit.Draw(canvas, condition, left, Ink(top + h * 0.76f, conditionSize), conditionSize, Fade(style.Muted, alpha));
    }

    /// <summary>The transport control, the artwork and the track, with the
    /// elapsed / total readout under it. The play-pause glyph is the card's live
    /// control, so it sits where the design puts it.</summary>
    private static LayoutRect DrawMedia(SKCanvas canvas, LayoutRect rect, GlanceMedia data,
        GlanceStyle style, double alpha)
    {
        var w = (float)rect.Width;
        var h = (float)rect.Height;
        var pad = Pad(rect);
        var left = (float)rect.X + pad;
        var top = (float)rect.Y;

        var glyphCx = (float)rect.X + w * 0.15f;
        var glyphCy = top + h * 0.19f;
        if (data.Glyph.Length > 0)
        {
            ShellGlyph(canvas, data.Glyph, glyphCx, glyphCy, w * 0.15f, Fade(style.Text, alpha), alpha);
        }

        var artSize = w * 0.30f;
        var art = new SKRect(left, top + h * 0.435f, left + artSize, top + h * 0.435f + artSize);
        if (data.Art is { } artwork)
        {
            canvas.Save();
            using (var clip = new SKPath())
            {
                clip.AddRoundRect(art, w * 0.05f, w * 0.05f);
                canvas.ClipPath(clip, SKClipOperation.Intersect, true);
            }
            using var paint = new SKPaint { IsAntialias = true, Color = Fade(Col.White, alpha).ToSk() };
            canvas.DrawBitmap(artwork, art, paint);
            canvas.Restore();
        }
        else
        {
            using var plate = new SKPaint { IsAntialias = true, Color = Fade(style.Hairline, alpha * 0.9).ToSk() };
            canvas.DrawRoundRect(art, w * 0.05f, w * 0.05f, plate);
            ShellGlyph(canvas, "\uE8D6", art.MidX, art.MidY, artSize * 0.42f, Fade(style.Muted, alpha), alpha);
        }

        var toggleSize = w * 0.18f;
        var toggle = new LayoutRect(glyphCx - toggleSize / 2, glyphCy - toggleSize / 2, toggleSize, toggleSize);

        var textLeft = art.Right + w * 0.05f;
        var textWidth = (float)rect.Right - w * RightPadFraction - textLeft;
        if (textWidth < 24) return toggle;

        var titleSize = TypeSize(w, 0.072f);
        TextKit.Draw(canvas, TextKit.Ellipsize(data.Title, textWidth, titleSize, TextKit.Semibold),
            textLeft, Ink(top + h * 0.47f, titleSize), titleSize, Fade(style.Text, alpha), TextKit.Semibold);
        var artistSize = TypeSize(w, 0.072f);
        TextKit.Draw(canvas, TextKit.Ellipsize(data.Artist, textWidth, artistSize),
            textLeft, Ink(top + h * 0.62f, artistSize), artistSize, Fade(style.Muted, alpha));

        if (data.Total.Length > 0)
        {
            var readoutSize = TypeSize(w, 0.064f);
            var readout = data.Elapsed.Length > 0 ? $"{data.Elapsed} / {data.Total}" : data.Total;
            TextKit.Draw(canvas, TextKit.Ellipsize(readout, textWidth, readoutSize, TextKit.Monospace),
                textLeft, Ink(top + h * 0.76f, readoutSize), readoutSize, Fade(style.Muted, alpha), TextKit.Monospace);
        }
        return toggle;
    }

    /// <summary>The phase, the countdown and the progress ring, which is the
    /// card's live control (pause while it runs, resume while it is held).</summary>
    private static LayoutRect DrawFocus(SKCanvas canvas, LayoutRect rect, GlanceFocus data,
        GlanceStyle style, double alpha)
    {
        var w = (float)rect.Width;
        var h = (float)rect.Height;
        var pad = Pad(rect);
        var top = (float)rect.Y;
        var radius = w * 0.145f;
        var thickness = Math.Max(2f, w * 0.018f);
        var cx = (float)rect.X + w * 0.28f;
        var cy = top + h * 0.49f;

        // The track is a wash of the card's own text colour rather than the
        // hairline: on the dark glass the hairline is invisible, and the design
        // shows the un-run part of the ring as a soft grey circle.
        GlossyGlass.DrawRing(canvas, cx, cy, radius, thickness, data.Progress01,
            data.Active ? data.Accent : style.Muted, style.Text.WithAlpha(40), alpha);

        if (data.Glyph.Length > 0)
        {
            ShellGlyph(canvas, data.Glyph, cx, cy, w * 0.085f,
                Fade(data.Active ? style.Text : style.Muted, alpha), alpha);
        }

        var textLeft = (float)rect.X + w * 0.54f;
        var textWidth = Math.Max(20, (float)rect.Right - w * RightPadFraction - textLeft);
        var labelSize = TypeSize(w, 0.071f);
        TextKit.Draw(canvas, TextKit.Ellipsize(data.Label, textWidth, labelSize, TextKit.Semibold),
            textLeft, Ink(top + h * 0.294f, labelSize), labelSize,
            Fade(data.Active ? style.Text : style.Muted, alpha), TextKit.Semibold);

        if (data.Time.Length > 0)
        {
            var timeSize = TypeSize(w, 0.115f);
            TextKit.Draw(canvas, TextKit.Ellipsize(data.Time, textWidth, timeSize, TextKit.Semibold),
                textLeft, Ink(top + h * 0.547f, timeSize), timeSize,
                Fade(data.Active ? style.Text : style.Muted, alpha), TextKit.Semibold);
        }

        var target = radius + w * 0.07f;
        return new LayoutRect(cx - target, cy - target, target * 2, target * 2);
    }

    /// <summary>The metric rows on the left and the rolling line on the right.</summary>
    private static void DrawStats(SKCanvas canvas, LayoutRect rect, GlanceStats data,
        GlanceStyle style, double alpha)
    {
        var w = (float)rect.Width;
        var h = (float)rect.Height;
        var pad = Pad(rect);
        var left = (float)rect.X + pad;
        var top = (float)rect.Y;

        var titleSize = TypeSize(w, 0.070f);
        TextKit.Draw(canvas, TextKit.Ellipsize(data.Title, w - pad - w * RightPadFraction, titleSize, TextKit.Semibold),
            left, Ink(top + h * 0.196f, titleSize), titleSize, Fade(style.Text, alpha), TextKit.Semibold);

        var chartWidth = w * 0.40f;
        var chart = new SKRect(
            (float)rect.Right - pad - chartWidth,
            top + h * 0.45f,
            (float)rect.Right - pad,
            top + h * 0.78f);

        var labelSize = TypeSize(w, 0.066f);
        var rows = new[] { (data.PrimaryLabel, data.PrimaryValue), (data.SecondaryLabel, data.SecondaryValue) };
        var rowInkTop = top + h * 0.464f;
        var valueRight = chart.Left - w * 0.04f;
        foreach (var (label, value) in rows)
        {
            if (label.Length == 0) continue;
            TextKit.Draw(canvas, label, left, Ink(rowInkTop, labelSize), labelSize,
                Fade(style.Muted, alpha), TextKit.Monospace);
            if (value.Length > 0)
            {
                var valueWidth = TextKit.Measure(value, labelSize, TextKit.Monospace);
                var valueLeft = Math.Max(left + w * 0.20f, valueRight - valueWidth);
                TextKit.Draw(canvas, value, valueLeft, Ink(rowInkTop, labelSize), labelSize,
                    Fade(style.Text, alpha), TextKit.Monospace);
            }
            rowInkTop += h * 0.234f;
        }

        var points = Sparkline.Normalize(data.History, Math.Max(2, (int)Math.Round(chart.Width / 3)));
        GlossyGlass.DrawSparkline(canvas, chart, points, style.Accent, alpha);
    }

    /// <summary>A type size from the design's own fraction of the card width,
    /// screened for the face actually in use.</summary>
    private static float TypeSize(float width, float fraction) =>
        width * fraction * FaceWidthFactor;

    /// <summary>The inset the design keeps to the first column of text, in card
    /// units — one number now that every other metric is a fraction.</summary>
    private static float Pad(LayoutRect rect) => (float)rect.Width * PadFraction;

    /// <summary>The top of a line's text box for ink that should begin at
    /// <paramref name="inkTop"/> — see <see cref="InkOffset"/>.</summary>
    private static float Ink(float inkTop, float size) => inkTop - size * InkOffset;

    private static SKRect Sk(LayoutRect rect) =>
        new((float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom);

    private static Col Fade(Col color, double alpha) =>
        color.WithAlpha((byte)Math.Clamp(Math.Round(color.A * alpha), 0, 255));

    private static void ShellGlyph(SKCanvas canvas, string glyph, float cx, float cy, float size,
        Col color, double alpha)
    {
        var face = TextKit.Icons;
        // Without the icon face the glyph draws as a blank box; fall back to the
        // UI face so the control still reads as a control.
        if (face is null || !TextKit.IconFaceResolved)
        {
            TextKit.DrawCentered(canvas, glyph, cx, cy, size * 0.9f, Fade(color, alpha), TextKit.Semibold);
            return;
        }
        TextKit.DrawCentered(canvas, glyph, cx, cy, size, Fade(color, alpha), face);
    }
}
