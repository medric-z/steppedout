namespace SteppedOut.Tests;

[TestClass]
public class ScreensOffTests
{
    [TestMethod]
    public void TheScreensGoOffAgainOnlyAfterAMovementThenTenQuietSeconds()
    {
        uint offAt = 1_000_000;

        Assert.IsFalse(ScreensOff.WokenThenQuiet(offAt, offAt, offAt + 60_000), "nothing has woken them");
        Assert.IsFalse(ScreensOff.WokenThenQuiet(offAt, offAt + 5_000, offAt + 9_000), "the twitch was 4 s ago");
        Assert.IsTrue(ScreensOff.WokenThenQuiet(offAt, offAt + 5_000, offAt + 15_000));
    }

    [TestMethod]
    public void QuietIsStillMeasuredRightWhenTheTickClockWraps()
    {
        uint twitch = uint.MaxValue - 2_000;

        Assert.IsFalse(ScreensOff.WokenThenQuiet(uint.MaxValue - 60_000, twitch, unchecked(twitch + 5_000)));
        Assert.IsTrue(ScreensOff.WokenThenQuiet(uint.MaxValue - 60_000, twitch, unchecked(twitch + 10_000)));
    }

    [TestMethod]
    public void InputStampedAheadOfTheClockIsNotQuietTime()
    {
        uint offAt = 1_000_000;
        uint now = offAt + 30_000;

        Assert.IsFalse(ScreensOff.WokenThenQuiet(offAt, now + 50, now), "stamped 50 ms ahead");
        Assert.IsTrue(ScreensOff.WokenThenQuiet(offAt, now + 50, now + 10_050));
    }

    // Screens that only count, on a tick clock the test sets.
    sealed class Screens : IScreens
    {
        public int Offs { get; private set; }

        public uint? Input { get; set; } = 1_000;

        public uint Now { get; set; } = 1_000;

        public bool TurnOff()
        {
            Offs++;
            return true;
        }

        public uint? LastInputTick() => Input;
    }

    [TestMethod]
    public void WithALockTheScreensGoOffOnceTheLockScreenHasFadedIn()
    {
        var time = new AwayFakes.ManualTime();
        var screens = new Screens();
        var log = new List<string>();
        var off = new ScreensOff(screens, time, log.Add);

        var leaving = Task.Run(() => off.Leave(locking: true, CancellationToken.None));
        time.WaitForPending(1);
        time.Advance(TimeSpan.FromSeconds(1.9));
        Assert.AreEqual(0, screens.Offs);
        time.Advance(TimeSpan.FromSeconds(0.1));
        AwayFakes.Wait(leaving, "turned off");

        Assert.AreEqual(1, screens.Offs);
        Assert.AreEqual(0, time.Pending, "with a lock, nothing watches afterwards");
        CollectionAssert.AreEqual(new[] { "screens off" }, log);
    }

    [TestMethod]
    public void BackDuringTheWaitLeavesTheScreensOn()
    {
        var time = new AwayFakes.ManualTime();
        var screens = new Screens();
        var log = new List<string>();
        var off = new ScreensOff(screens, time, log.Add);
        using var back = new CancellationTokenSource();

        var leaving = Task.Run(() => off.Leave(locking: true, back.Token));
        time.WaitForPending(1);
        back.Cancel();
        AwayFakes.Wait(leaving, "gave up");

        Assert.AreEqual(0, screens.Offs);
        CollectionAssert.AreEqual(new[] { "screens left on: already back" }, log);
    }

    [TestMethod]
    public void WithoutALockAWakeThatIsNotComingBackIsTurnedOffAgainAfterTenQuietSeconds()
    {
        var time = new AwayFakes.ManualTime();
        var screens = new Screens();
        var log = new List<string>();
        var off = new ScreensOff(screens, time, log.Add);
        using var back = new CancellationTokenSource();

        var leaving = Task.Run(() => off.Leave(locking: false, back.Token));
        time.WaitForPending(1);
        time.Advance(TimeSpan.FromSeconds(3.5));
        AwayFakes.Wait(leaving, "turned off");
        Assert.AreEqual(1, screens.Offs);
        Assert.AreEqual(1, time.Pending, "watching");

        // A bumped desk wakes them; ten quiet seconds later they go off again.
        screens.Input = 5_000;
        screens.Now = 6_000;
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.AreEqual(1, screens.Offs);
        screens.Now = 15_000;
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.AreEqual(2, screens.Offs);

        back.Cancel();
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.AreEqual(0, time.Pending, "coming back stops the watch");
        CollectionAssert.AreEqual(new[] { "screens off", "screens off again: what woke them did not count as coming back" }, log);
    }
}
