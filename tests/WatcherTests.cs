using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SteppedOut.Tests;

// What the rules would read from Windows, as a test sets it. A reading can be made to fail, and the
// moments the sounds, the process list and the call records were read are kept.
sealed class FakeWorld
{
    public Func<TimeSpan> Clock { get; set; } = () => TimeSpan.Zero;

    public List<RunningProcess> Processes { get; } = new();

    public Dictionary<int, int> Parents { get; } = new();

    public List<TopWindow> Windows { get; } = new();

    public List<CallApp> Calls { get; } = new();

    public List<Wasapi.Session> Sounds { get; } = new();

    public bool DisplayRequired { get; set; }

    public string? FullScreen { get; set; }

    public PowerLineStatus Power { get; set; } = PowerLineStatus.Online;

    public Dictionary<int, string> Paths { get; } = new();

    public Dictionary<int, string> Families { get; } = new();

    // Readings that throw: "sounds" (a COMException, as Windows gives), "calls", "processes",
    // "windows", "display", "fullscreen", "power", "anything" (sounds with a plain exception).
    public HashSet<string> Failing { get; } = new();

    public List<TimeSpan> SoundReads { get; } = new();

    public List<TimeSpan> ProcessReads { get; } = new();

    public List<TimeSpan> CallReads { get; } = new();

    // While set and not signalled, reading the sounds waits on it, as a hung audio service would.
    public ManualResetEventSlim? Hold { get; set; }

    public Sources Sources => new()
    {
        Processes = () =>
        {
            ProcessReads.Add(Clock());
            Fail("processes");
            return Processes.ToList();
        },
        Parents = () => new Dictionary<int, int>(Parents),
        Windows = () =>
        {
            Fail("windows");
            return Windows.ToList();
        },
        Calls = () =>
        {
            CallReads.Add(Clock());
            Fail("calls");
            return Calls.ToList();
        },
        Sounds = threshold =>
        {
            Hold?.Wait();
            SoundReads.Add(Clock());
            if (Failing.Contains("sounds")) throw new COMException("device invalidated");
            Fail("anything");
            return Sounds.Where(s => s.Peak >= threshold).ToList();
        },
        DisplayRequired = () =>
        {
            Fail("display");
            return DisplayRequired;
        },
        FullScreen = () =>
        {
            Fail("fullscreen");
            return FullScreen;
        },
        PowerLine = () =>
        {
            Fail("power");
            return Power;
        },
        ImagePath = id => Paths.GetValueOrDefault(id),
        PackageFamily = id => Families.GetValueOrDefault(id),
    };

    void Fail(string what)
    {
        if (Failing.Contains(what)) throw new InvalidOperationException(what + " cannot be read");
    }

    // A program running in this session.
    public void Run(int id, string name) => Processes.Add(new RunningProcess(id, name, ProcessRule.MySession));

    public void Play(int id, string name, float peak = 0.5f) => Sounds.Add(new Wasapi.Session(id, name, peak));

    // A desktop app on a call, with its program file.
    public void Call(int id, string name)
    {
        string path = $@"C:\Apps\{name}\{name}.exe";
        Run(id, name);
        Paths[id] = path;
        Calls.Add(new CallApp(name, ExePath: path));
    }

    public Scene Look(TimeSpan now, bool fresh = true) => new(now, fresh, new Scans(Sources));
}

// A watcher on a fake clock, reading a fake world, that records what it does. Each Second is one
// tick with nobody at the PC: the idle time goes up by one.
sealed class Rig : IDisposable
{
    readonly TimeSpan start = TimeSpan.FromHours(10);

    // log hears the tick's own failures, which go nowhere otherwise.
    public Rig(Config config, Action<string>? log = null, bool dryRun = false)
    {
        Now = start;
        World.Clock = () => Now;
        Watcher = new Watcher(config, dryRun, new WatchSeams
        {
            Sample = () =>
            {
                var sample = new InputSample(Idle, Idle, Input);
                Input = false;
                return sample;
            },
            Monotonic = () => Now,
            Wall = () => Wall,
            RequestLock = () =>
            {
                LockRequests++;
                return !LockFails;
            },
            AcceptsNotifications = () => Notifications,
            SessionIsLocked = () => false,
            Sources = World.Sources,
            StatsPath = null,
            ErrorLog = log ?? (_ => { }),
        });
        Watcher.CountdownStarted += seconds => Note("start " + seconds);
        Watcher.CountdownEnded += () => Note("end");
        Watcher.WentAway += (locking, own) =>
        {
            Note(locking ? "away locking" : "away");
            if (own) Note("own lock");
        };
        Watcher.CameBack += _ => Note("back");
        Watcher.ReminderDue += reminder => Note("reminder " + reminder.Kind);
    }

    public TimeSpan Now { get; private set; }

    public DateTime Wall { get; private set; } = new(2026, 10, 20, 9, 0, 0);

    public double Idle { get; set; }

    public bool Input { get; set; }

    public bool Notifications { get; set; } = true;

    public int LockRequests { get; private set; }

    // Windows turns every lock request down, as LockWorkStation returning false.
    public bool LockFails { get; set; }

    public FakeWorld World { get; } = new();

    public Watcher Watcher { get; }

    public List<(double Idle, string What)> Events { get; } = new();

    void Note(string what) => Events.Add((Idle, what));

    // The idle time a reading taken at this moment saw, counting from the rig's start.
    public double IdleAt(TimeSpan moment) => (moment - start).TotalSeconds;

    // One second with nobody at the PC.
    public void Second(Action? before = null)
    {
        Now += TimeSpan.FromSeconds(1);
        Wall = Wall.AddSeconds(1);
        Idle++;
        before?.Invoke();
        Watcher.Tick();
    }

    // One second with someone at the PC, whose last input was a moment ago.
    public void Busy()
    {
        Now += TimeSpan.FromSeconds(1);
        Wall = Wall.AddSeconds(1);
        Idle = 0;
        Watcher.Tick();
    }

    // Time that passes with no tick at all, as in sleep or hibernation: both clocks move on.
    public void Jump(TimeSpan by)
    {
        Now += by;
        Wall = Wall.Add(by);
    }

    // Seconds go by until the idle time reaches idle; each calls before first, with the new idle time.
    public void Until(double idle, Action<double>? before = null)
    {
        while (Idle < idle) Second(before is null ? null : () => before(Idle));
    }

    public double? First(string what) => Events.Where(e => e.What == what || e.What.StartsWith(what + " ", StringComparison.Ordinal)).Select(e => (double?)e.Idle).FirstOrDefault();

    public int Count(string what) => Events.Count(e => e.What == what || e.What.StartsWith(what + " ", StringComparison.Ordinal));

    public void Dispose() => Watcher.Dispose();
}

