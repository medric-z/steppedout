using System.Reflection;

namespace SteppedOut.Tests;

// The settings window built without showing it: no window handle is made, nothing takes the
// keyboard, and the app is a stand-in that only writes down what it was asked. One at a time: the
// window switches the language and the colors, which every test shares.
[TestClass]
[DoNotParallelize]
public class SettingsFormTests
{
    // The app, as far as the window can tell: what it was asked, in order.
    sealed class StandIn : ISettingsHost
    {
        public readonly List<string> Calls = new();
        public readonly List<Config> Edited = new();
        public readonly List<TimeSpan?> Pauses = new();
        Action? statusChanged;

        public bool StartsWithWindows { get; set; } = true;
        public bool SaveFailed { get; set; }
        public AutostartState StartWithWindowsState => StartsWithWindows ? AutostartState.On : AutostartState.Off;
        public event Action? StartWithWindowsChanged
        {
            add { }
            remove { }
        }
        public LockStatus Status { get; set; } = LockStatus.Starting(15);
        public int Listening { get; private set; }

        // Takes the language and the colors as the tray does.
        public bool Applies { get; init; } = true;

        public void PauseChosen(TimeSpan? length)
        {
            Calls.Add("pause");
            Pauses.Add(length);
        }

        public void ResumeChosen() => Calls.Add("resume");

        public void LockNowChosen() => Calls.Add("lock");

        public void TryItChosen() => Calls.Add("try");

        public void ShortcutChanged(string shortcut) => Calls.Add("shortcut " + shortcut);

        public void LanguageChanged(string language)
        {
            Calls.Add("language " + language);
            if (Applies) Lang.Use(language);
        }

        public HotKeyResult ShortcutResult => HotKeyResult.Ok;

        public event Action? ShortcutResultChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<WhyLine> Explain() => [];

        public void TabChosen(int tab) => Calls.Add("tab " + tab);

        public void ConfigEdited(Config config)
        {
            Calls.Add("edited");
            Edited.Add(config);
            if (Applies) Theme.Choose(config);
        }

        public void StartWithWindowsChosen(bool on)
        {
            Calls.Add("start with windows " + (on ? "on" : "off"));
            StartsWithWindows = on;
        }

        public event Action? StatusChanged
        {
            add
            {
                statusChanged += value;
                Listening++;
            }
            remove
            {
                statusChanged -= value;
                Listening--;
            }
        }

        public void Tell(LockStatus status)
        {
            Status = status;
            statusChanged?.Invoke();
        }
    }

    Strings? wasLanguage;

    [TestInitialize]
    public void Start()
    {
        wasLanguage = Lang.Current;
        Lang.Use("en");
        Theme.UseForShots(false);
    }

    [TestCleanup]
    public void End()
    {
        Lang.Use(wasLanguage == Arabic.Text ? "ar" : "en");
        Theme.Choose(ThemeChoice.System, TextSize.System);
    }

    static V1Text V => Lang.Current.V1;

    // A choice from a list's menu, as a click on it makes it.
    static void Pick(DropDown list, string text)
    {
        using var menu = list.BuildMenu();
        var row = menu.Items.OfType<ToolStripMenuItem>().First(item => item.Text == text);
        row.PerformClick();
    }

    // A number typed into a list's Custom… box, and Enter.
    static void Custom(DropDown list, int number)
    {
        CustomBox? box = null;
        list.Presenter = b => box = b;
        list.OpenCustom(0);
        box!.Field.Controls.OfType<TextBox>().Single().Text = number.ToString();
        Assert.IsTrue(box.Key(Keys.Enter));
    }

    // A click as the control's own handler sees it. PerformClick does nothing on a control that is not shown.
    static void Click(Control control) => typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(control, [EventArgs.Empty]);

