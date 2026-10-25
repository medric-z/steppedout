using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace SteppedOut.Tests;

// The tray app's wiring and the parts it rests on. Nothing here locks, plays, registers a real
// shortcut, writes the Run value or shows an icon or a window: the assembly sets
// SessionLock.Blocked, and TrayRig gives the app a stand-in for every part.
[TestClass]
[DoNotParallelize]
public class TrayAppTests
{
    Strings? wasLanguage;

    [TestInitialize]
    public void Before()
    {
        wasLanguage = Lang.Current;
        Lang.Use("en");
    }

    [TestCleanup]
    public void After()
    {
        Lang.Use(wasLanguage == Arabic.Text ? "ar" : "en");
        Theme.Choose(ThemeChoice.System, TextSize.System);
    }

    static V1Text V => Lang.Current.V1;

    static Config Welcomed(Action<Config>? change = null)
    {
        var config = new Config { Welcomed = true };
        change?.Invoke(config);
        return config;
    }

    // A new install has no shortcut; these tests are about one the person picked.
    static Config WithShortcut() => Welcomed(c => c.LockShortcut = "Ctrl+Alt+L");

    static Config Edited(TrayRig rig, Action<Config> change)
    {
        var config = rig.App.Config.Copy();
        change(config);
        return config;
    }

    // A choice from a list's menu, as a click on it makes it.
    static void Pick(DropDown list, string text)
    {
        using var menu = list.BuildMenu();
        menu.Items.OfType<ToolStripMenuItem>().First(item => item.Text == text).PerformClick();
    }

