using System.Reflection;

namespace SteppedOut.Tests;

// The Away and back tab, built without a window: what each control asks the settings to become, the
// app lists and scripts, and that showing settings from outside never asks for a change. Apps come
// from a stand-in picker and scripts from a stand-in dialog, so nothing opens, plays, mutes, turns
// a screen off or closes anything. One at a time: the controls share the font cache.
[TestClass]
[DoNotParallelize]
public class AwayTabTests
{
    // Made-up apps, so no name here can ever match a real program.
    const string SampleExe = @"C:\Program Files\Sample Studio\samplestudio.exe";
    const string SampleAumid = "SampleCo.SampleMusic_abc123xyz!SampleMusic";

    sealed class Built
    {
        public required AwayTab Tab { get; init; }
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

    // Running apps as a test sets them: each with its name and program file.
    sealed class RunningApps(params (string Name, string Path)[] apps) : IProcessSource
    {
        sealed class App(int id, string name) : IRunningProcess
        {
            public int Id => id;
            public string Name => name;
            public IntPtr MainWindow => IntPtr.Zero;
            public string MainWindowTitle => "";
            public void Dispose()
            {
            }
        }

        public IEnumerable<IRunningProcess> Running() => apps.Select((app, i) => (IRunningProcess)new App(i + 1, app.Name));
        public bool IsHiddenOrTool(IntPtr window) => true;
        public string? ImagePath(int processId) => apps[processId - 1].Path;
        public string? Aumid(int processId) => null;
    }

