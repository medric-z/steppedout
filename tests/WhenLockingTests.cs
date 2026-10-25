namespace SteppedOut.Tests;

// "At the lock time": what comes after SteppedOut's own lock, and never after Lock now, the
// shortcut or a lock Windows makes. Leave(locking, byItself: true) is SteppedOut's own lock, as the
// watcher reports it. Every part is a fake: nothing turns a screen off or sleeps.
[TestClass]
public class WhenLockingTests
{
    static void Wait(Task task, string what = "done in time") => AwayFakes.Wait(task, what);

    static Config Choose(WhenLocking whenLocking, Action<Config>? change = null)
    {
        var config = new Config { MediaOnLock = MediaOnLock.KeepPlaying, WhenLocking = whenLocking };
        change?.Invoke(config);
        return config;
    }

    // The screens' wait for the lock screen to fade in, then everything queued after it.
    static void FinishScreens(AwayFakes.Rig rig, AwayActions actions)
    {
        rig.Time.WaitForPending(1);
        rig.Time.Advance(TimeSpan.FromSeconds(2));
        Wait(actions.Idle());
    }

    static void BackAgain(AwayActions actions)
    {
        Wait(actions.ComeBack());
        Wait(actions.Idle());
    }

    // ---- Lock and turn off the screen ----

    [TestMethod]
    public void LockAndTurnOffTheScreenTurnsTheScreensOffAfterItsOwnLock()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(Choose(WhenLocking.LockScreenOff));

        actions.Leave(locking: true, byItself: true);
        FinishScreens(rig, actions);

