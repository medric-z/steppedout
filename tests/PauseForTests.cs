namespace SteppedOut.Tests;

// Pausing for longer than an hour: the ready-made times, the Pause for window behind Custom…, the
// status line that says when the pause ends, and a week-long pause on a fake clock. The window is
// only built here, never shown.
[TestClass]
[DoNotParallelize]
public class PauseForTests
{
    // ---- The lists ----

    [TestMethod]
    public void TheReadyMadeTimesHaveNoTwoMinutes()
    {
        CollectionAssert.AreEqual(new[] { 5, 15, 30, 60 }, TimeChoices.WelcomeLockAfter);
        CollectionAssert.AreEqual(new[] { 30, 45, 60, 90, 120, 240 }, TimeChoices.Breaks);
        CollectionAssert.AreEqual(new[] { 5, 10, 15, 30, 60 }, TimeChoices.Snooze);
        CollectionAssert.AreEqual(new[] { 15, 30, 60, 120, 1440 }, TimeChoices.Pause);
        CollectionAssert.AreEqual(new[] { 5, 720, 240, 7 * 24 * 60 },
            new[] { TimeChoices.ShortestBreak, TimeChoices.LongestBreak, TimeChoices.LongestSnooze, TimeChoices.LongestPause }, "the Custom… ranges");
        foreach (var list in new[] { TimeChoices.WelcomeLockAfter, TimeChoices.LockAfter, TimeChoices.Breaks, TimeChoices.Snooze, TimeChoices.Pause })
            CollectionAssert.DoesNotContain(list, 2);
    }

