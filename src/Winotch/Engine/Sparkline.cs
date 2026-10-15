namespace Winotch.Engine;

/// <summary>Pure math behind the system-stats sparkline: a rolling window of
/// samples and the scaling that turns it into a line the card can draw.
///
/// The scaling is the interesting part. A raw 0..100 axis would flatten a
/// healthy machine's CPU line into the bottom pixel row of the card, so the
/// window is stretched around its own mid — and when the window is nearly flat
/// (an idle CPU barely moving), the band keeps a minimum span so the line sits
/// mid-card and reads as "steady" rather than as a bug. No Skia here: the deck
/// turns the 0..1 numbers into points.</summary>
public static class Sparkline
{
    /// <summary>Samples kept for the line — enough to read a few minutes of
    /// history at the battery/stats poll cadence.</summary>
    public const int Window = 48;

    /// <summary>Least value range the band covers, so a steady reading is
    /// drawn as a straight line through the middle of the card.</summary>
    public const double MinSpan = 16;

    /// <summary>Push a sample onto the history, dropping the oldest past the
    /// window. Values are percentages, so they are clamped to 0..100.</summary>
    public static void Append(List<double> history, double value, int max = Window)
    {
        history.Add(Math.Clamp(value, 0, 100));
        if (history.Count > max) history.RemoveRange(0, history.Count - max);
    }

    /// <summary><paramref name="count"/> points for the line, oldest first,
    /// scaled into 0..1 where 1 is the top of the band. An empty history gives a
    /// flat line at the bottom; a history shorter than the window is left-padded
    /// with its own first sample so the line always spans the full width instead
    /// of starting halfway across the card.</summary>
    public static double[] Normalize(IReadOnlyList<double> values, int count, double minSpan = MinSpan)
    {
        var length = Math.Max(1, count);
        var points = new double[length];
        if (values.Count == 0) return points;

        var start = Math.Max(0, values.Count - length);
        var available = values.Count - start;
        var pad = length - available;

        var low = double.MaxValue;
        var high = double.MinValue;
        for (var i = start; i < values.Count; i++)
        {
            low = Math.Min(low, values[i]);
            high = Math.Max(high, values[i]);
        }

        var span = high - low;
        if (span < minSpan)
        {
            var mid = (high + low) / 2;
            low = mid - minSpan / 2;
            high = mid + minSpan / 2;
            span = minSpan;
        }

        for (var i = 0; i < length; i++)
        {
            var value = i < pad ? values[start] : values[start + i - pad];
            points[i] = Math.Clamp((value - low) / span, 0, 1);
        }
        return points;
    }
}
