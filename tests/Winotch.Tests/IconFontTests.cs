using SkiaSharp;
using Winotch.Engine;

namespace Winotch.Tests;

public class IconFontTests
{
    /// <summary>What a substituted family hands back: a face that opens fine and
    /// has no private-use glyphs.</summary>
    private static Func<SKTypeface?> TextFace() => () => SKTypeface.FromFamilyName("Arial");

    private static (string, Func<SKTypeface?>, IconFontSource, string?)[] Probes(
        params (string Label, Func<SKTypeface?> Open, IconFontSource Kind, string? FilePath)[] probes) => probes;

    [Fact]
    public void Choose_TakesTheFirstCandidateThatCanDrawTheGlyph()
    {
        var picked = IconFont.Choose(Probes(
            ("First", () => null, IconFontSource.Installed, null),
            ("Second", TextFace(), IconFontSource.FontFile, "/fonts/second.ttf")),
            probeGlyph: 'A');

        Assert.True(picked.Resolved);
        Assert.Equal("Second", picked.Source);
        Assert.Equal(IconFontSource.FontFile, picked.Kind);
        Assert.Equal("/fonts/second.ttf", picked.FilePath);
    }

    [Fact]
    public void Choose_SkipsAFaceThatCannotDrawTheIconGlyph()
    {
        // This is the failure the capability test exists for: opening a face
        // proves nothing, and a face that cannot draw U+E706 will draw nothing
        // at all rather than falling back to another font.
        var picked = IconFont.Choose(Probes(
            ("Substituted", TextFace(), IconFontSource.Installed, null)));

        Assert.False(picked.Resolved);
        Assert.Equal(IconFontSource.None, picked.Kind);
        Assert.Null(picked.Source);
        Assert.NotNull(picked.Face);
    }

    [Fact]
    public void Choose_SurvivesAProbeThatThrows()
    {
        var picked = IconFont.Choose(Probes(
            ("Broken", () => throw new IOException("font file vanished"), IconFontSource.FontFile, "/gone.ttf"),
            ("Good", TextFace(), IconFontSource.Installed, null)),
            probeGlyph: 'A');

        Assert.Equal("Good", picked.Source);
    }

    [Fact]
    public void Candidates_NameTheFileEachIconFontShipsIn()
    {
        // The file is the route that still works when the family is installed but
        // hidden from font enumeration, so every candidate has to name one.
        Assert.Equal("Segoe Fluent Icons", IconFont.Candidates[0].Family);
        Assert.Equal("SegoeIcons.ttf", IconFont.Candidates[0].File);
        Assert.Contains(IconFont.Candidates, c => c.Family == "Segoe MDL2 Assets");
        Assert.All(IconFont.Candidates, c => Assert.EndsWith(".ttf", c.File));
    }

    [Fact]
    public void Resolve_ReturnsAUsableFaceOrSaysItCouldNot()
    {
        // On a machine with no Windows font directory this has to come back as
        // the unresolved answer rather than a crash or a silently substituted
        // face — the unresolved answer is what the settings window reports.
        var picked = IconFont.Resolve();

        Assert.NotNull(picked.Face);
        if (!picked.Resolved) Assert.Equal(IconFontSource.None, picked.Kind);
    }
}
