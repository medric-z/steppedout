namespace SteppedOut.Tests;

[TestClass]
public class LockEngineTests
{
    static LockEngine Engine(int warningSeconds = 3) => new(warningSeconds);

    // One step with nobody at the PC: idle seconds, and the lock time in minutes or null for never.
    static EngineEvent Idle(LockEngine e, double seconds, decimal? total) => e.Step(seconds, false, total, false, false);

    static EngineEvent Input(LockEngine e, decimal? total = 1) => e.Step(0, true, total, false, false);

    // Steps every second from one idle time to another with the same lock time; returns the events.
    static List<(double Idle, EngineEvent Event)> Run(LockEngine e, double from, double to, decimal? total)
    {
        var events = new List<(double, EngineEvent)>();
        for (double idle = from; idle <= to; idle++)
        {
            var happened = Idle(e, idle, total);
            if (happened != EngineEvent.None) events.Add((idle, happened));
        }
        return events;
    }

    [TestMethod]
    public void StaysWatchingBeforeTheCountdownStart()
    {
        var e = Engine();
        Assert.AreEqual(EngineEvent.None, Idle(e, 56, 1));
        Assert.AreEqual(Phase.Watching, e.Phase);
    }

    [TestMethod]
    public void StartsCountdownAtTheLockTimeLessTheWarning()
    {
        var e = Engine();
        Idle(e, 56, 1);
        Assert.AreEqual(EngineEvent.CountdownStarted, Idle(e, 57, 1));
        Assert.AreEqual(Phase.Countdown, e.Phase);
        Assert.AreEqual(3, e.CountdownRemaining);
        Assert.AreEqual(60, e.CountdownEnd);
    }

    [TestMethod]
    public void CountsDownThenGoesAwayOnceAtTheLockTime()
    {
        var e = Engine();
        Idle(e, 56, 1);
        Idle(e, 57, 1);
        Assert.AreEqual(EngineEvent.CountdownTick, Idle(e, 58, 1));
        Assert.AreEqual(2, e.CountdownRemaining);
        Assert.AreEqual(EngineEvent.CountdownTick, Idle(e, 59, 1));
        Assert.AreEqual(1, e.CountdownRemaining);
        Assert.AreEqual(EngineEvent.WentAway, Idle(e, 60, 1), "the lock lands at the lock time, with the countdown inside it");
        Assert.AreEqual(Phase.Away, e.Phase);
        Assert.AreEqual(EngineEvent.None, Idle(e, 61, 1));
        Assert.AreEqual(EngineEvent.None, Idle(e, 3600, 1));
    }

    [TestMethod]
    public void TheCountdownIsHalfTheLockTimeWhenThatIsShorter()
    {
        // 2 min with 50% sooner on battery is 1 min; a 60 s warning becomes 30 s, from 0:30.
        var e = Engine(60);
        Assert.IsEmpty(Run(e, 0, 29, 1));
        Assert.AreEqual(EngineEvent.CountdownStarted, Idle(e, 30, 1));
        Assert.AreEqual(30, e.CountdownRemaining);
        Assert.AreEqual(EngineEvent.WentAway, Run(e, 31, 60, 1).Last().Event);
        Assert.AreEqual(60, Run(Engine(60), 0, 60, 1).Single(x => x.Event == EngineEvent.WentAway).Idle);
    }

    [TestMethod]
    public void ThirtyMinutesWithASixtySecondWarningStartsAtTwentyNine()
    {
        var events = Run(Engine(60), 0, 1800, 30);
        Assert.AreEqual((1740.0, EngineEvent.CountdownStarted), events.First());
        Assert.AreEqual((1800.0, EngineEvent.WentAway), events.Last());
    }

    [TestMethod]
    public void TheRemainingTimeComesFromTheIdleTimeNotFromCountingSteps()
    {
        var e = Engine(15);
        Idle(e, 884, 15);
        Idle(e, 885, 15);
        // A second step lost to a busy moment: the idle time moved on by two.
        Assert.AreEqual(EngineEvent.CountdownTick, Idle(e, 888, 15));
        Assert.AreEqual(12, e.CountdownRemaining);
        Assert.AreEqual(EngineEvent.WentAway, Idle(e, 900, 15));
    }