        CollectionAssert.AreEqual(new[] { "screens off" }, rig.Rec.Events);
    }

    [TestMethod]
    public void LockNowAndAnAwayWithoutALockGetNothingMore()
    {
        foreach (var whenLocking in new[] { WhenLocking.LockScreenOff, WhenLocking.LockSleep })
        {
            using var rig = new AwayFakes.Rig();
            using var actions = rig.Make(Choose(whenLocking));

            actions.Leave(locking: true, byItself: false);
            Wait(actions.Idle(), whenLocking + ", Lock now: no wait for the screens or the lock");
            BackAgain(actions);
            actions.Leave(locking: false, byItself: true);
            Wait(actions.Idle(), whenLocking + ", no lock: nothing to add to");
            actions.Leave(locking: true);
            Wait(actions.Idle(), whenLocking + ", Lock now added to that away");
            BackAgain(actions);

            Assert.IsEmpty(rig.Rec.Events, whenLocking.ToString());
            Assert.AreEqual(0, rig.Time.Pending, whenLocking.ToString());
            Assert.AreEqual(0, rig.Sleeper.Looks, whenLocking + ": the lock was never waited for");
        }
    }

    [TestMethod]
    public void LockThePcAddsNothing()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(Choose(WhenLocking.Lock, c => c.SoundWhenAway = ActionSound.SteppedOut));

        actions.Leave(locking: true, byItself: true);
        Wait(actions.Idle());

        CollectionAssert.AreEqual(new[] { "sound AwaySteppedOut" }, rig.Rec.Events, "played, not waited for");
        Assert.AreEqual(0, rig.Sleeper.Looks);
    }

    // The away action turns the screens off already: they go off once, in their place.
    [TestMethod]
    public void TheScreensGoOffOnceWhenTheAwayActionTurnsThemOffToo()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(Choose(WhenLocking.LockScreenOff, c => c.ScreensOffWhenAway = true));

        actions.Leave(locking: true, byItself: true);
        FinishScreens(rig, actions);

        CollectionAssert.AreEqual(new[] { "screens off" }, rig.Rec.Events);
    }

    [TestMethod]
    public void BackDuringTheLockScreenFadeLeavesTheScreensOn()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(Choose(WhenLocking.LockScreenOff));

        actions.Leave(locking: true, byItself: true);
        rig.Time.WaitForPending(1);
        BackAgain(actions);

        Assert.IsEmpty(rig.Rec.Events);
        CollectionAssert.Contains(rig.Log, "screens left on: already back");
    }

    // ---- Lock and sleep ----

    // Last, after the away sound has finished and everything else this away queued.
    [TestMethod]
    public void LockAndSleepSleepsLastOnceTheAwaySoundHasFinished()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(Choose(WhenLocking.LockSleep, c =>
        {
            c.SoundWhenAway = ActionSound.SteppedOut;
            c.MediaOnLock = MediaOnLock.PauseUntilBack;
            c.OpenWhenAway = [rig.AppToOpen];
            c.RunWhenAway = @"C:\Scripts\away.bat";
        }));

        actions.Leave(locking: true, byItself: true);
        Wait(actions.Idle());

        CollectionAssert.AreEqual(new[]
        {
            "sound AwaySteppedOut waited for", "pause", "start " + rig.AppToOpen, @"start C:\Scripts\away.bat", "sleep",
        }, rig.Rec.Events);
        Assert.IsNotNull(rig.Sound.WaitEnded, "the sound played to its end");
        CollectionAssert.Contains(rig.Log, "back from the sleep Windows was asked for");
    }

    // Windows returns from the sleep only once the PC wakes, so the ask is logged before it and the
    // answer after: the log reads in the order things happened.
    [TestMethod]
    public void TheSleepIsLoggedBeforeItIsAskedForAndAgainAfter()
    {
        using var rig = new AwayFakes.Rig();
        List<string>? whileAsleep = null;
        rig.Sleeper.WhileAsleep = () => whileAsleep = rig.Log.ToList();
        using var actions = rig.Make(Choose(WhenLocking.LockSleep));

        actions.Leave(locking: true, byItself: true);
        Wait(actions.Idle());

        Assert.IsNotNull(whileAsleep);
        Assert.AreEqual("asking Windows to sleep", whileAsleep[^1], "the last line before the sleep");
        var after = rig.Log.ToList();
        int asking = after.IndexOf("asking Windows to sleep");
        Assert.AreEqual("back from the sleep Windows was asked for", after[asking + 1]);
        Assert.HasCount(asking + 2, after, "nothing else after it");
    }

    // An app being closed gets its whole wait, and the force after it, before the PC sleeps.
    [TestMethod]
    public void LockAndSleepWaitsForTheAppsBeingClosed()
    {
        using var rig = new AwayFakes.Rig();
        rig.Processes.Add(2000, "inkwell.exe");
        using var actions = rig.Make(Choose(WhenLocking.LockSleep, c => c.CloseWhenAway = ["inkwell"]));

        actions.Leave(locking: true, byItself: true);
        Wait(actions.Idle());
        rig.Time.WaitForPending(1);
        CollectionAssert.AreEqual(new[] { "ask inkwell.exe" }, rig.Rec.Events, "no sleep while it is closing");
        CollectionAssert.Contains(rig.Log, "put the PC to sleep, once closing apps is over");

        rig.Time.Advance(TimeSpan.FromSeconds(30));
        Wait(actions.Settled());
        Wait(actions.Idle());

        CollectionAssert.AreEqual(new[] { "ask inkwell.exe", "force inkwell.exe", "sleep" }, rig.Rec.Events);
    }

    // An app closed and opened again when away is opened before the PC sleeps: both wait for its
    // close, and the sleep always comes after the open, run after run.
    [TestMethod]
    public void LockAndSleepSleepsOnceAnAppClosedAndOpenedAgainIsOpen()
    {
        for (int run = 0; run < 25; run++)
        {
            using var rig = new AwayFakes.Rig();
            var app = rig.Processes.Add(2000, "open-me.exe");
            using var actions = rig.Make(Choose(WhenLocking.LockSleep, c =>
            {
                c.CloseWhenAway = ["open-me"];
                c.OpenWhenAway = [rig.AppToOpen];
            }));

            actions.Leave(locking: true, byItself: true);
            Wait(actions.Idle());
            rig.Time.WaitForPending(1);
            CollectionAssert.AreEqual(new[] { "ask open-me.exe" }, rig.Rec.Events, $"run {run}: nothing more while it is closing");

            app.Exit();
            Wait(actions.Settled());
            Wait(actions.Idle());

            CollectionAssert.AreEqual(new[] { "ask open-me.exe", "start " + rig.AppToOpen, "sleep" }, rig.Rec.Events, $"run {run}");
        }
    }

    [TestMethod]
    public void BackWhileAnAppIsClosingMeansNoSleep()
    {
        using var rig = new AwayFakes.Rig();
        rig.Processes.Add(2000, "inkwell.exe");
        using var actions = rig.Make(Choose(WhenLocking.LockSleep, c => c.CloseWhenAway = ["inkwell"]));
        actions.Leave(locking: true, byItself: true);
        Wait(actions.Idle());
        rig.Time.WaitForPending(1);

        BackAgain(actions);
        Wait(actions.Settled());
        Wait(actions.Idle());

        CollectionAssert.DoesNotContain(rig.Rec.Events, "sleep");
        CollectionAssert.Contains(rig.Log, "already back, so the PC does not sleep");
        Assert.AreEqual(0, rig.Sleeper.Looks);
    }

    // The lock that never came: looked for every 250 ms for 10 s, then no sleep.
    [TestMethod]
    public void NoSleepWhenThePcDoesNotLock()
    {
        using var rig = new AwayFakes.Rig();
        rig.Sleeper.Locked = false;
        using var actions = rig.Make(Choose(WhenLocking.LockSleep));

        actions.Leave(locking: true, byItself: true);
        for (int i = 0; i < 40; i++)
        {
            rig.Time.WaitForPending(1);
            rig.Time.Advance(TimeSpan.FromMilliseconds(250));
        }
        Wait(actions.Idle());

        Assert.IsEmpty(rig.Rec.Events);
        Assert.AreEqual(41, rig.Sleeper.Looks, "at once, then every 250 ms up to 10 s");
        CollectionAssert.Contains(rig.Log, "the PC did not lock, so it does not sleep");
    }

    [TestMethod]
    public void ALockThatComesLateIsStillWaitedFor()
    {
        using var rig = new AwayFakes.Rig();
        rig.Sleeper.Locked = false;
        using var actions = rig.Make(Choose(WhenLocking.LockSleep));

        actions.Leave(locking: true, byItself: true);
        for (int i = 0; i < 6; i++)
        {
            rig.Time.WaitForPending(1);
            rig.Time.Advance(TimeSpan.FromMilliseconds(250));
        }
        // Locked while it waits for its next look.
        rig.Time.WaitForPending(1);
        rig.Sleeper.Locked = true;
        rig.Time.Advance(TimeSpan.FromMilliseconds(250));
        Wait(actions.Idle());

        CollectionAssert.AreEqual(new[] { "sleep" }, rig.Rec.Events);
        Assert.AreEqual(8, rig.Sleeper.Looks);
    }

    // Coming back ends the wait for the lock at once.
    [TestMethod]
    public void BackBeforeTheLockMeansNoSleep()
    {
        using var rig = new AwayFakes.Rig();
        rig.Sleeper.Locked = false;
        using var actions = rig.Make(Choose(WhenLocking.LockSleep));
        actions.Leave(locking: true, byItself: true);
        rig.Time.WaitForPending(1);

        BackAgain(actions);

        Assert.IsEmpty(rig.Rec.Events);
        CollectionAssert.Contains(rig.Log, "back before the lock, so the PC does not sleep");
    }

    // A PC with Modern Standby has no sleep an app can ask for: the screens go off instead, which is
    // how it goes to standby. Never hibernate.
    [TestMethod]
    public void APcWithoutSleepTurnsTheScreensOffInstead()
    {
        using var rig = new AwayFakes.Rig();
        rig.Sleeper.CanSleep = false;
        using var actions = rig.Make(Choose(WhenLocking.LockSleep));

        actions.Leave(locking: true, byItself: true);
        FinishScreens(rig, actions);

        CollectionAssert.AreEqual(new[] { "screens off" }, rig.Rec.Events);
        CollectionAssert.Contains(rig.Log, "this PC has no sleep an app can ask for, so the screens are turned off instead");
    }

    [TestMethod]
    public void APcWithoutSleepDoesNotTurnTheScreensOffTwice()
    {
        using var rig = new AwayFakes.Rig();
        rig.Sleeper.CanSleep = false;
        using var actions = rig.Make(Choose(WhenLocking.LockSleep, c => c.ScreensOffWhenAway = true));

        actions.Leave(locking: true, byItself: true);
        FinishScreens(rig, actions);

        CollectionAssert.AreEqual(new[] { "screens off" }, rig.Rec.Events);
        CollectionAssert.Contains(rig.Log, "this PC has no sleep an app can ask for; the screens are already off");
    }

    [TestMethod]
    public void ASleepWindowsTurnsDownIsLogged()
    {
        using var rig = new AwayFakes.Rig();
        rig.Sleeper.Refuses = true;
        using var actions = rig.Make(Choose(WhenLocking.LockSleep));

        actions.Leave(locking: true, byItself: true);
        Wait(actions.Idle());

        CollectionAssert.AreEqual(new[] { "sleep" }, rig.Rec.Events);
        CollectionAssert.Contains(rig.Log, "Windows did not put the PC to sleep");
    }

    // After the sleep, coming back runs the back side as after any lock.
    [TestMethod]
    public void BackAfterTheSleepRunsTheBackActions()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(Choose(WhenLocking.LockSleep, c =>
        {
            c.SoundWhenBack = ActionSound.Drift;
            c.MuteWhenAway = true;
        }));
        actions.Leave(locking: true, byItself: true);
        Wait(actions.Idle());

        BackAgain(actions);

        CollectionAssert.AreEqual(new[] { "mute", "sleep", "unmute", "sound BackDrift" }, rig.Rec.Events);
    }

    // ---- The dry run ----

    [TestMethod]
    public void TheDryRunOnlySaysWhatItWouldDo()
    {
        foreach (var (whenLocking, line) in new[]
        {
            (WhenLocking.LockScreenOff, "WOULD turn the screens off after the lock (dry run)"),
            (WhenLocking.LockSleep, "WOULD put the PC to sleep once the lock is in place (dry run)"),
        })
        {
            using var rig = new AwayFakes.Rig();
            using var actions = rig.Make(Choose(whenLocking), dryRun: true);

            actions.Leave(locking: true, byItself: true);
            Wait(actions.Idle());
            BackAgain(actions);
            actions.Leave(locking: true, byItself: false);
            Wait(actions.Idle());

            Assert.IsEmpty(rig.Rec.Events, whenLocking.ToString());
            Assert.AreEqual(0, rig.Sleeper.Looks, whenLocking.ToString());
            CollectionAssert.AreEqual(new[] { line }, rig.Log.Where(l => l.StartsWith("WOULD ", StringComparison.Ordinal)).ToList(),
                whenLocking + ": once, for its own lock only");
        }
    }

    [TestMethod]
    public void TheDryRunSaysTheScreensOnceWhenTheAwayActionTurnsThemOff()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(Choose(WhenLocking.LockScreenOff, c => c.ScreensOffWhenAway = true), dryRun: true);

        actions.Leave(locking: true, byItself: true);
        Wait(actions.Idle());

        CollectionAssert.AreEqual(new[] { "WOULD turn the screens off (dry run)" },
            rig.Log.Where(l => l.StartsWith("WOULD ", StringComparison.Ordinal)).ToList());
    }

    // ---- The guard ----

    // While the tests block it, the real sleep refuses before it reaches Windows, and the away
    // actions' own parts refuse it too.
    [TestMethod]
    public void TheRealSleepRefusesWhileTheTestsBlockIt()
    {
        Assert.IsTrue(SessionLock.Blocked);
        Assert.ThrowsExactly<InvalidOperationException>(() => new PowerSleep().Sleep());

        using var actions = new AwayActions(dryRun: false);
        var sleeper = actions.Parts.Sleeper;
        Assert.IsInstanceOfType<AwayParts.Refused>(sleeper);
        Assert.ThrowsExactly<InvalidOperationException>(() => sleeper.Sleep());
        Assert.ThrowsExactly<InvalidOperationException>(() => sleeper.SessionLocked());
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = sleeper.CanSleep);
        Assert.IsInstanceOfType<AwayParts.Refused>(actions.Parts.NewScreens());
    }

    // The whole way through with the blocked parts: each step is refused and logged, and nothing
    // escapes the queue.
    [TestMethod]
    public void TheBlockedPartsRefuseTheScreensAndTheSleepAndNothingBreaks()
    {
        foreach (var whenLocking in new[] { WhenLocking.LockScreenOff, WhenLocking.LockSleep })
        {
            var log = new List<string>();
            var parts = AwayParts.Refusing();
            using var actions = new AwayActions(dryRun: false, new AwayParts
            {
                Time = parts.Time,
                Log = line =>
                {
                    lock (log) log.Add(line);
                },
                Sound = parts.Sound,
                Media = parts.Media,
                NewMute = parts.NewMute,
                UndoLeftoverMute = parts.UndoLeftoverMute,
                RetryLeftoverMute = parts.RetryLeftoverMute,
                NewScreens = parts.NewScreens,
                Sleeper = parts.Sleeper,
                Starter = parts.Starter,
                Processes = parts.Processes,
                CloseWait = parts.CloseWait,
                ListenToWindows = false,
            });
            actions.Apply(Choose(whenLocking));
            actions.Leave(locking: true, byItself: true);
            // The real clock: the screens wait out the lock screen's 2 s fade before they are refused.
            Wait(actions.Idle(), whenLocking.ToString());

            List<string> seen;
            lock (log) seen = log.ToList();
            Assert.IsTrue(seen.Any(line => line.Contains("blocked in this process", StringComparison.Ordinal)), whenLocking + ": " + string.Join(" | ", seen));
            Assert.DoesNotContain("back from the sleep Windows was asked for", seen, whenLocking.ToString());
        }
    }
}
