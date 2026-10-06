using System.IO;
using System.Windows;
using System.Windows.Media;
using Winotch.Engine;

namespace Winotch;

/// <summary>The WPF half of the icon face.
///
/// The settings window draws its own glyphs — the gear, the fold arrows, the
/// checkbox tick, the combo-box arrow — and they have to come from the same font
/// the pill uses, or a machine where the family cannot be matched by name ends
/// up with icons missing in one renderer instead of both. Naming the face as a
/// file-backed FontFamily is what lets it work when the font is installed but
/// hidden from font enumeration.</summary>
public static class IconFontWpf
{
    /// <summary>The application resource every glyph in XAML refers to.</summary>
    public const string ResourceKey = "NotchIconFont";

    /// <summary>Fallback spelled out for any code path that runs before the
    /// resource is replaced (and for the XAML designer).</summary>
    public const string FallbackFamilies = "Segoe Fluent Icons, Segoe MDL2 Assets";

    public static void Apply()
    {
        if (Application.Current is { } app) app.Resources[ResourceKey] = Resolve();
    }

    /// <summary>The best icon family this machine can offer.</summary>
    public static FontFamily Resolve()
    {
        // A file the app has to name itself, because the family could not be
        // matched: WPF picks the face after the '#' out of that file.
        if (TextKit.IconFontFile is { } file && TextKit.IconFamilyName is { } inside)
        {
            var directory = Path.GetDirectoryName(file) ?? "";
            try
            {
                return new FontFamily(
                    new Uri(directory + Path.DirectorySeparatorChar, UriKind.Absolute),
                    $"./{Path.GetFileName(file)}#{inside}");
            }
            catch (ArgumentException)
            {
                // A path WPF refuses; the fallback list below still applies.
            }
        }
        if (TextKit.IconInstalledFamily is { } installed) return new FontFamily(installed);
        if (TextKit.IconFamilyName is { } name) return new FontFamily(name);
        return new FontFamily(FallbackFamilies);
    }

    /// <summary>The face the icons are actually coming from, for the settings
    /// footer. Null while everything is fine.</summary>
    public static string? Note() => TextKit.IconFaceResolved
        ? null
        : "Icons are unavailable: none of Segoe Fluent Icons, Segoe MDL2 Assets or " +
          "Segoe UI Symbol could be read. Check that the Segoe icon fonts exist in " +
          "C:\\Windows\\Fonts (and are not hidden under Fonts → \"Hide fonts based on " +
          "language settings\").";
}
