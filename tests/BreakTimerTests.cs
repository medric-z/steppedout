namespace SteppedOut.Tests;

[TestClass]
public class BreakTimerTests
{
    static BreakTimer Timer(double reminderMinutes = 60) => new() { ReminderSeconds = reminderMinutes * 60 };

    [TestMethod]
    public void OffByDefault()
    {
        var t = new BreakTimer();
        Assert.IsNull(t.Due(10_000, 0, away: false));
    }

    [TestMethod]
    public void DueAfterAnUnbrokenStretch()
    {
        var t = Timer();
        Assert.IsNull(t.Due(0, 0, away: false));
        Assert.IsNull(t.Due(3599, 5, away: false));
        Assert.AreEqual(3600, t.Due(3600, 5, away: false));
    }

    [TestMethod]
    public void KeepsAskingUntilShownThenWaitsAnotherStretch()
    {
        var t = Timer();
        t.Due(0, 0, away: false);
        Assert.IsNotNull(t.Due(3600, 1, away: false));
        Assert.IsNotNull(t.Due(3601, 1, away: false), "not shown yet, for example during a fullscreen game");
        t.Reminded(3601);
        Assert.IsNull(t.Due(3700, 1, away: false));
        Assert.IsNotNull(t.Due(3601 + 3600, 1, away: false));
    }

    [TestMethod]
    public void ABreakStartsANewStretch()
    {
        var t = Timer();
        t.Due(0, 0, away: false);
        Assert.IsNull(t.Due(3000, t.BreakSeconds, away: false));
        Assert.IsNull(t.Due(3600, 10, away: false), "the stretch began when input came back, about 10 s ago");
        Assert.IsNotNull(t.Due(3590 + 3600, 10, away: false));
    }

    [TestMethod]
    public void BeingAwayCountsAsABreak()
    {
        var t = Timer();
        t.Due(0, 0, away: false);
        Assert.IsNull(t.Due(3500, 0, away: true));
        Assert.IsNull(t.Due(3600, 0, away: false));
    }

    [TestMethod]
    public void TurningItOffForgetsTheStretch()
    {
        var t = Timer();
        t.Due(0, 0, away: false);
        t.ReminderSeconds = 0;
        Assert.IsNull(t.Due(5000, 0, away: false));
        t.ReminderSeconds = 3600;
        Assert.IsNull(t.Due(5001, 0, away: false));
    }

    [TestMethod]
    public void SnoozeBringsTheReminderBackAfterTheChosenMinutes()
    {
        var t = Timer();
        t.Due(0, 0, away: false);
        Assert.IsNotNull(t.Due(3600, 1, away: false));
        t.Snooze(3610, 10);
        Assert.IsNull(t.Due(3611, 1, away: false));
        Assert.IsNull(t.Due(3610 + 599, 1, away: false));
        Assert.AreEqual(3610 + 600, t.Due(3610 + 600, 1, away: false), "back at now + 10 min, with the whole stretch");
        Assert.IsNotNull(t.Due(3610 + 601, 1, away: false), "still due until shown");
    }

    [TestMethod]
    public void ASnoozedReminderShownAgainWaitsAWholeStretch()
    {
        var t = Timer();
        t.Due(0, 0, away: false);
        t.Due(3600, 1, away: false);
        t.Snooze(3600, 30);
        Assert.IsNotNull(t.Due(5400, 1, away: false));
        t.Reminded(5400);
        Assert.IsNull(t.Due(5401, 1, away: false));
        Assert.IsNull(t.Due(5400 + 3599, 1, away: false));
        Assert.IsNotNull(t.Due(5400 + 3600, 1, away: false));
    }

    [TestMethod]
    public void SnoozeCanBeShorterThanTheReminder()
    {
        var t = Timer(reminderMinutes: 120);
        t.Due(0, 0, away: false);
        Assert.IsNotNull(t.Due(7200, 1, away: false));
        t.Snooze(7200, 10);
        Assert.IsNotNull(t.Due(7800, 1, away: false), "10 minutes, not another 2 hours");
    }

    [TestMethod]
    public void ABreakDropsASnoozedReminder()
    {
        var t = Timer();
        t.Due(0, 0, away: false);
        t.Due(3600, 1, away: false);
        t.Snooze(3600, 10);
        Assert.IsNull(t.Due(3900, 0, away: true));
        Assert.IsNull(t.Due(4200, 1, away: false), "a new stretch began at 4199");
        Assert.IsNull(t.Due(4199 + 3599, 1, away: false));
        Assert.IsNotNull(t.Due(4199 + 3600, 1, away: false));
    }

    [TestMethod]
    public void SnoozeOfZeroIsTheSameAsClosingTheReminder()
    {
        var t = Timer();
        t.Due(0, 0, away: false);
        t.Due(3600, 1, away: false);
        t.Snooze(3600, 0);
        Assert.IsNull(t.Due(3601, 1, away: false));
        Assert.IsNotNull(t.Due(7200, 1, away: false));
    }
}
