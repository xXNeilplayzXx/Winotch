namespace Winotch.Tests;

public class FocusTimerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static FocusTimerState Running(double startedSecondsAgo, int focusMin = 25, int breakMin = 5, bool autoCycle = false) =>
        FocusTimerState.Start(new FocusTimerSettings(
                TimeSpan.FromMinutes(focusMin), TimeSpan.FromMinutes(breakMin), autoCycle), Now)
            .WithBackdatedStart(startedSecondsAgo);

    [Fact]
    public void Start_ValidSettings_RunningFocusPhase()
    {
        var state = FocusTimerState.Start(
            new FocusTimerSettings(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), false), Now);
        Assert.Equal(FocusTimerStatus.Running, state.Status);
        Assert.Equal(FocusTimerPhase.Focus, state.Phase);
        Assert.Equal(TimeSpan.FromMinutes(25), state.RemainingAt(Now));
    }

    [Fact]
    public void Start_InvalidDuration_Stopped()
    {
        var state = FocusTimerState.Start(
            new FocusTimerSettings(TimeSpan.FromMinutes(0), TimeSpan.FromMinutes(5), false), Now);
        Assert.Equal(FocusTimerStatus.Stopped, state.Status);
    }

    [Fact]
    public void RemainingAt_TicksDown()
    {
        var state = Running(60);
        var remaining = state.RemainingAt(Now.AddSeconds(60));
        Assert.Equal(TimeSpan.FromMinutes(24), remaining);
    }

    [Fact]
    public void AdvanceTo_FocusElapsed_EntersBreakAndReportsCompletion()
    {
        var state = Running(25 * 60 + 1);
        var completions = new List<FocusTimerCompletion>();
        var next = state.AdvanceTo(Now.AddSeconds(25 * 60 + 1), completions.Add);
        Assert.Equal(FocusTimerPhase.Break, next.Phase);
        Assert.Equal(FocusTimerStatus.Running, next.Status);
        Assert.Equal(1, next.CompletedFocusCycles);
        Assert.Single(completions);
        Assert.Equal(FocusTimerCompletionKind.FocusComplete, completions[0].Kind);
    }

    [Fact]
    public void AdvanceTo_BreakElapsed_NoAutoCycle_Stops()
    {
        var state = Running(60, focusMin: 25, breakMin: 5).SwitchToBreak(5 * 60 + 1, cycles: 1);
        var completions = new List<FocusTimerCompletion>();
        var next = state.AdvanceTo(Now.AddSeconds(5 * 60 + 1), completions.Add);
        Assert.Equal(FocusTimerStatus.Stopped, next.Status);
        Assert.Equal(FocusTimerCompletionKind.BreakComplete, completions[^1].Kind);
    }

    [Fact]
    public void AdvanceTo_BreakElapsed_WithAutoCycle_ReturnsToFocus()
    {
        var state = Running(60, focusMin: 25, breakMin: 5, autoCycle: true).SwitchToBreak(5 * 60 + 1, cycles: 1);
        var next = state.AdvanceTo(Now.AddSeconds(5 * 60 + 1), _ => { });
        Assert.Equal(FocusTimerPhase.Focus, next.Phase);
        Assert.Equal(FocusTimerStatus.Running, next.Status);
        Assert.Equal(1, next.CompletedFocusCycles);
    }

    [Fact]
    public void Pause_FreezesRemaining()
    {
        var state = Running(60);
        var paused = state.Pause(Now.AddSeconds(60));
        Assert.Equal(FocusTimerStatus.Paused, paused.Status);
        var frozen = paused.RemainingAt(Now.AddSeconds(120));
        Assert.Equal(TimeSpan.FromMinutes(24), frozen);
    }

    [Fact]
    public void Resume_ContinuesFromPause()
    {
        var state = Running(60).Pause(Now.AddSeconds(60));
        var resumed = state.Resume(Now.AddSeconds(100));
        Assert.Equal(FocusTimerStatus.Running, resumed.Status);
        var after = resumed.RemainingAt(Now.AddSeconds(160));
        Assert.Equal(TimeSpan.FromMinutes(24), after); // 60s before pause + 60s after resume
    }

    [Fact]
    public void Skip_DuringFocus_AdvancesToBreak()
    {
        var state = Running(0);
        var next = state.Skip(Now, _ => { });
        Assert.Equal(FocusTimerPhase.Break, next.Phase);
        Assert.Equal(FocusTimerStatus.Running, next.Status);
    }

    [Fact]
    public void ProgressAt_Clamped()
    {
        var state = Running(10);
        Assert.InRange(state.ProgressAt(Now), 0, 1);
        Assert.Equal(0, FocusTimerState.Stopped.ProgressAt(Now));
    }
}

internal static class FocusTimerTestExtensions
{
    public static FocusTimerState WithBackdatedStart(this FocusTimerState state, double secondsAgo) =>
        state with { PhaseStartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-secondsAgo) };

    public static FocusTimerState SwitchToBreak(this FocusTimerState state, double breakSecondsAgo, int cycles) =>
        state with
        {
            Phase = FocusTimerPhase.Break,
            PhaseStartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-breakSecondsAgo),
            CompletedFocusCycles = cycles,
        };
}
