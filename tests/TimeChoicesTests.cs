namespace SteppedOut.Tests;

[TestClass]
public class TimeChoicesTests
{
    [TestMethod]
    public void ReadsWholeMinutes()
    {
        Assert.AreEqual(new TypedTime(true, 45, 45), TimeChoices.Read("45"));
        Assert.AreEqual(new TypedTime(true, 45, 45), TimeChoices.Read("  45 "));
        Assert.AreEqual(new TypedTime(true, 45, 45), TimeChoices.Read("045"));
        Assert.AreEqual(new TypedTime(true, 1440, 1440), TimeChoices.Read("1440"));
        Assert.IsFalse(TimeChoices.Read("45").Clamped);
    }

    [TestMethod]
    public void ReadsArabicDigits()
    {
        // Arabic-Indic, and the extended forms Persian and Urdu keyboards type.
        Assert.AreEqual(45, TimeChoices.Read("٤٥").Minutes);
        Assert.AreEqual(90, TimeChoices.Read("۹۰").Minutes);
        Assert.IsTrue(TimeChoices.Read("١٢٠").IsNumber);
    }

    [TestMethod]
    public void AnythingButDigitsIsNotATime()
    {
        foreach (string text in new[] { "", "   ", "abc", "4a5", "45 min", "4.5", "4,5", "-5", "+5", "1e3", "٤5x" })
        {
            var typed = TimeChoices.Read(text);
            Assert.IsFalse(typed.IsNumber, text);
            Assert.IsFalse(typed.Clamped, text);
        }
    }

    [TestMethod]
    public void NumbersOutsideTheRangeComeToTheNearestEnd()
    {
        var zero = TimeChoices.Read("0");
        Assert.AreEqual(1, zero.Minutes);
        Assert.IsTrue(zero.Clamped);
        var tooLong = TimeChoices.Read("5000");
        Assert.AreEqual(1440, tooLong.Minutes);
        Assert.IsTrue(tooLong.Clamped);
        // Far past any int, without overflowing.
        var huge = TimeChoices.Read("99999999999999999999999999");
        Assert.AreEqual(1440, huge.Minutes);
        Assert.IsTrue(huge.Clamped);
    }

    [TestMethod]
    public void ClampedTimesAreWhatTheConfigKeeps()
    {
        foreach (string text in new[] { "0", "1", "45", "1440", "1441", "9999" })
        {
            int minutes = TimeChoices.Read(text).Minutes;
            var c = new Config { IdleMinutes = minutes, MusicVideo = Effect.Extra(minutes) };
            c.Clamp();
            Assert.AreEqual(minutes, c.IdleMinutes, text);
            Assert.AreEqual(Effect.Extra(minutes), c.MusicVideo, text);
        }
    }

    [TestMethod]
    public void ConfigTimesMapToTheirReadyMadeChoice()
    {
        Assert.AreEqual(15, TimeChoices.Match(15, TimeChoices.WelcomeLockAfter));
        Assert.AreEqual(60, TimeChoices.Match(60, TimeChoices.WelcomeLockAfter));
        Assert.AreEqual(120, TimeChoices.Match(120, TimeChoices.Extras));
        // Not ready-made in the welcome window, so it opens on Custom with the time in the box.
        Assert.IsNull(TimeChoices.Match(10, TimeChoices.WelcomeLockAfter));
        Assert.IsNull(TimeChoices.Match(45, TimeChoices.WelcomeLockAfter));
        Assert.IsNull(TimeChoices.Match(15.5, TimeChoices.WelcomeLockAfter));
        Assert.IsNull(TimeChoices.Match(0.5, TimeChoices.LockAfter));
        Assert.AreEqual(10, TimeChoices.Match(10, TimeChoices.LockAfter));
    }

    [TestMethod]
    public void TheDefaultsAreReadyMade()
    {
        var defaults = new Config();
        Assert.IsNotNull(TimeChoices.Match(defaults.IdleMinutes, TimeChoices.WelcomeLockAfter));
        Assert.IsNotNull(TimeChoices.Match(defaults.IdleMinutes, TimeChoices.LockAfter));
        CollectionAssert.Contains(TimeChoices.Warning, defaults.GraceSeconds);
    }

    // The settings window's lists hold their own times (D16). A time the welcome offers that is not
    // one of them, such as 2 minutes, shows in the list as itself, never as Custom.
    [TestMethod]
    [DoNotParallelize]
    public void EveryWelcomeChoiceShowsAsItselfNeverAsCustom()
    {
        var was = Lang.Current;
        try
        {
            foreach (string language in new[] { "en", "ar" })
            {
                Lang.Use(language);
                var text = Lang.Current;
                foreach (int minutes in TimeChoices.WelcomeLockAfter)
                {
                    var list = new MainList(() => AutostartState.On);
                    using var built = list.Build(new Config { IdleMinutes = minutes }, text);
                    Assert.AreEqual(text.OptionDuration(minutes), list.LockAfter!.Text, $"{language} {minutes} min");
                    Assert.AreNotEqual(text.V1.Custom, list.LockAfter.Text);
                    Assert.AreEqual((double)minutes, list.LockAfter.Value);
                }
                foreach (int minutes in TimeChoices.Extras)
                {
                    var list = new MainList(() => AutostartState.On);
                    using var built = list.Build(new Config { MusicVideo = Effect.Extra(minutes) }, text);
                    Assert.AreEqual(text.ExtraLabel(minutes), list.MusicVideo!.Text, $"{language} +{minutes} min");
                    Assert.AreNotEqual(text.V1.Custom, list.MusicVideo.Text);
                    Assert.AreEqual(Effect.Extra(minutes), list.MusicVideo.Value);
                }
            }
        }
        finally
        {
            Lang.Use(was.RightToLeft ? "ar" : "en");
        }
    }

