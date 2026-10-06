using Winotch.Engine;

namespace Winotch.Tests;

public class PillRowTests
{
    [Fact]
    public void ContentSpan_GapsOnlyBetweenChips()
    {
        Assert.Equal(0, PillRow.ContentSpan(Array.Empty<double>()));
        Assert.Equal(10, PillRow.ContentSpan(new[] { 10.0 }), 3);
        // One gap for two chips, two for three — never a trailing gap, which
        // was the only slack the row used to have.
        Assert.Equal(168, PillRow.ContentSpan(new[] { 10.0, 150.0 }), 3);
        Assert.Equal(10 + 8 + 150 + 8 + 40, PillRow.ContentSpan(new[] { 10.0, 150.0, 40.0 }), 3);
    }

    [Fact]
    public void Span_AddsTheMarginAtBothEnds()
    {
        Assert.Equal(28, PillRow.Span(Array.Empty<double>()), 3);
        Assert.Equal(38, PillRow.Span(new[] { 10.0 }), 3);
        Assert.Equal(196, PillRow.Span(new[] { 10.0, 150.0 }), 3);
    }

    [Fact]
    public void Span_LeavesBothEndsClearOnceThePillIsThatWide()
    {
        double[] chips = { 10, 150 };
        var span = PillRow.Span(chips);
        var content = PillRow.ContentSpan(chips);
        var start = PillRow.Start(0, span, content);

        // The chip at each end of the row sits a full margin inside the pill.
        Assert.Equal(PillRow.SidePadding, start, 3);
        Assert.Equal(PillRow.SidePadding, span - (start + content), 3);
    }

    [Fact]
    public void Start_CentresTheRowAndOverflowsSymmetrically()
    {
        // Roomier than needed: the surplus is split evenly.
        Assert.Equal(20, PillRow.Start(0, 240, 200), 3);
        // Narrower than the chips need (a screen-clamped pill, or one still
        // springing open): the row overflows both ends, never only one.
        Assert.Equal(-5, PillRow.Start(0, 190, 200), 3);
    }

    [Fact]
    public void Margin_ShrinksWhenThePillCannotAffordIt()
    {
        Assert.Equal(0, PillRow.Margin(100, 200), 3);
        Assert.Equal(0, PillRow.Margin(200, 200), 3);
        Assert.Equal(10, PillRow.Margin(220, 200), 3);
        // Never more than the designed margin, however wide the pill is.
        Assert.Equal(PillRow.SidePadding, PillRow.Margin(400, 200), 3);
    }
}
