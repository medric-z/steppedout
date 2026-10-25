using System.Reflection;
using System.Text.Json;

namespace SteppedOut.Tests;

// The Appearance, Breaks and Extras tabs, the Locking tab's lock time, shortcut and Why rows, and the
// Shortcut and Why windows, built without showing a window: what each choice asks the settings to
// become (one key each), that building and refilling a page asks for nothing, the lock shortcut's
// list and line, what the shortcut window takes and
// refuses, and what the Why window says. A stand-in host answers for the app and never registers a
// shortcut. One at a time: the controls share the font cache, and some tests set the language or
// the theme.
[TestClass]
[DoNotParallelize]
public class PagesTests
{
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;

    Strings? languageBefore;

    [TestInitialize]
    public void Start()
    {
        languageBefore = Lang.Current;
        Lang.Use("en");
    }

    [TestCleanup]
    public void End()
    {
        Lang.Use(languageBefore?.RightToLeft == true ? "ar" : "en");
    }

    // The app as the windows see it: registering a shortcut is only written down, and how it went
    // is whatever the test says.
    sealed class Host : ISettingsHost
    {
        public HotKeyResult ShortcutResult { get; set; } = HotKeyResult.Ok;
        public event Action? ShortcutResultChanged;
        public Func<string, HotKeyResult> Register { get; set; } = _ => HotKeyResult.Ok;
        public List<string> Applied { get; } = new();
        public IReadOnlyList<WhyLine> Lines { get; set; } = [];
        public int Asked { get; private set; }
        public LockStatus Status { get; set; } = new() { Kind = LockState.On, Time = LockTime.Plain(15), SecondsLeft = 600 };
#pragma warning disable CS0067
        public event Action? StatusChanged;
#pragma warning restore CS0067
        public bool StartsWithWindows => false;
        public AutostartState StartWithWindowsState => StartsWithWindows ? AutostartState.On : AutostartState.Off;
        public event Action? StartWithWindowsChanged
        {
            add { }
            remove { }
        }

        public int Listeners => ShortcutResultChanged?.GetInvocationList().Length ?? 0;

        // "Choose another shortcut…" said it opened (true) or closed (false), in order.
        public List<bool> Choosing { get; } = new();

        public void ChoosingShortcut(bool open) => Choosing.Add(open);

        public void ShortcutChanged(string shortcut)
        {
            Applied.Add(shortcut);
            ShortcutResult = shortcut.Length == 0 ? HotKeyResult.Ok : Register(shortcut);
            ShortcutResultChanged?.Invoke();
        }

        public void Tell(HotKeyResult result)
        {
            ShortcutResult = result;
            ShortcutResultChanged?.Invoke();
        }

        public IReadOnlyList<WhyLine> Explain()
        {
            Asked++;
            return Lines;
        }

        public void PauseChosen(TimeSpan? length) => Assert.Fail("no pause");
        public void ResumeChosen() => Assert.Fail("no turn back on");
        public void LockNowChosen() => Assert.Fail("no lock");
        public void TryItChosen() => Assert.Fail("no preview");
        public void LanguageChanged(string language) => Assert.Fail("no language change");
        public void ConfigEdited(Config config) => Assert.Fail("pages hand their edits to the window");
        public void StartWithWindowsChosen(bool on) => Assert.Fail("no start with Windows");

        // Show the welcome again and Remove SteppedOut's files, as the Extras tab asked for them.
        public int WelcomesAsked { get; private set; }

        public int RemovesAsked { get; private set; }

        public void ShowWelcomeChosen() => WelcomesAsked++;

        public void RemoveFilesChosen() => RemovesAsked++;
    }

    sealed class Built
    {
        public required ISettingsPage Tab { get; init; }
        public required Control Page { get; init; }
        public List<Func<Config, Config>> Edits { get; } = new();

        // The settings as the window keeps them: each edit applied to a copy, as the window does.
        public Config Config { get; set; } = new();

        public Config Apply()
        {
            foreach (var edit in Edits) Config = edit(Config.Copy());
            Edits.Clear();
            return Config;
        }
    }

    static Built Build(ISettingsPage tab, Config? config = null)
    {
        config ??= new Config();
        var page = tab.Build(config, Lang.Current);
        var built = new Built { Tab = tab, Page = page, Config = config.Copy() };
        tab.Edited += built.Edits.Add;
        page.Width = 740;
        page.PerformLayout();
        return built;
    }

