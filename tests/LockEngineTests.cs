namespace SteppedOut.Tests;

[TestClass]
public class LockEngineTests
{
    static LockEngine Engine(double thresholdSeconds = 60, int graceSeconds = 3) => new(thresholdSeconds, graceSeconds);

    static EngineEvent Idle(LockEngine e, double seconds, string? suppressor = null) => e.Step(seconds, false, suppressor, false, false);

    [TestMethod]
    public void StaysWatchingBelowThreshold()
    {
        var e = Engine();
        Assert.AreEqual(EngineEvent.None, Idle(e, 59));
        Assert.AreEqual(Phase.Watching, e.Phase);
    }

    [TestMethod]
    public void StartsCountdownAtThreshold()
    {
        var e = Engine();
        Assert.AreEqual(EngineEvent.CountdownStarted, Idle(e, 60));
        Assert.AreEqual(Phase.Countdown, e.Phase);
        Assert.AreEqual(3, e.CountdownRemaining);
    }

    [TestMethod]
    public void CountsDownThenRequestsLockOnce()
    {
        var e = Engine();
        Idle(e, 60);
        Assert.AreEqual(EngineEvent.CountdownTick, Idle(e, 61));
        Assert.AreEqual(2, e.CountdownRemaining);
        Assert.AreEqual(EngineEvent.CountdownTick, Idle(e, 62));
        Assert.AreEqual(EngineEvent.LockRequested, Idle(e, 63));
        Assert.AreEqual(Phase.Locked, e.Phase);
        Assert.AreEqual(EngineEvent.None, Idle(e, 64));
        Assert.AreEqual(EngineEvent.None, Idle(e, 3600));
    }

    [TestMethod]
    public void InputCancelsCountdown()
    {
        var e = Engine();
        Idle(e, 60);
        Assert.AreEqual(EngineEvent.CountdownCancelled, e.Step(0, true, null, false, false));
        Assert.AreEqual("input", e.CancelReason);
        Assert.AreEqual(Phase.Watching, e.Phase);
    }

    [TestMethod]
    public void SuppressorCancelsCountdown()
    {
        var e = Engine();
        Idle(e, 60);
        Assert.AreEqual(EngineEvent.CountdownCancelled, Idle(e, 61, "audio playing"));
        Assert.AreEqual("audio playing", e.CancelReason);
        Assert.AreEqual(Phase.Watching, e.Phase);
    }

    [TestMethod]
    public void SuppressorHoldsCountdownUntilLifted()
    {
        var e = Engine();
        Assert.AreEqual(EngineEvent.SuppressionChanged, Idle(e, 60, "fullscreen"));
        Assert.AreEqual(EngineEvent.None, Idle(e, 61, "fullscreen"));
        Assert.AreEqual(EngineEvent.SuppressionChanged, Idle(e, 62, "audio playing"));
        Assert.AreEqual(EngineEvent.CountdownStarted, Idle(e, 63));
    }

    [TestMethod]
    public void PausedNeverLocks()
    {
        var e = Engine();
        Assert.AreEqual(EngineEvent.None, e.Step(6000, false, null, true, false));
        Assert.AreEqual(Phase.Watching, e.Phase);
    }

    [TestMethod]
    public void PausingCancelsCountdown()
    {
        var e = Engine();
        Idle(e, 60);
        Assert.AreEqual(EngineEvent.CountdownCancelled, e.Step(61, false, null, true, false));
        Assert.AreEqual("paused", e.CancelReason);
    }

    [TestMethod]
    public void ZeroGraceLocksImmediately()
    {
        var e = Engine(graceSeconds: 0);
        Assert.AreEqual(EngineEvent.LockRequested, Idle(e, 60));
        Assert.AreEqual(Phase.Locked, e.Phase);
    }

    [TestMethod]
    public void LockedUntilInputOrReset()
    {
        var e = Engine(graceSeconds: 0);
        Idle(e, 60);
        Assert.AreEqual(EngineEvent.None, Idle(e, 120));
        e.Reset();
        Assert.AreEqual(Phase.Watching, e.Phase);
        Assert.AreEqual(EngineEvent.LockRequested, Idle(e, 121));
        Assert.AreEqual(EngineEvent.None, e.Step(0, true, null, false, false));
        Assert.AreEqual(Phase.Watching, e.Phase);
    }

    [TestMethod]
    public void LockedSessionHoldsEverything()
    {
        var e = Engine();
        Idle(e, 60);
        Assert.AreEqual(EngineEvent.CountdownCancelled, e.Step(61, false, null, false, true));
        Assert.AreEqual("session locked", e.CancelReason);
        Assert.AreEqual(Phase.Locked, e.Phase);
        Assert.AreEqual(EngineEvent.None, e.Step(6000, false, null, false, true));
    }
}