    static void Choose(PartGroup group, int part) => typeof(PartGroup).GetMethod("Choose", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(group, [group.Parts[part]]);

    static LockStatus On(double idleSeconds, params ActiveEffect[] effects)
    {
        var time = LockTime.Compute(30, effects);
        return new LockStatus
        {
            Kind = time.NeverLocks ? LockState.Never : LockState.On,
            Time = time,
            IdleSeconds = idleSeconds,
            SecondsLeft = time.TotalSeconds is decimal total ? (double)total - idleSeconds : 0,
            Today = new TodayCounts(3, TimeSpan.FromMinutes(80)),
        };
    }

    [TestMethod]
    public void ItBuildsInEnglishAndArabicWithoutAWindow()
    {
        foreach (string language in new[] { "en", "ar" })
        {
            Lang.Use(language);
            var host = new StandIn();
            using var form = new SettingsForm(new Config(), host);
            Assert.IsFalse(form.IsHandleCreated, language);
            Assert.AreEqual(V.SettingsTitle, form.Text);
            Assert.AreEqual(language == "ar" ? RightToLeft.Yes : RightToLeft.No, form.RightToLeft);
            Assert.AreEqual(language == "ar", form.RightToLeftLayout);
            Assert.HasCount(7, form.Main.Rows);
            Assert.HasCount(5, form.Tabs.Parts);
            Assert.AreEqual(0, form.Tab);
            Assert.AreEqual(language, form.Pills.Value, "Same as Windows shows as the language in use");
            Assert.AreEqual(V.Language, form.Pills.AccessibleName);
            Assert.IsNotNull(form.PageControl);
            Assert.AreEqual(form.LogicalToDeviceUnits(740), form.ColumnWidth);
            Assert.AreEqual(0, form.EditsWhileFilling);
            Assert.IsEmpty(host.Edited);
        }
    }

    [TestMethod]
    public void TheSevenRowsAreInTheSketchOrder()
    {
        using var form = new SettingsForm(new Config(), new StandIn());
        var rows = form.Main.Rows;
        CollectionAssert.AreEqual(
            new[] { V.LockAfter, V.MusicVideo, V.OnCall, V.FullScreen, V.WarnMe, V.MediaOnLock, V.StartWithWindows },
            rows.Select(row => row.Title).ToArray());
        Assert.IsTrue(rows.All(row => row.Look == RowLook.LookC));
        var m = form.Main;
        foreach (var list in new[] { m.LockAfter, m.MusicVideo, m.OnCall, m.FullScreen, m.Warning, m.MediaOnLock })
        {
            Assert.IsTrue(list!.LookC);
            Assert.AreEqual(190, list.ButtonWidth);
        }
        Assert.IsTrue(rows[4].Contains(m.TryIt) && rows[4].Contains(m.Warning), "Try it sits beside the warning's list");
        Assert.IsTrue(rows[6].Contains(m.StartWithWindows));
        Assert.AreEqual(ToggleLook.LookC, m.StartWithWindows!.Look);
        Assert.IsTrue(m.StartWithWindows.ShowLabel, "On or Off beside the switch");
        CollectionAssert.AreEqual(new[] { V.LockingTab, V.AwayTab, V.AppearanceTab, V.BreaksTab, V.AdvancedTab }, form.Tabs.Parts.Select(p => p.Text).ToArray());
    }

    // The window opens on the tab used last, Locking the first time. Going to another tab tells the
    // host which, once, and is no edit: nothing is handed over and nothing waits to be. A later edit
    // carries the tab, so it never takes it back.
    [TestMethod]
    public void TheWindowOpensOnTheTabUsedLastAndTellsTheHostOfANewOne()
    {
        var host = new StandIn();
        using (var first = new SettingsForm(new Config(), host))
        {
            Assert.AreEqual(0, first.Tab, "Locking the first time");
            Assert.AreEqual(0, first.Tabs.Selected);
        }
        for (int tab = 0; tab <= Config.MaxSettingsTab; tab++)
        {
            using var again = new SettingsForm(new Config { SettingsTab = tab }, host);
            Assert.AreEqual(tab, again.Tab);
            Assert.AreEqual(tab, again.Tabs.Selected);
            Assert.IsNotNull(again.PageControl);
        }
        Assert.IsEmpty(host.Calls, "opening asks nothing");

        using var form = new SettingsForm(new Config { SettingsTab = 1 }, host);
        Choose(form.Tabs, 3);
        Assert.AreEqual(3, form.Tab);
        CollectionAssert.AreEqual(new[] { "tab 3" }, host.Calls);
        Assert.IsFalse(form.Pending, "no edit waits");
        Assert.AreEqual(3, form.Working.SettingsTab);
        Choose(form.Tabs, 3);
        CollectionAssert.AreEqual(new[] { "tab 3" }, host.Calls, "the same tab again says nothing");

        // An edit hands over the settings with the tab in them.
        Pick(form.Main.LockAfter!, Lang.Current.OptionDuration(30));
        form.FlushPending();
        Assert.AreEqual(3, host.Edited.Single().SettingsTab);

        // Settings from elsewhere move no open window to another tab.
        form.Refill(new Config { SettingsTab = 0, IdleMinutes = 30 });
        Assert.AreEqual(3, form.Tab);

        // Named again while the settings say another tab: kept as the last one.
        host.Calls.Clear();
        form.OpenTab(3);
        Assert.AreEqual(3, form.Tab);
        CollectionAssert.AreEqual(new[] { "tab 3" }, host.Calls);
        Assert.AreEqual(3, form.Working.SettingsTab);
        form.OpenTab(3);
        CollectionAssert.AreEqual(new[] { "tab 3" }, host.Calls, "named again when already kept: nothing");

        // A place in the app that names a tab goes there, and it is kept as the last one.
        host.Calls.Clear();
        form.OpenTab(2);
        Assert.AreEqual(2, form.Tab);
        Assert.AreEqual(2, form.Tabs.Selected);
        CollectionAssert.AreEqual(new[] { "tab 2" }, host.Calls);
        form.OpenTab(99);
        Assert.AreEqual(Config.MaxSettingsTab, form.Tab, "out of range: the nearest");
    }

    // The tab strip in Arabic opens on the tab kept, as in English.
    [TestMethod]
    public void TheLastTabOpensInArabicToo()
    {
        Lang.Use("ar");
        using var form = new SettingsForm(new Config { Language = "ar", SettingsTab = 4 }, new StandIn());
        Assert.AreEqual(4, form.Tab);
        Assert.AreEqual(4, form.Tabs.Selected);
        Assert.IsNotNull(form.PageControl);
    }

    // Screen readers read the window's pieces in the order of its children, which is the order of
    // the child windows: the language, the status box, the list, the tabs, the page, then the
    // footer, on every tab (I12).
    [TestMethod]
    public void ScreenReadersReadThePageBeforeTheFooter()
    {
        using var form = new SettingsForm(new Config(), new StandIn());
        var content = (Control)form.Scroller;
        for (int tab = 0; tab < 5; tab++)
        {
            Choose(form.Tabs, tab);
            var children = content.Controls.Cast<Control>().ToList();
            int page = children.IndexOf(form.PageControl!);
            Assert.AreEqual(children.IndexOf(form.Tabs) + 1, page, $"tab {tab}: the page comes right after the tabs");
            Assert.AreEqual(children.Count - 1, page + 1, $"tab {tab}: and the footer after it, last");
        }
    }

    // At a large text size no word on the tabs is cut where the tab ends: a name whose longest word
    // does not fit beside its icon puts every icon above its name, and a word too long even for the
    // whole tab ends in an ellipsis. Up to Windows' 150% every name fits whole (I15).
    [TestMethod]
    public void TabWordsAreNeverCutMidWord()
    {
        try
        {
            foreach (string language in new[] { "en", "ar" })
            {
                foreach (float scale in new[] { 1f, 1.2f, 1.5f, 2.25f })
                {
                    Lang.Use(language);
                    Theme.UseForShots(false, scale);
                    using var form = new SettingsForm(new Config(), new StandIn());
                    var strip = form.Tabs;
                    strip.Size = new Size(form.ColumnWidth, strip.GetPreferredSize(new Size(form.ColumnWidth, 0)).Height);
                    strip.PerformLayout();
                    var drawn = new List<(string Text, Font Font, Rectangle Box, TextFormatFlags Flags)>();
                    KitControl.TextDrawn = (_, text, font, box, flags) => drawn.Add((text, font, box, flags));
                    foreach (var tab in strip.Parts)
                    {
                        using var picture = new Bitmap(tab.Width, tab.Height);
                        tab.DrawToBitmap(picture, new Rectangle(Point.Empty, tab.Size));
                    }
                    KitControl.TextDrawn = null;
                    var names = strip.Parts.Select(p => p.Text).ToHashSet();
                    Assert.HasCount(10, drawn, $"{language} at {scale}: a name and a line on each tab");
                    foreach (var (text, font, box, flags) in drawn)
                    {
                        string what = $"{language} at {scale}: \"{text}\" in {box.Size}";
                        var tall = TextRenderer.MeasureText(text, font, new Size(box.Width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                        Assert.IsLessThanOrEqualTo(box.Height + 1, tall.Height, $"{what}: every line shows");
                        // Where each word's ink ends: TextRenderer starts it a little in from the box's edge.
                        bool whole = text.Split(' ').All(word =>
                        {
                            int ink = TextRenderer.MeasureText(word, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                            int padded = TextRenderer.MeasureText(word, font, Size.Empty, TextFormatFlags.SingleLine).Width;
                            return ink + (padded - ink) / 2 <= box.Width;
                        });
                        if (names.Contains(text) && scale <= 1.5f) Assert.IsTrue(whole, $"{what}: every word of the name fits");
                        else Assert.IsTrue(whole || flags.HasFlag(TextFormatFlags.WordEllipsis), $"{what}: a word that does not fit ends in an ellipsis");
                    }
                }
            }
        }
        finally
        {
            KitControl.TextDrawn = null;
            Theme.UseForShots(false);
        }
    }

    // The footer's two lines never run into each other: side by side while they fit, as up to 150%
    // text, and the version under the saved words when they do not, as "Changes couldn't be
    // saved. …" at Windows' 200% and 225%, in both languages. A save that fails or works again moves
    // what is under the footer with it.
    [TestMethod]
    public void TheFootersLinesNeverRunIntoEachOther()
    {
        try
        {
            foreach (string language in new[] { "en", "ar" })
            {
                foreach (float scale in new[] { 1f, 1.5f, 2f, 2.25f })
                {
                    Lang.Use(language);
                    Theme.UseForShots(false, scale);
                    var host = new StandIn();
                    using var form = new SettingsForm(new Config(), host);
                    form.Scroller.PerformLayout();
                    var footer = form.FooterLine!;
                    int oneLine = footer.Height;
                    foreach (bool failed in new[] { true, false })
                    {
                        host.SaveFailed = failed;
                        form.SaveStateChanged();
                        string name = $"{language} at {scale:P0}, \"{footer.Saved}\"";
                        Assert.AreEqual(form.ColumnWidth, footer.Width, name);
                        Assert.AreEqual(footer.GetPreferredSize(new Size(footer.Width, 0)).Height, footer.Height, $"{name}: laid out at the height it asks");
                        var drawn = new List<(string Text, Font Font, Rectangle Box, TextFormatFlags Flags)>();
                        KitControl.TextDrawn = (c, text, font, box, flags) =>
                        {
                            if (c == footer) drawn.Add((text, font, box, flags));
                        };
                        using (var picture = new Bitmap(footer.Width, footer.Height))
                            footer.DrawToBitmap(picture, new Rectangle(Point.Empty, footer.Size));
                        KitControl.TextDrawn = null;
                        Assert.HasCount(2, drawn, name);
                        var whole = new Rectangle(Point.Empty, footer.Size);
                        foreach (var (text, font, box, flags) in drawn)
                        {
                            var needs = TextRenderer.MeasureText(text, font, new Size(box.Width, int.MaxValue), flags | TextFormatFlags.NoPrefix);
                            Assert.IsLessThanOrEqualTo(box.Width, needs.Width, $"{name}: \"{text}\" is as wide as its box at most");
                            Assert.IsLessThanOrEqualTo(box.Height, needs.Height, $"{name}: every line of \"{text}\" shows");
                            Assert.IsTrue(whole.Contains(box), $"{name}: \"{text}\" in {box} lies inside the footer");
                        }
                        Assert.IsFalse(drawn[0].Box.IntersectsWith(drawn[1].Box), $"{name}: {drawn[0].Box} and {drawn[1].Box} do not meet");
                        if (scale <= 1.5f) Assert.AreEqual(oneLine, footer.Height, $"{name}: side by side");
                    }
                }
            }
        }
        finally
        {
            KitControl.TextDrawn = null;
            Theme.UseForShots(false);
        }
    }

    // A footer that grows while the window is open, as when a save fails at 225% text, is inside the
    // scroll range at once, so scrolled to the end all of it shows.
    [TestMethod]
    public void AFooterThatGrowsCanBeScrolledTo()
    {
        try
        {
            Theme.UseForShots(false, 2.25f);
            var host = new StandIn();
            using var form = new SettingsForm(new Config(), host);
            var scroller = form.Scroller;
            _ = form.Handle;
            _ = scroller.Handle;
            scroller.PerformLayout();
            scroller.PerformLayout();
            var footer = form.FooterLine!;
            int oneLine = footer.Height;
            host.SaveFailed = true;
            form.SaveStateChanged();
            Assert.IsGreaterThan(oneLine, footer.Height, "the not-saved line takes more room");
            Assert.IsGreaterThanOrEqualTo(form.ContentHeight, scroller.DisplayRectangle.Height, $"the scroll range reaches the footer's end at {form.ContentHeight}");
        }
        finally
        {
            Theme.UseForShots(false);
        }
    }

    [TestMethod]
    public void TheListsHoldTheirOwnTimes()
    {
        var text = Lang.Current;
        using var form = new SettingsForm(new Config(), new StandIn());
        var m = form.Main;
        CollectionAssert.AreEqual(new[] { "5 minutes", "10 minutes", "15 minutes", "30 minutes", "1 hour", V.Custom }, m.LockAfter!.Rows.ToArray());
        CollectionAssert.AreEqual(new[] { V.NoExtraTime, text.ExtraLabel(15), text.ExtraLabel(30), text.ExtraLabel(60), text.ExtraLabel(120), V.NeverLock, V.Custom }, m.MusicVideo!.Rows.ToArray());
        CollectionAssert.AreEqual(m.MusicVideo.Rows.ToArray(), m.OnCall!.Rows.ToArray());
        CollectionAssert.AreEqual(m.MusicVideo.Rows.ToArray(), m.FullScreen!.Rows.ToArray());
        CollectionAssert.AreEqual(new[] { V.Off, text.SecondsOption(10), text.SecondsOption(15), text.SecondsOption(30), text.SecondsOption(60), V.Custom }, m.Warning!.Rows.ToArray());
        CollectionAssert.AreEqual(new[] { V.PauseUntilBack, V.PauseAndStayPaused, V.KeepPlaying }, m.MediaOnLock!.Rows.ToArray());
    }

    [TestMethod]
    public void EachListMakesItsOwnEdit()
    {
        var text = Lang.Current;
        var start = new Config();
        var steps = new (string Name, Action<MainList> Do, Func<Config, object> Read, object Want)[]
        {
            ("lock after", m => Pick(m.LockAfter!, "10 minutes"), c => c.IdleMinutes, 10.0),
            ("lock after custom", m => Custom(m.LockAfter!, 45), c => c.IdleMinutes, 45.0),
            ("music", m => Pick(m.MusicVideo!, text.ExtraLabel(30)), c => c.MusicVideo, Effect.Extra(30)),
            ("music custom", m => Custom(m.MusicVideo!, 45), c => c.MusicVideo, Effect.Extra(45)),
            ("call", m => Pick(m.OnCall!, V.NoExtraTime), c => c.OnCall, Effect.None),
            ("call extra", m => Pick(m.OnCall!, text.ExtraLabel(120)), c => c.OnCall, Effect.Extra(120)),
            ("full screen", m => Pick(m.FullScreen!, text.ExtraLabel(60)), c => c.FullScreen, Effect.Extra(60)),
            ("warning", m => Pick(m.Warning!, text.SecondsOption(30)), c => c.GraceSeconds, 30),
            ("warning off", m => Pick(m.Warning!, V.Off), c => c.GraceSeconds, 0),
            ("warning custom", m => Custom(m.Warning!, 20), c => c.GraceSeconds, 20),
            ("media", m => Pick(m.MediaOnLock!, V.KeepPlaying), c => c.MediaOnLock, MediaOnLock.KeepPlaying),
            ("media stay", m => Pick(m.MediaOnLock!, V.PauseAndStayPaused), c => c.MediaOnLock, MediaOnLock.PauseStayPaused),
        };
        foreach (var (name, act, read, want) in steps)
        {
            var host = new StandIn();
            using var form = new SettingsForm(start, host);
            act(form.Main);
            Assert.IsTrue(form.Pending, name);
            Assert.IsEmpty(host.Edited, $"{name}: nothing goes out before the short wait");
            form.FlushPending();
            Assert.HasCount(1, host.Edited, name);
            var saved = host.Edited[0];
            Assert.AreEqual(want, read(saved), name);
            // Only that one setting changed.
            var back = saved.Copy();
            back.IdleMinutes = start.IdleMinutes;
            back.MusicVideo = start.MusicVideo;
            back.OnCall = start.OnCall;
            back.FullScreen = start.FullScreen;
            back.GraceSeconds = start.GraceSeconds;
            back.MediaOnLock = start.MediaOnLock;
            Assert.IsTrue(back.SameAs(start), $"{name}: nothing else changed");
            Assert.IsFalse(form.Pending);
            Assert.AreEqual(0, form.EditsWhileFilling, name);
        }
    }

    [TestMethod]
    public void ValuesNotInTheListsShowAsThemselves()
    {
        foreach (string language in new[] { "en", "ar" })
        {
            Lang.Use(language);
            var text = Lang.Current;
            var config = new Config
            {
                IdleMinutes = 2,
                MusicVideo = Effect.Extra(45),
                OnCall = Effect.Extra(90),
                FullScreen = Effect.Extra(240),
                GraceSeconds = 20,
            };
            using var form = new SettingsForm(config, new StandIn());
            var m = form.Main;
            Assert.AreEqual(text.OptionDuration(2), m.LockAfter!.Text);
            Assert.AreEqual(text.ExtraLabel(45), m.MusicVideo!.Text);
            Assert.AreEqual(text.ExtraLabel(90), m.OnCall!.Text);
            Assert.AreEqual(text.ExtraLabel(240), m.FullScreen!.Text);
            Assert.AreEqual(text.SecondsOption(20), m.Warning!.Text);
            foreach (var list in new[] { m.LockAfter, m.MusicVideo, m.OnCall, m.FullScreen, m.Warning })
            {
                Assert.AreNotEqual(V.Custom, list.Text, language);
                Assert.AreEqual(V.Custom, list.Rows[^1], "Custom… stays last");
                Assert.AreEqual(list.Text, list.Rows[^2], "the time of its own sits just above Custom…");
            }
            // A time from the file that is not whole minutes: under a minute in seconds, otherwise the nearest minute.
            form.Refill(new Config { IdleMinutes = 0.5 });
            Assert.AreEqual(text.SecondsOption(30), m.LockAfter.Text);
            form.Refill(new Config { IdleMinutes = 7.5 });
            Assert.AreEqual(text.OptionDuration(8), m.LockAfter.Text);
            Assert.AreEqual(0, form.EditsWhileFilling);
        }
    }

    [TestMethod]
    public void TryItAsksTheAppAfterHandingOverTheWarning()
    {
        var host = new StandIn();
        using var form = new SettingsForm(new Config(), host);
        var m = form.Main;
        Assert.IsTrue(m.TryIt!.Enabled);
        Pick(m.Warning!, Lang.Current.SecondsOption(30));
        Click(m.TryIt);
        CollectionAssert.AreEqual(new[] { "edited", "try" }, host.Calls.ToArray(), "the note shows the warning just picked");
        Assert.AreEqual(30, host.Edited[0].GraceSeconds);

        Pick(m.Warning!, V.Off);
        Assert.IsFalse(m.TryIt.Enabled, "greyed out while the warning is off");
        Pick(m.Warning!, Lang.Current.SecondsOption(10));
        Assert.IsTrue(m.TryIt.Enabled);
    }

    [TestMethod]
    public void StartWithWindowsGoesToTheAppNotTheFile()
    {
        var host = new StandIn { StartsWithWindows = true };
        using var form = new SettingsForm(new Config(), host);
        var toggle = form.Main.StartWithWindows!;
        Assert.IsTrue(toggle.Checked);
        toggle.Checked = false;
        CollectionAssert.AreEqual(new[] { "start with windows off" }, host.Calls.ToArray());
        Assert.IsFalse(form.Pending, "nothing in the settings changed");
        Assert.IsFalse(toggle.Checked);

        // The app could not change it: the switch shows what Windows has.
        var stuck = new StandIn { StartsWithWindows = false };
        using var other = new SettingsForm(new Config(), new StuckHost(stuck));
        other.Main.StartWithWindows!.Checked = true;
        Assert.IsFalse(other.Main.StartWithWindows.Checked);

        // Windows changed it later, or a choice finished later: the app says so and the switch follows
        // without asking the app for anything.
        host.StartsWithWindows = true;
        form.StartWithWindowsChanged();
        Assert.IsTrue(toggle.Checked);
        Assert.HasCount(1, host.Calls);
        Assert.IsFalse(form.Pending);
    }

    // An app where Start with Windows cannot be turned on, as under a policy.
    sealed class StuckHost(StandIn inner) : ISettingsHost
    {
        public void PauseChosen(TimeSpan? length) => inner.PauseChosen(length);
        public void ResumeChosen() => inner.ResumeChosen();
        public void LockNowChosen() => inner.LockNowChosen();
        public void TryItChosen() => inner.TryItChosen();
        public void ShortcutChanged(string shortcut) => inner.ShortcutChanged(shortcut);
        public void LanguageChanged(string language) => inner.LanguageChanged(language);
        public HotKeyResult ShortcutResult => HotKeyResult.Ok;

        public event Action? ShortcutResultChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<WhyLine> Explain() => [];
        public void ConfigEdited(Config config) => inner.ConfigEdited(config);
        public bool StartsWithWindows => false;
        public AutostartState StartWithWindowsState => StartsWithWindows ? AutostartState.On : AutostartState.Off;
        public event Action? StartWithWindowsChanged
        {
            add { }
            remove { }
        }
        public void StartWithWindowsChosen(bool on) => inner.Calls.Add("refused");
        public LockStatus Status => inner.Status;

        public event Action? StatusChanged
        {
            add => inner.StatusChanged += value;
            remove => inner.StatusChanged -= value;
        }
    }

    [TestMethod]
    public void RefillingRaisesNoEdits()
    {
        var configs = new List<Config>
        {
            new(),
            new() { IdleMinutes = 2, MusicVideo = Effect.Extra(45), OnCall = Effect.None, FullScreen = Effect.Extra(240), GraceSeconds = 20 },
            new() { IdleMinutes = 0.5, GraceSeconds = 0, MediaOnLock = MediaOnLock.KeepPlaying, LockWhenAway = false },
            new() { Language = "auto" },
            new() { Language = "ar" },
            new() { Language = "en", MusicVideo = Effect.Never, OnCall = Effect.Never, FullScreen = Effect.Never },
            // Brought into range by Clamp: a snooze between the choices and a rule that does nothing.
            new() { SnoozeMinutes = 5, AppRules = { new AppRule("notepad", Effect.None) }, GraceSeconds = 900, IdleMinutes = 5000 },
            new() { IdleMinutes = 7.5, MusicVideo = Effect.Extra(1), GraceSeconds = 600 },
        };
        foreach (string language in new[] { "en", "ar" })
        {
            Lang.Use(language);
            var host = new StandIn { Applies = false };
            using var form = new SettingsForm(new Config(), host);
            int raw = 0;
            form.Main.Edited += _ => raw++;
            for (int round = 0; round < 2; round++)
            {
                foreach (var config in configs)
                {
                    form.Refill(config);
                    form.Main.Refill(config);
                }
            }
            Assert.AreEqual(0, raw, $"{language}: the main list raised an edit while filled");
            Assert.AreEqual(0, form.EditsWhileFilling, language);
            Assert.IsFalse(form.Pending, language);
            form.FlushPending();
            Assert.IsEmpty(host.Edited, language);
        }
    }

    [TestMethod]
    public void OnlyARealChangeIsHandedOverAndABurstIsOneSave()
    {
        var host = new StandIn();
        using var form = new SettingsForm(new Config(), host);
        var m = form.Main;
        Pick(m.LockAfter!, "10 minutes");
        Pick(m.LockAfter!, "15 minutes");
        Assert.IsFalse(form.Pending, "back where it started");
        form.FlushPending();
        Assert.IsEmpty(host.Edited);

        Pick(m.LockAfter!, "5 minutes");
        Pick(m.Warning!, Lang.Current.SecondsOption(60));
        Pick(m.MediaOnLock!, V.KeepPlaying);
        form.FlushPending();
        Assert.HasCount(1, host.Edited);
        Assert.AreEqual(5.0, host.Edited[0].IdleMinutes);
        Assert.AreEqual(60, host.Edited[0].GraceSeconds);
        Assert.AreEqual(MediaOnLock.KeepPlaying, host.Edited[0].MediaOnLock);

        // The settings the app now has, sent back to the window, change nothing.
        form.Refill(host.Edited[0]);
        form.FlushPending();
        Assert.HasCount(1, host.Edited);
    }

    [TestMethod]
    public void AChangeWaitingStaysOnTopOfOneFromOutside()
    {
        var host = new StandIn();
        using var form = new SettingsForm(new Config(), host);
        Pick(form.Main.LockAfter!, "10 minutes");
        // The tray or the file changes something else meanwhile, and the lock time back to the old one.
        form.Refill(new Config { MuteWhenAway = true, GraceSeconds = 30 });
        Assert.AreEqual("10 minutes", form.Main.LockAfter!.Text, "the choice just made stays");
        Assert.AreEqual(Lang.Current.SecondsOption(30), form.Main.Warning!.Text, "the other change shows");
        Assert.IsTrue(form.Pending);
        form.FlushPending();
        Assert.HasCount(1, host.Edited);
        Assert.AreEqual(10.0, host.Edited[0].IdleMinutes);
        Assert.IsTrue(host.Edited[0].MuteWhenAway);
        Assert.AreEqual(30, host.Edited[0].GraceSeconds);
    }

    [TestMethod]
    public void WhatTheWindowHandsOverSavesAndReadsBackTheSame()
    {
        var host = new StandIn();
        using var form = new SettingsForm(new Config { Language = "auto", AppRules = { new AppRule("notepad", Effect.Never) } }, host);
        Pick(form.Main.MusicVideo!, Lang.Current.ExtraLabel(30));
        Custom(form.Main.Warning!, 20);
        form.FlushPending();
        var saved = host.Edited.Single();
        Assert.AreEqual("auto", saved.Language, "an edit to something else keeps Same as Windows");
        string folder = Path.Combine(Path.GetTempPath(), "settings-form-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(folder, "config.json");
            string text = saved.Write(path);
            var read = Config.Parse(text, path, out string? problem, out _);
            Assert.IsNull(problem);
            Assert.AreEqual(text, read.Write(Path.Combine(folder, "again.json")));
            Assert.IsTrue(read.SameAs(saved));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void TheAppGetsCopiesOfItsOwn()
    {
        var config = new Config();
        var host = new StandIn();
        using var form = new SettingsForm(config, host);
        config.IdleMinutes = 60;
        Assert.AreEqual("15 minutes", form.Main.LockAfter!.Text, "the window keeps its own copy");
        Pick(form.Main.LockAfter!, "30 minutes");
        form.FlushPending();
        host.Edited[0].IdleMinutes = 5;
        host.Edited[0].CloseWhenAway.Add("notepad");
        Assert.AreEqual(30.0, form.Working.IdleMinutes);
        Assert.IsEmpty(form.Working.CloseWhenAway);
    }

    [TestMethod]
    public void PickingALanguageGoesOutAtOnceAndBuildsTheWindowAgain()
    {
        var host = new StandIn();
        using var form = new SettingsForm(new Config { Language = "en" }, host);
        Choose(form.Tabs, 3);
        Assert.AreEqual(3, form.Tab);
        var before = form.StatusBox;
        Choose(form.Pills, 1);
        CollectionAssert.AreEqual(new[] { "tab 3", "edited", "language ar" }, host.Calls.ToArray(), "no short wait for a language");
        Assert.AreEqual("ar", host.Edited[0].Language);
        Assert.AreEqual(3, host.Edited[0].SettingsTab, "the tab goes with it");
        Assert.AreEqual(Arabic.Text.V1.SettingsTitle, form.Text);
        Assert.AreEqual(RightToLeft.Yes, form.RightToLeft);
        Assert.IsTrue(form.RightToLeftLayout);
        Assert.AreNotSame(before, form.StatusBox, "built again");
        Assert.IsTrue(before.IsDisposed);
        Assert.AreEqual(3, form.Tab, "on the same tab");
        Assert.AreEqual(3, form.Tabs.Selected);
        Assert.AreEqual("ar", form.Pills.Value);
        Assert.AreEqual(Arabic.Text.V1.BreaksTab, form.Tabs.Parts[3].Text);
        Assert.HasCount(7, form.Main.Rows);
        Assert.AreEqual(Arabic.Text.V1.LockAfter, form.Main.Rows[0].Title);
        Assert.AreEqual(0, form.EditsWhileFilling);
        Assert.HasCount(1, host.Edited, "building again hands nothing over");
        Assert.HasCount(3, host.Calls, "nor tells of a tab");
    }

    [TestMethod]
    public void NewColorsOrTextSizeBuildTheWindowAgainOnTheSameTab()
    {
        var host = new StandIn();
        using var form = new SettingsForm(new Config(), host);
        Choose(form.Tabs, 2);
        var first = form.Main.LockAfter;
        form.RebuildIfStale();
        Assert.AreSame(first, form.Main.LockAfter, "nothing changed, nothing built");

        Theme.UseForShots(true);
        form.RebuildIfStale();
        Assert.AreNotSame(first, form.Main.LockAfter);
        Assert.AreEqual(2, form.Tab);
        Assert.AreEqual(Theme.Current.Window, form.BackColor);

        var second = form.Main.LockAfter;
        Theme.UseForShots(true, 1.2f);
        form.RebuildIfStale();
        Assert.AreNotSame(second, form.Main.LockAfter);
        Assert.AreEqual(2, form.Tab);
        Assert.AreEqual(0, form.EditsWhileFilling);
        Assert.IsEmpty(host.Edited);
    }

    // A page whose height changes by itself, as when a row shows or a chip is added.
    sealed class GrowingPage : ISettingsPage
    {
        public ISettingsHost? Host;
        public int Tall = 100;
        public Grower? Control;

        public sealed class Grower(GrowingPage page) : Panel
        {
            public override Size GetPreferredSize(Size proposedSize) => new(proposedSize.Width, page.Tall);
        }

        public Control Build(Config config, Strings text) => Control = new Grower(this);

        public void Refill(Config config)
        {
        }

        public event Action<Func<Config, Config>>? Edited
        {
            add { }
            remove { }
        }
    }

    [TestMethod]
    public void APageThatGrowsMovesWhatIsUnderIt()
    {
        var host = new StandIn();
        var page = new GrowingPage();
        using var form = new SettingsForm(new Config(), host, [h => { page.Host = h; return page; }]);
        Assert.AreSame(host, page.Host, "the page gets the app");
        form.Scroller.PerformLayout();
        Assert.AreEqual(100, page.Control!.Height);
        int range = form.Scroller.AutoScrollMinSize.Height;

        page.Tall = 400;
        page.Control.PerformLayout();
        Assert.AreEqual(400, page.Control.Height, "the page gets its new height");
        Assert.AreEqual(range + 300, form.Scroller.AutoScrollMinSize.Height, "the footer and the scroll range move with it");

        // The other tabs have no page yet and show what they will hold.
        Choose(form.Tabs, 4);
        Assert.IsNotNull(form.PageControl);
        Assert.AreNotSame(page.Control, form.PageControl);
    }

    [TestMethod]
    public void TheStatusBoxShowsWhatTheAppSays()
    {
        var text = Lang.Current;
        var host = new StandIn { Status = On(120) };
        using var form = new SettingsForm(new Config { IdleMinutes = 30 }, host);
        var box = form.StatusBox;
        Assert.AreEqual(text.Headline(host.Status), box.Headline);
        Assert.AreEqual(text.Sentence(host.Status), box.Sentence);
        Assert.IsFalse(box.WhyShown, "nothing changes the lock time");
        Assert.AreEqual(text.AwayToday(TimeSpan.FromMinutes(80)), box.AwayText);
        Assert.AreEqual(text.Times(3), box.LockedText);
        Assert.AreEqual(Ring.FractionOf(host.Status), box.Ring.Fraction);
        Assert.AreEqual(BoxTone.Green, box.Tone);

        host.Tell(On(120, new ActiveEffect(Effect.Extra(60), EffectSource.MusicOrVideo), new ActiveEffect(Effect.Extra(120), EffectSource.FullScreen)));
        Assert.IsTrue(box.WhyShown);
        Assert.AreEqual(text.Why(host.Status), box.Why);
        Assert.AreEqual(text.Headline(host.Status), box.Headline);

        host.Tell(On(120, new ActiveEffect(Effect.Never, EffectSource.Call)));
        Assert.AreEqual(text.Headline(host.Status), box.Headline);
        Assert.AreEqual(BoxTone.Green, box.Tone, "green, as the sketch draws a call");

        host.Tell(new LockStatus { Kind = LockState.Countdown, Time = LockTime.Plain(30), SecondsLeft = 9 });
        Assert.AreEqual(BoxTone.Amber, box.Tone);
        host.Tell(new LockStatus { Kind = LockState.SessionLocked, Time = LockTime.Plain(30) });
        Assert.AreEqual(BoxTone.Grey, box.Tone);
        host.Tell(new LockStatus { Kind = LockState.On, Time = LockTime.Plain(30), SecondsLeft = 600, DryRun = true });
        Assert.AreEqual(text.Headline(host.Status), box.Headline);
        StringAssert.Contains(box.Headline, string.Format(V.DryRun, "").Trim());
    }

    [TestMethod]
    public void PauseBecomesTurnBackOnWhilePaused()
    {
        var host = new StandIn { Status = On(60) };
        using var form = new SettingsForm(new Config { IdleMinutes = 30 }, host);
        var box = form.StatusBox;
        Assert.AreEqual(V.Pause, box.PauseButton.Text);
        Assert.IsTrue(box.PauseButton.Chevron);
        var items = box.PauseChoices();
        CollectionAssert.AreEqual(
            new[] { Lang.Current.PauseChoice(15), Lang.Current.PauseChoice(30), Lang.Current.PauseChoice(60), Lang.Current.PauseChoice(120), Lang.Current.PauseChoice(24 * 60), "", Lang.Current.PauseChoice(null), V.Custom },
            items.Select(i => i is ToolStripSeparator ? "" : i.Text).ToArray(), "the tray menu's own list");
        items[1].PerformClick();
        items[4].PerformClick();
        items[6].PerformClick();
        CollectionAssert.AreEqual(new TimeSpan?[] { TimeSpan.FromMinutes(30), TimeSpan.FromDays(1), null }, host.Pauses.ToArray());

        host.Tell(new LockStatus { Kind = LockState.Paused, Time = LockTime.Plain(30) });
        Assert.AreEqual(V.TurnBackOn, box.PauseButton.Text);
        Assert.IsFalse(box.PauseButton.Chevron);
        Assert.AreEqual(BoxTone.Grey, box.Tone);
        Click(box.PauseButton);
        Assert.AreEqual("resume", host.Calls[^1]);

        host.Tell(On(60));
        Assert.AreEqual(V.Pause, box.PauseButton.Text);
        Click(box.LockNowButton);
        Assert.AreEqual("lock", host.Calls[^1]);
        Assert.IsTrue(box.LockNowButton.Primary);
    }

    // Pause turns into Turn back on in a button of the same size, so the words beside it keep their
    // width, the box its height, and nothing under the box moves.
    [TestMethod]
    public void PausingMovesNothingInTheStatusBox()
    {
        foreach (string language in new[] { "en", "ar" })
        {
            Lang.Use(language);
            var host = new StandIn { Status = On(120) };
            using var form = new SettingsForm(new Config { IdleMinutes = 30 }, host);
            form.Scroller.PerformLayout();
            var box = form.StatusBox;
            var on = Places(form);
            Assert.AreEqual(form.LogicalToDeviceUnits(32), box.PauseButton.Height, $"{language}: the sketch's 32 px buttons");
            Assert.AreEqual(box.PauseButton.Height, box.LockNowButton.Height, language);

            host.Tell(new LockStatus { Kind = LockState.Paused, Time = LockTime.Plain(30), Today = new TodayCounts(3, TimeSpan.FromMinutes(80)) });
            Assert.AreEqual(V.TurnBackOn, box.PauseButton.Text, language);
            Assert.AreEqual(on, Places(form), $"{language}: paused until turned back on");
            host.Tell(new LockStatus { Kind = LockState.Paused, Time = LockTime.Plain(30), PausedUntil = DateTime.Today.AddHours(15).AddMinutes(30), Today = new TodayCounts(3, TimeSpan.FromMinutes(80)) });
            Assert.AreEqual(on, Places(form), $"{language}: paused until a time");
            host.Tell(On(120));
            Assert.AreEqual(V.Pause, box.PauseButton.Text, language);
            Assert.AreEqual(on, Places(form), $"{language}: on again");
        }
    }

    // The box's height, its two buttons, the sentence and the top of the rows under it.
    static (int, Rectangle, Rectangle, Rectangle, int) Places(SettingsForm form)
    {
        var box = form.StatusBox;
        var sentence = box.Controls.OfType<StatusBox.TextLine>().First(t => t.Text == box.Sentence);
        return (box.Height, box.PauseButton.Bounds, box.LockNowButton.Bounds, sentence.Bounds, form.Main.Rows[0].Parent!.Top);
    }

    // The two counts may use the room before the buttons, but never come closer than 12 px to them,
    // in either language and whatever the box says.
    [TestMethod]
    public void TheCountsKeepTheirDistanceFromTheButtons()
    {
        var states = new[]
        {
            On(120),
            On(120, new ActiveEffect(Effect.Never, EffectSource.Call)),
            new LockStatus { Kind = LockState.Paused, Time = LockTime.Plain(30), Today = new TodayCounts(3, TimeSpan.FromMinutes(80)) },
            new LockStatus { Kind = LockState.Countdown, Time = LockTime.Plain(30), IdleSeconds = 1791, SecondsLeft = 9, Today = new TodayCounts(3, TimeSpan.FromMinutes(80)) },
            new LockStatus { Kind = LockState.SessionLocked, Time = LockTime.Plain(30), Today = new TodayCounts(12, TimeSpan.FromMinutes(615)) },
        };
        foreach (string language in new[] { "en", "ar" })
        {
            Lang.Use(language);
            var host = new StandIn { Status = states[0] };
            using var form = new SettingsForm(new Config { IdleMinutes = 30 }, host);
            form.Scroller.PerformLayout();
            var box = form.StatusBox;
            Assert.AreEqual(2, box.Controls.OfType<StatusBox.Stat>().Count(), "the time away and the locks today");
            foreach (var status in states)
            {
                host.Tell(status);
                foreach (var count in box.Controls.OfType<StatusBox.Stat>())
                {
                    foreach (var button in new[] { box.PauseButton, box.LockNowButton })
                    {
                        bool sameLine = count.Top < button.Bottom && button.Top < count.Bottom;
                        int apart = Math.Max(count.Left - button.Right, button.Left - count.Right);
                        Assert.IsTrue(!sameLine || apart >= form.LogicalToDeviceUnits(12),
                            $"{language} {status.Kind}: \"{count.AccessibleName}\" at {count.Bounds} is {apart} px from {button.Text} at {button.Bounds}");
                    }
                }
            }
        }
    }

    // A long day's counts, 5 hr 20 min away and 12 locks, stay side by side in the sketch's 122 px
    // box in both languages, beside the buttons and clear of them (I6). The buttons rise to the
    // headline's line only when the counts need the room, which neither language's do now.
    [TestMethod]
    public void ALongDaysCountsStaySideBySide()
    {
        foreach (string language in new[] { "en", "ar" })
        {
            Lang.Use(language);
            var host = new StandIn { Status = On(120) with { Today = new TodayCounts(12, TimeSpan.FromMinutes(320)) } };
            using var form = new SettingsForm(new Config { IdleMinutes = 30 }, host);
            form.Scroller.PerformLayout();
            var box = form.StatusBox;
            var counts = box.Controls.OfType<StatusBox.Stat>().ToList();
            Assert.HasCount(2, counts);
            Assert.AreEqual(counts[0].Top, counts[1].Top, $"{language}: side by side");
            foreach (var count in counts)
            {
                Assert.IsTrue(count.Left >= form.LogicalToDeviceUnits(20) && count.Right <= box.Width - form.LogicalToDeviceUnits(20), $"{language}: {count.Bounds} inside the box's padding");
                foreach (var button in new[] { box.PauseButton, box.LockNowButton })
                    Assert.IsFalse(count.Bounds.IntersectsWith(Rectangle.Inflate(button.Bounds, form.LogicalToDeviceUnits(6), form.LogicalToDeviceUnits(6))),
                        $"{language}: \"{count.AccessibleName}\" at {count.Bounds} clear of {button.Text} at {button.Bounds}");
            }
            Assert.IsFalse(box.ButtonsUp, $"{language}: the buttons rise only when the counts need the room");
            int longDay = box.Height;
            host.Tell(On(120));
            Assert.AreEqual(box.Height, longDay, $"{language}: as tall as the box with the sketch's day");
            if (language == "en") Assert.AreEqual(form.LogicalToDeviceUnits(122), longDay, "the sketch's 122 px box");
        }
    }

    // At every text size up to Windows' 225%, in both languages, the status box shows its headline
    // and its counts whole, and nothing in it meets anything else. Where the counts no longer fit
    // beside the buttons (200% and 225%), the buttons go under them.
    [TestMethod]
    public void TheStatusBoxShowsItsWordsAndCountsWholeAtEveryTextSize()
    {
        try
        {
            foreach (float scale in new[] { 1f, 1.5f, 2f, 2.25f })
            {
                Theme.UseForShots(false, scale);
                foreach (string language in new[] { "en", "ar" })
                {
                    Lang.Use(language);
                    var host = new StandIn { Status = On(120) with { Today = new TodayCounts(3, TimeSpan.FromMinutes(80)) } };
                    using var form = new SettingsForm(new Config { IdleMinutes = 30 }, host);
                    form.Scroller.PerformLayout();
                    var box = form.StatusBox;
                    string name = $"{language} at {scale:P0}";
                    var headline = box.Controls.OfType<StatusBox.TextLine>().First(t => t.Text == box.Headline);
                    Assert.IsFalse(headline.Cut, $"{name}: \"{headline.Text}\" whole");
                    var counts = box.Controls.OfType<StatusBox.Stat>().ToList();
                    foreach (var count in counts)
                    {
                        Assert.IsGreaterThanOrEqualTo(count.GetPreferredSize(Size.Empty).Width, count.Width, $"{name}: \"{count.AccessibleName}\" whole");
                        Assert.IsTrue(count.Left >= box.LogicalToDeviceUnits(20) && count.Right <= box.Width - box.LogicalToDeviceUnits(20), $"{name}: {count.Bounds} inside the box's padding");
                    }
                    var parts = new Control[] { headline, box.PauseButton, box.LockNowButton }.Concat(counts).ToList();
                    for (int i = 0; i < parts.Count; i++)
                        for (int j = i + 1; j < parts.Count; j++)
                            Assert.IsFalse(parts[i].Bounds.IntersectsWith(parts[j].Bounds), $"{name}: {parts[i].AccessibleName} at {parts[i].Bounds} and {parts[j].AccessibleName} at {parts[j].Bounds}");
                    foreach (var button in new[] { box.PauseButton, box.LockNowButton })
                        Assert.IsLessThanOrEqualTo(box.Height - box.LogicalToDeviceUnits(18), button.Bottom, $"{name}: {button.Text} {button.Bounds} inside the box {box.Size}");
                    bool under = counts.All(c => box.PauseButton.Top >= c.Bottom);
                    Assert.AreEqual(scale >= 2f, under, $"{name}: the buttons {(under ? "under" : "beside")} the counts");
                    Assert.AreEqual(under, box.ButtonsDown, name);
                }
            }
        }
        finally
        {
            Theme.UseForShots(false, 1f);
            Lang.Use("en");
        }
    }

    // The longest paused headlines, a weekday or a date a week on, show whole in the status box at
    // 100% text in both languages; at larger text the one-line headline is cut with its whole words
    // in a tip, as any long headline is.
    [TestMethod]
    public void TheLongestPausedLinesShowWholeInTheStatusBox()
    {
        var was = Strings.LocalNow;
        try
        {
            var now = new DateTime(2026, 9, 23, 14, 0, 0);
            Strings.LocalNow = () => now;
            Theme.UseForShots(false, 1f);
            foreach (string language in new[] { "en", "ar" })
            {
                Lang.Use(language);
                foreach (int days in new[] { 0, 1, 6, 7 })
                {
                    var until = now.Date.AddDays(days).AddHours(12).AddMinutes(45);
                    var status = new LockStatus { Kind = LockState.Paused, Time = LockTime.Plain(30), PausedUntil = until, Today = new TodayCounts(3, TimeSpan.FromMinutes(80)) };
                    using var form = new SettingsForm(new Config { IdleMinutes = 30 }, new StandIn { Status = status });
                    form.Scroller.PerformLayout();
                    var box = form.StatusBox;
                    var headline = box.Controls.OfType<StatusBox.TextLine>().First(t => t.Text == box.Headline);
                    Assert.AreEqual(Lang.Current.PausedLine(until, now), headline.Text);
                    Assert.IsFalse(headline.Cut, $"{language}: \"{headline.Text}\" whole");
                }
            }
        }
        finally
        {
            Strings.LocalNow = was;
            Lang.Use("en");
        }
    }

    // A tab's words show whole up to Windows' 225% text, as many lines as Windows wraps them to when
    // it draws them; a word too long for the tab ends in an ellipsis, and then the tab's tip has all
    // of its words.
    [TestMethod]
    public void ATabsWordsAreNeverCutOffAndACutTabHasItsWholeWordsInATip()
    {
        try
        {
            foreach (float scale in new[] { 1f, 1.5f, 2f, 2.25f })
            {
                Theme.UseForShots(false, scale);
                foreach (string language in new[] { "en", "ar" })
                {
                    Lang.Use(language);
                    using var form = new SettingsForm(new Config(), new StandIn());
                    form.Scroller.PerformLayout();
                    var tabs = form.Tabs;
                    for (int i = 0; i < tabs.Parts.Count; i++)
                    {
                        var tab = tabs.Parts[i];
                        var drawn = new List<(string Text, Font Font, Rectangle Box, TextFormatFlags Flags)>();
                        KitControl.TextDrawn = (c, text, font, box, flags) =>
                        {
                            if (c == tab) drawn.Add((text, font, box, flags));
                        };
                        using (var picture = new Bitmap(tab.Width, tab.Height))
                            tab.DrawToBitmap(picture, new Rectangle(Point.Empty, tab.Size));
                        KitControl.TextDrawn = null;
                        string name = $"{language} at {scale:P0}, {tab.Text}";
                        var words = drawn.Where(d => d.Flags.HasFlag(TextFormatFlags.WordBreak)).ToList();
                        Assert.IsNotEmpty(words, name);
                        foreach (var (text, font, box, flags) in words)
                        {
                            var rtl = language == "ar" ? TextFormatFlags.RightToLeft : 0;
                            var needs = TextRenderer.MeasureText(text, font, new Size(box.Width, int.MaxValue), flags | TextFormatFlags.NoPrefix | rtl);
                            Assert.IsLessThanOrEqualTo(box.Height, needs.Height, $"{name}: every line of \"{text}\" shows ({needs.Height} in {box.Height})");
                            Assert.IsTrue(new Rectangle(Point.Empty, tab.Size).Contains(box), $"{name}: \"{text}\" in {box} inside the tab {tab.Size}");
                        }
                        bool cut = words.Any(d => d.Flags.HasFlag(TextFormatFlags.WordEllipsis));
                        Assert.AreEqual(cut, tabs.TipOf(i) is not null, $"{name}: a tip exactly when a word ends in an ellipsis");
                        if (cut)
                        {
                            StringAssert.Contains(tabs.TipOf(i), tab.Text, name);
                            StringAssert.Contains(tabs.TipOf(i), tab.AccessibleDescription, name);
                        }
                    }
                }
            }
        }
        finally
        {
            KitControl.TextDrawn = null;
            Theme.UseForShots(false, 1f);
            Lang.Use("en");
        }
    }

    // The marks Arabic sets under its letters, such as the two under the last letter of "seconds"
    // in the countdown, hang below the line's height as Windows measures it: an Arabic line of the
    // status box is taller by as much, at every text size, so they are never cut off (I5).
    [TestMethod]
    public void ArabicMarksUnderTheLineAreNeverCut()
    {
        try
        {
            foreach (float scale in new[] { 1f, 1.5f, 2.25f })
            {
                Theme.UseForShots(false, scale);
                foreach (string language in new[] { "en", "ar" })
                {
                    Lang.Use(language);
                    var host = new StandIn { Status = new LockStatus { Kind = LockState.Countdown, Time = LockTime.Plain(30), IdleSeconds = 1791, SecondsLeft = 9, Today = new TodayCounts(3, TimeSpan.FromMinutes(80)) } };
                    using var form = new SettingsForm(new Config { IdleMinutes = 30 }, host);
                    form.Scroller.PerformLayout();
                    var headline = form.StatusBox.Controls.OfType<StatusBox.TextLine>().First(t => t.Text == form.StatusBox.Headline);
                    string name = $"{language} at {scale:P0}";
                    Assert.AreEqual(headline.HeightFor(headline.Width), headline.Height, $"{name}: the line is as tall as it asks");
                    int extra = headline.HeightFor(headline.Width) - Plain(headline);
                    if (language == "ar") Assert.IsGreaterThan(0, extra, $"{name}: room under the line");
                    else Assert.AreEqual(0, extra, $"{name}: no room under an English line");
                    int ink = TrayMenuTests.InkBottom(headline.Text, headline.Face, headline.RightToLeft == RightToLeft.Yes);
                    Assert.IsLessThanOrEqualTo(headline.Height, ink, $"{name}, {headline.Text}: the words' lowest mark is inside the line");
                }
            }
        }
        finally
        {
            Theme.UseForShots(false, 1f);
        }
    }

    // A long program name leaves the headline on one line, cut with "…" as the tray menu cuts it, the
    // whole of it in a tip and for screen readers, and the box as tall as with a short name (I27).
    [TestMethod]
    public void ALongProgramNameIsCutOnTheHeadlinesOneLine()
    {
        try
        {
            foreach (string language in new[] { "en", "ar" })
            {
                Lang.Use(language);
                var heights = new List<int>();
                foreach (string app in new[] { "notepad", "Quarterly Planning Workbook Editor for Large Teams" })
                {
                    var effect = new ActiveEffect(Effect.Never, EffectSource.ForApp(app));
                    var host = new StandIn { Status = new LockStatus { Kind = LockState.Never, Time = LockTime.Compute(30, [effect]), Today = new TodayCounts(3, TimeSpan.FromMinutes(80)) } };
                    using var form = new SettingsForm(new Config(), host);
                    form.Scroller.PerformLayout();
                    var headline = form.StatusBox.Controls.OfType<StatusBox.TextLine>().First(t => t.Text == form.StatusBox.Headline);
                    bool cut = app != "notepad";
                    string name = $"{language}, {app}";
                    Assert.AreEqual(headline.HeightFor(headline.Width), headline.Height, $"{name}: one line");
                    Assert.AreEqual(cut, headline.Cut, name);
                    Assert.AreEqual(cut ? form.StatusBox.Headline : null, headline.Tip, $"{name}: the whole headline in the tip");
                    Assert.AreEqual(form.StatusBox.Headline, headline.AccessibilityObject.Name, $"{name}: screen readers hear all of it");
                    heights.Add(form.StatusBox.Height);
                }
                Assert.AreEqual(heights[0], heights[1], $"{language}: the box keeps its height");
            }
        }
        finally
        {
            Lang.Use("en");
        }
    }

    // The words of the rows, the status headline and the footer start at the edge of their boxes, as
    // the headings, notes and chips drawn beside them do, not 3 or 4 px in (I8).
    [TestMethod]
    public void WordsStartAtTheEdgeOfTheirBoxes()
    {
        try
        {
            Theme.UseForShots(false);
            foreach (string language in new[] { "en", "ar" })
            {
                Lang.Use(language);
                bool rtl = language == "ar";
                using var form = new SettingsForm(new Config(), new StandIn());
                form.Scroller.PerformLayout();
                var row = Every(form).OfType<SettingRow>().First(r => r.Look == RowLook.LookC);
                var title = row.Controls.OfType<RowLabel>().First();
                var headline = form.StatusBox.Controls.OfType<StatusBox.TextLine>().First(t => t.Text == form.StatusBox.Headline);
                var footer = Every(form).Single(c => c.GetType().Name == "Footer");
                foreach (var (what, control) in new (string, Control)[] { ("a row's title", title), ("the status headline", headline), ("the footer", footer) })
                {
                    var (first, last) = Ink(control);
                    int gap = rtl ? control.Width - 1 - last : first;
                    Assert.IsLessThanOrEqualTo(1, gap, $"{language}: {what} starts {gap} px from its {(rtl ? "right" : "left")} edge");
                }
            }
        }
        finally
        {
            Lang.Use("en");
        }
    }

    static IEnumerable<Control> Every(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Every(c)));

    // The first and last columns of a control's drawn words, on white.
    static (int First, int Last) Ink(Control control)
    {
        using var picture = new Bitmap(control.Width, control.Height);
        using (var g = Graphics.FromImage(picture)) g.Clear(Color.White);
        control.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
        int first = -1, last = -1;
        for (int x = 0; x < picture.Width; x++)
        {
            for (int y = 0; y < picture.Height; y++)
            {
                if (picture.GetPixel(x, y).GetBrightness() >= 0.6f) continue;
                if (first < 0) first = x;
                last = x;
                break;
            }
        }
        return (first, last);
    }

    // A line's height as Windows measures its words, without the room under it.
    static int Plain(StatusBox.TextLine line)
    {
        var rtl = line.RightToLeft;
        line.RightToLeft = RightToLeft.No;
        int height = line.HeightFor(line.Width);
        line.RightToLeft = rtl;
        return height;
    }

    [TestMethod]
    public void TheWindowStopsListeningWhenItCloses()
    {
        var host = new StandIn();
        var form = new SettingsForm(new Config(), host);
        Assert.AreEqual(1, host.Listening);
        Theme.UseForShots(true);
        form.RebuildIfStale();
        Assert.AreEqual(1, host.Listening, "the box built again replaces the old one's ear");
        form.Dispose();
        Assert.AreEqual(0, host.Listening);
    }
}
