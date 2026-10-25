namespace SteppedOut.Tests;

// Made-up controller readings only: nothing here reads or touches a real controller, except the
// last test, which only asks XInput whether slot 0 has one.
[TestClass]
public class ControllerInputTests
{
    sealed class FakePads : IPads
    {
        public readonly PadState?[] Slot = new PadState?[4];
        public readonly int[] Reads = new int[4];
        public bool Throw;

        public bool TryRead(int slot, out PadState state)
        {
            Reads[slot]++;
            if (Throw) throw new InvalidOperationException("pad");
            state = Slot[slot] ?? default;
            return Slot[slot] is not null;
        }
    }

    static readonly PadState Rest = new(1, 0, 0, 0, 0, 0, 0, 0);

    static (ControllerInput Input, FakePads Pads, List<long> Noted) Make()
    {
        var pads = new FakePads();
        var noted = new List<long>();
        return (new ControllerInput(noted.Add, pads), pads, noted);
    }

    // A pad in slot 0 that has been read once at rest, so the next reading is compared with it.
    static (ControllerInput Input, FakePads Pads, List<long> Noted) Connected()
    {
        var made = Make();
        made.Pads.Slot[0] = Rest;
        Assert.IsFalse(made.Input.Poll(0));
        Assert.IsTrue(made.Input.IsConnected(0));
        return made;
    }

    static bool Next(ControllerInput input, FakePads pads, PadState state, long at)
    {
        pads.Slot[0] = state;
        return input.Poll(at);
    }

    [TestMethod]
    public void DriftInsideTheDeadZoneIsNoInput()
    {
        var (input, pads, noted) = Connected();
        // A drifting stick: new packets and small changes, all inside the dead zone.
        var drift = new (short X, short Y)[] { (1200, -800), (3100, 2200), (-4000, 5000), (5500, -5500), (7000, 3000), (-7800, 0) };
        long at = 250;
        uint packet = 2;
        foreach (var (x, y) in drift)
        {
            Assert.IsFalse(Next(input, pads, Rest with { Packet = packet++, LeftX = x, LeftY = y }, at));
            Assert.IsFalse(Next(input, pads, Rest with { Packet = packet++, RightX = y, RightY = x }, at += 250));
            at += 250;
        }
        Assert.IsEmpty(noted);
    }

    [TestMethod]
    public void ANewPacketNumberAloneIsNoInput()
    {
        var (input, pads, noted) = Connected();
        for (uint p = 2; p < 50; p++) Assert.IsFalse(Next(input, pads, Rest with { Packet = p }, p * 250));
        Assert.IsEmpty(noted);
    }

    [TestMethod]
    public void AButtonPressCountsAndSoDoesLettingGo()
    {
        var (input, pads, noted) = Connected();
        const ushort A = 0x1000;
        Assert.IsTrue(Next(input, pads, Rest with { Packet = 2, Buttons = A }, 4600));
        Assert.IsFalse(Next(input, pads, Rest with { Packet = 2, Buttons = A }, 4850), "held, nothing new");
        Assert.IsTrue(Next(input, pads, Rest with { Packet = 3 }, 5100));
        CollectionAssert.AreEqual(new long[] { 4600, 5100 }, noted);
    }

    [TestMethod]
    public void AStickPushedPastTheDeadZoneCounts()
    {
        var (input, pads, noted) = Connected();
        Assert.IsTrue(Next(input, pads, Rest with { Packet = 2, LeftX = 20000 }, 250));
        Assert.IsTrue(Next(input, pads, Rest with { Packet = 3, LeftX = 20000, LeftY = -15000 }, 500));
        Assert.IsTrue(Next(input, pads, Rest with { Packet = 4, RightY = 32767 }, 750));
        Assert.HasCount(3, noted);
    }

    [TestMethod]
    public void AStickHeldStillPastTheDeadZoneCountsOnlyWhenItMoved()
    {
        var (input, pads, noted) = Connected();
        Assert.IsTrue(Next(input, pads, Rest with { Packet = 2, LeftX = 20000 }, 250));
        // A worn stick resting past the dead zone, wobbling a little: no input.
        Assert.IsFalse(Next(input, pads, Rest with { Packet = 3, LeftX = 20040, LeftY = 30 }, 500));
        Assert.IsFalse(Next(input, pads, Rest with { Packet = 4, LeftX = 19980, LeftY = -20 }, 750));
        Assert.IsFalse(Next(input, pads, Rest with { Packet = 5, LeftX = 20000 }, 1000));
        Assert.HasCount(1, noted);
    }