    // A 2 saved before still loads as 2 and shows in the settings list as itself.
    [TestMethod]
    public void ASavedTwoMinutesStillLoadsAndShowsAsItself()
    {
        string folder = Path.Combine(Path.GetTempPath(), "so-r2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "config.json");
            File.WriteAllText(path, "{ \"configVersion\": 1, \"idleMinutes\": 2 }");
            var c = Config.Load(path, out _);
            Assert.AreEqual(2, c.IdleMinutes);
            var list = new MainList(() => AutostartState.On);
            using var built = list.Build(c, English.Text);
            Assert.AreEqual("2 minutes", list.LockAfter!.Text);
            Assert.AreEqual(2.0, list.LockAfter.Value);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [TestMethod]
    public void PauseLengthsReadAsDaysOrAsAListSaysThem()
    {
        var en = English.Text;
        Assert.AreEqual("1 day", en.PauseLength(1440));
        Assert.AreEqual("7 days", en.PauseLength(7 * 1440));
        Assert.AreEqual("2 hours", en.PauseLength(120));
        Assert.AreEqual("1 minute", en.PauseLength(1));
        Assert.AreEqual(en.OptionDuration(1500), en.PauseLength(1500), "a day and an hour is said in hours and minutes");
        foreach (int minutes in new[] { 1, 15, 1440, 3 * 1440, 10080 })
            Assert.IsFalse(string.IsNullOrWhiteSpace(Arabic.Text.PauseLength(minutes)));
    }

    // ---- The Pause for window ----

    [TestMethod]
    [DataRow("90", "Minutes", 90, false)]
    [DataRow("3", "Hours", 180, false)]
    [DataRow("2", "Days", 2880, false)]
    [DataRow("7", "Days", 10080, false)]
    [DataRow("10080", "Minutes", 10080, false)]
    [DataRow("1", "Minutes", 1, false)]
    [DataRow("0", "Minutes", 1, true)]
    [DataRow("0", "Days", 1, true)]
    [DataRow("8", "Days", 10080, true)]
    [DataRow("169", "Hours", 10080, true)]
    [DataRow("99999", "Days", 10080, true)]
    [DataRow("99999999999999999999", "Days", 10080, true)]
    [DataRow(" 45 ", "Minutes", 45, false)]
    [DataRow("٣", "Hours", 180, false)]
    public void ANumberAndUnitComeToALengthInRange(string typed, string unit, int minutes, bool clamped)
    {
        Assert.AreEqual((minutes, clamped), PauseForWindow.Length(typed, Enum.Parse<PauseUnit>(unit)));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("  ")]
    [DataRow("3h")]
    [DataRow("-2")]
    [DataRow("1.5")]
    public void AnythingButDigitsIsNoLength(string typed)
    {
        Assert.IsNull(PauseForWindow.Length(typed, PauseUnit.Hours));
    }

    [TestMethod]
    public void TheLineSaysTheRangeAndWhatIsUsed()
    {
        var en = English.Text;
        Assert.AreEqual("From 1 minute to 7 days.", PauseForWindow.LineFor(en, (90, false)));
        Assert.AreEqual("Only 1 minute to 7 days work, so it pauses for 7 days.", PauseForWindow.LineFor(en, (10080, true)));
        Assert.AreEqual("Only 1 minute to 7 days work, so it pauses for 1 minute.", PauseForWindow.LineFor(en, (1, true)));
        Assert.AreEqual("Type a number.", PauseForWindow.LineFor(en, null));
    }

    [TestMethod]
    public void TheWindowPausesForWhatItSaysAndIsKeyboardFriendly()
    {
        foreach (var text in new[] { English.Text, Arabic.Text })
        {
            var asked = new List<TimeSpan>();
            using var window = new PauseForWindow(text, asked.Add);
            var v = text.V1;
            Assert.AreEqual(v.PauseForTitle, window.Text);
            Assert.AreEqual(v.PauseForTitle, window.Number.Input.AccessibleName, "the number box is named");
            Assert.AreEqual(v.PauseForUnit, window.Unit.AccessibleName, "the unit list is named");
            Assert.AreSame(window.Number.Input, window.ActiveControl, "the number box has the keyboard first");
            Assert.AreSame(window.PauseButton, window.AcceptButton, "Enter pauses");
            Assert.AreSame(window.Dismiss, window.CancelButton, "Esc cancels");
            Assert.AreEqual(text.RightToLeft ? RightToLeft.Yes : RightToLeft.No, window.RightToLeft);
            CollectionAssert.AreEqual(new[] { v.UnitMinutes, v.UnitHours, v.UnitDays }, window.Unit.Choices.ToArray());
            Assert.AreEqual("3", window.Number.Value);
            Assert.AreEqual(PauseUnit.Hours, window.Unit.Value);
            Assert.AreEqual(v.PauseForRange, window.Line);
            Assert.AreEqual(v.PauseForRange, window.Number.Input.AccessibleDescription, "the line is the box's description");
            var order = new Control[] { window.Number, window.Unit, window.PauseButton, window.Dismiss }.Select(c => c.TabIndex).ToArray();
            CollectionAssert.AreEqual(order.OrderBy(i => i).ToArray(), order, "Tab goes number, unit, Pause, Cancel");

            window.Number.Value = "";
            Assert.IsFalse(window.PauseButton.Enabled, "nothing to pause for without a number");
            Assert.AreEqual(v.PauseForNotANumber, window.Line);
            window.Pause();
            Assert.IsEmpty(asked);

            window.Number.Value = "9";
            using (var menu = window.Unit.BuildMenu()) menu.Items.OfType<ToolStripMenuItem>().ElementAt(2).PerformClick();
            Assert.AreEqual(PauseUnit.Days, window.Unit.Value);
            Assert.IsTrue(window.PauseButton.Enabled);
            Assert.AreEqual(string.Format(v.PauseForClamped, text.PauseLength(10080)), window.Line);
            window.Pause();
            CollectionAssert.AreEqual(new[] { TimeSpan.FromDays(7) }, asked, "out of range uses the nearest");
        }
    }

    // The unit list, picked as a person picks it, changes the length and the line.
    [TestMethod]
    public void PickingAUnitChangesTheLength()
    {
        var asked = new List<TimeSpan>();
        using var window = new PauseForWindow(English.Text, asked.Add);
        window.Number.Value = "2";
        using (var menu = window.Unit.BuildMenu()) menu.Items.OfType<ToolStripMenuItem>().ElementAt(2).PerformClick();
        Assert.AreEqual(PauseUnit.Days, window.Unit.Value);
        window.Pause();
        using var second = new PauseForWindow(English.Text, asked.Add);
        second.Number.Value = "45";
        using (var menu = second.Unit.BuildMenu()) menu.Items.OfType<ToolStripMenuItem>().ElementAt(0).PerformClick();
        second.Pause();
        CollectionAssert.AreEqual(new[] { TimeSpan.FromDays(2), TimeSpan.FromMinutes(45) }, asked);
    }

    // ---- When it comes back ----

    static readonly DateTime Thursday = new(2026, 10, 22, 14, 0, 0);

    [TestMethod]
    public void ThePausedLineSaysTodayTomorrowOrTheDay()
    {
        var en = English.Text;
        var v = en.V1;
        var today = Thursday.Date.AddHours(17).AddMinutes(12);
        Assert.AreEqual(string.Format(v.PausedUntil, en.ClockTime(today)), en.PausedLine(today, Thursday));
        StringAssert.StartsWith(en.PausedLine(today, Thursday), "Paused · back on at ");

        var tomorrow = today.AddDays(1);
        Assert.AreEqual(string.Format(v.PausedUntilTomorrow, en.ClockTime(tomorrow)), en.PausedLine(tomorrow, Thursday));
        StringAssert.StartsWith(en.PausedLine(tomorrow, Thursday), "Paused · back on tomorrow at ");
        // Just after midnight is tomorrow, though it is minutes away.
        Assert.AreEqual(string.Format(v.PausedUntilTomorrow, en.ClockTime(Thursday.Date.AddDays(1).AddMinutes(10))),
            en.PausedLine(Thursday.Date.AddDays(1).AddMinutes(10), Thursday.Date.AddHours(23).AddMinutes(50)));

        var saturday = today.AddDays(2);
        Assert.AreEqual(string.Format(v.PausedUntilDay, "Saturday", en.ClockTime(saturday)), en.PausedLine(saturday, Thursday));
        Assert.AreEqual(string.Format(v.PausedUntilDay, "Wednesday", en.ClockTime(today.AddDays(6))), en.PausedLine(today.AddDays(6), Thursday));

        // A full week on would be today's own day name: the date instead.
        string week = en.PausedLine(today.AddDays(7), Thursday);
        StringAssert.Contains(week, "29");
        Assert.IsFalse(week.Contains("Thursday", StringComparison.Ordinal), week);
    }

    [TestMethod]
    public void ThePausedLineWorksInArabic()
    {
        var ar = Arabic.Text;
        var until = Thursday.AddDays(2);
        string line = ar.PausedLine(until, Thursday);
        StringAssert.Contains(line, "السبت", "the day's name from an Arabic culture");
        StringAssert.Contains(line, ar.ClockTime(until));
        foreach (int days in new[] { 0, 1, 3, 7 })
            Assert.IsFalse(string.IsNullOrWhiteSpace(ar.PausedLine(Thursday.AddHours(1).AddDays(days), Thursday)));
    }

    // The status box headline and the tray tooltip both say it.
    [TestMethod]
    public void TheHeadlineAndTooltipFollow()
    {
        var was = Strings.LocalNow;
        try
        {
            Strings.LocalNow = () => Thursday;
            var status = new LockStatus { Kind = LockState.Paused, Time = LockTime.Plain(15), PausedUntil = Thursday.AddDays(2).Date.AddHours(17) };
            foreach (var text in new[] { English.Text, Arabic.Text })
            {
                string line = text.PausedLine(status.PausedUntil!.Value, Thursday);
                Assert.AreEqual(line, text.Headline(status));
                StringAssert.Contains(text.Tooltip(status), line);
                Assert.IsLessThanOrEqualTo(Strings.TooltipLength, text.Tooltip(status).Length);
            }
            Assert.AreEqual(English.Text.V1.PausedUntilTurnedBackOn, English.Text.Headline(status with { PausedUntil = null }));
        }
        finally
        {
            Strings.LocalNow = was;
        }
    }

    // ---- A week on a fake clock ----

    // The pause ends are kept on the clock that counts through sleep, so a week-long pause ends a
    // week on, slept through or not, and the status says the day it ends.
    [TestMethod]
    public void AWeekLongPauseLastsAWeekThroughSleep()
    {
        using var rig = new Rig(new Config { IdleMinutes = 15, GraceSeconds = 15 });
        var ends = new DateTimeOffset(rig.Wall).AddDays(7).LocalDateTime;
        rig.Watcher.Pause(TimeSpan.FromDays(7));
        Assert.IsTrue(rig.Watcher.IsPaused);
        Assert.AreEqual(LockState.Paused, rig.Watcher.LockStatus.Kind);
        Assert.AreEqual(ends, rig.Watcher.LockStatus.PausedUntil);

        rig.Second();
        rig.Jump(TimeSpan.FromDays(3));
        rig.Second();
        Assert.IsTrue(rig.Watcher.IsPaused, "still paused after sleeping three days");
        Assert.AreEqual(ends, rig.Watcher.LockStatus.PausedUntil, "the end has not moved");

        rig.Jump(TimeSpan.FromDays(4) - TimeSpan.FromSeconds(10));
        rig.Second();
        Assert.IsTrue(rig.Watcher.IsPaused, "seconds before the week is up");
        Assert.AreEqual(0, rig.LockRequests);
        for (int i = 0; i < 10; i++) rig.Second();
        Assert.IsFalse(rig.Watcher.IsPaused, "a week on, it is back on");
        Assert.AreNotEqual(LockState.Paused, rig.Watcher.LockStatus.Kind);
    }

    // A pause that ended while the PC slept is over on waking.
    [TestMethod]
    public void APauseThatEndsDuringSleepIsOverOnWaking()
    {
        using var rig = new Rig(new Config { IdleMinutes = 15, GraceSeconds = 15 });
        rig.Watcher.Pause(TimeSpan.FromDays(2));
        rig.Second();
        rig.Jump(TimeSpan.FromDays(3));
        rig.Second();
        Assert.IsFalse(rig.Watcher.IsPaused);
        Assert.AreNotEqual(LockState.Paused, rig.Watcher.LockStatus.Kind);
    }

    // A pause lives in memory only: it is not a setting, so a new start of the app is not paused.
    [TestMethod]
    public void APauseIsNotKeptAcrossARestart()
    {
        using (var rig = new Rig(new Config()))
        {
            rig.Watcher.Pause(TimeSpan.FromDays(7));
            Assert.IsTrue(rig.Watcher.IsPaused);
        }
        using var again = new Rig(new Config());
        Assert.IsFalse(again.Watcher.IsPaused);
        Assert.IsFalse(typeof(Config).GetProperties().Any(p => p.Name.Contains("Pause", StringComparison.Ordinal)), "no setting holds a pause");
    }
}
