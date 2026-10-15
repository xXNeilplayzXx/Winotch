using Winotch;
using Winotch.Engine;

namespace Winotch.Tests;

/// <summary>GlanceLayout is the floating glass deck's geometry: the clock
/// capsule centered at the top with four cards framing it. These tests pin the
/// composition's invariants — the framing is symmetric, the cards clear the pill
/// by the design gap without ever overlapping it, and the deck degrades to "no
/// deck" (leaving the classic pill drawing) rather than squeezing itself onto a
/// canvas that cannot hold it.</summary>
public class GlanceLayoutTests
{
    /// <summary>A typical laptop canvas, and the sizes the composition has to
    /// hold on: a small 16:10 panel, a 1080p screen, a 1440p screen.</summary>
    public static TheoryData<double, double> Canvases => new()
    {
        { 1280, 800 },
        { 1366, 768 },
        { 1512, 982 },
        { 1920, 1080 },
        { 2560, 1440 },
        { 3840, 2160 },
    };

    private static GlanceDeck Deck(double width, double height)
    {
        var deck = GlanceLayout.Compute(width, height, GlanceLayout.TopGap(height));
        Assert.NotNull(deck);
        return deck!;
    }

    [Theory]
    [MemberData(nameof(Canvases))]
    public void Composition_FitsTheCanvasOnEveryScreenSize(double width, double height)
    {
        var deck = Deck(width, height);
        Assert.True(deck.Bounds.X >= 0, $"deck runs off the left edge: {deck.Bounds}");
        Assert.True(deck.Bounds.Right <= width, $"deck runs off the right edge: {deck.Bounds}");
        Assert.True(deck.Bounds.Bottom <= height, $"deck runs off the bottom edge: {deck.Bounds}");
    }

    [Theory]
    [MemberData(nameof(Canvases))]
    public void Cards_AreFramedSymmetricallyAroundTheCanvas(double width, double height)
    {
        var deck = Deck(width, height);
        var left = deck.Weather.Rect;
        var right = deck.Media.Rect;

        // The composition is centered on the screen, so the left card's inset
        // equals the right card's inset and the two rows line up.
        Assert.Equal(left.X, width - right.Right, 6);
        Assert.Equal(left.Y, right.Y, 6);
        Assert.Equal(deck.Focus.Rect.Y, deck.System.Rect.Y, 6);
    }

    [Theory]
    [MemberData(nameof(Canvases))]
    public void Cards_ClearThePillByTheDesignGap(double width, double height)
    {
        var deck = Deck(width, height);
        var gap = GlanceLayout.GapX(width);
        Assert.Equal(deck.Pill.Rect.X - deck.Weather.Rect.Right, gap, 6);
        Assert.Equal(deck.System.Rect.X - deck.Pill.Rect.Right, gap, 6);
    }

