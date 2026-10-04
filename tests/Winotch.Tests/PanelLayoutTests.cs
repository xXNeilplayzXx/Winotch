using Winotch;

namespace Winotch.Tests;

/// <summary>PanelLayout.SplitColumns balances the expanded panel's two-column
/// mode (Horizontal open direction): visible cards in persisted order are
/// split so column heights stay even, order preserved, left column first.</summary>
public class PanelLayoutTests
{
    private static PanelLayout.PanelCard Card(string id, double height) => new(id, height);

    [Fact]
    public void EmptyList_ReturnsTwoEmptyColumns()
    {
        var result = PanelLayout.SplitColumns(Array.Empty<PanelLayout.PanelCard>());
        Assert.Equal(2, result.Count);
        Assert.All(result, c => Assert.Empty(c));
    }

    [Fact]
    public void SingleCard_GoesInLeftColumn()
    {
        var result = PanelLayout.SplitColumns(new[] { Card("clock", 88) });
        Assert.Single(result[0]);
        Assert.Equal("clock", result[0][0].Id);
        Assert.Empty(result[1]);
    }

    [Fact]
    public void ZeroHeightCards_AreExcluded()
    {
        var result = PanelLayout.SplitColumns(new[] { Card("clock", 88), Card("media", 0), Card("notes", 104) });
        Assert.Equal(new[] { "clock", "notes" }, result.SelectMany(c => c).Select(c => c.Id));
    }

    [Fact]
    public void BalancedSplit_EvenHeights_AlternateColumns()
    {
        var cards = new[] { Card("a", 100), Card("b", 100), Card("c", 100), Card("d", 100) };
        var result = PanelLayout.SplitColumns(cards);
        Assert.Equal(new[] { "a", "b" }, result[0].Select(c => c.Id));
        Assert.Equal(new[] { "c", "d" }, result[1].Select(c => c.Id));
    }

    [Fact]
    public void BalancedSplit_TallCardMovesToSecondColumn()
    {
        // Total 400, half 200: "a" (150) fits column 0; "b" (250) pushes the
        // accumulator past half, so b/c/d land in column 1.
        var cards = new[] { Card("a", 150), Card("b", 250), Card("c", 60), Card("d", 40) };
        var result = PanelLayout.SplitColumns(cards);
        Assert.Equal(new[] { "a" }, result[0].Select(c => c.Id));
        Assert.Equal(new[] { "b", "c", "d" }, result[1].Select(c => c.Id));
    }

    [Fact]
    public void Order_IsPreservedWithinColumns()
    {
        var cards = new[] { Card("z", 50), Card("m", 50), Card("a", 50), Card("q", 50) };
        var result = PanelLayout.SplitColumns(cards);
        Assert.Equal(new[] { "z", "m" }, result[0].Select(c => c.Id));
        Assert.Equal(new[] { "a", "q" }, result[1].Select(c => c.Id));
    }

    [Fact]
    public void SingleColumnRequest_ReturnsOneColumn()
    {
        var cards = new[] { Card("a", 100), Card("b", 100) };
        var result = PanelLayout.SplitColumns(cards, columns: 1);
        Assert.Single(result);
        Assert.Equal(new[] { "a", "b" }, result[0].Select(c => c.Id));
    }

    [Fact]
    public void RealisticWidgetMix_SplitsIntoBalancedColumns()
    {
        // clock 88, system 62, media 112, notes 104 → total 366, half 183.
        // clock+system = 150 ≤ 183 stays left; media+notes fill the right.
        var cards = new[] { Card("clock", 88), Card("system", 62), Card("media", 112), Card("notes", 104) };
        var result = PanelLayout.SplitColumns(cards);
        Assert.Equal(new[] { "clock", "system" }, result[0].Select(c => c.Id));
        Assert.Equal(new[] { "media", "notes" }, result[1].Select(c => c.Id));
        var left = result[0].Sum(c => c.Height);
        var right = result[1].Sum(c => c.Height);
        Assert.True(Math.Abs(left - right) <= 130, $"columns wildly unbalanced: {left} vs {right}");
    }

    [Fact]
    public void ClipboardEmptyCardHeight_IsUnscaledAndMatchesTheDrawConstants()
    {
        // The empty clipboard card draws its hint at top+44 with 15px line
        // steps and a 12px bottom pad. The size factor is applied once, by
        // WidgetCardHeight — this used to multiply it here too, so the empty
        // card scaled by f² and looked cramped at Compact and huge at Large.
        Assert.Equal(44 + 0 * 15 + 12, PanelLayout.ClipboardEmptyCardHeight(0));
        Assert.Equal(44 + 1 * 15 + 12, PanelLayout.ClipboardEmptyCardHeight(1));
        Assert.Equal(44 + 3 * 15 + 12, PanelLayout.ClipboardEmptyCardHeight(3));

        var unscaled = PanelLayout.ClipboardEmptyCardHeight(3);
        Assert.Equal(unscaled * 0.85, unscaled * WidgetScale.Compact.Factor(), 6);
        Assert.Equal(unscaled * 1.15, unscaled * WidgetScale.Large.Factor(), 6);
        // A squared factor would land here instead — the regression guard.
        Assert.NotEqual(unscaled * 0.85 * 0.85, unscaled * WidgetScale.Compact.Factor(), 6);
    }
}
