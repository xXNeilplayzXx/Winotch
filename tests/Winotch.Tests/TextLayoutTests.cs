using Winotch;

namespace Winotch.Tests;

/// <summary>TextLayout.Wrap is the greedy word-wrap behind the widgets'
/// empty-state hints: lines must stay within the measured budget, words are
/// never split mid-word, and degenerate inputs (empty text, zero budget)
/// stay sane.</summary>
public class TextLayoutTests
{
    // Fixed-width fake measure: every character (spaces included) is 10 units.
    private static float Measure(string s) => s.Length * 10f;

    [Fact]
    public void TextWithinBudget_StaysOnOneLine()
    {
        var lines = TextLayout.Wrap("hello world", 200f, Measure);
        var line = Assert.Single(lines);
        Assert.Equal("hello world", line);
    }

    [Fact]
    public void ExactFit_StaysOnOneLine()
    {
        // "hello world" = 11 chars = 110 units — exactly the budget fits.
        var lines = TextLayout.Wrap("hello world", 110f, Measure);
        Assert.Equal(new[] { "hello world" }, lines);
    }

    [Fact]
    public void LongText_WrapsAtWordBoundaries()
    {
        // "the quick brown" = 15 chars fits 150; adding " fox" (19 chars) overflows.
        var lines = TextLayout.Wrap("the quick brown fox", 150f, Measure);
        Assert.Equal(new[] { "the quick brown", "fox" }, lines);
    }

    [Fact]
    public void MultipleSpaces_Collapse()
    {
        var lines = TextLayout.Wrap("a   b", 200f, Measure);
        Assert.Equal(new[] { "a b" }, lines);
    }

    [Fact]
    public void OversizedWord_GetsItsOwnLine()
    {
        // A single word longer than the budget is never split mid-word.
        var lines = TextLayout.Wrap("supercalifragilistic tiny", 50f, Measure);
        Assert.Equal(new[] { "supercalifragilistic", "tiny" }, lines);
    }

    [Fact]
    public void EmptyAndWhitespace_YieldNoLines()
    {
        Assert.Empty(TextLayout.Wrap("", 100f, Measure));
        Assert.Empty(TextLayout.Wrap("   ", 100f, Measure));
    }

    [Fact]
    public void DegenerateBudget_ReturnsSingleLine()
    {
        var lines = TextLayout.Wrap("keep it", 0f, Measure);
        Assert.Equal(new[] { "keep it" }, lines);
    }
}
