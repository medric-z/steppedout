using System.Reflection;

namespace SteppedOut.Tests;

// The welcome window, built without showing it: no window is shown, nothing takes the keyboard, and
// nothing reaches the app but through a stand-in. Its pages, its defaults, every choice as it lands
// in the settings, the Custom… boxes, the Right now block, the timeline, a PC without a battery,
// Play with and without animations, and the keyboard. One at a time: these set the language, the
// theme and the window's seams, which every window reads.
[TestClass]
[DoNotParallelize]
public class WelcomeTests
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    Strings? languageBefore;

    [TestInitialize]
    public void Start()
    {
        languageBefore = Lang.Current;
        Lang.Use("en");
        Theme.UseForShots(false, 1f);
        WelcomeForm.HasBattery = () => true;
        WelcomeForm.Motion = () => true;
    }

    [TestCleanup]
    public void End()
    {
        Lang.Use(languageBefore?.RightToLeft == true ? "ar" : "en");
        Theme.UseForShots(false, 1f);
        WelcomeForm.HasBattery = Native.HasBattery;
        WelcomeForm.Motion = Native.AnimationsOn;
    }

    static T Field<T>(WelcomeForm form, string name) => (T)typeof(WelcomeForm).GetField(name, Private)!.GetValue(form)!;

    static void Call(WelcomeForm form, string method, params object[] args) =>
        typeof(WelcomeForm).GetMethod(method, Private)!.Invoke(form, args);

    // What a click does, on a window that is not on show, where PerformClick does nothing.
    static void Press(Control button) =>
        typeof(Control).GetMethod("OnClick", Private)!.Invoke(button, [EventArgs.Empty]);

    // The pill with this choice, picked as a click picks it.
    static void Pick(ChoiceChips chips, object choice) =>
        chips.Choose(chips.Chips.First(chip => !chip.Custom && Equals(chip.Choice, choice)));

    static void PickCustom(ChoiceChips chips) => chips.Choose(chips.CustomChip!);

    static ChoiceChips Chips(WelcomeForm form, string name) => Field<ChoiceChips>(form, name);

    // A window on these settings, with what it hands over when it finishes.
    sealed class Opened : IDisposable
    {
        public Opened(Config? config = null, bool startsWithWindows = true, ISettingsHost? host = null)
        {
            Form = new WelcomeForm(config ?? new Config(), startsWithWindows, host);
            Form.Finished += (c, start, settings) => Finishes.Add((c, start, settings));
        }

        public WelcomeForm Form { get; }

        public List<(Config Config, bool StartWithWindows, bool OpenSettings)> Finishes { get; } = new();

        public (Config Config, bool StartWithWindows, bool OpenSettings) Done()
        {
            Press(Field<KitButton>(Form, "done"));
            Assert.HasCount(1, Finishes);
            return Finishes[0];
        }

        public void Dispose() => Form.Dispose();
    }

    sealed class StandIn : ISettingsHost
    {
        public List<string> Calls { get; } = new();
        public void PauseChosen(TimeSpan? length) { }
        public void ResumeChosen() { }
        public void LockNowChosen() => Assert.Fail("the welcome never locks");
        public void TryItChosen() => Calls.Add("try");
        public void ShortcutChanged(string shortcut) => Calls.Add("shortcut " + shortcut);
        public void LanguageChanged(string language) { }
        public HotKeyResult ShortcutResult => HotKeyResult.Ok;
        public event Action? ShortcutResultChanged { add { } remove { } }
        public IReadOnlyList<WhyLine> Explain() => [];
        public void ConfigEdited(Config config) => Assert.Fail("the welcome saves nothing while open");
        public bool StartsWithWindows => false;
        public AutostartState StartWithWindowsState => AutostartState.Off;
        public event Action? StartWithWindowsChanged { add { } remove { } }
        public void StartWithWindowsChosen(bool on) => Assert.Fail("the app gives Windows the choice once the welcome finishes");
        public LockStatus Status => LockStatus.Starting(15);
        public event Action? StatusChanged { add { } remove { } }
    }

    // ---- Pages ----

    [TestMethod]
    public void StartNextBackAndSkipMoveBetweenThePages()
    {
        using var w = new Opened();
        var f = w.Form;
        Assert.AreEqual(1, f.Page);
        Assert.AreSame(Field<KitButton>(f, "startButton"), f.AcceptButton, "Enter is Start on the first page");
        Assert.IsFalse(Field<WelcomeFooter>(f, "footer").Visible, "no Back, Skip or Next on the first page");

        Press(Field<KitButton>(f, "startButton"));
        Assert.AreEqual(2, f.Page);
        Press(Field<KitButton>(f, "next"));
        Assert.AreEqual(3, f.Page);
        Press(Field<KitButton>(f, "back"));
        Assert.AreEqual(2, f.Page);
        Press(Field<KitButton>(f, "skip"));
        Assert.AreEqual(3, f.Page);
        Press(Field<KitButton>(f, "skip"));
        Assert.AreEqual(4, f.Page);
        Press(Field<KitButton>(f, "next"));
        Assert.AreEqual(5, f.Page);
        Press(Field<KitButton>(f, "back"));
        Assert.AreEqual(4, f.Page);
        Press(Field<KitButton>(f, "back"));
        Press(Field<KitButton>(f, "back"));
        Press(Field<KitButton>(f, "back"));
        Assert.AreEqual(1, f.Page);
        Assert.IsEmpty(w.Finishes, "moving between pages finishes nothing");
    }

    [TestMethod]
    public void EachPageHasItsButtonsAndEnterIsItsMainOne()
    {
        using var w = new Opened();
        var f = w.Form;
        var footer = Field<WelcomeFooter>(f, "footer");
        var dots = Field<StepDots>(f, "dots");
        for (int page = 2; page <= 4; page++)
        {
            f.GoTo(page);
            Assert.AreSame(Field<KitButton>(f, "next"), f.AcceptButton, $"page {page}");
            CollectionAssert.AreEquivalent(new[] { Field<KitButton>(f, "openSettings"), Field<KitButton>(f, "done") }, footer.Hidden.ToArray());
            Assert.AreEqual(page - 1, dots.Step);
            Assert.AreEqual($"Step {page - 1} of 4", dots.AccessibleName);
        }
        f.GoTo(5);
        Assert.AreSame(Field<KitButton>(f, "done"), f.AcceptButton);
        CollectionAssert.AreEquivalent(new[] { Field<KitButton>(f, "skip"), Field<KitButton>(f, "next") }, footer.Hidden.ToArray());
        Assert.AreEqual("Step 4 of 4", dots.AccessibleName);
    }

    // Skip goes on with this page's choices as they are: there is no skipping the whole welcome.
    [TestMethod]
    public void SkipKeepsThePagesChoices()
    {
        using var w = new Opened();
        var f = w.Form;
        f.GoTo(2);
        Pick(Chips(f, "lockAfter"), 30.0);
        Pick(Chips(f, "warning"), false);
        Press(Field<KitButton>(f, "skip"));
        Pick(Chips(f, "call"), Effect.Extra(60));
        Press(Field<KitButton>(f, "skip"));
        Press(Field<KitButton>(f, "skip"));
        Assert.AreEqual(5, f.Page);
        var (c, _, _) = w.Done();
        Assert.AreEqual(30, c.IdleMinutes);
        Assert.AreEqual(0, c.GraceSeconds);
        Assert.AreEqual(Effect.Extra(60), c.OnCall);
    }

    // Closing the window, with its X or Escape, keeps what is chosen so far (I31).
    [TestMethod]
    public void ClosingKeepsTheChoicesSoFar()
    {
        foreach (bool escape in new[] { false, true })
        {
            using var w = new Opened();
            var f = w.Form;
            _ = f.Handle;
            f.GoTo(2);
            Pick(Chips(f, "lockAfter"), 5.0);
            if (escape)
            {
                var message = Message.Create(f.Handle, 0x0100, (IntPtr)(int)Keys.Escape, IntPtr.Zero);
                var args = new object[] { message, Keys.Escape };
                Assert.IsTrue((bool)typeof(WelcomeForm).GetMethod("ProcessCmdKey", Private)!.Invoke(f, args)!);
            }
            else f.Close();
            Assert.HasCount(1, w.Finishes, escape ? "Escape" : "X");
            Assert.AreEqual(5, w.Finishes[0].Config.IdleMinutes);
            Assert.IsTrue(w.Finishes[0].Config.Welcomed);
            Assert.IsFalse(w.Finishes[0].OpenSettings);
        }
    }

    [TestMethod]
    public void DoneAndOpenSettingsFinishOnce()
    {
        using (var w = new Opened(startsWithWindows: false))
        {
            w.Form.GoTo(5);
            var (c, start, settings) = w.Done();
            Assert.IsTrue(c.Welcomed);
            Assert.IsFalse(start);
            Assert.IsFalse(settings);
            w.Form.Close();
            Assert.HasCount(1, w.Finishes, "closing after Done finishes nothing more");
        }
        using (var w = new Opened())
        {
            w.Form.GoTo(5);
            Press(Field<KitButton>(w.Form, "openSettings"));
            Assert.HasCount(1, w.Finishes);
            Assert.IsTrue(w.Finishes[0].OpenSettings);
            Assert.IsTrue(w.Finishes[0].StartWithWindows);
        }
    }

    // Quitting the app is not finishing the welcome: it shows again next time.
    [TestMethod]
    public void CloseWithoutFinishingHandsNothingOver()
    {
        using var w = new Opened();
        _ = w.Form.Handle;
        w.Form.CloseWithoutFinishing();
        Assert.IsEmpty(w.Finishes);
    }

    // ---- Defaults and choices ----

    [TestMethod]
    public void ANewPCOpensOnTheUsualChoices()
    {
        using var w = new Opened(new Config { Welcomed = false });
        var f = w.Form;
        Assert.AreEqual(15.0, Chips(f, "lockAfter").Value);
        Assert.IsTrue((bool)Chips(f, "warning").Value!, "the 15-second countdown");
        Assert.AreEqual("15-second countdown", Chips(f, "warning").Chips.First().Text);
        Assert.AreEqual("", Chips(f, "shortcut").Value, "no shortcut");
        Assert.AreEqual(Effect.Never, Chips(f, "music").Value);
        Assert.AreEqual(Effect.Never, Chips(f, "call").Value);
        Assert.AreEqual(Effect.Never, Chips(f, "fullScreen").Value);
        Assert.AreEqual(Effect.None, Chips(f, "battery").Value, "lock as usual");
        Assert.AreEqual(MediaOnLock.PauseUntilBack, Chips(f, "media").Value, "play again when you're back");
        Assert.IsFalse(Field<Toggle>(f, "mute").Checked);
        Assert.IsFalse(Field<Toggle>(f, "screenOff").Checked);
        Assert.IsTrue(Field<Toggle>(f, "timeAway").Checked);
        Assert.IsTrue(Field<Toggle>(f, "startWithWindows").Checked);
        foreach (var chips in new[] { "lockAfter", "music", "call", "fullScreen", "battery", "shortcut" })
            Assert.AreEqual(Chips(f, chips).CustomChip!.Plain, Chips(f, chips).CustomChip!.Text, chips + " shows Custom… as itself");

        var (c, start, _) = w.Done();
        var expected = new Config { Welcomed = true };
        Assert.IsTrue(expected.SameAs(c), "Done on the usual choices writes the defaults, welcomed");
        Assert.IsTrue(start);
    }

    // Every question on every page has its line under the title, and says it to a screen reader.
    [TestMethod]
    public void EveryQuestionHasALine()
    {
        using var w = new Opened();
        var all = Every(w.Form).ToList();
        var questions = all.OfType<WelcomeQuestion>().Select(q => (q.Title, q.Detail, Chooser: (Control)q.Chips))
            .Concat(all.OfType<ChipsCard>().Select(q => (q.Title, q.Detail, Chooser: (Control)q.Chips)))
            .ToList();
        var tiles = all.OfType<ChoiceTile>().Select(t => (t.Title, t.Detail)).ToList();
        Assert.HasCount(4, tiles);
        Assert.IsGreaterThanOrEqualTo(8, questions.Count);
        foreach (var (title, detail, chooser) in questions)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(detail), title + " has a line");
            Assert.AreEqual(detail, chooser.AccessibleDescription, title + " says its line");
        }
        foreach (var (title, detail) in tiles) Assert.IsFalse(string.IsNullOrWhiteSpace(detail), title + " has a line");
        Assert.AreEqual("Time without using the mouse or keyboard", questions.Single(q => q.Title == "Lock the PC after").Detail);
        Assert.AreEqual(Lang.Current.V1.LockAfterDetail, questions.Single(q => q.Title == "Lock the PC after").Detail, "the line the settings show");
    }

    static IEnumerable<Control> Every(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(c => Every(c).Prepend(c));

    // The sounds stay off: a welcome finished on its usual choices, or closed, sets none.
    [TestMethod]
    public void TheWelcomeLeavesTheSoundsOff()
    {
        foreach (bool close in new[] { false, true })
        {
            using var w = new Opened(Config.FirstStart());
            _ = w.Form.Handle;
            Config c;
            if (close)
            {
                w.Form.Close();
                Assert.HasCount(1, w.Finishes);
                c = w.Finishes[0].Config;
            }
            else
            {
                w.Form.GoTo(5);
                c = w.Done().Config;
            }
            Assert.AreEqual(ActionSound.Off, c.SoundWhenAway);
            Assert.AreEqual(ActionSound.Off, c.SoundWhenBack);
            Assert.AreEqual(BreakSound.Off, c.BreakSound);
            Assert.IsTrue(c.BreakReminders, "a brand-new install's reminders stay on");
            Assert.AreEqual(120, c.BreakMinutes);
        }
    }

    // ---- Break reminders ----

    // On a brand-new install the away page offers break reminders on, every 2 hours: Off, the
    // ready-made times of the Breaks tab, and Custom…, each landing in the settings.
    [TestMethod]
    public void BreakRemindersAreOnEveryTwoHoursAndEachChoiceLands()
    {
        using var w = new Opened(Config.FirstStart());
        var f = w.Form;
        CustomBox? box = null;
        f.Presenter = b => box = b;
        var breaks = Chips(f, "breaks");
        Assert.AreEqual(120, breaks.Value);
        CollectionAssert.AreEqual(
            new[] { "Off", "30 minutes", "45 minutes", "1 hour", "1 hour 30 minutes", "2 hours", "4 hours", "Custom…" },
            breaks.Chips.Select(c => c.Text).ToArray());
        CollectionAssert.AreEqual(new[] { 0 }.Concat(TimeChoices.Breaks).ToArray(), breaks.Chips.Where(c => !c.Custom).Select(c => (int)c.Choice!).ToArray());
        Assert.AreEqual("Break reminders", breaks.AccessibleName);

        f.GoTo(4);
        Pick(breaks, 0);
        Assert.IsFalse(f.Choices.BreakReminders);
        Assert.AreEqual(120, f.Choices.BreakMinutes, "Off keeps the time");
        Pick(breaks, 30);
        Assert.IsTrue(f.Choices.BreakReminders);
        Assert.AreEqual(30, f.Choices.BreakMinutes);

        void Type(string number)
        {
            box = null;
            PickCustom(breaks);
            Assert.IsNotNull(box);
            StringAssert.StartsWith(box.Field.Controls.OfType<TextBox>().Single().AccessibilityObject.Name, "Show a reminder after");
            box.Field.Controls.OfType<TextBox>().Single().Text = number;
            box.Commit();
            box.Dispose();
        }
        Type("50");
        Assert.IsTrue(f.Choices.BreakReminders);
        Assert.AreEqual(50, f.Choices.BreakMinutes);
        Assert.AreEqual("50 minutes", breaks.CustomChip!.Text);
        Type("99999");
        Assert.AreEqual(TimeChoices.LongestBreak, f.Choices.BreakMinutes);
        Type("0");
        Assert.AreEqual(TimeChoices.ShortestBreak, f.Choices.BreakMinutes);
        Type("45");
        Assert.AreEqual(45, breaks.Value, "a ready-made time picks its own pill");
        Assert.AreEqual("Custom…", breaks.CustomChip!.Text);

        f.GoTo(5);
        CollectionAssert.Contains(f.PickedLines.ToList(), "Break reminders: 45 minutes");
        var (c, _, _) = w.Done();
        Assert.IsTrue(c.BreakReminders);
        Assert.AreEqual(45, c.BreakMinutes);
    }

    // Settings with reminders off, from a file before the key or turned off, show Off and keep it.
    [TestMethod]
    public void BreakRemindersOffStayOff()
    {
        using var w = new Opened(new Config { BreakMinutes = 90 });
        Assert.AreEqual(0, Chips(w.Form, "breaks").Value);
        w.Form.GoTo(5);
        CollectionAssert.Contains(w.Form.PickedLines.ToList(), "Break reminders: Off");
        var (c, _, _) = w.Done();
        Assert.IsFalse(c.BreakReminders);
        Assert.AreEqual(90, c.BreakMinutes);
    }

    // Nothing carries over from before 1.0 (D50): such a file loads as the defaults, so the welcome
    // opens on its usual choices.
    [TestMethod]
    public void AFileFromBefore10OpensOnTheUsualChoices()
    {
        string folder = Path.Combine(Path.GetTempPath(), "SteppedOut-welcome-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "config.json");
            File.WriteAllText(path, "{ \"whilePlaying\": \"moreTime\", \"moreTimeMinutes\": 100, \"whileOnCall\": \"lockAnyway\", \"idleMinutes\": 30 }");
            var settings = Config.Load(path, out _);
            FreshStart.Apply(settings, path, persist: false);
            using var w = new Opened(settings);
            Assert.AreEqual(15.0, Chips(w.Form, "lockAfter").Value);
            Assert.AreEqual(Effect.Never, Chips(w.Form, "music").Value);
            Assert.AreEqual(Effect.Never, Chips(w.Form, "call").Value);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    // Shown again from the settings, the welcome opens on the settings in use.
    [TestMethod]
    public void TheSettingsInUseShowAsTheirChoices()
    {
        var c = new Config
        {
            Welcomed = true,
            IdleMinutes = 45,
            GraceSeconds = 30,
            LockShortcut = "Ctrl+Shift+K",
            MusicVideo = Effect.Extra(90),
            OnCall = Effect.None,
            FullScreen = Effect.Extra(30),
            OnBattery = Effect.Sooner(40),
            MediaOnLock = MediaOnLock.KeepPlaying,
            MuteWhenAway = true,
        };
        using var w = new Opened(c, startsWithWindows: false);
        var f = w.Form;
        var text = Lang.Current;
        Assert.AreEqual(45.0, Chips(f, "lockAfter").Value);
        Assert.AreEqual("45 minutes", Chips(f, "lockAfter").CustomChip!.Text);
        Assert.AreEqual(text.SecondsOption(30), Chips(f, "warning").Chips.First().Text, "the countdown as it is set");
        Assert.AreEqual("Ctrl+Shift+K", Chips(f, "shortcut").CustomChip!.Text);
        Assert.AreEqual(text.ExtraLabel(90), Chips(f, "music").CustomChip!.Text);
        Assert.AreEqual(Effect.None, Chips(f, "call").Value);
        Assert.AreEqual(Effect.Extra(30), Chips(f, "fullScreen").Value);
        StringAssert.StartsWith(Chips(f, "battery").CustomChip!.Text, "40% sooner");
        Assert.IsFalse(Field<Toggle>(f, "startWithWindows").Checked);

        // Done hands the same settings back, the warning kept at 30 seconds.
        var (back, _, _) = w.Done();
        c.Welcomed = true;
        Assert.IsTrue(c.SameAs(back));
    }

    [TestMethod]
    public void EveryChoiceLandsInTheSettings()
    {
        var host = new StandIn();
        using var w = new Opened(new Config { WhenLocking = WhenLocking.LockScreenOff }, host: host);
        var f = w.Form;
        f.GoTo(2);
        foreach (double minutes in new[] { 5.0, 30.0, 60.0 })
        {
            Pick(Chips(f, "lockAfter"), minutes);
            Assert.AreEqual(minutes, f.Choices.LockAfter);
        }
        Pick(Chips(f, "warning"), false);
        Pick(Chips(f, "shortcut"), "Ctrl+Alt+L");
        f.GoTo(3);
        Pick(Chips(f, "music"), Effect.Extra(15));
        Pick(Chips(f, "call"), Effect.None);
        Pick(Chips(f, "fullScreen"), Effect.Extra(120));
        Pick(Chips(f, "battery"), Effect.Sooner(50));
        f.GoTo(4);
        Pick(Chips(f, "media"), MediaOnLock.KeepPlaying);
        Field<Toggle>(f, "mute").Checked = true;
        Field<Toggle>(f, "screenOff").Checked = true;
        Field<Toggle>(f, "timeAway").Checked = false;
        f.GoTo(5);
        Field<Toggle>(f, "startWithWindows").Checked = false;

        var (c, start, _) = w.Done();
        Assert.AreEqual(60, c.IdleMinutes);
        Assert.AreEqual(0, c.GraceSeconds);
        Assert.AreEqual("Ctrl+Alt+L", c.LockShortcut);
        Assert.AreEqual(Effect.Extra(15), c.MusicVideo);
        Assert.AreEqual(Effect.None, c.OnCall);
        Assert.AreEqual(Effect.Extra(120), c.FullScreen);
        Assert.AreEqual(Effect.Sooner(50), c.OnBattery);
        Assert.AreEqual(MediaOnLock.KeepPlaying, c.MediaOnLock);
        Assert.IsTrue(c.MuteWhenAway);
        Assert.IsTrue(c.ScreensOffWhenAway);
        Assert.IsFalse(c.ShowTimeAway);
        Assert.IsFalse(start);
        Assert.AreEqual(WhenLocking.LockScreenOff, c.WhenLocking, "what the welcome does not ask stays");
        Assert.IsEmpty(host.Calls, "a ready-made shortcut is taken once the welcome closes, not before");

        // The summary says each of them.
        var lines = f.PickedLines.ToList();
        CollectionAssert.Contains(lines, "Lock the PC after: 1 hour");
        CollectionAssert.Contains(lines, "Warn me before it locks: No warning");
        CollectionAssert.Contains(lines, "Keyboard shortcut to lock now: Ctrl+Alt+L");
        CollectionAssert.Contains(lines, "While music or video is playing: +15 minutes");
        CollectionAssert.Contains(lines, "While you're on a call: No extra time");
        CollectionAssert.Contains(lines, "Mute the PC: On");
        CollectionAssert.Contains(lines, "Show how long you were away: Off");
        Assert.IsTrue(lines.Any(l => l.StartsWith("On battery power: 50% sooner (30 min)", StringComparison.Ordinal)), string.Join("\n", lines));
    }

    // Each battery choice says the lock time it comes to, from the lock time chosen.
    [TestMethod]
    public void TheBatteryChoicesSayTheirRealTimes()
    {
        using var w = new Opened(new Config { IdleMinutes = 20 });
        var battery = Chips(w.Form, "battery");
        Assert.AreEqual("25% sooner (15 min)", battery.Chips.Single(c => Equals(c.Choice, Effect.Sooner(25))).Text);
        Assert.AreEqual("50% sooner (10 min)", battery.Chips.Single(c => Equals(c.Choice, Effect.Sooner(50))).Text);
        Assert.AreEqual("75% sooner (5 min)", battery.Chips.Single(c => Equals(c.Choice, Effect.Sooner(75))).Text);
        w.Form.GoTo(2);
        Pick(Chips(w.Form, "lockAfter"), 60.0);
        Assert.AreEqual("50% sooner (30 min)", battery.Chips.Single(c => Equals(c.Choice, Effect.Sooner(50))).Text);
    }

    // ---- Custom… ----

    // The box under a Custom… pill: whole numbers in its range, and a number outside it brought to
    // the nearest end.
    [TestMethod]
    public void CustomBoxesTakeTheirRangeAndShowTheirValue()
    {
        using var w = new Opened();
        var f = w.Form;
        CustomBox? box = null;
        f.Presenter = b => box = b;

        void Type(ChoiceChips chips, string number)
        {
            box = null;
            PickCustom(chips);
            Assert.IsNotNull(box);
            box.Field.Controls.OfType<TextBox>().Single().Text = number;
            box.Commit();
            box.Dispose();
        }

        var lockAfter = Chips(f, "lockAfter");
        Type(lockAfter, "45");
        Assert.AreEqual(45, f.Choices.LockAfter);
        Assert.AreEqual(45.0, lockAfter.Value);
        Assert.AreEqual("45 minutes", lockAfter.CustomChip!.Text);
        Type(lockAfter, "5000");
        Assert.AreEqual(1440, f.Choices.LockAfter, "brought to a day");
        Type(lockAfter, "15");
        Assert.AreEqual(15.0, lockAfter.Value, "a ready-made time picks its own pill");
        Assert.AreEqual("Custom…", lockAfter.CustomChip!.Text);
        Type(lockAfter, "0");
        Assert.AreEqual(1, f.Choices.LockAfter, "brought to a minute");

        var music = Chips(f, "music");
        Type(music, "45");
        Assert.AreEqual(Effect.Extra(45), f.Choices.Music);
        Assert.AreEqual("+45 minutes", music.CustomChip!.Text);
        Type(music, "99999");
        Assert.AreEqual(Effect.Extra(Config.MaxEffectMinutes), f.Choices.Music);

        var battery = Chips(f, "battery");
        Type(battery, "99");
        Assert.AreEqual(Effect.Sooner(Config.MaxSoonerPercent), f.Choices.Battery, "95% at most");
        Type(battery, "0");
        Assert.AreEqual(Effect.Sooner(Config.MinSoonerPercent), f.Choices.Battery, "1% at least");
        Type(battery, "40");
        StringAssert.StartsWith(battery.CustomChip!.Text, "40% sooner (");

        // Escape leaves the choice as it was.
        box = null;
        PickCustom(lockAfter);
        box!.Field.Controls.OfType<TextBox>().Single().Text = "90";
        box.Cancel();
        box.Dispose();
        Assert.AreEqual(1, f.Choices.LockAfter);
        var (c, _, _) = w.Done();
        Assert.AreEqual(1, c.IdleMinutes);
        Assert.AreEqual(Effect.Sooner(40), c.OnBattery);
    }

    [TestMethod]
    public void TheBoxesSayWhatTheyAreFor()
    {
        using var w = new Opened();
        CustomBox? box = null;
        w.Form.Presenter = b => box = b;
        PickCustom(Chips(w.Form, "battery"));
        Assert.AreEqual("On battery power, as a percent sooner", box!.Field.Controls.OfType<TextBox>().Single().AccessibilityObject.Name);
        box.Dispose();
        PickCustom(Chips(w.Form, "lockAfter"));
        Assert.AreEqual("Lock the PC after, in minutes", box!.Field.Controls.OfType<TextBox>().Single().AccessibilityObject.Name);
        box.Dispose();
    }

    // "Choose another shortcut…" registers it through the app's own window; leaving it for another
    // choice lets go of it until the welcome closes.
    [TestMethod]
    public void AnotherShortcutShowsAndLandsInTheSettings()
    {
        var host = new StandIn();
        using var w = new Opened(host: host);
        var f = w.Form;
        f.ChooseShortcut = now =>
        {
            Assert.AreEqual("", now);
            return "Ctrl+Shift+K";
        };
        f.GoTo(2);
        var shortcut = Chips(f, "shortcut");
        PickCustom(shortcut);
        Assert.AreEqual("Ctrl+Shift+K", f.Choices.Shortcut);
        Assert.AreEqual("Ctrl+Shift+K", shortcut.CustomChip!.Text);
        Assert.AreEqual("Ctrl+Shift+K", shortcut.Value);
        f.ChooseShortcut = _ => null;
        PickCustom(shortcut);
        Assert.AreEqual("Ctrl+Shift+K", f.Choices.Shortcut, "closing the window without one keeps the one chosen");
        Assert.AreEqual("Ctrl+Shift+K", w.Done().Config.LockShortcut);
    }

    // ---- Right now ----

    [TestMethod]
    public void RightNowAddsTheExtrasToTheLockTime()
    {
        using var w = new Opened(new Config { MusicVideo = Effect.Extra(30), FullScreen = Effect.Extra(60) });
        var f = w.Form;
        f.GoTo(3);
        Assert.AreEqual(Situation.Music, f.Previewing, "music or video is ticked to start with");
        Assert.AreEqual("Locks after 45 minutes", f.Stage.Headline);
        Assert.AreEqual("15 min + 30 min (music or video)", f.Stage.Detail);
        Tick(f, Situation.FullScreen, true);
        Assert.AreEqual("Locks after 1 hour 45 minutes", f.Stage.Headline);
        Assert.AreEqual("15 min + 30 min (music or video) + 1 hr (full screen)", f.Stage.Detail);
        Tick(f, Situation.Music, false);
        Tick(f, Situation.FullScreen, false);
        Assert.AreEqual("Locks after 15 minutes", f.Stage.Headline);
        Assert.AreEqual("", f.Stage.Detail, "the lock time alone needs no sum");
    }

    [TestMethod]
    public void NeverLockSaysWhatHoldsIt()
    {
        using var w = new Opened();
        w.Form.GoTo(3);
        Assert.AreEqual("Never locks during music or video", w.Form.Stage.Headline);
        Assert.AreEqual("", w.Form.Stage.Detail);
        Pick(Chips(w.Form, "call"), Effect.Extra(30));
        Assert.AreEqual(Situation.Call, w.Form.Previewing, "picking a choice previews that thing");
        Assert.AreEqual("Locks after 45 minutes", w.Form.Stage.Headline);
        Pick(Chips(w.Form, "call"), Effect.Never);
        Assert.AreEqual("Never locks during a call", w.Form.Stage.Headline);
    }

    // On battery the line under the time only says how much sooner every time is, never "0%".
    [TestMethod]
    public void OnBatteryEveryTimeIsSooner()
    {
        using var w = new Opened(new Config { MusicVideo = Effect.Extra(30) });
        var f = w.Form;
        f.GoTo(3);
        Tick(f, Situation.Battery, true);
        Assert.AreEqual("15 min + 30 min (music or video)", f.Stage.Detail, "lock as usual: no percent at all");
        Assert.DoesNotContain("%", f.Stage.Detail);
        Pick(Chips(f, "battery"), Effect.Sooner(50));
        Assert.AreEqual("On battery, all times are 50% sooner.", f.Stage.Detail);
        StringAssert.StartsWith(f.Stage.Headline, "Locks after 23 min");
        Pick(Chips(f, "battery"), Effect.Sooner(25));
        Assert.AreEqual("On battery, all times are 25% sooner.", f.Stage.Detail);
        Tick(f, Situation.Battery, false);
        Assert.AreEqual("15 min + 30 min (music or video)", f.Stage.Detail);
        Pick(Chips(f, "battery"), Effect.None);
        Assert.AreEqual(Situation.Music | Situation.Battery, f.Previewing, "a battery choice ticks On battery");
        Assert.DoesNotContain("0%", f.Stage.Detail);
    }

    static void Tick(WelcomeForm f, Situation one, bool on)
    {
        var chips = f.Stage.Situations;
        if (chips.IsChecked(one) != on) chips.Choose(chips.Chips.First(c => Equals(c.Choice, one)));
        Assert.AreEqual(on, f.Previewing.HasFlag(one));
    }

    // ---- The timeline ----

    // The stretch before Locks grows with the time, more slowly the longer it is, and never past
    // its cap, so the stops after it always fit.
    [TestMethod]
    public void TheStretchGrowsWithTheTimeOnACappedScale()
    {
        float Stretch(double minutes, Effect music = default)
        {
            var choices = new WelcomeChoices { LockAfter = minutes, Music = music };
            var line = TimelineLayout.Compute(choices, music.Kind == EffectKind.None ? Situation.None : Situation.Music, 704);
            Assert.IsFalse(line.Never);
            Assert.IsGreaterThanOrEqualTo(2 * TimelineLayout.MinAfter - 1, line.Back - line.Locked, $"{minutes}: room for away and back");
            return line.StretchEnd - line.Start;
        }

        float five = Stretch(5), fifteen = Stretch(15), hour = Stretch(60), day = Stretch(1440);
        Assert.IsTrue(five <= fifteen && fifteen < hour && hour <= day, $"{five} {fifteen} {hour} {day}");
        Assert.IsLessThan(3f, hour / fifteen, "a square-root scale: four times the time is about twice the length");
        Assert.AreEqual(Stretch(1440), Stretch(100000), 0.5, "capped");
        Assert.IsGreaterThan(fifteen, Stretch(15, Effect.Extra(60)), "an extra lengthens it");
        var withExtra = TimelineLayout.Compute(new WelcomeChoices { Music = Effect.Extra(60) }, Situation.Music, 704);
        Assert.HasCount(2, withExtra.Segments);
        Assert.IsGreaterThanOrEqualTo(TimelineLayout.ShortestExtra, withExtra.Segments[1].Width);
        Assert.AreEqual(Situation.Music, withExtra.Segments[1].Source);
    }

    // Never lock: a striped bar that runs off the edge, with only the first stop left.
    [TestMethod]
    public void NeverLockRunsOffTheEdge()
    {
        using var w = new Opened();
        var f = w.Form;
        _ = f.Handle;
        f.Fit();
        f.GoTo(3);
        Assert.IsGreaterThanOrEqualTo(f.ClientSize.Width - SystemInformation.VerticalScrollBarWidth, f.Timeline.Width, "as wide as the window");
        var line = f.Timeline.Line;
        Assert.IsTrue(line.Never);
        Assert.IsTrue(float.IsNaN(line.Locked));
        foreach (WelcomeTimeline.Stop stop in f.Timeline.Stops)
            Assert.AreEqual(stop.Moment == Moment.Idle, stop.Enabled, stop.Moment.ToString());

        var drawn = Drawn(f.Timeline);
        CollectionAssert.Contains(drawn, "Never locks");
        var card = Theme.Current.Card;
        using var picture = new Bitmap(f.Timeline.Width, f.Timeline.Height);
        using (var g = Graphics.FromImage(picture)) g.Clear(card);
        f.Timeline.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
        int y = TrackY(f.Timeline);
        bool striped = Enumerable.Range(f.Timeline.Width - 6, 6).Any(x => !Near(picture.GetPixel(x, y), card, 12));
        Assert.IsTrue(striped, "the stripes reach the window's edge");

        Tick(f, Situation.Music, false);
        CollectionAssert.DoesNotContain(Drawn(f.Timeline), "Never locks");
        using var after = new Bitmap(f.Timeline.Width, f.Timeline.Height);
        using (var g = Graphics.FromImage(after)) g.Clear(card);
        f.Timeline.DrawToBitmap(after, new Rectangle(Point.Empty, after.Size));
        Assert.IsTrue(Enumerable.Range(f.Timeline.Width - 6, 6).All(x => Near(after.GetPixel(x, y), card, 12)), "a time that locks ends at You're back");
        Assert.IsTrue(f.Timeline.Stops.All(s => s.Enabled));
    }

    static bool Near(Color a, Color b, int by) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) < by;

    static int TrackY(WelcomeTimeline line) => (int)typeof(WelcomeTimeline).GetProperty("TrackY", Private)!.GetValue(line)!;

    // The text a control draws, as the render tool hears it.
    static List<string> Drawn(Control control)
    {
        var heard = new List<string>();
        KitControl.TextDrawn = (_, words, _, _, _) => heard.Add(words);
        try
        {
            using var picture = new Bitmap(Math.Max(1, control.Width), Math.Max(1, control.Height));
            control.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
        }
        finally
        {
            KitControl.TextDrawn = null;
        }
        return heard;
    }

    // With a shortcut chosen, the timeline says it locks at once.
    [TestMethod]
    public void TheShortcutLocksInstantlyOnTheTimeline()
    {
        using var w = new Opened();
        var f = w.Form;
        _ = f.Handle;
        f.Fit();
        f.GoTo(2);
        CollectionAssert.DoesNotContain(Drawn(f.Timeline), "Ctrl+Alt+L locks instantly");
        Pick(Chips(f, "shortcut"), "Ctrl+Alt+L");
        CollectionAssert.Contains(Drawn(f.Timeline), "Ctrl+Alt+L locks instantly");
        Assert.IsTrue(f.Stage.Parts.HasFlag(PcPart.Keys), "the keys show on the desk");
    }

    // Clicking a stop shows that moment on the PC, and what came with it is picked out.
    [TestMethod]
    public void AStopShowsItsMoment()
    {
        using var w = new Opened(new Config { MuteWhenAway = true });
        var f = w.Form;
        f.GoTo(2);
        Assert.AreEqual(Moment.Warning, f.Moment, "the Locking page opens on the warning");
        Assert.IsTrue(f.Stage.Parts.HasFlag(PcPart.Note));
        f.Timeline.Choose(f.Timeline.Stops[(int)Moment.Locked]);
        Assert.AreEqual(Moment.Locked, f.Moment);
        Assert.IsTrue(f.Stage.Parts.HasFlag(PcPart.Lock));
        Assert.IsTrue(f.Stage.Pointed.HasFlag(PcPart.Lock));
        Assert.AreEqual("Locks The PC locks by itself.", f.Stage.MomentLine);
        f.Timeline.Choose(f.Timeline.Stops[(int)Moment.Back]);
        Assert.IsTrue(f.Stage.Parts.HasFlag(PcPart.Toast), "show how long you were away");
        Pick(Chips(f, "warning"), false);
        Assert.IsFalse(f.Timeline.Stops[(int)Moment.Warning].Enabled, "no warning to show");
        Assert.AreEqual("No warning", f.Timeline.Stops[(int)Moment.Warning].AccessibleName);
    }

    // ---- Try it and Play ----

    [TestMethod]
    public void TryItShowsTheCountdownAndNeverLocks()
    {
        var host = new StandIn();
        using var w = new Opened(host: host);
        var f = w.Form;
        f.GoTo(2);
        Press(Field<KitButton>(f, "tryIt"));
        CollectionAssert.AreEqual(new[] { "try" }, host.Calls);
        Assert.IsTrue(f.Trying);
        Assert.IsTrue(f.Stage.Parts.HasFlag(PcPart.Note));
        for (int i = 0; i < Toast.PreviewSeconds; i++) f.TryTick();
        Assert.IsFalse(f.Trying);
        Pick(Chips(f, "warning"), false);
        Assert.IsFalse(Field<KitButton>(f, "tryIt").Enabled, "nothing to try without a warning");
    }

    // The line along the bottom of the countdown note starts full and runs down over the countdown's
    // own length, 30 seconds as much as 15, and over the 5 seconds of Try it, so it never runs past
    // the note.
    [TestMethod]
    public void TheCountdownNoteRunsDownFromItsOwnLength()
    {
        foreach (int seconds in new[] { 15, 30, 600 })
        {
            using var w = new Opened(new Config { GraceSeconds = seconds });
            var f = w.Form;
            f.GoTo(2);
            Assert.AreEqual(Moment.Warning, f.Moment);
            Assert.AreEqual(1f, f.Stage.NoteLeft, $"{seconds} s: the whole line to start with");
            Press(Field<KitButton>(f, "tryIt"));
            Assert.AreEqual(1f, f.Stage.NoteLeft, $"{seconds} s: Try it starts with the whole line too");
            f.TryTick();
            Assert.AreEqual(1f - 1f / Toast.PreviewSeconds, f.Stage.NoteLeft, 0.001f);
            for (int i = 1; i < Toast.PreviewSeconds; i++) f.TryTick();
            Assert.IsFalse(f.Trying);
            Assert.AreEqual(1f, f.Stage.NoteLeft, $"{seconds} s: back to the countdown's own line");
        }
    }

    [TestMethod]
    public void PlayWalksTheMomentsAndHoldsEach()
    {
        using var w = new Opened(new Config { MusicVideo = Effect.Extra(30) });
        var f = w.Form;
        f.GoTo(3);
        Assert.AreEqual(WelcomeForm.PlayHold, Field<System.Windows.Forms.Timer>(f, "playTimer").Interval);
        Press(f.Stage.Play);
        Assert.IsTrue(f.Playing);
        Assert.AreEqual("Stop", f.Stage.Play.Text);
        var seen = new List<Moment> { f.Moment };
        while (f.Playing)
        {
            f.PlayTick();
            if (f.Playing) seen.Add(f.Moment);
        }
        CollectionAssert.AreEqual(new[] { Moment.Idle, Moment.Warning, Moment.Locked, Moment.Away, Moment.Back }, seen);
        Assert.AreEqual(Moment.Back, f.Moment, "the last moment stays on show");
        Assert.AreEqual("Play", f.Stage.Play.Text);
        Assert.IsNull(f.Timeline.Playhead);
    }

    // With Windows' animations off nothing glides: Play steps from moment to moment.
    [TestMethod]
    public void NoGlidingWithoutAnimations()
    {
        foreach (bool motion in new[] { true, false })
        {
            WelcomeForm.Motion = () => motion;
            using var w = new Opened(new Config { MusicVideo = Effect.Extra(30) });
            var f = w.Form;
            f.GoTo(3);
            Press(f.Stage.Play);
            f.PlayTick();
            var glide = Field<System.Windows.Forms.Timer>(f, "glideTimer");
            Assert.AreEqual(motion, glide.Enabled, motion ? "glides" : "steps");
            if (!motion) Assert.AreEqual(f.Timeline.Line.X(f.Moment), f.Timeline.Playhead);
            Press(f.Stage.Play);
            Assert.IsFalse(f.Playing);
            Assert.IsFalse(glide.Enabled);
        }
    }

    // ---- A PC without a battery ----

    [TestMethod]
    public void WithoutABatteryNothingAsksAboutOne()
    {
        foreach (bool battery in new[] { true, false })
        {
            WelcomeForm.HasBattery = () => battery;
            using var w = new Opened();
            var f = w.Form;
            Assert.AreEqual(battery, f.HasBatteryShown);
            Assert.AreEqual(battery, f.Stage.Situations.Chips.Single(c => Equals(c.Choice, Situation.Battery)).Shown);
            f.GoTo(5);
            Assert.AreEqual(battery, f.PickedLines.Any(l => l.StartsWith("On battery power", StringComparison.Ordinal)));
            Assert.AreEqual(Effect.None, w.Done().Config.OnBattery);
        }
    }

    // ---- The keyboard ----

    // Tab goes through each page in reading order, then the buttons; every stop has a name.
    [TestMethod]
    public void TabFollowsTheReadingOrderAndEveryStopHasAName()
    {
        using var w = new Opened();
        var f = w.Form;
        var expected = new Dictionary<int, string[]>
        {
            [1] = ["Start"],
            [2] = ["Play", "Timeline", "Lock the PC after", "Warn me before it locks", "Try it", "Keyboard shortcut to lock now", "Back", "Skip", "Next"],
            [3] = ["Play", "Preview a situation", "Timeline", "While music or video is playing", "While you're on a call", "While an app is full screen", "On battery power", "Back", "Skip", "Next"],
            [4] = ["Pause music and video when you leave", "Mute the PC", "Turn off the screen", "Show how long you were away", "Break reminders", "Back", "Skip", "Next"],
            [5] = ["Start with Windows", "Back", "Open settings", "Done"],
        };
        foreach (var (page, names) in expected)
        {
            f.GoTo(page);
            var stops = Stops(f, f).ToList();
            CollectionAssert.AreEqual(names, stops.Select(Name).ToArray(), $"page {page}: " + string.Join(", ", stops.Select(Name)));
        }
    }

    static string Name(Control control) => control.AccessibilityObject.Name ?? "";

    static bool Selectable(Control control) =>
        (bool)typeof(Control).GetMethod("GetStyle", Private)!.Invoke(control, [ControlStyles.Selectable])!;

    // The Tab stops on the page on show, in Tab order.
    static IEnumerable<Control> Stops(WelcomeForm f, Control parent)
    {
        var footer = Field<WelcomeFooter>(f, "footer");
        var parts = (Control[])typeof(WelcomeForm).GetMethod("PartsOf", Private)!.Invoke(f, [f.Page])!;
        foreach (var child in parent.Controls.Cast<Control>().OrderBy(c => c.TabIndex))
        {
            if (child.Parent is WelcomeBody && !parts.Contains(child)) continue;
            if (child is WelcomeHead && !parts.Contains(f.Stage)) continue;
            if (child == footer && f.Page == 1) continue;
            if (child is KitButton b && footer.Hidden.Contains(b)) continue;
            if (child.Parent is PageStack stack && stack.Hidden.Contains(child)) continue;
            if (child == f.Stage.Situations && f.Page != 3) continue;
            if (child is PartGroup group)
            {
                if (group.Parts.Any(p => p.TabStop && p is not ChoiceChips.Chip { Shown: false }) && group.CanSelectByCode()) yield return group;
                continue;
            }
            if (child.TabStop && Selectable(child) && child.CanSelectByCode()) yield return child;
            foreach (var deeper in Stops(f, child)) yield return deeper;
        }
    }

    // The arrow keys go to the next stop that can be picked: past the warning there is none of, and
    // not to the stops that are not there while it never locks, which would leave Tab nowhere to land.
    [TestMethod]
    public void ArrowsOnTheTimelineSkipStopsThatCannotBePicked()
    {
        using (var w = new Opened(new Config { GraceSeconds = 0 }))
        {
            var f = w.Form;
            f.GoTo(2);
            var line = f.Timeline;
            line.Key(line.Stops[(int)Moment.Idle], Keys.Right);
            Assert.AreEqual(Moment.Locked, f.Moment, "no warning to stop at");
            Assert.AreEqual((int)Moment.Locked, line.Current);
            line.Key(line.Stops[(int)Moment.Locked], Keys.Left);
            Assert.AreEqual(Moment.Idle, f.Moment);
            Assert.AreEqual((int)Moment.Idle, line.Current);
            line.Key(line.Stops[(int)Moment.Idle], Keys.End);
            Assert.AreEqual(Moment.Back, f.Moment);
            line.Key(line.Stops[(int)Moment.Back], Keys.Home);
            Assert.AreEqual(Moment.Idle, f.Moment);
        }
        using (var w = new Opened())
        {
            var f = w.Form;
            f.GoTo(3);
            Assert.IsTrue(f.Choices.Preview(f.Previewing).NeverLocks, "it never locks to start with");
            var line = f.Timeline;
            foreach (var key in new[] { Keys.Right, Keys.Down, Keys.End, Keys.Left, Keys.Home })
            {
                line.Key(line.Stops[(int)Moment.Idle], key);
                Assert.AreEqual((int)Moment.Idle, line.Current, $"{key}: the one stop there is stays the one Tab lands on");
                Assert.AreEqual(Moment.Idle, f.Moment);
            }
        }
    }

    // On a PC without a battery its toggle is not there, and the arrow keys do not go to it.
    [TestMethod]
    public void ArrowsOnTheTogglesSkipTheOneThatIsNotThere()
    {
        WelcomeForm.HasBattery = () => false;
        using var w = new Opened();
        var f = w.Form;
        f.GoTo(3);
        var toggles = f.Stage.Situations;
        var full = toggles.Chips.Single(c => Equals(c.Choice, Situation.FullScreen));
        foreach (var key in new[] { Keys.Right, Keys.Down, Keys.End })
        {
            toggles.Key(full, key);
            Assert.AreEqual(toggles.IndexOf(full), toggles.Current, $"{key}: Tab still lands on a toggle that shows");
        }
        toggles.Key(full, Keys.Home);
        Assert.AreEqual(0, toggles.Current);
        toggles.Key(toggles.Chips.First(), Keys.Right);
        Assert.AreEqual(1, toggles.Current);
    }

    // After a move to a page the keyboard is on Next, below the page, and the wheel reaches the window
    // from there: the window takes it and scrolls the page, which has more under what shows. The
    // harness sends the wheel to Next in a window on show and sees the page move.
    [TestMethod]
    [TestCategory("Desktop")]
    public void TheWheelIsTakenByTheWindowToScrollThePage()
    {
        using var w = new Opened();
        var f = w.Form;
        _ = f.Handle;
        f.Fit();
        f.GoTo(3);
        f.PerformLayout();
        Assert.IsTrue(f.Pinned, "the stage and the timeline stay put on this page");
        Assert.AreSame(Field<WelcomeBody>(f, "body"), f.PageScroller, "the page scrolls under them");
        var wheel = typeof(Form).GetMethod("OnMouseWheel", Private)!;
        var turned = new HandledMouseEventArgs(MouseButtons.None, 0, 0, 0, -120);
        wheel.Invoke(f, [turned]);
        Assert.IsTrue(turned.Handled, "a notch the page took is not passed on");
        foreach (int page in new[] { 4, 5 })
        {
            f.GoTo(page);
            Assert.AreSame(Field<WelcomeBody>(f, "body"), f.PageScroller, $"page {page}");
        }
    }

    // Enter goes to the window's main button from any of the choices.
    [TestMethod]
    public void EnterIsLeftToTheWindow()
    {
        using var w = new Opened();
        foreach (var name in new[] { "lockAfter", "warning", "shortcut", "music", "battery", "media" })
        {
            var chip = Chips(w.Form, name).Chips.First();
            Assert.IsFalse((bool)typeof(Control).GetMethod("IsInputKey", Private)!.Invoke(chip, [Keys.Enter])!, name);
        }
    }

    // ---- Arabic ----

    // Every page lays out right to left without throwing, with English in the Arabic places too.
    [TestMethod]
    public void EveryPageLaysOutInArabic()
    {
        Lang.Use("ar");
        using var w = new Opened(new Config { MusicVideo = Effect.Extra(30) });
        var f = w.Form;
        _ = f.Handle;
        f.Fit();
        Assert.AreEqual(RightToLeft.Yes, f.RightToLeft);
        Assert.IsTrue(f.RightToLeftLayout);
        for (int page = 1; page <= WelcomeForm.PageCount; page++)
        {
            f.GoTo(page);
            f.PerformLayout();
            Assert.AreEqual(page, f.Page);
        }
    }
}

static class ControlTesting
{
    // Selectable as Tab would find it, on a window that is not on show: enabled all the way up.
    public static bool CanSelectByCode(this Control control)
    {
        for (var c = control; c is not null; c = c.Parent)
        {
            if (!c.Enabled) return false;
        }
        return true;
    }
}
