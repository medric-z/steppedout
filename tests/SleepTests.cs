using Microsoft.Win32;

namespace SteppedOut.Tests;

// D47: after the PC wakes from sleep the lock time starts over from zero. The idle time counts from
// the wake, a countdown that was running ends without the lock or the away actions, and the whole
// lock time runs again before any countdown. A lock or an away from before the sleep stays as it is.
// The wake is seen as a gap of Watcher.SleptAfter or more between ticks, or as Windows' Resume.
//
// With a lock time of 15 minutes and a warning of 15 s the countdown starts at 885 s and locks at 900 s.
// Windows' idle time (GetLastInputInfo against the tick count) keeps counting through a sleep, so a
// sleep here moves both clocks and the idle time on, with no tick in between.
[TestClass]
public class SleepTests
{
    static Config Settings() => new() { IdleMinutes = 15, GraceSeconds = 15, MusicVideo = Effect.None, FullScreen = Effect.None, OnCall = Effect.None };

    static void Sleep(Rig rig, TimeSpan span)
    {
        rig.Jump(span);
        rig.Idle += span.TotalSeconds;
    }

    // Seconds after the tick that saw the wake, with nobody at the PC.
    static void SecondsAfter(Rig rig, int seconds)
    {
        for (int i = 0; i < seconds; i++) rig.Second();
    }

    [TestMethod]
    public void SleepDuringACountdownEndsItQuietlyAndTheWholeLockTimeRunsAgain()
    {
        using var rig = new Rig(Settings());
        rig.Until(890);
        Assert.AreEqual(885, rig.First("start"));
        Assert.AreEqual(0, rig.Count("end"));

        Sleep(rig, TimeSpan.FromHours(2));
        rig.Second();
        double woke = rig.Idle;
        Assert.AreEqual(1, rig.Count("end"), "the countdown note goes");
        Assert.AreEqual(0, rig.Count("away"), "no away actions");
        Assert.AreEqual(0, rig.LockRequests, "no lock");
        Assert.AreEqual(0, rig.Watcher.LockStatus.IdleSeconds, "idle counts from the wake");
        Assert.AreEqual(LockState.On, rig.Watcher.LockStatus.Kind);

        SecondsAfter(rig, 884);
        Assert.AreEqual(1, rig.Count("start"), "no countdown before the whole lock time has run again");
        Assert.AreEqual(884, rig.Watcher.LockStatus.IdleSeconds);
        rig.Second();
        Assert.AreEqual(2, rig.Count("start"));
        Assert.AreEqual(woke + 885, rig.Events.Last(e => e.What.StartsWith("start", StringComparison.Ordinal)).Idle);
        Assert.AreEqual("start 15", rig.Events.Last(e => e.What.StartsWith("start", StringComparison.Ordinal)).What, "the whole warning");
        SecondsAfter(rig, 14);
        Assert.AreEqual(0, rig.LockRequests);
        rig.Second();
        Assert.AreEqual(1, rig.LockRequests, "locked 900 s after the wake");
        Assert.AreEqual(woke + 900, rig.First("away locking"));
    }

    // Idle already past the lock time when the PC went to sleep, as when something kept it from
    // locking until then, or it slept for longer than the lock time: waking does not lock at once.
    [TestMethod]
    [DataRow(600)]
    [DataRow(880)]
    public void SleepWhileWatchingStartsTheLockTimeOver(int idleBefore)
    {
        using var rig = new Rig(Settings());
        rig.Until(idleBefore);
        Sleep(rig, TimeSpan.FromMinutes(40));
        Assert.IsGreaterThan(900, rig.Idle);
        rig.Second();
        double woke = rig.Idle;
        Assert.AreEqual(0, rig.Count("start"), "no countdown on waking with the idle time past the lock time");
        Assert.AreEqual(0, rig.Count("end"), "there was no countdown to end");
        Assert.AreEqual(0, rig.LockRequests);

        rig.Until(woke + 885);
        Assert.AreEqual(woke + 885, rig.First("start"));
        rig.Until(woke + 900);
        Assert.AreEqual(1, rig.LockRequests);
        Assert.AreEqual(woke + 900, rig.First("away locking"));
    }

