namespace SteppedOut;

// The 20-20-20 eye break: after 20 minutes of use, a reminder to look into the distance. A minute
// without input, a lock or being away starts the 20 minutes again. Plain numbers in, like BreakTimer.
sealed class EyeBreaks
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan RestedAfter = TimeSpan.FromMinutes(1);

    readonly BreakTimer timer = new() { BreakSeconds = RestedAfter.TotalSeconds };

    public bool On
    {
        get => timer.ReminderSeconds > 0;
        set => timer.ReminderSeconds = value ? Every.TotalSeconds : 0;
    }

    // Seconds of use so far when an eye break is due, until Reminded or Snooze is called.
    public double? Due(double now, double idleSeconds, bool away) => timer.Due(now, idleSeconds, away);

    public void Reminded(double now) => timer.Reminded(now);

    public void Snooze(double now, double minutes) => timer.Snooze(now, minutes);
}

enum ReminderKind
{
    Break,
    EyeBreak,
}

// SecondsAtThePc is how long the stretch has lasted, for "· 1 hr at the PC".
readonly record struct Reminder(ReminderKind Kind, double SecondsAtThePc);

// Both reminders together, so they never show at once (D28). The tick asks Due once a second; the
// caller calls Shown when the note is really on screen (not during a pause, a countdown or while
// Windows holds notifications back), and Snooze from the note's button.
sealed class Reminders
{
    public BreakTimer Breaks { get; } = new();
    public EyeBreaks Eyes { get; } = new();

    public void Apply(bool breakReminders, int breakMinutes, bool eyeBreaks)
    {
        Breaks.ReminderSeconds = breakReminders ? Math.Max(1, breakMinutes) * 60 : 0;
        Eyes.On = eyeBreaks;
    }

    public Reminder? Due(double now, double idleSeconds, bool away)
    {
        // Both are asked every time, so each sees every break and every lock.
        double? sitting = Breaks.Due(now, idleSeconds, away);
        double? looking = Eyes.Due(now, idleSeconds, away);
        if (sitting is double s) return new(ReminderKind.Break, s);
        if (looking is double l) return new(ReminderKind.EyeBreak, l);
        return null;
    }

    public void Shown(ReminderKind kind, double now)
    {
        // Getting up for a break rests the eyes too, so an eye break due at the same time is
        // dropped rather than shown a second later.
        if (kind == ReminderKind.Break) Breaks.Reminded(now);
        Eyes.Reminded(now);
    }

    public void Snooze(ReminderKind kind, double now, double minutes)
    {
        if (kind == ReminderKind.Break)
        {
            Breaks.Snooze(now, minutes);
            Eyes.Reminded(now);
        }
        else
        {
            Eyes.Snooze(now, minutes);
        }
    }
}
