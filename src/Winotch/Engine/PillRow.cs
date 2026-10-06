namespace Winotch.Engine;

/// <summary>The collapsed pill's row geometry: how much room a set of chips
/// needs, and how much of that room is breathing space.
///
/// The row used to be laid out flush against the pill's silhouette — the first
/// chip started at the pill's left edge, and the only slack was the 8px gap
/// printed *after* the last chip — so a chip drawn near its own edge (or, as the
/// focus chip did, half its width to the left of its centre) poked straight out
/// of the rounded end and was clipped. Whatever the pill's width works out to,
/// both ends now keep a real margin.</summary>
public static class PillRow
{
    /// <summary>Space between two chips.</summary>
    public const double Gap = 8;

    /// <summary>Clear space kept inside each end of the pill, so chips are never
    /// flush with the rounded edge — flush reads as cut off even when the pixels
    /// are inside the shape. It also gives the spring a little room to spare
    /// while the pill is still growing into a chip that just appeared.</summary>
    public const double SidePadding = 14;

    /// <summary>Width the chips take up on their own, the gaps between them
    /// included. A single chip has no gap; an empty row is zero wide.</summary>
    public static double ContentSpan(IEnumerable<double> chips)
    {
        var count = 0;
        var sum = 0.0;
        foreach (var width in chips)
        {
            count++;
            sum += width;
        }
        return count == 0 ? 0 : sum + Gap * (count - 1);
    }

    /// <summary>Width the pill needs to show those chips with their margins — the
    /// number the collapsed pill sizes itself to.</summary>
    public static double Span(IEnumerable<double> chips) => ContentSpan(chips) + SidePadding * 2;

    /// <summary>How much margin actually fits. A pill narrower than its chips
    /// need — one clamped by the screen, or still springing open into a chip that
    /// just appeared — gets proportionally less, and none once the chips fill it:
    /// the margin must never push content out of a pill that is already too
    /// small, or it would clip the very thing it exists to protect.</summary>
    public static double Margin(double available, double content) =>
        Math.Clamp((available - content) / 2, 0, SidePadding);

    /// <summary>Where the row starts, centring it in whatever width the pill
    /// actually has. Centring the chips (rather than laying them out from the
    /// left edge) is what turns <see cref="Span"/>'s margins into real space on
    /// both sides, and degrades safely when the pill is too narrow.</summary>
    public static double Start(double left, double available, double content) =>
        left + (available - content) / 2;
}
