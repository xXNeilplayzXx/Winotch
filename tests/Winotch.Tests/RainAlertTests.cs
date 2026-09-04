using Winotch;

namespace Winotch.Tests;

/// <summary>The rain alert fires when precipitation starts within 30 minutes
/// (probability >= 40% or > 0.05 mm per 15-min bucket) and only once per
/// episode. Buckets are unix seconds; each covers [t, t+900).</summary>
public class RainAlertTests
{
    private static readonly long Base = 1_788_549_300; // arbitrary epoch second

    private static long[] Times(int from, int count) =>
        Enumerable.Range(from, count).Select(i => Base + i * 900L).ToArray();

    [Fact]
    public void RainStartingIn15Minutes_Fires()
    {
        // Dry now, rain in the next bucket (20 min out from now = Base-300).
        var times = Times(0, 4);
        var prob = new double[] { 0, 80, 90, 90 };
        var mm = new double[] { 0, 0.2, 0.4, 0.4 };
        var key = RainAlertEvaluator.Evaluate(times, prob, mm, Base - 300, null);
        Assert.NotNull(key);
        Assert.Equal($"rain-{Base + 900}", key);
    }

    [Fact]
    public void RainAtExactly30Minutes_Fires()
    {
        // Dry lead-in, rain starting exactly 30 minutes out (2 buckets).
        var times = Times(0, 4);
        var prob = new double[] { 0, 0, 60, 70 };
        var mm = new double[] { 0, 0, 0.1, 0.2 };
        var key = RainAlertEvaluator.Evaluate(times, prob, mm, Base, null);
        Assert.NotNull(key);
        Assert.Equal($"rain-{Base + 1800}", key);
    }

    [Fact]
    public void RainBeyond30Minutes_StayQuiet()
    {
        // First rainy bucket is 45+ minutes out — beyond the horizon.
        var times = Times(-1, 5);
        var prob = new double[] { 0, 0, 0, 0, 80 };
        var mm = new double[] { 0, 0, 0, 0, 0.2 };
        Assert.Null(RainAlertEvaluator.Evaluate(times, prob, mm, Base - 900, null));
    }

    [Fact]
    public void AlreadyRaining_NoAlert()
    {
        // The bucket covering "now" is wet — that is rain in progress.
        var times = Times(0, 3);
        var prob = new double[] { 70, 80, 0 };
        var mm = new double[] { 0.4, 0.2, 0 };
        Assert.Null(RainAlertEvaluator.Evaluate(times, prob, mm, Base + 450, null));
    }

    [Fact]
    public void SameEpisode_AlarmedOnce_ThenSilent()
    {
        var times = Times(0, 4);
        var prob = new double[] { 0, 60, 90, 90 };
        var mm = new double[] { 0, 0.1, 0.4, 0.4 };
        var now = Base - 300;
        var first = RainAlertEvaluator.Evaluate(times, prob, mm, now, null);
        Assert.NotNull(first);
        // Next probe (5 min later), same episode — must not re-fire.
        var second = RainAlertEvaluator.Evaluate(times, prob, mm, now + 300, first);
        Assert.Null(second);
        // And a third probe still inside the same episode window.
        var third = RainAlertEvaluator.Evaluate(times, prob, mm, now + 600, first);
        Assert.Null(third);
    }

    [Fact]
    public void AfterLongGap_NewEpisode_FiresAgain()
    {
        var times = Times(0, 4);
        var prob = new double[] { 0, 60, 90, 90 };
        var mm = new double[] { 0, 0.1, 0.4, 0.4 };
        var now = Base - 300;
        var first = RainAlertEvaluator.Evaluate(times, prob, mm, now, null);
        Assert.NotNull(first);
        // Hours later, a new episode: the previous alert's start is now more
        // than the re-alert gap behind the new episode's start (which begins
        // 30 minutes after this probe's "now").
        var times2 = Times(25, 4);
        var prob2 = new double[] { 0, 80, 90, 90 };
        var mm2 = new double[] { 0, 0.2, 0.5, 0.5 };
        var key2 = RainAlertEvaluator.Evaluate(times2, prob2, mm2, Base + 6 * 3600, first);
        Assert.NotNull(key2);
        Assert.Equal($"rain-{Base + 23400}", key2);
    }

    [Fact]
    public void RainJustAfterPreviousEpisodeStart_Suppressed()
    {
        // Previous alert fired for an episode starting at Base+900; a later
        // probe (now = Base+1000) sees rain 15 minutes after that start —
        // within both the horizon and the re-alert gap, so it is the same
        // shower and must stay silent even though the key differs.
        var times = Times(0, 4);
        var prob = new double[] { 0, 0, 80, 90 };
        var mm = new double[] { 0, 0, 0.2, 0.4 };
        Assert.Null(RainAlertEvaluator.Evaluate(
            times, prob, mm, Base + 1000, $"rain-{Base + 900}"));
    }

    [Fact]
    public void LowProbabilityAndDry_TrailingBucketsIgnored()
    {
        // Probability under threshold and no significant mm -> dry.
        var times = Times(0, 3);
        var prob = new double[] { 0, 0, 30 };
        var mm = new double[] { 0, 0, 0.02 };
        Assert.Null(RainAlertEvaluator.Evaluate(times, prob, mm, Base - 300, null));
    }

    [Fact]
    public void MmCountsEvenWithLowProbability()
    {
        // 20% probability but 0.3 mm — showers slipping under the % radar.
        var times = Times(0, 2);
        var prob = new double[] { 0, 20 };
        var mm = new double[] { 0, 0.3 };
        var key = RainAlertEvaluator.Evaluate(times, prob, mm, Base - 300, null);
        Assert.NotNull(key);
    }

    [Fact]
    public void EmptyArrays_Quiet()
    {
        Assert.Null(RainAlertEvaluator.Evaluate(
            Array.Empty<long>(), Array.Empty<double>(), Array.Empty<double>(), Base, null));
    }

    [Fact]
    public void AllBucketsInPast_Quiet()
    {
        // "now" is after every bucket — no forward window at all.
        var times = Times(-4, 3);
        var prob = new double[] { 0, 90, 90 };
        var mm = new double[] { 0, 0.3, 0.5 };
        Assert.Null(RainAlertEvaluator.Evaluate(times, prob, mm, Base + 3600, null));
    }
}
