using SkiaSharp;

namespace Winotch.Engine;

/// <summary>Cached typefaces and text drawing helpers. Fonts come from the OS —
/// Segoe UI Variable on Windows 11, Segoe UI on Windows 10, the Fluent / MDL2
/// icon fonts for glyphs, Segoe UI Emoji for status icons. Nothing bundled.</summary>
public static class TextKit
{
    private static SKTypeface UiRegular = null!;
    private static SKTypeface UiSemibold = null!;
    private static SKTypeface UiBold = null!;
    private static string? _uiFamily;
    private static string? _uiFontFile;
    private static readonly object FontGate = new();
    private static readonly Dictionary<string, (SKTypeface Regular, SKTypeface Semibold, SKTypeface Bold)> FaceCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly AsyncLocal<(SKTypeface Regular, SKTypeface Semibold, SKTypeface Bold)?> ScopedFaces = new();

    static TextKit()
    {
        SetUiFamily("Segoe UI Variable Text");
    }

    // The icon face is resolved by capability rather than by name, and can be
    // loaded straight from its file — private-use glyphs are never substituted,
    // so a wrong choice here draws nothing at all (see IconFont).
    private static readonly IconFontPick IconFace = IconFont.Resolve();
    private static readonly SKTypeface Mono = Pick("Cascadia Mono", "Consolas", "Courier New");
    private static readonly SKTypeface UiEmoji = Pick("Segoe UI Emoji", "Segoe UI Symbol", "Arial");

    public static SKTypeface Regular => ScopedFaces.Value?.Regular ?? UiRegular;
    public static SKTypeface Semibold => ScopedFaces.Value?.Semibold ?? UiSemibold;
    public static SKTypeface Bold => ScopedFaces.Value?.Bold ?? UiBold;
    public static SKTypeface Icons => IconFace.Face;

    /// <summary>The icon face as installed, when that is how it was found, so the
    /// other renderer (WPF, for the settings window's own glyphs) can name it.</summary>
    public static string? IconInstalledFamily =>
        IconFace.Kind == IconFontSource.Installed ? IconFace.Source : null;

    /// <summary>The font file the icon face was loaded from, when the family
    /// could not be matched by name (hidden or unenumerated fonts).</summary>
    public static string? IconFontFile =>
        IconFace.Kind == IconFontSource.FontFile ? IconFace.FilePath : null;

    /// <summary>The family name inside the resolved face — what a file-based
    /// WPF FontFamily has to name after the '#' to get the right face.</summary>
    public static string? IconFamilyName => IconFace.Face.FamilyName;

    /// <summary>False when no face could draw the icons, so every glyph comes
    /// out blank — worth saying out loud rather than looking broken.</summary>
    public static bool IconFaceResolved => IconFace.Resolved;
    public static SKTypeface Monospace => Mono;
    public static SKTypeface Emoji => UiEmoji;

    /// <summary>Switch the UI faces — every non-mono, non-icon text draw — to
    /// an installed family (or an imported font file), falling back through
    /// the OS defaults when it is missing. Called at startup and when the
    /// Appearance font setting changes. Icons, emoji and the mono faces are
    /// intentionally fixed.</summary>
    public static void SetUiFamily(string family, string? fontFile = null)
    {
        lock (FontGate)
        {
            if (!string.Equals(_uiFamily, family, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_uiFontFile, fontFile, StringComparison.OrdinalIgnoreCase))
            {
                var faces = ResolveFacesCached(family, fontFile);
                UiRegular = faces.Regular;
                UiSemibold = faces.Semibold;
                UiBold = faces.Bold;
                _uiFamily = family;
                _uiFontFile = fontFile;
            }
        }
    }

    /// <summary>Temporarily use a monitor's selected font while painting its
    /// widget cards. Monitor profiles can have different fonts, so a card must
    /// not depend on the application-wide face alone.</summary>
    public static IDisposable UseFontFamily(string family, string? fontFile = null)
    {
        var previous = ScopedFaces.Value;
        lock (FontGate)
        {
            ScopedFaces.Value = ResolveFacesCached(family, fontFile);
        }
        return new FontScope(previous);
    }