[TestClass]
public class WatcherTests
{
    static Config Settings(double normal = 15, Effect? music = null, Effect? fullScreen = null, Effect? call = null, Effect? battery = null,
        int warning = 15, params AppRule[] apps) => new()
        {
            IdleMinutes = normal,
            GraceSeconds = warning,
            MusicVideo = music ?? Effect.None,
            FullScreen = fullScreen ?? Effect.None,
            OnCall = call ?? Effect.None,
            OnBattery = battery ?? Effect.None,
            AppRules = apps.ToList(),
        };

    [TestMethod]
    public void HeartbeatSaysTheStateInWords()
    {
        var plain = LockTime.Plain(15);
        WhyLine[] none = [];
        WhyLine[] seen =
        [
            new(Signal.Battery, Effect.Cap(10)),
            new(Signal.Call, Effect.Extra(30), "Teams", Counts: false),
            new(Signal.Sound, Effect.Never, "Spotify"),
            new(Signal.Call, Effect.Never, "Teams"),
            new(Signal.FullScreen, Effect.Extra(60), "game"),
        ];
        Assert.AreEqual("locks in 12:30", Watcher.LogText(new LockStatus { Kind = LockState.On, Time = plain, SecondsLeft = 750 }, 0, none));
        Assert.AreEqual("locks in 12:30", Watcher.LogText(new LockStatus { Kind = LockState.LockingOff, Time = plain, SecondsLeft = 750 }, 0, seen));
        Assert.AreEqual("session locked", Watcher.LogText(new LockStatus { Kind = LockState.SessionLocked, Time = plain }, 0, seen));
        Assert.AreEqual("away", Watcher.LogText(new LockStatus { Kind = LockState.Away, Time = plain }, 0, seen));
        Assert.AreEqual("starting", Watcher.LogText(LockStatus.Starting(15), 0, seen));
        Assert.AreEqual("counting down, 0:09 left", Watcher.LogText(new LockStatus { Kind = LockState.Countdown, Time = plain, SecondsLeft = 3 }, 9, seen));
        Assert.AreEqual("not locking, audio playing: Spotify", Watcher.LogText(new LockStatus { Kind = LockState.Never, Time = plain }, 0, seen));
        Assert.AreEqual("not locking", Watcher.LogText(new LockStatus { Kind = LockState.Never, Time = plain }, 0, none));
        Assert.AreEqual("paused until 14:05", Watcher.LogText(new LockStatus { Kind = LockState.Paused, Time = plain, PausedUntil = new DateTime(2026, 10, 19, 14, 5, 0) }, 0, seen));
        Assert.AreEqual("paused", Watcher.LogText(new LockStatus { Kind = LockState.Paused, Time = plain }, 0, seen));

        // A longer wait names the first thing seen that buys more time, and only one that counts.
        var longer = LockTime.Compute(15, [new ActiveEffect(Effect.Extra(60), EffectSource.FullScreen)]);
        Assert.AreEqual("waiting longer, locks in 1:14:00, fullscreen app: game", Watcher.LogText(new LockStatus { Kind = LockState.On, Time = longer, SecondsLeft = 4440 }, 0, seen));
        Assert.AreEqual("waiting longer, locks in 1:14:00, an app asked Windows to keep the display on",
            Watcher.LogText(new LockStatus { Kind = LockState.On, Time = longer, SecondsLeft = 4440 }, 0, [new(Signal.Display, Effect.Extra(60), "chrome")]));
        Assert.AreEqual("waiting longer, locks in 1:14:00, process running: obs64",
            Watcher.LogText(new LockStatus { Kind = LockState.On, Time = longer, SecondsLeft = 4440 }, 0, [new(Signal.App, Effect.Extra(60), "obs64")]));
    }

    [TestMethod]
    public void LocksAtTheNormalTimeWithTheCountdownInsideIt()
    {
        using var rig = new Rig(Settings(normal: 15));
        rig.Until(901);
        Assert.AreEqual(885, rig.First("start"));
        Assert.AreEqual(900, rig.First("away"));
        Assert.AreEqual(1, rig.LockRequests);
    }

    [TestMethod]
    public void ThreeExtrasAddUpTo210Minutes()
    {
        using var rig = new Rig(Settings(normal: 30, music: Effect.Extra(60), fullScreen: Effect.Extra(90), apps: new AppRule("blender", Effect.Extra(30))));
        rig.World.Play(10, "Spotify");
        rig.World.FullScreen = "game";
        rig.World.Run(20, "blender");
        rig.Until(12601);
        Assert.AreEqual(210m, rig.Watcher.LockStatus.Time.Total);
        Assert.AreEqual(12585, rig.First("start"), "3:29:45");
        Assert.AreEqual(12600, rig.First("away"), "3:30:00");
    }

    [TestMethod]
    public void MusicOrVideoCountsOnceWhenSoundAndTheDisplayRequestBothFire()
    {
        using var rig = new Rig(Settings(normal: 15, music: Effect.Extra(60)));
        rig.World.Play(10, "vlc");
        rig.World.DisplayRequired = true;
        rig.Until(10);
        Assert.AreEqual(75m, rig.Watcher.LockStatus.Time.Total, "75, not 135");
    }

    // The look-ahead regression: a cut from a rule asked only every few seconds, from a process
    // scan, still moves the countdown to the cut lock time. The rules used to be asked only from the
    // normal time less the audio hold, so this locked at 30:00.
    [TestMethod]
    public void ACutFromAnAppRuleLocksAtTheCutTime()
    {
        using var rig = new Rig(Settings(normal: 30, music: Effect.Never, apps: new AppRule("render", Effect.Sooner(50))));
        rig.World.Run(20, "render");
        rig.Until(901);
        Assert.AreEqual(885, rig.First("start"));
        Assert.AreEqual(900, rig.First("away"), "15:00, one tick at most from the lock time");
    }

    [TestMethod]
    public void NormalThirtyOnBatteryAtFiftyPercentLocksAtFifteen()
    {
        using var rig = new Rig(Settings(normal: 30, music: Effect.Never, battery: Effect.Sooner(50)));
        rig.World.Power = PowerLineStatus.Offline;
        rig.Until(901);
        Assert.AreEqual(885, rig.First("start"));
        Assert.AreEqual(900, rig.First("away"));

        // From the audio hold plus 2 s before the earliest countdown on, the sound is read every
        // second, so it has been heard for its whole hold by the time the countdown could start.
        var heard = rig.World.SoundReads.Select(rig.IdleAt).Distinct().OrderBy(s => s).ToList();
        double closeFrom = 885 - 32;
        Assert.AreEqual(closeFrom, PollSchedule.CloseFrom(Settings(normal: 30, battery: Effect.Sooner(50))));
        for (double s = closeFrom; s <= 885; s++) Assert.Contains(s, heard, $"sound read at idle {s}");
    }

