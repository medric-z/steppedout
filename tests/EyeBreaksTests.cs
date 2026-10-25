namespace SteppedOut.Tests;

[TestClass]
public class EyeBreaksTests
{
    const double Twenty = 20 * 60;

    static EyeBreaks Eyes() => new() { On = true };

    static Reminders Both(int breakMinutes = 60, bool breaks = true, bool eyes = true)
    {
        var r = new Reminders();
        r.Apply(breaks, breakMinutes, eyes);
        return r;
    }

    // A brand-new install's break reminder comes after 2 hours at the PC from the start, not at once,
    // and no eye break comes with it.
    [TestMethod]
    public void ANewInstallsFirstBreakReminderComesAfterTwoHours()
    {
        var c = Config.FirstStart();
        var r = new Reminders();
        r.Apply(c.BreakReminders, c.BreakMinutes, c.EyeBreaks);
        Assert.IsNull(r.Due(0, 0, away: false), "not at once");
        Assert.IsNull(r.Due(60, 0, away: false));
        Assert.IsNull(r.Due(2 * 3600 - 1, 0, away: false));
        Assert.AreEqual(new Reminder(ReminderKind.Break, 2 * 3600), r.Due(2 * 3600, 0, away: false));
    }

    [TestMethod]
    public void OffByDefault()
    {
        var e = new EyeBreaks();
        Assert.IsFalse(e.On);
        e.Due(0, 0, away: false);
        Assert.IsNull(e.Due(10_000, 0, away: false));
    }

    [TestMethod]
    public void DueAfterTwentyMinutesOfUse()
    {
        var e = Eyes();
        Assert.IsNull(e.Due(0, 0, away: false));
        Assert.IsNull(e.Due(Twenty - 1, 5, away: false));
        Assert.AreEqual(Twenty, e.Due(Twenty, 5, away: false));
        Assert.IsNotNull(e.Due(Twenty + 1, 5, away: false), "still due until shown");
    }

    [TestMethod]
    public void ShownThenDueAgainTwentyMinutesLater()
    {
        var e = Eyes();
        e.Due(0, 0, away: false);
        e.Due(Twenty, 1, away: false);
        e.Reminded(Twenty);
        Assert.IsNull(e.Due(Twenty + 1, 1, away: false));
        Assert.IsNull(e.Due(2 * Twenty - 1, 1, away: false));
        Assert.IsNotNull(e.Due(2 * Twenty, 1, away: false));
    }

    [TestMethod]
    public void AMinuteWithoutInputStartsTheTwentyMinutesAgain()
    {
        var e = Eyes();
        e.Due(0, 0, away: false);
        Assert.IsNull(e.Due(600, 59, away: false), "59 s is not a rest");
        Assert.IsNull(e.Due(900, 60, away: false), "a minute is");
        Assert.IsNull(e.Due(901, 0, away: false));
        Assert.IsNull(e.Due(901 + Twenty - 1, 0, away: false));
        Assert.IsNotNull(e.Due(901 + Twenty, 0, away: false));
    }

    [TestMethod]
    public void ALockOrBeingAwayStartsTheTwentyMinutesAgain()
    {
        var e = Eyes();
        e.Due(0, 0, away: false);
        Assert.IsNull(e.Due(1000, 0, away: true), "locked or away");
        Assert.IsNull(e.Due(1010, 0, away: false));
        Assert.IsNull(e.Due(Twenty + 5, 0, away: false), "no longer due at the old time");
        Assert.IsNotNull(e.Due(1010 + Twenty, 0, away: false));
    }

    [TestMethod]
    public void SnoozeBringsTheEyeBreakBackAfterTheChosenMinutes()
    {
        var e = Eyes();
        e.Due(0, 0, away: false);
        e.Due(Twenty, 1, away: false);
        e.Snooze(Twenty, 10);
        Assert.IsNull(e.Due(Twenty + 599, 1, away: false));
        Assert.IsNotNull(e.Due(Twenty + 600, 1, away: false));
    }

