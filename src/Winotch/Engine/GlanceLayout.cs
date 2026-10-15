namespace Winotch.Engine;

/// <summary>A rectangle in DIP layout space. Deliberately not an SKRect: the
/// glance deck's geometry is pure layout math (see <see cref="GlanceLayout"/>),
/// so it stays unit-testable and free of any drawing dependency — the shell
/// converts at the draw call.</summary>
public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(double x, double y) =>
        x >= X && x <= Right && y >= Y && y <= Bottom;

    /// <summary>The same rect grown on every side — the hit-tolerance and
    /// scrim box the deck needs.</summary>
    public LayoutRect Grown(double amount) =>
        new(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);
}

/// <summary>The five regions of the glance deck: the central clock capsule and
/// the four cards that frame it.</summary>
public enum GlanceSlot { Pill, Weather, Media, Focus, System }

public readonly record struct GlanceBox(GlanceSlot Slot, LayoutRect Rect);

/// <summary>A resolved glance deck — where the pill and its four cards sit for
/// one frame's canvas. Only ever produced by <see cref="GlanceLayout"/>, and
/// only when the canvas is large enough to hold the composition.</summary>
public sealed record GlanceDeck(
    GlanceBox Pill,
    GlanceBox Weather,
    GlanceBox Media,
    GlanceBox Focus,
    GlanceBox System)
{
    public GlanceBox For(GlanceSlot slot) => slot switch
    {
        GlanceSlot.Weather => Weather,
        GlanceSlot.Media => Media,
        GlanceSlot.Focus => Focus,
        GlanceSlot.System => System,
        _ => Pill,
    };

    /// <summary>The four cards, in the order the mockup frames them.</summary>
    public IReadOnlyList<GlanceBox> Cards => [Weather, Media, Focus, System];

    /// <summary>The box that encloses the whole composition — what the soft
    /// backdrop scrim is painted into.</summary>
    public LayoutRect Bounds
    {
        get
        {
            var left = Math.Min(Pill.Rect.X, Math.Min(Weather.Rect.X, Focus.Rect.X));
            var right = Math.Max(Pill.Rect.Right, Math.Max(Media.Rect.Right, System.Rect.Right));
            var top = Math.Min(Pill.Rect.Y, Math.Min(Weather.Rect.Y, Media.Rect.Y));
            var bottom = Math.Max(Focus.Rect.Bottom, System.Rect.Bottom);
            return new LayoutRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }
    }
}

/// <summary>Where the glance layout puts everything.
///
/// The proportions come from the design: the clock is a wide capsule (about
/// 3.6:1 with the radius pinned at half its height, so it is always a true
/// capsule), the four cards are 1.45:1 and clear the pill by <see cref="GapX"/>,
/// the upper pair sits level with the pill's band and the lower pair hangs a row
/// gap below it. Sizes track the canvas between explicit clamps, so the deck
/// keeps its proportions from a laptop panel up to a 4K display instead of
/// shrinking away or overflowing.
///
/// Everything here is pure: no Skia, no settings, no time. That is what keeps
/// the composition testable — the shell only decides *whether* to show the deck
/// and feeds it live content.</summary>
public static class GlanceLayout
{
    /// <summary>The narrowest canvas the full composition fits on. Below it the
    /// shell stays with the classic pill rather than crowding a small screen.</summary>
    public const double MinCanvasWidth = 940;
    public const double MinCanvasHeight = 560;

    /// <summary>Space above the capsule — the design's breathing room under the
    /// top edge (and under a physical notch).</summary>
    public const double TopGapMin = 18;
    public const double TopGapMax = 60;

    /// <summary>Space between a card and the capsule beside it, as a fraction of
    /// the capsule's own width — the design's framing gap scales with the pill,
    /// so the composition keeps its spacing rather than spreading apart.</summary>
    public const double GapFraction = 0.12;
    public const double GapXMin = 24;
    public const double GapXMax = 72;

    /// <summary>Space between the upper and lower card rows, as a share of the
    /// capsule's height — the design separates them by half a capsule, which is
    /// what makes the two rows read as a frame rather than as a grid.</summary>
    public const double RowGapFraction = 0.52;
    public const double RowGapMin = 28;
    public const double RowGapMax = 96;

    /// <summary>The capsule's width:height ratio from the design.</summary>
    public const double PillAspect = 3.64;

    /// <summary>Card width:height ratios from the design. The upper pair is
    /// plainly taller than the lower pair — a weather card carries a headline
    /// over two stacked lines plus its illustration, a now-playing card an
    /// artwork block, while the timer and stats cards only need a row each — and
    /// four equal cards lose the composition's asymmetry.</summary>
    public const double CardAspectUpper = 1.37;
    public const double CardAspectLower = 1.74;

    /// <summary>Card widths are canvas-relative with a floor, so a card is never
    /// too small to read the type inside it — the deck simply stops framing on a
    /// canvas below <see cref="MinCanvasWidth"/>.</summary>
    public const double CardWidthMin = 190;
    public const double CardWidthMax = 320;

