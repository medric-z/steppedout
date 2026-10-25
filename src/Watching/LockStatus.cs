namespace SteppedOut;

enum LockState
{
    // Before the first look at the idle time.
    Starting,

    // Counting idle time toward the lock.
    On,

    // Something going on keeps it from locking at all (LockTime.NeverCauses says what).
    Never,

    // The warning countdown before the lock is running.
    Countdown,

    // Paused from the menu, until PausedUntil or until turned back on.
    Paused,

    // Locking when you're away is turned off: the away actions still run after the lock time.
    LockingOff,

    // Stepped out: about to lock, or away with locking turned off.
    Away,

    // The session is locked, by this app or by hand.
    SessionLocked,
}

// How many times the PC locked today and how long you were away today. They start over at
// local midnight.
readonly record struct TodayCounts(int Locks, TimeSpan Away);

// What the status box, the tray menu, the tooltip and the minutes on the icon show, once a second.
sealed record LockStatus
{
    public required LockState Kind { get; init; }

    // The lock time now, with what makes it up.
    public required LockTime Time { get; init; }

    public double IdleSeconds { get; init; }

    // Until the lock while on, or until the away actions with locking turned off; what is left of
    // the countdown while it runs.
    public double SecondsLeft { get; init; }

    // Local time a pause ends; null while not paused or paused until turned back on.
    public DateTime? PausedUntil { get; init; }

    public bool DryRun { get; init; }

    // Rules that failed and are being tried again, by name.
    public IReadOnlyList<string> FailedRules { get; init; } = Array.Empty<string>();

    public TodayCounts Today { get; init; }

    public static LockStatus Starting(decimal normalMinutes) => new() { Kind = LockState.Starting, Time = LockTime.Plain(normalMinutes) };

    public bool Equals(LockStatus? other) =>
        other is not null && Kind == other.Kind && Time == other.Time && IdleSeconds == other.IdleSeconds
        && SecondsLeft == other.SecondsLeft && PausedUntil == other.PausedUntil && DryRun == other.DryRun
        && FailedRules.SequenceEqual(other.FailedRules) && Today == other.Today;

    public override int GetHashCode() => HashCode.Combine(Kind, Time, IdleSeconds, SecondsLeft, PausedUntil, DryRun, FailedRules.Count, Today);
}
