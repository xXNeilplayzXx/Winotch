namespace Winotch;

public enum FocusTimerStatus { Stopped, Running, Paused }
public enum FocusTimerPhase { Focus, Break }    /// <summary>Pure focus-timer state machine: 25/5, 50/10 or custom 1..180 min
    /// sessions with pause/resume/skip/stop and optional auto-cycle. Every phase
    /// transition reports a completion through the callback, so the UI can toast
    /// "focus complete" and "break over" identically whether the timer ended the
    /// phase naturally, was skipped, or stopped. Pausing freezes remaining at the
    /// pause instant; resuming rebases the phase start so paused time is not
    /// counted.</summary>
public sealed record FocusTimerState(
    FocusTimerStatus Status,
    FocusTimerPhase Phase,
    TimeSpan FocusDuration,
    TimeSpan BreakDuration,
    bool AutoCycle,
    DateTimeOffset PhaseStartedAtUtc,
    TimeSpan PausedElapsed,
    DateTimeOffset? PausedAtUtc,
    int CompletedFocusCycles)
{
    public static FocusTimerState Stopped { get; } = new(
        FocusTimerStatus.Stopped, FocusTimerPhase.Focus,
        TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), false,
        DateTimeOffset.UnixEpoch, TimeSpan.Zero, null, 0);

    public static FocusTimerState Start(FocusTimerSettings settings, DateTimeOffset now) =>
        settings.IsValid
            ? new FocusTimerState(FocusTimerStatus.Running, FocusTimerPhase.Focus,
                settings.FocusDuration, settings.BreakDuration, settings.AutoCycle,
                now.ToUniversalTime(), TimeSpan.Zero, null, 0)
            : Stopped;

    public bool IsActive => Status is FocusTimerStatus.Running or FocusTimerStatus.Paused;

    private TimeSpan PhaseDuration => Phase == FocusTimerPhase.Focus ? FocusDuration : BreakDuration;

    public TimeSpan RemainingAt(DateTimeOffset now) =>
        Status == FocusTimerStatus.Stopped ? TimeSpan.Zero : PhaseDuration - ElapsedAt(now);

    public double ProgressAt(DateTimeOffset now)
    {
        if (Status == FocusTimerStatus.Stopped || PhaseDuration <= TimeSpan.Zero) return 0;
        return Math.Clamp(ElapsedAt(now).TotalSeconds / PhaseDuration.TotalSeconds, 0, 1);
    }

    public string PhaseLabel => Phase == FocusTimerPhase.Focus ? "Focus" : "Break";

    /// <summary>Advance through every completed phase; each transition reports
    /// its completion (focus finished -> break, break finished -> focus or
    /// stop).</summary>
    public FocusTimerState AdvanceTo(DateTimeOffset now, Action<FocusTimerCompletion> onComplete)
    {
        if (Status != FocusTimerStatus.Running) return this;
        var state = this;
        var utcNow = now.ToUniversalTime();
        var guard = 0;
        while (state.Status == FocusTimerStatus.Running && state.RemainingAt(utcNow) <= TimeSpan.Zero)
        {
            var (next, completion) = state.NextPhaseWithCompletion();
            onComplete(completion);
            state = next;
            if (++guard > 128) break;
        }
        return state;
    }

    public FocusTimerState Pause(DateTimeOffset now)
    {
        if (Status != FocusTimerStatus.Running) return this;
        var advanced = AdvanceTo(now, _ => { });
        if (advanced.Status != FocusTimerStatus.Running) return advanced;
        return advanced with
        {
            Status = FocusTimerStatus.Paused,
            PausedElapsed = advanced.ElapsedAt(now),
            PausedAtUtc = now.ToUniversalTime(),
        };
    }

    /// <summary>Resume by rebasing the phase start so the time spent paused is
    /// not counted; the pre-pause elapsed stays baked into the new start.</summary>
    public FocusTimerState Resume(DateTimeOffset now)
    {
        if (Status != FocusTimerStatus.Paused) return this;
        return this with
        {
            Status = FocusTimerStatus.Running,
            PhaseStartedAtUtc = now.ToUniversalTime() - PausedElapsed,
            PausedElapsed = TimeSpan.Zero,
            PausedAtUtc = null,
        };
    }

    public FocusTimerState Skip(DateTimeOffset now, Action<FocusTimerCompletion> onComplete)
    {
        if (!IsActive) return this;
        var advanced = Status == FocusTimerStatus.Running
            ? AdvanceTo(now, onComplete)
            : Resume(now).AdvanceTo(now, onComplete);
        if (!advanced.IsActive) return advanced;
        var (next, completion) = advanced.NextPhaseWithCompletion();
        onComplete(completion);
        return next;
    }

    public FocusTimerState Stop() => Stopped;

    private (FocusTimerState State, FocusTimerCompletion Completion) NextPhaseWithCompletion()
    {
        if (Phase == FocusTimerPhase.Focus)
        {
            var cycles = CompletedFocusCycles + 1;
            var next = this with
            {
                Phase = FocusTimerPhase.Break,
                Status = FocusTimerStatus.Running,
                PhaseStartedAtUtc = DateTimeOffset.UtcNow,
                PausedElapsed = TimeSpan.Zero,
                PausedAtUtc = null,
                CompletedFocusCycles = cycles,
            };
            return (next, new FocusTimerCompletion(FocusTimerCompletionKind.FocusComplete, BreakDuration, cycles));
        }

        if (!AutoCycle)
        {
            return (Stopped, new FocusTimerCompletion(FocusTimerCompletionKind.BreakComplete, FocusDuration, CompletedFocusCycles));
        }

        var resumed = this with
        {
            Phase = FocusTimerPhase.Focus,
            Status = FocusTimerStatus.Running,
            PhaseStartedAtUtc = DateTimeOffset.UtcNow,
            PausedElapsed = TimeSpan.Zero,
            PausedAtUtc = null,
        };
        return (resumed, new FocusTimerCompletion(FocusTimerCompletionKind.BreakComplete, FocusDuration, CompletedFocusCycles));
    }

    private TimeSpan ElapsedAt(DateTimeOffset now)
    {
        // While paused, elapsed is frozen at the value captured when pause was
        // pressed; while running, the paused duration has been folded into the
        // phase start, so plain clock math is correct.
        if (Status == FocusTimerStatus.Paused && PausedAtUtc is not null) return PausedElapsed;
        return Positive(now.ToUniversalTime() - PhaseStartedAtUtc.ToUniversalTime());
    }

    private static TimeSpan Positive(TimeSpan value) => value <= TimeSpan.Zero ? TimeSpan.Zero : value;
}

public readonly record struct FocusTimerSettings(
    TimeSpan FocusDuration, TimeSpan BreakDuration, bool AutoCycle)
{
    public bool IsValid =>
        FocusDuration >= TimeSpan.FromMinutes(1) && FocusDuration <= TimeSpan.FromMinutes(180) &&
        BreakDuration >= TimeSpan.FromMinutes(1) && BreakDuration <= TimeSpan.FromMinutes(60);
}

public readonly record struct FocusTimerCompletion(
    FocusTimerCompletionKind Kind, TimeSpan NextPhaseDuration, int CompletedFocusCycles);

public enum FocusTimerCompletionKind { FocusComplete, BreakComplete }
/// <summary>Whether transient media and weather feedback should be suppressed
/// for the currently running focus phase. Paused sessions and breaks stay
/// unsuppressed so the user does not miss feedback while not actively focusing.</summary>
public static class FocusFeedbackPolicy
{
    public static bool ShouldSuppress(bool quietDuringFocus, IEnumerable<FocusTimerState> sessions) =>
        quietDuringFocus && sessions.Any(session =>
            session.Status == FocusTimerStatus.Running && session.Phase == FocusTimerPhase.Focus);
}
