namespace SteppedOut.Tests;

[TestClass]
public class OnceReportTests
{
    static LockTime Time(decimal normal, params ActiveEffect[] effects) => LockTime.Compute(normal, effects);

    static ActiveEffect On(Effect effect, EffectSource source) => new(effect, source);

    // Each row is written out by hand, not worked out with the calculator the report uses.
    [TestMethod]
    public void TheVerdictNamesTheLockTimeWithTheCountdownInsideIt()
    {
        var config = new Config();
        Assert.AreEqual("would lock once idle reaches 3:30:00, after a 15 s countdown from 3:29:45",
            OnceReport.Verdict(false, Time(30, On(Effect.Extra(60), EffectSource.MusicOrVideo), On(Effect.Extra(120), EffectSource.FullScreen)), 0, config));
        Assert.AreEqual("would lock once idle reaches 1:15:00, after a 15 s countdown from 1:14:45",
            OnceReport.Verdict(false, Time(30, On(Effect.Extra(120), EffectSource.FullScreen), On(Effect.Sooner(50), EffectSource.Battery)), 0, config));
        Assert.AreEqual("would not lock while this lasts (never: a call)",
            OnceReport.Verdict(false, Time(30, On(Effect.Never, EffectSource.Call)), 0, config));
        Assert.AreEqual("would lock once idle reaches 10:00, after a 15 s countdown from 9:45",
            OnceReport.Verdict(false, Time(30, On(Effect.Never, EffectSource.Call), On(Effect.Cap(10), EffectSource.Battery)), 0, config), "the cap beats never");
    }

    [TestMethod]
    public void AShortIdleTimeForATestRunLocksAfterThreeSeconds()
    {
        var config = new Config { IdleMinutes = 0.05 };
        Assert.AreEqual("would lock once idle reaches 0:03, after a 2 s countdown from 0:01", OnceReport.Verdict(false, Time(0.05m), 0, config));
        Assert.AreEqual("would lock now, after a 2 s countdown", OnceReport.Verdict(false, Time(0.05m), 4, config), "after Start-Sleep with --idle 0.05");
        Assert.AreEqual("would lock now", OnceReport.Verdict(false, Time(0.05m), 4, new Config { IdleMinutes = 0.05, GraceSeconds = 0 }));
    }

    // The longest lock time is a day (1440 minutes), and an idle or away stretch can be longer. The
    // hours are counted in full: a day once read 0:00:00, in --once and in the log.
    [TestMethod]
    public void ADayOrMoreReadsInFullHours()
    {
        Assert.AreEqual("0:00", Watcher.Format(0));
        Assert.AreEqual("0:59", Watcher.Format(59.4));
        Assert.AreEqual("59:59", Watcher.Format(3599.4));
        Assert.AreEqual("1:00:00", Watcher.Format(3599.6));
        Assert.AreEqual("23:59:59", Watcher.Format(86399));
        Assert.AreEqual("24:00:00", Watcher.Format(86400));
        Assert.AreEqual("25:01:01", Watcher.Format(90061));
        Assert.AreEqual("100:00:05", Watcher.Format(360005));

        var config = new Config { IdleMinutes = 1440 };
        Assert.AreEqual("would lock once idle reaches 24:00:00, after a 15 s countdown from 23:59:45", OnceReport.Verdict(false, Time(1440), 0, config));
        var lines = OnceReport.Report(config, new Options(), [], new FakeWorld().Look(TimeSpan.FromHours(1)), 349, false);
        CollectionAssert.Contains(lines, "idle: 5:49 of 24:00:00");
    }

    [TestMethod]
    public void LockingTurnedOffSaysItStepsOut()
    {
        Assert.AreEqual("would step out without locking once idle reaches 15:00, after a 15 s countdown from 14:45",
            OnceReport.Verdict(false, Time(15), 0, new Config { LockWhenAway = false }));
    }

    [TestMethod]
    public void ALockedSessionComesFirst()
    {
        Assert.AreEqual("session already locked", OnceReport.Verdict(true, Time(30, On(Effect.Never, EffectSource.Call)), 900, new Config()));
    }

    [TestMethod]
    public void TheReportListsEachRuleAndEndsWithTheVerdict()
    {
        var world = new FakeWorld();
        world.Play(10, "Spotify");
        world.FullScreen = "game";
        world.Power = PowerLineStatus.Offline;
        var config = new Config { IdleMinutes = 30, MusicVideo = Effect.Extra(60), FullScreen = Effect.Extra(120), OnCall = Effect.None, OnBattery = Effect.Sooner(50) };
        var lines = OnceReport.Report(config, new Options { DryRun = true }, Rules.Build(config), world.Look(TimeSpan.FromHours(1)), 0, false);
        CollectionAssert.Contains(lines, "rule call: clear");
        CollectionAssert.Contains(lines, "rule audio: sound from Spotify (later 60)");
        CollectionAssert.Contains(lines, "rule display: clear");
        CollectionAssert.Contains(lines, "rule fullscreen: full screen: game (later 120)");
        CollectionAssert.Contains(lines, "rule battery: on battery (sooner 50)");
        CollectionAssert.Contains(lines, "rule apps: none set");
        StringAssert.StartsWith(lines[0], AppInfo.Name);
        StringAssert.EndsWith(lines[0], "(dry run)");
        Assert.AreEqual("verdict: would lock once idle reaches 1:45:00, after a 15 s countdown from 1:44:45", lines[^1], "(30 + 60 + 120) × 50%");
    }

    [TestMethod]
    public void TheVerdictIsPrintedEvenWhenEveryRuleFails()
    {
        var world = new FakeWorld();
        foreach (string what in new[] { "anything", "calls", "processes", "windows", "display", "fullscreen", "power" }) world.Failing.Add(what);
        var config = new Config { MusicVideo = Effect.Never, FullScreen = Effect.Never, OnCall = Effect.Never, OnBattery = Effect.Cap(5),
            AppRules = { new AppRule("x", Effect.Never) } };
        var rules = Rules.Build(config);
        Assert.HasCount(6, rules);
        var lines = OnceReport.Report(config, new Options(), rules, world.Look(TimeSpan.FromHours(1)), 0, false, "a problem", "a warning");
        Assert.AreEqual(6, lines.Count(l => l.StartsWith("rule ", StringComparison.Ordinal) && l.Contains(": failed, ")));
        CollectionAssert.Contains(lines, "config: a problem");
        CollectionAssert.Contains(lines, "config: a warning");
        Assert.AreEqual("verdict: would lock once idle reaches 15:00, after a 15 s countdown from 14:45", lines[^1], "nothing could say otherwise");
    }

    [TestMethod]
    public void TheReportsVerdictMatchesTheCalculator()
    {
        var world = new FakeWorld();
        world.Call(30, "Teams");
        world.Play(30, "Teams");
        world.Play(10, "Spotify", 0.1f);
        var config = new Config { IdleMinutes = 15, MusicVideo = Effect.Extra(60), OnCall = Effect.Extra(30), FullScreen = Effect.None };
        var lines = OnceReport.Report(config, new Options(), Rules.Build(config), world.Look(TimeSpan.FromHours(1)), 0, false);
        Assert.AreEqual("lock time: 105 min (15 + 30 a call + 60 music or video)", lines[^2]);
        Assert.AreEqual("verdict: would lock once idle reaches 1:45:00, after a 15 s countdown from 1:44:45", lines[^1]);
        CollectionAssert.Contains(lines, "rule audio: sound from Teams (part of the call); sound from Spotify (later 60)");
    }
}