    [TestMethod]
    public void InputCancelsCountdown()
    {
        var e = Engine();
        Idle(e, 56, 1);
        Idle(e, 57, 1);
        Assert.AreEqual(EngineEvent.CountdownCancelled, Input(e));
        Assert.AreEqual("input", e.CancelReason);
        Assert.AreEqual(Phase.Watching, e.Phase);
    }

    [TestMethod]
    public void NeverCancelsCountdown()
    {
        var e = Engine();
        Idle(e, 56, 1);
        Idle(e, 57, 1);
        Assert.AreEqual(EngineEvent.CountdownCancelled, Idle(e, 58, null));
        Assert.AreEqual("never", e.CancelReason);
        Assert.AreEqual(Phase.Watching, e.Phase);
    }

    [TestMethod]
    public void NeverKeepsItUnlockedUntilLifted()
    {
        var e = Engine();
        Assert.IsEmpty(Run(e, 0, 7200, null), "never holds for two hours");
        Assert.AreEqual(Phase.Watching, e.Phase);
    }

    [TestMethod]
    public void AnExtraDuringTheCountdownCancelsIt()
    {
        var e = Engine(15);
        Run(e, 1780, 1786, 30);
        Assert.AreEqual(Phase.Countdown, e.Phase);
        Assert.AreEqual(EngineEvent.CountdownCancelled, Idle(e, 1787, 90), "music started: +60");
        Assert.AreEqual("later", e.CancelReason);
        Assert.AreEqual(Phase.Watching, e.Phase);
        Assert.AreEqual((5385.0, EngineEvent.CountdownStarted), Run(e, 1788, 5400, 90).First(), "counts down again before the new lock time");
    }

    [TestMethod]
    public void AnExtraShorterThanTheCountdownMovesItsEnd()
    {
        // A 2 min warning with a 30 min lock time starts at 28:00. A +1 min extra at 29:30 leaves the
        // new start (29:00) behind the idle time, so the countdown goes on, to the new lock time.
        var e = Engine(120);
        Run(e, 1670, 1680, 30);
        Assert.AreEqual(Phase.Countdown, e.Phase);
        Run(e, 1681, 1769, 30);
        var events = Run(e, 1770, 1900, 31);
        Assert.IsFalse(events.Any(x => x.Event == EngineEvent.CountdownCancelled));
        Assert.AreEqual((1860.0, EngineEvent.WentAway), events.Last(), "locks at 31:00, not at 30:00");
    }

    [TestMethod]
    public void AFallingLockTimeNeverShortensARunningCountdown()
    {
        // A 10 min warning with 30 min starts at 20:00. Unplugged at 22:00 with 50% sooner: 15 min,
        // below the idle time. The countdown still ends where it was going to.
        var e = Engine(600);
        Run(e, 1190, 1200, 30);
        Assert.AreEqual(Phase.Countdown, e.Phase);
        Run(e, 1201, 1319, 30);
        var events = Run(e, 1320, 1800, 15);
        Assert.AreEqual((1800.0, EngineEvent.WentAway), events.Last());
        Assert.AreEqual(1, events.Count(x => x.Event == EngineEvent.WentAway));
    }

    [TestMethod]
    public void ExtrasMoveTheCountdown()
    {
        var events = Run(Engine(15), 0, 5400, 90);
        Assert.AreEqual((5385.0, EngineEvent.CountdownStarted), events.First());
        Assert.AreEqual((5400.0, EngineEvent.WentAway), events.Last());
    }

    [TestMethod]
    public void RaisingTheLockTimeCancelsCountdown()
    {
        var e = Engine();
        Idle(e, 56, 1);
        Idle(e, 57, 1);
        Assert.AreEqual(EngineEvent.CountdownCancelled, Idle(e, 58, 2));
        Assert.AreEqual("later", e.CancelReason);
        Assert.AreEqual(Phase.Watching, e.Phase);
        Assert.AreEqual(EngineEvent.None, Idle(e, 59, 2), "no new countdown in the next step");
        Assert.AreEqual((117.0, EngineEvent.CountdownStarted), Run(e, 60, 120, 2).First());
    }

    [TestMethod]
    public void InputAfterGoingAwayMeansBack()
    {
        var e = Engine(0);
        Idle(e, 59, 1);
        Assert.AreEqual(EngineEvent.WentAway, Idle(e, 60, 1));
        Assert.AreEqual(EngineEvent.CameBack, Input(e));
        Assert.AreEqual(Phase.Watching, e.Phase);
        Assert.AreEqual(EngineEvent.None, Input(e));
    }