    [TestMethod]
    public void EveryChoiceIsInRangeAndInOrder()
    {
        foreach (var list in new[] { TimeChoices.WelcomeLockAfter, TimeChoices.LockAfter, TimeChoices.Extras })
        {
            Assert.IsTrue(list.All(m => m is >= TimeChoices.Shortest and <= TimeChoices.Longest));
            CollectionAssert.AreEqual(list.Order().Distinct().ToArray(), list);
        }
        Assert.IsTrue(TimeChoices.Warning.All(s => s is > 0 and <= TimeChoices.LongestWarning));
        CollectionAssert.AreEqual(TimeChoices.Warning.Order().Distinct().ToArray(), TimeChoices.Warning);
        // D16: the settings window's own lists.
        CollectionAssert.AreEqual(new[] { 5, 10, 15, 30, 60 }, TimeChoices.LockAfter);
        CollectionAssert.AreEqual(new[] { 15, 30, 60, 120 }, TimeChoices.Extras);
        CollectionAssert.AreEqual(new[] { 10, 15, 30, 60 }, TimeChoices.Warning);
    }

    [TestMethod]
    public void HintSaysWhatIsUsed()
    {
        var en = English.Text;
        Assert.AreEqual("From 1 to 1440 minutes.", TimeChoices.Hint(en, TimeChoices.Read("45"), 15));
        Assert.AreEqual("That's 1 hour 30 minutes.", TimeChoices.Hint(en, TimeChoices.Read("90"), 15));
        Assert.AreEqual("Only 1 to 1440 minutes work, so it uses 24 hours.", TimeChoices.Hint(en, TimeChoices.Read("5000"), 15));
        Assert.AreEqual("Only 1 to 1440 minutes work, so it uses 1 minute.", TimeChoices.Hint(en, TimeChoices.Read("0"), 15));
        Assert.AreEqual("Type a number of minutes. Until then it uses 15 minutes.", TimeChoices.Hint(en, TimeChoices.Read(""), 15));
        Assert.AreEqual("Type a number of minutes. Until then it uses 2 hours.", TimeChoices.Hint(en, TimeChoices.Read("abc"), 120));
        var ar = Arabic.Text;
        Assert.AreEqual("المدة: ساعتان", TimeChoices.Hint(ar, TimeChoices.Read("120"), 15));
        Assert.AreEqual("اكتب عدد الدقائق. القيمة المستخدمة الآن: دقيقتان.", TimeChoices.Hint(ar, TimeChoices.Read(""), 2));
    }

    [TestMethod]
    public void TheRangeInTheTextIsTheRealOne()
    {
        foreach (var strings in new[] { English.Text, Arabic.Text })
        {
            foreach (string line in new[] { strings.MinutesRange, strings.MinutesClamped })
            {
                Assert.Contains(TimeChoices.Shortest.ToString(), line);
                Assert.Contains(TimeChoices.Longest.ToString(), line);
            }
        }
    }

    [TestMethod]
    public void TimesFromTheFileShowAsTheyAre()
    {
        Assert.AreEqual("45", TimeChoices.Show(45));
        Assert.AreEqual("0.5", TimeChoices.Show(0.5));
        Assert.AreEqual("That's 30 seconds.", TimeChoices.Describe(English.Text, 0.5));
        Assert.AreEqual("From 1 to 1440 minutes.", TimeChoices.Describe(English.Text, 45));
        Assert.AreEqual("That's 2 hours.", TimeChoices.Describe(English.Text, 120));
        // A minute or more is said to the nearest minute, not cut down to the one below.
        Assert.AreEqual("That's 2 minutes.", TimeChoices.Describe(English.Text, 1.5));
        Assert.AreEqual("That's 1 hour 31 minutes.", TimeChoices.Describe(English.Text, 90.5));
        Assert.AreEqual("Type a number of minutes. Until then it uses 2 minutes.", TimeChoices.Hint(English.Text, TimeChoices.Read(""), 1.5));
    }

    [TestMethod]
    public void ArrowsStepAWholeMinute()
    {
        Assert.AreEqual(16, TimeChoices.Step(TimeChoices.Read("15"), 15, up: true));
        Assert.AreEqual(14, TimeChoices.Step(TimeChoices.Read("15"), 15, up: false));
        // At the ends of the range the step stays there.
        Assert.AreEqual(1, TimeChoices.Step(TimeChoices.Read("1"), 1, up: false));
        Assert.AreEqual(1440, TimeChoices.Step(TimeChoices.Read("1440"), 1440, up: true));
        // A number out of range steps from the end it was brought to.
        Assert.AreEqual(1439, TimeChoices.Step(TimeChoices.Read("5000"), 1440, up: false));
        // An empty box steps from the time in use.
        Assert.AreEqual(31, TimeChoices.Step(TimeChoices.Read(""), 30, up: true));
    }

    [TestMethod]
    public void ArrowsFromAFractionGoThatWay()
    {
        // The box shows a fraction from the file as a decimal, which does not read as a number.
        Assert.AreEqual(3, TimeChoices.Step(TimeChoices.Read("2.5"), 2.5, up: true));
        Assert.AreEqual(2, TimeChoices.Step(TimeChoices.Read("2.5"), 2.5, up: false));
        Assert.AreEqual(1, TimeChoices.Step(TimeChoices.Read("0.5"), 0.5, up: true));
        // Under a minute there is no whole minute below.
        Assert.IsNull(TimeChoices.Step(TimeChoices.Read("0.5"), 0.5, up: false));
        Assert.IsNull(TimeChoices.Step(TimeChoices.Read("0.05"), 0.05, up: false));
    }
}
