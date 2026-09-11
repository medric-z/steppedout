namespace SteppedOut;

enum Phase
{
    Watching,
    Countdown,
    Locked,
}

enum EngineEvent
{
    None,
    CountdownStarted,
    CountdownTick,
    CountdownCancelled,
    LockRequested,
    SuppressionChanged,
}

// The decision logic, kept free of Windows calls so it can be tested with plain numbers.
// One Step per second.
sealed class LockEngine
{
    public LockEngine(double idleThresholdSeconds, int graceSeconds)
    {
        IdleThresholdSeconds = idleThresholdSeconds;
        GraceSeconds = graceSeconds;
    }

    public double IdleThresholdSeconds { get; set; }
    public int GraceSeconds { get; set; }
    public Phase Phase { get; private set; }
    public int CountdownRemaining { get; private set; }
    public string? Suppressor { get; private set; }
    public string? CancelReason { get; private set; }

    public EngineEvent Step(double idleSeconds, bool realInput, string? suppressor, bool paused, bool sessionLocked)
    {
        if (paused || sessionLocked)
        {
            var was = Phase;
            Phase = sessionLocked ? Phase.Locked : Phase.Watching;
            Suppressor = null;
            return was == Phase.Countdown ? Cancel(paused ? "paused" : "session locked") : EngineEvent.None;
        }

        if (realInput)
        {
            var was = Phase;
            Phase = Phase.Watching;
            Suppressor = null;
            return was == Phase.Countdown ? Cancel("input") : EngineEvent.None;
        }

        bool changed = suppressor != Suppressor;
        Suppressor = suppressor;

        switch (Phase)
        {
            case Phase.Locked:
                return EngineEvent.None;

            case Phase.Watching:
                if (idleSeconds < IdleThresholdSeconds) return EngineEvent.None;
                if (suppressor is not null) return changed ? EngineEvent.SuppressionChanged : EngineEvent.None;
                if (GraceSeconds <= 0)
                {
                    Phase = Phase.Locked;
                    return EngineEvent.LockRequested;
                }
                Phase = Phase.Countdown;
                CountdownRemaining = GraceSeconds;
                return EngineEvent.CountdownStarted;

            default:
                if (suppressor is not null)
                {
                    Phase = Phase.Watching;
                    return Cancel(suppressor);
                }
                CountdownRemaining--;
                if (CountdownRemaining <= 0)
                {
                    Phase = Phase.Locked;
                    return EngineEvent.LockRequested;
                }
                return EngineEvent.CountdownTick;
        }
    }

    public void Reset()
    {
        Phase = Phase.Watching;
        Suppressor = null;
    }

    EngineEvent Cancel(string reason)
    {
        CancelReason = reason;
        return EngineEvent.CountdownCancelled;
    }
}