    [TestMethod]
    public void PausedNeverLocks()
    {
        var e = Engine();
        Assert.AreEqual(EngineEvent.None, e.Step(6000, false, 1, true, false));
        Assert.AreEqual(Phase.Watching, e.Phase);
    }

    [TestMethod]
    public void PausingCancelsCountdown()
    {
        var e = Engine();
        Idle(e, 56, 1);
        Idle(e, 57, 1);
        Assert.AreEqual(EngineEvent.CountdownCancelled, e.Step(58, false, 1, true, false));
        Assert.AreEqual("paused", e.CancelReason);
    }

    [TestMethod]
    public void NoWarningGoesAwayAtTheLockTime()
    {
        var e = Engine(0);
        Assert.AreEqual(EngineEvent.None, Idle(e, 59, 1));
        Assert.AreEqual(EngineEvent.WentAway, Idle(e, 60, 1));
        Assert.AreEqual(Phase.Away, e.Phase);
    }

    [TestMethod]
    public void AwayUntilInputOrReset()
    {
        var e = Engine(0);
        Idle(e, 59, 1);
        Idle(e, 60, 1);
        Assert.AreEqual(EngineEvent.None, Idle(e, 120, 1));
        e.Reset();
        Assert.AreEqual(Phase.Watching, e.Phase);
        Assert.AreEqual(EngineEvent.None, Idle(e, 121, 1), "after a reset the idle time is already past the lock time: one more step first");
        Assert.AreEqual(EngineEvent.WentAway, Idle(e, 122, 1));
    }

    [TestMethod]
    public void ASessionLockEndsARunningCountdownAtOnce()
    {
        var e = Engine();
        Idle(e, 56, 1);
        Assert.AreEqual(EngineEvent.CountdownStarted, Idle(e, 57, 1));

        Assert.IsTrue(e.SessionLocked());
        Assert.AreEqual(Phase.Away, e.Phase);
        Assert.AreEqual("session locked", e.CancelReason);
        Assert.AreEqual(EngineEvent.None, e.Step(58, false, 1, false, true), "the step while locked cancels nothing more");
        Assert.IsFalse(e.SessionLocked(), "no countdown the second time");
        Assert.IsFalse(e.Reset());
        Assert.AreEqual(Phase.Watching, e.Phase);
    }

    [TestMethod]
    public void AResetSaysWhenItEndedACountdown()
    {
        var e = Engine();
        Idle(e, 56, 1);
        Idle(e, 57, 1);

        Assert.IsTrue(e.Reset());
        Assert.AreEqual("session unlocked", e.CancelReason);
        Assert.IsFalse(e.Reset());
        Assert.AreEqual(EngineEvent.None, Idle(e, 1, 1), "watching from the start again");
    }

    [TestMethod]
    public void GoAwayFromTheMenu()
    {
        var e = Engine();
        e.GoAway();
        Assert.AreEqual(Phase.Away, e.Phase);
        Assert.AreEqual(EngineEvent.None, Idle(e, 10, 1));
        Assert.AreEqual(EngineEvent.CameBack, Input(e));
    }

    [TestMethod]
    public void LockedSessionHoldsEverything()
    {
        var e = Engine();
        Idle(e, 56, 1);
        Idle(e, 57, 1);
        Assert.AreEqual(EngineEvent.CountdownCancelled, e.Step(58, false, 1, false, true));
        Assert.AreEqual("session locked", e.CancelReason);
        Assert.AreEqual(Phase.Away, e.Phase);
        Assert.AreEqual(EngineEvent.None, e.Step(6000, false, 1, false, true));
    }

    [TestMethod]
    public void PeekChangesNothing()
    {
        var e = Engine();
        Idle(e, 56, 1);
        Assert.AreEqual(EngineEvent.CountdownStarted, e.Peek(57, false, 1, false, false));
        Assert.AreEqual(EngineEvent.CountdownStarted, e.Peek(57, false, 1, false, false));
        Assert.AreEqual(Phase.Watching, e.Phase);
        Assert.AreEqual(EngineEvent.None, e.Peek(57, false, null, false, false));
        Assert.AreEqual(EngineEvent.CountdownStarted, Idle(e, 57, 1));
    }

