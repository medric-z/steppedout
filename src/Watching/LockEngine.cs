namespace SteppedOut;

enum Phase
{
    Watching,
    Countdown,
    Away,
}

enum EngineEvent
{
    None,
    CountdownStarted,
    CountdownTick,
    CountdownCancelled,
    WentAway,
    CameBack,
}

// The decision logic, kept free of Windows calls and clocks so it can be tested with plain numbers.
// One Step per second, given the idle time and the lock time worked out for that second.
//
// The warning countdown sits inside the lock time (D1). Its length is w = min(warning, total ÷ 2),
// rounded up to whole seconds. It starts on the first step with idle ≥ total − w and records where
// it ends: max(total, the idle time at the start + w). Started on time that is the lock time; started
// late, because the lock time dropped to or below the idle time already reached (something that
// made it later ended, the battery was unplugged, a pause ended, the first step after starting up),
// it still runs the whole w and never locks on its first step.
//
// Every step the lock time is worked out again. A never, or an extra or a lost cut that puts the
// countdown's start back past the idle time, cancels it. An extra that leaves the start before the
// idle time moves the end to the new lock time. A lock time that falls never brings the end forward.
//
// With no warning (w = 0) there is no countdown: idle reaching the lock time locks at once. A lock
// time that drops to or below the idle time already reached waits for one more step that agrees, so
// one bad reading cannot lock at once.
sealed class LockEngine
{
    State state;

    public LockEngine(int warningSeconds = 0)
    {
        WarningSeconds = warningSeconds;
    }

    // The warning chosen in the settings (graceSeconds), before the cap at half the lock time.
    public int WarningSeconds { get; set; }

    public Phase Phase => state.Phase;

    // The idle time, in seconds, at which the running countdown ends and the PC locks.
    public double CountdownEnd => state.End;

    // Whole seconds left of the running countdown at the last step.
    public int CountdownRemaining => state.Remaining;

    // Why the last countdown was cancelled: "input", "paused", "session locked", "session unlocked",
    // "the PC slept", "never", or "later" (the lock time moved past the idle time).
    public string? CancelReason { get; private set; }

    // totalMinutes is the lock time now, or null while something keeps it from locking at all.
    public EngineEvent Step(double idleSeconds, bool realInput, decimal? totalMinutes, bool paused, bool sessionLocked)
    {
        var (next, happened, reason) = Next(state, idleSeconds, realInput, totalMinutes, WarningSeconds, paused, sessionLocked);
        state = next;
        if (happened == EngineEvent.CountdownCancelled) CancelReason = reason;
        return happened;
    }

    // What Step would do with these, without doing it. The watcher asks before a countdown starts or
    // the PC locks, so it can take a fresh look first.
    public EngineEvent Peek(double idleSeconds, bool realInput, decimal? totalMinutes, bool paused, bool sessionLocked) =>
        Next(state, idleSeconds, realInput, totalMinutes, WarningSeconds, paused, sessionLocked).Event;

    // "Lock now" from the menu or the lock shortcut.
    public void GoAway() => state = new State(Phase.Away);

    // The moment the session locks, rather than at the next step: a running countdown ends here, and
    // the engine stays away until the session unlocks, as a step while it is locked would leave it.
    // True when a countdown was running.
    public bool SessionLocked()
    {
        bool counting = state.Phase == Phase.Countdown;
        if (counting) CancelReason = "session locked";
        state = new State(Phase.Away);
        return counting;
    }

    // After the session unlocks, or after the PC wakes from sleep: watching from the start again.
    // True when a countdown was running, which this ends without going away; after an unlock that
    // only happens when the lock itself was never reported.
    public bool Reset(string reason = "session unlocked")
    {
        bool counting = state.Phase == Phase.Countdown;
        if (counting) CancelReason = reason;
        state = new State(Phase.Watching);
        return counting;
    }

    readonly record struct State(Phase Phase, double End = 0, int Remaining = 0, double? LastIdle = null, bool Confirming = false);

    static (State State, EngineEvent Event, string? Reason) Next(State s, double idle, bool input, decimal? total, int warning, bool paused, bool locked)
    {
        if (paused || locked)
        {
            var after = new State(locked ? Phase.Away : Phase.Watching);
            return s.Phase == Phase.Countdown
                ? (after, EngineEvent.CountdownCancelled, paused ? "paused" : "session locked")
                : (after, EngineEvent.None, null);
        }

        if (input)
        {
            var after = new State(Phase.Watching, LastIdle: idle);
            return s.Phase switch
            {
                Phase.Countdown => (after, EngineEvent.CountdownCancelled, "input"),
                Phase.Away => (after, EngineEvent.CameBack, null),
                _ => (after, EngineEvent.None, null),
            };
        }

        if (s.Phase == Phase.Away) return (s, EngineEvent.None, null);

        if (total is not decimal minutes)
        {
            return s.Phase == Phase.Countdown
                ? (new State(Phase.Watching, LastIdle: idle), EngineEvent.CountdownCancelled, "never")
                : (s with { LastIdle = idle, Confirming = false }, EngineEvent.None, null);
        }

        int w = LockTime.CountdownSeconds(minutes, warning);
        double lockAt = (double)(minutes * 60);
        double start = (double)(minutes * 60 - w);

        if (s.Phase == Phase.Countdown)
        {
            if (idle < start) return (new State(Phase.Watching, LastIdle: idle), EngineEvent.CountdownCancelled, "later");
            double end = Math.Max(s.End, lockAt);
            if (idle >= end) return (new State(Phase.Away), EngineEvent.WentAway, null);
            return (s with { End = end, Remaining = Left(end, idle), LastIdle = idle }, EngineEvent.CountdownTick, null);
        }

        // Watching. On time means the idle time crossed the start since the last step; anything else
        // is late: the start dropped behind the idle time, or there was no last step to compare.
        bool onTime = s.LastIdle is double before && before < start;
        if (idle < start) return (s with { LastIdle = idle, Confirming = false }, EngineEvent.None, null);
        if (w <= 0)
        {
            return onTime || s.Confirming
                ? (new State(Phase.Away), EngineEvent.WentAway, null)
                : (s with { LastIdle = idle, Confirming = true }, EngineEvent.None, null);
        }
        double ends = Math.Max(lockAt, idle + w);
        return (new State(Phase.Countdown, ends, Left(ends, idle), idle), EngineEvent.CountdownStarted, null);
    }

    // Whole seconds to go, rounded up, at least 1 while the countdown runs.
    static int Left(double end, double idle) => Math.Max(1, (int)Math.Ceiling(end - idle - 1e-9));
}