    private sealed class FontScope((SKTypeface Regular, SKTypeface Semibold, SKTypeface Bold)? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ScopedFaces.Value = previous;
        }
    }

    private static (SKTypeface Regular, SKTypeface Semibold, SKTypeface Bold) ResolveFacesCached(
        string family, string? fontFile)
    {
        var key = $"{family}\0{fontFile}";
        if (FaceCache.TryGetValue(key, out var cached)) return cached;
        var faces = ResolveFaces(family, fontFile);
        FaceCache[key] = faces;
        return faces;
    }

    private static (SKTypeface Regular, SKTypeface Semibold, SKTypeface Bold) ResolveFaces(
        string family, string? fontFile)
    {
        if (!string.IsNullOrWhiteSpace(fontFile))
        {
            var fileFace = SKTypeface.FromFile(fontFile);
            if (fileFace is not null) return (fileFace, fileFace, fileFace);
        }
        return (
            Pick(family, "Segoe UI Variable Text", "Segoe UI"),
            Pick(family, "Segoe UI Variable Text", "Segoe UI", weight: SKFontStyleWeight.SemiBold),
            Pick(family, "Segoe UI Variable Text", "Segoe UI", weight: SKFontStyleWeight.Bold));
    }

    private static SKTypeface Pick(string primary, string fallback, string last,
        SKFontStyleWeight weight = SKFontStyleWeight.Normal, int probeGlyph = 'A')
    {
        foreach (var family in new[] { primary, fallback, last })
        {
            if (TryFamily(family, weight, probeGlyph) is { } face) return face;
        }
        return TryFamily("Arial", SKFontStyleWeight.Normal, probeGlyph) ?? SKTypeface.Default;
    }

    /// <summary>Resolve one family, rejecting the face the font manager hands
    /// back for a family that is not installed (so the caller tries the next
    /// candidate) and any face that cannot draw the glyph it is about to be
    /// asked for. Both rejections matter: without them the first candidate
    /// always "wins", even when it does not exist.</summary>
    private static SKTypeface? TryFamily(string family, SKFontStyleWeight weight, int probeGlyph)
    {
        var face = SKTypeface.FromFamilyName(family, weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        if (face is null || !FontFallback.Matches(family, face.FamilyName)) return null;
        return FontFallback.CanDraw(face.GetGlyph(probeGlyph)) ? face : null;
    }

    public static float Measure(string text, float size, SKTypeface? face = null)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        using var paint = NewPaint(size, face ?? Regular, Col.White);
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
        using var paint = NewPaint(size, face ?? Regular, color);
        var baseline = y - paint.FontMetrics.Ascent;
        canvas.DrawText(text, x, baseline, paint);
    }

    /// <summary>Draw text left-aligned on x with its line centred on centerY —
    /// the vertical twin of DrawCentered, for stacked lines. Draw takes the
    /// line's top instead, which would need per-font guessing to line two
    /// stacked lines up.</summary>
    public static void DrawLeftCentered(SKCanvas canvas, string text, float x, float centerY,
        float size, Col color, SKTypeface? face = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        using var paint = NewPaint(size, face ?? Regular, color);
        var metrics = paint.FontMetrics;
        var baseline = centerY - (metrics.Ascent + metrics.Descent) / 2;
        canvas.DrawText(text, x, baseline, paint);
    }

    /// <summary>Draw text centered horizontally on centerX and vertically on
    /// centerY (cap height centered, not full line box).</summary>
    public static void DrawCentered(SKCanvas canvas, string text, float centerX, float centerY,
        float size, Col color, SKTypeface? face = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        using var paint = NewPaint(size, face ?? Regular, color);
        var metrics = paint.FontMetrics;
        var width = paint.MeasureText(text);
        var baseline = centerY - (metrics.Ascent + metrics.Descent) / 2;
        canvas.DrawText(text, centerX - width / 2, baseline, paint);
    }

    /// <summary>Draw text word-wrapped to maxWidth, starting at the top-left
    /// (x, y) with the given spacing between lines; returns the number of
    /// lines drawn. Wrapping is greedy word-wrap (see TextLayout.Wrap).</summary>
    public static int DrawWrapped(SKCanvas canvas, string text, float x, float y,
        float maxWidth, float size, Col color, SKTypeface? face = null, float lineHeight = 15f)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var lines = TextLayout.Wrap(text, maxWidth, s => Measure(s, size, face));
        var cursor = y;
        foreach (var line in lines)
        {
            Draw(canvas, line, x, cursor, size, color, face);
            cursor += lineHeight;
        }
        return lines.Count;
    }

    public static float LineHeight(float size, SKTypeface? face = null)
    {
        using var paint = NewPaint(size, face ?? Regular, Col.White);
        var metrics = paint.FontMetrics;
        return metrics.Descent - metrics.Ascent;
    }

    /// <summary>Shorten text to fit maxWidth, appending an ellipsis. Lives here
    /// rather than in the shell's UI helpers so a composition can fit its own
    /// strings without dragging in the app palette.</summary>
    public static string Ellipsize(string text, float maxWidth, float size, SKTypeface? face = null)
    {
        if (Measure(text, size, face) <= maxWidth) return text;
        const string ellipsis = "\u2026";
        var ellipsisWidth = Measure(ellipsis, size, face);
        var budget = Math.Max(1, maxWidth - ellipsisWidth);
        for (var len = text.Length - 1; len > 0; len--)
        {
            var candidate = text[..len];
            if (Measure(candidate, size, face) <= budget)
            {
                return candidate.TrimEnd() + ellipsis;
            }
        }
        return ellipsis;
    }
}
