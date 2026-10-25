using Microsoft.Win32;

namespace SteppedOut.Tests;

// The window timer samples the cursor several times a second and the watcher's tick runs once a
// second. Windows fires a window timer on its 15.6 ms timer grid, so a 100 ms timer really fires
// every 109.375 ms; counting ten of those made a "second" of 1.094 s, and the countdown skipped a
// number and locked up to 1.4 s late. The tick now goes by the clock.
//
// Each step here is one timer event: the clocks and the idle time move on by the event's spacing.
[TestClass]
public class TickScheduleTests
{
    static Config Settings() => new() { IdleMinutes = 15, GraceSeconds = 15, MusicVideo = Effect.None, FullScreen = Effect.None, OnCall = Effect.None };

    sealed class Timed : IDisposable
    {
        public Timed()
        {
            Rig = new Rig(Settings());
            Rig.Watcher.Updated += () => Ticks.Add(Rig.Idle);
            Rig.Watcher.CountdownStarted += seconds => Shown.Add(seconds);
            Rig.Watcher.CountdownTick += seconds => Shown.Add(seconds);
        }

        public Rig Rig { get; }

        // The idle time at each tick.
        public List<double> Ticks { get; } = new();

        // Every number the countdown showed, in order.
        public List<int> Shown { get; } = new();

        // Timer events every `every` milliseconds until the idle time reaches `idle`.
        public void Until(double idle, double every)
        {
            while (Rig.Idle < idle)
            {
                Rig.Jump(TimeSpan.FromMilliseconds(every));
                Rig.Idle += every / 1000;
                Rig.Watcher.TimerFired();
            }
        }

        public void Dispose() => Rig.Dispose();
    }

    [TestMethod]
    [DataRow(109.375, DisplayName = "a 100 ms timer on the 15.6 ms grid")]
    [DataRow(125.0, DisplayName = "8 steps of the grid")]
    [DataRow(100.0, DisplayName = "a 1 ms grid")]
    [DataRow(156.25, DisplayName = "a busy UI thread")]
    public void TheTickRunsOnceASecond(double every)
    {
        using var timed = new Timed();
        timed.Until(120, every);
        Assert.IsInRange(119, 121, timed.Ticks.Count, "one tick a second over two minutes");
        var gaps = timed.Ticks.Zip(timed.Ticks.Skip(1), (a, b) => b - a).ToList();
        Assert.AreEqual(1, gaps.Average(), 0.01, "a second each on average");
        Assert.IsLessThan(1 + every / 1000 + 1e-6, gaps.Max(), "no second is stretched by more than one timer event");
    }

    // The timer fires every 125 ms, 8 steps of the grid, and on a PC whose grid is finer, every
    // 125 ms or 100 ms.
    [TestMethod]
    [DataRow(125.0)]
    [DataRow(100.0)]
    public void TheCountdownShowsEveryNumberAndLocksOnTime(double every)
    {
        using var timed = new Timed();
        timed.Until(905, every);
        CollectionAssert.AreEqual(Enumerable.Range(1, 15).Reverse().ToList(), timed.Shown.Distinct().ToList(), "15 down to 1, none skipped");
        Assert.AreEqual(1, timed.Rig.LockRequests);
        double lockedAt = timed.Rig.First("away locking")!.Value;
        Assert.IsInRange(900, 900 + 1 + every / 1000, lockedAt, "the lock comes on the first tick at or after the lock time");
    }

    // A timer event that comes a little early does not push the tick to the next event: with the
    // timer at a second while the session is locked, that would make every tick two seconds apart.
    [TestMethod]
    [DataRow(999.0)]
    [DataRow(1000.0)]
    [DataRow(1015.625)]
    public void ASecondTimerTicksEveryEvent(double every)
    {
        using var timed = new Timed();
        timed.Until(60, every);
        Assert.IsInRange(59, 61, timed.Ticks.Count);
    }