    // Windows is never asked for a Store app's name here unless startMenu says otherwise: by default
    // it knows none of the made-up ids. No app is running unless running says so, and programs give
    // their own names only as names says.
    static Built Build(Config? config = null, Strings? text = null, Action<Control, IReadOnlyList<string>, Action<PickedApp>>? pick = null,
        Func<Control, string?>? script = null, Func<string, string?>? startMenu = null, IProcessSource? running = null, ProgramNames? names = null,
        Func<char, DriveType>? drives = null)
    {
        config ??= new Config();
        var tab = new AwayTab
        {
            PickApp = pick ?? ((_, _, _) => Assert.Fail("no app list should open")),
            ChooseScript = script ?? (_ => { Assert.Fail("no file dialog should open"); return null; }),
            StartMenuName = startMenu ?? (_ => null),
            Processes = running ?? new RunningApps(),
            Names = names ?? ProgramNames.Shared,
            Drives = drives ?? LocalPaths.DriveTypeOf,
        };
        var page = tab.Build(config, text ?? Lang.For("en"));
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

    // The rows in order, with the sketch's titles.
    static List<SettingRow> Rows(Control page) => All<SettingRow>(page).ToList();

    static T ControlOf<T>(SettingRow row) where T : Control => row.Controls.OfType<T>().Single(c => c != row.Below);

    static SettingRow Row(Control page, int index) => Rows(page)[index];

    // A row's apps: the list under it, which Close apps keeps under its warning.
    static ChipList Chips(SettingRow row) => row.Below switch
    {
        ChipList chips => chips,
        AwayTab.CloseList close => close.Chips,
        _ => throw new AssertFailedException("no apps under " + row.Title),
    };

    static void Press(Control control, Keys key) =>
        typeof(Control).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(control, [new KeyEventArgs(key)]);

    // Row places, in the sketch's order.
    const int MuteRow = 0, ScreenRow = 1, SoundAwayRow = 2, OpenAwayRow = 3, CloseAwayRow = 4,
        ShowTimeRow = 5, SoundBackRow = 6, OpenBackRow = 7, CloseBackRow = 8, ScriptAwayRow = 9, ScriptBackRow = 10;

    [TestMethod]
    public void TheRowsAreInTheSketchOrder()
    {
        var v = Lang.For("en").V1;
        var b = Build();
        CollectionAssert.AreEqual(new[]
        {
            v.Mute, v.ScreenOff, v.PlaySound, v.OpenApps, v.CloseApps,
            v.ShowTimeAway, v.PlaySound, v.OpenApps, v.CloseApps,
            v.RunScriptAway, v.RunScriptBack,
        }, Rows(b.Page).Select(r => r.Title).ToArray());
        var heads = All<PageHeading>(b.Page).Select(h => h.Title).ToArray();
        CollectionAssert.AreEqual(new[] { v.AwayTab, v.WhenAway, v.WhenBack, v.Scripts }, heads);
        Assert.AreEqual(v.AwayIntro, All<PageHeading>(b.Page).First().Detail);
        Assert.AreEqual(v.ScriptsDetail, All<PageHeading>(b.Page).Last().Detail, "the supported files line sits under Scripts");
        Assert.IsInstanceOfType<FlatPanel>(b.Page);
        var panel = (FlatPanel)b.Page;
        var lined = panel.Controls.Cast<Control>().Where(panel.HasLineAbove).OfType<PageHeading>().Select(h => h.Title).ToArray();
        CollectionAssert.AreEqual(new[] { v.WhenAway, v.WhenBack, v.Scripts }, lined, "a line above each part");
    }

    [TestMethod]
    public void TheDefaultsShowEverythingOffButTheTimeAwayNote()
    {
        var b = Build();
        Assert.IsFalse(ControlOf<Toggle>(Row(b.Page, MuteRow)).Checked);
        Assert.IsFalse(ControlOf<Toggle>(Row(b.Page, ScreenRow)).Checked);
        Assert.IsTrue(ControlOf<Toggle>(Row(b.Page, ShowTimeRow)).Checked);
        Assert.AreEqual(ActionSound.Off, ControlOf<DropDown>(Row(b.Page, SoundAwayRow)).Value);
        Assert.AreEqual(ActionSound.Off, ControlOf<DropDown>(Row(b.Page, SoundBackRow)).Value);
        foreach (int row in new[] { OpenAwayRow, CloseAwayRow, OpenBackRow, CloseBackRow })
        {
            var chips = Chips(Row(b.Page, row));
            Assert.IsEmpty(chips.Texts);
            Assert.AreEqual(Lang.For("en").V1.NoAppsAdded, chips.EmptyText);
        }
        Assert.AreEqual(Lang.For("en").V1.NoScriptChosen, Row(b.Page, ScriptAwayRow).Detail);
        Assert.AreEqual(Lang.For("en").V1.Choose, ControlOf<KitButton>(Row(b.Page, ScriptAwayRow)).Text);
        Assert.IsEmpty(b.Edits, "building raises nothing");
    }

    [TestMethod]
    public void EachSwitchEditsItsOwnKey()
    {
        var cases = new (int Row, Func<Config, bool> Read)[]
        {
            (MuteRow, c => c.MuteWhenAway),
            (ScreenRow, c => c.ScreensOffWhenAway),
            (ShowTimeRow, c => c.ShowTimeAway),
        };
        foreach (var (row, read) in cases)
        {
            var b = Build();
            var toggle = ControlOf<Toggle>(Row(b.Page, row));
            bool was = read(b.Config);
            toggle.Checked = !was;
            Assert.HasCount(1, b.Edits, $"row {row}: one edit");
            var after = b.Apply();
            Assert.AreEqual(!was, read(after), $"row {row}");
            // Nothing else changed: putting the key back gives the defaults again.
            Assert.IsTrue(Restore(after, row).SameAs(new Config()), $"row {row} changed only its key");
            toggle.Checked = was;
            Assert.AreEqual(was, read(b.Apply()));
        }
    }

    static Config Restore(Config c, int row)
    {
        var d = new Config();
        var copy = c.Copy();
        if (row == MuteRow) copy.MuteWhenAway = d.MuteWhenAway;
        if (row == ScreenRow) copy.ScreensOffWhenAway = d.ScreensOffWhenAway;
        if (row == ShowTimeRow) copy.ShowTimeAway = d.ShowTimeAway;
        return copy;
    }

    [TestMethod]
    public void ClickingTheRowFlipsItsSwitch()
    {
        var b = Build();
        var row = Row(b.Page, MuteRow);
        typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(row, [EventArgs.Empty]);
        Assert.IsTrue(b.Apply().MuteWhenAway);
    }

    [TestMethod]
    public void TheSoundListsEditTheirOwnKeys()
    {
        var b = Build();
        var v = Lang.For("en").V1;
        var away = ControlOf<DropDown>(Row(b.Page, SoundAwayRow));
        var back = ControlOf<DropDown>(Row(b.Page, SoundBackRow));
        string[] names = [v.Off, v.SoundSteppedOut, v.SoundDrift, v.SoundPebble, v.SoundGlow, v.SoundRipple];
        CollectionAssert.AreEqual(names, away.Choices.ToArray());
        CollectionAssert.AreEqual(names, back.Choices.ToArray());
        Assert.AreEqual(v.YourOwnSound, away.Rows[^1], "the list ends with Your own sound…");
        Assert.AreEqual(v.YourOwnSound, back.Rows[^1]);
        Press(away, Keys.Down);
        var c = b.Apply();
        Assert.AreEqual(ActionSound.SteppedOut, c.SoundWhenAway);
        Assert.AreEqual(ActionSound.Off, c.SoundWhenBack);
        Press(back, Keys.Down);
        Press(back, Keys.Down);
        c = b.Apply();
        Assert.AreEqual(ActionSound.SteppedOut, c.SoundWhenAway);
        Assert.AreEqual(ActionSound.Drift, c.SoundWhenBack);
        Press(away, Keys.Up);
        Assert.AreEqual(ActionSound.Off, b.Apply().SoundWhenAway);
    }

    [TestMethod]
    public void AddAppPutsAProgramPathOrAStoreIdInTheOpenLists()
    {
        var picks = new Queue<PickedApp>([
            new("samplestudio", SampleExe, null),
            new("SampleMusic", null, SampleAumid),
            new("samplestudio", SampleExe, null),
        ]);
        IReadOnlyList<string>? already = null;
        var b = Build(pick: (_, names, picked) =>
        {
            already = names;
            picked(picks.Dequeue());
        });
        var row = Row(b.Page, OpenAwayRow);
        var add = ControlOf<KitButton>(row);
        var chips = Chips(row);
        add.PerformClick();
        CollectionAssert.AreEqual(Array.Empty<string>(), already!.ToArray());
        CollectionAssert.AreEqual(new[] { SampleExe }, b.Apply().OpenWhenAway);
        CollectionAssert.AreEqual(new[] { "samplestudio" }, chips.Texts.ToArray(), "the chip shows at once, before any refill");
        add.PerformClick();
        CollectionAssert.AreEqual(new[] { "samplestudio" }, already!.ToArray(), "the picker leaves out what is listed");
        CollectionAssert.AreEqual(new[] { SampleExe, OpenApps.AppsFolder + SampleAumid }, b.Apply().OpenWhenAway);
        CollectionAssert.AreEqual(new[] { "samplestudio", "SampleMusic" }, chips.Texts.ToArray());
        add.PerformClick();
        Assert.IsEmpty(b.Edits, "the same app twice is not added again");
        Assert.IsEmpty(b.Config.OpenWhenBack, "only its own list");
    }

    [TestMethod]
    public void AnAppWithNoKnownProgramIsNotAddedToAnOpenList()
    {
        var b = Build(pick: (_, _, picked) => picked(new PickedApp("sampletool", null, null)));
        ControlOf<KitButton>(Row(b.Page, OpenBackRow)).PerformClick();
        Assert.IsEmpty(b.Edits);
        b = Build(pick: (_, _, picked) => picked(new PickedApp("sampletool", "sampletool.exe", null)));
        ControlOf<KitButton>(Row(b.Page, OpenBackRow)).PerformClick();
        Assert.IsEmpty(b.Edits, "a path that is not a full one would never open");
    }

    [TestMethod]
    public void AddAppPutsTheNameInTheCloseLists()
    {
        var b = Build(pick: (_, _, picked) => picked(new PickedApp("samplestudio", SampleExe, null)));
        ControlOf<KitButton>(Row(b.Page, CloseBackRow)).PerformClick();
        var c = b.Apply();
        CollectionAssert.AreEqual(new[] { "samplestudio" }, c.CloseWhenBack);
        Assert.IsEmpty(c.CloseWhenAway);
        b.Tab.Refill(c);
        ControlOf<KitButton>(Row(b.Page, CloseBackRow)).PerformClick();
        Assert.IsEmpty(b.Edits, "already listed");
        // Its own name, chosen from the files as SampleStudio.EXE, is the same app.
        var picked = new PickedApp("SampleStudio", @"D:\Other\SampleStudio.EXE", null);
        b = Build(c, pick: (_, _, p) => p(picked));
        ControlOf<KitButton>(Row(b.Page, CloseBackRow)).PerformClick();
        Assert.IsEmpty(b.Edits, "names are compared whatever the case");
    }

    // A chip shows an app by the name the program gives itself, as the app rules on the Locking tab
    // do: a program's file from its description, a Close apps name from the app running under it.
    [TestMethod]
    public void ChipsShowTheNameAProgramGivesItself()
    {
        var names = new ProgramNames(path => path.Equals(SampleExe, StringComparison.OrdinalIgnoreCase) ? "Sample Studio" : null);
        var running = new RunningApps(("samplestudio", SampleExe), ("other", @"C:\Tools\other.exe"));
        var config = new Config();
        config.OpenWhenAway.AddRange([SampleExe, @"C:\Tools\sampletool.exe", OpenApps.AppsFolder + SampleAumid]);
        config.CloseWhenAway.AddRange(["samplestudio", "sampletool"]);
        config.OpenWhenBack.Add(SampleExe);
        config.CloseWhenBack.Add("SampleStudio.exe");
        var b = Build(config, running: running, names: names);
        CollectionAssert.AreEqual(new[] { "Sample Studio", "sampletool", "SampleMusic" }, Chips(Row(b.Page, OpenAwayRow)).Texts.ToArray());
        CollectionAssert.AreEqual(new[] { "Sample Studio", "sampletool" }, Chips(Row(b.Page, CloseAwayRow)).Texts.ToArray(),
            "a running app's own name, and a name with nothing running under it as it is kept");
        CollectionAssert.AreEqual(new[] { "Sample Studio" }, Chips(Row(b.Page, OpenBackRow)).Texts.ToArray());
        CollectionAssert.AreEqual(new[] { "Sample Studio" }, Chips(Row(b.Page, CloseBackRow)).Texts.ToArray());
        CollectionAssert.AreEqual(new object[] { "samplestudio", "sampletool" }, Chips(Row(b.Page, CloseAwayRow)).Values.ToArray(), "the lists keep their own entries");
        Assert.IsEmpty(b.Edits, "showing names edits nothing");
    }

    [TestMethod]
    public void RemovingAChipTakesThatAppOutOfItsList()
    {
        var config = new Config();
        config.OpenWhenAway.AddRange([SampleExe, OpenApps.AppsFolder + SampleAumid]);
        config.CloseWhenAway.AddRange(["samplestudio", "sampletool"]);
        var b = Build(config);
        var open = Chips(Row(b.Page, OpenAwayRow));
        var close = Chips(Row(b.Page, CloseAwayRow));
        CollectionAssert.AreEqual(new[] { "samplestudio", "SampleMusic" }, open.Texts.ToArray());
        CollectionAssert.AreEqual(new[] { "samplestudio", "sampletool" }, close.Texts.ToArray());
        open.RemoveByUser(open.Parts[0]);
        var c = b.Apply();
        CollectionAssert.AreEqual(new[] { OpenApps.AppsFolder + SampleAumid }, c.OpenWhenAway);
        CollectionAssert.AreEqual(new[] { "samplestudio", "sampletool" }, c.CloseWhenAway, "the close list keeps its own samplestudio");
        close.RemoveByUser(close.Parts[1]);
        c = b.Apply();
        CollectionAssert.AreEqual(new[] { "samplestudio" }, c.CloseWhenAway);
        close.RemoveByUser(close.Parts[0]);
        Assert.IsEmpty(b.Apply().CloseWhenAway);
        Assert.IsEmpty(close.Texts);
        Assert.AreSame(ControlOf<KitButton>(Row(b.Page, CloseAwayRow)), close.FocusWhenEmpty, "an empty list sends focus to Add app");
    }

    // D51: Close apps says plainly what closing does to unsaved work, on both sides, above the apps,
    // and a screen reader hears it once, on the list of apps: not on the group around it and on Add
    // app as well, which would say it three times in a row. Add app has the row's explanation, as
    // Open apps' does. Open apps has no warning.
    [TestMethod]
    public void TheCloseListsCarryTheUnsavedWorkWarning()
    {
        foreach (string lang in new[] { "en", "ar" })
        {
            var v = Lang.For(lang).V1;
            var b = Build(text: Lang.For(lang), config: new Config { CloseWhenAway = { "samplestudio" }, CloseWhenBack = { "sampletool" } });
            foreach (int row in new[] { CloseAwayRow, CloseBackRow })
            {
                var r = Row(b.Page, row);
                Assert.AreEqual(v.CloseAppsRowDetail, r.Detail, lang);
                var close = (AwayTab.CloseList)r.Below!;
                Assert.AreEqual(v.CloseAppsWarning, close.Warning, lang);
                Assert.AreEqual(v.CloseAppsWarning, close.Chips.AccessibleDescription, "a screen reader on the list hears it");
                Assert.AreEqual(close.Chips.AccessibleName, close.AccessibleName);
                Assert.IsNull(close.AccessibleDescription, "not on the group around the list too");
                Assert.AreEqual(v.CloseAppsRowDetail, ControlOf<KitButton>(r).AccessibleDescription, "nor on Add app, which has the row's explanation");
            }
            foreach (int row in new[] { OpenAwayRow, OpenBackRow })
            {
                var r = Row(b.Page, row);
                Assert.IsInstanceOfType<ChipList>(r.Below);
                Assert.IsNull(r.Below!.AccessibleDescription);
                Assert.AreEqual(r.Detail, ControlOf<KitButton>(r).AccessibleDescription, "Add app has the row's explanation");
            }
        }
        StringAssert.Contains(Lang.For("en").V1.CloseAppsWarning, "30 seconds");
        StringAssert.Contains(Lang.For("ar").V1.CloseAppsWarning, "30\u00A0ثانية");
    }

    [TestMethod]
    public void ChooseThenRemoveAScript()
    {
        string file = @"C:\Scripts\leaving.cmd";
        int asked = 0;
        var b = Build(script: _ =>
        {
            asked++;
            return file;
        });
        var v = Lang.For("en").V1;
        var row = (AwayTab.ScriptRow)Row(b.Page, ScriptAwayRow);
        var button = row.Button;
        button.PerformClick();
        Assert.AreEqual(1, asked);
        var c = b.Apply();
        Assert.AreEqual(file, c.RunWhenAway);
        Assert.AreEqual("", c.RunWhenBack);
        Assert.AreEqual(file, row.Detail);
        Assert.AreEqual(v.Remove, button.Text);
        Assert.AreEqual(v.RunScriptAway + ". " + v.Remove, button.AccessibleName);
        button.PerformClick();
        Assert.AreEqual(1, asked, "Remove opens no dialog");
        Assert.AreEqual("", b.Apply().RunWhenAway);
        Assert.AreEqual(v.NoScriptChosen, row.Detail);
        Assert.AreEqual(v.Choose, button.Text);
        Assert.AreEqual(v.RunScriptAway + ". " + v.Choose, button.AccessibleName);
    }

    [TestMethod]
    public void ChooseBackScriptAndCancel()
    {
        string? answer = null;
        var b = Build(script: _ => answer);
        var button = ((AwayTab.ScriptRow)Row(b.Page, ScriptBackRow)).Button;
        button.PerformClick();
        Assert.IsEmpty(b.Edits, "a cancelled dialog changes nothing");
        answer = "\"C:\\Scripts\\back home.ps1\"";
        button.PerformClick();
        var c = b.Apply();
        Assert.AreEqual(@"C:\Scripts\back home.ps1", c.RunWhenBack, "quotes from a pasted path are dropped");
        Assert.AreEqual("", c.RunWhenAway);
    }

    // Choose… does not keep a script the row would never run (RunProgram.Refusal). One on another PC,
    // by a network path or on a mapped network drive, says so under the row's title, in the warning
    // color, and with the button; one of another kind, its name typed into the dialog, or on a drive
    // not connected now, says SteppedOut can't run it. The script chosen next takes the note away.
    [TestMethod]
    public void AScriptTheRowWouldNotRunIsNotKeptAndTheRowSaysWhy()
    {
        string? answer = @"\\192.0.2.10\share\away.bat";
        var b = Build(script: _ => answer, drives: letter => letter switch
        {
            'Z' => DriveType.Network,
            'Q' => DriveType.NoRootDirectory,
            _ => DriveType.Fixed,
        });
        var v = Lang.For("en").V1;
        var row = (AwayTab.ScriptRow)Row(b.Page, ScriptAwayRow);
        row.Button.PerformClick();
        Assert.IsEmpty(b.Edits, "not kept");
        Assert.AreEqual(v.ScriptOnAnotherPC, row.Detail);
        Assert.IsTrue(row.DetailWarns);
        Assert.AreEqual(v.ScriptOnAnotherPC, row.Button.AccessibleDescription);
        Assert.AreEqual(v.Choose, row.Button.Text);
        Assert.AreEqual(v.NoScriptChosen, Row(b.Page, ScriptBackRow).Detail, "only the row it was picked for");

        foreach (var (file, note) in new[]
        {
            (@"Z:\Scripts\away.bat", v.ScriptOnAnotherPC),
            (@"Q:\Scripts\away.bat", v.ScriptNotRunnable),
            (@"C:\Scripts\notes.txt", v.ScriptNotRunnable),
        })
        {
            answer = file;
            row.Button.PerformClick();
            Assert.IsEmpty(b.Edits, file);
            Assert.AreEqual(note, row.Detail, file);
        }

        answer = null;
        row.Button.PerformClick();
        Assert.AreEqual(v.ScriptNotRunnable, row.Detail, "a cancelled dialog leaves the note");

        answer = @"C:\Scripts\leaving.cmd";
        row.Button.PerformClick();
        Assert.AreEqual(answer, b.Apply().RunWhenAway);
        Assert.AreEqual(answer, row.Detail);
        Assert.IsFalse(row.DetailWarns);
        Assert.AreEqual(answer, row.Button.AccessibleDescription);
        row.Button.PerformClick();
        Assert.AreEqual(v.NoScriptChosen, row.Detail, "removed: no note comes back");
    }

    // Add app does not put an app that runs from another PC in Open apps, which would never open it
    // (OpenApps): the row says so in place of its explanation, in the warning color, until the next
    // change to that list.
    [TestMethod]
    public void AnAppOnAnotherPCIsNotAddedToOpenAppsAndTheRowSaysWhy()
    {
        var next = new PickedApp("tool", @"\\192.0.2.10\share\tool.exe", null);
        var b = Build(pick: (_, _, picked) => picked(next), drives: letter => letter == 'Z' ? DriveType.Network : DriveType.Fixed);
        var v = Lang.For("en").V1;
        var row = Row(b.Page, OpenAwayRow);
        var add = ControlOf<KitButton>(row);
        add.PerformClick();
        Assert.IsEmpty(b.Edits);
        Assert.AreEqual(v.AppOnAnotherPC, row.Detail);
        Assert.IsTrue(row.DetailWarns);
        Assert.AreEqual(v.AppOnAnotherPC, add.AccessibleDescription);
        Assert.AreEqual(v.OpenAppsBackDetail, Row(b.Page, OpenBackRow).Detail, "only the list it was picked for");

        next = new PickedApp("tool", @"Z:\Tools\tool.exe", null);
        add.PerformClick();
        Assert.IsEmpty(b.Edits, "nor one on a mapped network drive");
        Assert.AreEqual(v.AppOnAnotherPC, row.Detail);

        next = new PickedApp("samplestudio", SampleExe, null);
        add.PerformClick();
        CollectionAssert.AreEqual(new[] { SampleExe }, b.Apply().OpenWhenAway);
        Assert.AreEqual(v.OpenAppsAwayDetail, row.Detail, "the explanation back");
        Assert.IsFalse(row.DetailWarns);
        Assert.AreEqual(v.OpenAppsAwayDetail, add.AccessibleDescription);
    }

    // D27: a program chosen in an older version still shows and still runs.
    [TestMethod]
    public void AnOlderProgramChoiceShowsAsItself()
    {
        var config = new Config { RunWhenAway = @"C:\Tools\sampletool.exe" };
        var b = Build(config);
        var row = (AwayTab.ScriptRow)Row(b.Page, ScriptAwayRow);
        Assert.AreEqual(@"C:\Tools\sampletool.exe", row.Detail);
        Assert.AreEqual(Lang.For("en").V1.Remove, row.Button.Text);
        Assert.IsEmpty(b.Edits);
    }

    [TestMethod]
    public void RefillRaisesZeroEdits()
    {
        var b = Build();
        var full = new Config
        {
            MuteWhenAway = true,
            ScreensOffWhenAway = true,
            SoundWhenAway = ActionSound.SteppedOut,
            ShowTimeAway = false,
            SoundWhenBack = ActionSound.Drift,
            RunWhenAway = @"C:\Scripts\leaving.cmd",
            RunWhenBack = @"C:\Scripts\back.lnk",
        };
        full.OpenWhenAway.Add(SampleExe);
        full.CloseWhenAway.Add("samplestudio");
        full.OpenWhenBack.Add(OpenApps.AppsFolder + SampleAumid);
        full.CloseWhenBack.Add("sampletool");
        b.Tab.Refill(full);
        Assert.IsEmpty(b.Edits, "refill to everything on");
        Assert.IsTrue(ControlOf<Toggle>(Row(b.Page, MuteRow)).Checked);
        Assert.IsTrue(ControlOf<Toggle>(Row(b.Page, ScreenRow)).Checked);
        Assert.IsFalse(ControlOf<Toggle>(Row(b.Page, ShowTimeRow)).Checked);
        Assert.AreEqual(ActionSound.SteppedOut, ControlOf<DropDown>(Row(b.Page, SoundAwayRow)).Value);
        Assert.AreEqual(ActionSound.Drift, ControlOf<DropDown>(Row(b.Page, SoundBackRow)).Value);
        CollectionAssert.AreEqual(new[] { "samplestudio" }, Chips(Row(b.Page, OpenAwayRow)).Texts.ToArray());
        CollectionAssert.AreEqual(new[] { "samplestudio" }, Chips(Row(b.Page, CloseAwayRow)).Texts.ToArray());
        CollectionAssert.AreEqual(new[] { "SampleMusic" }, Chips(Row(b.Page, OpenBackRow)).Texts.ToArray());
        CollectionAssert.AreEqual(new[] { "sampletool" }, Chips(Row(b.Page, CloseBackRow)).Texts.ToArray());
        Assert.AreEqual(@"C:\Scripts\leaving.cmd", Row(b.Page, ScriptAwayRow).Detail);
        Assert.AreEqual(@"C:\Scripts\back.lnk", Row(b.Page, ScriptBackRow).Detail);
        b.Tab.Refill(full);
        Assert.IsEmpty(b.Edits, "the same refill again");
        b.Tab.Refill(new Config());
        Assert.IsEmpty(b.Edits, "refill back to the defaults");
        Assert.IsFalse(ControlOf<Toggle>(Row(b.Page, MuteRow)).Checked);
        Assert.IsTrue(ControlOf<Toggle>(Row(b.Page, ShowTimeRow)).Checked);
        Assert.IsEmpty(Chips(Row(b.Page, OpenAwayRow)).Texts);
        Assert.AreEqual(Lang.For("en").V1.NoScriptChosen, Row(b.Page, ScriptBackRow).Detail);
        // A refill before the page is built is kept for the build.
        var early = new AwayTab();
        int raised = 0;
        early.Edited += _ => raised++;
        early.Refill(full);
        var page = early.Build(full, Lang.For("en"));
        Assert.AreEqual(0, raised);
        Assert.IsTrue(ControlOf<Toggle>(Rows(page)[MuteRow]).Checked);
    }

    // After a refill, a person's change starts from the refilled settings, not from the build's.
    [TestMethod]
    public void AnEditAfterARefillStartsFromTheRefilledSettings()
    {
        var b = Build(pick: (_, _, picked) => picked(new PickedApp("sampletool", @"C:\Tools\sampletool.exe", null)));
        var outside = new Config();
        outside.OpenWhenAway.Add(SampleExe);
        b.Tab.Refill(outside);
        b.Config = outside.Copy();
        ControlOf<KitButton>(Row(b.Page, OpenAwayRow)).PerformClick();
        CollectionAssert.AreEqual(new[] { SampleExe, @"C:\Tools\sampletool.exe" }, b.Apply().OpenWhenAway);
        CollectionAssert.AreEqual(new[] { "samplestudio", "sampletool" }, Chips(Row(b.Page, OpenAwayRow)).Texts.ToArray());
    }

    [TestMethod]
    public void ARebuildShowsTheSettingsAndTheOldControlsGoQuiet()
    {
        var b = Build();
        var oldMute = ControlOf<Toggle>(Row(b.Page, MuteRow));
        var config = new Config { MuteWhenAway = true };
        b.Tab.Refill(config);
        var page = b.Tab.Build(config, Lang.For("ar"));
        page.Width = 740;
        page.PerformLayout();
        var v = Lang.For("ar").V1;
        Assert.AreEqual(v.Mute, Rows(page)[MuteRow].Title);
        Assert.IsTrue(ControlOf<Toggle>(Rows(page)[MuteRow]).Checked);
        Assert.IsEmpty(b.Edits);
        Assert.IsTrue(oldMute.Checked, "the refill reached the page that was showing");
        oldMute.Checked = false;
        Assert.IsEmpty(b.Edits, "a control from the earlier build changes nothing");
        ControlOf<Toggle>(Rows(page)[ScreenRow]).Checked = true;
        Assert.HasCount(1, b.Edits, "the new page edits");
        var c = b.Edits[0](config.Copy());
        Assert.IsTrue(c.ScreensOffWhenAway && c.MuteWhenAway);
        b.Page.Dispose();
        Assert.IsTrue(oldMute.IsDisposed);
    }

    [TestMethod]
    public void TheArabicPageUsesTheArabicText()
    {
        var ar = Lang.For("ar");
        var b = Build(text: ar);
        var v = ar.V1;
        Assert.AreEqual(v.CloseApps, Row(b.Page, CloseAwayRow).Title);
        Assert.AreEqual(v.AddApp, ControlOf<KitButton>(Row(b.Page, OpenAwayRow)).Text);
        Assert.AreEqual(v.NoAppsAdded, Chips(Row(b.Page, OpenAwayRow)).EmptyText);
        Assert.AreEqual(v.Choose, ((AwayTab.ScriptRow)Row(b.Page, ScriptAwayRow)).Button.Text);
        CollectionAssert.AreEqual(new[] { v.Off, v.SoundSteppedOut, v.SoundDrift, v.SoundPebble, v.SoundGlow, v.SoundRipple }, ControlOf<DropDown>(Row(b.Page, SoundAwayRow)).Choices.ToArray());
        var preview = (AwayTab.AwayPreview)Row(b.Page, ShowTimeRow).Below!;
        Assert.AreEqual(ar.YouWereAwayFor(TimeSpan.FromMinutes(42)), preview.Lead);
    }

    [TestMethod]
    public void TheTimeAwayRowShowsAPreviewOfTheNote()
    {
        var en = Lang.For("en");
        var b = Build();
        var preview = (AwayTab.AwayPreview)Row(b.Page, ShowTimeRow).Below!;
        Assert.AreEqual("You were away 42 min", preview.Lead);
        Assert.AreEqual(en.V1.MediaPlayingAgain, preview.Tail);
        Assert.IsFalse(preview.CanSelect, "only a picture: Tab passes it by");
        Assert.AreEqual("You were away 42 min " + en.V1.MediaPlayingAgain, preview.AccessibleName);
    }

    [TestMethod]
    public void TheTimeAwayPreviewGoesOnTwoLinesWhenOneIsTooNarrow()
    {
        var preview = (AwayTab.AwayPreview)Row(Build().Page, ShowTimeRow).Below!;
        Assert.IsFalse(preview.OnTwoLines(2000));
        Assert.IsTrue(preview.OnTwoLines(150));
        Assert.IsGreaterThan(preview.GetPreferredSize(new System.Drawing.Size(2000, 0)).Height, preview.GetPreferredSize(new System.Drawing.Size(150, 0)).Height);
        Assert.AreEqual("music or video is playing again", preview.SecondLine);
        var ar = (AwayTab.AwayPreview)Row(Build(text: Lang.For("ar")).Page, ShowTimeRow).Below!;
        Assert.AreEqual("تم استئناف التشغيل", ar.SecondLine);
    }

    [TestMethod]
    public void EveryControlAKeyReachesHasAName()
    {
        var config = new Config();
        config.OpenWhenAway.Add(SampleExe);
        config.CloseWhenBack.Add("sampletool");
        var b = Build(config);
        var reachable = All<Control>(b.Page).Where(c => c is Toggle or DropDown or KitButton or Part).ToList();
        Assert.IsGreaterThanOrEqualTo(13, reachable.Count);
        var unnamed = reachable.Where(c => string.IsNullOrWhiteSpace(c.AccessibilityObject.Name)).Select(c => c.GetType().Name).ToList();
        Assert.IsEmpty(unnamed, string.Join(", ", unnamed));
        foreach (var head in All<PageHeading>(b.Page)) Assert.IsFalse(head.CanSelect);
    }

    // Someone moving with Tab never meets the heads, so a list both parts have says which part it is in.
    [TestMethod]
    public void TheListsBothPartsHaveSayWhichPartTheyAreIn()
    {
        foreach (var lang in new[] { "en", "ar" })
        {
            var text = Lang.For(lang);
            var v = text.V1;
            string comma = text.RightToLeft ? "\u060C " : ", ";
            var b = Build(text: text);
            foreach (var (row, title, part) in new[]
            {
                (OpenAwayRow, v.OpenApps, v.WhenAway), (CloseAwayRow, v.CloseApps, v.WhenAway),
                (OpenBackRow, v.OpenApps, v.WhenBack), (CloseBackRow, v.CloseApps, v.WhenBack),
            })
            {
                var r = Row(b.Page, row);
                Assert.AreEqual(title + comma + part + ". " + v.AddApp, ControlOf<KitButton>(r).AccessibleName, lang);
                Assert.AreEqual(title + comma + part, r.Below!.AccessibleName, lang);
            }
            Assert.AreEqual(v.PlaySound + comma + v.WhenAway, ControlOf<DropDown>(Row(b.Page, SoundAwayRow)).AccessibleName, lang);
            Assert.AreEqual(v.PlaySound + comma + v.WhenBack, ControlOf<DropDown>(Row(b.Page, SoundBackRow)).AccessibleName, lang);
            var names = All<Control>(b.Page).Where(c => c is Toggle or DropDown or KitButton or ChipList)
                .Select(c => c.AccessibilityObject.Name ?? "").ToList();
            var twice = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.IsEmpty(twice, lang + ": " + string.Join(" | ", twice));
        }
    }

    // A long script path shows its start and its file's name on one line, "..." for the folders between;
    // the settings and the button's description keep all of it.
    [TestMethod]
    public void ALongScriptPathKeepsItsFileName()
    {
        string path = @"D:\Shared\Sample\Documents\Automation\Scripts\NoSpacesAtAllInThisFolderNameBecauseItIsVeryLongIndeed\Another\leaving_v2_final.cmd";
        var b = Build(new Config { RunWhenAway = path });
        var row = (AwayTab.ScriptRow)Row(b.Page, ScriptAwayRow);
        row.Width = 360;
        row.PerformLayout();
        string shown = row.Detail!;
        Assert.StartsWith(@"D:\", shown);
        Assert.EndsWith(@"\leaving_v2_final.cmd", shown);
        Assert.Contains("\u2026", shown);
        var label = row.Controls.OfType<Label>().Skip(1).First();
        int width = TextRenderer.MeasureText(shown, label.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
        Assert.IsLessThanOrEqualTo(label.Width, width, "it fits on its line");
        Assert.AreEqual(path, row.Path);
        Assert.AreEqual(path, row.Button.AccessibleDescription);
        b.Page.Width = 2400;
        b.Page.PerformLayout();
        row.PerformLayout();
        Assert.AreEqual(path, row.Detail, "with room, all of it");
        Assert.IsEmpty(b.Edits);
    }

    [TestMethod]
    public void APathIsCutDownFromTheFolders()
    {
        static Func<string, bool> Upto(int n) => s => s.Length <= n;
        Assert.AreEqual(@"C:\a\b\c.cmd", AwayTab.ScriptRow.Compact(@"C:\a\b\c.cmd", Upto(50)));
        Assert.AreEqual("C:\\\u2026\\bb\\c.cmd", AwayTab.ScriptRow.Compact(@"C:\aaaa\bb\c.cmd", Upto(13)));
        Assert.AreEqual("C:\\\u2026\\c.cmd", AwayTab.ScriptRow.Compact(@"C:\aaaa\bbbb\c.cmd", Upto(10)));
        Assert.AreEqual("\u2026\\c.cmd", AwayTab.ScriptRow.Compact(@"C:\aaaa\bbbb\c.cmd", Upto(7)));
        Assert.AreEqual("\\\\nas\\share\\\u2026\\away.cmd", AwayTab.ScriptRow.Compact(@"\\nas\share\scripts\more\away.cmd", Upto(22)), "a network path keeps its share");
        Assert.AreEqual("\u2026\\away.cmd", AwayTab.ScriptRow.Compact(@"\\nas\share\away.cmd", Upto(12)));
        Assert.AreEqual("away.cmd", AwayTab.ScriptRow.Compact("away.cmd", Upto(3)), "nothing to leave out");
    }

    // In the Arabic window a path still reads left to right: a network path keeps its \\ at the start
    // and Arabic folder names stay in their places.
    [TestMethod]
    public void AnArabicWindowShowsAPathLeftToRight()
    {
        string network = @"\\nas\share\scripts\away.cmd";
        string arabic = "D:\\Team\\\u0633\u0627\u0631\u0629\\Documents\\\u0639\u0648\u062F\u0629 \u0627\u0644\u062C\u0647\u0627\u0632.ps1";
        var b = Build(new Config { RunWhenAway = network, RunWhenBack = arabic }, Lang.For("ar"));
        foreach (var (index, path) in new[] { (ScriptAwayRow, network), (ScriptBackRow, arabic) })
        {
            var row = (AwayTab.ScriptRow)Row(b.Page, index);
            row.Width = 2000;
            row.PerformLayout();
            Assert.AreEqual("\u202A" + path + "\u202C", row.Detail);
            Assert.AreEqual(path, row.Button.AccessibleDescription, "the description keeps the path as it is");
            // Drawn right to left, as the Arabic window draws it, it comes out as the path drawn left to right.
            var font = row.Controls.OfType<Label>().Skip(1).First().Font;
            using var asShown = Drawn(row.Detail!, font, TextFormatFlags.RightToLeft);
            using var asPath = Drawn(path, font, 0);
            using var unmarked = Drawn(path, font, TextFormatFlags.RightToLeft);
            Assert.AreEqual(0, Differ(asShown, asPath), path);
            Assert.AreNotEqual(0, Differ(unmarked, asPath), "without the marks it would come out in another order");
        }
        Assert.IsEmpty(b.Edits);
        var en = Build(new Config { RunWhenAway = network });
        var english = (AwayTab.ScriptRow)Row(en.Page, ScriptAwayRow);
        english.Width = 2000;
        english.PerformLayout();
        Assert.AreEqual(network, english.Detail, "an English window needs no marks");
    }

    static Bitmap Drawn(string words, Font font, TextFormatFlags direction)
    {
        var picture = new Bitmap(600, 24);
        using var g = Graphics.FromImage(picture);
        g.Clear(Color.White);
        TextRenderer.DrawText(g, words, font, new Rectangle(0, 0, 600, 24), Color.Black, Color.White,
            TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | direction);
        return picture;
    }

    static int Differ(Bitmap a, Bitmap b)
    {
        int count = 0;
        for (int x = 0; x < a.Width; x++)
        {
            for (int y = 0; y < a.Height; y++)
            {
                if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb()) count++;
            }
        }
        return count;
    }

    [TestMethod]
    public void OpenEntriesAndChipNames()
    {
        Assert.AreEqual(SampleExe, AwayTab.OpenEntry(new PickedApp("samplestudio", SampleExe, null)));
        Assert.AreEqual(OpenApps.AppsFolder + SampleAumid, AwayTab.OpenEntry(new PickedApp("x", SampleExe, SampleAumid)), "a Store app opens by its id");
        Assert.IsNull(AwayTab.OpenEntry(new PickedApp("x", null, null)));
        Assert.IsNull(AwayTab.OpenEntry(new PickedApp("x", "", "")));
        Assert.IsNull(AwayTab.OpenEntry(new PickedApp("x", @"\\192.0.2.10\share\tool.exe", null)), "a program on another PC");
        Assert.IsNull(AwayTab.OpenEntry(new PickedApp("x", @"Z:\Tools\tool.exe", null), _ => DriveType.Network), "or on a mapped network drive");
        Assert.AreEqual(OpenApps.AppsFolder + SampleAumid, AwayTab.OpenEntry(new PickedApp("x", @"\\192.0.2.10\share\tool.exe", SampleAumid)), "a Store app by its id");
        Assert.AreEqual("samplestudio", AwayTab.CloseEntry(new PickedApp("samplestudio.exe", null, null)));
        Assert.AreEqual("samplestudio", AwayTab.AppName(SampleExe));
        Assert.AreEqual("samplestudio", AwayTab.AppName("\"" + SampleExe + "\""));
        Assert.AreEqual("SampleMusic", AwayTab.AppName(OpenApps.AppsFolder + SampleAumid, Unknown));
        Assert.AreEqual("SampleCalc", AwayTab.AppName(OpenApps.AppsFolder + "SampleCo.SampleCalc_abc123xyz!App", Unknown));
        Assert.AreEqual("SampleCalc", AwayTab.AppName(OpenApps.AppsFolder + "SampleCo.SampleCalc_abc123xyz", Unknown));
        Assert.AreEqual("shell", AwayTab.AppName("shell"));
    }

    static string? Unknown(string id) => null;

    // The ids a Store app really has: the part after ! is often the package's own dotted name, a
    // program file, or just App. Windows' Start menu name is what the chip shows, whatever the shape.
    static readonly (string Id, string StartMenu, string Guess)[] StoreShapes =
    [
        ("SampleCo.SampleTunes_abc123xyz!SampleCo.SampleTunes", "Sample Tunes", "SampleCo.SampleTunes"),
        ("SampleCo.SampleMailForWindows_abc123xyz!SampleCo.SampleMailforWindows", "Sample Mail", "SampleCo.SampleMailforWindows"),
        ("sampleco.windowslive.samplenotes_abc123xyz!sampleco.windowslive.samplenotes", "Sample Notes", "sampleco.windowslive.samplenotes"),
        ("SampleCo.SampleClicker_abc123xyz!SampleClicker.exe", "Sample Clicker", "SampleClicker"),
        ("SampleCo.WindowsSampleCalc_abc123xyz!App", "Sample Calculator", "WindowsSampleCalc"),
        ("SampleCo.SampleOverlay_abc123xyz", "Sample Bar", "SampleOverlay"),
        ("SampleCo.SampleTool", "Sample Tool", "SampleTool"),
    ];

    [TestMethod]
    public void AStoreAppIsCalledWhatTheStartMenuCallsIt()
    {
        var names = StoreShapes.ToDictionary(shape => shape.Id, shape => shape.StartMenu);
        var asked = new List<string>();
        string? StartMenu(string id)
        {
            asked.Add(id);
            return names.GetValueOrDefault(id);
        }
        foreach (var (id, startMenu, _) in StoreShapes)
            Assert.AreEqual(startMenu, AwayTab.AppName(OpenApps.AppsFolder + id, StartMenu), id);
        CollectionAssert.AreEqual(StoreShapes.Select(shape => shape.Id).ToArray(), asked.ToArray(), "Windows is asked with the id alone");
        const string arabic = "\u0645\u0634\u063A\u0644 \u0627\u0644\u0648\u0633\u0627\u0626\u0637";
        Assert.AreEqual(arabic, AwayTab.AppName(OpenApps.AppsFolder + StoreShapes[0].Id, _ => "  " + arabic + " "), "the name in the language Windows gives, trimmed");

        var config = new Config();
        config.OpenWhenAway.AddRange(StoreShapes.Take(4).Select(shape => OpenApps.AppsFolder + shape.Id));
        config.OpenWhenBack.AddRange([SampleExe, OpenApps.AppsFolder + StoreShapes[4].Id]);
        var b = Build(config, startMenu: StartMenu);
        var away = Chips(Row(b.Page, OpenAwayRow));
        var back = Chips(Row(b.Page, OpenBackRow));
        CollectionAssert.AreEqual(new[] { "Sample Tunes", "Sample Mail", "Sample Notes", "Sample Clicker" }, away.Texts.ToArray());
        CollectionAssert.AreEqual(new[] { "samplestudio", "Sample Calculator" }, back.Texts.ToArray());
        CollectionAssert.AreEqual(config.OpenWhenAway.Cast<object?>().ToArray(), away.Values.ToArray(), "each chip still holds its id");
        Assert.IsEmpty(b.Edits);
    }

    [TestMethod]
    public void WhenWindowsDoesNotKnowAStoreAppTheChipStillHasAName()
    {
        foreach (var (id, _, guess) in StoreShapes)
        {
            Assert.AreEqual(guess, AwayTab.AppName(OpenApps.AppsFolder + id, Unknown), id);
            Assert.AreEqual(guess, AwayTab.AppName(OpenApps.AppsFolder + id, _ => "   "), id + ", a blank answer");
            Assert.AreEqual(guess, AwayTab.AppName(OpenApps.AppsFolder + id, _ => throw new InvalidOperationException("no shell")), id + ", Windows failing");
        }
        foreach (var entry in new[] { OpenApps.AppsFolder, OpenApps.AppsFolder + "  ", "\"" + OpenApps.AppsFolder + "\"", "shell:appsfolder\\", OpenApps.AppsFolder + "!", OpenApps.AppsFolder + "!App", OpenApps.AppsFolder + "_x!" })
        {
            string name = AwayTab.AppName(entry, Unknown);
            Assert.IsFalse(string.IsNullOrWhiteSpace(name), $"[{entry}] gives a name");
        }

        // A list holding such ids is drawn with a name on every chip.
        var config = new Config();
        config.OpenWhenAway.AddRange([OpenApps.AppsFolder, OpenApps.AppsFolder + StoreShapes[3].Id]);
        var b = Build(config);
        var chips = Chips(Row(b.Page, OpenAwayRow));
        Assert.HasCount(2, chips.Texts);
        Assert.IsTrue(chips.Texts.All(text => text.Trim().Length > 0));
        Assert.AreEqual("SampleClicker", chips.Texts[1]);
    }

    [TestMethod]
    public void ThePickerLeavesOutAStoreAppByItsGuessAndItsStartMenuName()
    {
        var config = new Config();
        config.OpenWhenAway.Add(OpenApps.AppsFolder + StoreShapes[0].Id);
        IReadOnlyList<string>? already = null;
        var b = Build(config, pick: (_, names, _) => already = names, startMenu: id => id == StoreShapes[0].Id ? "Sample Tunes" : null);
        ControlOf<KitButton>(Row(b.Page, OpenAwayRow)).PerformClick();
        Assert.IsNotNull(already);
        CollectionAssert.AreEquivalent(new[] { "SampleCo.SampleTunes", "Sample Tunes" }, already!.ToArray());
    }

    // Windows' own answer on this PC: File Explorer is in every Windows desktop's Start menu, and an id
    // it has never heard of comes back as the guess.
    [TestMethod]
    public void WindowsNamesFileExplorerAndNotAnIdItDoesNotKnow()
    {
        string madeUp = "SampleCo.NoSuchApp" + Guid.NewGuid().ToString("N") + "_abc123xyz!App";
        Assert.AreEqual(AwayTab.AppName(OpenApps.AppsFolder + madeUp, Unknown), AwayTab.AppName(OpenApps.AppsFolder + madeUp));
        string explorer = AwayTab.AppName(OpenApps.AppsFolder + "Microsoft.Windows.Explorer");
        Assert.IsFalse(string.IsNullOrWhiteSpace(explorer));
        if (explorer == "Explorer") Assert.Inconclusive("this Windows has no File Explorer in its Start menu");
        Assert.DoesNotContain(".", explorer, "Windows' name, not the id");
        Assert.AreEqual(explorer, AwayTab.AppName(OpenApps.AppsFolder + "Microsoft.Windows.Explorer"), "the same name when asked again");
    }

    [TestMethod]
    public void ThePickerFilterAndTheScriptFilterOfferTheRightFiles()
    {
        foreach (var lang in new[] { "en", "ar" })
        {
            var v = Lang.For(lang).V1;
            var parts = v.ScriptFileFilter.Split('|');
            Assert.HasCount(2, parts, lang);
            CollectionAssert.AreEqual(new[] { "*.bat", "*.cmd", "*.ps1", "*.lnk" }, parts[1].Split(';'), lang);
            parts = v.AppPickerFileFilter.Split('|');
            Assert.HasCount(2, parts, lang);
            Assert.AreEqual("*.exe", parts[1], lang);
        }
    }
}