    // A tick that comes a few seconds late, as on a busy PC, is no sleep: nothing starts over.
    [TestMethod]
    public void AShortHiccupStartsNothingOver()
    {
        using var rig = new Rig(Settings());
        rig.Until(880);
        Sleep(rig, TimeSpan.FromSeconds(2));
        rig.Until(890);
        Assert.AreEqual(885, rig.First("start"));
        Sleep(rig, TimeSpan.FromSeconds(2));
        rig.Until(899);
        Assert.AreEqual(0, rig.Count("end"), "a 3 s gap in the countdown does not end it");
        rig.Until(900);
        Assert.AreEqual(1, rig.Count("start"));
        Assert.AreEqual(1, rig.LockRequests);
        Assert.AreEqual(900, rig.First("away locking"));
    }

    // The threshold itself: a gap just under SleptAfter is a hold-up, a gap of SleptAfter a sleep.
    [TestMethod]
    public void AGapOfSleptAfterIsASleepAndOneJustUnderIsNot()
    {
        var under = TimeSpan.FromSeconds(Watcher.SleptAfter.TotalSeconds - 2);
        using (var rig = new Rig(Settings()))
        {
            rig.Until(890);
            Sleep(rig, under);
            rig.Second();
            Assert.AreEqual(1, rig.LockRequests, "a gap of " + (under.TotalSeconds + 1) + " s: the countdown ran out in it and locks");
            Assert.AreEqual(1, rig.Count("away locking"));
        }
        using (var rig = new Rig(Settings()))
        {
            rig.Until(890);
            Sleep(rig, Watcher.SleptAfter - TimeSpan.FromSeconds(1));
            rig.Second();
            Assert.AreEqual(1, rig.Count("end"), "a gap of " + Watcher.SleptAfter.TotalSeconds + " s");
            Assert.AreEqual(0, rig.LockRequests);
        }
        Assert.AreEqual(TimeSpan.FromSeconds(30), Watcher.SleptAfter);
    }

    // Input after the wake: the idle time counts from that input, as at any other time.
    [TestMethod]
    public void RealInputAfterTheWakeCountsFromTheInput()
    {
        using var rig = new Rig(Settings());
        rig.Until(890);
        Sleep(rig, TimeSpan.FromHours(1));
        rig.Second();
        SecondsAfter(rig, 10);
        Assert.AreEqual(10, rig.Watcher.LockStatus.IdleSeconds);

        rig.Input = true;
        rig.Busy();
        Assert.AreEqual(0, rig.Watcher.LockStatus.IdleSeconds);
        Assert.AreEqual(0, rig.Count("back"), "no away to come back from");
        rig.Until(884);
        Assert.AreEqual(1, rig.Count("start"));
        rig.Until(900);
        Assert.AreEqual(2, rig.Count("start"));
        Assert.AreEqual(885, rig.Events.Last(e => e.What.StartsWith("start", StringComparison.Ordinal)).Idle);
        Assert.AreEqual(900, rig.First("away locking"));
    }

    // The key press that woke the PC is input like any other.
    [TestMethod]
    public void InputThatWokeThePcCountsFromThere()
    {
        using var rig = new Rig(Settings());
        rig.Until(600);
        Sleep(rig, TimeSpan.FromHours(1));
        rig.Input = true;
        rig.Busy();
        Assert.AreEqual(0, rig.Watcher.LockStatus.IdleSeconds);
        rig.Until(885);
        Assert.AreEqual(885, rig.First("start"));
        rig.Until(900);
        Assert.AreEqual(1, rig.LockRequests);
    }

