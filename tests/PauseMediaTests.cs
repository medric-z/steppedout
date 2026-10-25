using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace SteppedOut.Tests;

[TestClass]
public class PauseMediaTests
{
    static void Wait(Task task) => AwayFakes.Wait(task, "done in time");

    static (PauseMedia Media, AwayFakes.FakeMedia Fake, AwayFakes.Recorder Rec, List<string> Log) Make(bool stayPaused, params string[] playing)
    {
        var rec = new AwayFakes.Recorder();
        var fake = new AwayFakes.FakeMedia(rec) { Playing = playing };
        var log = new List<string>();
        return (new PauseMedia(fake, stayPaused, log.Add), fake, rec, log);
    }

    [TestMethod]
    public void PauseUntilBackPlaysAgainWhatWasPausedAndSaysWhich()
    {
        var (media, fake, _, log) = Make(stayPaused: false, "Player", "Radio");

        media.Leave();
        var played = media.Return();

        CollectionAssert.AreEqual(new[] { "Player", "Radio" }, played.ToList());
        Assert.AreEqual(2, fake.PlayedAgain);
        Assert.AreEqual(0, fake.Released);
        CollectionAssert.AreEqual(new[] { "paused: Player, Radio", "playing again: Player, Radio" }, log);
    }

    [TestMethod]
    public void PauseAndStayPausedLetsGoOfEveryAppWithoutPlayingIt()
    {
        var (media, fake, rec, log) = Make(stayPaused: true, "Player", "Radio");

        media.Leave();
        var played = media.Return();

        Assert.IsEmpty(played);
        Assert.AreEqual(0, fake.PlayedAgain);
        Assert.AreEqual(2, fake.Released);
        CollectionAssert.AreEqual(new[] { "pause", "let go" }, rec.Events);
        CollectionAssert.Contains(log, "left paused: Player, Radio");
    }

    [TestMethod]
    public void EveryPausedAppIsPlayedAgainOrLetGoOfExactlyOnce()
    {
        foreach (bool stayPaused in new[] { false, true })
        {
            var (media, fake, _, _) = Make(stayPaused, "Player", "Radio", "Podcast");

            media.Leave();
            media.Return();
            media.Return();
            media.Leave();
            media.Return();

            Assert.AreEqual(6, fake.Paused, $"stay paused {stayPaused}");
            Assert.AreEqual(fake.Paused, fake.PlayedAgain + fake.Released, $"stay paused {stayPaused}: each one given back once");
            Assert.AreEqual(stayPaused ? 0 : 6, fake.PlayedAgain);
        }
    }

    [TestMethod]
    public void NothingPlayingMeansNothingToPlayOrLetGoOf()
    {
        foreach (bool stayPaused in new[] { false, true })
        {
            var (media, _, rec, log) = Make(stayPaused);

            media.Leave();
            var played = media.Return();

            Assert.IsEmpty(played);
            CollectionAssert.AreEqual(new[] { "pause" }, rec.Events);
            Assert.IsEmpty(log);
        }
    }

    [TestMethod]
    public void ASecondLeaveDoesNotPauseAgainAndLoseTheList()
    {
        var (media, fake, _, _) = Make(stayPaused: false, "Player");

        media.Leave();
        media.Leave();
        var played = media.Return();

        Assert.AreEqual(1, fake.Paused);
        CollectionAssert.AreEqual(new[] { "Player" }, played.ToList());
    }

    [TestMethod]
    public void KeepPlayingAddsNoActionAtAll()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(new Config { MediaOnLock = MediaOnLock.KeepPlaying });

        actions.Leave(locking: true);
        var result = actions.ComeBack();
        Wait(result);
        Wait(actions.Idle());