    [TestMethod]
    public void RulesAreAskedEveryFiveSecondsFarFromTheLockAndEverySecondNearIt()
    {
        using var rig = new Rig(Settings(normal: 30, music: Effect.Never, apps: new AppRule("render", Effect.Sooner(50))));
        rig.World.Run(20, "render");
        rig.Until(890);
        var heard = rig.World.SoundReads.Select(rig.IdleAt).Distinct().OrderBy(s => s).ToList();
        Assert.IsLessThanOrEqualTo(1, heard[0], "asked on the first tick");
        double closeFrom = PollSchedule.CloseFrom(Settings(normal: 30, apps: new AppRule("render", Effect.Sooner(50))));
        Assert.AreEqual(853, closeFrom);
        for (int i = 1; i < heard.Count; i++)
        {
            double gap = heard[i] - heard[i - 1];
            if (heard[i] <= closeFrom) Assert.IsLessThanOrEqualTo(5, gap, $"at most 5 s apart before {closeFrom}, at {heard[i]}");
            else Assert.AreEqual(1, gap, $"every second from {closeFrom}, at {heard[i]}");
        }
        // The process list is read again at least every 5 seconds, and never kept past that.
        var scans = rig.World.ProcessReads.Select(rig.IdleAt).Distinct().OrderBy(s => s).ToList();
        for (int i = 1; i < scans.Count; i++) Assert.IsLessThanOrEqualTo(5, scans[i] - scans[i - 1]);
    }

    [TestMethod]
    public void NeverHolds()
    {
        using var rig = new Rig(Settings(normal: 15, call: Effect.Never));
        rig.World.Call(30, "Teams");
        rig.Until(3 * 3600);
        Assert.AreEqual(0, rig.Count("start"));
        Assert.AreEqual(LockState.Never, rig.Watcher.LockStatus.Kind);
        Assert.AreEqual(EffectSource.Call, rig.Watcher.LockStatus.Time.NeverCause);
    }

    [TestMethod]
    public void TheCapBeatsNever()
    {
        using var rig = new Rig(Settings(normal: 15, call: Effect.Never, battery: Effect.Cap(10)));
        rig.World.Call(30, "Teams");
        rig.World.Power = PowerLineStatus.Offline;
        rig.Until(601);
        Assert.AreEqual(585, rig.First("start"));
        Assert.AreEqual(600, rig.First("away"));
    }

    [TestMethod]
    public void AnExtraDuringTheCountdownCancelsIt()
    {
        using var rig = new Rig(Settings(normal: 15, music: Effect.Extra(60)));
        rig.Until(890, idle =>
        {
            if (idle == 890) rig.World.Play(10, "Spotify");
        });
        Assert.AreEqual(885, rig.First("start"));
        Assert.AreEqual(890, rig.First("end"));
        rig.Until(4485);
        Assert.AreEqual(0, rig.Count("away"));
        Assert.AreEqual(75m, rig.Watcher.LockStatus.Time.Total);
    }

    // The late starts: the lock time drops to or below the idle time already reached.

    [TestMethod]
    public void MusicThatEndsAtFiftyMinutesGivesACountdownNotALock()
    {
        // Normal 30, music +60. The sound stops at 49:30; its 30 s hold ends at 50:00.
        using var rig = new Rig(Settings(normal: 30, music: Effect.Extra(60)));
        rig.World.Play(10, "Spotify");
        rig.Until(3030, idle =>
        {
            if (idle == 2971) rig.World.Sounds.Clear();
        });
        Assert.AreEqual(3000, rig.First("start"), "50:00");
        Assert.AreEqual(3015, rig.First("away"), "50:15, not 50:00");
        Assert.AreEqual(1, rig.LockRequests);
    }

    [TestMethod]
    public void ACallThatEndsAfterThreeHoursGivesTheFullCountdown()
    {
        using var rig = new Rig(Settings(normal: 30, call: Effect.Never));
        rig.World.Call(30, "Teams");
        rig.Until(10840, idle =>
        {
            if (idle == 10800) rig.World.Calls.Clear();
        });
        double start = rig.First("start")!.Value;
        Assert.IsGreaterThanOrEqualTo(10800, start);
        Assert.IsLessThanOrEqualTo(10809, start, "seen within the scan and hold times");
        Assert.AreEqual(start + 15, rig.First("away"), "the full countdown");
    }

    [TestMethod]
    public void ABatteryCapThatAppearsAtTwentyMinutesGivesTheFullCountdown()
    {
        using var rig = new Rig(Settings(normal: 30, battery: Effect.Cap(10)));
        rig.Until(1230, idle =>
        {
            if (idle == 1200) rig.World.Power = PowerLineStatus.Offline;
        });
        Assert.AreEqual(1200, rig.First("start"));
        Assert.AreEqual(1215, rig.First("away"));
    }

    [TestMethod]
    public void APauseThatEndsPastTheLockTimeGivesTheFullCountdown()
    {
        using var rig = new Rig(Settings(normal: 15));
        rig.Watcher.Pause(TimeSpan.FromMinutes(30));
        rig.Until(1830);
        Assert.AreEqual(1800, rig.First("start"));
        Assert.AreEqual(1815, rig.First("away"));
    }

    [TestMethod]
    public void TheFirstLookAfterStartingUpPastTheLockTimeGivesTheFullCountdown()
    {
        using var rig = new Rig(Settings(normal: 30));
        rig.Idle = 3600;
        rig.Second();
        Assert.AreEqual(3601, rig.First("start"));
        Assert.AreEqual(0, rig.LockRequests, "never on the first tick");
        rig.Until(3620);
        Assert.AreEqual(3616, rig.First("away"));
    }

    [TestMethod]
    public void ALockTimeLoweredInTheSettingsBelowTheIdleTimeGivesTheFullCountdown()
    {
        using var rig = new Rig(Settings(normal: 30));
        rig.Until(1000);
        rig.Watcher.Apply(Settings(normal: 10));
        rig.Until(1030);
        Assert.AreEqual(1001, rig.First("start"));
        Assert.AreEqual(1016, rig.First("away"));
    }

    // One bad reading never locks.

    [TestMethod]
    public void FullScreenMissingForOneTickNeverLocks()
    {
        using var rig = new Rig(Settings(normal: 15, fullScreen: Effect.Never));
        rig.World.FullScreen = "game";
        rig.Until(10800);
        rig.Second(() => rig.World.FullScreen = null);
        rig.Second(() => rig.World.FullScreen = "game");
        rig.Until(10900);
        Assert.AreEqual(0, rig.Count("start"), "held over the blink");
        Assert.AreEqual(0, rig.LockRequests);
    }

    [TestMethod]
    public void FullScreenMissingLongerThanItsHoldGivesACountdownThenACancel()
    {
        using var rig = new Rig(Settings(normal: 15, fullScreen: Effect.Never));
        rig.World.FullScreen = "game";
        rig.Until(10800);
        rig.World.FullScreen = null;
        rig.Until(10806);
        rig.World.FullScreen = "game";
        rig.Until(10900);
        Assert.AreEqual(1, rig.Count("start"));
        Assert.AreEqual(1, rig.Count("end"));
        Assert.AreEqual(0, rig.Count("away"));
        Assert.AreEqual(0, rig.LockRequests);
    }

