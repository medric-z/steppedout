using System.Runtime.InteropServices;

namespace SteppedOut.Tests;

[TestClass]
public class AudioRuleTests
{
    static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    static AudioRule Rule() => new(Effect.Never, 0.01, 30);

    static List<WhyLine> Check(AudioRule rule, FakeWorld world, double seconds)
    {
        var lines = new List<WhyLine>();
        rule.Check(world.Look(At(seconds), fresh: false), lines);
        return lines;
    }

    static WhyLine Playing(string process) => new(Signal.Sound, Effect.Never, process);

    [TestMethod]
    public void AFailedScanStillCountsSoundHeardAMomentAgo()
    {
        var rule = Rule();
        var world = new FakeWorld();
        world.Play(1, "vlc");
        Check(rule, world, 100);
        world.Failing.Add("sounds");
        CollectionAssert.AreEqual(new[] { Playing("vlc") }, Check(rule, world, 101));
        CollectionAssert.AreEqual(new[] { Playing("vlc") }, Check(rule, world, 129));
    }

    [TestMethod]
    public void AFailedScanPastTheHoldStillFails()
    {
        var rule = Rule();
        var world = new FakeWorld();
        world.Play(1, "vlc");
        Check(rule, world, 100);
        world.Failing.Add("sounds");
        Assert.ThrowsExactly<COMException>(() => Check(rule, world, 130));
        Assert.ThrowsExactly<COMException>(() => Check(Rule(), world, 1), "nothing heard yet");
    }

    [TestMethod]
    public void SoundCountsUntilQuietForTheWholeHold()
    {
        var rule = Rule();
        Assert.AreEqual("vlc", rule.Heard("vlc", At(100)));
        Assert.AreEqual("vlc", rule.Heard(null, At(129)), "a quiet scene");
        Assert.IsNull(rule.Heard(null, At(130)));
    }

    [TestMethod]
    public void TheNameStaysWhileTwoProgramsTakeTurnsBeingLoudest()
    {
        var rule = Rule();
        for (int second = 0; second < 60; second++)
        {
            Assert.AreEqual("game", rule.Heard(second % 2 == 0 ? "game" : "Discord", At(second)));
        }
    }

    [TestMethod]
    public void TheNameMovesOnOnceTheOtherProgramKeepsBeingLoudest()
    {
        var rule = Rule();
        rule.Heard("game", At(0));
        Assert.AreEqual("game", rule.Heard("Spotify", At(9)));
        Assert.AreEqual("Spotify", rule.Heard("Spotify", At(10)));
        Assert.AreEqual("Spotify", rule.Heard(null, At(20)), "the hold keeps the new name");
    }

    [TestMethod]
    public void FirstSoundIsNamedEvenRightAfterStartup()
    {
        var rule = Rule();
        Assert.IsNull(rule.Heard(null, At(1)));
        Assert.AreEqual("vlc", rule.Heard("vlc", At(2)));
    }

    [TestMethod]
    public void TheLoudestProgramIsNamed()
    {
        var world = new FakeWorld();
        world.Play(1, "quiet", 0.1f);
        world.Play(2, "loud", 0.9f);
        CollectionAssert.AreEqual(new[] { Playing("loud") }, Check(Rule(), world, 5));
    }

    [TestMethod]
    public void SoundBelowTheThresholdIsSilence()
    {
        var world = new FakeWorld();
        world.Play(1, "fan", 0.001f);
        Assert.IsEmpty(Check(Rule(), world, 5));
    }

    [TestMethod]
    public void TheCallAppsOwnSoundIsTheCallAndOtherMusicStillCounts()
    {
        var world = new FakeWorld();
        world.Call(30, "Teams");
        world.Play(30, "Teams", 0.9f);
        world.Play(10, "Spotify", 0.2f);
        CollectionAssert.AreEqual(new[] { new WhyLine(Signal.Sound, Effect.Never, "Teams", Counts: false), Playing("Spotify") }, Check(Rule(), world, 5));
    }

    [TestMethod]
    public void ASoundIsMatchedToTheCallByItsProcessNeverByItsName()
    {
        var world = new FakeWorld();
        world.Call(30, "Teams");
        world.Paths[31] = @"C:\Somewhere\Else\Teams.exe";
        world.Play(31, "Teams");
        CollectionAssert.AreEqual(new[] { Playing("Teams") }, Check(Rule(), world, 5));
    }

    [TestMethod]
    public void TheProcessTreeIsFollowedOnlyAFewStepsUp()
    {
        var world = new FakeWorld();
        world.Call(30, "Teams");
        for (int id = 31; id <= 35; id++) world.Parents[id] = id - 1;
        var scene = world.Look(At(5));
        var calls = world.Calls;
        Assert.IsTrue(AudioRule.IsCallSound(scene, 33, calls), "three steps up");
        Assert.IsFalse(AudioRule.IsCallSound(scene, 34, calls), "four is too far");
        Assert.IsFalse(AudioRule.IsCallSound(scene, 4, calls), "the system process is never followed");
    }

    [TestMethod]
    public void MusicStillCountsWhenTheCallRecordsCannotBeRead()
    {
        var world = new FakeWorld();
        world.Play(10, "Spotify");
        world.Failing.Add("calls");
        CollectionAssert.AreEqual(new[] { Playing("Spotify") }, Check(Rule(), world, 5));
    }

    [TestMethod]
    public void SettingItAgainKeepsWhatItHeard()
    {
        var rule = Rule();
        rule.Heard("vlc", At(100));
        rule.Set(Effect.Extra(60), 0.03, 10);
        Assert.AreEqual(Effect.Extra(60), rule.Effect);
        Assert.AreEqual("vlc", rule.Heard(null, At(109)));
        Assert.IsNull(rule.Heard(null, At(110)), "with the new 10 s hold");
    }

    [TestMethod]
    public void ReadingTheRealSessionsDoesNotThrow()
    {
        try
        {
            var sessions = Wasapi.Sessions(0.01f);
            Assert.IsTrue(sessions.All(s => s.Peak >= 0.01f));
        }
        catch (COMException)
        {
            Assert.Inconclusive("no audio service on this PC");
        }
    }
}