    // The late starts: the lock time drops to or below the idle time already reached. The countdown
    // still runs its whole length and never locks on its first step (D1).

    [TestMethod]
    public void MusicThatEndsAtFiftyMinutesGivesAFullCountdownNotALock()
    {
        // Normal 30 with music +60 is 90 min; the music ends at idle 50:00.
        var e = Engine(15);
        Assert.IsEmpty(Run(e, 0, 2999, 90));
        Assert.AreEqual(EngineEvent.CountdownStarted, Idle(e, 3000, 30));
        Assert.AreEqual(15, e.CountdownRemaining);
        var events = Run(e, 3001, 3020, 30);
        Assert.AreEqual((3015.0, EngineEvent.WentAway), events.Last(), "locks at 50:15, not at 50:00");
    }

    [TestMethod]
    public void ACallThatEndsAfterThreeHoursGivesTheFullCountdown()
    {
        var e = Engine(15);
        Assert.IsEmpty(Run(e, 0, 10799, null));
        Assert.AreEqual(EngineEvent.CountdownStarted, Idle(e, 10800, 30));
        Assert.AreEqual(15, e.CountdownRemaining);
        Assert.AreEqual((10815.0, EngineEvent.WentAway), Run(e, 10801, 10830, 30).Last());
    }

    [TestMethod]
    public void ABatteryCapThatAppearsAtTwentyMinutesGivesTheFullCountdown()
    {
        var e = Engine(15);
        Assert.IsEmpty(Run(e, 0, 1199, 30));
        Assert.AreEqual(EngineEvent.CountdownStarted, Idle(e, 1200, 10), "always lock after 10 min, on battery");
        Assert.AreEqual(15, e.CountdownRemaining);
        Assert.AreEqual((1215.0, EngineEvent.WentAway), Run(e, 1201, 1230, 10).Last());
    }

    [TestMethod]
    public void APauseThatEndsPastTheLockTimeGivesTheFullCountdown()
    {
        var e = Engine(15);
        for (double idle = 0; idle < 2000; idle++) Assert.AreEqual(EngineEvent.None, e.Step(idle, false, 30, true, false));
        Assert.AreEqual(EngineEvent.CountdownStarted, Idle(e, 2000, 30));
        Assert.AreEqual((2015.0, EngineEvent.WentAway), Run(e, 2001, 2030, 30).Last());
    }

    [TestMethod]
    public void TheFirstStepAfterStartingUpPastTheLockTimeGivesTheFullCountdown()
    {
        var e = Engine(15);
        Assert.AreEqual(EngineEvent.CountdownStarted, Idle(e, 3600, 30));
        Assert.AreEqual(15, e.CountdownRemaining);
        Assert.AreEqual(3615, e.CountdownEnd);
    }

    [TestMethod]
    public void ALockTimeLoweredBelowTheIdleTimeGivesTheFullCountdown()
    {
        var e = Engine(15);
        Run(e, 0, 1000, 30);
        Assert.AreEqual(EngineEvent.CountdownStarted, Idle(e, 1001, 10), "the settings now say 10 min");
        Assert.AreEqual(1016, e.CountdownEnd);
    }

    [TestMethod]
    public void OneStepOfAMissingNeverGivesACountdownThenACancel()
    {
        var e = Engine(15);
        Run(e, 0, 10000, null);
        Assert.AreEqual(EngineEvent.CountdownStarted, Idle(e, 10001, 30));
        Assert.AreEqual(EngineEvent.CountdownCancelled, Idle(e, 10002, null));
        Assert.IsEmpty(Run(e, 10003, 20000, null));
    }

    [TestMethod]
    public void WithNoWarningALateLockWaitsForOneMoreStep()
    {
        var e = Engine(0);
        Run(e, 0, 10000, null);
        Assert.AreEqual(EngineEvent.None, Idle(e, 10001, 30), "one reading alone does not lock");
        Assert.AreEqual(EngineEvent.None, Idle(e, 10002, null), "and the next one says never again");
        Assert.AreEqual(Phase.Watching, e.Phase);
        Assert.AreEqual(EngineEvent.None, Idle(e, 10003, 30));
        Assert.AreEqual(EngineEvent.WentAway, Idle(e, 10004, 30), "two readings in a row");
    }
}