    [TestMethod]
    public void OnlyTheBreakReminderShowsWhenBothAreDue()
    {
        // A 20-minute break reminder and an eye break fall due on the same tick.
        var r = Both(breakMinutes: 20);
        Assert.IsNull(r.Due(0, 0, away: false));
        var due = r.Due(Twenty, 1, away: false);
        Assert.AreEqual(new Reminder(ReminderKind.Break, Twenty), due);
        r.Shown(ReminderKind.Break, Twenty);
        Assert.IsNull(r.Due(Twenty + 1, 1, away: false), "the eye break was dropped, not shown next");
        Assert.IsNull(r.Due(2 * Twenty - 1, 1, away: false));
        Assert.AreEqual(ReminderKind.Break, r.Due(2 * Twenty, 1, away: false)?.Kind);
    }

    [TestMethod]
    public void TheBreakReminderWinsUntilItIsShown()
    {
        var r = Both(breakMinutes: 20);
        r.Due(0, 0, away: false);
        // Not shown yet, for example during a countdown: the eye break never takes its place.
        for (double now = Twenty; now < Twenty + 30; now++)
            Assert.AreEqual(ReminderKind.Break, r.Due(now, 1, away: false)?.Kind);
    }

    [TestMethod]
    public void EyeBreaksShowBetweenBreakReminders()
    {
        var r = Both(breakMinutes: 60);
        r.Due(0, 0, away: false);
        Assert.AreEqual(ReminderKind.EyeBreak, r.Due(Twenty, 1, away: false)?.Kind);
        r.Shown(ReminderKind.EyeBreak, Twenty);
        Assert.IsNull(r.Due(Twenty + 1, 1, away: false));
        Assert.AreEqual(ReminderKind.EyeBreak, r.Due(2 * Twenty, 1, away: false)?.Kind);
        r.Shown(ReminderKind.EyeBreak, 2 * Twenty);
        // At an hour both are due; only the break reminder shows.
        Assert.AreEqual(new Reminder(ReminderKind.Break, 3600), r.Due(3600, 1, away: false));
        r.Shown(ReminderKind.Break, 3600);
        Assert.IsNull(r.Due(3601, 1, away: false));
        Assert.AreEqual(ReminderKind.EyeBreak, r.Due(3600 + Twenty, 1, away: false)?.Kind);
    }

    [TestMethod]
    public void SnoozeAppliesToBothReminders()
    {
        var r = Both(breakMinutes: 60);
        r.Due(0, 0, away: false);
        Assert.AreEqual(ReminderKind.EyeBreak, r.Due(Twenty, 1, away: false)?.Kind);
        r.Shown(ReminderKind.EyeBreak, Twenty);
        r.Snooze(ReminderKind.EyeBreak, Twenty + 5, 10);
        Assert.IsNull(r.Due(Twenty + 5 + 599, 1, away: false));
        Assert.AreEqual(ReminderKind.EyeBreak, r.Due(Twenty + 5 + 600, 1, away: false)?.Kind);
        r.Shown(ReminderKind.EyeBreak, Twenty + 605);

        Assert.AreEqual(ReminderKind.Break, r.Due(3600, 1, away: false)?.Kind);
        r.Shown(ReminderKind.Break, 3600);
        r.Snooze(ReminderKind.Break, 3600, 10);
        Assert.IsNull(r.Due(4199, 1, away: false));
        Assert.AreEqual(ReminderKind.Break, r.Due(4200, 1, away: false)?.Kind);
    }

    [TestMethod]
    public void ALockResetsBoth()
    {
        var r = Both(breakMinutes: 60);
        r.Due(0, 0, away: false);
        r.Due(3000, 1, away: false);
        Assert.IsNull(r.Due(3100, 0, away: true));
        Assert.IsNull(r.Due(3200, 0, away: false));
        Assert.IsNull(r.Due(3600, 0, away: false), "the break reminder starts over too");
        Assert.AreEqual(ReminderKind.EyeBreak, r.Due(3200 + Twenty, 0, away: false)?.Kind);
    }

    [TestMethod]
    public void EachCanBeOnAlone()
    {
        var eyesOnly = Both(breaks: false);
        eyesOnly.Due(0, 0, away: false);
        Assert.AreEqual(ReminderKind.EyeBreak, eyesOnly.Due(Twenty, 1, away: false)?.Kind);

        var breaksOnly = Both(breakMinutes: 20, eyes: false);
        breaksOnly.Due(0, 0, away: false);
        Assert.AreEqual(ReminderKind.Break, breaksOnly.Due(Twenty, 1, away: false)?.Kind);

        var neither = Both(breaks: false, eyes: false);
        neither.Due(0, 0, away: false);
        Assert.IsNull(neither.Due(10_000, 1, away: false));
    }
}