        Assert.IsEmpty(rig.Rec.Events);
        Assert.IsFalse(result.Result.AnyPlayedAgain);
    }

    [TestMethod]
    public void StayPausedComesBackWithNothingPlayingAgain()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(new Config { MediaOnLock = MediaOnLock.PauseStayPaused });

        actions.Leave(locking: true);
        var result = actions.ComeBack();
        Wait(result);

        Assert.IsFalse(result.Result.AnyPlayedAgain);
        CollectionAssert.AreEqual(new[] { "pause", "let go" }, rig.Rec.Events);
        Assert.AreEqual(1, rig.Media.Released);
    }

    // The setting is named for the lock, but with locking off the other away actions run too, and
    // music playing to an empty room is still missed, so it pauses then as well, as before 1.0.
    [TestMethod]
    public void MusicIsPausedOnAnAwayWithoutALockToo()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(new Config { MediaOnLock = MediaOnLock.PauseUntilBack });

        actions.Leave(locking: false);
        var result = actions.ComeBack();
        Wait(result);

        CollectionAssert.AreEqual(new[] { "pause", "play again" }, rig.Rec.Events);
        CollectionAssert.AreEqual(new[] { "Player" }, result.Result.PlayedAgain.ToList());
    }

    // ---- The real MediaSessions code, against made-up native objects that count their Release. ----

    [TestMethod]
    public void TheRealReleaseGivesBackEveryReferenceItHeld()
    {
        using var first = new NativeSession();
        using var second = new NativeSession();
        var paused = new MediaSessions.Paused();
        paused.Sessions.Add((first.Pointer, "Player"));
        paused.Sessions.Add((second.Pointer, "Radio"));

        MediaSessions.Release(paused);

        Assert.AreEqual(1, first.Releases);
        Assert.AreEqual(1, second.Releases);
        Assert.IsEmpty(paused.Sessions);
    }

    [TestMethod]
    public void TheRealPlayAgainGivesBackEveryReferenceEvenWhenAnAppFailsToAnswer()
    {
        using var first = new NativeSession();
        using var second = new NativeSession();
        var paused = new MediaSessions.Paused();
        paused.Sessions.Add((first.Pointer, "Player"));
        paused.Sessions.Add((second.Pointer, "Radio"));

        var played = MediaSessions.PlayAgain(paused);

        Assert.IsEmpty(played);
        Assert.AreEqual(1, first.TryPlays);
        Assert.AreEqual(1, first.Releases);
        Assert.AreEqual(1, second.Releases);
        Assert.IsEmpty(paused.Sessions);
    }

    // A COM-shaped object in native memory: a pointer to a table of 11 functions, where slot 2 is
    // Release and slot 10 is the session's TryPlay, which here always fails.
    sealed class NativeSession : IDisposable
    {
        const int EFail = unchecked((int)0x80004005);
        const int ENoInterface = unchecked((int)0x80004002);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int TwoArguments(IntPtr self, IntPtr argument);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int ThreeArguments(IntPtr self, IntPtr first, IntPtr second);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate uint OneArgument(IntPtr self);

        static readonly ConcurrentDictionary<IntPtr, int> releases = new();
        static readonly ConcurrentDictionary<IntPtr, int> tries = new();
        static readonly ThreeArguments queryInterface = (_, _, _) => ENoInterface;
        static readonly OneArgument addRef = _ => 1;
        static readonly OneArgument release = self => (uint)releases.AddOrUpdate(self, 1, (_, n) => n + 1);
        static readonly TwoArguments fail = (_, _) => EFail;
        static readonly TwoArguments tryPlay = (self, _) =>
        {
            tries.AddOrUpdate(self, 1, (_, n) => n + 1);
            return EFail;
        };

        readonly IntPtr table;

        public NativeSession()
        {
            table = Marshal.AllocHGlobal(11 * IntPtr.Size);
            for (int slot = 0; slot < 11; slot++) Marshal.WriteIntPtr(table, slot * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(fail));
            Marshal.WriteIntPtr(table, 0, Marshal.GetFunctionPointerForDelegate(queryInterface));
            Marshal.WriteIntPtr(table, IntPtr.Size, Marshal.GetFunctionPointerForDelegate(addRef));
            Marshal.WriteIntPtr(table, 2 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(release));
            Marshal.WriteIntPtr(table, 10 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(tryPlay));
            Pointer = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(Pointer, table);
        }

        public IntPtr Pointer { get; }

        public int Releases => releases.TryGetValue(Pointer, out int n) ? n : 0;

        public int TryPlays => tries.TryGetValue(Pointer, out int n) ? n : 0;

        public void Dispose()
        {
            releases.TryRemove(Pointer, out _);
            tries.TryRemove(Pointer, out _);
            Marshal.FreeHGlobal(Pointer);
            Marshal.FreeHGlobal(table);
        }
    }
}
