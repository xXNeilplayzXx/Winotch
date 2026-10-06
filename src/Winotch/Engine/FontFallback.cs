namespace Winotch.Engine;

/// <summary>Choosing a font family by name when the platform substitutes
/// instead of failing.
///
/// <c>SKTypeface.FromFamilyName</c> does not return null for a family that is
/// not installed — the font manager hands back its default face instead. That
/// makes "did I get a typeface back?" useless as an existence test, and the
/// picker used exactly that test: it accepted the substituted default as the
/// first candidate every time, so the icon face became the platform default on
/// every machine without Segoe Fluent Icons (all of Windows 10) and the Segoe
/// MDL2 Assets entry beneath it was never reached. The default face has no
/// private-use glyphs, so every icon the pill draws with the symbol face —
/// volume, brightness, transport, charging, the call phone — came out blank.</summary>
public static class FontFallback
{
    /// <summary>True when a resolved family name really is the family that was
    /// asked for. A style-suffixed name ("Segoe UI Semibold" for "Segoe UI") is
    /// accepted, because that is the same family; a substituted default
    /// ("Segoe UI" for "Segoe Fluent Icons", "Helvetica" on a Mac) is not.</summary>
    public static bool Matches(string requested, string? resolved) =>
        !string.IsNullOrWhiteSpace(requested) &&
        resolved is not null &&
        (string.Equals(resolved, requested, StringComparison.OrdinalIgnoreCase) ||
         resolved.StartsWith(requested + " ", StringComparison.OrdinalIgnoreCase));

    /// <summary>The first candidate the platform actually has, in order, given a
    /// resolver that reports the family name a request resolved to. Null when it
    /// recognised none of them — the caller then has only its last resort.</summary>
    public static string? FirstPresent(IReadOnlyList<string> candidates, Func<string, string?> resolve)
    {
        foreach (var candidate in candidates)
        {
            if (Matches(candidate, resolve(candidate))) return candidate;
        }
        return null;
    }

    /// <summary>True when a face can draw the glyph it is about to be asked for.
    /// A family being present is not the same as it covering the codepoint, and
    /// a zero glyph id draws as a blank box — which is the failure this whole
    /// type exists to avoid.</summary>
    public static bool CanDraw(int glyphId) => glyphId != 0;
}