    /// <summary>Least margin a card may keep from the screen edge.</summary>
    public const double EdgeMargin = 14;

    /// <summary>How far the upper cards drop below the pill's top, as a share of
    /// the canvas — the pill reads as the anchor and the cards frame it slightly
    /// lower, by about a sixth of the capsule's own height.</summary>
    public const double UpperBiasFraction = 0.019;
    public const double UpperBiasMin = 6;
    public const double UpperBiasMax = 36;

    /// <summary>Clear space the composition keeps above the bottom edge.</summary>
    public const double BottomMargin = 16;

    /// <summary>The capsule takes a little over a third of the canvas, measured
    /// off the design (1075 of its 2752 pixels).</summary>
    public static double PillWidth(double canvasWidth) =>
        Math.Clamp(canvasWidth * 0.39, 340, 620);

    public static double PillHeight(double canvasWidth) =>
        Math.Round(PillWidth(canvasWidth) / PillAspect);

    public static double TopGap(double canvasHeight) =>
        Math.Clamp(canvasHeight * 0.045, TopGapMin, TopGapMax);

    public static double GapX(double canvasWidth) =>
        Math.Clamp(PillWidth(canvasWidth) * GapFraction, GapXMin, GapXMax);

    public static double RowGap(double canvasWidth) =>
        Math.Clamp(PillHeight(canvasWidth) * RowGapFraction, RowGapMin, RowGapMax);

    public static double UpperBias(double canvasWidth) =>
        Math.Clamp(canvasWidth * UpperBiasFraction, UpperBiasMin, UpperBiasMax);

    public static double CardWidth(double canvasWidth) =>
        Math.Clamp(canvasWidth * 0.17, CardWidthMin, CardWidthMax);

    public static double CardHeightUpper(double canvasWidth) =>
        Math.Round(CardWidth(canvasWidth) / CardAspectUpper);

    public static double CardHeightLower(double canvasWidth) =>
        Math.Round(CardWidth(canvasWidth) / CardAspectLower);

    /// <summary>The capsule's resting rect — the collapsed pill's own target in
    /// glance mode, and the anchor the cards are placed from.</summary>
    public static LayoutRect PillRect(double canvasWidth, double canvasHeight, double pillTop)
    {
        var width = PillWidth(canvasWidth);
        var height = PillHeight(canvasWidth);
        return new LayoutRect((canvasWidth - width) / 2, pillTop, width, height);
    }

    /// <summary>The deck for a canvas, anchored on the pill's live top edge so
    /// the cards travel with a dragged pill. Null when the canvas is too small —
    /// or the pill has been dragged too low — for the composition to fit, which
    /// is the shell's cue to keep drawing the classic pill.</summary>
    public static GlanceDeck? Compute(double canvasWidth, double canvasHeight, double pillTop)
    {
        if (canvasWidth < MinCanvasWidth || canvasHeight < MinCanvasHeight) return null;

        var pillWidth = PillWidth(canvasWidth);
        var pillHeight = PillHeight(canvasWidth);
        var pillLeft = (canvasWidth - pillWidth) / 2;
        var pill = new LayoutRect(pillLeft, pillTop, pillWidth, pillHeight);

        // The cards clear the pill by the design gap, so their X comes off the
        // pill rather than off the screen edge — that is what makes the framing
        // look intentional instead of accidentally wide.
        var gapX = GapX(canvasWidth);
        var available = pillLeft - gapX - EdgeMargin;
        var cardWidth = Math.Min(CardWidth(canvasWidth), available);
        if (cardWidth < CardWidthMin) return null;
        var upperHeight = Math.Round(cardWidth / CardAspectUpper);
        var lowerHeight = Math.Round(cardWidth / CardAspectLower);

        var upperY = pillTop + UpperBias(canvasWidth);
        var lowerY = upperY + upperHeight + RowGap(canvasWidth);
        if (lowerY + lowerHeight > canvasHeight - BottomMargin) return null;
        if (upperY < 0) return null;

        var leftX = pillLeft - gapX - cardWidth;
        var rightX = pill.Right + gapX;
        if (leftX < EdgeMargin || rightX + cardWidth > canvasWidth - EdgeMargin) return null;

        return new GlanceDeck(
            new GlanceBox(GlanceSlot.Pill, pill),
            new GlanceBox(GlanceSlot.Weather, new LayoutRect(leftX, upperY, cardWidth, upperHeight)),
            new GlanceBox(GlanceSlot.Media, new LayoutRect(rightX, upperY, cardWidth, upperHeight)),
            new GlanceBox(GlanceSlot.Focus, new LayoutRect(leftX, lowerY, cardWidth, lowerHeight)),
            new GlanceBox(GlanceSlot.System, new LayoutRect(rightX, lowerY, cardWidth, lowerHeight)));
    }
}