    [TestMethod]
    public void TheDeadZoneIsRound()
    {
        var (input, pads, _) = Connected();
        // 6000 on each axis is inside the square but past the round dead zone (8485 from the middle).
        Assert.IsTrue(Next(input, pads, Rest with { Packet = 2, LeftX = 6000, LeftY = 6000 }, 250));
        // Back to the middle is inside the dead zone: letting go of the stick is not counted.
        Assert.IsFalse(Next(input, pads, Rest with { Packet = 3 }, 500));
        // The right stick has the larger dead zone: 8000 is inside it.
        Assert.IsFalse(Next(input, pads, Rest with { Packet = 4, RightX = 8000 }, 750));
        Assert.IsTrue(Next(input, pads, Rest with { Packet = 5, RightX = 10000 }, 1000));
    }

    [TestMethod]
    public void ATriggerCountsPast30()
    {
        var (input, pads, _) = Connected();
        Assert.IsFalse(Next(input, pads, Rest with { Packet = 2, LeftTrigger = 12 }, 250));
        Assert.IsFalse(Next(input, pads, Rest with { Packet = 3, LeftTrigger = 30 }, 500));
        Assert.IsTrue(Next(input, pads, Rest with { Packet = 4, LeftTrigger = 31 }, 750));
        Assert.IsFalse(Next(input, pads, Rest with { Packet = 5, LeftTrigger = 31 }, 1000), "held still");
        Assert.IsTrue(Next(input, pads, Rest with { Packet = 6, RightTrigger = 255 }, 1250));
        Assert.IsTrue(Next(input, pads, Rest with { Packet = 7 }, 1500), "let go from past 30");
    }

    // A trigger resting part-pulled past 30, as under a controller lying on it, wobbles by a step:
    // that is not someone playing, so the PC can still lock. A real pull past it still counts.
    [TestMethod]
    public void ATriggerRestingPast30ThatOnlyWobblesIsNoInput()
    {
        var (input, pads, noted) = Connected();
        Assert.IsTrue(Next(input, pads, Rest with { Packet = 2, LeftTrigger = 40, RightTrigger = 140 }, 250), "pulled past 30");
        long at = 500;
        uint packet = 3;
        for (int i = 0; i < 40; i++, at += 250)
        {
            byte wobble = (byte)(i % 2 == 0 ? 41 : 40);
            Assert.IsFalse(Next(input, pads, Rest with { Packet = packet++, LeftTrigger = wobble, RightTrigger = (byte)(wobble + 100) }, at), $"poll {i}");
        }
        Assert.HasCount(1, noted, "only the pull counted");
        Assert.IsFalse(Next(input, pads, Rest with { Packet = packet++, LeftTrigger = 44, RightTrigger = 141 }, at += 250), "4 steps is still a wobble");
        Assert.IsTrue(Next(input, pads, Rest with { Packet = packet++, LeftTrigger = 49, RightTrigger = 141 }, at += 250), "5 steps is a pull");
        Assert.IsTrue(Next(input, pads, Rest with { Packet = packet++, LeftTrigger = 30, RightTrigger = 141 }, at += 250), "let go to 30");
        Assert.IsTrue(Next(input, pads, Rest with { Packet = packet++, LeftTrigger = 31, RightTrigger = 141 }, at + 250), "a step past 30 is a pull");
    }

    [TestMethod]
    public void ADisconnectedPadCountsAsNothing()
    {
        var (input, pads, noted) = Make();
        for (long at = 0; at < 20000; at += 250) Assert.IsFalse(input.Poll(at));
        Assert.IsEmpty(noted);
        Assert.IsFalse(input.IsConnected(0));
    }

    [TestMethod]
    public void ConnectingAndReconnectingAreNotInput()
    {
        var (input, pads, noted) = Make();
        // Connected with a button already down and the stick pushed: only a starting point.
        pads.Slot[1] = Rest with { Buttons = 0x1000, LeftX = 30000 };
        Assert.IsFalse(input.Poll(0));
        Assert.IsTrue(input.IsConnected(1));

        pads.Slot[1] = null;
        Assert.IsFalse(input.Poll(250));
        Assert.IsFalse(input.IsConnected(1));

        // Back, in a different state: still only a starting point.
        pads.Slot[1] = Rest with { Buttons = 0x2000 };
        Assert.IsFalse(input.Poll(3250));
        Assert.IsTrue(input.IsConnected(1));
        Assert.IsEmpty(noted);

        pads.Slot[1] = Rest with { Buttons = 0x0000 };
        Assert.IsTrue(input.Poll(3500));
    }

    [TestMethod]
    public void ConnectedPadsAreAskedOftenAndEmptySlotsEvery3Seconds()
    {
        var (input, pads, _) = Make();
        pads.Slot[2] = Rest;
        Assert.IsFalse(input.Poll(0));
        Assert.AreEqual(ControllerInput.ConnectedPollMs, input.NextDelayMs(0));
        for (long at = 250; at < 3000; at += 250) input.Poll(at);
        Assert.AreEqual(12, pads.Reads[2], "every quarter second");
        Assert.AreEqual(1, pads.Reads[0], "empty slots only at the start");
        Assert.AreEqual(1, pads.Reads[3]);
        input.Poll(3000);
        Assert.AreEqual(2, pads.Reads[0], "and again 3 seconds later");
        Assert.AreEqual(13, pads.Reads[2]);
    }