    [TestMethod]
    public void WithNoWarningAOneTickMissDoesNotLock()
    {
        using var rig = new Rig(Settings(normal: 15, fullScreen: Effect.Never, warning: 0));
        rig.World.FullScreen = "game";
        rig.Until(10800);
        rig.Second(() => rig.World.FullScreen = null);
        rig.Second(() => rig.World.FullScreen = "game");
        rig.Until(10900);
        Assert.AreEqual(0, rig.Count("away"));
        Assert.AreEqual(0, rig.LockRequests);
    }

    [TestMethod]
    public void ARuleThatThrowsOnTheDecidingTickDoesNotLock()
    {
        using var rig = new Rig(Settings(normal: 15, fullScreen: Effect.Never));
        rig.World.FullScreen = "game";
        rig.Until(10800);
        rig.Second(() => rig.World.Failing.Add("fullscreen"));
        rig.Second(() => rig.World.Failing.Remove("fullscreen"));
        rig.Until(10900);
        Assert.AreEqual(0, rig.Count("start"));
        Assert.AreEqual(0, rig.LockRequests);
        Assert.IsEmpty(rig.Watcher.LockStatus.FailedRules, "working again");
    }

    [TestMethod]
    public void ACallThatStartsJustBeforeTheLockIsSeenAfresh()
    {
        using var rig = new Rig(Settings(normal: 15, call: Effect.Never));
        rig.Until(899);
        Assert.AreEqual(885, rig.First("start"));
        int readsBefore = rig.World.CallReads.Count;
        rig.World.Call(30, "Teams");
        rig.Second();
        Assert.IsGreaterThan(readsBefore, rig.World.CallReads.Count, "the call records are read again before the lock");
        Assert.AreEqual(0, rig.LockRequests);
        Assert.AreEqual(900, rig.First("end"));
        Assert.AreEqual(LockState.Never, rig.Watcher.LockStatus.Kind);
    }

    [TestMethod]
    public void AFailingRuleDoesNotBlockTheLock()
    {
        using var rig = new Rig(Settings(normal: 15, music: Effect.Never));
        rig.World.Failing.Add("anything");
        rig.Until(901);
        Assert.AreEqual(900, rig.First("away"));
        Assert.AreEqual(1, rig.LockRequests);
        CollectionAssert.AreEqual(new[] { "audio" }, rig.Watcher.LockStatus.FailedRules.ToArray());
    }

    [TestMethod]
    public void AFailingRuleKeepsItsLastGoodLinesForAMomentThenStopsHolding()
    {
        using var rig = new Rig(Settings(normal: 15, call: Effect.Never));
        rig.World.Call(30, "Teams");
        rig.Until(5000);
        rig.World.Failing.Add("calls");
        rig.Until(5000 + Evaluator.KeepAfterFailure.TotalSeconds - 1);
        Assert.AreEqual(0, rig.Count("start"), "still a call for a little while");
        rig.Until(5100);
        Assert.AreEqual(1, rig.LockRequests, "a rule that keeps failing does not hold the lock off");
        Assert.AreEqual(rig.First("start") + 15, rig.First("away"));
    }

    [TestMethod]
    public void MediaThatStoppedJustBeforeClosePollingStillHoldsUntilItsHoldEnds()
    {
        using var rig = new Rig(Settings(normal: 15, music: Effect.Never));
        double closeFrom = PollSchedule.CloseFrom(Settings(normal: 15));
        Assert.AreEqual(853, closeFrom);
        rig.World.Play(10, "Spotify");
        rig.Until(closeFrom, idle =>
        {
            if (idle == closeFrom - 1) rig.World.Sounds.Clear();
        });
        // The last 5-second look that heard it was at most 4 s before it stopped.
        Assert.AreEqual(LockState.Never, rig.Watcher.LockStatus.Kind);
        rig.Until(closeFrom + 30 - 6);
        Assert.AreEqual(LockState.Never, rig.Watcher.LockStatus.Kind, "still within its 30 s hold");
        rig.Until(closeFrom + 30);
        Assert.AreEqual(LockState.On, rig.Watcher.LockStatus.Kind);
        rig.Until(901);
        Assert.AreEqual(885, rig.First("start"));
        Assert.AreEqual(900, rig.First("away"));
    }

    [TestMethod]
    public void MediaHeardDuringAPauseStillHoldsWhenThePauseEnds()
    {
        using var rig = new Rig(Settings(normal: 15, music: Effect.Never));
        rig.Watcher.Pause(TimeSpan.FromMinutes(20));
        rig.World.Play(10, "Spotify");
        rig.Until(1260, idle =>
        {
            if (idle == 1191) rig.World.Sounds.Clear();
        });
        // Heard last at 1190; the pause ends at 1200; the hold ends at 1220.
        Assert.AreEqual(1220, rig.First("start"));
        Assert.AreEqual(1235, rig.First("away"));
    }

    [TestMethod]
    public void WithANeverCallAtTwoMinutesTheTooltipSaysNever()
    {
        using var rig = new Rig(Settings(normal: 15, call: Effect.Never));
        rig.World.Call(30, "Teams");
        rig.Until(120);
        var status = rig.Watcher.LockStatus;
        Assert.AreEqual(LockState.Never, status.Kind);
        StringAssert.Contains(English.Text.Tooltip(status), English.Text.V1.DuringCall);
        Assert.AreEqual("not locking, microphone in use: Teams", rig.Watcher.HeartbeatText(), "the log too");
    }

    // Calls and music (D32, D5).

    [TestMethod]
    public void ACallAndQuieterMusicFromAnotherAppCountBothExtras()
    {
        using var rig = new Rig(Settings(normal: 15, music: Effect.Extra(60), call: Effect.Extra(30)));
        rig.World.Call(30, "Teams");
        rig.World.Play(30, "Teams", 0.8f);
        rig.World.Play(10, "Spotify", 0.2f);
        rig.Until(10);
        Assert.AreEqual(105m, rig.Watcher.LockStatus.Time.Total);
        Assert.Contains(new WhyLine(Signal.Sound, Effect.Extra(60), "Spotify"), rig.Watcher.Explain().ToList());
    }

    [TestMethod]
    public void ACallAloneAddsOnlyTheCallExtra()
    {
        using var rig = new Rig(Settings(normal: 15, music: Effect.Extra(60), call: Effect.Extra(30)));
        rig.World.Call(30, "Teams");
        rig.World.Play(30, "Teams");
        rig.World.DisplayRequired = true;
        rig.Until(10);
        Assert.AreEqual(45m, rig.Watcher.LockStatus.Time.Total, "the call's own sound and display request are the call");
        var why = rig.Watcher.Explain();
        Assert.Contains(new WhyLine(Signal.Sound, Effect.Extra(60), "Teams", Counts: false), why.ToList());
        Assert.Contains(new WhyLine(Signal.Display, Effect.Extra(60), Counts: false), why.ToList());
    }