    static IEnumerable<T> All<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T match) yield return match;
            foreach (var deeper in All<T>(child)) yield return deeper;
        }
    }

    static List<SettingRow> Rows(Control page) => All<SettingRow>(page).ToList();

    static T Of<T>(SettingRow row) where T : Control => row.Controls.OfType<T>().Single(c => c != row.Below);

    static void Click(Control control) => typeof(Control).GetMethod("OnClick", Hidden)!.Invoke(control, [EventArgs.Empty]);

    static void Press(Control control, Keys key) => typeof(Control).GetMethod("OnKeyDown", Hidden)!.Invoke(control, [new KeyEventArgs(key)]);

    // A row of the open list, picked as a person picks it.
    static void Pick(DropDown list, int row)
    {
        using var menu = list.BuildMenu();
        menu.Items.OfType<ToolStripMenuItem>().ElementAt(row).PerformClick();
    }

    static void Choose(Segmented choices, int part) => Click(choices.Parts[part]);

    // The settings the file would hold, key by key.
    static Dictionary<string, string> KeysOf(Config config)
    {
        var options = (JsonSerializerOptions)typeof(Config).GetField("JsonOptions", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(config, options));
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetRawText());
    }

    // The keys that differ, so an edit can be seen to change exactly one.
    static List<string> Changed(Config before, Config after)
    {
        var a = KeysOf(before);
        var b = KeysOf(after);
        return a.Keys.Union(b.Keys).Where(k => !a.TryGetValue(k, out var x) || !b.TryGetValue(k, out var y) || x != y).OrderBy(k => k).ToList();
    }

    // Applies the edits to the settings and checks they changed only this key.
    static Config OnlyKey(Built b, string key)
    {
        var before = b.Config.Copy();
        var after = b.Apply();
        CollectionAssert.AreEqual(new[] { key }, Changed(before, after), "the keys changed");
        return after;
    }

    // ---- Appearance ----

    [TestMethod]
    public void AppearanceRowsFollowTheSketch()
    {
        var v = Lang.Current.V1;
        var b = Build(new AppearanceTab());
        // No button color: the buttons are always the app's green.
        CollectionAssert.AreEqual(new[] { v.ThemeMode, v.TextSize, v.MinutesOnIcon, v.NotificationPlace }, Rows(b.Page).Select(r => r.Title).ToList());
        Assert.IsEmpty(b.Page.Controls.Find("buttonColor", true));
        var head = All<PageHeading>(b.Page).Single();
        Assert.AreEqual(v.AppearanceTab, head.Title);
        Assert.AreEqual(v.AppearanceIntro, head.Detail);
        CollectionAssert.AreEqual(new[] { v.Light, v.Dark, v.System }, Of<Segmented>(Rows(b.Page)[0]).Parts.Select(p => p.Text).ToList());
        CollectionAssert.AreEqual(new[] { v.Small, v.Medium, v.Large, v.System }, Of<Segmented>(Rows(b.Page)[1]).Parts.Select(p => p.Text).ToList());
        CollectionAssert.AreEqual(new[] { v.BottomRight, v.BottomLeft, v.TopRight, v.TopLeft, v.Center }, Of<DropDown>(Rows(b.Page)[3]).Choices.ToList());
        Assert.IsEmpty(b.Edits);
    }

    [TestMethod]
    public void AppearanceChoicesEachEditOneKey()
    {
        var b = Build(new AppearanceTab());
        var rows = Rows(b.Page);
        Choose(Of<Segmented>(rows[0]), 1);
        Assert.AreEqual(ThemeChoice.Dark, OnlyKey(b, "theme").Theme);
        Choose(Of<Segmented>(rows[1]), 2);
        Assert.AreEqual(TextSize.Large, OnlyKey(b, "textSize").TextSize);
        Click(Of<Toggle>(rows[2]));
        Assert.IsTrue(OnlyKey(b, "minutesOnTrayIcon").MinutesOnTrayIcon);
        Pick(Of<DropDown>(rows[3]), 3);
        Assert.AreEqual(NotePosition.TopLeft, OnlyKey(b, "notificationPlace").NotificationPlace);
        Pick(Of<DropDown>(rows[3]), 4);
        Assert.AreEqual(NotePosition.Center, OnlyKey(b, "notificationPlace").NotificationPlace);
        // The arrow keys on the text size choose as they move.
        Press(Of<Segmented>(rows[1]).Parts[2], System.Windows.Forms.Keys.Right);
        Assert.AreEqual(TextSize.System, OnlyKey(b, "textSize").TextSize);
    }

    // D37: the place is not saved until someone picks it, and picking the one already shown saves it.
    [TestMethod]
    public void TheNotificationPlaceIsSavedOnlyOncePicked()
    {
        var config = new Config();
        Assert.IsNull(config.NotificationPlace);
        var b = Build(new AppearanceTab(), config);
        var place = Of<DropDown>(Rows(b.Page)[3]);
        Assert.AreEqual(config.NotificationPlaceInUse, place.Value, "the list shows the place in use");
        b.Tab.Refill(config);
        Assert.IsEmpty(b.Edits, "showing it saves nothing");
        int shown = NotePlace.All.ToList().IndexOf(config.NotificationPlaceInUse);
        Pick(place, shown);
        Assert.AreEqual(config.NotificationPlaceInUse, OnlyKey(b, "notificationPlace").NotificationPlace, "picking the one shown saves it");
        Pick(place, shown);
        Assert.IsEmpty(b.Edits, "picking it again changes nothing");
    }

    // The app uses a look as soon as the edit reaches it (Theme.Choose), and the window builds the
    // page again: the same controls by name, now showing the new choices.
    [TestMethod]
    public void APickedLookAppliesAtOnceAndThePageIsBuiltAgainInIt()
    {
        if (SystemInformation.HighContrast) Assert.Inconclusive("high contrast keeps its own colors");
        var tab = new AppearanceTab();
        var b = Build(tab);
        try
        {
            var rows = Rows(b.Page);
            Choose(Of<Segmented>(rows[0]), 0);
            Choose(Of<Segmented>(rows[1]), 2);
            Pick(Of<DropDown>(rows[3]), 3);
            var config = b.Apply();
            Theme.Choose(ThemeChoice.Dark, TextSize.Small);
            Assert.IsTrue(Theme.Choose(config), "the look changed");
            Assert.IsFalse(Theme.Current.IsDark);
            Assert.AreEqual(1.2f, Theme.TextScale);
            Assert.AreEqual(Theme.Light.Accent, Theme.Current.Accent, "always the green");
            var names = All<Control>(b.Page).Where(c => c.Name.Length > 0).Select(c => c.Name).ToList();
            b.Page.Dispose();
            var again = tab.Build(config, Lang.Current);
            CollectionAssert.AreEqual(names, All<Control>(again).Where(c => c.Name.Length > 0).Select(c => c.Name).ToList(), "the same controls by name");
            Assert.AreEqual(ThemeChoice.Light, ((Segmented)again.Controls.Find("themeMode", true).Single()).Value);
            Assert.AreEqual(TextSize.Large, ((Segmented)again.Controls.Find("textSize", true).Single()).Value);
            Assert.AreEqual(NotePosition.TopLeft, ((DropDown)again.Controls.Find("notificationPlace", true).Single()).Value);
            Assert.IsEmpty(b.Edits, "building again asks for nothing");
            again.Dispose();
        }
        finally
        {
            Theme.Choose(ThemeChoice.System, TextSize.System);
        }
    }

    // ---- Breaks ----

    [TestMethod]
    public void BreaksRowsFollowTheSketch()
    {
        var v = Lang.Current.V1;
        var text = Lang.Current;
        var b = Build(new BreaksTab());
        CollectionAssert.AreEqual(new[] { v.BreakReminders, v.ReminderAfter, v.ReminderSound, v.EyeBreaks, v.Snooze }, Rows(b.Page).Select(r => r.Title).ToList());
        var rows = Rows(b.Page);
        CollectionAssert.AreEqual(new[] { 30, 45, 60, 90, 120, 240 }.Select(text.OptionDuration).ToList(), Of<DropDown>(rows[1]).Choices.ToList());
        Assert.AreEqual(v.Custom, Of<DropDown>(rows[1]).Rows[^1], "the reminder time ends with Custom…");
        CollectionAssert.AreEqual(new[] { v.Off, v.SoundSteppedOut, v.SoundDawn, v.SoundMeadow, v.SoundLantern, v.SoundTide }, Of<DropDown>(rows[2]).Choices.ToList());
        Assert.AreEqual(v.YourOwnSound, Of<DropDown>(rows[2]).Rows[^1], "the reminder sound ends with Your own sound…");
        // No snooze is Off, never "0 minutes later".
        CollectionAssert.AreEqual(new[] { v.Off, text.SnoozeLabel(5), v.Later10, text.SnoozeLabel(15), v.Later30, text.SnoozeLabel(60) }, Of<DropDown>(rows[4]).Choices.ToList());
        Assert.AreEqual(v.Custom, Of<DropDown>(rows[4]).Rows[^1], "the snooze time ends with Custom…");
        Assert.AreEqual(v.Hour1, Of<DropDown>(rows[1]).Text);
        Assert.AreEqual(v.Later10, Of<DropDown>(rows[4]).Text);
        var pill = All<NotePreview>(b.Page).Single();
        Assert.AreEqual(v.TimeForABreak, pill.Lead);
        Assert.AreEqual(text.AtThePcFor(TimeSpan.FromHours(1)), pill.Tail);
    }

    [TestMethod]
    public void BreakChoicesEachEditOneKey()
    {
        var b = Build(new BreaksTab(), new Config { BreakMinutes = 90 });
        var rows = Rows(b.Page);
        // Turning reminders on keeps the time already chosen.
        Click(Of<Toggle>(rows[0]));
        var on = OnlyKey(b, "breakReminders");
        Assert.IsTrue(on.BreakReminders);
        Assert.AreEqual(90, on.BreakMinutes);
        Pick(Of<DropDown>(rows[1]), 0);
        Assert.AreEqual(30, OnlyKey(b, "breakMinutes").BreakMinutes);
        Pick(Of<DropDown>(rows[2]), 1);
        Assert.AreEqual(BreakSound.SteppedOut, OnlyKey(b, "breakSound").BreakSound);
        Click(Of<Toggle>(rows[3]));
        Assert.IsTrue(OnlyKey(b, "eyeBreaks").EyeBreaks);
        Pick(Of<DropDown>(rows[4]), 0);
        Assert.AreEqual(0, OnlyKey(b, "snoozeMinutes").SnoozeMinutes);
        Pick(Of<DropDown>(rows[4]), 4);
        Assert.AreEqual(30, OnlyKey(b, "snoozeMinutes").SnoozeMinutes);
        Pick(Of<DropDown>(rows[4]), 5);
        Assert.AreEqual(60, OnlyKey(b, "snoozeMinutes").SnoozeMinutes);
    }

    // Typed into a list's Custom… box and taken with Enter, as a person does.
    static void Type(DropDown list, string typed)
    {
        CustomBox? box = null;
        list.Presenter = b => box = b;
        list.OpenCustom(0);
        Assert.IsNotNull(box);
        box.Field.Controls.OfType<TextBox>().Single().Text = typed;
        Assert.IsTrue(box.Key(Keys.Enter));
    }

    // Both lists end with Custom…: the reminder from 5 minutes to 12 hours, the snooze from 1 minute
    // to 4 hours. A number out of range takes the nearest end. A time typed, or saved earlier and not
    // on the list, shows as itself.
    [TestMethod]
    public void BreakAndSnoozeTimesTakeCustomWithinTheirRange()
    {
        var text = Lang.Current;
        var b = Build(new BreaksTab());
        var rows = Rows(b.Page);
        var after = Of<DropDown>(rows[1]);
        var snooze = Of<DropDown>(rows[4]);

        Type(after, "75");
        Assert.AreEqual(75, OnlyKey(b, "breakMinutes").BreakMinutes);
        Assert.AreEqual(text.OptionDuration(75), after.Text);
        Type(after, "1000");
        Assert.AreEqual(720, OnlyKey(b, "breakMinutes").BreakMinutes);
        Type(after, "2");
        Assert.AreEqual(5, OnlyKey(b, "breakMinutes").BreakMinutes);
        Type(after, "240");
        Assert.AreEqual(240, OnlyKey(b, "breakMinutes").BreakMinutes);
        Assert.AreEqual(1, after.Choices.Count(c => c == text.OptionDuration(240)), "a ready-made time chooses itself and adds nothing");
        Assert.AreEqual(text.OptionDuration(240), after.Text);

        Type(snooze, "45");
        Assert.AreEqual(45, OnlyKey(b, "snoozeMinutes").SnoozeMinutes);
        Assert.AreEqual(text.SnoozeLabel(45), snooze.Text);
        Type(snooze, "999");
        Assert.AreEqual(240, OnlyKey(b, "snoozeMinutes").SnoozeMinutes);
        Type(snooze, "0");
        Assert.AreEqual(1, OnlyKey(b, "snoozeMinutes").SnoozeMinutes);

        var saved = Build(new BreaksTab(), new Config { BreakMinutes = 50, SnoozeMinutes = 7 });
        var savedRows = Rows(saved.Page);
        Assert.AreEqual(text.OptionDuration(50), Of<DropDown>(savedRows[1]).Text, "50 minutes from before shows as itself");
        Assert.AreEqual(text.SnoozeLabel(7), Of<DropDown>(savedRows[4]).Text);
        Assert.IsEmpty(saved.Edits, "showing a saved time asks for no change");
    }

    [TestMethod]
    public void ThePreviewSaysTheReminderTime()
    {
        var text = Lang.Current;
        var b = Build(new BreaksTab());
        Pick(Of<DropDown>(Rows(b.Page)[1]), 3);
        Assert.AreEqual(text.AtThePcFor(TimeSpan.FromMinutes(90)), All<NotePreview>(b.Page).Single().Tail);
        b.Tab.Refill(new Config { BreakMinutes = 120 });
        Assert.AreEqual(text.AtThePcFor(TimeSpan.FromMinutes(120)), All<NotePreview>(b.Page).Single().Tail);
    }

    // ---- Extras ----

    // The rows, top to bottom: Mouse and audio, then Settings and log.
    const int JitterRow = 0, LoudnessRow = 1, SilenceRow = 2, FolderRow = 3, LogSizeRow = 4, SaveLoadRow = 5, WelcomeRow = 6,
        ResetRow = 7, RemoveRow = 8;

    static AdvancedTab Extras(Host host) => new(host, _ => Assert.Fail("no folder"));

    // The Locking tab's rows, found by their titles: the battery's row above them shows only on a PC
    // with a battery.
    static SettingRow RowTitled(Control page, string title) => Rows(page).Single(r => r.Title == title);

    static SettingRow AtLockTimeRow(Control page) => RowTitled(page, Lang.Current.V1.AtLockTime);

    static SettingRow ShortcutRow(Control page) => RowTitled(page, Lang.Current.V1.Shortcut);

    static SettingRow WhyRow(Control page) => RowTitled(page, Lang.Current.V1.WhyNotLocking);

    [TestMethod]
    public void ExtrasRowsComeInTwoParts()
    {
        var v = Lang.Current.V1;
        var b = Build(Extras(new Host()), new Config { LockShortcut = "Ctrl+Alt+L" });
        CollectionAssert.AreEqual(new[]
        {
            v.IgnoreTinyMoves, v.SoundsThatCount, v.StoppedAfter,
            v.FilesAndLog, v.LogSize, v.SaveAndLoad, v.ShowWelcomeAgain, v.ResetAll, v.RemoveFiles,
        }, Rows(b.Page).Select(r => r.Title).ToList());
        var heads = All<PageHeading>(b.Page).ToList();
        CollectionAssert.AreEqual(new[] { v.AdvancedTab, v.MouseAndAudio, v.SettingsAndLog }, heads.Select(h => h.Title).ToList());
        Assert.IsFalse(heads[0].Part);
        Assert.IsTrue(heads.Skip(1).All(h => h.Part && ((FlatPanel)b.Page).HasLineAbove(h)), "each part starts under a line");
        Assert.AreEqual(v.MouseAndAudioIntro, heads[1].Detail);
        Assert.AreEqual("Extras", v.AdvancedTab);
        Assert.IsEmpty(All<ShortcutNote>(b.Page), "the shortcut is on the Locking tab");

        var rows = Rows(b.Page);
        CollectionAssert.AreEqual(new[] { "Off", "Under 3 pixels", "Under 5 pixels", "Under 10 pixels" }, Of<DropDown>(rows[JitterRow]).Choices.ToList());
        CollectionAssert.AreEqual(new[] { v.EvenQuietSounds, v.MediumAndLoudSounds, v.OnlyLoudSounds }, Of<DropDown>(rows[LoudnessRow]).Choices.ToList());
        CollectionAssert.AreEqual(new[] { v.Seconds10, v.Seconds30, v.Minute1 }, Of<DropDown>(rows[SilenceRow]).Choices.ToList());
        CollectionAssert.AreEqual(new[] { "256 KB", "512 KB", "1 MB", "4 MB" }, Of<DropDown>(rows[LogSizeRow]).Choices.ToList());
        foreach (int row in new[] { JitterRow, LoudnessRow, SilenceRow, LogSizeRow })
        {
            Assert.AreEqual(v.Custom, Of<DropDown>(rows[row]).Rows[^1], rows[row].Title + " ends with Custom…");
        }

        // The defaults.
        Assert.AreEqual("Under 5 pixels", Of<DropDown>(rows[JitterRow]).Text);
        Assert.AreEqual(v.EvenQuietSounds, Of<DropDown>(rows[LoudnessRow]).Text);
        Assert.AreEqual(v.Seconds30, Of<DropDown>(rows[SilenceRow]).Text);
        Assert.AreEqual("512 KB", Of<DropDown>(rows[LogSizeRow]).Text);

        Assert.AreEqual(v.OpenFolder, Of<KitButton>(rows[FolderRow]).Text);
        var pair = Of<ButtonPair>(rows[SaveLoadRow]);
        Assert.AreEqual("Save to file…", pair.First.Text);
        Assert.AreEqual("Load from file…", pair.Second.Text);
        Assert.AreEqual(v.Show, Of<KitButton>(rows[WelcomeRow]).Text);
        Assert.AreEqual("Reset…", Of<KitButton>(rows[ResetRow]).Text);
        Assert.AreEqual("Remove…", Of<KitButton>(rows[RemoveRow]).Text);
        Assert.IsEmpty(b.Edits);
    }

    // ---- Locking: At the lock time, the shortcut and Why ----

    // At the lock time comes after the battery's row, and the shortcut and Why close the tab, the
    // shortcut under a line. The rows and their lines are the ones the Extras tab had.
    [TestMethod]
    public void LockingHasAtTheLockTimeAndEndsWithTheShortcutAndWhy()
    {
        var v = Lang.Current.V1;
        foreach (bool battery in new[] { true, false })
        {
            var before = WelcomeForm.HasBattery;
            WelcomeForm.HasBattery = () => battery;
            try
            {
                var b = Build(new LockingTab(new Host()), new Config { LockShortcut = "Ctrl+Alt+L" });
                var titles = Rows(b.Page).Select(r => r.Title).ToList();
                var expected = new List<string> { v.AtLockTime, v.CountController, v.AppRules, v.Shortcut, v.WhyNotLocking };
                if (battery) expected.Insert(0, v.OnBattery);
                CollectionAssert.AreEqual(expected, titles, "battery " + battery);
                Assert.AreEqual(v.AtLockTimeDetail, AtLockTimeRow(b.Page).Detail);
                Assert.AreEqual(v.ShortcutDetail, ShortcutRow(b.Page).Detail);
                Assert.AreEqual(v.WhyNotLockingDetail, WhyRow(b.Page).Detail);
                Assert.IsTrue(((FlatPanel)b.Page).HasLineAbove(ShortcutRow(b.Page)), "the shortcut starts under a line");
                Assert.AreSame(ShortcutRow(b.Page), b.Page.Controls.OfType<Control>().Reverse().Skip(1).First(), "the shortcut, then Why, last on the tab");
                Assert.AreSame(WhyRow(b.Page), b.Page.Controls.OfType<Control>().Last());

                CollectionAssert.AreEqual(new[] { "Lock the PC", "Lock and turn off the screen", "Lock and sleep", "Don't lock" },
                    Of<DropDown>(AtLockTimeRow(b.Page)).Choices.ToList());
                Assert.AreEqual("Lock the PC", Of<DropDown>(AtLockTimeRow(b.Page)).Text, "a new install locks");
                CollectionAssert.AreEqual(new[] { v.CtrlAltL, v.Off, v.ChooseAnotherShortcut }, Of<DropDown>(ShortcutRow(b.Page)).Choices.ToList());
                Assert.AreEqual(v.CtrlAltL, Of<DropDown>(ShortcutRow(b.Page)).Text);
                Assert.IsFalse(All<ShortcutNote>(b.Page).Single().Wanted, "no line while the shortcut works");
                Assert.AreEqual(v.Show, Of<KitButton>(WhyRow(b.Page)).Text);
                Assert.IsEmpty(b.Edits);
            }
            finally
            {
                WelcomeForm.HasBattery = before;
            }
        }

        // A new install has no shortcut until one is picked: the row shows Off, with no line under it.
        var fresh = Build(new LockingTab(new Host()));
        Assert.AreEqual(v.Off, Of<DropDown>(ShortcutRow(fresh.Page)).Text);
        Assert.AreEqual("", Of<DropDown>(ShortcutRow(fresh.Page)).Value);
        Assert.IsFalse(All<ShortcutNote>(fresh.Page).Single().Wanted, "no line while off");
        Assert.IsEmpty(fresh.Edits);
    }

    // One list, two keys: Don't lock is lockWhenAway off alone, keeping whenLocking for later; the
    // others are whenLocking, and lockWhenAway too when locking was off.
    [TestMethod]
    public void AtTheLockTimeWritesLockWhenAwayAndWhenLocking()
    {
        var b = Build(new LockingTab(new Host()), new Config { WhenLocking = WhenLocking.LockSleep });
        var list = Of<DropDown>(AtLockTimeRow(b.Page));
        Assert.AreEqual("Lock and sleep", list.Text);

        Pick(list, 3);
        var off = OnlyKey(b, "lockWhenAway");
        Assert.IsFalse(off.LockWhenAway);
        Assert.AreEqual(WhenLocking.LockSleep, off.WhenLocking, "kept for when locking is on again");
        Assert.AreEqual("Don't lock", list.Text);

        var before = b.Config.Copy();
        Pick(list, 1);
        var on = b.Apply();
        CollectionAssert.AreEqual(new[] { "lockWhenAway", "whenLocking" }, Changed(before, on));
        Assert.IsTrue(on.LockWhenAway);
        Assert.AreEqual(WhenLocking.LockScreenOff, on.WhenLocking);

        Pick(list, 2);
        Assert.AreEqual(WhenLocking.LockSleep, OnlyKey(b, "whenLocking").WhenLocking);
        Pick(list, 0);
        Assert.AreEqual(WhenLocking.Lock, OnlyKey(b, "whenLocking").WhenLocking);
        Assert.AreEqual("Lock the PC", list.Text);
    }

    // Whatever turns locking on or off, or changes what comes after the lock, the list follows on
    // the next refill: Don't lock whenever locking is off, whatever whenLocking holds. A refill asks
    // for nothing.
    [TestMethod]
    public void AtTheLockTimeFollowsTheSettings()
    {
        var b = Build(new LockingTab(new Host()));
        var list = Of<DropDown>(AtLockTimeRow(b.Page));
        foreach (var (lockWhenAway, whenLocking, shown) in new[]
        {
            (false, WhenLocking.Lock, "Don't lock"),
            (false, WhenLocking.LockScreenOff, "Don't lock"),
            (true, WhenLocking.LockScreenOff, "Lock and turn off the screen"),
            (false, WhenLocking.LockSleep, "Don't lock"),
            (true, WhenLocking.LockSleep, "Lock and sleep"),
            (true, (WhenLocking)7, "Lock the PC"),
            (true, WhenLocking.Lock, "Lock the PC"),
        })
        {
            b.Tab.Refill(new Config { LockWhenAway = lockWhenAway, WhenLocking = whenLocking });
            Assert.AreEqual(shown, list.Text, $"{lockWhenAway} {whenLocking}");
        }
        Assert.IsEmpty(b.Edits);
        Assert.AreSame(LockingTab.DontLock, LockingTab.AtLockTimeOf(new Config { LockWhenAway = false, WhenLocking = WhenLocking.LockSleep }));
        Assert.AreEqual(WhenLocking.LockSleep, LockingTab.AtLockTimeOf(new Config { WhenLocking = WhenLocking.LockSleep }));
    }

    // Off: the app lets go of the shortcut at once, before the save.
    [TestMethod]
    public void TheShortcutListEditsItsKeyAndTellsTheAppAtOnce()
    {
        var host = new Host();
        var b = Build(new LockingTab(host), new Config { LockShortcut = "Ctrl+Alt+L" });
        Pick(Of<DropDown>(ShortcutRow(b.Page)), 1);
        Assert.AreEqual("", OnlyKey(b, "lockShortcut").LockShortcut);
        CollectionAssert.AreEqual(new[] { "" }, host.Applied);
        Pick(Of<DropDown>(ShortcutRow(b.Page)), 0);
        Assert.AreEqual("Ctrl+Alt+L", OnlyKey(b, "lockShortcut").LockShortcut);
        CollectionAssert.AreEqual(new[] { "", "Ctrl+Alt+L" }, host.Applied);
    }

    [TestMethod]
    public void ExtrasChoicesEachEditOneKey()
    {
        var host = new Host();
        var b = Build(Extras(host), new Config { LockShortcut = "Ctrl+Alt+L" });
        var rows = Rows(b.Page);

        // Off is jitterFilter alone, keeping the size; a size is jitterPixels, and jitterFilter too
        // when it was off, so a file keeps meaning what it meant.
        var jitter = Of<DropDown>(rows[JitterRow]);
        Pick(jitter, 0);
        var off = OnlyKey(b, "jitterFilter");
        Assert.IsFalse(off.JitterFilter);
        Assert.AreEqual(5, off.JitterPixels);
        Assert.AreEqual("Off", jitter.Text);
        Pick(jitter, 2);
        var on = OnlyKey(b, "jitterFilter");
        Assert.IsTrue(on.JitterFilter);
        Assert.AreEqual(5, on.JitterPixels);
        Pick(jitter, 1);
        Assert.AreEqual(3, OnlyKey(b, "jitterPixels").JitterPixels);
        Pick(jitter, 3);
        Assert.AreEqual(10, OnlyKey(b, "jitterPixels").JitterPixels);
        Pick(jitter, 0);
        OnlyKey(b, "jitterFilter");
        Pick(jitter, 3);
        Assert.IsTrue(OnlyKey(b, "jitterFilter").JitterFilter, "back on at the 10 pixels it had");

        var loudness = Of<DropDown>(rows[LoudnessRow]);
        Pick(loudness, 1);
        Assert.AreEqual(0.03, OnlyKey(b, "audioPeakThreshold").AudioPeakThreshold);
        Pick(loudness, 2);
        Assert.AreEqual(0.1, OnlyKey(b, "audioPeakThreshold").AudioPeakThreshold);
        Pick(loudness, 0);
        Assert.AreEqual(0.01, OnlyKey(b, "audioPeakThreshold").AudioPeakThreshold);
        Pick(Of<DropDown>(rows[SilenceRow]), 2);
        Assert.AreEqual(60, OnlyKey(b, "audioSilenceSeconds").AudioSilenceSeconds);

        var logSize = Of<DropDown>(rows[LogSizeRow]);
        Pick(logSize, 0);
        Assert.AreEqual(256, OnlyKey(b, "logMaxKilobytes").LogMaxKilobytes);
        Pick(logSize, 2);
        Assert.AreEqual(1024, OnlyKey(b, "logMaxKilobytes").LogMaxKilobytes);
        Pick(logSize, 3);
        Assert.AreEqual(4096, OnlyKey(b, "logMaxKilobytes").LogMaxKilobytes);
        Assert.IsEmpty(host.Applied, "nothing here registers a shortcut");
    }

    // The named loudness levels are 1%, 3% and 10% of full volume, as the file has always kept them
    // (0.01, 0.03 and 0.1).
    [TestMethod]
    public void TheLoudnessLevelsArePercentsOfFullVolume()
    {
        CollectionAssert.AreEqual(new[] { 1, 3, 10 }, AdvancedTab.LoudnessPercents);
        CollectionAssert.AreEqual(new[] { 0.01, 0.03, 0.1 }, AdvancedTab.LoudnessChoices);
        Assert.AreEqual(new Config().AudioPeakThreshold, AdvancedTab.LoudnessChoices[0], "the default is the first level");
        Assert.AreEqual(3, AdvancedTab.WholePercent(0.03));
        Assert.AreEqual(100, AdvancedTab.WholePercent(1));
        Assert.IsNull(AdvancedTab.WholePercent(0.002), "a fraction of a percent opens the box on its own start");
    }

    // Each list's Custom… takes a number in its own unit and range, the nearest end for one outside
    // it, and shows it as itself.
    [TestMethod]
    public void ExtrasCustomTakesItsOwnUnitAndRange()
    {
        var text = Lang.Current;
        var b = Build(Extras(new Host()));
        var rows = Rows(b.Page);
        var jitter = Of<DropDown>(rows[JitterRow]);
        var loudness = Of<DropDown>(rows[LoudnessRow]);
        var silence = Of<DropDown>(rows[SilenceRow]);
        var logSize = Of<DropDown>(rows[LogSizeRow]);

        Type(jitter, "7");
        Assert.AreEqual(7, OnlyKey(b, "jitterPixels").JitterPixels);
        Assert.AreEqual("Under 7 pixels", jitter.Text);
        Type(jitter, "80");
        Assert.AreEqual(50, OnlyKey(b, "jitterPixels").JitterPixels);
        Type(jitter, "0");
        Assert.AreEqual(1, OnlyKey(b, "jitterPixels").JitterPixels);
        Assert.AreEqual("Under 1 pixel", jitter.Text);
        Type(jitter, "3");
        Assert.AreEqual(3, OnlyKey(b, "jitterPixels").JitterPixels);
        Assert.AreEqual(1, jitter.Choices.Count(c => c == "Under 3 pixels"), "a ready-made size chooses itself");

        Type(loudness, "4");
        Assert.AreEqual(0.04, OnlyKey(b, "audioPeakThreshold").AudioPeakThreshold, 1e-12);
        Assert.AreEqual("4%", loudness.Text);
        Type(loudness, "250");
        Assert.AreEqual(1.0, OnlyKey(b, "audioPeakThreshold").AudioPeakThreshold);
        Assert.AreEqual("100%", loudness.Text);
        Type(loudness, "3");
        Assert.AreEqual(0.03, OnlyKey(b, "audioPeakThreshold").AudioPeakThreshold);
        Assert.AreEqual(text.V1.MediumAndLoudSounds, loudness.Text, "3% is the named level");

        Type(silence, "45");
        Assert.AreEqual(45, OnlyKey(b, "audioSilenceSeconds").AudioSilenceSeconds);
        Assert.AreEqual("45 seconds", silence.Text);
        Type(silence, "2");
        Assert.AreEqual(5, OnlyKey(b, "audioSilenceSeconds").AudioSilenceSeconds);
        Type(silence, "9999");
        Assert.AreEqual(600, OnlyKey(b, "audioSilenceSeconds").AudioSilenceSeconds);
        Assert.AreEqual("10 minutes", silence.Text);

        Type(logSize, "2048");
        Assert.AreEqual(2048, OnlyKey(b, "logMaxKilobytes").LogMaxKilobytes);
        Assert.AreEqual("2 MB", logSize.Text);
        Type(logSize, "100");
        Assert.AreEqual(100, OnlyKey(b, "logMaxKilobytes").LogMaxKilobytes);
        Assert.AreEqual("100 KB", logSize.Text);
        Type(logSize, "10");
        Assert.AreEqual(64, OnlyKey(b, "logMaxKilobytes").LogMaxKilobytes);
        Type(logSize, "999999");
        Assert.AreEqual(65536, OnlyKey(b, "logMaxKilobytes").LogMaxKilobytes);
        Assert.AreEqual("64 MB", logSize.Text);
    }

    // The boxes say their unit, their range, and what they used, in words a screen reader reads too.
    [TestMethod]
    public void ExtrasCustomBoxesSayTheirUnitAndRange()
    {
        var b = Build(Extras(new Host()));
        var rows = Rows(b.Page);
        foreach (var (row, name, range, typed, clamped) in new[]
        {
            (JitterRow, "Ignore tiny mouse movements, in pixels", "From 1 pixel to 50 pixels.", "99", "Only 1 pixel to 50 pixels work, so it uses 50 pixels."),
            (LoudnessRow, "How loud sound must be to count, as a percent of full volume", "From 1% to 100%.", "0", "Only 1% to 100% work, so it uses 1%."),
            (SilenceRow, "Silence before it counts as stopped, in seconds", "From 5 to 600 seconds.", "1000", "Only 5 to 600 seconds work, so it uses 10 minutes."),
            (LogSizeRow, "Log size, in KB", "From 64 KB to 65536 KB.", "1", "Only 64 KB to 65536 KB work, so it uses 64 KB."),
        })
        {
            var list = Of<DropDown>(rows[row]);
            CustomBox? box = null;
            list.Presenter = opened => box = opened;
            list.OpenCustom(0);
            Assert.IsNotNull(box);
            var input = box.Field.Controls.OfType<TextBox>().Single();
            Assert.AreEqual(name, input.AccessibilityObject.Name);
            Assert.AreEqual(range, box.Field.Hint, name);
            input.Text = typed;
            Assert.AreEqual(clamped, box.Field.Hint, name);
            input.Text = "";
            StringAssert.StartsWith(box.Field.Hint, "Type a number", name);
            Assert.IsTrue(box.Key(Keys.Escape));
            Assert.IsEmpty(b.Edits, "Escape changes nothing");
        }
    }

    [TestMethod]
    public void OpenFolderOpensTheSettingsFolder()
    {
        var opened = new List<string>();
        var b = Build(new AdvancedTab(new Host(), opened.Add));
        Of<KitButton>(Rows(b.Page)[FolderRow]).PerformClick();
        CollectionAssert.AreEqual(new[] { Packaged.SettingsFolder() }, opened);
        Assert.IsEmpty(b.Edits);
    }

    [TestMethod]
    public void ShowOpensTheWhyWindow()
    {
        int shown = 0;
        var b = Build(new LockingTab(new Host()) { ShowWhy = _ => shown++ });
        Of<KitButton>(WhyRow(b.Page)).PerformClick();
        Assert.AreEqual(1, shown);
        Assert.IsEmpty(b.Edits);
    }

    // Show asks the app for the welcome, which closes the settings window first; the page changes
    // nothing itself.
    [TestMethod]
    public void ShowTheWelcomeAgainAsksTheApp()
    {
        var host = new Host();
        var b = Build(Extras(host));
        Of<KitButton>(Rows(b.Page)[WelcomeRow]).PerformClick();
        Assert.AreEqual(1, host.WelcomesAsked);
        Assert.AreEqual(0, host.RemovesAsked);
        Assert.IsEmpty(b.Edits);
    }

    // Reset… asks first, in the app's words; Cancel changes nothing. Reset puts every setting back
    // as a new install has it, through the normal path, and keeps the language, that the welcome was
    // seen and the tab the window is on.
    [TestMethod]
    public void ResetAsksFirstThenPutsEverySettingBackButTheLanguage()
    {
        var v = Lang.Current.V1;
        var host = new Host();
        var asked = new List<Question>();
        bool answer = false;
        var tab = new AdvancedTab(host, _ => Assert.Fail("no folder"))
        {
            Ask = (_, question) =>
            {
                asked.Add(question);
                return answer;
            },
        };
        var changed = new Config
        {
            Language = "ar",
            Welcomed = true,
            SettingsTab = 4,
            IdleMinutes = 45,
            WhenLocking = WhenLocking.LockSleep,
            LockShortcut = "Ctrl+Alt+K",
            JitterFilter = false,
            AudioPeakThreshold = 0.1,
            LogMaxKilobytes = 4096,
            Theme = ThemeChoice.Dark,
            BreakReminders = true,
            AppRules = [new AppRule("notepad.exe", Effect.Never)],
        };
        var b = Build(tab, changed);
        var reset = Of<KitButton>(Rows(b.Page)[ResetRow]);

        reset.PerformClick();
        Assert.AreEqual(new Question("Reset all settings?", "Every setting goes back to how it was when you first started SteppedOut. Your language stays.", "Reset"), asked.Single());
        Assert.AreEqual(v.ResetTitle, asked[0].Title);
        Assert.IsEmpty(b.Edits, "Cancel changes nothing");
        Assert.IsEmpty(host.Applied);

        answer = true;
        reset.PerformClick();
        var after = b.Apply();
        var expected = Config.FirstStart();
        expected.Language = "ar";
        expected.Welcomed = true;
        expected.SettingsTab = 4;
        Assert.IsTrue(after.SameAs(expected), "every other setting is a new install's");
        CollectionAssert.AreEqual(new[] { "" }, host.Applied, "the shortcut is let go at once, as when Off is picked");
        Assert.AreEqual("Under 5 pixels", Of<DropDown>(Rows(b.Page)[JitterRow]).Text, "the page shows it at once");
        Assert.AreEqual(0, host.RemovesAsked);
    }

    // Remove… asks first, in the app's words; only Remove and quit hands it to the app.
    [TestMethod]
    public void RemoveAsksFirstAndOnlyThenAsksTheApp()
    {
        var host = new Host();
        var asked = new List<Question>();
        bool answer = false;
        var tab = new AdvancedTab(host, _ => Assert.Fail("no folder"))
        {
            Ask = (_, question) =>
            {
                asked.Add(question);
                return answer;
            },
        };
        var b = Build(tab);
        var remove = Of<KitButton>(Rows(b.Page)[RemoveRow]);
        remove.PerformClick();
        Assert.AreEqual(new Question("Remove SteppedOut's files?",
            "This deletes SteppedOut's settings, log and today's counts on this PC, turns off Start with Windows, and quits SteppedOut. It can't be undone.",
            "Remove and quit"), asked.Single());
        Assert.AreEqual(0, host.RemovesAsked, "Cancel removes nothing");
        answer = true;
        remove.PerformClick();
        Assert.AreEqual(1, host.RemovesAsked);
        Assert.IsEmpty(b.Edits);
    }

    // The window that asks: Cancel has the keyboard and is what Escape and the X do; only the other
    // button says yes.
    [TestMethod]
    public void TheQuestionWindowStartsOnCancel()
    {
        var question = new Question("Reset all settings?", "Every setting goes back.", "Reset");
        using (var window = new QuestionWindow(question, Lang.Current))
        {
            Assert.AreSame(window.Dismiss, window.ActiveControl);
            Assert.AreSame(window.Dismiss, window.CancelButton);
            Assert.IsNull(window.AcceptButton, "Enter presses the button with the keyboard, Cancel at first");
            Assert.AreEqual("Reset", window.GoAhead.Text);
            Assert.AreEqual(Lang.Current.V1.Cancel, window.Dismiss.Text);
            Assert.AreEqual(question.Title, window.Title.Text);
            Assert.AreEqual(question.Body, window.Body.Text);
            Assert.IsLessThanOrEqualTo(window.Dismiss.Left, window.GoAhead.Right, "Reset, then Cancel at the end");
            Assert.IsLessThanOrEqualTo(window.ClientSize.Width, window.Dismiss.Right);
            Assert.IsGreaterThanOrEqualTo(0, window.GoAhead.Left);
            Assert.IsLessThanOrEqualTo(window.GoAhead.Top, window.Body.Bottom);
        }
        using (var window = new QuestionWindow(question, Lang.Current))
        {
            _ = window.Handle;
            Click(window.GoAhead);
            Assert.AreEqual(DialogResult.OK, window.DialogResult);
        }
        using (var window = new QuestionWindow(question, Lang.Current))
        {
            _ = window.Handle;
            Click(window.Dismiss);
            Assert.AreEqual(DialogResult.Cancel, window.DialogResult);
        }
    }

    // Save to file… writes the settings shown, waiting changes and all, in config.json's shape, to
    // the file picked; Load from file… reads one back through the normal path. Start with Windows is
    // in neither, as it is not in config.json. Temp files only.
    [TestMethod]
    public void SaveAndLoadRoundTripThroughAFile()
    {
        string folder = Path.Combine(TestRunFolders.Scratch, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string file = Path.Combine(folder, "SteppedOut settings.json");
            var told = new List<string>();
            var saved = new Config { Welcomed = true, IdleMinutes = 25, WhenLocking = WhenLocking.LockScreenOff, LogMaxKilobytes = 1024, LockShortcut = "Ctrl+Alt+K" };
            var host = new Host();
            var b = Build(new AdvancedTab(host, _ => Assert.Fail("no folder")) { PickSaveFile = _ => file, Tell = (_, words) => told.Add(words) }, saved);
            Pick(Of<DropDown>(Rows(b.Page)[JitterRow]), 1);
            b.Apply();
            Of<ButtonPair>(Rows(b.Page)[SaveLoadRow]).First.PerformClick();
            Assert.IsEmpty(told);
            Assert.IsEmpty(b.Edits, "saving changes nothing");
            string written = File.ReadAllText(file);
            StringAssert.Contains(written, "\"whenLocking\": \"lockScreenOff\"");
            StringAssert.Contains(written, "\"jitterPixels\": 3", "the change waiting to be saved is in it");
            using (var doc = JsonDocument.Parse(written))
            {
                CollectionAssert.AreEquivalent(KeysOf(saved).Keys.ToList(), doc.RootElement.EnumerateObject().Select(p => p.Name).ToList(), "config.json's shape, and nothing more");
            }
            Assert.IsFalse(written.Contains("startWithWindows", StringComparison.OrdinalIgnoreCase));
            var back = Config.Load(file, out string? problem, out string? warning);
            Assert.IsNull(problem);
            Assert.IsNull(warning);
            Assert.AreEqual(WhenLocking.LockScreenOff, back.WhenLocking);

            // Loaded into a page on other settings: they become the file's, through the edit, with
            // the shortcut registered at once.
            var other = new Config { Welcomed = true, SettingsTab = 2, Language = "en" };
            var loader = Build(new AdvancedTab(host, _ => Assert.Fail("no folder")) { PickLoadFile = _ => file, Tell = (_, words) => told.Add(words) }, other);
            Of<ButtonPair>(Rows(loader.Page)[SaveLoadRow]).Second.PerformClick();
            var after = loader.Apply();
            Assert.IsEmpty(told);
            Assert.AreEqual(25, after.IdleMinutes);
            Assert.AreEqual(WhenLocking.LockScreenOff, after.WhenLocking);
            Assert.AreEqual(3, after.JitterPixels);
            Assert.AreEqual(1024, after.LogMaxKilobytes);
            Assert.AreEqual("Ctrl+Alt+K", after.LockShortcut);
            Assert.AreEqual(2, after.SettingsTab, "the window stays on its tab");
            Assert.AreEqual("Ctrl+Alt+K", host.Applied.Last());
            Assert.AreEqual("1 MB", Of<DropDown>(Rows(loader.Page)[LogSizeRow]).Text, "the page shows it at once");

            // Closing Windows' window does nothing.
            var none = Build(new AdvancedTab(host, _ => Assert.Fail("no folder")) { PickLoadFile = _ => null, PickSaveFile = _ => null, Tell = (_, words) => told.Add(words) });
            Of<ButtonPair>(Rows(none.Page)[SaveLoadRow]).First.PerformClick();
            Of<ButtonPair>(Rows(none.Page)[SaveLoadRow]).Second.PerformClick();
            Assert.IsEmpty(none.Edits);
            Assert.IsEmpty(told);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // A file that can't be used is checked exactly as config.json is, and the page says so plainly
    // and changes nothing; one with a value it could not read loads the rest and says that.
    [TestMethod]
    public void LoadSaysPlainlyWhenAFileCannotBeUsed()
    {
        var v = Lang.Current.V1;
        string folder = Path.Combine(TestRunFolders.Scratch, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            foreach (var (contents, said) in new[]
            {
                ("not json at all", v.LoadCantUse),
                ("[1, 2, 3]", v.LoadCantUse),
                ("{ \"configVersion\": 1, \"idleMinutes\": \"soon\" }", v.LoadCantUse),
                ("{ \"idleMinutes\": 30, \"welcomed\": true }", v.LoadTooOld),
                ("{ \"suppressWhilePlaying\": true }", v.LoadTooOld),
                ("{}", v.LoadTooOld),
            })
            {
                string file = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllText(file, contents);
                var told = new List<string>();
                var b = Build(new AdvancedTab(new Host(), _ => Assert.Fail("no folder")) { PickLoadFile = _ => file, Tell = (_, words) => told.Add(words) });
                Of<ButtonPair>(Rows(b.Page)[SaveLoadRow]).Second.PerformClick();
                CollectionAssert.AreEqual(new[] { said }, told, contents);
                Assert.IsEmpty(b.Edits, contents);
                Assert.AreEqual(contents, File.ReadAllText(file), "the file is only read");
            }

            var gone = AdvancedTab.Read(Path.Combine(folder, "gone.json"), Lang.Current);
            Assert.IsNull(gone.Settings);
            Assert.AreEqual(v.LoadCantOpen, gone.Problem);
            string huge = Path.Combine(folder, "huge.json");
            File.WriteAllText(huge, "{ \"configVersion\": 1 }" + new string(' ', (int)AdvancedTab.LongestSettingsFile));
            var tooBig = AdvancedTab.Read(huge, Lang.Current);
            Assert.IsNull(tooBig.Settings);
            Assert.AreEqual(v.LoadCantOpen, tooBig.Problem);

            string partly = Path.Combine(folder, "partly.json");
            File.WriteAllText(partly, "{ \"configVersion\": 1, \"idleMinutes\": 20, \"whenLocking\": \"explode\", \"theme\": \"dark\" }");
            var toldPartly = new List<string>();
            var p = Build(new AdvancedTab(new Host(), _ => Assert.Fail("no folder")) { PickLoadFile = _ => partly, Tell = (_, words) => toldPartly.Add(words) });
            Of<ButtonPair>(Rows(p.Page)[SaveLoadRow]).Second.PerformClick();
            var loaded = p.Apply();
            Assert.AreEqual(20, loaded.IdleMinutes);
            Assert.AreEqual(ThemeChoice.Dark, loaded.Theme);
            Assert.AreEqual(WhenLocking.Lock, loaded.WhenLocking, "the value it could not read is the default");
            CollectionAssert.AreEqual(new[] { v.LoadPartly }, toldPartly);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // A file Windows will not let it write is said plainly; nothing changes.
    [TestMethod]
    public void SaveSaysPlainlyWhenTheFileCannotBeWritten()
    {
        string folder = Path.Combine(TestRunFolders.Scratch, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            // A folder by the file's name: no file can be written there.
            string taken = Path.Combine(folder, "taken.json");
            Directory.CreateDirectory(taken);
            var told = new List<string>();
            var b = Build(new AdvancedTab(new Host(), _ => Assert.Fail("no folder")) { PickSaveFile = _ => taken, Tell = (_, words) => told.Add(words) });
            Of<ButtonPair>(Rows(b.Page)[SaveLoadRow]).First.PerformClick();
            CollectionAssert.AreEqual(new[] { Lang.Current.V1.SaveCantWrite }, told);
            Assert.IsEmpty(b.Edits);
            Assert.IsTrue(Directory.Exists(taken));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // Values set by hand in the file show as themselves, and showing them changes nothing.
    [TestMethod]
    public void HandSetValuesShowAsThemselves()
    {
        var text = Lang.Current;
        var config = new Config
        {
            BreakMinutes = 45,
            AudioPeakThreshold = 0.002,
            AudioSilenceSeconds = 45,
            LockShortcut = "ctrl+shift+f9",
            JitterPixels = 80,
            LogMaxKilobytes = 100,
        };
        var breaks = Build(new BreaksTab(), config);
        Assert.AreEqual(text.OptionDuration(45), Of<DropDown>(Rows(breaks.Page)[1]).Text);
        var advanced = Build(Extras(new Host()), config);
        var rows = Rows(advanced.Page);
        Assert.AreEqual("0.2%", Of<DropDown>(rows[LoudnessRow]).Text);
        Assert.AreEqual(text.SilenceLabel(45), Of<DropDown>(rows[SilenceRow]).Text);
        Assert.AreEqual("Under 80 pixels", Of<DropDown>(rows[JitterRow]).Text);
        Assert.AreEqual("100 KB", Of<DropDown>(rows[LogSizeRow]).Text);
        var locking = Build(new LockingTab(new Host()), config);
        var list = Of<DropDown>(ShortcutRow(locking.Page));
        Assert.AreEqual(Shortcut.Parse("Ctrl+Shift+F9")!.Value.Display, list.Text);
        Assert.HasCount(4, list.Choices, "one row for it, beside Ctrl+Alt+L, Off and Choose another");
        breaks.Tab.Refill(config);
        advanced.Tab.Refill(config);
        locking.Tab.Refill(config);
        Assert.AreEqual(0, breaks.Edits.Count + advanced.Edits.Count + locking.Edits.Count);
        Assert.AreEqual(45, breaks.Config.BreakMinutes);
        // "ctrl+alt+l" in the file is the usual Ctrl+Alt+L, not a second row with the same words.
        var lower = Build(new LockingTab(new Host()), new Config { LockShortcut = "ctrl+alt+l" });
        var lowerList = Of<DropDown>(ShortcutRow(lower.Page));
        Assert.HasCount(3, lowerList.Choices);
        Assert.AreEqual("Ctrl+Alt+L", lowerList.Value);
        // Ignoring them turned off shows Off, whatever the size kept for later.
        var off = Build(Extras(new Host()), new Config { JitterFilter = false, JitterPixels = 80 });
        Assert.AreEqual("Off", Of<DropDown>(Rows(off.Page)[JitterRow]).Text);
    }

    [TestMethod]
    public void ALoudnessCloseToALevelIsThatLevel()
    {
        Assert.AreEqual(0.01, AdvancedTab.Level(0.0100000001));
        Assert.AreEqual(0.03, AdvancedTab.Level(0.03));
        Assert.AreEqual(0.002, AdvancedTab.Level(0.002));
    }

    // The line under the shortcut list follows what the app says about registering it, from the first
    // build, and stops listening once the page is gone.
    [TestMethod]
    public void TheShortcutLineFollowsTheApp()
    {
        var v = Lang.Current.V1;
        var host = new Host { ShortcutResult = HotKeyResult.InUse };
        var b = Build(new LockingTab(host), new Config { LockShortcut = "Ctrl+Alt+L" });
        var note = All<ShortcutNote>(b.Page).Single();
        Assert.IsTrue(note.Wanted, "shown from the start");
        Assert.AreEqual(string.Format(v.ShortcutRowInUse, Shortcut.Default.Display), note.Text);
        host.Tell(HotKeyResult.Ok);
        Assert.IsFalse(note.Wanted);
        host.Tell(HotKeyResult.TypesALetter);
        Assert.AreEqual(string.Format(v.ShortcutRowTypesALetter, Shortcut.Default.Display), note.Text);
        host.Tell(HotKeyResult.Invalid);
        Assert.AreEqual(v.ShortcutInvalid, note.Text);
        // Off: nothing to say.
        Pick(Of<DropDown>(ShortcutRow(b.Page)), 1);
        Assert.IsFalse(note.Wanted);
        Assert.AreEqual(1, host.Listeners);
        b.Page.Dispose();
        Assert.AreEqual(0, host.Listeners, "a page that is gone stops listening");
        host.Tell(HotKeyResult.InUse);
    }

    // "Choose another shortcut…": picked from the list it opens the window once, and the list never
    // stays on it. Stepped onto with the arrow keys it only goes back. Picking the shortcut already
    // shown opens nothing.
    [TestMethod]
    public void ChooseAnotherShortcutOpensTheWindowOnlyWhenPicked()
    {
        var host = new Host();
        var later = new List<Action>();
        var asked = new List<string>();
        string? answer = "Ctrl+Alt+K";
        var tab = new LockingTab(host)
        {
            Later = later.Add,
            ChooseShortcut = (_, now) =>
            {
                asked.Add(now);
                return answer;
            },
        };
        var b = Build(tab, new Config { LockShortcut = "Ctrl+Alt+L" });
        var list = Of<DropDown>(ShortcutRow(b.Page));
        void RunLater()
        {
            var due = later.ToList();
            later.Clear();
            foreach (var a in due) a();
        }

        // Picking Ctrl+Alt+L, already shown: nothing.
        Pick(list, 0);
        RunLater();
        Assert.AreEqual(0, asked.Count + b.Edits.Count + host.Applied.Count);

        // The arrow keys: down onto Off, then onto Choose another, which puts the list back on Off.
        Press(list, System.Windows.Forms.Keys.Down);
        Assert.AreEqual("", OnlyKey(b, "lockShortcut").LockShortcut);
        Press(list, System.Windows.Forms.Keys.Down);
        Assert.AreEqual("", list.Value, "back on Off at once");
        RunLater();
        Assert.IsEmpty(asked, "no window from the arrow keys");
        Assert.IsEmpty(b.Edits);

        // Picked from the list: the window opens once, after the click, and its shortcut is kept.
        Pick(list, 2);
        Assert.AreEqual("", list.Value, "the list never shows Choose another");
        Assert.IsEmpty(asked, "not inside the click");
        RunLater();
        RunLater();
        CollectionAssert.AreEqual(new[] { "" }, asked);
        Assert.AreEqual("Ctrl+Alt+K", OnlyKey(b, "lockShortcut").LockShortcut);
        Assert.AreEqual(Shortcut.Parse("Ctrl+Alt+K")!.Value.Display, list.Text);

        // Cancelled: nothing changes.
        answer = null;
        Pick(list, 2);
        RunLater();
        Assert.HasCount(2, asked);
        Assert.IsEmpty(b.Edits);
        Assert.AreEqual("Ctrl+Alt+K", list.Value);
    }

    // ---- Refill and build ----

    static IEnumerable<Func<Host, ISettingsPage>> Pages() =>
    [
        _ => new AppearanceTab(),
        _ => new BreaksTab(),
        h => new AdvancedTab(h, _ => Assert.Fail("no folder")),
        h => new LockingTab(h),
    ];

    static IEnumerable<Config> Settings()
    {
        yield return new Config();
        yield return new Config
        {
            Theme = ThemeChoice.Dark,
            TextSize = TextSize.Large,
            MinutesOnTrayIcon = true,
            NotificationPlace = NotePosition.TopLeft,
            BreakReminders = true,
            BreakMinutes = 120,
            BreakSound = BreakSound.SteppedOut,
            EyeBreaks = true,
            SnoozeMinutes = 0,
            JitterFilter = false,
            LockShortcut = "",
            AudioPeakThreshold = 0.1,
            AudioSilenceSeconds = 10,
            LockWhenAway = false,
            WhenLocking = WhenLocking.LockSleep,
        };
        yield return new Config { BreakMinutes = 45, SnoozeMinutes = 5, LockShortcut = "Alt+F9", AudioPeakThreshold = 0.002, AudioSilenceSeconds = 45 };
    }

    [TestMethod]
    public void BuildingAndRefillingAskForNothing()
    {
        foreach (var make in Pages())
        {
            foreach (var config in Settings())
            {
                foreach (var other in Settings())
                {
                    var b = Build(make(new Host()), config);
                    b.Tab.Refill(other);
                    b.Tab.Refill(config);
                    Assert.IsEmpty(b.Edits, $"{b.Tab.GetType().Name}");
                    b.Page.Dispose();
                }
            }
        }
    }

    [TestMethod]
    public void RefillBeforeBuildingOrAfterThePageIsGoneIsHarmless()
    {
        foreach (var make in Pages())
        {
            var host = new Host { ShortcutResult = HotKeyResult.InUse };
            var tab = make(host);
            tab.Refill(new Config());
            var b = Build(tab);
            b.Page.Dispose();
            tab.Refill(new Config { BreakMinutes = 90, JitterFilter = false });
            host.Tell(HotKeyResult.Ok);
            Assert.IsEmpty(b.Edits);
        }
    }

    // A page whose refill failed still sends a person's changes afterwards.
    sealed class Failing : FlatTab
    {
        Toggle? toggle;
        public bool Throw { get; set; }

        public Toggle TheSwitch => toggle!;

        protected override void Fill(FlatPanel page, Action<Func<Config, Config>> edit)
        {
            toggle = Switch("test");
            page.Add(new FlatRow("x", null, toggle));
            toggle.CheckedChanged += (_, _) =>
            {
                bool on = toggle.Checked;
                edit(c => { c.EyeBreaks = on; return c; });
            };
        }

        protected override void Show(Config config)
        {
            toggle!.Checked = config.EyeBreaks;
            if (Throw) throw new InvalidOperationException("refill failed");
        }
    }

    [TestMethod]
    public void AFailedRefillDoesNotSilenceThePage()
    {
        var tab = new Failing();
        var b = Build(tab);
        tab.Throw = true;
        Assert.ThrowsExactly<InvalidOperationException>(() => tab.Refill(new Config { EyeBreaks = true }));
        Assert.IsEmpty(b.Edits);
        tab.Throw = false;
        Click(tab.TheSwitch);
        Assert.HasCount(1, b.Edits, "a person's change still goes out");
    }

    // An earlier build's controls, still around until the window disposes them, change nothing.
    [TestMethod]
    public void AnEarlierBuildChangesNothing()
    {
        var tab = new BreaksTab();
        var first = Build(tab);
        var edits = new List<Func<Config, Config>>();
        var second = tab.Build(new Config(), Lang.Current);
        tab.Edited += edits.Add;
        Click(Of<Toggle>(Rows(first.Page)[0]));
        Assert.IsEmpty(edits);
        Click(Of<Toggle>(Rows(second)[0]));
        Assert.HasCount(1, edits);
        second.Dispose();
        first.Page.Dispose();
    }

    [TestMethod]
    public void ArabicPagesBuildAndUseTheArabicWords()
    {
        Lang.Use("ar");
        var v = Lang.Current.V1;
        foreach (var make in Pages())
        {
            var b = Build(make(new Host()));
            Assert.IsGreaterThanOrEqualTo(4, Rows(b.Page).Count);
            Assert.IsTrue(Rows(b.Page).All(r => r.Title.Length > 0));
            Assert.IsEmpty(b.Edits);
            b.Page.Dispose();
        }
        var locking = Build(new LockingTab(new Host()), new Config { LockShortcut = "Ctrl+Alt+L" });
        Assert.AreEqual(v.CtrlAltL, Of<DropDown>(ShortcutRow(locking.Page)).Text);
        Assert.AreEqual(v.LockOnly, Of<DropDown>(AtLockTimeRow(locking.Page)).Text);
        var advanced = Build(new AdvancedTab(new Host(), _ => { }));
        Assert.AreEqual(Lang.Current.JitterLabel(5), Of<DropDown>(Rows(advanced.Page)[JitterRow]).Text);
    }

    // Every control a key reaches has a name, and a switch's name is its row's, without the On or Off
    // drawn beside it.
    [TestMethod]
    public void ControlsAreNamedForScreenReaders()
    {
        var v = Lang.Current.V1;
        foreach (var make in Pages())
        {
            var b = Build(make(new Host()));
            foreach (var row in Rows(b.Page))
            {
                var control = row.Controls.Cast<Control>().First(c => c is not Label && c != row.Below);
                Assert.IsFalse(string.IsNullOrWhiteSpace(control.AccessibilityObject.Name), row.Title);
                if (control is Toggle) Assert.AreEqual(row.Title, control.AccessibilityObject.Name);
            }
            b.Page.Dispose();
        }
    }

    // ---- The shortcut window ----

    static ShortcutDialog Dialog(Host host, Func<string, HotKeyResult>? check = null, bool win = false) =>
        new(host, "Ctrl+Alt+L", Lang.Current, check ?? (_ => HotKeyResult.Ok), () => win);

    [TestMethod]
    public void TheShortcutWindowRefusesWhatCannotWork()
    {
        var v = Lang.Current.V1;
        var host = new Host();
        using var d = Dialog(host);
        d.Take(System.Windows.Forms.Keys.L);
        Assert.IsFalse(d.SaveButton.Enabled, "a letter alone");
        Assert.AreEqual(v.ShortcutInvalid, d.Message);
        d.Take(System.Windows.Forms.Keys.Shift | System.Windows.Forms.Keys.L);
        Assert.IsFalse(d.SaveButton.Enabled, "Shift and a letter");
        d.Take(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.F4);
        Assert.IsFalse(d.SaveButton.Enabled, "Alt+F4");
        Assert.AreEqual("Alt+F4", d.Box.Shown);
        d.Take(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.K);
        Assert.IsTrue(d.SaveButton.Enabled, "Ctrl+Alt+K");
        Assert.AreEqual("", d.Message);
        d.Save();
        Assert.AreEqual(DialogResult.OK, d.DialogResult);
        Assert.AreEqual("Ctrl+Alt+K", d.Chosen);
        CollectionAssert.AreEqual(new[] { "Ctrl+Alt+K" }, host.Applied);
    }

    [TestMethod]
    public void TheShortcutWindowRefusesTheWindowsKeyAndALetterOnAKeyboard()
    {
        var v = Lang.Current.V1;
        using var win = Dialog(new Host(), win: true);
        win.Take(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.K);
        Assert.IsFalse(win.SaveButton.Enabled);
        Assert.AreEqual("Win+Ctrl+Alt+K", win.Box.Shown);
        var asked = new List<string>();
        using var typing = Dialog(new Host(), s =>
        {
            asked.Add(s);
            return HotKeyResult.TypesALetter;
        });
        typing.Take(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.E);
        Assert.IsFalse(typing.SaveButton.Enabled);
        Assert.AreEqual(v.ShortcutTypesALetter, typing.Message);
        CollectionAssert.AreEqual(new[] { "Ctrl+Alt+E" }, asked);
        typing.Save();
        Assert.AreEqual(DialogResult.None, typing.DialogResult, "Save does nothing while it is off");
    }

    // Another app has it: Save says so, has the app let go of it, and stays open holding nothing (the
    // old one pressed there would lock the PC, I1); closing then, any way, puts the old one back once.
    [TestMethod]
    public void TheShortcutWindowLetsGoOfANewOneAnotherAppHas()
    {
        var v = Lang.Current.V1;
        foreach (var close in new Action<ShortcutDialog>[] { d => d.Close(), d => d.Dismiss.PerformClick(), d => d.Dispose() })
        {
            var host = new Host { Register = s => s == "Ctrl+Alt+K" ? HotKeyResult.InUse : HotKeyResult.Ok };
            var d = Dialog(host);
            _ = d.Handle;
            d.Take(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.K);
            d.Save();
            Assert.AreEqual(DialogResult.None, d.DialogResult, "stays open");
            Assert.AreEqual(v.ShortcutInUse, d.Message);
            Assert.IsFalse(d.SaveButton.Enabled);
            Assert.IsNull(d.Chosen);
            CollectionAssert.AreEqual(new[] { "Ctrl+Alt+K", "" }, host.Applied);
            Assert.AreEqual(HotKeyResult.Ok, host.ShortcutResult);
            close(d);
            d.Dispose();
            CollectionAssert.AreEqual(new[] { "Ctrl+Alt+K", "", "Ctrl+Alt+L" }, host.Applied, "the old one back as it closes, once");
        }
    }

    // Should the app fail while trying the new one, the window still holds nothing, and the old one
    // goes back when it closes.
    [TestMethod]
    public void TheShortcutWindowPutsTheOldOneBackEvenAfterAFailure()
    {
        var host = new Host { Register = s => s == "Ctrl+Alt+K" ? throw new InvalidOperationException("no") : HotKeyResult.Ok };
        var d = Dialog(host);
        d.Take(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.K);
        d.Save();
        Assert.AreEqual("", host.Applied[^1]);
        Assert.AreEqual(DialogResult.None, d.DialogResult);
        d.Dispose();
        Assert.AreEqual("Ctrl+Alt+L", host.Applied[^1]);
    }

    // As the window shows (Form.OnLoad) the app is told to let go of the shortcut in use, so pressing
    // it types it into the box instead of locking the PC (I1). Cancelled or saved as the same one, it
    // comes back as the window closes; a new one saved is kept. The app hears the close after that.
    [TestMethod]
    public void TheShortcutWindowHoldsNoShortcutWhileOpen()
    {
        var load = typeof(Form).GetMethod("OnLoad", Hidden)!;
        ShortcutDialog Open(Host host)
        {
            var d = Dialog(host);
            load.Invoke(d, [EventArgs.Empty]);
            CollectionAssert.AreEqual(new[] { true }, host.Choosing, "told as it shows");
            Assert.IsEmpty(host.Applied);
            return d;
        }

        var cancelled = new Host();
        var d = Open(cancelled);
        d.Dispose();
        CollectionAssert.AreEqual(new[] { "Ctrl+Alt+L" }, cancelled.Applied, "cancelled: put back");
        CollectionAssert.AreEqual(new[] { true, false }, cancelled.Choosing);

        var same = new Host();
        d = Open(same);
        d.Take(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.L);
        Assert.AreEqual("Ctrl+Alt+L", d.Box.Shown, "the shortcut in use reaches the box");
        d.Save();
        Assert.AreEqual(DialogResult.OK, d.DialogResult);
        Assert.AreEqual("Ctrl+Alt+L", d.Chosen);
        d.Dispose();
        CollectionAssert.AreEqual(new[] { "Ctrl+Alt+L" }, same.Applied, "saved as the same one: put back");
        CollectionAssert.AreEqual(new[] { true, false }, same.Choosing);

        var chosen = new Host();
        d = Open(chosen);
        d.Take(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.K);
        d.Save();
        Assert.AreEqual("Ctrl+Alt+K", d.Chosen);
        d.Dispose();
        CollectionAssert.AreEqual(new[] { "Ctrl+Alt+K" }, chosen.Applied, "a new one: kept");
        CollectionAssert.AreEqual(new[] { true, false }, chosen.Choosing);
    }

    [TestMethod]
    public void TheShortcutWindowCancelsWithEscAndChangesNothing()
    {
        var host = new Host();
        using var d = Dialog(host);
        Assert.AreSame(d.Dismiss, d.CancelButton);
        Assert.AreEqual(DialogResult.Cancel, d.Dismiss.DialogResult);
        Assert.AreSame(d.SaveButton, d.AcceptButton);
        Assert.IsFalse(ShortcutDialog.KeyBox.Captures(System.Windows.Forms.Keys.Escape), "Esc reaches the window");
        d.Take(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.K);
        d.Close();
        Assert.IsEmpty(host.Applied);
        Assert.IsNull(d.Chosen);
    }

    // The keys the box takes before the window would: Alt and Ctrl chords (so Alt+F4 does not close the
    // window), F10 and the modifiers on their own; never Tab, Shift+Tab, Enter or Esc on their own.
    [TestMethod]
    public void TheKeyBoxTakesChordsAndLeavesTabToTheWindow()
    {
        Assert.IsTrue(ShortcutDialog.KeyBox.Captures(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.L));
        Assert.IsTrue(ShortcutDialog.KeyBox.Captures(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.L));
        Assert.IsTrue(ShortcutDialog.KeyBox.Captures(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.F4));
        Assert.IsTrue(ShortcutDialog.KeyBox.Captures(System.Windows.Forms.Keys.F10));
        Assert.IsTrue(ShortcutDialog.KeyBox.Captures(System.Windows.Forms.Keys.Menu | System.Windows.Forms.Keys.Alt));
        Assert.IsFalse(ShortcutDialog.KeyBox.Captures(System.Windows.Forms.Keys.Tab));
        Assert.IsFalse(ShortcutDialog.KeyBox.Captures(System.Windows.Forms.Keys.Shift | System.Windows.Forms.Keys.Tab));
        Assert.IsFalse(ShortcutDialog.KeyBox.Captures(System.Windows.Forms.Keys.Enter));
        Assert.IsFalse(ShortcutDialog.KeyBox.Captures(System.Windows.Forms.Keys.L));

        using var d = Dialog(new Host());
        var box = d.Box;
        // A system key message for Alt+F4, as the message loop hands it over before the window sees it.
        var message = Message.Create(IntPtr.Zero, 0x0104, (IntPtr)(int)System.Windows.Forms.Keys.F4, IntPtr.Zero);
        object[] args = [message, System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.F4];
        bool taken = (bool)typeof(Control).GetMethod("ProcessCmdKey", Hidden)!.Invoke(box, args)!;
        Assert.IsTrue(taken, "the box takes Alt+F4");
        Assert.AreEqual("Alt+F4", box.Shown);
        Assert.IsFalse(d.SaveButton.Enabled);
        // Letting go of Alt does not reach the window menu.
        var up = new KeyEventArgs(System.Windows.Forms.Keys.Menu | System.Windows.Forms.Keys.Alt);
        typeof(Control).GetMethod("OnKeyUp", Hidden)!.Invoke(box, [up]);
        Assert.IsTrue(up.Handled);
        // Tab is left to the window, to move on to Save and Cancel.
        bool tab = (bool)typeof(Control).GetMethod("IsInputKey", Hidden)!.Invoke(box, [System.Windows.Forms.Keys.Tab])!;
        Assert.IsFalse(tab);
        // A plain letter typed in the box shows what it is and says why it is no shortcut.
        Press(box, System.Windows.Forms.Keys.L);
        Assert.AreEqual("L", box.Shown);
        Assert.AreEqual(Lang.Current.V1.ShortcutInvalid, d.Message);
        // Ctrl and Alt held, waiting for the key.
        Press(box, System.Windows.Forms.Keys.Menu | System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Alt);
        Assert.AreEqual("Ctrl+Alt+", box.Shown);
    }

    // ---- The Why window ----

    [TestMethod]
    public void TheWhyWindowSaysTheLockTimeOfThisRunAndWhatChangesIt()
    {
        var text = Lang.Current;
        var v = text.V1;
        // A run started with --idle 0.5: the window says the run's time, not the saved one.
        var host = new Host { Status = new LockStatus { Kind = LockState.On, Time = LockTime.Plain(0.5m) } };
        using var d = new WhyDialog(host, () => true, text);
        Assert.AreEqual(text.WhyLockTime(LockTime.Plain(0.5m)), d.Heading);
        CollectionAssert.AreEqual(new[] { v.WhyNothing }, d.Lines.ToList());
        int builds = d.Builds;
        host.Lines = [new WhyLine(Signal.Sound, Effect.Extra(60), "wmplayer"), new WhyLine(Signal.Battery, Effect.Sooner(50))];
        d.Tick();
        var time = LockTime.Compute(0.5m, Evaluator.Effects(host.Lines));
        Assert.AreEqual(text.WhyLockTime(time), d.Heading);
        var (what, does) = text.WhyRow(host.Lines[0]);
        var (battery, sooner) = text.WhyRow(host.Lines[1]);
        CollectionAssert.AreEqual(new[] { what, does, battery, sooner }, d.Lines.ToList());
        Assert.AreEqual(builds + 1, d.Builds);
        // The same again a second later: nothing is made again.
        host.Lines = host.Lines.ToList();
        d.Tick();
        Assert.AreEqual(builds + 1, d.Builds);
        Assert.IsGreaterThanOrEqualTo(3, host.Asked);
        // A never: never.
        host.Lines = [new WhyLine(Signal.App, Effect.Never, "notepad")];
        d.Tick();
        Assert.AreEqual(v.WhyNeverNow, d.Heading);
    }

    // While paused (or away, locked, or with locking off) that is the reason, not "nothing".
    [TestMethod]
    public void TheWhyWindowSaysWhenItIsPausedOrLockingIsOff()
    {
        var text = Lang.Current;
        var paused = new LockStatus { Kind = LockState.Paused, Time = LockTime.Plain(15), PausedUntil = null };
        using (var d = new WhyDialog(new Host { Status = paused }, () => true, text))
        {
            Assert.AreEqual(text.Headline(paused, true), d.Heading);
            Assert.AreEqual(text.V1.PausedUntilTurnedBackOn, d.Heading);
            Assert.IsEmpty(d.Lines, "not \"nothing is delaying\"");
        }
        var on = new LockStatus { Kind = LockState.On, Time = LockTime.Plain(15), SecondsLeft = 60 };
        using (var d = new WhyDialog(new Host { Status = on }, () => false, text))
        {
            Assert.AreEqual(text.Headline(on, false), d.Heading);
            Assert.IsEmpty(d.Lines);
        }
        var dry = new LockStatus { Kind = LockState.On, Time = LockTime.Plain(15), DryRun = true };
        using (var d = new WhyDialog(new Host { Status = dry }, () => true, text))
        {
            Assert.AreEqual(string.Format(text.V1.DryRun, text.WhyLockTime(LockTime.Plain(15))), d.Heading);
        }
    }

    // Each row reads from its start: the thing at the left in English and at the right in Arabic,
    // what it does at the other end. The card is not mirrored with the window, so this is laid out.
    [TestMethod]
    public void TheWhyRowsStartAtTheReadingStartInBothLanguages()
    {
        foreach (var language in new[] { "en", "ar" })
        {
            Lang.Use(language);
            bool rtl = language == "ar";
            var host = new Host { Lines = [new WhyLine(Signal.Sound, Effect.Extra(60), "wmplayer"), new WhyLine(Signal.Battery, Effect.Sooner(50))] };
            using var d = new WhyDialog(host, () => true, Lang.Current);
            Assert.IsFalse(d.List.IsMirrored, "the card itself is laid out left to right");
            var labels = d.Scroller.Controls.OfType<Label>().ToList();
            Assert.HasCount(4, labels);
            int pad = d.LogicalToDeviceUnits(12);
            int width = d.List.Width;
            // Where a row's words sit in the card: 12 px in from either edge.
            int Left(Control c) => d.Scroller.Left + c.Left;
            int Right(Control c) => d.Scroller.Left + c.Right;
            for (int i = 0; i < labels.Count; i += 2)
            {
                var what = labels[i];
                var does = labels[i + 1];
                if (rtl)
                {
                    Assert.AreEqual(width - pad, Right(what), $"{language}: \"{what.Text}\" starts at the right");
                    Assert.AreEqual(pad, Left(does), $"{language}: \"{does.Text}\" ends at the left");
                    Assert.IsLessThan(what.Left, does.Right, $"{language}: what it does comes after the thing");
                }
                else
                {
                    Assert.AreEqual(pad, Left(what), $"{language}: \"{what.Text}\" starts at the left");
                    Assert.AreEqual(width - pad, Right(does), $"{language}: \"{does.Text}\" ends at the right");
                    Assert.IsLessThan(does.Left, what.Right, $"{language}: what it does comes after the thing");
                }
                Assert.AreEqual(ContentAlignment.TopRight, does.TextAlign, "set to the end; a label in Arabic turns it to the left");
            }
        }
    }

    // The icon beside a page's or a part's heading grows with the text size and stays whole, up to
    // Windows' largest (I16: the Away page's part icons were cut at 150%).
    [TestMethod]
    public void HeadingIconsFitTheirBoxesAtEveryTextSize()
    {
        try
        {
            foreach (float scale in new[] { 1f, 1.2f, 1.5f, 2.25f })
            {
                Theme.UseForShots(false, scale);
                foreach (bool part in new[] { false, true })
                {
                    using var head = new PageHeading(Glyphs.Clock, "Breaks", part ? null : "Short breaks away from the screen", part);
                    head.Width = 400;
                    head.Height = head.GetPreferredSize(new Size(400, 0)).Height;
                    var drawn = new List<(string Text, Font Font, Rectangle Box)>();
                    KitControl.TextDrawn = (_, text, font, box, _) => drawn.Add((text, font, box));
                    using (var picture = new Bitmap(head.Width, head.Height))
                        head.DrawToBitmap(picture, new Rectangle(Point.Empty, head.Size));
                    KitControl.TextDrawn = null;
                    var icon = drawn.Single(d => d.Text == Glyphs.Clock);
                    var size = TextRenderer.MeasureText(icon.Text, icon.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    string what = $"{(part ? "part" : "page")} at {scale}: {size} in {icon.Box.Size}";
                    Assert.IsLessThanOrEqualTo(icon.Box.Width, size.Width, what);
                    Assert.IsLessThanOrEqualTo(icon.Box.Height, size.Height, what);
                    Assert.IsLessThanOrEqualTo(head.Height, icon.Box.Bottom, what);
                    var title = drawn.Single(d => d.Text == "Breaks");
                    Assert.IsGreaterThanOrEqualTo(icon.Box.Right, title.Box.Left, $"{what}: the title starts after the icon");
                }
            }
        }
        finally
        {
            KitControl.TextDrawn = null;
            Theme.UseForShots(false, 1f);
        }
    }

    // Many rows scroll inside the card, the scroll bar within its edge and clear of its round
    // corners, and Tab reaches them so the keys scroll them; Close stays the window's default. A few
    // rows that fit are no stop for Tab.
    [TestMethod]
    public void ManyWhyRowsScrollInsideTheCardFromTheKeyboard()
    {
        var host = new Host { Lines = Enumerable.Range(1, 80).Select(i => new WhyLine(Signal.App, Effect.Extra(i), "app" + i)).ToList() };
        using var d = new WhyDialog(host, () => true, Lang.Current);
        var rows = d.Scroller;
        // Made as a shown window's rows are, without showing the window, so they can scroll.
        typeof(Control).GetMethod("CreateControl", Hidden, [typeof(bool)])!.Invoke(rows, [true]);
        Assert.IsTrue(rows.TabStop, "Tab reaches the rows while they scroll");
        Assert.IsFalse(string.IsNullOrEmpty(rows.AccessibleName), "and a screen reader can name them");
        var inside = Rectangle.Inflate(d.List.ClientRectangle, -d.LogicalToDeviceUnits(1), -d.LogicalToDeviceUnits(6));
        Assert.IsTrue(inside.Contains(rows.Bounds), $"the rows and their scroll bar sit inside the card's edge, clear of its corners ({rows.Bounds} in {d.List.ClientRectangle})");
        Assert.IsGreaterThan(rows.ClientSize.Height, rows.DisplayRectangle.Height, "the rows are taller than the room they show in");
        Assert.AreEqual("close", (d.ActiveControl as Control)?.Name, "Close has the keys first");
        Assert.AreSame(d.AcceptButton, d.CancelButton);

        Press(rows, Keys.Down);
        int down = -rows.AutoScrollPosition.Y;
        Assert.IsGreaterThan(0, down, "Down scrolls a little");
        Press(rows, Keys.PageDown);
        Assert.IsGreaterThan(down, -rows.AutoScrollPosition.Y, "Page Down a page");
        Press(rows, Keys.End);
        Assert.AreEqual(rows.DisplayRectangle.Height - rows.ClientSize.Height, -rows.AutoScrollPosition.Y, "End to the last row");
        Press(rows, Keys.Home);
        Assert.AreEqual(0, -rows.AutoScrollPosition.Y, "Home to the first");

        host.Lines = [new WhyLine(Signal.Battery, Effect.Sooner(50))];
        d.Tick();
        Assert.IsFalse(rows.TabStop, "a row that fits is no stop for Tab");
    }

    [TestMethod]
    public void TheWhyWindowStopsAskingWhenItCloses()
    {
        var host = new Host();
        var d = new WhyDialog(host, () => true, Lang.Current);
        Assert.IsFalse(d.Ticking, "not before it shows");
        typeof(Form).GetMethod("OnShown", Hidden)!.Invoke(d, [EventArgs.Empty]);
        Assert.IsTrue(d.Ticking);
        d.Dispose();
        Assert.IsFalse(d.Ticking);
        Assert.AreSame(d.CancelButton, d.AcceptButton);
    }
}