    // A tick that ran very late, as after a sleep, does not make up the missed seconds one after the
    // other: the next one is a second later.
    [TestMethod]
    public void MissedSecondsAreNotMadeUp()
    {
        using var timed = new Timed();
        timed.Until(10, 125);
        int before = timed.Ticks.Count;
        timed.Rig.Jump(TimeSpan.FromSeconds(20));
        timed.Rig.Idle += 20;
        timed.Rig.Watcher.TimerFired();
        Assert.HasCount(before + 1, timed.Ticks);
        timed.Until(timed.Rig.Idle + 0.8, 125);
        Assert.HasCount(before + 1, timed.Ticks, "nothing more within the next second");
        timed.Until(timed.Rig.Idle + 0.2, 125);
        Assert.HasCount(before + 2, timed.Ticks);
    }

    // A tick that throws every time stops the locking. The timer goes on, the log hears of it in full
    // once (ErrorRepeats), and after FailingAfter failed ticks the tray is told, once, and once more
    // when the tick has worked FailingAfter times in a row.
    [TestMethod]
    public void ATickThatKeepsThrowingIsToldOnceAndNotThrownOn()
    {
        using var timed = new Timed();
        var told = new List<bool>();
        timed.Rig.Watcher.TicksFailing += failing => told.Add(failing);
        bool broken = false;
        timed.Rig.Watcher.Updated += () =>
        {
            if (broken) throw new InvalidOperationException("broken");
        };
        timed.Until(5, 125);
        broken = true;
        timed.Until(5 + Watcher.FailingAfter - 1.5, 125);
        Assert.IsEmpty(told, "not yet");
        timed.Until(60, 125);
        CollectionAssert.AreEqual(new[] { true }, told);
        broken = false;
        timed.Until(60 + Watcher.FailingAfter - 1.5, 125);
        CollectionAssert.AreEqual(new[] { true }, told, "not after a few good ticks");
        timed.Until(60 + Watcher.FailingAfter + 1, 125);
        CollectionAssert.AreEqual(new[] { true, false }, told);
        Assert.IsInRange(69, 72, timed.Ticks.Count, "the tick ran every second throughout");
    }

    // A tick that fails every other second never fails ten times in a row, yet the log heard of each
    // failure in full and the tray was never told. It is one run of errors: the log has it in full
    // once, and the tray is told once FailingAfter have failed without FailingAfter good ticks
    // between them.
    [TestMethod]
    public void ATickThatFailsEveryOtherSecondIsWrittenOnceAndTold()
    {
        var log = new List<string>();
        using var rig = new Rig(Settings(), log.Add);
        var told = new List<bool>();
        rig.Watcher.TicksFailing += failing => told.Add(failing);
        int ticks = 0;
        rig.Watcher.Updated += () =>
        {
            if (++ticks % 2 == 0) throw new InvalidOperationException("broken");
        };
        for (int second = 0; second < 120; second++)
        {
            rig.Jump(TimeSpan.FromSeconds(1));
            rig.Idle += 1;
            rig.Watcher.TimerFired();
        }
        Assert.AreEqual(1, log.Count(line => line.StartsWith("tick failed: System.InvalidOperationException: broken", StringComparison.Ordinal)), string.Join(" | ", log.Select(line => line.Split('\n')[0])));
        CollectionAssert.AreEqual(new[] { true }, told, "told once");
        CollectionAssert.Contains(log, "the tick keeps failing: locking may not work until it works again");
    }

    // While the session is locked nothing is watched but the unlock and the clock, so the timer
    // wakes once a second instead of eight times.
    [TestMethod]
    public void TheTimerSlowsWhileTheSessionIsLocked()
    {
        using var rig = new Rig(Settings());
        Assert.AreEqual(125, rig.Watcher.TimerInterval);
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionLock);
        Assert.AreEqual(1000, rig.Watcher.TimerInterval);
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionUnlock);
        Assert.AreEqual(125, rig.Watcher.TimerInterval);
    }
}