    [TestMethod]
    public void APackagedCallAppsOwnSoundIsNotMusic()
    {
        using var rig = new Rig(Settings(normal: 15, music: Effect.Never, call: Effect.Extra(30)));
        rig.World.Calls.Add(new CallApp("MSTeams", Family: "MSTeams_8wekyb3d8bbwe"));
        rig.World.Families[40] = "MSTeams_8wekyb3d8bbwe";
        rig.World.Play(40, "ms-teams");
        rig.Until(10);
        Assert.AreEqual(45m, rig.Watcher.LockStatus.Time.Total);
    }

    [TestMethod]
    public void ACallAppsSoundFromAHelperProcessIsTheCall()
    {
        using var rig = new Rig(Settings(normal: 15, music: Effect.Never, call: Effect.Extra(30)));
        rig.World.Calls.Add(new CallApp("MSTeams", Family: "MSTeams_8wekyb3d8bbwe"));
        rig.World.Families[40] = "MSTeams_8wekyb3d8bbwe";
        rig.World.Parents[41] = 40;
        rig.World.Parents[42] = 41;
        rig.World.Play(42, "msedgewebview2");
        rig.Until(10);
        Assert.AreEqual(45m, rig.Watcher.LockStatus.Time.Total);
    }

    [TestMethod]
    public void AnAppWithTheCallAppsNameButAnotherFileIsMusic()
    {
        using var rig = new Rig(Settings(normal: 15, music: Effect.Extra(60), call: Effect.Extra(30)));
        rig.World.Call(30, "Teams");
        rig.World.Paths[50] = @"D:\Other\Teams.exe";
        rig.World.Play(50, "Teams");
        rig.Until(10);
        Assert.AreEqual(105m, rig.Watcher.LockStatus.Time.Total, "matched by process, never by name");
    }

    [TestMethod]
    public void ACallSetToChangeNothingStillKeepsItsSoundAndDisplayOutOfMusic()
    {
        using var rig = new Rig(Settings(normal: 15, music: Effect.Never));
        rig.World.Call(30, "Teams");
        rig.World.Play(30, "Teams");
        rig.World.DisplayRequired = true;
        rig.Until(901);
        Assert.AreEqual(900, rig.First("away"), "a video call set to lock as usual locks at the lock time");
    }

    // App rules.

    [TestMethod]
    public void TwelveProcessesOfOneAppAddItsExtraOnce()
    {
        using var rig = new Rig(Settings(normal: 15, apps: new AppRule("chrome", Effect.Extra(30))));
        for (int i = 0; i < 12; i++) rig.World.Run(100 + i, "chrome");
        rig.Until(10);
        Assert.AreEqual(45m, rig.Watcher.LockStatus.Time.Total);
        Assert.HasCount(1, rig.Watcher.Explain());
    }

    [TestMethod]
    public void TwoRulesForOneAppTheFirstWins()
    {
        using var rig = new Rig(Settings(normal: 30, apps: new[] { new AppRule("chrome", Effect.Extra(30)), new AppRule(@"C:\x\CHROME.exe", Effect.Sooner(50)) }));
        rig.World.Run(100, "chrome");
        rig.Until(10);
        Assert.AreEqual(60m, rig.Watcher.LockStatus.Time.Total);
    }

    // How the rules add up, and how they are asked.

    [TestMethod]
    public void NeverBeatsAnExtraWhicheverComesFirst()
    {
        using var rig = new Rig(Settings(normal: 15, music: Effect.Extra(60), call: Effect.Never));
        rig.World.Play(10, "Spotify");
        rig.Until(100);
        Assert.AreEqual(75m, rig.Watcher.LockStatus.Time.Total);
        rig.World.Call(30, "Teams");
        rig.Until(110);
        Assert.IsTrue(rig.Watcher.LockStatus.Time.NeverLocks);
        Assert.AreEqual("not locking, microphone in use: Teams", rig.Watcher.HeartbeatText(), "the log names the never, not the extra");

        using var other = new Rig(Settings(normal: 15, music: Effect.Extra(60), call: Effect.Never));
        other.World.Call(30, "Teams");
        other.Until(100);
        other.World.Play(10, "Spotify");
        other.Until(110);
        Assert.IsTrue(other.Watcher.LockStatus.Time.NeverLocks, "the never first, then the extra");
        Assert.AreEqual("not locking, microphone in use: Teams", other.Watcher.HeartbeatText());
    }

    [TestMethod]
    public void EveryRuleIsAskedEvenWhileOneSaysNever()
    {
        using var rig = new Rig(Settings(normal: 15, music: Effect.Never, call: Effect.Never));
        rig.World.Call(30, "Teams");
        rig.World.Play(10, "Spotify");
        rig.Until(950);
        var heard = rig.World.SoundReads.Select(rig.IdleAt).Distinct().ToList();
        for (double s = 860; s <= 950; s++) Assert.Contains(s, heard, $"the audio rule keeps listening during a call, idle {s}");
        var why = rig.Watcher.Explain().ToList();
        Assert.Contains(new WhyLine(Signal.Call, Effect.Never, "Teams"), why);
        Assert.Contains(new WhyLine(Signal.Sound, Effect.Never, "Spotify"), why);
    }

    [TestMethod]
    public void AFailingRuleDoesNotStopTheOthers()
    {
        using var rig = new Rig(Settings(normal: 15, music: Effect.Never, fullScreen: Effect.Extra(60)));
        rig.World.Failing.Add("anything");
        rig.World.FullScreen = "game";
        rig.Until(10);
        Assert.AreEqual(75m, rig.Watcher.LockStatus.Time.Total);
        CollectionAssert.AreEqual(new[] { "audio" }, rig.Watcher.LockStatus.FailedRules.ToArray());
    }

    // Status.

    [TestMethod]
    public void LockStatusListsEveryCause()
    {
        using var rig = new Rig(Settings(normal: 30, music: Effect.Extra(60), fullScreen: Effect.Extra(120), call: Effect.Never, battery: Effect.Cap(10),
            apps: new AppRule("render", Effect.Sooner(25))));
        rig.World.Play(10, "Spotify");
        rig.World.FullScreen = "game";
        rig.World.Call(30, "Teams");
        rig.World.Run(20, "render");
        rig.World.Power = PowerLineStatus.Offline;
        rig.Until(10);
        var time = rig.Watcher.LockStatus.Time;
        CollectionAssert.AreEquivalent(new[] { EffectSource.MusicOrVideo, EffectSource.FullScreen }, time.Extras.Select(e => e.Source).ToArray());
        CollectionAssert.AreEqual(new[] { EffectSource.Call }, time.NeverCauses.ToArray());
        Assert.AreEqual(EffectSource.ForApp("render"), time.Cut?.Source);
        Assert.AreEqual(EffectSource.Battery, time.Cap?.Source);
        Assert.AreEqual(10m, time.Total, "the cap beats never");
        CollectionAssert.AreEquivalent(new[] { Signal.Sound, Signal.FullScreen, Signal.Call, Signal.App, Signal.Battery },
            rig.Watcher.Explain().Select(l => l.Signal).Distinct().ToArray());
    }

