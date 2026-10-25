using Microsoft.Win32;

namespace SteppedOut.Tests;

// Lock now after Windows turned the lock down twice for the same away: nothing is asking for the
// lock any more, so Lock now asks again, with one more try after the wait, as the first request had.
// Nothing here locks: the rig counts requests and turns each down.
[TestClass]
public class LockNowAgainTests
{
    static Config Settings() => new()
    {
        IdleMinutes = 15,
        GraceSeconds = 15,
        MusicVideo = Effect.None,
        FullScreen = Effect.None,
        OnCall = Effect.None,
    };

    static double Retry => Watcher.LockRetryAfter.TotalSeconds;

    [TestMethod]
    public void LockNowAsksAgainAfterTwoRefusals()
    {
        using var rig = new Rig(Settings()) { LockFails = true };
        rig.Until(900 + Retry + 1);
        Assert.AreEqual(2, rig.LockRequests, "the lock and its one retry, both turned down");

        rig.Watcher.LockNow();
        Assert.AreEqual(3, rig.LockRequests, "Lock now asks at once");
        rig.Watcher.LockNow();
        Assert.AreEqual(3, rig.LockRequests, "a second click while that request waits for its retry changes nothing");

        rig.Until(900 + 2 * Retry + 2);
        Assert.AreEqual(4, rig.LockRequests, "one more try after the wait");
        rig.Until(4000);
        Assert.AreEqual(4, rig.LockRequests, "and then it stops, never a loop");
        Assert.AreEqual(1, rig.Count("away"), "the away actions ran once");
        Assert.AreEqual(0, rig.Count("back"));
        Assert.AreEqual(LockState.Away, rig.Watcher.LockStatus.Kind);
    }

    [TestMethod]
    public void LockNowAfterTwoRefusalsCanLockAfterAll()
    {
        using var rig = new Rig(Settings()) { LockFails = true };
        rig.Until(900 + Retry + 1);
        rig.LockFails = false;

        rig.Watcher.LockNow();
        Assert.AreEqual(3, rig.LockRequests);
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionLock);
        rig.Watcher.LockNow();
        rig.Until(2000);
        Assert.AreEqual(3, rig.LockRequests, "locked: nothing more is asked");
        Assert.AreEqual(LockState.SessionLocked, rig.Watcher.LockStatus.Kind);
    }

    // While the retry is still to come, something is still asking, so Lock now changes nothing, as
    // before; so does a request Windows took that has not locked yet.
    [TestMethod]
    public void LockNowStillChangesNothingWhileALockIsOnItsWay()
    {
        using var waiting = new Rig(Settings()) { LockFails = true };
        waiting.Until(901);
        waiting.Watcher.LockNow();
        Assert.AreEqual(1, waiting.LockRequests, "the retry is still to come");

        using var taken = new Rig(Settings());
        taken.Until(901);
        taken.Watcher.LockNow();
        taken.Until(1000);
        Assert.AreEqual(1, taken.LockRequests, "Windows took it");
    }

    // Back at the PC and away again: the new away starts over, with its own request and retry.
    [TestMethod]
    public void ANewAwayStartsOver()
    {
        using var rig = new Rig(Settings()) { LockFails = true };
        rig.Until(900 + Retry + 1);
        rig.Second(() => rig.Input = true);
        rig.Busy();
        Assert.AreEqual(1, rig.Count("back"));

        rig.Watcher.LockNow();
        Assert.AreEqual(3, rig.LockRequests, "a new away with Lock now asks once");
        rig.Watcher.LockNow();
        Assert.AreEqual(3, rig.LockRequests, "and a second click is the same away");
    }

    // Lock now while paused, turned down: the pause puts the engine back to watching, but the away
    // still asked for a lock, so the one retry comes as it does without the pause, and after a
    // second refusal Lock now asks again.
    [TestMethod]
    public void LockNowWhilePausedAndTurnedDownIsAskedOnceMore()
    {
        using var rig = new Rig(Settings()) { LockFails = true };
        rig.Until(10);
        rig.Watcher.Pause(TimeSpan.FromMinutes(1));
        rig.Watcher.LockNow();
        Assert.AreEqual(1, rig.LockRequests);

        rig.Until(10 + Retry + 1);
        Assert.AreEqual(2, rig.LockRequests, "the one retry, as without the pause");
        rig.Watcher.LockNow();
        Assert.AreEqual(3, rig.LockRequests, "turned down twice, so Lock now asks again");
        rig.Until(4000);
        Assert.AreEqual(4, rig.LockRequests, "with its one retry, and then it stops");
        Assert.AreEqual(1, rig.Count("away"), "the away actions ran once");
    }

    // Turned down once during a pause, then taken: the retry locks the PC, instead of the status
    // saying Away for hours while nothing asks for the lock again.
    [TestMethod]
    public void LockNowWhilePausedTurnedDownOnceLocksOnTheRetry()
    {
        using var rig = new Rig(Settings()) { LockFails = true };
        rig.Until(10);
        rig.Watcher.Pause(TimeSpan.FromMinutes(1));
        rig.Watcher.LockNow();
        rig.LockFails = false;

        rig.Until(10 + Retry + 1);
        Assert.AreEqual(2, rig.LockRequests, "asked once more, and Windows took it");
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionLock);
        rig.Until(3 * 3600);
        Assert.AreEqual(2, rig.LockRequests, "locked: nothing more is asked");
        Assert.AreEqual(LockState.SessionLocked, rig.Watcher.LockStatus.Kind);
    }

    // A dry run never asks Windows, so it never has a lock turned down to ask again for.
    [TestMethod]
    public void ADryRunNeverAsks()
    {
        using var rig = new Rig(Settings(), dryRun: true) { LockFails = true };
        rig.Until(1000);
        rig.Watcher.LockNow();
        rig.Until(1100);
        Assert.AreEqual(0, rig.LockRequests);
    }
}