    [Theory]
    [MemberData(nameof(Canvases))]
    public void NoTwoBoxesOverlap(double width, double height)
    {
        var deck = Deck(width, height);
        var boxes = new List<GlanceBox> { deck.Pill };
        boxes.AddRange(deck.Cards);

        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                var a = boxes[i].Rect;
                var b = boxes[j].Rect;
                var overlaps = a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
                Assert.False(overlaps, $"{boxes[i].Slot} overlaps {boxes[j].Slot}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Canvases))]
    public void Capsule_KeepsTheDesignsProportions(double width, double height)
    {
        var deck = Deck(width, height);
        var pill = deck.Pill.Rect;

        Assert.Equal(width / 2, pill.CenterX, 6);
        Assert.Equal(GlanceLayout.PillAspect, pill.Width / pill.Height, 0.2);
        // The radius is half the height, so the capsule is a true capsule.
        Assert.True(pill.Height >= 60, $"capsule too short to hold the clock: {pill.Height}");
    }

    [Fact]
    public void CapsuleScalesWithTheCanvasUpToItsClamp()
    {
        // Below the clamps the capsule tracks the screen (39% of its width,
        // measured off the design) so the composition keeps its proportions; a
        // bigger canvas past the cap keeps the same capsule rather than growing
        // the clock to the size of the display.
        var small = Deck(1000, 700).Pill.Rect;
        var medium = Deck(1200, 800).Pill.Rect;
        var huge = Deck(3840, 2160).Pill.Rect;
        Assert.True(medium.Width > small.Width);
        Assert.True(medium.Height > small.Height);
        Assert.Equal(620, huge.Width, 6);
        Assert.Equal(620, Deck(1920, 1080).Pill.Rect.Width, 6);
        Assert.Equal(GlanceLayout.PillWidth(1200), medium.Width, 6);
    }

    [Fact]
    public void UpperCards_AreTallerThanTheLowerPair()
    {
        // The design's asymmetry: the weather and now-playing cards carry a
        // headline over a content block, the timer and stats cards a row each.
        var deck = Deck(1920, 1080);
        Assert.True(deck.Weather.Rect.Height > deck.Focus.Rect.Height);
        Assert.Equal(GlanceLayout.CardHeightUpper(1920), deck.Weather.Rect.Height, 6);
        Assert.Equal(GlanceLayout.CardHeightLower(1920), deck.Focus.Rect.Height, 6);
        Assert.Equal(deck.Weather.Rect.Height, deck.Media.Rect.Height, 6);
        Assert.Equal(deck.Focus.Rect.Height, deck.System.Rect.Height, 6);
    }

    [Theory]
    [InlineData(1000, 700)]
    [InlineData(940, 560)]
    public void SmallestSupportedCanvas_StillFramesTheDeck(double width, double height)
    {
        var deck = Deck(width, height);
        Assert.True(deck.Weather.Rect.Right < deck.Pill.Rect.X, "the left card must clear the capsule");
        Assert.True(deck.Pill.Rect.Right < deck.System.Rect.X, "the right card must clear the capsule");
        Assert.True(deck.Bounds.Bottom <= height);
    }

    [Fact]
    public void SmallCanvas_HasNoDeck()
    {
        Assert.Null(GlanceLayout.Compute(GlanceLayout.MinCanvasWidth - 1, 1080, 40));
        Assert.Null(GlanceLayout.Compute(1920, GlanceLayout.MinCanvasHeight - 1, 40));
    }

    [Fact]
    public void DockedToTheBottomEdge_RemovesTheDeckRatherThanOverlappingTheTaskbar()
    {
        // The user dragged the pill most of the way down the screen: the lower
        // row would land off the bottom, so the shell falls back to the pill.
        Assert.Null(GlanceLayout.Compute(1920, 1080, 700));
    }

    [Fact]
    public void Deck_TracksThePillWhenItIsDragged()
    {
        var rest = Deck(1920, 1080);
        var dragged = GlanceLayout.Compute(1920, 1080, GlanceLayout.TopGap(1080) + 40);
        Assert.NotNull(dragged);

        // Every box moves with the pill by exactly the drag offset — the cards
        // are framing it, not the screen.
        Assert.Equal(rest.Pill.Rect.Y + 40, dragged!.Pill.Rect.Y, 6);
        Assert.Equal(rest.Weather.Rect.Y + 40, dragged.Weather.Rect.Y, 6);
        Assert.Equal(rest.Focus.Rect.Y + 40, dragged.Focus.Rect.Y, 6);
    }

    [Fact]
    public void LowRow_SitsARowGapUnderTheUpperRow()
    {
        var deck = Deck(1920, 1080);
        Assert.Equal(
            deck.Weather.Rect.Bottom + GlanceLayout.RowGap(1920),
            deck.Focus.Rect.Y,
            6);
    }

    [Fact]
    public void Bounds_EncloseEveryBox()
    {
        var deck = Deck(1920, 1080);
        var bounds = deck.Bounds;
        foreach (var box in deck.Cards.Append(deck.Pill))
        {
            Assert.True(box.Rect.X >= bounds.X - 0.001 && box.Rect.Right <= bounds.Right + 0.001);
            Assert.True(box.Rect.Y >= bounds.Y - 0.001 && box.Rect.Bottom <= bounds.Bottom + 0.001);
        }
    }

    [Fact]
    public void Slots_ResolveThroughFor()
    {
        var deck = Deck(1920, 1080);
        Assert.Equal(deck.Weather.Rect, deck.For(GlanceSlot.Weather).Rect);
        Assert.Equal(deck.Media.Rect, deck.For(GlanceSlot.Media).Rect);
        Assert.Equal(deck.Focus.Rect, deck.For(GlanceSlot.Focus).Rect);
        Assert.Equal(deck.System.Rect, deck.For(GlanceSlot.System).Rect);
        Assert.Equal(deck.Pill.Rect, deck.For(GlanceSlot.Pill).Rect);
    }
}

/// <summary>The settings half of the layout profile: an unknown persisted value
/// must fall back to the classic pill rather than leaving every geometry branch
/// comparing against a value nothing matches.</summary>
public class ShellLayoutSettingsTests
{
    [Fact]
    public void Defaults_ToTheClassicPill()
    {
        var appearance = new AppearanceSettings();
        Assert.Equal(ShellLayoutMode.Classic, appearance.ShellLayout);
        Assert.False(appearance.GlossyGlass);
    }

    [Fact]
    public void UnknownPersistedLayout_NormalizesToClassic()
    {
        var appearance = new AppearanceSettings { ShellLayout = (ShellLayoutMode)99 };
        Assert.Equal(ShellLayoutMode.Classic, appearance.Normalize().ShellLayout);
    }

    [Fact]
    public void GlanceLayout_SurvivesNormalization()
    {
        var appearance = new AppearanceSettings
        {
            ShellLayout = ShellLayoutMode.Glance,
            GlossyGlass = true,
        }.Normalize();
        Assert.Equal(ShellLayoutMode.Glance, appearance.ShellLayout);
        Assert.True(appearance.GlossyGlass);
    }
}

/// <summary>The stats sparkline's scaling: a rolling window that reads as a
/// line rather than as a bug when the machine is idle.</summary>
public class SparklineTests
{
    [Fact]
    public void EmptyHistory_IsAFlatLineAtTheBottom()
    {
        var points = Sparkline.Normalize(Array.Empty<double>(), 8);
        Assert.Equal(8, points.Length);
        Assert.All(points, p => Assert.Equal(0, p));
    }

    [Fact]
    public void SteadyReading_SitsInTheMiddleOfTheBand()
    {
        // 14% on every sample: a flat axis would pin the line to the floor, so
        // the band keeps its minimum span and centers the reading instead.
        var points = Sparkline.Normalize(new double[] { 14, 14, 14, 14 }, 4);
        Assert.All(points, p => Assert.Equal(0.5, p, 3));
    }

    [Fact]
    public void RisingWindow_SpansTheFullBandHeight()
    {
        var points = Sparkline.Normalize(new double[] { 10, 20, 30, 40, 50 }, 5);
        Assert.Equal(0, points[0], 6);
        Assert.Equal(1, points[^1], 6);
        for (var i = 0; i < points.Length; i++)
        {
            Assert.True(points[i] >= 0 && points[i] <= 1);
            if (i > 0) Assert.True(points[i] > points[i - 1]);
        }
    }

    [Fact]
    public void ShorterHistory_IsLeftPaddedSoTheLineSpansTheCard()
    {
        var points = Sparkline.Normalize(new double[] { 20, 60 }, 6);
        Assert.Equal(6, points.Length);
        // The first four points hold the oldest sample, the last two are the
        // real readings — the line starts at the card's left edge either way.
        Assert.Equal(points[0], points[1], 6);
        Assert.Equal(points[0], points[2], 6);
        Assert.Equal(points[0], points[3], 6);
        Assert.True(points[5] > points[4]);
    }

    [Fact]
    public void LongHistory_UsesTheMostRecentSamples()
    {
        // Ninety-two flat 50% samples, then 0/10/20/30. The window is the last
        // four, whose own span clears MinSpan — so the band is 0..30 and the line
        // runs the full height. Both halves of that matter: a line drawing the
        // oldest samples would sit flat mid-band, because they are all 50s, and a
        // line scaled against the whole history instead of the window would
        // stretch 0..50 and land somewhere else entirely.
        var history = Enumerable.Repeat(50.0, 92).ToList();
        history.AddRange(new[] { 0.0, 10, 20, 30 });

        var points = Sparkline.Normalize(history, 4);

        Assert.Equal(0, points[0], 6);
        Assert.Equal(1.0 / 3, points[1], 6);
        Assert.Equal(2.0 / 3, points[2], 6);
        Assert.Equal(1, points[^1], 6);
    }

    [Fact]
    public void LongHistory_NarrowWindow_IsCentredRatherThanStretched()
    {
        // The same history, but consecutive samples 1 apart: the window (92..95)
        // is narrower than MinSpan, so the band is widened around its middle
        // instead of stretching a 3-wide window across the whole card. This is
        // what a raw window-relative scale would get wrong.
        var history = Enumerable.Range(0, 96).Select(i => (double)i).ToList();
        var points = Sparkline.Normalize(history, 4);

        var halfWindow = (95 - 92) / 2.0;
        var expectedLow = (Sparkline.MinSpan / 2 - halfWindow) / Sparkline.MinSpan;
        Assert.Equal(expectedLow, points[0], 6);
        Assert.Equal(1 - expectedLow, points[^1], 6);
        Assert.True(points[^1] - points[0] < 0.5,
            "a narrow window must not be stretched across the band");
    }

    [Fact]
    public void Append_ClampsToTheWindowAndDropsTheOldest()
    {
        var history = new List<double>();
        for (var i = 0; i < Sparkline.Window + 10; i++) Sparkline.Append(history, 50);
        Assert.Equal(Sparkline.Window, history.Count);

        history.Clear();
        Sparkline.Append(history, 140, max: 3);
        Sparkline.Append(history, -20, max: 3);
        Sparkline.Append(history, 30, max: 3);
        Sparkline.Append(history, 40, max: 3);
        Assert.Equal(new double[] { 0, 30, 40 }, history.ToArray());
    }
}