    // Windows saying the PC resumed starts the lock time over too, for a sleep the ticks did not show.
    [TestMethod]
    public void WindowsResumeStartsTheLockTimeOver()
    {
        using var rig = new Rig(Settings());
        rig.Until(890);
        rig.Watcher.PowerChanged(PowerModes.Suspend);
        rig.Watcher.PowerChanged(PowerModes.StatusChange);
        Assert.AreEqual(0, rig.Count("end"), "only the resume counts");

        rig.Watcher.PowerChanged(PowerModes.Resume);
        Assert.AreEqual(1, rig.Count("end"));
        Assert.AreEqual(0, rig.LockRequests);
        rig.Second();
        Assert.AreEqual(1, rig.Watcher.LockStatus.IdleSeconds, "counted from the resume");
        rig.Until(890 + 884);
        Assert.AreEqual(1, rig.Count("start"));
        rig.Until(890 + 900);
        Assert.AreEqual(2, rig.Count("start"));
        Assert.AreEqual(1, rig.LockRequests);
        Assert.AreEqual(890 + 900, rig.First("away locking"));
    }

    // Both seen for one wake starts over twice within a moment, which is the same.
    [TestMethod]
    public void ResumeAndTheGapTogetherAreOneStartOver()
    {
        using var rig = new Rig(Settings());
        rig.Until(890);
        Sleep(rig, TimeSpan.FromHours(1));
        rig.Watcher.PowerChanged(PowerModes.Resume);
        rig.Second();
        double woke = rig.Idle;
        Assert.AreEqual(1, rig.Count("end"));
        rig.Until(woke + 900);
        Assert.AreEqual(1, rig.LockRequests);
        Assert.AreEqual(woke + 900, rig.First("away locking"));
    }

    // A lock from before the sleep is not undone or done again, and the time away counts the sleep:
    // it is time away from the PC that began before it.
    [TestMethod]
    public void ALockFromBeforeTheSleepStaysAsItIs()
    {
        using var rig = new Rig(Settings());
        rig.Until(900);
        Assert.AreEqual(1, rig.LockRequests);
        Assert.AreEqual(LockState.Away, rig.Watcher.LockStatus.Kind);

        int ends = rig.Count("end");
        Sleep(rig, TimeSpan.FromHours(1));
        rig.Second();
        rig.Watcher.PowerChanged(PowerModes.Resume);
        SecondsAfter(rig, 5);
        Assert.AreEqual(1, rig.LockRequests, "not locked again");
        Assert.AreEqual(ends, rig.Count("end"));
        Assert.AreEqual(0, rig.Count("back"));
        Assert.AreEqual(LockState.Away, rig.Watcher.LockStatus.Kind);

        rig.Input = true;
        rig.Busy();
        Assert.AreEqual(1, rig.Count("back"));
        var away = rig.Watcher.LockStatus.Today.Away;
        Assert.IsGreaterThan(TimeSpan.FromHours(1), away, "the sleep inside the away is time away");
        Assert.IsLessThan(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(20), away);
    }