    [TestMethod]
    public void TheLogNamesWhatHoldsOrWaits()
    {
        using var rig = new Rig(Settings(normal: 15, music: Effect.Extra(60)));
        rig.World.Play(10, "Spotify");
        rig.Until(60);
        Assert.AreEqual("waiting longer, locks in 1:14:00, audio playing: Spotify", rig.Watcher.HeartbeatText());
        Assert.AreEqual(LockState.On, rig.Watcher.LockStatus.Kind);
        Assert.AreEqual(75 * 60 - 60, rig.Watcher.LockStatus.SecondsLeft);
    }

    [TestMethod]
    public void ExplainGivesTheLastLookWithoutAskingAgain()
    {
        using var rig = new Rig(Settings(normal: 15, music: Effect.Never));
        rig.World.Play(10, "Spotify");
        rig.Until(10);
        var reads = rig.World.SoundReads.ToList();
        CollectionAssert.AreEqual(rig.Watcher.LastLook.Lines.ToArray(), rig.Watcher.Explain().ToArray());
        CollectionAssert.AreEqual(reads, rig.World.SoundReads, "the look the watcher decides by");
    }

    [TestMethod]
    public void KeepLiveAsksEveryRuleEverySecondUntilReleased()
    {
        using var rig = new Rig(Settings(normal: 15, music: Effect.Never));
        rig.Busy();
        var live = rig.Watcher.KeepLive();
        int before = rig.World.SoundReads.Count;
        for (int i = 0; i < 10; i++) rig.Busy();
        Assert.HasCount(before + 10, rig.World.SoundReads);
        live.Dispose();
        live.Dispose();
        rig.Busy();
        before = rig.World.SoundReads.Count;
        for (int i = 0; i < 10; i++) rig.Busy();
        Assert.HasCount(before + 2, rig.World.SoundReads, "every 5 seconds again");
    }

    [TestMethod]
    public void ApplyKeepsWhatTheAudioRuleHeard()
    {
        using var rig = new Rig(Settings(normal: 15, music: Effect.Never));
        rig.World.Play(10, "Spotify");
        rig.Until(880);
        rig.World.Sounds.Clear();
        rig.Watcher.Apply(Settings(normal: 15, music: Effect.Never));
        rig.Second();
        Assert.AreEqual(LockState.Never, rig.Watcher.LockStatus.Kind, "the sound heard a second ago still holds");
    }

    // The tray calls Restate right after new settings: the status and the tooltip show the new rules
    // at once, not the old ones until the next tick.
    [TestMethod]
    public void TheStatusRightAfterNewSettingsUsesThem()
    {
        using var rig = new Rig(Settings(normal: 15, fullScreen: Effect.Never));
        rig.World.FullScreen = "quillpad";
        rig.Until(60);
        Assert.AreEqual(LockState.Never, rig.Watcher.LockStatus.Kind);

        rig.Watcher.Apply(Settings(normal: 15));
        rig.Watcher.Restate();
        Assert.AreEqual(LockState.On, rig.Watcher.LockStatus.Kind, "full screen changes nothing now");
        Assert.AreEqual(840, rig.Watcher.LockStatus.SecondsLeft, 1e-9);

        rig.Watcher.Apply(Settings(normal: 15, fullScreen: Effect.Extra(30)));
        rig.Watcher.Restate();
        Assert.AreEqual(45m, rig.Watcher.LockStatus.Time.Total, "and an extra counts at once");
    }

    // A pause of 15 minutes is logged in minutes, not as "0.3 h".
    [TestMethod]
    public void APauseIsLoggedInMinutes()
    {
        Assert.AreEqual("paused for 15 min", Watcher.PauseText(TimeSpan.FromMinutes(15)));
        Assert.AreEqual("paused for 30 min", Watcher.PauseText(TimeSpan.FromMinutes(30)));
        Assert.AreEqual("paused for 120 min", Watcher.PauseText(TimeSpan.FromHours(2)));
        Assert.AreEqual("paused until resumed", Watcher.PauseText(null));
    }

    // Safety and the day's counts.

    [TestMethod]
    public void TheDryRunNeverAsksForALock()
    {
        using var dry = new Rig(Settings(normal: 15), dryRun: true);
        dry.Until(901);
        Assert.AreEqual(900, dry.First("away locking"));
        Assert.AreEqual(0, dry.LockRequests);
        Assert.IsTrue(dry.Watcher.LockStatus.DryRun);
        Assert.IsTrue(SessionLock.Blocked, "the guard stays set");

        using var real = new Rig(Settings(normal: 15));
        real.Until(901);
        Assert.AreEqual(1, real.LockRequests, "the same run without --dry-run asks once");
    }

    [TestMethod]
    public void TheDryRunCountsAwayTimeButNoLock()
    {
        using var rig = new Rig(Settings(normal: 15), dryRun: true);
        rig.Until(960);
        var today = rig.Watcher.LockStatus.Today;
        Assert.AreEqual(0, today.Locks);
        Assert.AreEqual(TimeSpan.FromSeconds(960), today.Away, "from when the idle stretch began");
    }

