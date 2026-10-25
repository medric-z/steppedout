namespace SteppedOut;

// How long someone has been at the PC without a break, for the optional reminder. Plain numbers
// in, so it can be tested without a clock. Eye breaks use the same timer with other lengths.
sealed class BreakTimer
{
    double? stretchStart;
    double? remindedAt;
    double? snoozedUntil;

    // 0 turns the reminder off.
    public double ReminderSeconds { get; set; }

    // Going this long without input counts as having taken a break.
    public double BreakSeconds { get; set; } = 3 * 60;

    // now and idleSeconds in seconds. Returns how long the current stretch has lasted when a
    // reminder is due, and keeps returning it until Reminded or Snooze is called.
    public double? Due(double now, double idleSeconds, bool away)
    {
        if (ReminderSeconds <= 0 || away || idleSeconds >= BreakSeconds)
        {
            stretchStart = null;
            remindedAt = null;
            snoozedUntil = null;
            return null;
        }

        stretchStart ??= now - idleSeconds;
        double sitting = now - stretchStart.Value;
        if (snoozedUntil is double until) return now >= until ? sitting : null;
        if (sitting < ReminderSeconds) return null;
        if (remindedAt is double last && now - last < ReminderSeconds) return null;
        return sitting;
    }

    public void Reminded(double now)
    {
        remindedAt = now;
        snoozedUntil = null;
    }

    // From the Snooze button on the reminder: it comes back at now + minutes, however long the
    // stretch has lasted. A break before then drops it, like any reminder. 0 or less is the same
    // as closing the reminder.
    public void Snooze(double now, double minutes)
    {
        remindedAt = now;
        snoozedUntil = minutes > 0 ? now + minutes * 60 : null;
    }
}
