using System.IO;
using SkiaSharp;

namespace Winotch.Engine;

/// <summary>Where the icon face came from.</summary>
public enum IconFontSource { None, Installed, FontFile }

/// <summary>The face that draws the icons, and where it was found.</summary>
public readonly record struct IconFontPick(
    SKTypeface Face, string? Source, IconFontSource Kind, string? FilePath = null)
{
    /// <summary>False when nothing suitable was found and the glyphs will be
    /// blank — the one case worth telling the user about.</summary>
    public bool Resolved => Kind != IconFontSource.None;
}

/// <summary>Choosing the face that draws the icons.
///
/// Every icon here is a private-use codepoint (U+E7xx), and the system never
/// font-substitutes a private-use character: if the face that covers the glyph
/// is not the one used for the draw, the glyph does not appear at all. So a
/// wrong choice is *invisible* rather than obviously wrong, and there is more
/// than one way to get it wrong:
///
/// - the family may simply not exist (Windows 10 has no Segoe Fluent Icons), and
///   a font manager answers a missing family with a substituted default face
///   rather than failing, so asking for it by name "succeeds" and draws nothing;
/// - the family may be installed but hidden from font enumeration — Windows'
///   "hide fonts based on language settings", a rebuilt or damaged font cache,
///   a trimmed Windows image — in which case matching by name fails while the
///   font *file* on disk is perfectly usable.
///
/// So candidates are probed by capability — can this face draw the glyph it is
/// about to be asked for? — and the font file is a candidate in its own right.</summary>
public static class IconFont
{
    /// <summary>The pill's icon block: U+E706 is the brightness glyph, and
    /// everything else the icon face draws lives in the same block.</summary>
    public const int ProbeGlyph = 0xE706;

    /// <summary>The families Windows draws these icons with, best first, and the
    /// font file each one ships in.</summary>
    public static readonly (string Family, string File)[] Candidates =
    {
        ("Segoe Fluent Icons", "SegoeIcons.ttf"), // Windows 11
        ("Segoe MDL2 Assets", "segmdl2.ttf"),     // Windows 10
        ("Segoe UI Symbol", "seguisym.ttf"),      // last resort
    };

    public static SKTypeface? OpenInstalled(string family)
    {
        try
        {
            return SKTypeface.FromFamilyName(family, SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        }
        catch
        {
            return null;
        }
    }

    public static SKTypeface? OpenFile(string path)
    {
        try
        {
            return File.Exists(path) ? SKTypeface.FromFile(path) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The first candidate that can actually draw the probe glyph. The
    /// capability test is what makes the search safe: a substituted default, a
    /// missing family and a wrong-by-name font file are all skipped for the same
    /// reason — they would draw a blank.</summary>
    public static IconFontPick Choose(
        IEnumerable<(string Label, Func<SKTypeface?> Open, IconFontSource Kind, string? FilePath)> probes,
        int probeGlyph = ProbeGlyph)
    {
        foreach (var probe in probes)
        {
            SKTypeface? face = null;
            try { face = probe.Open(); } catch { face = null; }
            if (face is null || !FontFallback.CanDraw(face.GetGlyph(probeGlyph))) continue;
            return new IconFontPick(face, probe.Label, probe.Kind, probe.FilePath);
        }
        // Nothing could draw it. The default face keeps layout sane; the icons
        // are blank either way, which is why Resolved exists.
        return new IconFontPick(SKTypeface.Default, null, IconFontSource.None);
    }

    /// <summary>The candidates for this machine: each family as installed, then
    /// the same font read straight from the Windows font directory, then any
    /// other file there that looks like an icon font (a Windows build that ships
    /// it under another name still gets picked up).</summary>
    public static IconFontPick Resolve()
    {
        var probes = new List<(string, Func<SKTypeface?>, IconFontSource, string?)>();
        foreach (var (family, file) in Candidates)
        {
            probes.Add((family, () => OpenInstalled(family), IconFontSource.Installed, null));
            if (FontsPath(file) is { } path)
            {
                probes.Add((file, () => OpenFile(path), IconFontSource.FontFile, path));
            }
        }
        foreach (var path in ScanFonts())
        {
            var candidate = path;
            probes.Add((Path.GetFileName(candidate), () => OpenFile(candidate),
                IconFontSource.FontFile, candidate));
        }
        return Choose(probes);
    }

    private static string? FontsPath(string file) =>
        FontsDirectory is { } dir ? Path.Combine(dir, file) : null;

    private static string? FontsDirectory
    {
        get
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(windows)) return null;
            var dir = Path.Combine(windows, "Fonts");
            return Directory.Exists(dir) ? dir : null;
        }
    }

    /// <summary>Icon-looking font files in the font directory, so the search is
    /// not tied to the file names this build happens to know.</summary>
    private static IEnumerable<string> ScanFonts()
    {
        if (FontsDirectory is not { } dir) return Array.Empty<string>();
        try
        {
            return Directory.EnumerateFiles(dir, "*.ttf")
                .Where(path =>
                {
                    var name = Path.GetFileName(path);
                    if (Candidates.Any(c => string.Equals(c.File, name, StringComparison.OrdinalIgnoreCase)))
                        return false;
                    return name.Contains("segoe", StringComparison.OrdinalIgnoreCase) &&
                        (name.Contains("icon", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("mdl2", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("sym", StringComparison.OrdinalIgnoreCase));
                })
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
