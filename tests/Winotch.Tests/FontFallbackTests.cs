using SkiaSharp;
using Winotch.Engine;

namespace Winotch.Tests;

public class FontFallbackTests
{
    [Theory]
    [InlineData("Segoe UI", "Segoe UI")]
    [InlineData("Segoe UI", "segoe ui")]
    // A style suffix is the same family, resolved to a face.
    [InlineData("Segoe UI", "Segoe UI Semibold")]
    [InlineData("Cascadia Mono", "Cascadia Mono PL")]
    [InlineData("Segoe UI", "Segoe UI Variable Text Semibold")]
    public void Matches_AcceptsTheFamilyAndItsStyles(string requested, string resolved) =>
        Assert.True(FontFallback.Matches(requested, resolved));

    [Theory]
    // A different family is not a match, however plausible it looks — this is
    // the substitution the picker used to accept as the first candidate, which
    // is why the MDL2 entry below it was never reached on Windows 10.
    [InlineData("Segoe Fluent Icons", "Segoe MDL2 Assets")]
    [InlineData("Segoe Fluent Icons", "Segoe UI")]
    [InlineData("Segoe Fluent Icons", "Helvetica")] // what the substitute looks like
    [InlineData("Segoe Fluent Icons", null)]
    [InlineData("Segoe Fluent Icons", "")]
    [InlineData("", "Segoe UI")]
    [InlineData(" ", "Segoe UI")]
    public void Matches_RejectsASubstituteOrAnEmptyRequest(string requested, string? resolved) =>
        Assert.False(FontFallback.Matches(requested, resolved));

    [Fact]
    public void FirstPresent_SkipsTheSubstitutedCandidateAndTakesTheNextRealOne()
    {
        // The bug this type exists for: the manager answers every request — the
        // missing family included — with its default face, so the first
        // candidate was always accepted and the fallback was never reached.
        var resolved = new Dictionary<string, string>
        {
            ["Segoe Fluent Icons"] = "Helvetica", // substituted
            ["Segoe MDL2 Assets"] = "Segoe MDL2 Assets", // really installed
        };

        var chosen = FontFallback.FirstPresent(
            new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets", "Segoe UI Symbol" },
            family => resolved.TryGetValue(family, out var name) ? name : "Helvetica");

        Assert.Equal("Segoe MDL2 Assets", chosen);
    }

    [Fact]
    public void FirstPresent_IsNullWhenNothingIsInstalled()
    {
        // Everything substituted: there is no honest answer but "none of these",
        // and the caller then has to use its own last resort rather than
        // silently drawing blanks with a default face.
        var chosen = FontFallback.FirstPresent(
            new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" },
            _ => "Helvetica");

        Assert.Null(chosen);
    }

    [Fact]
    public void CanDraw_OnlyForARealGlyph()
    {
        Assert.True(FontFallback.CanDraw(42));
        Assert.False(FontFallback.CanDraw(0));
    }

    [Fact]
    public void TheFontManagerSubstitutesRatherThanFailing()
    {
        // The assumption every rule above rests on. If a future SkiaSharp
        // returns null (or a matching family name) for a missing family, this
        // test says so and the rules above can be simplified.
        using var face = SKTypeface.FromFamilyName("Winotch No Such Font 12345",
            SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

        Assert.NotNull(face);
        Assert.False(FontFallback.Matches("Winotch No Such Font 12345", face.FamilyName));
        // ...and the substitute cannot draw the icons the pill asks for.
        Assert.False(FontFallback.CanDraw(face.GetGlyph(0xE706)));
    }
}
