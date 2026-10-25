namespace SteppedOut.Tests;

[TestClass]
public class InputTrackerTests
{
    static readonly IntPtr Window = new(1);

    static Native.POINT At(int x, int y) => new() { X = x, Y = y };

    // First reading at 0 ms with the cursor at 100,100 and Windows' last input tick at 1.
    static InputTracker Primed()
    {
        var t = new InputTracker { JitterPixels = 5 };
        t.Sample(0, 0, 1, Window, At(100, 100));
        return t;
    }

    [TestMethod]
    public void AKeyWithAStillCursorCounts()
    {
        var t = Primed();
        t.SampleCursor(At(100, 100));
        var s = t.Sample(1000, 200, 2, Window, At(100, 100));
        Assert.IsTrue(s.RealInput);
        Assert.AreEqual(0.2, s.IdleSeconds, 1e-9);
    }

    // Input another program stamped 219 ms ahead of this PC's tick count, read at once.
    const uint AheadOfTheClock = uint.MaxValue - 218;

    [TestMethod]
    public void InputStampedAheadOfTheClockIsJustNow()
    {
        var t = Primed();
        t.SampleCursor(At(100, 100));
        var s = t.Sample(1000, AheadOfTheClock, 2, Window, At(100, 100));
        Assert.IsTrue(s.RealInput);
        Assert.AreEqual(0.0, s.IdleSeconds, 1e-9);

        // A second later the stamp is 781 ms in the past, and nothing new came in.
        t.SampleCursor(At(100, 100));
        Assert.AreEqual(1.0, t.Sample(2000, 781, 2, Window, At(100, 100)).IdleSeconds, 1e-9);
    }

    [TestMethod]
    public void InputStampedAheadOfTheClockIsNotDaysOfIdleOnTheFirstReading()
    {
        var s = new InputTracker().Sample(0, AheadOfTheClock, 1, Window, At(100, 100));
        Assert.AreEqual(0.0, s.RawIdleSeconds, 1e-9);
        Assert.AreEqual(0.0, s.IdleSeconds, 1e-9);
    }

    [TestMethod]
    public void AHandOnTheMouseCounts()
    {
        var t = Primed();
        t.SampleCursor(At(160, 100));
        Assert.IsTrue(t.Sample(1000, 200, 2, Window, At(160, 100)).RealInput);
    }

    [TestMethod]
    public void ATwitchJustAfterASampleIsStillMeasured()
    {
        var t = Primed();
        t.SampleCursor(At(100, 100));
        Assert.IsFalse(t.Sample(1000, 1000, 1, Window, At(100, 100)).RealInput);

        // The sensor moves the cursor 2 px before the next 100 ms reading, then sits still.
        t.SampleCursor(At(102, 100));
        t.SampleCursor(At(102, 100));
        var s = t.Sample(2000, 950, 2, Window, At(102, 100));

        Assert.IsFalse(s.RealInput, "a 2 px move is a twitch, not a key press");
        Assert.AreEqual(2.0, s.IdleSeconds, 1e-9);
        Assert.AreEqual(1, t.IgnoredSinceLastInput);
    }

    [TestMethod]
    public void TheClickThatSteppedOutIsNotComingBack()
    {
        var t = Primed();
        t.SampleCursor(At(300, 400));
        t.IgnoreInputSoFar(2, Window, At(300, 400));
        var s = t.Sample(1000, 100, 2, Window, At(300, 400));
        Assert.IsFalse(s.RealInput);
        Assert.AreEqual(1.0, s.IdleSeconds, 1e-9);

        t.SampleCursor(At(360, 400));
        Assert.IsTrue(t.Sample(2000, 100, 3, Window, At(360, 400)).RealInput, "what comes after it still counts");
    }

    // Input Windows does not report, such as a game controller's, noted by whoever saw it.
    [TestMethod]
    public void NotedInputCountsOnceFromWhenItWasMade()
    {
        var t = Primed();
        t.NoteInput(4600);
        t.NoteInput(4800);
        t.NoteInput(4700);
        t.SampleCursor(At(100, 100));
        var s = t.Sample(5000, 5000, 1, Window, At(100, 100));
        Assert.IsTrue(s.RealInput, "Windows saw nothing for 5 s, the controller did");
        Assert.AreEqual(0.2, s.IdleSeconds, 1e-9, "from the latest one noted");

        t.SampleCursor(At(100, 100));
        var next = t.Sample(6000, 6000, 1, Window, At(100, 100));
        Assert.IsFalse(next.RealInput, "it counts once");
        Assert.AreEqual(1.2, next.IdleSeconds, 1e-9);
    }

    [TestMethod]
    public void NotedInputLeavesTheJitterFilterAlone()
    {
        var t = Primed();
        t.NoteInput(500);
        t.SampleCursor(At(100, 100));
        Assert.IsTrue(t.Sample(1000, 1000, 1, Window, At(100, 100)).RealInput);

        // The sensor moves the cursor 2 px and Windows reports it: still a twitch.
        t.SampleCursor(At(102, 100));
        var s = t.Sample(2000, 100, 2, Window, At(102, 100));
        Assert.IsFalse(s.RealInput);
        Assert.AreEqual(1.5, s.IdleSeconds, 1e-9, "idle since the noted input");
        Assert.AreEqual(1, t.IgnoredSinceLastInput);
    }

    [TestMethod]
    public void InputNotedBeforeSteppingOutIsNotComingBack()
    {
        var t = Primed();
        t.NoteInput(900);
        t.IgnoreInputSoFar(1, Window, At(100, 100));
        Assert.IsFalse(t.Sample(1000, 1000, 1, Window, At(100, 100)).RealInput);
    }
}
