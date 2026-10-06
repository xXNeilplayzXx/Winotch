using Winotch.Engine;

namespace Winotch.Tests;

public class MediaChipTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Visible_WhilePlaying()
    {
        Assert.True(MediaChip.Visible(enabled: true, hasTrack: true, playing: true, null, T0));
    }

    [Fact]
    public void Visible_BrieflyAfterStopping_SoThePausedStateIsSeen()
    {
        Assert.True(MediaChip.Visible(true, true, playing: false, T0, T0.AddSeconds(1)));
        Assert.True(MediaChip.Visible(true, true, playing: false, T0,
            T0.AddSeconds(MediaChip.PauseGraceSeconds)));
        // Then the pill goes back to the clock rather than holding a stale track.
        Assert.False(MediaChip.Visible(true, true, playing: false, T0,
            T0.AddSeconds(MediaChip.PauseGraceSeconds + 0.5)));
        // A stop we never saw keeps nothing around.
        Assert.False(MediaChip.Visible(true, true, playing: false, null, T0));
    }

    [Fact]
    public void Visible_NeedsTheChipSwitchedOnAndATrack()
    {
        Assert.False(MediaChip.Visible(enabled: false, hasTrack: true, playing: true, null, T0));
        Assert.False(MediaChip.Visible(enabled: true, hasTrack: false, playing: true, null, T0));
    }

    [Fact]
    public void ShouldScroll_OnlyWhenPlayingAndTooWideForTheChip()
    {
        Assert.True(MediaChip.ShouldScroll(playing: true, titleWidth: 200, budget: 116));
        Assert.False(MediaChip.ShouldScroll(playing: true, titleWidth: 116, budget: 116));
        Assert.False(MediaChip.ShouldScroll(playing: true, titleWidth: 60, budget: 116));
        // A paused title sits still rather than scrolling under a stopped clock.
        Assert.False(MediaChip.ShouldScroll(playing: false, titleWidth: 200, budget: 116));
    }
}