    static void Choose(PartGroup group, int part) =>
        typeof(PartGroup).GetMethod("Choose", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(group, [group.Parts[part]]);

    static IEnumerable<T> All<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T match) yield return match;
            foreach (var deeper in All<T>(child)) yield return deeper;
        }
    }

    static FieldInfo Field(Type type, string name) =>
        type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new AssertFailedException("no field " + name);

    // What an event's listeners are, read from its field.
    static T? Listeners<T>(object owner, string name) where T : Delegate => Field(owner.GetType(), name).GetValue(owner) as T;

    static SettingsForm OpenSettings(TrayRig rig)
    {
        rig.App.OpenSettings();
        return rig.App.Settings ?? throw new AssertFailedException("no settings window");
    }

    // ---- The parts ----

    // Start-up runs in its order: the look, the notes (the first control, so what listens to Windows
    // is called back on this thread), the away actions, the icon with its picture before it shows,
    // start with Windows, the watcher, the shortcut, the controller, the welcome. On a first run the
    // shortcut is not taken until the welcome is finished (D46).
    [TestMethod]
    public void StartUpRunsInOrder()
    {
        using var rig = new TrayRig(new Config { Welcomed = false }, before: r => r.Autostart.Missing = true);
        CollectionAssert.AreEqual(new[]
        {
            "theme", "toast", "actions", "icon", "picture", "visible", "take over?", "read", "watcher", "hotkey", "controller",
            "window WelcomeForm",
        }, rig.Steps.ToArray());
        CollectionAssert.AreEqual(new[] { true }, rig.Autostart.Chosen, "a Run entry for a copy that is gone is taken over, once");
        Assert.IsTrue(rig.Icon.Visible);
    }

    // After the welcome, the shortcut is taken at start, in its place.
    [TestMethod]
    public void StartUpAfterTheWelcomeTakesTheShortcutAtOnce()
    {
        using var rig = new TrayRig(WithShortcut());
        CollectionAssert.AreEqual(new[]
        {
            "theme", "toast", "actions", "icon", "picture", "visible", "take over?", "read", "watcher", "hotkey", "register", "controller",
        }, rig.Steps.ToArray());
        Assert.IsTrue(rig.Keys.Holds("Ctrl+Alt+L"));
    }

    // While SessionLock.Blocked is set the real parts are safe on their own, so a test that forgets
    // one touches nothing of the person's.
    [TestMethod]
    public void TheRealPartsAreSafeWhileBlocked()
    {
        Assert.IsTrue(SessionLock.Blocked);
        var real = TrayParts.Real;
        using (var menu = new ContextMenuStrip())
        using (var icon = real.NewIcon(menu))
            Assert.IsInstanceOfType<InertTrayIcon>(icon, "no icon in the tray");
        Assert.IsInstanceOfType<MemoryAutostart>(real.Autostart, "the Run entry is never written");
        var before = Autostart.Read();
        real.Autostart.Choose(!before.IsOn());
        Assert.AreEqual(before, Autostart.Read());
        using (var form = new Form())
        {
            real.ShowWindow(form);
            Assert.IsTrue(form.IsHandleCreated);
            Assert.IsFalse(form.Visible, "never shown");
        }
        int noted = 0;
        using (var pads = real.NewController(_ => noted++))
        {
            Assert.IsFalse(pads.Poll(0));
            Assert.IsInstanceOfType<TrayParts.NoPads>(Field(typeof(ControllerInput), "pads").GetValue(pads));
        }
        Assert.AreEqual(0, noted);
        using (var toast = real.NewToast())
            Assert.IsTrue((bool)Field(typeof(Toast), "headless").GetValue(toast)!, "notes never make a window");
        using (var key = real.NewHotKey())
        {
            key.Apply("Ctrl+Alt+L");
            Assert.IsFalse(key.IsRegistered, "Ctrl+Alt+L is never taken");
        }
        using (var actions = real.NewActions(false))
            Assert.AreSame(AwayParts.Refused.Instance, actions.Parts.Sound, "nothing plays");
        Assert.AreNotEqual(AppInfo.ConfigPath, real.ConfigPath);
        StringAssert.StartsWith(real.ConfigPath, Path.GetTempPath());
    }

    // The second layer: every part TrayRig gives is its own, never one left at the real default.
    // The look is the one allowed: it is the real Theme.Choose, wrapped.
    [TestMethod]
    public void TheTestPartsReplaceEveryRealOne()
    {
        using var rig = new TrayRig(before: r => r.SkipStart = true);
        var real = TrayParts.Real;
        string[] allowed = [nameof(TrayParts.ChooseTheme)];
        foreach (var property in typeof(TrayParts).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (allowed.Contains(property.Name)) continue;
            object? mine = property.GetValue(rig.Parts), theirs = property.GetValue(real);
            bool same = (mine, theirs) switch
            {
                (Delegate a, Delegate b) => a.Method == b.Method,
                (string a, string b) => a == b,
                (bool a, bool b) => a == b,
                (null, null) => true,
                (null, _) or (_, null) => false,
                _ => mine.GetType() == theirs.GetType(),
            };
            Assert.IsFalse(same, $"{property.Name} is left at the real part");
        }
    }

    // The shortcut, the controller and start with Windows can each fail to start; the tray still
    // comes up and still locks.
    [TestMethod]
    public void APartThatFailsLeavesTheTrayUp()
    {
        using var rig = new TrayRig(before: r =>
        {
            r.Parts = r.Parts with
            {
                NewHotKey = () => throw new InvalidOperationException("no window"),
                NewController = _ => throw new DllNotFoundException("xinput"),
            };
            r.Autostart.Throws = true;
        });
        Assert.IsTrue(rig.Icon.Visible);
        Assert.IsNull(rig.App.HotKey);
        Assert.IsNull(rig.App.Controller);
        Assert.AreEqual(HotKeyResult.Ok, rig.App.ShortcutResult);
        rig.App.ShortcutChanged("Ctrl+Alt+K");
        rig.Second();
        rig.App.LockNowChosen();
        Assert.AreEqual(LockState.Away, rig.App.Status.Kind);
    }

    // A take-over check that fails (a Run key the PC locks down) still leaves the first read: the row
    // is not left on Reading for the whole run.
    [TestMethod]
    public void ATakeOverCheckThatFailsStillReadsStartWithWindows()
    {
        using var rig = new TrayRig(before: r =>
        {
            r.Autostart.Throws = true;
            r.Autostart.State = AutostartState.On;
        });
        ISettingsHost host = rig.App;
        Assert.AreEqual(AutostartState.On, host.StartWithWindowsState);
        Assert.AreEqual(1, rig.Autostart.Reads);
        var toggle = OpenSettings(rig).Main.StartWithWindows!;
        Assert.IsTrue(toggle.Enabled);
        Assert.IsTrue(toggle.Checked);
    }

    // A config folder that is gone: the file is not watched, and the tray still comes up.
    [TestMethod]
    public void AConfigFolderThatIsGoneLeavesTheTrayUp()
    {
        using var rig = new TrayRig(before: r => r.Parts = r.Parts with { ConfigPath = Path.Combine(r.Folder, "gone", "config.json") });
        Assert.IsTrue(rig.Icon.Visible);
        rig.App.ConfigEdited(Edited(rig, c => c.IdleMinutes = 30));
        Assert.AreEqual(30, rig.App.Config.IdleMinutes, "used, though it could not be saved");
    }

    // ---- The status ----

    // The tray's Pause row and the status box choose between Pause and Turn back on from the status:
    // it must show each click when the call returns, not a tick later.
    [TestMethod]
    public void StatusFollowsPauseResumeAndLockNowWithoutATick()
    {
        using var rig = new Rig(new Config { IdleMinutes = 15 }, dryRun: true);
        int updated = 0;
        rig.Watcher.Updated += () => updated++;
        rig.Second();
        Assert.AreEqual(LockState.On, rig.Watcher.LockStatus.Kind);
        Assert.AreEqual(1, updated, "every tick raises Updated");

        rig.Watcher.Pause(TimeSpan.FromMinutes(15));
        Assert.AreEqual(LockState.Paused, rig.Watcher.LockStatus.Kind);
        Assert.AreEqual(2, updated);

        rig.Watcher.Resume();
        Assert.AreEqual(LockState.On, rig.Watcher.LockStatus.Kind);
        Assert.AreEqual(3, updated);

        rig.Watcher.LockNow();
        Assert.AreEqual(LockState.Away, rig.Watcher.LockStatus.Kind);
        Assert.AreEqual(4, updated);
        Assert.AreEqual(0, rig.LockRequests, "a dry run never asks Windows to lock");

        // Asked again while already stepping out: nothing changes, so nothing is raised.
        rig.Watcher.LockNow();
        Assert.AreEqual(LockState.Away, rig.Watcher.LockStatus.Kind);
    }

    // Through the host, as the status box calls it: the status, StatusChanged and the icon have all
    // changed when each call returns.
    [TestMethod]
    public void EachChoiceShowsWhenItsCallReturns()
    {
        using var rig = new TrayRig();
        ISettingsHost host = rig.App;
        rig.Second();
        int changed = 0;
        host.StatusChanged += () => changed++;

        host.PauseChosen(TimeSpan.FromMinutes(15));
        Assert.AreEqual(LockState.Paused, host.Status.Kind);
        Assert.AreEqual(1, changed);
        Assert.AreEqual(TrayIconState.Paused, rig.App.LastView!.Icon);

        host.ResumeChosen();
        Assert.AreEqual(LockState.On, host.Status.Kind);
        Assert.AreEqual(2, changed);
        Assert.AreEqual(TrayIconState.On, rig.App.LastView!.Icon);

        host.LockNowChosen();
        Assert.AreEqual(LockState.Away, host.Status.Kind);
        Assert.AreEqual(3, changed);
        Assert.AreEqual(0, rig.LockRequests);
        CollectionAssert.AreEqual(new[] { "away locking" }, rig.Events);

        rig.Second();
        Assert.AreEqual(4, changed, "and every tick");
    }

    // The menu's rows reach the same calls.
    [TestMethod]
    public void TheMenuRowsDoWhatTheySay()
    {
        using var rig = new TrayRig();
        var menu = rig.App.Menu;
        rig.Second();
        menu.PauseChoices.OfType<ToolStripMenuItem>().First().PerformClick();
        Assert.IsTrue(rig.Watcher.IsPaused);
        Assert.AreEqual(LockState.Paused, rig.App.Status.Kind);
        menu.RaiseOpenedForTests();
        Assert.IsTrue(menu.TurnBackOnRow.Available);
        Assert.IsFalse(menu.PauseRow.Available);
        menu.TurnBackOnRow.PerformClick();
        Assert.IsFalse(rig.Watcher.IsPaused);
        menu.LockNowRow.PerformClick();
        Assert.AreEqual(LockState.Away, rig.App.Status.Kind);
        CollectionAssert.AreEqual(new[] { "away locking" }, rig.Events);
        menu.SettingsRow.PerformClick();
        Assert.IsNotNull(rig.App.Settings);
        menu.RaiseClosedForTests();

        // Quit waits for the menu to finish with the click.
        menu.QuitRow.PerformClick();
        Assert.IsFalse(rig.App.Quitting);
        rig.Drain();
        Assert.IsTrue(rig.App.Quitting);
        Assert.IsTrue(rig.Icon.Disposed);
    }

    // A left click on the icon opens the settings, and a second one brings the same window back.
    [TestMethod]
    public void ALeftClickOpensTheSettings()
    {
        using var rig = new TrayRig();
        rig.Icon.Click();
        var form = rig.App.Settings;
        Assert.IsNotNull(form);
        rig.Icon.Click();
        Assert.AreSame(form, rig.App.Settings);
        Assert.AreEqual(1, rig.Shown.OfType<SettingsForm>().Distinct().Count(), "one window");
    }

    // A listener that fails, such as a page on its way out, stops neither the others nor the icon.
    [TestMethod]
    public void AFailingStatusListenerStopsNothing()
    {
        using var rig = new TrayRig(Welcomed(c => c.MinutesOnTrayIcon = true));
        ISettingsHost host = rig.App;
        int after = 0;
        host.StatusChanged += () => throw new ObjectDisposedException("page");
        host.StatusChanged += () => after++;
        rig.Second();
        rig.Second();
        Assert.AreEqual(2, after);
        rig.Idle = 120;
        rig.Second();
        Assert.AreEqual("13", rig.App.LastView!.Minutes, "the icon still follows");
    }

    // The words on hover say a dry run is one.
    [TestMethod]
    public void TheTooltipSaysItIsADryRun()
    {
        using var rig = new TrayRig();
        rig.Second();
        StringAssert.Contains(rig.Icon.Text, "(dry run)");
    }

    // NotifyIcon takes 127 characters at most, the marks that keep Arabic right to left included.
    [TestMethod]
    public void TheTooltipFitsForALongArabicAppName()
    {
        var text = Arabic.Text;
        var status = new LockStatus
        {
            Kind = LockState.Never,
            Time = LockTime.Compute(15, [new ActiveEffect(Effect.Never, EffectSource.ForApp(new string('ق', 200)))]),
        };
        var view = TrayView.From(status, false, new Config(), null, text);
        Assert.IsLessThanOrEqualTo(127, view.Tooltip.Length);
        Assert.AreEqual('‫', view.Tooltip[0]);
        Assert.AreEqual('‬', view.Tooltip[^1]);
        StringAssert.StartsWith(view.Tooltip, "‫SteppedOut · ");
    }

    // Today's counts reach the settings window's status and the tray's status box.
    [TestMethod]
    public void TodaysCountsReachTheStatus()
    {
        using var rig = new TrayRig();
        rig.Second();
        rig.App.LockNowChosen();
        rig.Jump(TimeSpan.FromMinutes(3));
        rig.Busy();
        var today = rig.Watcher.LockStatus.Today;
        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromMinutes(3), today.Away);
        Assert.AreEqual(today, rig.App.Status.Today);
        rig.App.Menu.RaiseOpenedForTests();
        Assert.AreEqual(Lang.Current.AwayToday(today.Away), rig.App.Menu.StatusItem.AwayValue);
    }

    // The Why window's lines are the watcher's.
    [TestMethod]
    public void WhyComesFromTheWatcher()
    {
        using var rig = new TrayRig();
        rig.World.FullScreen = "quillpad";
        rig.Second();
        Assert.AreEqual(LockState.Never, rig.App.Status.Kind);
        var lines = rig.App.Explain();
        Assert.IsNotEmpty(lines);
        CollectionAssert.AreEqual(rig.Watcher.Explain().ToList(), lines.ToList());
    }

    // ---- The lock shortcut ----

    // Start-up registers the shortcut in the settings, once; none in the settings lets it go.
    [TestMethod]
    public void TheShortcutIsRegisteredOnceAndNoneLetsItGo()
    {
        using (var fresh = new TrayRig())
        {
            Assert.IsNull(fresh.Keys.Held, "a new install has none");
            Assert.AreEqual(0, fresh.Keys.Registers);
            Assert.IsNull(fresh.App.LastView!.Menu.Shortcut, "nothing beside Lock now");
        }
        using var rig = new TrayRig(WithShortcut());
        Assert.AreEqual(1, rig.Keys.Registers);
        Assert.IsTrue(rig.Keys.Holds("Ctrl+Alt+L"));
        Assert.AreEqual("Ctrl+Alt+L", rig.App.LastView!.Menu.Shortcut, "beside Lock now");
        rig.App.ConfigEdited(Edited(rig, c => c.LockShortcut = ""));
        Assert.IsNull(rig.Keys.Held);
        Assert.AreEqual(1, rig.Keys.Unregisters);
        Assert.IsNull(rig.App.LastView!.Menu.Shortcut, "nothing beside Lock now");
    }

    // The window tries a shortcut through ShortcutChanged and reads the result at once; nothing is
    // saved by it.
    [TestMethod]
    public void AShortcutTriedIsDoneWhenTheCallReturns()
    {
        using var rig = new TrayRig(WithShortcut(), before: r => r.Keys.Error = s => s.Key == Keys.J ? FakeKeys.Taken : 0);
        ISettingsHost host = rig.App;
        int raised = 0;
        host.ShortcutResultChanged += () => raised++;
        host.ShortcutChanged("Ctrl+Alt+K");
        Assert.AreEqual(HotKeyResult.Ok, host.ShortcutResult);
        Assert.IsTrue(rig.Keys.Holds("Ctrl+Alt+K"));
        Assert.AreEqual(1, raised);
        host.ShortcutChanged("Ctrl+Alt+J");
        Assert.AreEqual(HotKeyResult.InUse, host.ShortcutResult);
        Assert.AreEqual(2, raised);
        Assert.AreEqual("Ctrl+Alt+L", rig.Saved().LockShortcut);
    }

    // Taken by another app: the shortcut's row, on the Locking tab, says so.
    [TestMethod]
    public void ATakenShortcutShowsInTheLockingRow()
    {
        using var rig = new TrayRig(WithShortcut(), before: r => r.Keys.Error = _ => FakeKeys.Taken);
        Assert.AreEqual(HotKeyResult.InUse, rig.App.ShortcutResult);
        var form = OpenSettings(rig);
        Choose(form.Tabs, 4);
        Assert.IsEmpty(All<ShortcutNote>(form.PageControl!), "no shortcut on Extras");
        Choose(form.Tabs, 0);
        var note = All<ShortcutNote>(form.PageControl!).Single();
        Assert.AreEqual(string.Format(V.ShortcutRowInUse, Shortcut.Default.Display), note.Text);
    }

    // A keyboard layout that types the shortcut turns it off (D9): found when the settings open, on
    // the next refresh after the layout is added, and when the file changes.
    [TestMethod]
    public void ALayoutThatTypesTheShortcutTurnsItOff()
    {
        using (var rig = new TrayRig(WithShortcut()))
        {
            rig.Layouts.List.Add(FakeLayouts.Polish);
            rig.App.OpenSettings();
            Assert.AreEqual(HotKeyResult.TypesALetter, rig.App.ShortcutResult, "checked as the settings open");
            Assert.IsNull(rig.Keys.Held);
        }
        using (var rig = new TrayRig(WithShortcut()))
        {
            rig.Second();
            Assert.IsTrue(rig.Keys.Holds("Ctrl+Alt+L"));
            rig.Layouts.List.Add(FakeLayouts.Polish);
            rig.Second();
            Assert.AreEqual(HotKeyResult.TypesALetter, rig.App.ShortcutResult, "checked on the next refresh");
            Assert.IsNull(rig.Keys.Held);
            rig.Layouts.List.Remove(FakeLayouts.Polish);
            rig.Second();
            Assert.IsTrue(rig.Keys.Holds("Ctrl+Alt+L"));

            // A hand edit applies the shortcut again.
            Welcomed(c => c.LockShortcut = "Ctrl+Alt+K").Save(rig.ConfigPath);
            rig.App.ReloadConfig(true);
            Assert.IsTrue(rig.Keys.Holds("Ctrl+Alt+K"));
        }
    }

    // The settings window opens on the tab used last, Locking the first time. Going to a tab saves it
    // through the usual save, with nothing applied again; an open window keeps its tab when it is
    // brought back, and a place that names a tab goes to it.
    [TestMethod]
    public void TheSettingsOpenOnTheTabUsedLast()
    {
        using var rig = new TrayRig();
        var form = OpenSettings(rig);
        Assert.AreEqual(0, form.Tab, "Locking the first time");
        int applied = rig.App.Applied;
        string before = File.ReadAllText(rig.ConfigPath);

        Choose(form.Tabs, 2);
        Assert.AreEqual(2, rig.App.Config.SettingsTab);
        Assert.AreEqual(2, rig.Saved().SettingsTab, "saved");
        Assert.AreEqual(applied, rig.App.Applied, "nothing applied again");
        Assert.IsFalse(form.Pending);
        var saved = rig.Saved();
        saved.SettingsTab = 0;
        Assert.IsTrue(saved.SameAs(Config.Parse(before, rig.ConfigPath, out _, out _)), "only the tab changed in the file");

        // Brought back while open: the same tab.
        Choose(form.Tabs, 4);
        rig.App.OpenSettings();
        Assert.AreSame(form, rig.App.Settings);
        Assert.AreEqual(4, form.Tab);

        // Closed and opened again: the tab used last.
        form.Close();
        form.Dispose();
        var again = OpenSettings(rig);
        Assert.AreNotSame(form, again);
        Assert.AreEqual(4, again.Tab);

        // Named by a place in the app: that tab, open or not, and kept.
        rig.App.OpenSettings(1);
        Assert.AreEqual(1, again.Tab);
        again.Close();
        again.Dispose();
        rig.App.OpenSettings(3);
        Assert.AreEqual(3, rig.App.Settings!.Tab);
        Assert.AreEqual(3, rig.Saved().SettingsTab);
        Assert.AreEqual(applied, rig.App.Applied, "still nothing applied again");
    }

    // A tab is no reason to write over a file that did not load: kept in memory until a real save.
    [TestMethod]
    public void ATabChosenNeverWritesOverABrokenFile()
    {
        using var rig = new TrayRig(new Config(), configBroken: true);
        const string broken = "{ \"configVersion\": 1, \"idleMinutes\": ";
        File.WriteAllText(rig.ConfigPath, broken);
        var form = OpenSettings(rig);
        Choose(form.Tabs, 3);
        Assert.AreEqual(broken, File.ReadAllText(rig.ConfigPath), "untouched");
        Assert.AreEqual(3, rig.App.Config.SettingsTab);
        rig.App.ConfigEdited(Edited(rig, c => c.IdleMinutes = 20));
        Assert.AreEqual(3, rig.Saved().SettingsTab, "the next save writes it");
    }

    // Pressing the shortcut is Lock now: one away, locking, and no lock asked of Windows in a dry run.
    [TestMethod]
    public void TheShortcutLocksAsLockNowDoes()
    {
        using var rig = new TrayRig(WithShortcut());
        rig.Second();
        TrayWin.Press(rig.App.HotKey!);
        Assert.AreEqual(LockState.Away, rig.App.Status.Kind);
        Assert.AreEqual(0, rig.LockRequests);
        TrayWin.Press(rig.App.HotKey!);
        CollectionAssert.AreEqual(new[] { "away locking" }, rig.Events, "once");
    }

    // "Choose another shortcut…" as it shows (Form.OnLoad), with every keyboard accepting the keys.
    static ShortcutDialog OpenShortcutDialog(TrayRig rig, SettingsForm form)
    {
        var dialog = new ShortcutDialog(rig.App, rig.App.Config.LockShortcut, Lang.Current, _ => HotKeyResult.Ok, () => false) { Owner = form };
        _ = dialog.Handle;
        typeof(Form).GetMethod("OnLoad", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dialog, [EventArgs.Empty]);
        return dialog;
    }

    // While "Choose another shortcut…" is open the app holds no shortcut: Windows hands a registered
    // one to its owner, never to the window in front, so pressing it there would lock the PC and run
    // the away actions (I1). Nothing takes it again until the window closes, and it comes back then
    // when the window saves the same one or is cancelled.
    [TestMethod]
    public void TheShortcutIsLetGoWhileANewOneIsChosen()
    {
        using var rig = new TrayRig(WithShortcut());
        rig.Second();
        var form = OpenSettings(rig);
        Assert.IsTrue(rig.Keys.Holds("Ctrl+Alt+L"));

        var dialog = OpenShortcutDialog(rig, form);
        Assert.IsNull(rig.Keys.Held, "let go as the window shows");
        TrayWin.Press(rig.App.HotKey!);
        Assert.AreNotEqual(LockState.Away, rig.App.Status.Kind, "pressed in the box, not a lock");
        Assert.IsEmpty(rig.Events);
        rig.App.ConfigEdited(Edited(rig, c => c.IdleMinutes = 20));
        rig.App.OpenSettings();
        rig.Layouts.List.Add(new IntPtr(0x04070407));
        rig.Second();
        Assert.IsNull(rig.Keys.Held, "an edit, the window coming to the front and a new keyboard take nothing");
        dialog.Take(Keys.Control | Keys.Alt | Keys.L);
        dialog.Save();
        Assert.AreEqual(DialogResult.OK, dialog.DialogResult);
        dialog.Close();
        Assert.IsTrue(rig.Keys.Holds("Ctrl+Alt+L"), "the same one saved: back as it closes");
        dialog.Dispose();

        dialog = OpenShortcutDialog(rig, form);
        Assert.IsNull(rig.Keys.Held);
        dialog.Close();
        Assert.IsTrue(rig.Keys.Holds("Ctrl+Alt+L"), "cancelled: back");
        dialog.Dispose();
        TrayWin.Press(rig.App.HotKey!);
        Assert.AreEqual(LockState.Away, rig.App.Status.Kind, "locks again once the window is gone");
    }

    // A new one saved is held at once and kept; one another app has is let go again, and the window
    // stays open holding nothing; settings applied while it was open are taken as it closes.
    [TestMethod]
    public void AShortcutChosenIsKeptAndOneTakenIsLetGo()
    {
        using var rig = new TrayRig(before: r => r.Keys.Error = s => s.Key == Keys.J ? FakeKeys.Taken : 0);
        var form = OpenSettings(rig);
        var dialog = OpenShortcutDialog(rig, form);
        dialog.Take(Keys.Control | Keys.Alt | Keys.J);
        dialog.Save();
        Assert.AreEqual(DialogResult.None, dialog.DialogResult, "taken by another app: stays open");
        Assert.IsNull(rig.Keys.Held, "and holds nothing");
        TrayWin.Press(rig.App.HotKey!);
        Assert.IsEmpty(rig.Events);
        dialog.Take(Keys.Control | Keys.Alt | Keys.K);
        dialog.Save();
        Assert.AreEqual(DialogResult.OK, dialog.DialogResult);
        dialog.Close();
        dialog.Dispose();
        Assert.IsTrue(rig.Keys.Holds("Ctrl+Alt+K"), "the new one, kept");

        // The file changed while the window was open: its shortcut is taken as the window closes.
        dialog = OpenShortcutDialog(rig, form);
        Welcomed(c => c.LockShortcut = "Ctrl+Alt+P").Save(rig.ConfigPath);
        rig.App.ReloadConfig(true);
        Assert.IsNull(rig.Keys.Held);
        dialog.Close();
        dialog.Dispose();
        Assert.IsTrue(rig.Keys.Holds("Ctrl+Alt+P"));
    }

    // Settings applied while the window is open, then a new one saved: the new one is held as the
    // window closes, not the one in the settings applied meanwhile, which the page's edit replaces
    // a moment later. Pressed between Save and the window going, the new one was meant for the box.
    [TestMethod]
    public void ANewShortcutSavedAfterSettingsWereAppliedIsHeldAsTheWindowCloses()
    {
        using var rig = new TrayRig();
        rig.Second();
        var form = OpenSettings(rig);
        var dialog = OpenShortcutDialog(rig, form);
        rig.App.ConfigEdited(Edited(rig, c => c.IdleMinutes = 20));
        dialog.Take(Keys.Control | Keys.Alt | Keys.K);
        dialog.Save();
        Assert.AreEqual(DialogResult.OK, dialog.DialogResult);
        Assert.IsTrue(rig.Keys.Holds("Ctrl+Alt+K"));
        TrayWin.Press(rig.App.HotKey!);
        Assert.AreNotEqual(LockState.Away, rig.App.Status.Kind, "pressed before the window is gone: not a lock");
        Assert.IsEmpty(rig.Events);

        int registers = rig.Keys.Registers;
        dialog.Close();
        dialog.Dispose();
        Assert.IsTrue(rig.Keys.Holds("Ctrl+Alt+K"), "the new one, held as the window closes");
        Assert.AreEqual(registers, rig.Keys.Registers, "the old one is not taken back in between");
        rig.App.ConfigEdited(Edited(rig, c => c.LockShortcut = "Ctrl+Alt+K"));
        Assert.IsTrue(rig.Keys.Holds("Ctrl+Alt+K"));
        TrayWin.Press(rig.App.HotKey!);
        Assert.AreEqual(LockState.Away, rig.App.Status.Kind, "locks once the window is gone");
    }

    // However the window gets there (a pick, Off, the dialog's OK, a taken one put back, a cancel, a
    // file changed in between, a window closed with an edit waiting), the shortcut held ends as the
    // one in the settings.
    [TestMethod]
    public void TheShortcutHeldAlwaysEndsAsTheSettingsSay()
    {
        using var rig = new TrayRig(before: r => r.Keys.Error = s => s.Key == Keys.J ? FakeKeys.Taken : 0);
        ISettingsHost host = rig.App;
        void Agrees(string what)
        {
            Assert.IsTrue(rig.Keys.Holds(rig.App.Config.LockShortcut), what);
            Assert.AreEqual(HotKeyResult.Ok, host.ShortcutResult, what);
        }
        Agrees("start");
        host.ShortcutChanged("Ctrl+Alt+K");
        host.ConfigEdited(Edited(rig, c => c.LockShortcut = "Ctrl+Alt+K"));
        Agrees("pick");
        host.ShortcutChanged("");
        host.ConfigEdited(Edited(rig, c => c.LockShortcut = ""));
        Agrees("off");
        host.ShortcutChanged("Ctrl+Shift+Alt+F12");
        host.ConfigEdited(Edited(rig, c => c.LockShortcut = "Ctrl+Shift+Alt+F12"));
        Agrees("the dialog's OK");
        host.ShortcutChanged("Ctrl+Alt+J");
        Assert.AreEqual(HotKeyResult.InUse, host.ShortcutResult);
        host.ShortcutChanged(rig.App.Config.LockShortcut);
        Agrees("taken, and the one before put back");
        host.ShortcutChanged("Ctrl+Alt+M");
        host.ShortcutChanged(rig.App.Config.LockShortcut);
        Agrees("cancel");
        host.ShortcutChanged("Ctrl+Alt+N");
        Welcomed(c => c.LockShortcut = "Ctrl+Alt+P").Save(rig.ConfigPath);
        rig.App.ReloadConfig(true);
        Agrees("the file changed while the edit waited");
        host.ConfigEdited(Edited(rig, c => c.LockShortcut = "Ctrl+Alt+N"));
        Agrees("then the edit");
        var form = OpenSettings(rig);
        Pick(form.Main.LockAfter!, "30 minutes");
        form.Close();
        Agrees("closed with an edit waiting");
        Assert.AreEqual(30, rig.Saved().IdleMinutes);
    }

    // ---- The controller ----

    [TestMethod]
    public void TheControllerFollowsTheSetting()
    {
        using var rig = new TrayRig(Welcomed(c => c.CountController = true));
        Assert.IsTrue(rig.App.Controller!.Running);
        Assert.AreSame(rig.Watcher, rig.ControllerNote!.Target, "use counts as the watcher's input");
        Assert.AreEqual(nameof(Watcher.NoteInput), rig.ControllerNote.Method.Name);
        rig.App.ConfigEdited(Edited(rig, c => c.CountController = false));
        Assert.IsFalse(rig.App.Controller.Running);
        Welcomed(c => c.CountController = true).Save(rig.ConfigPath);
        rig.App.ReloadConfig(true);
        Assert.IsTrue(rig.App.Controller.Running, "after a hand edit");
    }

    // ---- Reminders, sounds, coming back ----

    // A break due shows one note and asks for its sound once, though the watcher raises two events
    // for it; Snooze holds the next one back for the snooze time.
    [TestMethod]
    public void ABreakShowsOneNoteAndOneSound()
    {
        using var rig = new TrayRig(Welcomed(c =>
        {
            c.BreakReminders = true;
            c.BreakMinutes = 1;
            c.SnoozeMinutes = 10;
        }));
        for (int i = 0; i < 61; i++) rig.Busy();
        Assert.AreEqual(1, rig.App.Notes.Reminders);
        Assert.AreEqual(1, rig.App.Notes.ReminderSounds);
        Assert.AreEqual(V.TimeForABreak, rig.App.Toast.Title);
        Assert.IsTrue(rig.App.Toast.SnoozeShown);

        rig.App.Toast.PressSnooze();
        for (int i = 0; i < 9 * 60; i++) rig.Busy();
        Assert.AreEqual(1, rig.App.Notes.Reminders, "snoozed");
        for (int i = 0; i < 61; i++) rig.Busy();
        Assert.AreEqual(2, rig.App.Notes.Reminders, "back after the snooze time");
    }

    // Snooze is in the tray menu, first, while a reminder offers it: the note never takes the
    // keyboard and screen readers do not see its button, so the menu is where they reach it (I13).
    // With Snooze off the note has none, and the menu neither.
    [TestMethod]
    public void SnoozeIsInTheTrayMenuWhileAReminderOffersIt()
    {
        foreach (int snooze in new[] { 10, 0 })
        {
            using var rig = new TrayRig(Welcomed(c =>
            {
                c.BreakReminders = true;
                c.BreakMinutes = 1;
                c.SnoozeMinutes = snooze;
            }));
            var menu = rig.App.Menu;
            menu.RaiseOpenedForTests();
            Assert.IsFalse(menu.SnoozeRow.Available, "no reminder, no row");
            menu.RaiseClosedForTests();
            for (int i = 0; i < 61; i++) rig.Busy();
            Assert.AreEqual(V.TimeForABreak, rig.App.Toast.Title);
            menu.RaiseOpenedForTests();
            Assert.AreEqual(snooze > 0, menu.SnoozeRow.Available, $"snooze {snooze}");
            if (snooze == 0) continue;
            Assert.AreEqual(V.SnoozeReminder, menu.SnoozeRow.Text);
            var first = menu.Menu.Items.Cast<ToolStripItem>().First(i => i.Available && i.CanSelect);
            Assert.AreSame(menu.SnoozeRow, first, "the keys start on it");
            Assert.AreEqual(menu.StatusItem.Spoken, menu.SnoozeRow.AccessibleDescription, "it carries the status for screen readers");

            menu.SnoozeRow.PerformClick();
            Assert.IsFalse(rig.App.Toast.IsShowing, "the note goes");
            menu.RaiseOpenedForTests();
            Assert.IsFalse(menu.SnoozeRow.Available, "and the row with it");
            for (int i = 0; i < 9 * 60; i++) rig.Busy();
            Assert.AreEqual(1, rig.App.Notes.Reminders, "snoozed");
            for (int i = 0; i < 61; i++) rig.Busy();
            Assert.AreEqual(2, rig.App.Notes.Reminders, "back after the snooze time");
        }
    }

    [TestMethod]
    public void AnEyeBreakShowsItsOwnNote()
    {
        using var rig = new TrayRig(Welcomed(c => c.EyeBreaks = true));
        for (int i = 0; i < 20 * 60 + 5; i++) rig.Busy();
        Assert.AreEqual(1, rig.App.Notes.Reminders);
        Assert.AreEqual(V.EyeBreakTitle, rig.App.Toast.Title);
    }

    // Not a dry run: the reminder sound plays once when chosen, and not at all when off.
    [TestMethod]
    public void TheReminderSoundPlaysOnceWhenChosen()
    {
        foreach (var (sound, plays) in new[] { (BreakSound.SteppedOut, 1), (BreakSound.Off, 0) })
        {
            using var rig = new TrayRig(Welcomed(c =>
            {
                c.BreakReminders = true;
                c.BreakMinutes = 1;
                c.BreakSound = sound;
            }), dryRun: false);
            for (int i = 0; i < 61; i++) rig.Busy();
            Assert.AreEqual(1, rig.App.Notes.Reminders, sound.ToString());
            Assert.HasCount(plays, rig.Away.Rec.Starting("sound"), sound.ToString());
        }
    }

    // A sound picked in the settings goes to the away actions' player, the fake one here; in a dry
    // run it is only logged.
    [TestMethod]
    public void ASoundPickedInTheSettingsIsPlayedOnceThroughTheAwayActions()
    {
        using (var rig = new TrayRig(Welcomed(), dryRun: false))
        {
            rig.App.PreviewSound(Sound.Of(SoundClip.BreakMeadow));
            CollectionAssert.AreEqual(new[] { "sound BreakMeadow" }, rig.Away.Rec.Starting("sound"));
        }
        using (var rig = new TrayRig(Welcomed(), dryRun: true))
        {
            rig.App.PreviewSound(Sound.Of(SoundClip.BreakMeadow));
            Assert.IsEmpty(rig.Away.Rec.Starting("sound"));
        }
    }

    // Not a dry run: Lock now plays the away sound once and coming back from that lock the back
    // sound. The shortcut and the menu go through the same call, so this covers them too (D12).
    [TestMethod]
    public void LockNowPlaysTheAwaySoundAndTheUnlockTheBackSound()
    {
        foreach (bool sounds in new[] { true, false })
        {
            using var rig = new TrayRig(Welcomed(c =>
            {
                c.SoundWhenAway = sounds ? ActionSound.SteppedOut : ActionSound.Off;
                c.SoundWhenBack = sounds ? ActionSound.Drift : ActionSound.Off;
                c.MediaOnLock = MediaOnLock.KeepPlaying;
            }), dryRun: false);
            rig.Second();
            rig.App.LockNowChosen();
            Assert.AreEqual(1, rig.LockRequests);
            AwayFakes.Wait(rig.App.Actions.Idle(), "away side");
            rig.Watcher.SessionSwitched(SessionSwitchReason.SessionLock);
            rig.Watcher.SessionSwitched(SessionSwitchReason.SessionUnlock);
            AwayFakes.Wait(rig.App.Actions.Idle(), "back side");
            CollectionAssert.AreEqual(sounds ? new[] { "sound AwaySteppedOut", "sound BackDrift" } : Array.Empty<string>(), rig.Away.Rec.Starting("sound"));
        }
    }

    static void LockedFor(TrayRig rig, TimeSpan away, Action? beforeUnlock = null)
    {
        rig.Second();
        rig.App.LockNowChosen();
        AwayFakes.Wait(rig.App.Actions.Idle(), "away side");
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionLock);
        rig.Jump(away);
        beforeUnlock?.Invoke();
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionUnlock);
    }

    // Back from a lock with music paused on the way out: the note says it is playing again. That is
    // known on another thread, so the note comes through Post, and not before.
    [TestMethod]
    public void ComingBackSaysWhenMusicPlaysAgain()
    {
        using var rig = new TrayRig(Welcomed(c => c.ShowTimeAway = true), dryRun: false);
        string before = "";
        LockedFor(rig, TimeSpan.FromMinutes(2), () => before = rig.App.Toast.Title);
        Assert.AreEqual(before, rig.App.Toast.Title, "nothing until the posted work runs");
        Assert.IsTrue(SpinWait.SpinUntil(() =>
        {
            lock (rig.Posted) return rig.Posted.Count > 0;
        }, TimeSpan.FromSeconds(10)));
        bool fromPool;
        lock (rig.Posted) fromPool = rig.Posted[0].FromPool;
        Assert.IsTrue(fromPool, "posted from the thread that knew");
        rig.Drain();
        Assert.AreEqual(1, rig.App.Notes.BackNotes);
        Assert.AreEqual(1, rig.App.Notes.BackNotesComing);
        Assert.AreEqual(Lang.Current.YouWereAwayFor(TimeSpan.FromMinutes(2)), rig.App.Toast.Title);
        Assert.AreEqual(V.MediaPlayingAgain, rig.App.Toast.Detail);
    }

    // A media app that hangs holds the note back for a moment only, which then shows without the line.
    [TestMethod]
    public void AHungMediaAppDelaysTheNoteOnlyAMoment()
    {
        using var rig = new TrayRig(Welcomed(c => c.ShowTimeAway = true), dryRun: false);
        using var hung = new ManualResetEventSlim(false);
        Stopwatch? clock = null;
        LockedFor(rig, TimeSpan.FromMinutes(2), () =>
        {
            rig.App.Actions.Enqueue(() => hung.Wait(TimeSpan.FromSeconds(10)));
            clock = Stopwatch.StartNew();
        });
        rig.DrainWhen(() => rig.Posted.Count > 0, "the note comes anyway");
        Assert.IsGreaterThanOrEqualTo(1.2, clock!.Elapsed.TotalSeconds);
        Assert.AreEqual(1, rig.App.Notes.BackNotes);
        Assert.AreEqual("", rig.App.Toast.Detail);
        hung.Set();
    }

    // No note when it is turned off or the away was under a minute.
    [TestMethod]
    public void NoNoteForAShortAwayOrWhenTurnedOff()
    {
        foreach (var (show, away) in new[] { (false, TimeSpan.FromMinutes(5)), (true, TimeSpan.FromSeconds(30)) })
        {
            using var rig = new TrayRig(Welcomed(c => c.ShowTimeAway = show), dryRun: false);
            LockedFor(rig, away);
            AwayFakes.Wait(rig.App.Actions.Idle(), "back side");
            // None is on its way, so none can arrive later, up to BackWait from now.
            Assert.AreEqual(0, rig.App.Notes.BackNotesComing, $"{show} {away}");
            Thread.Sleep(100);
            lock (rig.Posted) Assert.IsEmpty(rig.Posted, $"{show} {away}");
            Assert.AreEqual(0, rig.App.Notes.BackNotes);
        }
    }

    // Try it shows the note and never locks; a real countdown wins.
    [TestMethod]
    public void TryItNeverLocks()
    {
        using var rig = new TrayRig(Welcomed(c =>
        {
            c.IdleMinutes = 1;
            c.GraceSeconds = 15;
        }));
        ISettingsHost host = rig.App;
        rig.Second();
        host.TryItChosen();
        Assert.IsTrue(rig.App.Toast.IsPreviewing);
        Assert.AreEqual(0, rig.LockRequests);
        Assert.IsEmpty(rig.Events);
        rig.Until(46);
        Assert.IsTrue(rig.App.Toast.IsCounting, "the countdown took over");
        host.TryItChosen();
        Assert.IsTrue(rig.App.Toast.IsCounting);
        Assert.IsFalse(rig.App.Toast.IsPreviewing);
        Assert.IsEmpty(rig.Events);
    }

    // ---- The look and the language ----

    // A dark theme picked in the window: the menu restyles and the window is built again in place.
    [TestMethod]
    public void ADarkThemeRestylesTheMenuAndTheWindowInPlace()
    {
        using var rig = new TrayRig(Welcomed(c => c.Theme = ThemeChoice.Light));
        Assert.IsFalse(Theme.Current.IsDark);
        var form = OpenSettings(rig);
        Choose(form.Tabs, 2);
        var oldRenderer = rig.App.Menu.Menu.Renderer;
        rig.App.ConfigEdited(Edited(rig, c => c.Theme = ThemeChoice.Dark));
        Assert.IsTrue(Theme.Current.IsDark);
        Assert.AreNotSame(oldRenderer, rig.App.Menu.Menu.Renderer, "the menu is drawn in the new colors");
        Application.DoEvents();
        Assert.AreSame(form, rig.App.Settings);
        Assert.AreEqual(2, form.Tab);
        Assert.AreSame(Theme.Current, Field(typeof(SettingsForm), "builtTheme").GetValue(form));
        Assert.AreEqual(ThemeChoice.Dark, rig.Saved().Theme);
    }

    // Arabic from the window's pill: the menu reads right to left, has no language row, and the window
    // is built again in place.
    [TestMethod]
    public void ArabicRelabelsTheMenuAndTheWindowInPlace()
    {
        using var rig = new TrayRig();
        var form = OpenSettings(rig);
        Choose(form.Pills, 1);
        Assert.AreEqual(Arabic.Text, Lang.Current);
        var menu = rig.App.Menu;
        Assert.AreEqual(RightToLeft.Yes, menu.Menu.RightToLeft);
        Assert.AreEqual(Arabic.Text.V1.LockNow, menu.LockNowRow.Text);
        var words = menu.Menu.Items.Cast<ToolStripItem>().Concat(menu.PauseChoices).Select(i => i.Text).ToList();
        CollectionAssert.DoesNotContain(words, English.Text.V1.Language);
        CollectionAssert.DoesNotContain(words, Arabic.Text.V1.Language);
        Application.DoEvents();
        Assert.AreSame(form, rig.App.Settings);
        Assert.IsFalse(form.IsDisposed);
        Assert.AreEqual("ar", rig.Saved().Language);
        StringAssert.StartsWith(rig.Icon.Text, "‫");
    }

    // A Windows color change: the menu restyles and the open window is built again; another kind of
    // change does nothing.
    [TestMethod]
    public void AWindowsColorChangeRestylesTheMenuAndTheWindow()
    {
        using var rig = new TrayRig();
        var form = OpenSettings(rig);
        Theme.Refresh();
        bool windowsDark = Theme.Current.IsDark;
        Theme.UseForShots(!windowsDark);
        var oldRenderer = rig.App.Menu.Menu.Renderer;
        rig.App.PreferenceChanged(UserPreferenceCategory.Locale);
        Assert.AreSame(oldRenderer, rig.App.Menu.Menu.Renderer, "not a color change");
        rig.App.PreferenceChanged(UserPreferenceCategory.Color);
        Assert.AreEqual(windowsDark, Theme.Current.IsDark);
        Assert.AreNotSame(oldRenderer, rig.App.Menu.Menu.Renderer);
        Application.DoEvents();
        Assert.AreSame(form, rig.App.Settings);
        Assert.AreSame(Theme.Current, Field(typeof(SettingsForm), "builtTheme").GetValue(form));
    }

    // Minutes on the icon only when chosen, and only while counting toward a lock.
    [TestMethod]
    public void MinutesShowOnTheIconOnlyWhenChosen()
    {
        using var rig = new TrayRig(Welcomed(c =>
        {
            c.MinutesOnTrayIcon = true;
            c.IdleMinutes = 15;
        }));
        rig.Second();
        Assert.AreEqual("15", rig.App.LastView!.Minutes);
        rig.App.PauseChosen(null);
        Assert.IsNull(rig.App.LastView!.Minutes);
        rig.App.ResumeChosen();
        Assert.AreEqual("15", rig.App.LastView!.Minutes);
        rig.App.ConfigEdited(Edited(rig, c => c.MinutesOnTrayIcon = false));
        Assert.IsNull(rig.App.LastView!.Minutes);
        rig.App.ConfigEdited(Edited(rig, c =>
        {
            c.MinutesOnTrayIcon = true;
            c.IdleMinutes = 1;
        }));
        rig.Until(50);
        Assert.AreEqual(TrayIconState.CountingDown, rig.App.LastView!.Icon);
        Assert.AreEqual("1", rig.App.LastView!.Minutes);
    }

    // 200 ticks that each change the minutes on the icon leave the handle counts where they were.
    [TestMethod]
    public void TwoHundredMinuteChangesLeaveNoHandlesBehind()
    {
        using var rig = new TrayRig(Welcomed(c =>
        {
            c.MinutesOnTrayIcon = true;
            c.IdleMinutes = 100;
        }));
        void Tick(int i)
        {
            rig.Idle = i % 95 * 60;
            rig.Second();
        }
        for (int i = 0; i < 20; i++) Tick(i);
        TrayWin.Settle();
        int gdi = TrayWin.Gdi, user = TrayWin.User, shown = rig.Icon.IconsShown;
        for (int i = 20; i < 220; i++) Tick(i);
        TrayWin.Settle();
        Assert.IsGreaterThanOrEqualTo(190, rig.Icon.IconsShown - shown, "the icon changed every tick");
        Assert.IsLessThanOrEqualTo(8, TrayWin.Gdi - gdi, "GDI objects");
        Assert.IsLessThanOrEqualTo(8, TrayWin.User - user, "USER objects");
    }

    [TestMethod]
    public void TheNotePlaceFollowsTheSettings()
    {
        using var rig = new TrayRig();
        rig.App.ConfigEdited(Edited(rig, c => c.NotificationPlace = NotePosition.TopRight));
        Assert.AreEqual(NotePosition.TopRight, rig.App.Toast.Place);
    }

    // ---- Start with Windows ----

    // The Store package asks Windows off the UI thread. The switch never claims Off before the first
    // answer, a click returns at once and the switch stays as clicked, greyed out, until Windows
    // answers; an older read that finishes later never overwrites a newer choice.
    [TestMethod]
    public void AStoreChoiceFinishesLaterWithoutTheSwitchJumpingBack()
    {
        using var rig = new TrayRig(before: r =>
        {
            r.Autostart.Slow = true;
            r.Autostart.Gate.Reset();
        });
        ISettingsHost host = rig.App;
        Assert.AreEqual(AutostartState.Reading, host.StartWithWindowsState);
        var form = OpenSettings(rig);
        var toggle = form.Main.StartWithWindows!;
        Assert.IsFalse(toggle.Enabled, "greyed out until Windows answers");
        int changed = 0;
        host.StartWithWindowsChanged += () => changed++;
        rig.Autostart.Gate.Set();
        rig.DrainWhen(() => rig.Posted.Count > 0, "the first read");
        Assert.AreEqual(AutostartState.Off, host.StartWithWindowsState);
        Assert.AreEqual(1, changed);
        Assert.IsTrue(toggle.Enabled);
        Assert.IsFalse(toggle.Checked);

        rig.Autostart.Gate.Reset();
        var clock = Stopwatch.StartNew();
        toggle.Checked = true;
        Assert.IsLessThan(2, clock.Elapsed.TotalSeconds, "the click does not wait for Windows");
        Assert.AreEqual(AutostartState.Reading, host.StartWithWindowsState);
        Assert.IsTrue(toggle.Checked, "the switch stays as clicked");
        Assert.IsFalse(toggle.Enabled);
        lock (rig.Autostart.Chosen) Assert.IsEmpty(rig.Autostart.Chosen);
        rig.Autostart.Gate.Set();
        rig.DrainWhen(() => rig.Posted.Count > 0, "the choice");
        Assert.AreEqual(AutostartState.On, host.StartWithWindowsState);
        Assert.IsTrue(toggle.Checked);
        Assert.IsTrue(toggle.Enabled);
        CollectionAssert.AreEqual(new[] { true }, rig.Autostart.Chosen);

        // A read asked before a choice comes back first, with the state from before: it is dropped.
        rig.Autostart.Gate.Reset();
        rig.App.Startup.Refresh();
        host.StartWithWindowsChosen(false);
        int before = changed;
        rig.Autostart.Gate.Set();
        AwayFakes.Wait(rig.App.Startup.Pending, "both calls");
        rig.Drain();
        Assert.AreEqual(AutostartState.Off, host.StartWithWindowsState);
        Assert.AreEqual(before + 1, changed, "Reading, then Off, with no On between");
    }

    // Windows' Settings or Task Manager changes start with Windows while the app runs: opening the
    // settings, showing the open window again and coming back to it read it again, and the switch
    // follows.
    [TestMethod]
    public void TheStartupRowFollowsWindowsWhileTheAppRuns()
    {
        using var rig = new TrayRig(before: r => r.Autostart.State = AutostartState.On);
        ISettingsHost host = rig.App;
        var form = OpenSettings(rig);
        var toggle = form.Main.StartWithWindows!;
        Assert.IsTrue(toggle.Checked);

        rig.Autostart.State = AutostartState.Off;
        Activate(form);
        Assert.AreEqual(AutostartState.Off, host.StartWithWindowsState);
        Assert.IsFalse(toggle.Checked, "coming back to the window");

        rig.Autostart.State = AutostartState.On;
        Assert.AreSame(form, OpenSettings(rig));
        Assert.IsTrue(toggle.Checked, "the open window shown again");

        form.Close();
        rig.Autostart.State = AutostartState.Off;
        Assert.IsFalse(OpenSettings(rig).Main.StartWithWindows!.Checked, "a new window");
        Assert.IsEmpty(rig.Posted);
    }

    // The Store package: the note that it is off in Windows goes once the person turns it on there.
    // The read goes through the same queue, the switch keeps what it shows meanwhile, and coming back
    // to the window twice while Windows is still answering asks once.
    [TestMethod]
    public void TheStoreStartupRowFollowsWindowsWhileTheAppRuns()
    {
        using var rig = new TrayRig(before: r =>
        {
            r.Autostart.Slow = true;
            r.Autostart.State = AutostartState.OffInWindows;
        });
        ISettingsHost host = rig.App;
        rig.DrainWhen(() => rig.Posted.Count > 0, "the first read");
        Assert.AreEqual(AutostartState.OffInWindows, host.StartWithWindowsState);
        int reads = rig.Autostart.Reads;

        rig.Autostart.State = AutostartState.On;
        var form = OpenSettings(rig);
        var toggle = form.Main.StartWithWindows!;
        var row = form.Main.Rows[^1];
        Assert.AreEqual(V.StartupOffInWindows, row.Detail, "until Windows answers");
        rig.DrainWhen(() => rig.Posted.Count > 0, "the read on opening");
        Assert.AreEqual(AutostartState.On, host.StartWithWindowsState);
        Assert.IsTrue(toggle.Checked);
        Assert.IsTrue(toggle.Enabled);
        Assert.AreEqual(V.StartWithWindowsDetail, row.Detail);
        Assert.AreEqual(reads + 1, rig.Autostart.Reads);

        rig.Autostart.Gate.Reset();
        rig.Autostart.State = AutostartState.OffInWindows;
        Activate(form);
        Activate(form);
        Assert.AreEqual(AutostartState.On, host.StartWithWindowsState, "not greyed out while Windows answers");
        Assert.IsTrue(toggle.Enabled);
        rig.Autostart.Gate.Set();
        AwayFakes.Wait(rig.App.Startup.Pending, "the read on coming back");
        rig.Drain();
        Assert.AreEqual(AutostartState.OffInWindows, host.StartWithWindowsState);
        Assert.AreEqual(V.StartupOffInWindows, row.Detail);
        Assert.AreEqual(reads + 2, rig.Autostart.Reads, "asked once for the two");
    }

    // As Windows raises it when the window comes to the front.
    static void Activate(Form form) =>
        typeof(Form).GetMethod("OnActivated", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, [EventArgs.Empty]);

    // The normal exe: done when the call returns.
    [TestMethod]
    public void TheRunEntryChoiceIsDoneAtOnce()
    {
        using var rig = new TrayRig();
        ISettingsHost host = rig.App;
        Assert.AreEqual(AutostartState.Off, host.StartWithWindowsState);
        int changed = 0;
        host.StartWithWindowsChanged += () => changed++;
        host.StartWithWindowsChosen(true);
        Assert.IsTrue(host.StartsWithWindows);
        Assert.AreEqual(1, changed);
        Assert.IsEmpty(rig.Posted);
    }

    // ---- The welcome ----

    // Done saves the welcome's choices, gives Windows the start-with-Windows choice, and Open
    // settings opens the settings once the welcome has gone.
    [TestMethod]
    public void TheWelcomeSavesAndGivesWindowsItsChoice()
    {
        using var rig = new TrayRig(new Config { Welcomed = false });
        var welcome = rig.App.Welcome!;
        var fresh = rig.App.Config.Copy();
        fresh.Welcomed = true;
        fresh.IdleMinutes = 30;
        Listeners<Action<Config, bool, bool>>(welcome, "Finished")!(fresh, false, true);
        Assert.IsTrue(rig.Saved().Welcomed);
        Assert.AreEqual(30, rig.Saved().IdleMinutes);
        CollectionAssert.AreEqual(new[] { false }, rig.Autostart.Chosen);
        Assert.IsNull(rig.App.Settings, "not while the welcome is still there");
        welcome.CloseWithoutFinishing();
        rig.Drain();
        Assert.IsNotNull(rig.App.Settings, "Open settings opens them once the welcome has gone");
    }

    // Quitting is not finishing the welcome: nothing is saved, and it shows again next time. The
    // shortcut was never taken (D46).
    [TestMethod]
    public void QuittingWithTheWelcomeOpenFinishesNothing()
    {
        using var rig = new TrayRig(new Config { Welcomed = false });
        var welcome = rig.App.Welcome!;
        rig.App.Quit();
        Assert.IsTrue(welcome.IsDisposed);
        Assert.IsFalse(rig.Saved().Welcomed);
        Assert.IsEmpty(rig.Autostart.Chosen);
        Assert.AreEqual(0, rig.Keys.Registers);
    }

    // D46: on a first run the lock shortcut is taken only once the welcome is finished and saved, so
    // a key press can't lock the PC before the person knows what the app does, even when it is picked
    // in the welcome. A new keyboard layout meanwhile does not take it either.
    [TestMethod]
    public void TheShortcutWaitsForTheWelcome()
    {
        using var rig = new TrayRig(new Config { Welcomed = false, LockShortcut = "Ctrl+Alt+L" });
        Assert.AreEqual(0, rig.Keys.Registers);
        Assert.IsTrue(rig.Keys.Holds(""));

        var welcome = rig.App.Welcome!;
        welcome.GoTo(2);
        // A German layout, which types nothing with Ctrl+Alt+L.
        rig.Layouts.List.Add(new IntPtr(0x04070407));
        rig.App.RefreshView();
        Assert.AreEqual(0, rig.Keys.Registers, "not while the welcome is open, nor for a new layout");

        var fresh = rig.App.Config.Copy();
        fresh.Welcomed = true;
        Listeners<Action<Config, bool, bool>>(welcome, "Finished")!(fresh, true, false);
        Assert.IsTrue(rig.Saved().Welcomed, "saved first");
        Assert.AreEqual(1, rig.Keys.Registers);
        Assert.IsTrue(rig.Keys.Holds("Ctrl+Alt+L"));
    }

    // A file that did not load is no first run: the shortcut is taken at start, as it always was.
    [TestMethod]
    public void ABrokenFileTakesTheShortcutAtStart()
    {
        using var rig = new TrayRig(new Config(), configBroken: true);
        Assert.IsNull(rig.App.Welcome);
        Assert.IsNull(rig.Keys.Held, "the defaults have none");
        rig.App.ConfigEdited(Edited(rig, c => c.LockShortcut = "Ctrl+Alt+L"));
        Assert.IsTrue(rig.Keys.Holds("Ctrl+Alt+L"), "taken at once, with no welcome to wait for");
    }

    // A new install finishes the welcome on its usual choices, where the shortcut is off: none is
    // taken, and the tray menu shows none beside Lock now.
    [TestMethod]
    public void ANewInstallFinishesTheWelcomeWithNoShortcut()
    {
        using var rig = new TrayRig(new Config { Welcomed = false });
        var welcome = rig.App.Welcome!;
        var fresh = rig.App.Config.Copy();
        fresh.Welcomed = true;
        Listeners<Action<Config, bool, bool>>(welcome, "Finished")!(fresh, true, false);
        Assert.IsTrue(rig.Saved().Welcomed);
        Assert.AreEqual("", rig.Saved().LockShortcut);
        Assert.IsNull(rig.Keys.Held);
        Assert.AreEqual(0, rig.Keys.Registers);
        Assert.IsNull(rig.App.LastView!.Menu.Shortcut);
    }

    // Start with Windows chosen in the welcome points the Run value at this copy, even when an older
    // copy in another folder, still on the disk, had its own entry there (PLAN 3Q step 5). Read and
    // written through Autostart itself, on a scratch key in place of the Run key.
    [TestMethod]
    public void FinishingTheWelcomeTakesStartWithWindowsOverFromAnotherCopy()
    {
        string folder = Path.Combine(Path.GetTempPath(), "SteppedOut.Tests", "old-copy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string oldCopy = Path.Combine(folder, "SteppedOut.exe");
        File.WriteAllText(oldCopy, "");
        try
        {
            string entry = $"\"{oldCopy}\"";
            using (var key = Registry.CurrentUser.CreateSubKey(ScratchAutostart.Key))
            {
                key.SetValue(AppInfo.Name, entry);
            }
            using var rig = new TrayRig(new Config { Welcomed = false }, before: r => r.Parts = r.Parts with { Autostart = new ScratchAutostart() });
            Assert.AreEqual(entry, ScratchAutostart.Entry(), "an entry for a copy that is still there is not taken over at start");
            Assert.IsFalse(rig.App.StartsWithWindows, "another copy's entry does not start this one");

            var fresh = rig.App.Config.Copy();
            fresh.Welcomed = true;
            Listeners<Action<Config, bool, bool>>(rig.App.Welcome!, "Finished")!(fresh, true, false);
            Assert.AreEqual(Autostart.Command(), ScratchAutostart.Entry());
            Assert.IsTrue(rig.App.StartsWithWindows);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(ScratchAutostart.Root, throwOnMissingSubKey: false);
            Directory.Delete(folder, recursive: true);
        }
    }

    // Autostart itself on a key of the test's own, never the Run key.
    sealed class ScratchAutostart : IAutostart
    {
        public const string Root = @"Software\SteppedOut.Tests.Welcome";
        public const string Key = Root + @"\Run";

        public static string? Entry()
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(AppInfo.Name) as string;
        }

        public bool Slow => false;

        public AutostartState Read() => Autostart.Read(null, Key);

        public AutostartState Choose(bool on) => Autostart.Choose(on, null, Key);

        public bool PointsAtMissingFile() => Autostart.PointsAtMissingFile(Key);
    }

    // ---- The settings file ----

    // The app's own save is not read back as a change on disk.
    [TestMethod]
    public void TheAppsOwnSaveIsNotReadBack()
    {
        using var rig = new TrayRig();
        rig.App.ConfigEdited(Edited(rig, c => c.IdleMinutes = 30));
        Assert.AreEqual(30, rig.Saved().IdleMinutes);
        int applied = rig.App.Applied;
        rig.App.ReloadConfig(true);
        Assert.AreEqual(applied, rig.App.Applied);
    }

    // D50: a file from before 1.0 put back while the app runs, as an older copy may, does not start
    // the settings over part way through a session. It is kept next to config.json, the settings in
    // use are written back over it, and they apply once; the welcome does not come back.
    [TestMethod]
    public void AFileFromBefore10PutBackKeepsTheSettingsInUse()
    {
        using var rig = new TrayRig(Welcomed(c => c.IdleMinutes = 25));
        const string old = "{ \"language\": \"ar\", \"idleMinutes\": 60, \"whilePlaying\": \"moreTime\", \"welcomed\": true }";
        File.WriteAllText(rig.ConfigPath, old);
        int applied = rig.App.Applied;
        rig.App.ReloadConfig(true);
        Assert.AreEqual(applied + 1, rig.App.Applied);
        Assert.AreEqual(25, rig.App.Config.IdleMinutes);
        Assert.IsTrue(rig.App.Config.Welcomed);
        Assert.IsNull(rig.App.Welcome);
        Assert.AreNotEqual(Arabic.Text, Lang.Current, "nothing in the old file is read");
        Assert.AreEqual(25, rig.Saved().IdleMinutes);
        Assert.AreEqual(old, File.ReadAllText(rig.ConfigPath + FreshStart.BackupSuffix));

        // The change on disk that writing back made is the app's own.
        rig.App.ReloadConfig(true);
        rig.App.ReloadConfig(true);
        Assert.AreEqual(applied + 1, rig.App.Applied);
    }

    // A hand edit is applied and fills the open window.
    [TestMethod]
    public void AHandEditFillsTheOpenWindow()
    {
        using var rig = new TrayRig();
        var form = OpenSettings(rig);
        Welcomed(c => c.IdleMinutes = 45).Save(rig.ConfigPath);
        rig.App.ReloadConfig(true);
        Assert.AreEqual(45, rig.App.Config.IdleMinutes);
        Assert.AreEqual(45, form.Working.IdleMinutes);
        Assert.AreEqual(45m, rig.App.Status.Time.Normal, "the status follows at once");
    }

    // A file that does not load is tried again, then said so in a note; a good file takes the note
    // away; the next save keeps a bad one as config.json.broken.
    [TestMethod]
    public void AFileThatDoesNotLoadIsSaidAndKept()
    {
        using var rig = new TrayRig();
        File.WriteAllText(rig.ConfigPath, "{ \"idleMinutes\": ");
        rig.App.ReloadConfig(true);
        Assert.IsFalse(rig.App.Toast.IsShowing, "tried again first");
        for (int i = 1; i < ConfigReload.Tries; i++) rig.App.ReloadConfig(false);
        Assert.AreEqual(Lang.Current.ConfigNotUsed, rig.App.Toast.Title);
        Welcomed(c => c.IdleMinutes = 20).Save(rig.ConfigPath);
        rig.App.ReloadConfig(true);
        Assert.IsFalse(rig.App.Toast.IsShowing, "the fix takes the note away");
        Assert.AreEqual(20, rig.App.Config.IdleMinutes);

        File.WriteAllText(rig.ConfigPath, "{ \"idleMinutes\": ");
        for (int i = 0; i < ConfigReload.Tries; i++) rig.App.ReloadConfig(i == 0);
        rig.App.ConfigEdited(Edited(rig, c => c.IdleMinutes = 25));
        Assert.IsTrue(File.Exists(rig.ConfigPath + ".broken"));
        Assert.AreEqual(25, rig.Saved().IdleMinutes);
    }

    // --idle is for this run: the status shows it, the file never gets it.
    [TestMethod]
    public void TheIdleOptionIsForThisRunOnly()
    {
        using var rig = new TrayRig(idleOption: 0.5);
        rig.Second();
        Assert.AreEqual(0.5m, rig.App.Status.Time.Normal);
        rig.App.ConfigEdited(Edited(rig, c => c.IdleMinutes = 30));
        Assert.AreEqual(0.5m, rig.App.Status.Time.Normal);
        Assert.AreEqual(30, rig.Saved().IdleMinutes);
        Assert.IsFalse(File.ReadAllText(rig.ConfigPath).Contains("0.5", StringComparison.Ordinal));
    }

    // ---- Keeping the status live ----

    // The rules are asked every second while the menu or the settings window shows the status, and
    // a menu that went away without saying so lets go at the next refresh.
    [TestMethod]
    public void TheStatusIsKeptLiveWhileShown()
    {
        using var rig = new TrayRig();
        Assert.AreEqual(0, rig.Watcher.LiveViews);
        rig.App.Menu.RaiseOpenedForTests();
        Assert.AreEqual(1, rig.Watcher.LiveViews);
        var form = OpenSettings(rig);
        Assert.AreEqual(2, rig.Watcher.LiveViews);
        rig.App.Menu.RaiseClosedForTests();
        Assert.AreEqual(1, rig.Watcher.LiveViews);
        form.Close();
        Assert.AreEqual(0, rig.Watcher.LiveViews);

        rig.App.Menu.RaiseOpenedForTests();
        rig.App.Menu.OpenForTests = false;
        rig.Second();
        Assert.AreEqual(0, rig.Watcher.LiveViews, "gone without Closed");

        rig.App.Menu.RaiseOpenedForTests();
        OpenSettings(rig);
        rig.App.Dispose();
        Assert.AreEqual(0, rig.Watcher.LiveViews);
    }

    // A second start of the exe opens the settings, once, and adds the icon again.
    [TestMethod]
    public void ASecondStartOpensTheSettingsOnce()
    {
        using var rig = new TrayRig();
        rig.Icon.VisibleSets.Clear();
        rig.ShowRequest.Set();
        rig.Second();
        Assert.IsNotNull(rig.App.Settings);
        CollectionAssert.AreEqual(new[] { false, true }, rig.Icon.VisibleSets);
        rig.Second();
        Assert.AreEqual(1, rig.Shown.Count(f => f is SettingsForm));
    }

    // ---- Quitting ----

    // Quit saves an edit still waiting, without applying it, and lets go of everything.
    [TestMethod]
    public void QuitSavesAWaitingEditAndLetsGoOfEverything()
    {
        using var rig = new TrayRig(Welcomed(c => c.CountController = true), before: r => r.StartWatcherForReal = true);
        var form = OpenSettings(rig);
        Pick(form.Main.LockAfter!, "30 minutes");
        Assert.IsTrue(form.Pending);
        int applied = rig.App.Applied;
        rig.App.Quit();
        Assert.AreEqual(30, rig.Saved().IdleMinutes);
        Assert.AreEqual(applied, rig.App.Applied, "nothing applied on the way out");
        Assert.IsTrue(form.IsDisposed);
        Assert.IsNull(rig.Keys.Held);
        Assert.IsFalse(rig.App.Controller!.Running);
        Assert.IsTrue(rig.Icon.Disposed);
        Assert.IsFalse(rig.Icon.Visible);
        Assert.IsTrue(rig.App.Toast.IsDisposed);
        Assert.AreEqual(0, rig.Watcher.LiveViews);
        Assert.IsTrue(File.Exists(Path.Combine(rig.Folder, "today.json")), "today's counts are saved");
        Assert.IsNull(Listeners<Delegate>(rig.Watcher, "ReminderDue"), "the notes let go of the watcher");
        Assert.IsNull(Listeners<Delegate>(rig.Watcher, "Updated"));
    }

    // A dialog over the settings window is closed first; what it asks on its way out, and anything
    // asked after, is answered with nothing.
    [TestMethod]
    public void QuitClosesADialogOverTheSettingsFirst()
    {
        using var rig = new TrayRig();
        var form = OpenSettings(rig);
        var dialog = new ShortcutDialog(rig.App, "Ctrl+Alt+L", Lang.Current) { Owner = form };
        _ = dialog.Handle;
        // As after trying a shortcut: closing puts the one before back through the host.
        Field(typeof(ShortcutDialog), "tried").SetValue(dialog, true);
        CollectionAssert.Contains(form.OwnedForms, dialog);
        int registers = rig.Keys.Registers;
        rig.App.Quit();
        Assert.IsTrue(dialog.IsDisposed);
        Assert.IsTrue(form.IsDisposed);
        Assert.AreEqual(registers, rig.Keys.Registers, "putting the old shortcut back asked nothing");
        rig.App.ShortcutChanged("Ctrl+Alt+K");
        rig.App.PauseChosen(null);
        rig.App.StartWithWindowsChosen(true);
        rig.App.OpenSettings();
        Assert.AreEqual(registers, rig.Keys.Registers);
        Assert.IsFalse(rig.Watcher.IsPaused);
        Assert.IsEmpty(rig.Autostart.Chosen);
        Assert.IsEmpty(rig.App.Explain());
        Assert.IsNull(rig.App.Settings);
    }

    // The likeliest bug: a theme edit waiting as the app quits. It is written, not applied, and
    // nothing is built again afterwards.
    [TestMethod]
    public void QuitWithAThemeEditWaitingRebuildsNothing()
    {
        using var rig = new TrayRig(Welcomed(c => c.Theme = ThemeChoice.Light));
        var form = OpenSettings(rig);
        Choose(form.Tabs, 2);
        var mode = All<Segmented>(form.PageControl!).Single(s => s.Name == "themeMode");
        Choose(mode, 1);
        Assert.IsTrue(form.Pending);
        int registers = rig.Keys.Registers;
        rig.App.Quit();
        Assert.AreEqual(ThemeChoice.Dark, rig.Saved().Theme);
        Assert.IsFalse(Theme.Current.IsDark, "not applied on the way out");
        Assert.AreEqual(registers, rig.Keys.Registers);
        Assert.AreEqual(0, rig.Drain());
        Application.DoEvents();
        Assert.IsTrue(form.IsDisposed);
    }

    // Windows signing out: today's counts are saved as it starts, and an edit waiting with them as it
    // ends, before anything else.
    [TestMethod]
    public void SigningOutSavesTheDayAndAWaitingEdit()
    {
        using var rig = new TrayRig(before: r => r.StartWatcherForReal = true);
        string today = Path.Combine(rig.Folder, "today.json");
        rig.App.SessionEnding();
        Assert.IsTrue(File.Exists(today));
        File.Delete(today);
        var form = OpenSettings(rig);
        Pick(form.Main.LockAfter!, "30 minutes");
        Assert.IsTrue(form.Pending);
        int applied = rig.App.Applied;
        int registers = rig.Keys.Registers;
        rig.App.SessionEnded();
        Assert.IsTrue(File.Exists(today));
        Assert.AreEqual(30, rig.Saved().IdleMinutes);
        // Written only, as on Quit: Windows is shutting down, so nothing is applied to the parts (I40).
        Assert.AreEqual(applied, rig.App.Applied, "not applied on the way out");
        Assert.AreEqual(registers, rig.Keys.Registers, "the shortcut is not taken again");
        Assert.IsTrue(rig.App.Quitting);
        Application.DoEvents();
        Assert.IsTrue(form.IsDisposed);
    }

    // A save Windows refuses (another program holds config.json without sharing) keeps the new
    // settings in use and leaves the file as it was. It is said once, in a note and in the settings
    // window's footer in place of "Changes are saved automatically."; the next save that works
    // takes both back.
    [TestMethod]
    public void ASaveThatFailsIsSaidAndTheNextOneTakesItBack()
    {
        using var rig = new TrayRig();
        var form = OpenSettings(rig);
        Assert.AreEqual(V.AutoSaved, form.FooterLine!.Saved);
        string before = File.ReadAllText(rig.ConfigPath);
        using (new FileStream(rig.ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            rig.App.ConfigEdited(Edited(rig, c => c.IdleMinutes = 30));
            Assert.AreEqual(30, rig.App.Config.IdleMinutes);
            Assert.AreEqual(30m, rig.App.Status.Time.Normal, "in use though not saved");
            Assert.IsTrue(rig.App.SaveFailed);
            Assert.AreEqual(V.SaveFailedTitle, rig.App.Toast.Title);
            Assert.AreEqual(V.SaveFailedFooter, form.FooterLine.Saved);
            StringAssert.StartsWith(form.FooterLine.AccessibleName, V.SaveFailedFooter);

            // A second refused save says nothing new.
            Assert.AreEqual(1, rig.App.SaveFailedNotes);
            rig.App.ConfigEdited(Edited(rig, c => c.IdleMinutes = 31));
            Assert.AreEqual(1, rig.App.SaveFailedNotes, "said once");
            Assert.AreEqual(V.SaveFailedFooter, form.FooterLine.Saved);

            // A window opened meanwhile says so from the start.
            form.Close();
            form = OpenSettings(rig);
            Assert.AreEqual(V.SaveFailedFooter, form.FooterLine!.Saved);
        }
        Assert.AreEqual(before, File.ReadAllText(rig.ConfigPath));
        Assert.IsFalse(Directory.EnumerateFiles(rig.Folder, "~*").Any(), "no file of its own is left");

        rig.App.ConfigEdited(Edited(rig, c => c.IdleMinutes = 35));
        Assert.AreEqual(35, rig.Saved().IdleMinutes);
        Assert.IsFalse(rig.App.SaveFailed);
        Assert.AreEqual(V.AutoSaved, form.FooterLine!.Saved);
        Assert.IsFalse(rig.App.Toast.IsShowing, "the note is taken back");
        Assert.AreEqual(1, rig.App.SaveFailedNotes);
    }

    // After a refused save, a real change on disk puts the file's settings in use, so nothing waits
    // unsaved any more: the note and the footer line are taken back, and a note says the settings
    // that could not be saved were replaced.
    [TestMethod]
    public void AChangeOnDiskAfterASaveThatFailedTakesItBack()
    {
        using var rig = new TrayRig();
        var form = OpenSettings(rig);
        using (new FileStream(rig.ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None))
            rig.App.ConfigEdited(Edited(rig, c => c.IdleMinutes = 30));
        Assert.IsTrue(rig.App.SaveFailed);
        Assert.AreEqual(V.SaveFailedFooter, form.FooterLine!.Saved);
        Assert.IsTrue(rig.App.Toast.IsShowing);

        Welcomed(c => c.IdleMinutes = 45).Save(rig.ConfigPath);
        rig.App.ReloadConfig(true);
        Assert.AreEqual(45, rig.App.Config.IdleMinutes);
        Assert.IsFalse(rig.App.SaveFailed, "the settings in use are the file's");
        Assert.AreEqual(V.AutoSaved, form.FooterLine!.Saved);
        Assert.IsTrue(rig.App.Toast.IsShowing);
        Assert.AreEqual(V.UnsavedReplacedTitle, rig.App.Toast.Title, "the refused save's note gives way to this one");
        Assert.AreEqual(1, rig.App.SaveFailedNotes);
    }

    // After a refused save, a change event on config.json that leaves it as it was, as an antivirus,
    // a backup or a sync program can make, does not put the file's settings back over the ones in
    // use: they still wait unsaved, the note and the footer line stay, and the next save that works
    // writes them.
    [TestMethod]
    public void AFileWrittenAgainAsItWasAfterARefusedSaveLeavesTheSettingsInUse()
    {
        using var rig = new TrayRig();
        var form = OpenSettings(rig);
        string before = File.ReadAllText(rig.ConfigPath);
        using (new FileStream(rig.ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None))
            rig.App.ConfigEdited(Edited(rig, c => c.IdleMinutes = 30));
        Assert.IsTrue(rig.App.SaveFailed);

        rig.App.ReloadConfig(true);
        Assert.AreEqual(30, rig.App.Config.IdleMinutes, "nothing changed");
        File.WriteAllText(rig.ConfigPath, before);
        File.SetLastWriteTimeUtc(rig.ConfigPath, DateTime.UtcNow.AddMinutes(1));
        rig.App.ReloadConfig(true);
        Assert.AreEqual(30, rig.App.Config.IdleMinutes, "the same text written again");
        Assert.AreEqual(30m, rig.App.Status.Time.Normal, "still in use");
        Assert.IsTrue(rig.App.SaveFailed);
        Assert.AreEqual(V.SaveFailedFooter, form.FooterLine!.Saved);
        Assert.AreEqual(V.SaveFailedTitle, rig.App.Toast.Title);

        rig.App.ConfigEdited(Edited(rig, c => c.GraceSeconds = 20));
        Assert.AreEqual(30, rig.Saved().IdleMinutes);
        Assert.AreEqual(20, rig.Saved().GraceSeconds);
        Assert.IsFalse(rig.App.SaveFailed);
    }

    // A change on disk to the very settings in use loses nothing, so it says nothing more.
    [TestMethod]
    public void AChangeOnDiskToTheSettingsInUseAfterARefusedSaveSaysNothingWasReplaced()
    {
        using var rig = new TrayRig();
        using (new FileStream(rig.ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None))
            rig.App.ConfigEdited(Edited(rig, c => c.IdleMinutes = 30));
        Assert.IsTrue(rig.App.SaveFailed);
        rig.App.Config.Copy().Save(rig.ConfigPath);
        rig.App.ReloadConfig(true);
        Assert.IsFalse(rig.App.SaveFailed);
        Assert.IsFalse(rig.App.Toast.IsShowing, "the refused save's note is taken back, and no other comes");
    }

    // The watcher's tick keeps failing, so the PC may not lock: a note says so once, stays however
    // long the tick keeps failing, and goes once the tick has worked again for a while
    // (Watcher.TicksFailing, 3I2.1).
    [TestMethod]
    public void ATickThatKeepsFailingIsSaidInANoteThatGoesWhenItWorks()
    {
        using var rig = new TrayRig();
        var toast = rig.App.Toast;
        long later = 0;
        toast.Clock = () => Environment.TickCount64 + later;
        toast.Pointer = () => new Point(-30000, -30000);
        bool broken = false;
        rig.Watcher.Updated += () =>
        {
            if (broken) throw new InvalidOperationException("broken");
        };
        void Seconds(int count)
        {
            for (int i = 0; i < count; i++)
            {
                rig.Jump(TimeSpan.FromSeconds(1));
                rig.Watcher.TimerFired();
            }
        }
        Seconds(3);
        broken = true;
        Seconds(Watcher.FailingAfter - 1);
        Assert.IsFalse(toast.IsShowing, "not yet");
        Seconds(1);
        Assert.IsTrue(toast.IsShowing);
        Assert.AreEqual(V.TicksFailingTitle, toast.Title);
        later = (long)TimeSpan.FromHours(1).TotalMilliseconds;
        toast.Step();
        Assert.IsTrue(toast.IsShowing, "still there an hour on, with the tick still failing");
        broken = false;
        Seconds(Watcher.FailingAfter);
        Assert.IsFalse(toast.IsShowing, "taken back once the tick works again");
    }

    // Clicked away, the note stays away while the tick keeps failing, and comes again only when it
    // starts failing again after working.
    [TestMethod]
    public void ATickFailingNoteClickedAwayComesBackOnlyForTheNextRunOfFailures()
    {
        using var rig = new TrayRig();
        var toast = rig.App.Toast;
        bool broken = true;
        rig.Watcher.Updated += () =>
        {
            if (broken) throw new InvalidOperationException("broken");
        };
        void Seconds(int count)
        {
            for (int i = 0; i < count; i++)
            {
                rig.Jump(TimeSpan.FromSeconds(1));
                rig.Watcher.TimerFired();
            }
        }
        Seconds(Watcher.FailingAfter);
        Assert.AreEqual(V.TicksFailingTitle, toast.IsShowing ? toast.Title : null);
        toast.Clicked(new Point(10, 10));
        Assert.IsFalse(toast.IsShowing, "clicked away");
        Seconds(3 * Watcher.FailingAfter);
        Assert.IsFalse(toast.IsShowing, "not again while the same failures go on");
        broken = false;
        Seconds(Watcher.FailingAfter);
        broken = true;
        Seconds(Watcher.FailingAfter);
        Assert.AreEqual(V.TicksFailingTitle, toast.IsShowing ? toast.Title : null, "again once the tick works and then fails again");
    }

    // Signing out after quitting started does nothing more.
    [TestMethod]
    public void SigningOutAfterQuittingDoesNothing()
    {
        using var rig = new TrayRig();
        rig.App.Quit();
        int applied = rig.App.Applied;
        rig.App.SessionEnding();
        rig.App.SessionEnded();
        Assert.AreEqual(applied, rig.App.Applied);
    }

    // Windows' notices are listened to through SystemEvents when the parts ask for it, and let go of
    // on quitting, whether from the menu or because Windows signs out.
    [TestMethod]
    public void WindowsNoticesAreListenedToAndLetGoOfOnQuitting()
    {
        foreach (bool signOut in new[] { false, true })
        {
            using var rig = new TrayRig(before: r => r.Parts = r.Parts with { ListenToWindows = true });
            Assert.IsTrue(rig.App.Listening);
            if (signOut) rig.App.SessionEnded();
            else rig.App.Quit();
            Assert.IsFalse(rig.App.Listening);
        }
        using var quiet = new TrayRig();
        Assert.IsFalse(quiet.App.Listening, "the rig's parts listen to nothing");
    }

    // Disposed twice is fine, and work that finishes on another thread afterwards does nothing.
    [TestMethod]
    public void LateWorkAfterDisposeDoesNothing()
    {
        using var rig = new TrayRig(Welcomed(c => c.ShowTimeAway = true), dryRun: false, before: r => r.Autostart.Slow = true);
        rig.DrainWhen(() => rig.Posted.Count > 0, "the first read");
        using var hung = new ManualResetEventSlim(false);
        LockedFor(rig, TimeSpan.FromMinutes(2), () => rig.App.Actions.Enqueue(() => hung.Wait(TimeSpan.FromSeconds(10))));
        rig.Autostart.Gate.Reset();
        rig.App.StartWithWindowsChosen(true);
        rig.App.Dispose();
        rig.App.Dispose();
        hung.Set();
        rig.Autostart.Gate.Set();
        AwayFakes.Wait(rig.App.Startup.Pending, "the choice finished");
        Thread.Sleep((int)TrayNotes.BackWait.TotalMilliseconds + 300);
        lock (rig.Posted) Assert.IsEmpty(rig.Posted);
    }

    // Work posted before Quit that runs after it does nothing: the coming-back note waits in the
    // queue, Quit runs, then the queue runs.
    [TestMethod]
    public void WorkPostedBeforeQuitDoesNothingAfterIt()
    {
        using var rig = new TrayRig(Welcomed(c => c.ShowTimeAway = true), dryRun: false);
        LockedFor(rig, TimeSpan.FromMinutes(2));
        Assert.IsTrue(SpinWait.SpinUntil(() =>
        {
            lock (rig.Posted) return rig.Posted.Count > 0;
        }, TimeSpan.FromSeconds(10)), "the note was posted");
        rig.App.Quit();
        Assert.AreEqual(1, rig.Drain());
        Assert.AreEqual(0, rig.App.Notes.BackNotes);
    }

    // 50 starts and quits: nothing thrown on the UI thread, and the handle counts come back.
    [TestMethod]
    public void FiftyStartsAndQuitsLeaveNothingBehind()
    {
        var thrown = new List<Exception>();
        ThreadExceptionEventHandler onUi = (_, e) => thrown.Add(e.Exception);
        Application.ThreadException += onUi;
        try
        {
            void Once()
            {
                using var rig = new TrayRig();
                rig.Second();
                rig.App.OpenSettings();
                rig.App.Menu.RaiseOpenedForTests();
                rig.App.Quit();
                rig.Drain();
                Application.DoEvents();
            }
            Once();
            Once();
            TrayWin.Settle();
            int gdi = TrayWin.Gdi, user = TrayWin.User, handles = Process.GetCurrentProcess().HandleCount;
            for (int i = 0; i < 50; i++) Once();
            TrayWin.Settle();
            Assert.IsEmpty(thrown);
            Assert.IsLessThanOrEqualTo(16, TrayWin.Gdi - gdi, "GDI objects");
            Assert.IsLessThanOrEqualTo(16, TrayWin.User - user, "USER objects");
            Assert.IsLessThanOrEqualTo(64, Process.GetCurrentProcess().HandleCount - handles, "handles");
        }
        finally
        {
            Application.ThreadException -= onUi;
        }
    }

    // ---- The groundwork ----

    // For Windows signing out, which can end the process before it quits: the day's counts are on
    // disk at once.
    [TestMethod]
    public void SaveStatsWritesTheDayNow()
    {
        string folder = Path.Combine(Path.GetTempPath(), "SteppedOut-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "today.json");
        try
        {
            var world = new FakeWorld();
            var watcher = new Watcher(new Config(), true, new WatchSeams
            {
                Sample = () => new InputSample(0, 0, false),
                Monotonic = () => TimeSpan.FromHours(1),
                Wall = () => new DateTime(2026, 10, 20, 9, 0, 0),
                RequestLock = () => false,
                AcceptsNotifications = () => true,
                SessionIsLocked = () => false,
                Sources = world.Sources,
                StatsPath = path,
            });
            try
            {
                // Start reads the day's file, as the tray does; no message loop runs, so no tick.
                watcher.Start();
                Assert.IsFalse(File.Exists(path));
                watcher.SaveStats();
                Assert.IsTrue(File.Exists(path));
            }
            finally
            {
                watcher.Dispose();
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // A change on disk that was posted just before the app closed never starts a reload after it.
    [TestMethod]
    public void AClosedConfigWatcherNeverStartsAgain()
    {
        var before = SynchronizationContext.Current;
        string folder = Path.Combine(Path.GetTempPath(), "SteppedOut-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        // Not disposed: its control is the one every window message loop context on this thread
        // shares, and a later test's window could no longer post through it.
        var context = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            int changed = 0;
            using (var open = new ConfigWatcher(Path.Combine(folder, "config.json")))
            {
                open.BumpForTest();
                Application.DoEvents();
                Assert.IsTrue(open.Waiting, "a change on disk starts the wait");
            }

            var watcher = new ConfigWatcher(Path.Combine(folder, "config.json"));
            watcher.Changed += _ => changed++;
            watcher.BumpForTest();
            watcher.Dispose();
            Application.DoEvents();
            Assert.IsFalse(watcher.Waiting, "the change posted before Dispose");
            watcher.TryAgain();
            watcher.BumpForTest();
            Application.DoEvents();
            Assert.IsFalse(watcher.Waiting);
            Assert.AreEqual(0, changed);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(before);
            Directory.Delete(folder, recursive: true);
        }
    }

    // The Start with Windows row shows the state as Windows has it: greyed out with the reason when
    // Windows or a policy holds the choice, back to normal when it no longer does, and as it was,
    // greyed out, while Windows has not answered yet.
    [TestMethod]
    public void TheStartupRowShowsWhoHoldsTheChoice()
    {
        var text = English.Text;
        var state = AutostartState.OffByPolicy;
        var list = new MainList(() => state);
        using var built = list.Build(new Config(), text);
        var row = list.Rows[^1];
        Assert.IsFalse(list.StartWithWindows!.Checked);
        Assert.IsFalse(list.StartWithWindows.Enabled);
        Assert.AreEqual(text.V1.StartupOffByPolicy, row.Detail);

        state = AutostartState.OnByPolicy;
        list.Refill(new Config());
        Assert.IsTrue(list.StartWithWindows.Checked);
        Assert.IsFalse(list.StartWithWindows.Enabled);
        Assert.AreEqual(text.V1.StartupOnByPolicy, row.Detail);

        state = AutostartState.On;
        list.Refill(new Config());
        Assert.IsTrue(list.StartWithWindows.Checked);
        Assert.IsTrue(list.StartWithWindows.Enabled);
        Assert.AreEqual(text.V1.StartWithWindowsDetail, row.Detail);

        state = AutostartState.Reading;
        list.Refill(new Config());
        Assert.IsTrue(list.StartWithWindows.Checked, "kept as it was");
        Assert.IsFalse(list.StartWithWindows.Enabled);
        Assert.AreEqual(text.V1.StartWithWindowsDetail, row.Detail);
    }
}