    // A sleep from watching or from a countdown is not time away: away today starts at the idle
    // stretch that ends in the lock after the wake, so it counts from the wake.
    [TestMethod]
    public void ASleepFromWatchingIsNotTimeAway()
    {
        using var rig = new Rig(Settings());
        rig.Until(890);
        Sleep(rig, TimeSpan.FromHours(1));
        rig.Second();
        Assert.AreEqual(TimeSpan.Zero, rig.Watcher.LockStatus.Today.Away);
        double woke = rig.Idle;
        rig.Until(woke + 900);
        Assert.AreEqual(1, rig.LockRequests);
        SecondsAfter(rig, 60);
        rig.Input = true;
        rig.Busy();
        var away = rig.Watcher.LockStatus.Today.Away;
        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(900), away);
        Assert.IsLessThan(TimeSpan.FromMinutes(20), away, "the hour asleep is not in it");
    }

    // With "sign in again when the PC wakes" Windows locks the session as the PC goes to sleep. That
    // lock is the sleep's, not time away: it still counts as a lock, and its away time starts at the
    // wake, so only the lock screen after waking is in Away today.
    [TestMethod]
    [DataRow(false, 0, DisplayName = "the lock, then the gap")]
    [DataRow(true, 0, DisplayName = "Windows' suspend and resume notices")]
    [DataRow(false, 5, DisplayName = "the lock a few seconds before the sleep")]
    public void ALockThatComesWithTheSleepIsNotTimeAway(bool notices, int secondsBefore)
    {
        using var rig = new Rig(Settings());
        rig.Until(300);
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionLock);
        SecondsAfter(rig, secondsBefore);
        if (notices) rig.Watcher.PowerChanged(PowerModes.Suspend);
        Sleep(rig, TimeSpan.FromHours(1));
        if (notices) rig.Watcher.PowerChanged(PowerModes.Resume);
        rig.Second();
        SecondsAfter(rig, 19);
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionUnlock);

        var today = rig.Watcher.LockStatus.Today;
        Assert.AreEqual(1, today.Locks, "it is still a lock");
        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(19), today.Away);
        Assert.IsLessThanOrEqualTo(TimeSpan.FromSeconds(21), today.Away, "only the lock screen after the wake");
        Assert.AreEqual(0, rig.LockRequests);
    }

    // A PC with Modern Standby can keep SteppedOut running for a while after the screen goes off and
    // the session locks, so the last tick before the gap can come well after the lock. Windows' own
    // notice that the PC is going to sleep marks the sleep then: a lock from no more than SleptAfter
    // before the notice is still the sleep's.
    [TestMethod]
    public void ALockAtTheSleepNoticeIsTheSleepsEvenWhenThePcRunsOnAWhile()
    {
        using var rig = new Rig(Settings());
        rig.Until(300);
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionLock);
        rig.Second();
        rig.Watcher.PowerChanged(PowerModes.Suspend);
        SecondsAfter(rig, 90);
        Sleep(rig, TimeSpan.FromHours(1));
        rig.Second();
        SecondsAfter(rig, 19);
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionUnlock);

        var today = rig.Watcher.LockStatus.Today;
        Assert.AreEqual(1, today.Locks);
        Assert.IsLessThanOrEqualTo(TimeSpan.FromSeconds(21), today.Away, "only the lock screen after the wake");
    }

    // A notice with no sleep after it for long, as when the sleep was called off, says nothing about
    // a later sleep: a Win+L well before that sleep still counts it as time away.
    [TestMethod]
    public void AnOldSleepNoticeSaysNothingAboutALaterSleep()
    {
        using var rig = new Rig(Settings());
        rig.Until(100);
        rig.Watcher.PowerChanged(PowerModes.Suspend);
        rig.Until(110);
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionLock);
        SecondsAfter(rig, (int)Watcher.StandbyLag.TotalSeconds + 60);
        Sleep(rig, TimeSpan.FromHours(1));
        rig.Second();
        SecondsAfter(rig, 19);
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionUnlock);
        Assert.IsGreaterThan(TimeSpan.FromHours(1), rig.Watcher.LockStatus.Today.Away);
    }

    // A lock that Windows reports only after the wake starts there anyway.
    [TestMethod]
    public void ALockReportedAfterTheWakeCountsFromThere()
    {
        using var rig = new Rig(Settings());
        rig.Until(300);
        Sleep(rig, TimeSpan.FromHours(1));
        rig.Second();
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionLock);
        SecondsAfter(rig, 20);
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionUnlock);
        Assert.AreEqual(1, rig.Watcher.LockStatus.Today.Locks);
        Assert.IsLessThanOrEqualTo(TimeSpan.FromSeconds(21), rig.Watcher.LockStatus.Today.Away);
    }

    // A lock well before the sleep, as with Win+L and the PC sleeping later on its own, is a lock
    // from before the sleep: the sleep inside it is time away.
    [TestMethod]
    public void ALockLongBeforeTheSleepCountsTheSleep()
    {
        using var rig = new Rig(Settings());
        rig.Until(300);
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionLock);
        SecondsAfter(rig, (int)Watcher.SleptAfter.TotalSeconds + 30);
        Sleep(rig, TimeSpan.FromHours(1));
        rig.Second();
        SecondsAfter(rig, 19);
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionUnlock);
        Assert.IsGreaterThan(TimeSpan.FromHours(1), rig.Watcher.LockStatus.Today.Away);
    }
}
