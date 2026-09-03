namespace Winotch.Tests;

public class FocusTimerTests
{
    // Single fixed clock for the whole suite: every helper and assertion uses
    // this instant, so elapsed math is exact (the state machine only consults
    // the real clock inside phase *transitions*, which we assert by phase/status).
    private static readonly DateTimeOffset Now = TestClock.Now;

    private static FocusTimerState Started(int focusMin = 25, int breakMin = 5, bool autoCycle = false) =>
        FocusTimerState.Start(
            new FocusTimerSettings(TimeSpan.FromMinutes(focusMin), TimeSpan.FromMinutes(breakMin), autoCycle),
            Now);

    private static FocusTimerState Running(double startedSecondsAgo, int focusMin = 25, int breakMin = 5, bool autoCycle = false) =>
        Started(focusMin, breakMin, autoCycle).BackdateStart(startedSecondsAgo);

    [Fact]
    public void Start_ValidSettings_RunningFocusPhase()
    {
        var state = Started();
        Assert.Equal(FocusTimerStatus.Running, state.Status);
        Assert.Equal(FocusTimerPhase.Focus, state.Phase);
        Assert.Equal(TimeSpan.FromMinutes(25), state.RemainingAt(Now));
    }

    [Fact]
    public void Start_InvalidDuration_Stopped()
    {
        var state = FocusTimerState.Start(
            new FocusTimerSettings(TimeSpan.Zero, TimeSpan.FromMinutes(5), false), Now);
        Assert.Equal(FocusTimerStatus.Stopped, state.Status);
    }

    [Fact]
    public void RemainingAt_TicksDown()
    {
        var state = Running(60);
        Assert.Equal(TimeSpan.FromMinutes(24), state.RemainingAt(Now));
        Assert.Equal(TimeSpan.FromMinutes(23), state.RemainingAt(Now.AddMinutes(1)));
    }

    [Fact]
    public void AdvanceTo_FocusElapsed_EntersBreakAndReportsCompletion()
    {
        var state = Running(25 * 60 + 1);
        var completions = new List<FocusTimerCompletion>();
        var next = state.AdvanceTo(Now, completions.Add);
        Assert.Equal(FocusTimerPhase.Break, next.Phase);
        Assert.Equal(FocusTimerStatus.Running, next.Status);
        Assert.Equal(1, next.CompletedFocusCycles);
        Assert.Single(completions);
        Assert.Equal(FocusTimerCompletionKind.FocusComplete, completions[0].Kind);
    }

    [Fact]
    public void AdvanceTo_BreakElapsed_NoAutoCycle_Stops()
    {
        var state = Running(60).SwitchToBreak(5 * 60 + 1, cycles: 1);
        var completions = new List<FocusTimerCompletion>();
        var next = state.AdvanceTo(Now, completions.Add);
        Assert.Equal(FocusTimerStatus.Stopped, next.Status);
        Assert.Equal(FocusTimerCompletionKind.BreakComplete, completions[^1].Kind);
    }

    [Fact]
    public void AdvanceTo_BreakElapsed_WithAutoCycle_ReturnsToFocus()
    {
        var state = Running(60, autoCycle: true).SwitchToBreak(5 * 60 + 1, cycles: 1);
        var next = state.AdvanceTo(Now, _ => { });
        Assert.Equal(FocusTimerPhase.Focus, next.Phase);
        Assert.Equal(FocusTimerStatus.Running, next.Status);
        Assert.Equal(1, next.CompletedFocusCycles);
    }

    [Fact]
    public void Pause_FreezesRemaining()
    {
        var state = Running(60);
        var paused = state.Pause(Now);
        Assert.Equal(FocusTimerStatus.Paused, paused.Status);
        // Frozen at the pause instant: 24 minutes remain, forever.
        Assert.Equal(TimeSpan.FromMinutes(24), paused.RemainingAt(Now.AddMinutes(5)));
    }

    [Fact]
    public void Resume_ContinuesFromPause()
    {
        var state = Running(60).Pause(Now);
        var resumed = state.Resume(Now.AddSeconds(40));
        Assert.Equal(FocusTimerStatus.Running, resumed.Status);
        // 60s before the pause + 60s after resume = 2 minutes elapsed.
        Assert.Equal(TimeSpan.FromMinutes(23), resumed.RemainingAt(Now.AddSeconds(40 + 60)));
    }

    [Fact]
    public void Skip_DuringFocus_AdvancesToBreak()
    {
        var state = Running(0);
        var next = state.Skip(Now, _ => { });
        Assert.Equal(FocusTimerPhase.Break, next.Phase);
        Assert.Equal(FocusTimerStatus.Running, next.Status);
        Assert.Equal(1, next.CompletedFocusCycles);
    }

    [Fact]
    public void ProgressAt_Clamped()
    {
        var state = Running(10);
        Assert.InRange(state.ProgressAt(Now), 0, 1);
        Assert.Equal(0, FocusTimerState.Stopped.ProgressAt(Now));
    }

    [Fact]
    public void Stopped_IgnoresAdvance()
    {
        var next = FocusTimerState.Stopped.AdvanceTo(Now.AddMinutes(60), _ => { });
        Assert.Equal(FocusTimerStatus.Stopped, next.Status);
    }
}

internal static class FocusTimerTestExtensions
{
    public static FocusTimerState BackdateStart(this FocusTimerState state, double secondsAgo) =>
        state with { PhaseStartedAtUtc = TestClock.Now - TimeSpan.FromSeconds(secondsAgo) };

    public static FocusTimerState SwitchToBreak(this FocusTimerState state, double breakSecondsAgo, int cycles) =>
        state with
        {
            Phase = FocusTimerPhase.Break,
            PhaseStartedAtUtc = TestClock.Now - TimeSpan.FromSeconds(breakSecondsAgo),
            CompletedFocusCycles = cycles,
        };

}

internal static class TestClock
{
    // Captured once per run so helpers and assertions share one instant.
    public static readonly DateTimeOffset Now = DateTimeOffset.UtcNow.AddMinutes(-1);
}
