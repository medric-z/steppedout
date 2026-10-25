using Microsoft.Win32;

namespace SteppedOut.Tests;

// The Extras tab's rows that reach past the settings: "At the lock time" in the running app,
// "Show the welcome again", and "Remove SteppedOut's files" with what it may delete. Every part is
// a stand-in and every folder is the test's own: nothing locks, sleeps, or touches the real
// settings folder or Run value.
[TestClass]
[DoNotParallelize]
public class ExtrasAppTests
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

    static Config Welcomed(Action<Config>? change = null)
    {
        var config = new Config { Welcomed = true };
        change?.Invoke(config);
        return config;
    }

    static Config Sleeps() => Welcomed(c =>
    {
        c.IdleMinutes = 1;
        c.GraceSeconds = 15;
        c.MusicVideo = Effect.None;
        c.FullScreen = Effect.None;
        c.OnCall = Effect.None;
        c.MediaOnLock = MediaOnLock.KeepPlaying;
        c.SoundWhenAway = ActionSound.SteppedOut;
        c.SoundWhenBack = ActionSound.Drift;
        c.WhenLocking = WhenLocking.LockSleep;
    });

    // ---- When it's time to lock ----

    // Not a dry run: the lock at the lock time sleeps once the away sound has played, and after the
    // wake the away from before the sleep stays as it is (D47), the back actions run when you are
    // back, and the whole lock time runs again before the next lock.
    [TestMethod]
    public void TheLockAtTheLockTimeSleepsAndTheWakeFollowsTheUsualRules()
    {
        using var rig = new TrayRig(Sleeps(), dryRun: false);
        rig.Until(61);
        Assert.AreEqual(1, rig.LockRequests);
        AwayFakes.Wait(rig.App.Actions.Idle(), "away side");
        CollectionAssert.AreEqual(new[] { "sound AwaySteppedOut waited for", "sleep" }, rig.Away.Rec.Events);

        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionLock);
        rig.Watcher.PowerChanged(PowerModes.Suspend);
        rig.Jump(TimeSpan.FromHours(1));
        rig.Idle += 3600;
        rig.Watcher.PowerChanged(PowerModes.Resume);
        rig.Second();
        Assert.AreEqual(LockState.SessionLocked, rig.Watcher.LockStatus.Kind, "still locked after the wake");
        Assert.AreEqual(0, rig.Events.Count(e => e == "back"));

        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionUnlock);
        rig.Busy();
        AwayFakes.Wait(rig.App.Actions.Idle(), "back side");
        CollectionAssert.AreEqual(new[] { "sound AwaySteppedOut waited for", "sleep", "sound BackDrift" }, rig.Away.Rec.Events);
        Assert.AreEqual(1, rig.Events.Count(e => e == "back"));
        Assert.AreEqual(1, rig.Watcher.LockStatus.Today.Locks);
        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromHours(1), rig.Watcher.LockStatus.Today.Away, "the sleep inside the away is time away");

        rig.Until(59);
        Assert.AreEqual(1, rig.LockRequests, "the whole lock time again");
        rig.Until(61);
        Assert.AreEqual(2, rig.LockRequests);
    }

    // Lock now, the shortcut and the menu go through the same call: no sleep, and the away sound is
    // not held for one.
    [TestMethod]
    public void LockNowNeverSleeps()
    {
        using var rig = new TrayRig(Sleeps(), dryRun: false);
        rig.Second();
        rig.App.LockNowChosen();
        AwayFakes.Wait(rig.App.Actions.Idle(), "away side");

        Assert.AreEqual(1, rig.LockRequests);
        CollectionAssert.AreEqual(new[] { "sound AwaySteppedOut" }, rig.Away.Rec.Events);
        Assert.AreEqual(0, rig.Away.Sleeper.Looks);
    }

    // A lock Windows makes (Win+L) is not an away of SteppedOut's at all.
    [TestMethod]
    public void ALockWindowsMakesNeverSleeps()
    {
        using var rig = new TrayRig(Sleeps(), dryRun: false);
        rig.Second();
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionLock);
        rig.Second();
        AwayFakes.Wait(rig.App.Actions.Idle(), "away side");

        CollectionAssert.DoesNotContain(rig.Away.Rec.Events, "sleep");
        Assert.AreEqual(0, rig.Away.Sleeper.Looks);
    }

    // A choice made in the settings applies to the next lock.
    [TestMethod]
    public void TheChoiceAppliesOnceEdited()
    {
        using var rig = new TrayRig(Welcomed(c =>
        {
            c.IdleMinutes = 1;
            c.MediaOnLock = MediaOnLock.KeepPlaying;
        }), dryRun: false);
        var edited = rig.App.Config.Copy();
        edited.WhenLocking = WhenLocking.LockSleep;
        rig.App.ConfigEdited(edited);
        rig.Until(61);
        AwayFakes.Wait(rig.App.Actions.Idle(), "away side");

        CollectionAssert.AreEqual(new[] { "sleep" }, rig.Away.Rec.Events);
        Assert.AreEqual(WhenLocking.LockSleep, rig.Saved().WhenLocking);
    }

    // ---- Show the welcome again ----

    // One window at a time: the settings window closes first and the welcome opens on the settings
    // in use. Asked again, the same welcome comes forward. Closing it keeps what it shows.
    [TestMethod]
    public void ShowTheWelcomeAgainClosesTheSettingsAndOpensTheWelcome()
    {
        using var rig = new TrayRig(Welcomed(c =>
        {
            c.IdleMinutes = 20;
            c.WhenLocking = WhenLocking.LockScreenOff;
        }));
        rig.App.OpenSettings();
        var settings = rig.App.Settings!;
        ISettingsHost host = rig.App;

        host.ShowWelcomeChosen();
        Assert.IsNull(rig.App.Welcome, "posted, not run inside the click");
        rig.Drain();

        Assert.IsTrue(settings.IsDisposed);
        Assert.IsNull(rig.App.Settings);
        var welcome = rig.App.Welcome!;
        Assert.IsFalse(welcome.IsDisposed);

        host.ShowWelcomeChosen();
        rig.Drain();
        rig.App.OpenSettings();
        Assert.AreSame(welcome, rig.App.Welcome);
        Assert.IsNull(rig.App.Settings, "the settings wait for the welcome");
        Assert.AreEqual(1, rig.Shown.OfType<WelcomeForm>().Distinct().Count(), "one welcome");

        welcome.Close();
        rig.Drain();
        var saved = rig.Saved();
        Assert.IsTrue(saved.Welcomed);
        Assert.AreEqual(20, saved.IdleMinutes);
        Assert.AreEqual(WhenLocking.LockScreenOff, saved.WhenLocking, "what the welcome does not show stays");
    }

    // ---- Remove SteppedOut's files ----

    static readonly string[] Own =
    [
        "SteppedOut.log", "SteppedOut.old.log", "config.json.broken", "config.json.before-1.0", "config.json.before-1.0.20261001-0900",
        "config.json.before-1.0.20261001-0900-2", "~config.json.tmp", "muted-by-SteppedOut",
    ];

    static readonly string[] Strangers = ["notes.txt", "SteppedOut.log.txt", "config.json.bak", "~notes.txt.tmp"];

    static void Put(string folder, IEnumerable<string> names)
    {
        foreach (string name in names) File.WriteAllText(Path.Combine(folder, name), "x");
    }

    static List<string> Left(string folder) =>
        Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(folder, path)).Order(StringComparer.OrdinalIgnoreCase).ToList();

    [TestMethod]
    public void RemoveDeletesOnlyItsOwnFilesTurnsOffStartWithWindowsAndQuits()
    {
        using var rig = new TrayRig(Welcomed(), dryRun: false);
        rig.Until(30);
        Put(rig.Folder, Own);
        Put(rig.Folder, Strangers);
        Directory.CreateDirectory(Path.Combine(rig.Folder, "inside"));
        File.WriteAllText(Path.Combine(rig.Folder, "inside", "config.json"), "{}");
        ISettingsHost host = rig.App;

        host.RemoveFilesChosen();
        Assert.IsFalse(rig.App.Quitting, "posted, not run inside the click");
        rig.Drain();
        // Whatever the app still holds lets go now, as it would on the way out: nothing comes back.
        rig.Watcher.Dispose();

        Assert.IsTrue(rig.App.Quitting);
        CollectionAssert.AreEqual(new[] { false }, rig.Autostart.Chosen);
        var left = Left(rig.Folder);
        CollectionAssert.AreEqual(new[] { "config.json.bak", "inside", Path.Combine("inside", "config.json"), "notes.txt", "SteppedOut.log.txt", "~notes.txt.tmp" }
            .Order(StringComparer.OrdinalIgnoreCase).ToList(), left);
    }

    [TestMethod]
    public void RemoveDeletesTheFolderWhenNothingElseIsInIt()
    {
        using var rig = new TrayRig(Welcomed(), dryRun: false);
        rig.Until(30);
        Put(rig.Folder, Own);
        Assert.IsTrue(File.Exists(rig.ConfigPath));
        ISettingsHost host = rig.App;

        host.RemoveFilesChosen();
        rig.Drain();
        rig.Watcher.Dispose();

        Assert.IsFalse(Directory.Exists(rig.Folder));
    }

    // A waiting change in the settings window is saved on the way out like any quit, then removed.
    [TestMethod]
    public void RemoveWithTheSettingsOpenLeavesNothingBehind()
    {
        using var rig = new TrayRig(Welcomed(), dryRun: false);
        rig.App.OpenSettings();
        var settings = rig.App.Settings!;
        ISettingsHost host = rig.App;

        host.RemoveFilesChosen();
        rig.Drain();
        rig.Watcher.Dispose();

        Assert.IsTrue(settings.IsDisposed);
        Assert.IsFalse(Directory.Exists(rig.Folder));
    }

    [TestMethod]
    public void TheDryRunRemovesNothingAndQuits()
    {
        using var rig = new TrayRig(Welcomed());
        Put(rig.Folder, Own);
        var before = Left(rig.Folder);
        ISettingsHost host = rig.App;

        host.RemoveFilesChosen();
        rig.Drain();

        Assert.IsTrue(rig.App.Quitting);
        CollectionAssert.IsSubsetOf(before, Left(rig.Folder));
    }

    [TestMethod]
    public void OwnFilesAreKnownByTheirWholeNames()
    {
        foreach (string name in Own.Concat(["config.json", "today.json", "CONFIG.JSON", "~config.json.before-1.0.tmp", "config.json.before-1.0.20261001-0900-99"]))
        {
            Assert.IsTrue(OwnFiles.IsOwn(name), name);
        }
        foreach (string name in Strangers.Concat(["config.json.before-1.0.2026", "config.json.before-1.0.20261001-0900-100", "today.json.old", "~today.json.tmp", "SteppedOut.exe", "x.config.json"]))
        {
            Assert.IsFalse(OwnFiles.IsOwn(name), name);
        }
        // Every name the app writes, from the code that writes it.
        foreach (string name in new[]
        {
            Path.GetFileName(AppInfo.ConfigPath), Path.GetFileName(AppInfo.LogPath), Path.GetFileName(RollingLog.OldPath(AppInfo.LogPath)),
            DailyStats.FileName, MuteSound.MarkerName, Path.GetFileName(WholeFile.Temporary(AppInfo.ConfigPath)),
            Path.GetFileName(AppInfo.ConfigPath) + FreshStart.BackupSuffix,
        })
        {
            Assert.IsTrue(OwnFiles.IsOwn(name), name);
        }
    }

    [TestMethod]
    public void TheDryRunSaysWhatItWouldDelete()
    {
        string folder = Path.Combine(TestRunFolders.Scratch, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Put(folder, ["config.json", "today.json", "notes.txt"]);
            var lines = new List<string>();

            OwnFiles.WouldRemove(folder, lines.Add);

            CollectionAssert.AreEqual(new[] { "WOULD delete config.json (dry run)", "WOULD delete today.json (dry run)" }, lines);
            CollectionAssert.AreEqual(new[] { "config.json", "notes.txt", "today.json" }, Left(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // The guard, proved on a folder of the test's own standing in for the real one, so not even this
    // test names the real folder: while the tests block it, nothing there is deleted, however the
    // path is written.
    [TestMethod]
    public void TheRealFolderIsNeverTouchedWhileBlocked()
    {
        Assert.IsTrue(SessionLock.Blocked);
        Assert.AreEqual(AppInfo.ConfigDir, OwnFiles.RealFolder(), "the guard is on the app's own folder");

        string folder = Path.Combine(TestRunFolders.Scratch, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var real = OwnFiles.RealFolder;
        try
        {
            Put(folder, ["config.json", "today.json"]);
            OwnFiles.RealFolder = () => folder;
            foreach (string written in new[] { folder, folder + Path.DirectorySeparatorChar, folder.ToUpperInvariant(), Path.Combine(folder, "inside", "..") })
            {
                Assert.ThrowsExactly<InvalidOperationException>(() => OwnFiles.Remove(written, _ => { }), written);
            }
            CollectionAssert.AreEqual(new[] { "config.json", "today.json" }, Left(folder));
        }
        finally
        {
            OwnFiles.RealFolder = real;
            Directory.Delete(folder, recursive: true);
        }
    }

    // While the tests block it, only a folder inside the temp folder is ever emptied. One anywhere
    // else, here one beside the tests themselves, keeps every file, and the app's own folder is
    // refused however it is written; that is only asked, so nothing there is touched.
    [TestMethod]
    public void NothingOutsideTheTempFolderIsRemovedWhileBlocked()
    {
        Assert.IsTrue(SessionLock.Blocked);
        string temp = Path.GetTempPath();
        foreach (string written in new[]
        {
            AppInfo.ConfigDir, AppInfo.ConfigDir + Path.DirectorySeparatorChar, AppInfo.ConfigDir.ToUpperInvariant(),
            Path.Combine(AppInfo.ConfigDir, "inside", ".."), temp, Path.TrimEndingDirectorySeparator(temp) + "-elsewhere",
        })
        {
            Assert.IsFalse(OwnFiles.MayRemove(written), written);
        }
        Assert.IsTrue(OwnFiles.MayRemove(Path.Combine(TestRunFolders.Scratch, "tray-1")));

        string folder = Path.Combine(AppContext.BaseDirectory, "remove-guard-" + Guid.NewGuid().ToString("N"));
        if (folder.StartsWith(temp, StringComparison.OrdinalIgnoreCase)) Assert.Inconclusive("the tests run from inside the temp folder");
        Directory.CreateDirectory(folder);
        try
        {
            Put(folder, ["config.json", "today.json", "SteppedOut.log"]);
            Assert.ThrowsExactly<InvalidOperationException>(() => OwnFiles.Remove(folder, _ => { }));
            CollectionAssert.AreEqual(new[] { "config.json", "SteppedOut.log", "today.json" }, Left(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // A file that cannot be deleted is said, and the rest still go.
    [TestMethod]
    public void AFileInUseIsSaidAndTheRestStillGo()
    {
        string folder = Path.Combine(TestRunFolders.Scratch, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Put(folder, ["config.json", "today.json"]);
            var failed = new List<string>();
            List<string> removed;
            using (File.Open(Path.Combine(folder, "today.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                removed = OwnFiles.Remove(folder, failed.Add);
            }

            CollectionAssert.AreEqual(new[] { "config.json" }, removed);
            Assert.HasCount(1, failed);
            StringAssert.StartsWith(failed[0], "could not delete today.json");
            CollectionAssert.AreEqual(new[] { "today.json" }, Left(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // ---- The log stops for good ----

    [TestMethod]
    public void AClosedLogWritesNothingMore()
    {
        string folder = Path.Combine(TestRunFolders.Scratch, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "SteppedOut.log");
            var log = new RollingLog(path, 1 << 20);
            log.Write("before");
            log.Close();
            log.Write("after");
            string text = File.ReadAllText(path);
            StringAssert.Contains(text, "before");
            Assert.IsFalse(text.Contains("after", StringComparison.Ordinal));

            string other = Path.Combine(folder, "other.log");
            Log.Open(other, 1 << 20);
            Log.Write("open");
            Log.Close();
            Log.Write("closed");
            text = File.ReadAllText(other);
            StringAssert.Contains(text, "open");
            Assert.IsFalse(text.Contains("closed", StringComparison.Ordinal));
        }
        finally
        {
            Log.Close();
            Directory.Delete(folder, recursive: true);
        }
    }
}
