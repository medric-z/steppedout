using System.Reflection;

namespace SteppedOut.Tests;

// The Locking tab without a window: each change makes the right edit to the settings, values from
// the file show as themselves, and showing the settings again edits nothing. A stand-in for the
// settings window applies each edit to its own copy and shows the result again, as the window does.
// One at a time: the controls fill the shared font cache, which is not made for several threads.
[TestClass]
[DoNotParallelize]
public class LockingTabTests
{
    static readonly Strings En = Lang.For("en");

    // A PC with a battery unless a test says otherwise, so the tests run the same on any PC.
    [TestInitialize]
    public void Start() => WelcomeForm.HasBattery = () => true;

    [TestCleanup]
    public void End() => WelcomeForm.HasBattery = Native.HasBattery;

    // The app as the page sees it: the shortcut and Why rows ask nothing of it in these tests.
    sealed class NoApp : ISettingsHost
    {
        public HotKeyResult ShortcutResult => HotKeyResult.Ok;
#pragma warning disable CS0067
        public event Action? ShortcutResultChanged;
        public event Action? StatusChanged;
        public event Action? StartWithWindowsChanged;
#pragma warning restore CS0067
        public LockStatus Status => new() { Kind = LockState.On, Time = LockTime.Plain(15), SecondsLeft = 600 };
        public bool StartsWithWindows => false;
        public AutostartState StartWithWindowsState => AutostartState.Off;
        public IReadOnlyList<WhyLine> Explain() => [];
        public void PauseChosen(TimeSpan? length) => Assert.Fail("no pause");
        public void ResumeChosen() => Assert.Fail("no turn back on");
        public void LockNowChosen() => Assert.Fail("no lock");
        public void TryItChosen() => Assert.Fail("no preview");
        public void ShortcutChanged(string shortcut) => Assert.Fail("no shortcut");
        public void LanguageChanged(string language) => Assert.Fail("no language change");
        public void ConfigEdited(Config config) => Assert.Fail("pages hand their edits to the window");
        public void StartWithWindowsChosen(bool on) => Assert.Fail("no start with Windows");
    }

    sealed class Host
    {
        public LockingTab Tab { get; } = new(new NoApp());
        public Control Page { get; }
        public Config Config { get; set; }
        public int Edits { get; set; }
        public Func<Config, Config>? Last { get; private set; }

        public Host(Config config)
        {
            Config = config.Copy();
            Page = Tab.Build(config.Copy(), En);
            Tab.Edited += edit =>
            {
                Edits++;
                Last = edit;
                Config = edit(Config.Copy());
                Tab.Refill(Config);
            };
        }

        public DropDown Battery => Tab.Battery!;

        public AppRulesList Rules => Tab.Rules!;
    }

    static void Pick(DropDown list, int row)
    {
        using var menu = list.BuildMenu();
        menu.Items.OfType<ToolStripMenuItem>().ElementAt(row).PerformClick();
    }

    static void Press(Control control, Keys key) =>
        typeof(Control).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(control, [new KeyEventArgs(key)]);

    static void Type(DropDown list, int customRow, int number)
    {
        CustomBox? box = null;
        list.Presenter = b => box = b;
        list.OpenCustom(customRow);
        Assert.IsNotNull(box, "the Custom… row opens its box");
        box.Field.Controls.OfType<TextBox>().Single().Text = number.ToString();
        Assert.IsTrue(box.Key(Keys.Enter));
    }