    [TestMethod]
    public void WithNoPadItWaitsForTheNextEmptySlot()
    {
        var (input, pads, _) = Make();
        input.Poll(1000);
        Assert.AreEqual(ControllerInput.EmptyPollMs, input.NextDelayMs(1000));
        Assert.AreEqual(1000, input.NextDelayMs(3000));
        Assert.AreEqual(1, input.NextDelayMs(4000));
        Assert.AreEqual(1, input.NextDelayMs(9000));
    }

    [TestMethod]
    public void AFailingReadIsNoInput()
    {
        var (input, pads, noted) = Connected();
        pads.Throw = true;
        Assert.IsFalse(input.Poll(250));
        Assert.IsFalse(input.IsConnected(0));
        Assert.IsEmpty(noted);
    }

    [TestMethod]
    public void UseFromTwoPadsInOneRoundIsNotedOnce()
    {
        var (input, pads, noted) = Make();
        pads.Slot[0] = Rest;
        pads.Slot[3] = Rest;
        input.Poll(0);
        pads.Slot[0] = Rest with { Buttons = 1 };
        pads.Slot[3] = Rest with { Buttons = 2 };
        Assert.IsTrue(input.Poll(250));
        CollectionAssert.AreEqual(new long[] { 250 }, noted);
    }

    // Controller use reaches the idle clock through InputTracker.NoteInput, and the jitter filter
    // never sees it.
    [TestMethod]
    public void UseFeedsTheInputTracker()
    {
        var tracker = new InputTracker { JitterPixels = 5 };
        var window = new IntPtr(1);
        var cursor = new Native.POINT { X = 100, Y = 100 };
        tracker.Sample(0, 0, 1, window, cursor);

        var pads = new FakePads();
        var input = new ControllerInput(tracker.NoteInput, pads);
        pads.Slot[0] = Rest;
        input.Poll(1000);
        pads.Slot[0] = Rest with { Packet = 9, LeftX = -25000 };
        Assert.IsTrue(input.Poll(4600));

        // Windows saw no keyboard or mouse input since the start (idle 5 s by its clock).
        InputSample s = tracker.Sample(5000, 5000, 1, window, cursor);
        Assert.IsTrue(s.RealInput);
        Assert.AreEqual(0.4, s.IdleSeconds, 1e-9);
        Assert.AreEqual(0, tracker.IgnoredSinceLastInput);

        // Drift afterwards adds nothing: the idle clock runs on.
        pads.Slot[0] = Rest with { Packet = 10, LeftX = 300, LeftY = 200 };
        Assert.IsFalse(input.Poll(5250));
        s = tracker.Sample(6000, 6000, 1, window, cursor);
        Assert.IsFalse(s.RealInput);
        Assert.AreEqual(1.4, s.IdleSeconds, 1e-9);
    }

    [TestMethod]
    public void StartPollsOnItsOwnAndStopStops()
    {
        var pads = new FakePads();
        using var reads = new SemaphoreSlim(0);
        var counting = new CountingPads(pads, reads);
        var noted = new List<long>();
        using var input = new ControllerInput(t => { lock (noted) noted.Add(t); }, counting);
        Assert.IsFalse(input.Running);
        input.SetEnabled(true);
        Assert.IsTrue(input.Running);
        Assert.IsTrue(reads.Wait(5000), "the first round runs at once");
        input.SetEnabled(false);
        Assert.IsFalse(input.Running);
        input.Start();
        input.Start();
        Assert.IsTrue(input.Running);
        input.Dispose();
        Assert.IsFalse(input.Running);
        lock (noted) Assert.IsEmpty(noted);
    }

    sealed class CountingPads(IPads inner, SemaphoreSlim reads) : IPads
    {
        public bool TryRead(int slot, out PadState state)
        {
            bool ok = inner.TryRead(slot, out state);
            reads.Release();
            return ok;
        }
    }

    // Asks XInput itself once, whatever is plugged in: it must answer without throwing.
    [TestMethod]
    public void TheRealXInputAnswers()
    {
        var pads = XInputPads.Instance;
        for (int slot = 0; slot < ControllerInput.Slots; slot++) pads.TryRead(slot, out _);
        Assert.IsTrue(pads.Available, "xinput1_4.dll or xinput9_1_0.dll loads on Windows 10 and 11");
        Assert.IsFalse(pads.TryRead(4, out _));
        Assert.IsFalse(pads.TryRead(-1, out _));
    }
}