    [TestMethod]
    public void EveryLockOfTheSessionCountsForToday()
    {
        using var rig = new Rig(Settings(normal: 15));
        rig.Until(901);
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionLock);
        rig.Second();
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionLock);
        rig.Second();
        Assert.AreEqual(1, rig.Watcher.LockStatus.Today.Locks, "one lock, reported twice");
        Assert.AreEqual(LockState.SessionLocked, rig.Watcher.LockStatus.Kind);
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionUnlock);
        rig.Busy();
        Assert.AreEqual(1, rig.Count("back"));
    }

    // A lock and an unlock that both land between two ticks (a quick face unlock, or both messages
    // handled together after a stall) still end the countdown note, which would otherwise stay up
    // and hold back every note.
    [TestMethod]
    public void ALockAndUnlockBetweenTwoTicksEndTheCountdown()
    {
        using var rig = new Rig(Settings(normal: 15));
        rig.Until(890);
        Assert.AreEqual(885, rig.First("start"));

        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionLock);
        Assert.AreEqual(1, rig.Count("end"), "the box goes when the session locks, not at the next tick");
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionUnlock);
        for (int i = 0; i < 5; i++) rig.Busy();

        Assert.AreEqual(1, rig.Count("start"));
        Assert.AreEqual(1, rig.Count("end"));
        Assert.AreEqual(LockState.On, rig.Watcher.LockStatus.Kind);
        Assert.AreEqual(0, rig.LockRequests);
    }

    [TestMethod]
    public void ALockWithATickBeforeTheUnlockEndsTheCountdownOnce()
    {
        using var rig = new Rig(Settings(normal: 15));
        rig.Until(890);
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionLock);
        rig.Second();
        Assert.AreEqual(LockState.SessionLocked, rig.Watcher.LockStatus.Kind);
        rig.Watcher.SessionSwitched(SessionSwitchReason.SessionUnlock);
        for (int i = 0; i < 3; i++) rig.Busy();

        Assert.AreEqual(1, rig.Count("start"));
        Assert.AreEqual(1, rig.Count("end"), "ended once, at the lock");
    }

    // Lock now twice never steps out again.
    [TestMethod]
    public void LockNowTwiceLocksOnce()
    {
        using var rig = new Rig(Settings(normal: 15));
        rig.Busy();

        rig.Watcher.LockNow();
        rig.Watcher.LockNow();
        rig.Second();

        Assert.AreEqual(1, rig.Count("away locking"));
        Assert.AreEqual(1, rig.LockRequests);
        Assert.AreEqual(LockState.Away, rig.Watcher.LockStatus.Kind);
    }

    // With locking turned off, Lock now after stepping out locks the same away, once: one more
    // WentAway(true), for the away actions to add the lock, and one lock request.
    [TestMethod]
    public void LockNowAfterAStepOutWithoutALockLocksTheSameAwayOnce()
    {
        var config = Settings(normal: 15);
        config.LockWhenAway = false;
        using var rig = new Rig(config);
        rig.Until(901);
        Assert.AreEqual(900, rig.First("away"));
        Assert.AreEqual(0, rig.Count("away locking"));
        Assert.AreEqual(0, rig.LockRequests);
        var awayBefore = rig.Watcher.LockStatus.Today.Away;

        rig.Watcher.LockNow();
        rig.Watcher.LockNow();
        rig.Second();

        Assert.AreEqual(2, rig.Count("away"), "the first away, then the lock added to it");
        Assert.AreEqual(1, rig.Count("away locking"));
        Assert.AreEqual(1, rig.LockRequests);
        Assert.AreEqual(0, rig.Count("back"));
        Assert.AreEqual(awayBefore + TimeSpan.FromSeconds(1), rig.Watcher.LockStatus.Today.Away, "one away stretch, still counting");
    }

    // "At the lock time" adds to SteppedOut's own lock only: WentAway says which lock that is.

    [TestMethod]
    public void TheLockAtTheLockTimeIsSteppedOutsOwn()
    {
        foreach (bool dryRun in new[] { false, true })
        {
            using var rig = new Rig(Settings(normal: 15), dryRun: dryRun);
            rig.Until(901);
            Assert.AreEqual(1, rig.Count("away locking"), dryRun.ToString());
            Assert.AreEqual(1, rig.Count("own lock"), dryRun.ToString());
            Assert.AreEqual(900, rig.First("own lock"), dryRun.ToString());
        }
    }

    [TestMethod]
    public void LockNowIsNotSteppedOutsOwnLock()
    {
        using var rig = new Rig(Settings(normal: 15));
        rig.Busy();
        rig.Watcher.LockNow();
        rig.Second();
        Assert.AreEqual(1, rig.Count("away locking"));
        Assert.AreEqual(0, rig.Count("own lock"));
    }

    [TestMethod]
    public void NeitherIsAnAwayWithoutALockNorLockNowAddedToIt()
    {
        var config = Settings(normal: 15);
        config.LockWhenAway = false;
        using var rig = new Rig(config);
        rig.Until(901);
        rig.Watcher.LockNow();
        rig.Second();
        Assert.AreEqual(1, rig.Count("away locking"));
        Assert.AreEqual(0, rig.Count("own lock"));
    }

    // A lock request Windows turns down is asked for once more a few seconds later, while still
    // away and not locked; never more than that.

    [TestMethod]
    public void ALockWindowsTurnsDownIsAskedForOnceMore()
    {
        using var rig = new Rig(Settings(normal: 15)) { LockFails = true };
        rig.Until(901);
        Assert.AreEqual(1, rig.LockRequests);
        rig.Until(900 + Watcher.LockRetryAfter.TotalSeconds - 1);
        Assert.AreEqual(1, rig.LockRequests, "not before the wait");
        rig.Until(900 + Watcher.LockRetryAfter.TotalSeconds);
        Assert.AreEqual(2, rig.LockRequests);
        rig.Until(4000);
        Assert.AreEqual(2, rig.LockRequests, "once, never a loop");
        Assert.AreEqual(1, rig.Count("away"), "the away actions ran once");
        Assert.AreEqual(LockState.Away, rig.Watcher.LockStatus.Kind);
    }

    [TestMethod]
    public void ALockThatWorksIsNotAskedForAgain()
    {
        using var rig = new Rig(Settings(normal: 15));
        rig.Until(1000);
        Assert.AreEqual(1, rig.LockRequests);
    }

    [TestMethod]
    public void NoSecondLockRequestOnceTheSessionLocksOrYouAreBack()
    {
        using var locked = new Rig(Settings(normal: 15)) { LockFails = true };
        locked.Until(902);
        locked.Watcher.SessionSwitched(SessionSwitchReason.SessionLock);
        locked.Until(1000);
        Assert.AreEqual(1, locked.LockRequests, "locked after all");

        using var back = new Rig(Settings(normal: 15)) { LockFails = true };
        back.Until(901);
        back.Second(() => back.Input = true);
        back.Busy();
        back.Second(() => back.Input = true);
        back.Until(60);
        Assert.AreEqual(1, back.Count("back"));
        Assert.AreEqual(1, back.LockRequests, "back before the second try");

        using var dry = new Rig(Settings(normal: 15), dryRun: true) { LockFails = true };
        dry.Until(1000);
        Assert.AreEqual(0, dry.LockRequests, "a dry run never asks");
    }

    [TestMethod]
    public void LockNowThatWindowsTurnsDownIsAskedForOnceMore()
    {
        using var rig = new Rig(Settings(normal: 15)) { LockFails = true };
        rig.Busy();
        rig.Watcher.LockNow();
        for (int i = 0; i < 20; i++) rig.Second();
        Assert.AreEqual(2, rig.LockRequests);
    }

    // Sleep and hibernation: no tick runs, both clocks move on, and the first input on waking may
    // reach the first tick. That gap is a rest for the reminders, not hours at the PC.
    [TestMethod]
    public void SleepCountsAsABreakForTheReminders()
    {
        using var rig = new Rig(new Config { BreakReminders = true, BreakMinutes = 60, EyeBreaks = true });
        for (int i = 0; i < 5; i++) rig.Busy();

        rig.Jump(TimeSpan.FromHours(8));
        for (int i = 0; i < 10; i++) rig.Busy();

        Assert.AreEqual(0, rig.Count("reminder"), string.Join(" | ", rig.Events.Select(e => e.What)));
    }

    [TestMethod]
    public void AGapOfAMinuteOrMoreRestsTheEyesAndAShorterOneDoesNot()
    {
        using var rest = new Rig(new Config { EyeBreaks = true });
        for (int i = 0; i < 19 * 60; i++) rest.Busy();
        rest.Jump(EyeBreaks.RestedAfter);
        for (int i = 0; i < 2 * 60; i++) rest.Busy();
        Assert.AreEqual(0, rest.Count("reminder"), "a tick gap past the rest time starts the 20 minutes again");

        using var blink = new Rig(new Config { EyeBreaks = true });
        for (int i = 0; i < 19 * 60; i++) blink.Busy();
        blink.Jump(TimeSpan.FromSeconds(30));
        for (int i = 0; i < 2 * 60; i++) blink.Busy();
        Assert.AreEqual(1, blink.Count("reminder EyeBreak"), "a short stall is not a rest");
    }

    // Reminders.

    [TestMethod]
    public void ABreakReminderWaitsForACallToEnd()
    {
        using var rig = new Rig(new Config { BreakReminders = true, BreakMinutes = 1, OnCall = Effect.None });
        rig.World.Call(30, "Teams");
        for (int i = 0; i < 120; i++) rig.Busy();
        Assert.AreEqual(0, rig.Count("reminder"), "not during a call");
        rig.World.Calls.Clear();
        for (int i = 0; i < 20; i++) rig.Busy();
        Assert.AreEqual(1, rig.Count("reminder Break"));
    }

    [TestMethod]
    public void AnEyeBreakComesOnlyAsAReminder()
    {
        using var rig = new Rig(new Config { EyeBreaks = true });
        for (int i = 0; i < 20 * 60 + 5; i++) rig.Busy();
        Assert.AreEqual(1, rig.Count("reminder EyeBreak"));
        Assert.AreEqual(0, rig.Count("reminder Break"));
    }

    [TestMethod]
    public void ASnoozedReminderComesBackAfterTheSnoozeTime()
    {
        using var rig = new Rig(new Config { BreakReminders = true, BreakMinutes = 1, SnoozeMinutes = 10 });
        for (int i = 0; i < 61; i++) rig.Busy();
        Assert.AreEqual(1, rig.Count("reminder Break"));
        rig.Watcher.Snooze(ReminderKind.Break);
        for (int i = 0; i < 9 * 60; i++) rig.Busy();
        Assert.AreEqual(1, rig.Count("reminder Break"));
        for (int i = 0; i < 61; i++) rig.Busy();
        Assert.AreEqual(2, rig.Count("reminder Break"));
    }

    // When the rules are asked.

    [TestMethod]
    public void TheLowestLockTimeIsNeverAboveOneTheSettingsCanGive()
    {
        var random = new Random(20261020);
        for (int round = 0; round < 3000; round++)
        {
            decimal normal = random.Next(1, 200);
            var possible = new List<Effect>();
            for (int i = random.Next(0, 4); i > 0; i--) possible.Add(Effect.Sooner(random.Next(1, 96)));
            for (int i = random.Next(0, 3); i > 0; i--) possible.Add(Effect.Cap(random.Next(1, 300)));
            decimal lowest = PollSchedule.LowestTotal(normal, possible);
            int warning = random.Next(0, 120);
            decimal earliest = LockTime.CountdownStart(lowest, warning);

            // Any subset of them, with any extras and nevers.
            var active = possible.Where(_ => random.Next(2) == 0).Select(e => new ActiveEffect(e, EffectSource.Battery)).ToList();
            for (int i = random.Next(0, 3); i > 0; i--) active.Add(new ActiveEffect(Effect.Extra(random.Next(1, 120)), EffectSource.ForApp("a" + i)));
            if (random.Next(4) == 0) active.Add(new ActiveEffect(Effect.Never, EffectSource.Call));
            if (LockTime.Compute(normal, active).Total is not decimal total) continue;
            Assert.IsGreaterThanOrEqualTo(lowest, total, $"round {round}");
            Assert.IsGreaterThanOrEqualTo(earliest, LockTime.CountdownStart(total, warning), $"round {round}");
        }
    }

    [TestMethod]
    public void ClosePollingStartsTheAudioHoldBeforeTheEarliestCountdown()
    {
        Assert.AreEqual(900 - 15 - 32, PollSchedule.CloseFrom(new Config { IdleMinutes = 15, GraceSeconds = 15, AudioSilenceSeconds = 30 }));
        Assert.AreEqual(900 - 15 - 12, PollSchedule.CloseFrom(new Config { IdleMinutes = 30, GraceSeconds = 15, AudioSilenceSeconds = 10, OnBattery = Effect.Sooner(50) }));
        Assert.AreEqual(480 - 15 - 32, PollSchedule.CloseFrom(new Config { IdleMinutes = 15, OnBattery = Effect.Sooner(50) }), "7.5 min rounds up to 8");
        Assert.AreEqual(120 - 15 - 32, PollSchedule.CloseFrom(new Config { IdleMinutes = 30, AppRules = { new AppRule("x", Effect.Cap(2)) } }));
        Assert.AreEqual(0, PollSchedule.CloseFrom(new Config { IdleMinutes = 0.5 }), "never before idle 0");
        Assert.AreEqual(900 - 15 - 32, PollSchedule.CloseFrom(new Config { IdleMinutes = 15, AppRules = { new AppRule("x", Effect.Extra(60)), new AppRule("y", Effect.Never) } }),
            "extras and nevers never bring it forward");
    }

    [TestMethod]
    public void AllRulesAreAskedFarAwayEveryFifthTick()
    {
        Assert.IsFalse(PollSchedule.AllRules(10, 853, 4, false));
        Assert.IsTrue(PollSchedule.AllRules(10, 853, 5, false));
        Assert.IsTrue(PollSchedule.AllRules(853, 853, 1, false));
        Assert.IsTrue(PollSchedule.AllRules(10, 853, 1, true));
    }

    [TestMethod]
    public void RulesFromTheSettingsAreOnlyTheOnesThatCanChangeSomething()
    {
        CollectionAssert.AreEqual(new[] { "call" }, Rules.Build(Settings()).Select(r => r.Name).ToArray(), "calls are always watched");
        CollectionAssert.AreEqual(new[] { "call", "audio", "display", "fullscreen", "apps", "battery" },
            Rules.Build(Settings(music: Effect.Never, fullScreen: Effect.Extra(5), battery: Effect.Sooner(25), apps: new AppRule("x", Effect.Never))).Select(r => r.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "call" }, Rules.Build(Settings(battery: Effect.Extra(5), music: Effect.Sooner(10))).Select(r => r.Name).ToArray(),
            "an effect a setting cannot have is left out");
        CollectionAssert.AreEqual(new[] { "call", "audio", "display", "fullscreen" }, Rules.Build(new Config()).Select(r => r.Name).ToArray(),
            "new users: music or video, full screen and calls all say never");
    }
}