    static IEnumerable<string> Titles(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is SettingRow row) yield return row.Title;
            if (child is LockingTab.Heading head) yield return head.Title;
            foreach (string deeper in Titles(child)) yield return deeper;
        }
    }

    static Config Rules(params AppRule[] rules) => new() { IdleMinutes = 30, AppRules = rules.ToList() };

    [TestMethod]
    public void TheRowsComeInTheOrderDecided()
    {
        var host = new Host(new Config());
        var v = En.V1;
        CollectionAssert.AreEqual(new[] { v.LockingTab, v.OnBattery, v.AtLockTime, v.CountController, v.AppRules, v.Shortcut, v.WhyNotLocking },
            Titles(host.Page).ToArray());
    }

    // On a PC without a battery its row is left out, the line above it starts the next row, and the
    // setting stays as the file has it through every change on the page and every refill.
    [TestMethod]
    public void WithoutABatteryItsRowIsLeftOutAndItsSettingKept()
    {
        WelcomeForm.HasBattery = () => false;
        var v = En.V1;
        var host = new Host(new Config { OnBattery = Effect.Sooner(50) });
        CollectionAssert.AreEqual(new[] { v.LockingTab, v.AtLockTime, v.CountController, v.AppRules, v.Shortcut, v.WhyNotLocking },
            Titles(host.Page).ToArray());
        Assert.IsNull(host.Tab.Battery);
        var panel = (FlatPanel)host.Page;
        var away = panel.Controls.OfType<SettingRow>().Single(r => r.Title == v.AtLockTime);
        Assert.IsTrue(panel.HasLineAbove(away), "the line under the page head stays");
        Pick(host.Tab.AtLockTime!, 3);
        host.Tab.CountController!.Checked = false;
        Assert.AreEqual(2, host.Edits);
        Assert.AreEqual(Effect.Sooner(50), host.Config.OnBattery);
        host.Tab.Refill(new Config { OnBattery = Effect.Cap(45) });
        Assert.IsNull(host.Tab.Battery, "a refill does not bring the row back");
        Assert.AreEqual(2, host.Edits);
    }

    [TestMethod]
    public void WithABatteryItsRowShows()
    {
        var host = new Host(new Config { OnBattery = Effect.Sooner(50) });
        Assert.AreEqual(Effect.Sooner(50), host.Battery.Value);
        var panel = (FlatPanel)host.Page;
        var rows = panel.Controls.OfType<SettingRow>().ToList();
        Assert.IsTrue(panel.HasLineAbove(rows.Single(r => r.Title == En.V1.OnBattery)));
        Assert.IsFalse(panel.HasLineAbove(rows.Single(r => r.Title == En.V1.AtLockTime)));
    }

    // The row is only hidden: the battery's setting still makes its rule, which applies whenever
    // Windows says the PC runs on battery, so a battery that shows up later is followed.
    [TestMethod]
    public void TheHiddenSettingStillApplies()
    {
        WelcomeForm.HasBattery = () => false;
        var config = new Config { OnBattery = Effect.Sooner(50) };
        _ = new Host(config);
        var rule = global::SteppedOut.Rules.Build(config).OfType<BatteryRule>().Single();
        var lines = new List<WhyLine>();
        rule.Check(new FakeWorld { Power = PowerLineStatus.Offline }.Look(TimeSpan.FromSeconds(1)), lines);
        CollectionAssert.AreEqual(new[] { new WhyLine(Signal.Battery, Effect.Sooner(50)) }, lines);
    }

    [TestMethod]
    public void ANewUserSeesNoRulesAndLocksAsUsualOnBattery()
    {
        var host = new Host(new Config());
        Assert.AreEqual(Effect.None, host.Battery.Value);
        Assert.AreEqual(En.V1.LockAsUsual, host.Battery.Text);
        Assert.IsEmpty(host.Rules.Rows);
        Assert.AreEqual(WhenLocking.Lock, host.Tab.AtLockTime!.Value);
        Assert.IsTrue(host.Tab.CountController!.Checked);
        Assert.AreEqual(0, host.Edits);
    }

    [TestMethod]
    public void TheBatteryListMakesItsEdit()
    {
        var host = new Host(new Config { IdleMinutes = 30 });
        Press(host.Battery, Keys.Down);
        Assert.AreEqual(1, host.Edits);
        Assert.AreEqual(Effect.Sooner(25), host.Config.OnBattery);
        Assert.AreEqual(En.SoonerLabel(25) + " " + En.PercentNote(23), host.Battery.Text, "30 min 25% sooner is 22.5, rounded up");
    }

    [TestMethod]
    public void CustomOnTheBatteryWith45GivesAlways45()
    {
        var host = new Host(new Config { IdleMinutes = 30 });
        Type(host.Battery, 0, 45);
        Assert.AreEqual(1, host.Edits);
        Assert.AreEqual("always 45", host.Config.OnBattery.ToString());
        Assert.AreEqual(En.AlwaysAfterLabel(45), host.Battery.Text);
    }

    [TestMethod]
    public void AHandEditedAlways45ShowsAsItselfUnderFixedTime()
    {
        var host = new Host(new Config { IdleMinutes = 30, OnBattery = Effect.Cap(45) });
        Assert.AreEqual(Effect.Cap(45), host.Battery.Value);
        Assert.AreEqual(En.AlwaysAfterLabel(45), host.Battery.Text);
        var rows = host.Battery.Rows.ToList();
        int at = rows.IndexOf(En.AlwaysAfterLabel(45));
        Assert.IsGreaterThan(rows.IndexOf(En.V1.FixedTimeHeading), at, "under Fixed time");
        Assert.AreEqual(En.V1.Custom, rows[at + 1], "just above Custom…, where a typed time goes");
        Assert.AreEqual(0, host.Edits);

        // The same from a refill while the page is open, and a sooner the list doesn't have.
        host.Tab.Refill(new Config { IdleMinutes = 30, OnBattery = Effect.Sooner(40) });
        Assert.AreEqual(Effect.Sooner(40), host.Battery.Value);
        Assert.AreEqual(En.SoonerLabel(40) + " " + En.PercentNote(18), host.Battery.Text);
        host.Tab.Refill(new Config { IdleMinutes = 30, OnBattery = Effect.Cap(45) });
        Assert.AreEqual(En.AlwaysAfterLabel(45), host.Battery.Text);
        Assert.AreEqual(0, host.Edits);
    }

    [TestMethod]
    public void ThePercentNotesFollowTheLockTime()
    {
        var host = new Host(Set(Rules(new AppRule("notepad", Effect.Sooner(50))), c => c.OnBattery = Effect.Sooner(50)));
        var app = host.Rules.Rows[0].RuleList;
        Assert.AreEqual(En.SoonerLabel(50) + " " + En.PercentNote(15), host.Battery.Text);
        Assert.AreEqual(En.SoonerLabel(50) + " " + En.PercentNote(15), app.Text);
        var longer = host.Config.Copy();
        longer.IdleMinutes = 60;
        host.Tab.Refill(longer);
        Assert.AreEqual(En.SoonerLabel(50) + " " + En.PercentNote(30), host.Battery.Text);
        Assert.AreEqual(En.SoonerLabel(50) + " " + En.PercentNote(30), app.Text);
        Assert.Contains(En.SoonerLabel(75) + " " + En.PercentNote(15), host.Battery.Rows, "the choices not picked follow too");
        Assert.AreEqual(0, host.Edits);
    }

    static Config Set(Config c, Action<Config> change)
    {
        change(c);
        return c;
    }

    [TestMethod]
    public void AddingAnAppGivesItANeverLockRule()
    {
        var host = new Host(new Config());
        IEnumerable<string>? offered = null;
        host.Rules.Picker = (_, already, picked) =>
        {
            offered = already.ToList();
            picked(new PickedApp("notepad", @"C:\Windows\notepad.exe", null));
        };
        host.Rules.AddButton.PerformClick();
        Assert.AreEqual(1, host.Edits);
        Assert.HasCount(1, host.Config.AppRules);
        Assert.AreEqual(new AppRule("notepad", Effect.Never, AppWhen.Running, @"C:\Windows\notepad.exe", null), host.Config.AppRules[0]);
        Assert.HasCount(1, host.Rules.Rows);
        Assert.IsEmpty(offered!);

        // Adding it again from the files does nothing; the picker is told which apps are listed.
        host.Rules.Picker = (_, already, picked) =>
        {
            offered = already.ToList();
            picked(new PickedApp("NOTEPAD.EXE", @"D:\other\notepad.exe", null));
        };
        host.Rules.AddButton.PerformClick();
        CollectionAssert.AreEqual(new[] { "notepad" }, offered!.ToArray());
        Assert.AreEqual(1, host.Edits);
        Assert.HasCount(1, host.Config.AppRules);

        // A second app goes after the first, and survives Clamp: no rule ever does nothing.
        host.Rules.Add(new PickedApp("mspaint", null, null));
        Assert.AreEqual(2, host.Edits);
        CollectionAssert.AreEqual(new[] { "notepad", "mspaint" }, host.Config.AppRules.Select(r => r.App).ToArray());
        var clamped = host.Config.Copy();
        clamped.Clamp();
        Assert.HasCount(2, clamped.AppRules);
    }

    [TestMethod]
    public void ChangingARuleMakesItsEdit()
    {
        var host = new Host(Rules(new AppRule("notepad", Effect.Never), new AppRule("mspaint", Effect.Never)));
        var row = host.Rules.Rows[1];
        Press(row.RuleList, Keys.Down);
        Assert.AreEqual(1, host.Edits);
        Assert.AreEqual(Effect.Extra(15), host.Config.AppRules[1].Effect);
        Assert.AreEqual(Effect.Never, host.Config.AppRules[0].Effect, "the other rule stays");

        Press(row.WhenList, Keys.Down);
        Assert.AreEqual(2, host.Edits);
        Assert.AreEqual(AppWhen.OnScreen, host.Config.AppRules[1].When);
        Assert.AreEqual(En.V1.OnlyWhenOnScreen, row.WhenList.Text);
        Assert.AreEqual(En.V1.OnScreenExplained, row.WhenList.AccessibleDescription, "a screen reader hears what on screen means");

        // The edit lands on the rules the settings hold then, whatever else changed meanwhile.
        var other = host.Config.Copy();
        other.AppRules = [.. other.AppRules, new AppRule("calc", Effect.Cap(5))];
        var after = host.Last!(other);
        Assert.HasCount(3, after.AppRules);
        Assert.AreEqual(AppWhen.OnScreen, after.AppRules[1].When);
    }

    [TestMethod]
    public void CustomUnderWaitLongerWith90GivesLater90()
    {
        var host = new Host(Rules(new AppRule("notepad", Effect.Never)));
        var list = host.Rules.Rows[0].RuleList;
        Type(list, 0, 90);
        Assert.AreEqual(1, host.Edits);
        Assert.AreEqual("later 90", host.Config.AppRules[0].Effect.ToString());
        Assert.AreEqual(En.ExtraLabel(90), host.Rules.Rows[0].RuleList.Text);
        var rows = host.Rules.Rows[0].RuleList.Rows.ToList();
        int at = rows.IndexOf(En.ExtraLabel(90));
        Assert.IsGreaterThan(rows.IndexOf(En.V1.WaitLongerHeading), at);
        Assert.IsLessThan(rows.IndexOf(En.V1.LockSoonerHeading), at, "under Wait longer");

        // Custom… under Fixed time is the second.
        Type(host.Rules.Rows[0].RuleList, 1, 45);
        Assert.AreEqual("always 45", host.Config.AppRules[0].Effect.ToString());
    }

    [TestMethod]
    public void AnAppRuleMenuAlwaysHasARealChoice()
    {
        var host = new Host(Rules(new AppRule("notepad", Effect.Never)));
        var list = host.Rules.Rows[0].RuleList;
        Assert.IsFalse(list.Rows.Contains(En.V1.LockAsUsual));
        Assert.IsFalse(list.Rows.Contains(En.V1.NoExtraTime));
        foreach (var effect in (List<Effect>)list.Tag!) Assert.AreNotEqual(EffectKind.None, effect.Kind);
        Assert.AreEqual(En.V1.NeverLock, list.Rows[0]);
        CollectionAssert.AreEqual(new[] { En.V1.NeverLock, En.V1.WaitLongerHeading, En.V1.LockSoonerHeading, En.V1.FixedTimeHeading },
            list.Rows.Where(r => r == En.V1.NeverLock || r == En.V1.WaitLongerHeading || r == En.V1.LockSoonerHeading || r == En.V1.FixedTimeHeading).ToArray());
        // Wait longer offers the same times as the extra time for music, full screen and calls.
        CollectionAssert.AreEqual(TimeChoices.Extras.Select(Effect.Extra).ToArray(), ((List<Effect>)list.Tag!).Where(e => e.Kind == EffectKind.Extra).ToArray());
    }

    [TestMethod]
    public void HandEditedAppRulesShowAsThemselves()
    {
        var host = new Host(Rules(new AppRule("notepad", Effect.Extra(90)), new AppRule("mspaint", Effect.Sooner(40), AppWhen.OnScreen)));
        Assert.AreEqual(Effect.Extra(90), host.Rules.Rows[0].RuleList.Value);
        Assert.AreEqual(En.ExtraLabel(90), host.Rules.Rows[0].RuleList.Text);
        Assert.AreEqual(Effect.Sooner(40), host.Rules.Rows[1].RuleList.Value);
        Assert.AreEqual(En.SoonerLabel(40) + " " + En.PercentNote(18), host.Rules.Rows[1].RuleList.Text);
        Assert.AreEqual(En.V1.OnlyWhenOnScreen, host.Rules.Rows[1].WhenList.Text);
        Assert.AreEqual(0, host.Edits);
    }

    [TestMethod]
    public void RemovingARuleMakesItsEdit()
    {
        var host = new Host(Rules(new AppRule("notepad", Effect.Never), new AppRule("mspaint", Effect.Cap(5))));
        ((RuleRemove)host.Rules.Rows[0].RemoveButton).Press();
        Assert.AreEqual(1, host.Edits);
        CollectionAssert.AreEqual(new[] { "mspaint" }, host.Config.AppRules.Select(r => r.App).ToArray());
        Assert.HasCount(1, host.Rules.Rows);
        ((RuleRemove)host.Rules.Rows[0].RemoveButton).Press();
        Assert.AreEqual(2, host.Edits);
        Assert.IsEmpty(host.Config.AppRules);
        Assert.IsEmpty(host.Rules.Rows);
    }

    [TestMethod]
    public void TheSwitchesMakeTheirEdits()
    {
        var host = new Host(new Config());
        Pick(host.Tab.AtLockTime!, 3);
        Assert.AreEqual(1, host.Edits);
        Assert.IsFalse(host.Config.LockWhenAway);
        Assert.AreEqual(WhenLocking.Lock, host.Config.WhenLocking);
        host.Tab.CountController!.Checked = false;
        Assert.AreEqual(2, host.Edits);
        Assert.IsFalse(host.Config.CountController);
    }

    // The edit counter: showing any settings again, however different, raises no edit.
    [TestMethod]
    public void RefillRaisesZeroEdits()
    {
        var host = new Host(new Config());
        var configs = new[]
        {
            new Config { LockWhenAway = false, CountController = false, OnBattery = Effect.Sooner(75), IdleMinutes = 45 },
            Rules(new AppRule("notepad", Effect.Never), new AppRule("mspaint", Effect.Extra(120), AppWhen.OnScreen)),
            Set(Rules(new AppRule("mspaint", Effect.Cap(45)), new AppRule("notepad", Effect.Sooner(40))), c => c.OnBattery = Effect.Cap(45)),
            Set(Rules(new AppRule("notepad", Effect.Extra(90))), c => c.OnBattery = Effect.Sooner(40)),
            new Config(),
        };
        foreach (var config in configs)
        {
            host.Tab.Refill(config);
            Assert.AreEqual(config.LockWhenAway, !ReferenceEquals(host.Tab.AtLockTime!.Value, LockingTab.DontLock));
            Assert.AreEqual(config.CountController, host.Tab.CountController!.Checked);
            Assert.AreEqual(config.OnBattery, host.Battery.Value);
            CollectionAssert.AreEqual(config.AppRules.Select(r => r.Effect).ToArray(), host.Rules.Rows.Select(r => (Effect)r.RuleList.Value!).ToArray());
            CollectionAssert.AreEqual(config.AppRules.Select(r => r.When).ToArray(), host.Rules.Rows.Select(r => (AppWhen)r.WhenList.Value!).ToArray());
        }
        Assert.AreEqual(0, host.Edits);
    }

    [TestMethod]
    public void BuildingAgainForAnotherLanguageShowsTheSameSettings()
    {
        var config = Set(Rules(new AppRule("notepad", Effect.Sooner(50), AppWhen.OnScreen)), c => c.OnBattery = Effect.Cap(45));
        var tab = new LockingTab(new NoApp());
        int edits = 0;
        tab.Edited += _ => edits++;
        tab.Build(config, En);
        var ar = Lang.For("ar");
        tab.Build(config, ar);
        Assert.AreEqual(ar.AlwaysAfterLabel(45), tab.Battery!.Text);
        Assert.AreEqual(ar.SoonerLabel(50) + " " + ar.PercentNote(15), tab.Rules!.Rows[0].RuleList.Text);
        Assert.AreEqual(ar.V1.OnlyWhenOnScreen, tab.Rules.Rows[0].WhenList.Text);
        Assert.AreEqual(0, edits);
    }

    // The page at the settings window's width, laid out.
    static Host Wide(Config config, int width = 740)
    {
        var host = new Host(config);
        host.Page.Width = width;
        host.Page.Height = host.Page.GetPreferredSize(new Size(width, 0)).Height;
        host.Page.PerformLayout();
        foreach (var row in host.Rules.Rows) row.PerformLayout();
        return host;
    }

    [TestMethod]
    public void ThePageHeadIsASemiboldHeadingWithItsIconLevelWithTheName()
    {
        var host = Wide(new Config());
        var head = host.Tab.Head!;
        Assert.AreEqual(En.V1.LockingTab, head.Title);
        Assert.AreEqual(En.V1.LockingIntro, head.Detail);
        StringAssert.Contains(head.TitleFont.Name, "Semibold");
        Assert.AreEqual(2, head.AutomationProvider!.HeadingLevel, "screen readers hear a heading");
        Assert.IsFalse(head.CanSelect, "Tab passes the head by");
        int line = Shapes.Measure(head.Title, head.TitleFont).Height;
        int glyphMiddle = head.GlyphBox.Top + head.GlyphBox.Height / 2;
        int nameMiddle = head.TitleBox.Top + line / 2;
        Assert.IsLessThanOrEqualTo(1, Math.Abs(glyphMiddle - nameMiddle), "the icon is level with the name's first line");
        Assert.IsLessThan(head.TitleBox.Left, head.GlyphBox.Left, "the icon comes before the name");
    }

    [TestMethod]
    public void ThePageHeadsIconIsNeverCut()
    {
        var host = Wide(new Config());
        var head = host.Tab.Head!;
        var icon = TextRenderer.MeasureText(head.Glyph, head.GlyphFont, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        var box = head.GlyphBox;
        Assert.IsGreaterThanOrEqualTo(icon.Height, box.Height, "the icon's box is as tall as the icon");
        Assert.IsGreaterThanOrEqualTo(icon.Width, box.Width, "and as wide");
        Assert.IsGreaterThanOrEqualTo(head.LogicalToDeviceUnits(20), box.Height, "and at least the sketch's 20 px");
        Assert.IsGreaterThanOrEqualTo(0, box.Top, "nothing of it above the head");
        Assert.IsLessThanOrEqualTo(head.Height, box.Bottom, "or below it");
        Assert.IsLessThanOrEqualTo(head.GetPreferredSize(new Size(head.Width, 0)).Height, box.Bottom, "the head asks for room for the whole icon");
    }

    [TestMethod]
    public void ShortAppNamesKeepTheSketchsColumn()
    {
        var host = Wide(Rules(new AppRule("notepad", Effect.Never, AppWhen.Running), new AppRule("mspaint", Effect.Extra(60), AppWhen.OnScreen)));
        var row = host.Rules.Rows[0];
        Assert.AreEqual(row.LogicalToDeviceUnits(128), host.Rules.Grid(row, row.Width).NameW);
        Assert.IsTrue(host.Rules.Rows.All(r => !r.NameCut && r.NameTip.Length == 0));
    }

    [TestMethod]
    public void ALongAppNameGetsTheRoomItNeedsAndAWholeNameOnHover()
    {
        const string player = "Windows Media Player";
        const string fax = "Windows Fax and Scan";
        const string viewer = "Windows PowerShell Integrated Scripting Environment";
        var host = Wide(Rules(new AppRule(player, Effect.Never, AppWhen.Running), new AppRule(fax, Effect.Extra(30), AppWhen.OnScreen),
            new AppRule(viewer, Effect.Sooner(50), AppWhen.Running)));
        var rows = host.Rules.Rows;
        var c = host.Rules.Grid(rows[0], rows[0].Width);
        Assert.IsGreaterThan(rows[0].LogicalToDeviceUnits(128), c.NameW, "the name column grows past the sketch's 128 px");
        Assert.IsLessThanOrEqualTo(rows[0].Width / 3, c.NameW, "up to a third of the line");
        Assert.IsFalse(rows[0].NameCut, player + " shows whole");
        Assert.IsFalse(rows[1].NameCut, fax + " shows whole");
        Assert.AreEqual("", rows[0].NameTip);
        Assert.IsTrue(rows[2].NameCut, "a name longer than a third of the line is still cut");
        Assert.AreEqual(viewer, rows[2].NameTip, "and shows whole when the pointer rests on it");
        foreach (var row in rows)
        {
            Assert.IsGreaterThanOrEqualTo(row.RuleNeed, row.RuleList.Width, "the rule list keeps room for its words");
            Assert.IsGreaterThanOrEqualTo(row.WhenNeed, row.WhenList.Width, "so does Applies when");
        }

        // The long one goes: the column shrinks back to what the others need, and no hint is left.
        var keep = host.Config.AppRules.Take(2).ToList();
        host.Tab.Refill(Set(host.Config, x => x.AppRules = keep));
        foreach (var row in host.Rules.Rows) row.PerformLayout();
        Assert.HasCount(2, host.Rules.Rows);
        Assert.IsTrue(host.Rules.Rows.All(r => !r.NameCut && r.NameTip.Length == 0));
    }

    [TestMethod]
    public void TheListsKeepTheirWordsBeforeANameGrows()
    {
        var probe = new Control();
        int width = 600;
        int pad = 9, gap = 8, remove = 28;
        int ruleNeed = 200, whenNeed = 180;
        var c = AppRulesList.Columns(probe, width, ruleNeed, whenNeed, nameNeed: 400);
        int spare = width - 2 * pad - 128 - remove - 3 * gap - ruleNeed - whenNeed;
        Assert.AreEqual(128 + spare, c.NameW, "the name takes only the room the lists can spare");
        Assert.IsGreaterThanOrEqualTo(ruleNeed, c.RuleW);
        Assert.IsGreaterThanOrEqualTo(whenNeed, c.WhenW);
        var tight = AppRulesList.Columns(probe, width, 260, 230, nameNeed: 400);
        Assert.AreEqual(72, tight.NameW, "with the lists short of room the name gives up room, down to 72 px");
        Assert.AreEqual(width - 2 * pad - 72 - remove - 3 * gap, tight.RuleW + tight.WhenW, "and the lists get it");
        var some = AppRulesList.Columns(probe, width, 230, 200, nameNeed: 400);
        Assert.AreEqual(128 - (230 + 200 - (width - 2 * pad - 128 - remove - 3 * gap)), some.NameW, "only as much as the lists are short");
        Assert.AreEqual(230, some.RuleW);
        Assert.AreEqual(200, some.WhenW);
    }

    [TestMethod]
    public void AtLargeTextTheListsKeepTheirWordsAndTheNameGivesWay()
    {
        try
        {
            foreach (float scale in new[] { 1f, 1.2f, 1.5f })
            {
                Theme.UseForShots(false, scale);
                var host = Wide(Rules(new AppRule("Windows Media Player", Effect.Never, AppWhen.Running),
                    new AppRule("Remote Desktop Connection", Effect.Extra(60), AppWhen.OnScreen), new AppRule("Windows Fax and Scan", Effect.Sooner(50), AppWhen.Running),
                    new AppRule("Windows PowerShell Integrated Scripting Environment", Effect.Cap(5), AppWhen.OnScreen)));
                foreach (var row in host.Rules.Rows)
                {
                    foreach (var list in new[] { row.RuleList, row.WhenList })
                    {
                        var want = list.GetPreferredSize(Size.Empty);
                        Assert.IsLessThanOrEqualTo(list.Width, want.Width, $"at text {scale}, \"{list.Text}\" for {row.AppName} has the room it asks for");
                    }
                }
                var c = host.Rules.Grid(host.Rules.Rows[0], host.Rules.Rows[0].Width);
                Assert.IsGreaterThanOrEqualTo(host.Rules.Rows[0].LogicalToDeviceUnits(72), c.NameW, $"at text {scale} the name column keeps 72 px");
            }
        }
        finally
        {
            Theme.UseForShots(false, 1f);
        }
    }

    // A program file on a network share or a sleeping drive is read away from the window: the list
    // shows at once with the name the rule matches on, and the program's own name when it comes.
    [TestMethod]
    public void AProgramSlowToReadNeverHoldsTheListUp()
    {
        const string sample = @"C:\Program Files\Sample Studio\samplestudio.exe";
        using var gate = new ManualResetEventSlim();
        var list = new AppRulesList(En, _ => { }) { Names = new ProgramNames(_ => gate.Wait(TimeSpan.FromSeconds(10)) ? "Sample Studio" : null) };
        list.Width = 740;
        list.CreateControl();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        list.Show([new AppRule("samplestudio", Effect.Never, AppWhen.Running, sample)], 30);
        clock.Stop();
        Assert.IsLessThan(2000, clock.ElapsedMilliseconds, "the list shows without waiting for the file");
        var row = list.Rows.Single();
        Assert.AreEqual("samplestudio", row.AppName, "by the name the rule matches on meanwhile");
        gate.Set();
        var until = DateTime.UtcNow.AddSeconds(10);
        while (row.AppName != "Sample Studio" && DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Assert.AreEqual("Sample Studio", row.AppName, "and by its own name once the file is read");
        Assert.AreEqual("Sample Studio", row.AccessibleName);
        Assert.AreEqual(string.Format(En.V1.RemoveApp, "Sample Studio"), row.RemoveButton.AccessibleName);
        list.Dispose();
    }

    // A line draws its program's icon read away from the window too: the apps glyph at once, and the
    // program's own icon when it comes, read on another thread.
    [TestMethod]
    public void AProgramIconSlowToReadNeverHoldsTheLineUp()
    {
        string sample = Path.Combine(Path.GetTempPath(), "Sample Studio", "samplestudio.exe");
        using var gate = new ManualResetEventSlim();
        int window = Environment.CurrentManagedThreadId;
        int readOn = window;
        var list = new AppRulesList(En, _ => { })
        {
            Names = new ProgramNames(_ => null),
            Icons = new ProgramIcons((_, size) =>
            {
                readOn = Environment.CurrentManagedThreadId;
                if (!gate.Wait(TimeSpan.FromSeconds(10))) return null;
                var image = new Bitmap(size, size);
                using (var g = Graphics.FromImage(image)) g.Clear(Color.Red);
                return image;
            }),
        };
        list.Width = 740;
        list.CreateControl();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        list.Show([new AppRule("samplestudio", Effect.Never, AppWhen.Running, sample)], 30);
        var row = list.Rows.Single();
        row.PerformLayout();
        using (var drawn = new Bitmap(Math.Max(1, row.Width), Math.Max(1, row.Height)))
            row.DrawToBitmap(drawn, new Rectangle(Point.Empty, drawn.Size));
        clock.Stop();
        Assert.IsLessThan(2000, clock.ElapsedMilliseconds, "the line shows and draws without waiting for the file");
        Assert.IsFalse(row.OwnIcon, "the glyph meanwhile");
        Assert.IsNotNull(row.IconShown);

        gate.Set();
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!row.OwnIcon && DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Assert.IsTrue(row.OwnIcon, "the program's own icon once read");
        Assert.AreNotEqual(window, readOn, "read on another thread");
        Assert.AreEqual(Color.Red.ToArgb(), row.IconShown!.GetPixel(1, 1).ToArgb());
        list.Dispose();
    }

    // Only a program file on this PC is read for its icon; one on another PC keeps the glyph.
    [TestMethod]
    public void AProgramIconOnAnotherPCIsNotRead()
    {
        int reads = 0;
        var list = new AppRulesList(En, _ => { })
        {
            Names = new ProgramNames(_ => null),
            Icons = new ProgramIcons((_, size) =>
            {
                Interlocked.Increment(ref reads);
                return new Bitmap(size, size);
            }),
        };
        list.Width = 740;
        list.CreateControl();
        list.Show([new AppRule("remote", Effect.Never, AppWhen.Running, @"\\192.0.2.10\share\remote.exe")], 30);
        var row = list.Rows.Single();
        row.PerformLayout();
        using (var drawn = new Bitmap(Math.Max(1, row.Width), Math.Max(1, row.Height)))
            row.DrawToBitmap(drawn, new Rectangle(Point.Empty, drawn.Size));
        Application.DoEvents();
        Assert.AreEqual(0, reads);
        Assert.IsFalse(row.OwnIcon);
        Assert.IsNotNull(row.IconShown, "the glyph");
        list.Dispose();
    }

    [TestMethod]
    public void ALongListMeasuresEachLineOnce()
    {
        int Measures(int count)
        {
            var rules = Enumerable.Range(0, count).Select(i => new AppRule($"app{i}", i % 2 == 0 ? Effect.Never : Effect.Extra(30), AppWhen.Running)).ToArray();
            var host = Wide(Rules(rules));
            host.Page.PerformLayout();
            foreach (var row in host.Rules.Rows)
            {
                row.PerformLayout();
                using var picture = new Bitmap(Math.Max(1, row.Width), Math.Max(1, row.Height));
                row.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
            }
            return host.Rules.Measures;
        }

        int ten = Measures(10);
        int fifty = Measures(50);
        Assert.IsLessThanOrEqualTo(3 * 10 * 2, ten, "each line measures its name and two lists, again only when they change");
        Assert.IsLessThanOrEqualTo(6 * ten, fifty, $"five times the lines, about five times the measuring ({ten} for 10, {fifty} for 50)");
    }
}
