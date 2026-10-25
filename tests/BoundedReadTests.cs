using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SteppedOut.Tests;

// Every rule reading runs on the UI thread, which also draws the countdown, serves the tray and the
// shortcut and decides the lock. The sounds are the one reading that waits on another process, the
// audio service, so a look waits for them at most a limit (Scans.SoundLimit, 2 s; less here).
[TestClass]
public class BoundedReadTests
{
    static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(200);

    // The late read, once it is back; a call that comes too soon still gets the timeout.
    static string WhenBack(Bounded<int, string> bounded, int arg)
    {
        var until = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return bounded.Get(arg);
            }
            catch (TimeoutException) when (until.Elapsed < TimeSpan.FromSeconds(10))
            {
                Thread.Sleep(10);
            }
        }
    }

    [TestMethod]
    public void AQuickReadIsReturned()
    {
        var bounded = new Bounded<int, string>(n => "read " + n, Limit);
        Assert.AreEqual("read 3", bounded.Get(3));
        Assert.AreEqual("read 4", bounded.Get(4));
    }

    [TestMethod]
    public void AHungReadCostsOneWaitAndNoSecondRead()
    {
        using var gate = new ManualResetEventSlim(false);
        int reads = 0;
        var bounded = new Bounded<int, string>(n =>
        {
            if (Interlocked.Increment(ref reads) == 1) gate.Wait();
            return "read " + n;
        }, Limit);
        try
        {
            var clock = Stopwatch.StartNew();
            Assert.ThrowsExactly<TimeoutException>(() => bounded.Get(1));
            Assert.IsGreaterThanOrEqualTo(Limit - TimeSpan.FromMilliseconds(20), clock.Elapsed, "it waited the limit");
            clock.Restart();
            Assert.ThrowsExactly<TimeoutException>(() => bounded.Get(2));
            Assert.IsLessThan(Limit, clock.Elapsed, "no second wait while the first read is out");
            Assert.AreEqual(1, Volatile.Read(ref reads), "and no second read");
        }
        finally
        {
            gate.Set();
        }
        Assert.AreEqual("read 1", WhenBack(bounded, 3), "the late read is what the next call gets");
        Assert.AreEqual("read 4", bounded.Get(4));
        Assert.AreEqual(2, Volatile.Read(ref reads));
    }

    // A read that came back late is an old answer. The look that decides (a fresh one, before a
    // countdown or the lock) reads again instead, and so does any look once the late answer has
    // been back longer than the other readings are kept (Scans.MaxAge).
    [TestMethod]
    public void ALateReadIsNotTakenAsFreshOnTheLookThatDecides()
    {
        foreach (bool decides in new[] { true, false })
        {
            using var gate = new ManualResetEventSlim(false);
            int reads = 0;
            var now = TimeSpan.FromMinutes(1);
            var bounded = new Bounded<int, string>(n =>
            {
                if (Interlocked.Increment(ref reads) == 1) gate.Wait();
                return "read " + n;
            }, Limit, () => now);
            try
            {
                Assert.ThrowsExactly<TimeoutException>(() => bounded.Get(1));
            }
            finally
            {
                gate.Set();
            }
            Assert.IsTrue(SpinWait.SpinUntil(() => bounded.LateIsBack, TimeSpan.FromSeconds(10)), "the late read came back");
            if (decides)
            {
                Assert.AreEqual("read 2", bounded.Get(2, fresh: true), "the deciding look reads again");
            }
            else
            {
                now += Scans.MaxAge + TimeSpan.FromSeconds(1);
                Assert.AreEqual("read 2", bounded.Get(2), "back too long ago: read again");
            }
            Assert.AreEqual(2, Volatile.Read(ref reads));
        }
    }

    // A read that timed out comes back just as the next look hands over its own read: the reading
    // thread is held between marking the late read back and waking whoever waited for it, the next
    // look hands over its read, then the thread goes on. That wake-up was for the late read, so the
    // next look still waits for its own read: it is not taken as back, with no answer in it, nor as
    // timed out early.
    [TestMethod]
    public void ALateReadComingBackDoesNotWakeTheNextLook()
    {
        using var first = new ManualResetEventSlim(false);
        using var second = new ManualResetEventSlim(false);
        using var marked = new ManualResetEventSlim(false);
        using var wake = new ManualResetEventSlim(false);
        int reads = 0;
        var limit = TimeSpan.FromSeconds(1);
        var bounded = new Bounded<int, string>(n =>
        {
            int count = Interlocked.Increment(ref reads);
            if (count == 1) first.Wait();
            if (count == 2) second.Wait();
            return "read " + n;
        }, limit);
        bounded.Returning = () =>
        {
            if (Volatile.Read(ref reads) != 1) return;
            marked.Set();
            wake.Wait();
        };
        try
        {
            Assert.ThrowsExactly<TimeoutException>(() => bounded.Get(1));
            first.Set();
            Assert.IsTrue(marked.Wait(TimeSpan.FromSeconds(10)), "the late read is back, and the thread held before it wakes anyone");
            Assert.IsTrue(bounded.LateIsBack);
            var clock = Stopwatch.StartNew();
            var look = Task.Run(() => bounded.Get(2, fresh: true));
            Assert.IsTrue(SpinWait.SpinUntil(() => bounded.Handed, TimeSpan.FromSeconds(10)), "the next look handed over its read");
            wake.Set();
            var error = Assert.ThrowsExactly<AggregateException>(() => look.Wait(TimeSpan.FromSeconds(10)), "the next look did not end on the late read's wake-up");
            Assert.IsInstanceOfType<TimeoutException>(error.InnerException, "it waited for its own read, which is still out");
            Assert.IsGreaterThanOrEqualTo(limit - TimeSpan.FromMilliseconds(20), clock.Elapsed, "for the whole limit");
        }
        finally
        {
            wake.Set();
            first.Set();
            second.Set();
        }
        Assert.AreEqual("read 2", WhenBack(bounded, 3), "its read, once back, is what the next call gets");
        Assert.AreEqual(2, Volatile.Read(ref reads));
        bounded.Dispose();
    }

    // Each read used to start a thread and, when it was slow, a kernel event that nothing closed,
    // so their handles piled up until a garbage collection, a few a second while sound was asked
    // about. The reads now share one thread and one event.
    [TestMethod]
    [DoNotParallelize]
    public void SlowReadsLeaveNoHandlesBehind()
    {
        var bounded = new Bounded<int, string>(n =>
        {
            Thread.Sleep(3);
            return "read " + n;
        }, TimeSpan.FromSeconds(2));
        Assert.AreEqual("read 0", bounded.Get(0));
        using var self = Process.GetCurrentProcess();
        self.Refresh();
        int before = self.HandleCount;
        bool noCollection = GC.TryStartNoGCRegion(16 * 1024 * 1024);
        try
        {
            for (int i = 1; i <= 100; i++) Assert.AreEqual("read " + i, bounded.Get(i));
            self.Refresh();
            Assert.IsLessThan(before + 30, self.HandleCount, $"handles before {before}, after 100 slow reads {self.HandleCount}");
        }
        finally
        {
            if (noCollection && System.Runtime.GCSettings.LatencyMode == System.Runtime.GCLatencyMode.NoGCRegion) GC.EndNoGCRegion();
        }
    }

    // Disposed, the reading thread ends, so nothing it holds stays alive with it.
    [TestMethod]
    public void TheReadingThreadEndsWhenDisposed()
    {
        var bounded = new Bounded<int, string>(n => "read " + n, Limit);
        Assert.AreEqual("read 1", bounded.Get(1));
        Assert.IsTrue(bounded.Reading);
        bounded.Dispose();
        Assert.IsTrue(SpinWait.SpinUntil(() => !bounded.Reading, TimeSpan.FromSeconds(5)), "the thread ended");
        Assert.ThrowsExactly<ObjectDisposedException>(() => bounded.Get(2));
    }

    [TestMethod]
    public void TheReadsOwnErrorComesThrough()
    {
        var bounded = new Bounded<int, string>(_ => throw new COMException("device invalidated"), Limit);
        Assert.ThrowsExactly<COMException>(() => bounded.Get(1));
        Assert.ThrowsExactly<COMException>(() => bounded.Get(2), "and the next read is tried");
    }

    // A hung audio service: the look comes back, and the sound heard a moment ago still counts for
    // the silence hold, as when Windows cannot be asked.
    [TestMethod]
    public void AHungAudioServiceDoesNotHoldUpTheLook()
    {
        var world = new FakeWorld();
        world.Play(10, "Spotify");
        var scans = new Scans(world.Sources, Limit);
        var audio = new AudioRule(Effect.Never, 0.01, 30);
        var lines = new List<WhyLine>();
        audio.Check(new Scene(TimeSpan.FromSeconds(1), true, scans), lines);
        Assert.AreEqual("Spotify", lines.Single().Detail);

        using var hold = new ManualResetEventSlim(false);
        world.Hold = hold;
        try
        {
            lines.Clear();
            var look = Task.Run(() => audio.Check(new Scene(TimeSpan.FromSeconds(2), true, scans), lines));
            Assert.IsTrue(look.Wait(TimeSpan.FromSeconds(5)), "the look came back");
            Assert.AreEqual("Spotify", lines.Single().Detail, "the sound heard a second ago still counts");
        }
        finally
        {
            hold.Set();
        }
    }
}
