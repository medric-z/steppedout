using Microsoft.Win32;
using System.Diagnostics;

namespace SteppedOut.Tests;

// Made-up stand-ins for everything the away actions reach outside the test. Nothing here plays,
// mutes, turns off, starts or closes anything real. Kept in one holder class so its names never meet
// another test file's helpers.
static class AwayFakes
{
    public static void Wait(Task task, string what)
    {
        Assert.IsTrue(task.Wait(TimeSpan.FromSeconds(10)), what);
    }

    // A clock that only moves when the test moves it. Timers fire inside Advance, in order.
    public sealed class ManualTime : TimeProvider
    {
        readonly object gate = new();
        readonly List<Slot> timers = new();
        DateTimeOffset now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            lock (gate) return now;
        }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            lock (gate) return now.UtcTicks;
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var slot = new Slot(this, callback, state);
            slot.Change(dueTime, period);
            return slot;
        }

        // Timers waiting to fire.
        public int Pending
        {
            get
            {
                lock (gate) return timers.Count(timer => timer.Due is not null);
            }
        }

        // Waits, in real time, until at least this many timers are waiting, so the test never moves
        // the clock before the code under test has started its wait.
        public void WaitForPending(int count)
        {
            Assert.IsTrue(SpinWait.SpinUntil(() => Pending >= count, TimeSpan.FromSeconds(10)), $"{count} timer(s) waiting");
        }

        public void Advance(TimeSpan by)
        {
            DateTimeOffset target;
            lock (gate) target = now + by;
            while (true)
            {
                Slot? next;
                lock (gate)
                {
                    next = timers.Where(timer => timer.Due is { } due && due <= target).OrderBy(timer => timer.Due).FirstOrDefault();
                    if (next is null)
                    {
                        now = target;
                        return;
                    }
                    now = next.Due!.Value;
                    next.Due = next.Period > TimeSpan.Zero ? now + next.Period : null;
                }
                next.Fire();
            }
        }

        sealed class Slot(ManualTime clock, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset? Due { get; set; }

            public TimeSpan Period { get; private set; }

            public void Fire() => callback(state);

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (clock.gate)
                {
                    if (!clock.timers.Contains(this)) clock.timers.Add(this);
                    Period = period == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : period;
                    Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.now + (dueTime < TimeSpan.Zero ? TimeSpan.Zero : dueTime);
                }
                return true;
            }

            public void Dispose()
            {
                lock (clock.gate) clock.timers.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    // What happened, in order, across every fake.
    public sealed class Recorder
    {
        readonly object gate = new();
        readonly List<string> events = new();

        public void Add(string what)
        {
            lock (gate) events.Add(what);
        }

        public List<string> Events
        {
            get
            {
                lock (gate) return events.ToList();
            }
        }

        public List<string> Starting(params string[] prefixes) => Events.Where(e => prefixes.Any(p => e.StartsWith(p, StringComparison.Ordinal))).ToList();
    }

    public sealed class FakeSound(Recorder rec, ManualTime time) : ISoundPlayer
    {
        public DateTimeOffset? WaitStarted { get; private set; }

        public DateTimeOffset? WaitEnded { get; private set; }

        // A built-in sound as long as it is in the exe; an own sound, ten seconds, the longest it can be.
        public static TimeSpan Length(Sound sound) => sound.Clip is SoundClip clip
            ? TimeSpan.FromSeconds((SoundClips.Bytes(clip).Length - 44) / (44100.0 * 2))
            : TimeSpan.FromSeconds(OwnSound.LongestSeconds);

        public void Play(Sound sound) => rec.Add($"sound {sound}");

        // Plays on the fake clock: the clock moves on by the length of the sound before it returns.
        public void PlayAndWait(Sound sound)
        {
            WaitStarted = time.GetUtcNow();
            rec.Add($"sound {sound} waited for");
            time.Advance(Length(sound));
            WaitEnded = time.GetUtcNow();
        }
    }

    public sealed class FakeMedia(Recorder rec) : IMediaControl
    {
        int next = 1;

        public string[] Playing { get; set; } = ["Player"];

        public bool Throws { get; set; }

        public int Paused { get; private set; }

        public int PlayedAgain { get; private set; }

        public int Released { get; private set; }

        public MediaSessions.Paused PausePlaying()
        {
            rec.Add("pause");
            if (Throws) throw new InvalidOperationException("the media app hung");
            var paused = new MediaSessions.Paused();
            foreach (string app in Playing) paused.Sessions.Add((new IntPtr(next++), app));
            Paused += paused.Sessions.Count;
            return paused;
        }

        public List<string> PlayAgain(MediaSessions.Paused paused)
        {
            rec.Add("play again");
            var apps = paused.Apps.ToList();
            PlayedAgain += paused.Sessions.Count;
            paused.Sessions.Clear();
            return apps;
        }

        public void Release(MediaSessions.Paused paused)
        {
            rec.Add("let go");
            Released += paused.Sessions.Count;
            paused.Sessions.Clear();
        }
    }

    public sealed class FakeScreens(Recorder rec) : IScreens
    {
        public bool TurnOff()
        {
            rec.Add("screens off");
            return true;
        }

        public uint? LastInputTick() => 0;

        public uint Now => 0;
    }

    // The PC's sleep: it locks at once and can sleep, unless the test says otherwise.
    public sealed class FakeSleeper(Recorder rec) : ISleeper
    {
        int looks;

        public bool CanSleep { get; set; } = true;

        public bool Locked { get; set; } = true;

        public bool Refuses { get; set; }

        // Runs inside Sleep, where the real one waits until the PC wakes.
        public Action? WhileAsleep { get; set; }

        // How many times the lock was looked for.
        public int Looks => Volatile.Read(ref looks);

        public bool Sleep()
        {
            rec.Add("sleep");
            WhileAsleep?.Invoke();
            return !Refuses;
        }

        public bool SessionLocked()
        {
            Interlocked.Increment(ref looks);
            return Locked;
        }
    }

    public sealed class FakeStarter(Recorder rec) : IStarter
    {
        public List<ProcessStartInfo> Started { get; } = new();

        public void Start(ProcessStartInfo start)
        {
            lock (Started) Started.Add(start);
            rec.Add("start " + start.FileName);
        }
    }

    public sealed class FakeProcess
    {
        readonly TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public required int Id { get; init; }

        public required string Exe { get; set; }

        public int ParentId { get; set; }

        public string? Path { get; set; }

        public long? Start { get; set; } = 1_000;

        public int Session { get; set; } = 1;

        // Windows' answer when the process is opened: 0 for a handle, 5 for "runs as administrator".
        public int OpenError { get; set; }

        public int KillError { get; set; }

        // Closes by itself once every one of its windows was asked.
        public bool ClosesWhenAsked { get; set; }

        // Exits just as the force reaches it.
        public bool ExitsAtTheForce { get; set; }

        // Asking one of its windows to close fails.
        public bool AskFails { get; set; }

        public List<IntPtr> Windows { get; } = new();

        public bool Exited { get; private set; }

        public Task WhenExited => exited.Task;

        public void Exit()
        {
            Exited = true;
            exited.TrySetResult();
        }
    }

    // A made-up process list. The test's own process is 1000, in sign-in 1.
    public sealed class FakeProcesses : IProcessApi
    {
        readonly object gate = new();
        readonly Recorder rec;
        readonly List<FakeProcess> processes = new();
        readonly HashSet<IntPtr> asked = new();
        int nextWindow = 0x1000;

        public FakeProcesses(Recorder rec)
        {
            this.rec = rec;
            Own = Add(1000, "SteppedOut.exe", parent: 0, start: 500, windows: 0, path: @"C:\Tools\SteppedOut.exe");
        }

        public FakeProcess Own { get; }

        public int OwnId => Own.Id;

        public string? OwnPath { get; set; } = @"C:\Tools\SteppedOut.exe";

        // Extra windows, such as a Store app's frame, and the windows inside them.
        public List<AppWindow> ExtraWindows { get; } = new();

        public Dictionary<IntPtr, List<AppWindow>> Inside { get; } = new();

        public int Opened { get; private set; }

        public int Disposed { get; set; }

        public List<IntPtr> Asked
        {
            get
            {
                lock (gate) return asked.ToList();
            }
        }

        public FakeProcess Add(int id, string exe, int parent = 1, long? start = 1_000, int windows = 1, string? path = null, int session = 1)
        {
            var process = new FakeProcess { Id = id, Exe = exe, ParentId = parent, Start = start, Session = session, Path = path ?? @"C:\Apps\" + exe };
            for (int i = 0; i < windows; i++) process.Windows.Add(new IntPtr(nextWindow++));
            lock (gate) processes.Add(process);
            return process;
        }

        public FakeProcess Get(int id)
        {
            lock (gate) return processes.Single(p => p.Id == id);
        }

        FakeProcess? Find(int id)
        {
            lock (gate) return processes.FirstOrDefault(p => p.Id == id && !p.Exited);
        }

        public int Snapshots { get; private set; }

        // Runs as each process list is taken, with how many were taken so far, this one included.
        public Action<int>? OnSnapshot { get; set; }

        public IReadOnlyList<ProcessEntry> Snapshot()
        {
            int count;
            lock (gate) count = ++Snapshots;
            OnSnapshot?.Invoke(count);
            lock (gate) return processes.Where(p => !p.Exited).Select(p => new ProcessEntry(p.Id, p.ParentId, p.Exe)).ToList();
        }

        public int? SessionOf(int processId) => Find(processId)?.Session;

        public long? StartedAt(int processId) => Find(processId)?.Start;

        public IProcessHandle? Open(int processId, out int error)
        {
            var process = Find(processId);
            if (process is null)
            {
                error = 87;
                return null;
            }
            if (process.OpenError != 0)
            {
                error = process.OpenError;
                return null;
            }
            error = 0;
            lock (gate) Opened++;
            return new FakeHandle(this, process, rec);
        }

        public IReadOnlyList<AppWindow> Windows()
        {
            lock (gate)
            {
                var list = new List<AppWindow>(ExtraWindows);
                foreach (var process in processes.Where(p => !p.Exited))
                {
                    foreach (var window in process.Windows) list.Add(new AppWindow(window, process.Id, "AppWindow", process.Exe));
                }
                return list;
            }
        }

        public IReadOnlyList<AppWindow> ChildrenOf(IntPtr window)
        {
            lock (gate) return Inside.TryGetValue(window, out var children) ? children.ToList() : new List<AppWindow>();
        }

        public bool PostClose(IntPtr window)
        {
            FakeProcess? owner;
            lock (gate)
            {
                asked.Add(window);
                owner = processes.FirstOrDefault(p => p.Windows.Contains(window));
            }
            rec.Add("ask " + (owner?.Exe ?? $"window {window}"));
            if (owner is { AskFails: true }) throw new InvalidOperationException("the window is gone");
            if (owner is { ClosesWhenAsked: true } && owner.Windows.All(w => Asked.Contains(w))) owner.Exit();
            return true;
        }
    }

    public sealed class FakeHandle(FakeProcesses api, FakeProcess process, Recorder rec) : IProcessHandle
    {
        public int Id => process.Id;

        public string? ImagePath => process.Path;

        public long? StartedAt => process.Start;

        public bool HasExited => process.Exited;

        public Task WhenExited => process.WhenExited;

        public int Kill()
        {
            if (process.ExitsAtTheForce)
            {
                process.Exit();
                return 5;
            }
            rec.Add("force " + process.Exe);
            if (process.KillError != 0) return process.KillError;
            process.Exit();
            return 0;
        }

        public void Dispose()
        {
            lock (api) api.Disposed++;
        }
    }

    // Everything at once, for one test.
    public sealed class Rig : IDisposable
    {
        readonly List<string> log = new();

        public Rig()
        {
            Dir = Path.Combine(Path.GetTempPath(), "SteppedOut.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Dir);
            Sound = new FakeSound(Rec, Time);
            Media = new FakeMedia(Rec);
            Screens = new FakeScreens(Rec);
            Sleeper = new FakeSleeper(Rec);
            Starter = new FakeStarter(Rec);
            Processes = new FakeProcesses(Rec);
            // An empty file with a program's name, so Open apps finds it there; the starter is fake.
            AppToOpen = Path.Combine(Dir, "open-me.exe");
            File.WriteAllBytes(AppToOpen, []);
        }

        public string Dir { get; }

        public string AppToOpen { get; }

        public ManualTime Time { get; } = new();

        public Recorder Rec { get; } = new();

        public FakeSound Sound { get; }

        public FakeMedia Media { get; }

        public FakeScreens Screens { get; }

        public FakeSleeper Sleeper { get; }

        public FakeStarter Starter { get; }

        public FakeProcesses Processes { get; }

        public bool UnmuteThrows { get; set; }

        public DateTimeOffset? MutedAt { get; private set; }

        public int LeftoversUndone;

        public List<string> Log
        {
            get
            {
                lock (log) return log.ToList();
            }
        }

        public AwayParts Parts => new()
        {
            Time = Time,
            Log = line =>
            {
                lock (log) log.Add(line);
            },
            Sound = Sound,
            Media = Media,
            NewMute = () => new MuteSound(Path.Combine(Dir, "muted-marker"), () =>
            {
                Rec.Add("mute");
                MutedAt = Time.GetUtcNow();
                return new Wasapi.MutedOutput("{speakers}", () =>
                {
                    if (UnmuteThrows) throw new InvalidOperationException("device gone");
                    Rec.Add("unmute");
                });
            }, _ => true),
            UndoLeftoverMute = () => Interlocked.Increment(ref LeftoversUndone),
            RetryLeftoverMute = () => { },
            NewScreens = () => Screens,
            Sleeper = Sleeper,
            Starter = Starter,
            Processes = Processes,
            CloseWait = TimeSpan.FromSeconds(30),
            ListenToWindows = ListenToWindows,
        };

        // Windows' real notices, for the one test that checks they are listened to.
        public bool ListenToWindows { get; set; }

        public AwayActions Make(Config config, bool dryRun = false)
        {
            var actions = new AwayActions(dryRun, Parts);
            actions.Apply(config);
            return actions;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}

[TestClass]
public class AwayActionsTests
{
    static void Wait(Task task, string what = "done in time") => AwayFakes.Wait(task, what);

    // Every away action on, with one app to close that closes when asked.
    static Config Everything(AwayFakes.Rig rig) => new()
    {
        SoundWhenAway = ActionSound.SteppedOut,
        SoundWhenBack = ActionSound.Drift,
        MediaOnLock = MediaOnLock.PauseUntilBack,
        MuteWhenAway = true,
        ScreensOffWhenAway = true,
        CloseWhenAway = ["inkwell"],
        OpenWhenAway = [rig.AppToOpen],
        RunWhenAway = @"C:\Scripts\away.bat",
        CloseWhenBack = ["quillpad"],
        OpenWhenBack = [@"shell:AppsFolder\Example.Mail_8wekyb3d8bbwe!App"],
        RunWhenBack = @"C:\Scripts\back.bat",
    };

    // Leaves and gets the away side all the way through, the screens' wait included.
    static void LeaveAndFinish(AwayFakes.Rig rig, AwayActions actions, bool locking)
    {
        actions.Leave(locking);
        rig.Time.WaitForPending(1);
        rig.Time.Advance(locking ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(3.5));
        Wait(actions.Idle());
    }

    [TestMethod]
    public void AwayRunsSoundPauseMuteScreensCloseOpenScriptInThatOrderWhenThePcLocks()
    {
        using var rig = new AwayFakes.Rig();
        rig.Processes.Add(2000, "inkwell.exe").ClosesWhenAsked = true;
        using var actions = rig.Make(Everything(rig));

        LeaveAndFinish(rig, actions, locking: true);

        CollectionAssert.AreEqual(new[]
        {
            "sound AwaySteppedOut waited for", "pause", "mute", "screens off", "ask inkwell.exe", "start " + rig.AppToOpen, @"start C:\Scripts\away.bat",
        }, rig.Rec.Events);
    }

    [TestMethod]
    public void AwayWithoutALockPlaysNoSoundAndRunsTheRestInOrder()
    {
        using var rig = new AwayFakes.Rig();
        rig.Processes.Add(2000, "inkwell.exe").ClosesWhenAsked = true;
        using var actions = rig.Make(Everything(rig));

        LeaveAndFinish(rig, actions, locking: false);

        CollectionAssert.AreEqual(new[]
        {
            "pause", "mute", "screens off", "ask inkwell.exe", "start " + rig.AppToOpen, @"start C:\Scripts\away.bat",
        }, rig.Rec.Events);
    }

    [TestMethod]
    public void BackRunsUnmuteSoundPlayCloseOpenScriptInThatOrderAfterALock()
    {
        using var rig = new AwayFakes.Rig();
        rig.Processes.Add(2000, "inkwell.exe").ClosesWhenAsked = true;
        rig.Processes.Add(2001, "quillpad.exe").ClosesWhenAsked = true;
        using var actions = rig.Make(Everything(rig));
        LeaveAndFinish(rig, actions, locking: true);
        int before = rig.Rec.Events.Count;

        var result = actions.ComeBack();
        Wait(result);
        Wait(actions.Idle());

        CollectionAssert.AreEqual(new[]
        {
            "unmute", "sound BackDrift", "play again", "ask quillpad.exe", @"start shell:AppsFolder\Example.Mail_8wekyb3d8bbwe!App", @"start C:\Scripts\back.bat",
        }, rig.Rec.Events.Skip(before).ToList());
        CollectionAssert.AreEqual(new[] { "Player" }, result.Result.PlayedAgain.ToList());
    }

    [TestMethod]
    public void BackAfterAnAwayWithoutALockPlaysNoSound()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(new Config { SoundWhenAway = ActionSound.SteppedOut, SoundWhenBack = ActionSound.SteppedOut, MediaOnLock = MediaOnLock.KeepPlaying });

        actions.Leave(locking: false);
        Wait(actions.ComeBack());
        Wait(actions.Idle());
        Assert.IsEmpty(rig.Rec.Events, "no sound either way without a lock");

        actions.Leave(locking: true);
        Wait(actions.ComeBack());
        Wait(actions.Idle());
        CollectionAssert.AreEqual(new[] { "sound AwaySteppedOut", "sound BackSteppedOut" }, rig.Rec.Events);
    }

    [TestMethod]
    public void TheMuteStartsOnlyAfterTheAwaySoundHasEndedOnTheClock()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(new Config { SoundWhenAway = ActionSound.SteppedOut, MuteWhenAway = true, MediaOnLock = MediaOnLock.KeepPlaying });

        actions.Leave(locking: true);
        Wait(actions.Idle());

        Assert.IsNotNull(rig.Sound.WaitEnded, "the sound was waited for");
        Assert.AreEqual(AwayFakes.FakeSound.Length(Sound.Of(SoundClip.AwaySteppedOut)), rig.Sound.WaitEnded - rig.Sound.WaitStarted);
        Assert.IsNotNull(rig.MutedAt);
        Assert.IsTrue(rig.MutedAt >= rig.Sound.WaitEnded, $"muted at {rig.MutedAt:HH:mm:ss.fff}, sound ended {rig.Sound.WaitEnded:HH:mm:ss.fff}");
        CollectionAssert.AreEqual(new[] { "sound AwaySteppedOut waited for", "mute" }, rig.Rec.Events);
    }

    [TestMethod]
    public void WithoutAMuteTheAwaySoundIsNotWaitedFor()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(new Config { SoundWhenAway = ActionSound.Drift, MediaOnLock = MediaOnLock.KeepPlaying });

        actions.Leave(locking: true);
        Wait(actions.Idle());

        CollectionAssert.AreEqual(new[] { "sound AwayDrift" }, rig.Rec.Events);
        Assert.IsNull(rig.Sound.WaitStarted);
    }

    [TestMethod]
    public void ADryRunOnlyLogsWhatItWouldDo()
    {
        using var rig = new AwayFakes.Rig();
        var app = rig.Processes.Add(2000, "inkwell.exe", windows: 2);
        rig.Processes.Add(2001, "quillpad.exe");
        // Scripts that are there, since a dry run says NOT for one that is not.
        var config = Everything(rig);
        config.RunWhenAway = Path.Combine(rig.Dir, "away.bat");
        config.RunWhenBack = Path.Combine(rig.Dir, "back.bat");
        File.WriteAllText(config.RunWhenAway, "");
        File.WriteAllText(config.RunWhenBack, "");
        using var actions = rig.Make(config, dryRun: true);

        actions.Leave(locking: true);
        Wait(actions.Idle());
        Wait(actions.ComeBack());
        Wait(actions.Idle());
        rig.Time.Advance(TimeSpan.FromMinutes(5));

        Assert.IsEmpty(rig.Rec.Events, string.Join(" | ", rig.Rec.Events));
        Assert.IsEmpty(rig.Processes.Asked);
        Assert.IsFalse(app.Exited);
        Assert.AreEqual(0, rig.Time.Pending, "no wait was started");
        var would = rig.Log.Where(line => line.StartsWith("WOULD ", StringComparison.Ordinal)).ToList();
        CollectionAssert.AreEqual(new[]
        {
            "WOULD play the away sound (AwaySteppedOut) (dry run)",
            "WOULD pause what is playing (dry run)",
            "WOULD mute the sound (dry run)",
            "WOULD turn the screens off (dry run)",
            "WOULD close inkwell (pid 2000), 2 windows (dry run)",
            $"WOULD open {rig.AppToOpen} when away (dry run)",
            $"WOULD run {config.RunWhenAway} when away (dry run)",
            "WOULD play the sound for coming back (BackDrift) (dry run)",
            "WOULD close quillpad (pid 2001), 1 window (dry run)",
            @"WOULD open shell:AppsFolder\Example.Mail_8wekyb3d8bbwe!App when back (dry run)",
            $"WOULD run {config.RunWhenBack} when back (dry run)",
        }, would);
    }

    // A dry run says WOULD only for what the live run would start: a script or an app it refuses, on
    // another PC or of a kind it does not run, or a .ps1 that is not there, is said as WOULD NOT,
    // with the live run's reason.
    [TestMethod]
    public void ADryRunSaysWhatTheLiveRunWouldNotStart()
    {
        using var rig = new AwayFakes.Rig();
        string missingPs1 = Path.Combine(rig.Dir, "gone.ps1");
        using var actions = rig.Make(new Config
        {
            MediaOnLock = MediaOnLock.KeepPlaying,
            OpenWhenAway = [@"\\192.0.2.10\share\inkwell.exe", rig.AppToOpen, @"C:\Apps\away.bat"],
            RunWhenAway = @"\\192.0.2.10\share\away.bat",
            RunWhenBack = missingPs1,
        }, dryRun: true);

        actions.Leave(locking: true);
        Wait(actions.Idle());
        Wait(actions.ComeBack());
        Wait(actions.Idle());

        Assert.IsEmpty(rig.Rec.Events);
        CollectionAssert.AreEqual(new[]
        {
            $"WOULD open {rig.AppToOpen} when away (dry run)",
            @"WOULD NOT open \\192.0.2.10\share\inkwell.exe when away: it is on another PC, by a network path, and only a program on this PC or a Store app opens here (dry run)",
            @"WOULD NOT open C:\Apps\away.bat when away: only programs (.exe) and Store apps open here (dry run)",
            @"WOULD NOT run \\192.0.2.10\share\away.bat when away: it is on another PC, by a network path, and only a file on this PC runs here (dry run)",
            $"WOULD NOT run {missingPs1} when back: the file is not there (dry run)",
        }, rig.Log.Where(line => line.StartsWith("WOULD ", StringComparison.Ordinal)).ToList());
    }

    // As administrator the live run starts nothing (ElevatedStarter), and a dry run says so.
    [TestMethod]
    public void ADryRunAsAdministratorSaysNothingWouldStart()
    {
        using var rig = new AwayFakes.Rig();
        var parts = rig.Parts;
        using var actions = new AwayActions(dryRun: true, new AwayParts
        {
            Time = parts.Time, Log = parts.Log, Sound = parts.Sound, Media = parts.Media, NewMute = parts.NewMute,
            UndoLeftoverMute = parts.UndoLeftoverMute, RetryLeftoverMute = parts.RetryLeftoverMute, NewScreens = parts.NewScreens, Sleeper = parts.Sleeper,
            Starter = AwayParts.StarterFor(elevated: true), Processes = parts.Processes, CloseWait = parts.CloseWait, ListenToWindows = false,
        });
        actions.Apply(new Config { MediaOnLock = MediaOnLock.KeepPlaying, OpenWhenAway = [rig.AppToOpen], RunWhenAway = @"C:\Scripts\away.bat" });

        actions.Leave(locking: true);
        Wait(actions.Idle());

        CollectionAssert.AreEqual(new[]
        {
            $"WOULD NOT open {rig.AppToOpen} when away: {ElevatedStarter.Reason} (dry run)",
            $@"WOULD NOT run C:\Scripts\away.bat when away: {ElevatedStarter.Reason} (dry run)",
        }, rig.Log.Where(line => line.StartsWith("WOULD ", StringComparison.Ordinal)).ToList());
    }

    [TestMethod]
    public void ASecondLeaveOnlyAddsTheLockAndOnlyOnce()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(new Config { SoundWhenAway = ActionSound.SteppedOut, MediaOnLock = MediaOnLock.PauseUntilBack });

        actions.Leave(locking: false);
        actions.Leave(locking: false);
        actions.Leave(locking: true);
        actions.Leave(locking: true);
        Wait(actions.Idle());

        CollectionAssert.AreEqual(new[] { "pause", "sound AwaySteppedOut" }, rig.Rec.Events, "Lock now while away plays the away sound for the lock, once, and pauses nothing again");
        Assert.AreEqual(1, rig.Media.Paused);
    }

    // Away without a lock, then Lock now (D12): the lock gets its sound for coming back, the mute
    // and the media are left as they are, and the screens stop being watched, since the lock screen
    // turns them off by itself. The away sound is not played under the mute already in place.
    [TestMethod]
    public void LockNowWhileAwayWithoutALockGivesTheLockItsSounds()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(new Config
        {
            SoundWhenAway = ActionSound.SteppedOut,
            SoundWhenBack = ActionSound.Drift,
            MediaOnLock = MediaOnLock.PauseUntilBack,
            MuteWhenAway = true,
            ScreensOffWhenAway = true,
        });
        LeaveAndFinish(rig, actions, locking: false);
        Assert.AreEqual(1, rig.Time.Pending, "the screens are watched without a lock");

        actions.Leave(locking: true);
        Wait(actions.Idle());
        Assert.AreEqual(0, rig.Time.Pending, "the lock screen takes over the screens");
        var result = actions.ComeBack();
        Wait(result);
        Wait(actions.Idle());

        CollectionAssert.AreEqual(new[] { "pause", "mute", "screens off", "unmute", "sound BackDrift", "play again" }, rig.Rec.Events);
        Assert.AreEqual(1, rig.Media.Paused);
        CollectionAssert.AreEqual(new[] { "Player" }, result.Result.PlayedAgain.ToList());
        CollectionAssert.Contains(rig.Log, "no away sound for the lock: the sound is already muted");
    }

    [TestMethod]
    public void ADryRunLockNowWhileAwayOnlyLogsTheLocksSounds()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(new Config { SoundWhenAway = ActionSound.SteppedOut, SoundWhenBack = ActionSound.Drift, MediaOnLock = MediaOnLock.KeepPlaying }, dryRun: true);

        actions.Leave(locking: false);
        actions.Leave(locking: true);
        Wait(actions.Idle());
        Wait(actions.ComeBack());
        Wait(actions.Idle());

        Assert.IsEmpty(rig.Rec.Events);
        CollectionAssert.AreEqual(new[]
        {
            "WOULD play the away sound (AwaySteppedOut), now that the PC locks (dry run)",
            "WOULD play the sound for coming back (BackDrift) (dry run)",
        }, rig.Log.Where(line => line.StartsWith("WOULD ", StringComparison.Ordinal)).ToList());
    }

    // The same app to close and to open on one side, to start it afresh: it is opened only once its
    // close is over, or a program that allows one copy hands the start to the copy that is closing.

    [TestMethod]
    public void AnAppClosedAndOpenedWhenBackOpensOnlyAfterItsCloseIsOver()
    {
        using var rig = new AwayFakes.Rig();
        rig.Processes.Add(2001, "open-me.exe");
        using var actions = rig.Make(new Config { MediaOnLock = MediaOnLock.KeepPlaying, CloseWhenBack = ["open-me"], OpenWhenBack = [rig.AppToOpen] });
        actions.Leave(locking: true);
        Wait(actions.ComeBack());
        Wait(actions.Idle());
        rig.Time.WaitForPending(1);

        CollectionAssert.AreEqual(new[] { "ask open-me.exe" }, rig.Rec.Events, "not opened while the old one is still closing");
        rig.Time.Advance(TimeSpan.FromSeconds(30));
        Wait(actions.Settled());
        Wait(actions.Idle());

        CollectionAssert.AreEqual(new[] { "ask open-me.exe", "force open-me.exe", "start " + rig.AppToOpen }, rig.Rec.Events);
    }

    [TestMethod]
    public void AnAppClosedAndOpenedWhenBackIsNotOpenedWhenYouLeaveFirst()
    {
        using var rig = new AwayFakes.Rig();
        var app = rig.Processes.Add(2001, "open-me.exe");
        using var actions = rig.Make(new Config { MediaOnLock = MediaOnLock.KeepPlaying, CloseWhenBack = ["open-me"], OpenWhenBack = [rig.AppToOpen] });
        actions.Leave(locking: true);
        Wait(actions.ComeBack());
        Wait(actions.Idle());
        rig.Time.WaitForPending(1);

        actions.Leave(locking: true);
        Wait(actions.Settled());
        Wait(actions.Idle());
        rig.Time.Advance(TimeSpan.FromMinutes(1));
        Wait(actions.Idle());

        Assert.IsFalse(app.Exited);
        Assert.IsEmpty(rig.Starter.Started);
        CollectionAssert.AreEqual(new[] { "ask open-me.exe" }, rig.Rec.Events);
    }

    [TestMethod]
    public void AnAppClosedAndOpenedWhenAwayOpensOnceItHasClosedAndTheOthersAtOnce()
    {
        using var rig = new AwayFakes.Rig();
        var app = rig.Processes.Add(2000, "open-me.exe");
        string other = Path.Combine(rig.Dir, "other.exe");
        File.WriteAllBytes(other, []);
        using var actions = rig.Make(new Config
        {
            MediaOnLock = MediaOnLock.KeepPlaying,
            CloseWhenAway = ["open-me"],
            OpenWhenAway = [rig.AppToOpen, other],
            RunWhenAway = @"C:\Scripts\away.bat",
        });
        actions.Leave(locking: true);
        Wait(actions.Idle());
        rig.Time.WaitForPending(1);
        CollectionAssert.AreEqual(new[] { "ask open-me.exe", "start " + other, @"start C:\Scripts\away.bat" }, rig.Rec.Events);

        app.Exit();
        Wait(actions.Settled());
        Wait(actions.Idle());

        CollectionAssert.AreEqual(new[] { "ask open-me.exe", "start " + other, @"start C:\Scripts\away.bat", "start " + rig.AppToOpen }, rig.Rec.Events);
    }

    [TestMethod]
    public void AnAppClosedAndOpenedWhenAwayIsNotOpenedWhenYouAreBackFirst()
    {
        using var rig = new AwayFakes.Rig();
        var app = rig.Processes.Add(2000, "open-me.exe");
        using var actions = rig.Make(new Config { MediaOnLock = MediaOnLock.KeepPlaying, CloseWhenAway = ["open-me"], OpenWhenAway = [rig.AppToOpen] });
        actions.Leave(locking: true);
        Wait(actions.Idle());
        rig.Time.WaitForPending(1);

        Wait(actions.ComeBack());
        Wait(actions.Settled());
        Wait(actions.Idle());
        app.Exit();
        Wait(actions.Idle());

        Assert.IsEmpty(rig.Starter.Started);
        CollectionAssert.AreEqual(new[] { "ask open-me.exe" }, rig.Rec.Events);
    }

    [TestMethod]
    public void OnlyProgramsOnTheCloseListWaitAndAStoreAppNeverDoes()
    {
        using var rig = new AwayFakes.Rig();
        var close = new CloseApps([@"C:\Old\Open-Me.EXE", "quillpad"], whenAway: false, rig.Processes, rig.Time, TimeSpan.FromSeconds(30), _ => { });
        Assert.IsTrue(close.Closes(@"D:\New\open-me.exe"), "matched by program name, whatever the folder or case");
        Assert.IsTrue(close.Closes("quillpad.exe"));
        Assert.IsFalse(close.Closes(@"C:\Apps\inkwell.exe"));

        const string store = @"shell:AppsFolder\Example.Quillpad_8wekyb3d8bbwe!App";
        var open = new OpenApps([@"D:\New\open-me.exe", store, @"C:\Apps\inkwell.exe"], whenAway: false, rig.Starter, _ => { });
        var (now, onceClosed) = open.Split(close.Closes);
        Assert.AreEqual($@"open {store}, C:\Apps\inkwell.exe when back", now.Name);
        Assert.AreEqual(@"open D:\New\open-me.exe when back", onceClosed.Name);

        var (all, none) = open.Split(_ => false);
        Assert.AreEqual(open.Name, all.Name);
        Assert.IsFalse(none.Any);
        var (storeOnly, programs) = open.Split(_ => true);
        Assert.AreEqual($"open {store} when back", storeOnly.Name, "a Store app's ID names no program to wait for");
        Assert.AreEqual(@"open D:\New\open-me.exe, C:\Apps\inkwell.exe when back", programs.Name);
    }

    [TestMethod]
    public void AnAppNotRunningIsOpenedAtOnceInItsPlace()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(new Config
        {
            MediaOnLock = MediaOnLock.KeepPlaying,
            CloseWhenAway = ["open-me"],
            OpenWhenAway = [rig.AppToOpen],
            RunWhenAway = @"C:\Scripts\away.bat",
        });

        actions.Leave(locking: true);
        Wait(actions.Idle());

        CollectionAssert.AreEqual(new[] { "start " + rig.AppToOpen, @"start C:\Scripts\away.bat" }, rig.Rec.Events, "nothing to wait for keeps the usual order");
    }

    [TestMethod]
    public void ComingBackWithoutHavingLeftDoesNothing()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(Everything(rig));

        var result = actions.ComeBack();

        Assert.IsTrue(result.IsCompleted);
        Assert.AreSame(ReturnResult.Nothing, result.Result);
        Wait(actions.Idle());
        Assert.IsEmpty(rig.Rec.Events);
    }

    [TestMethod]
    public void LeaveReturnLeaveInQuickSuccessionClosesOnlyForTheLastAway()
    {
        using var rig = new AwayFakes.Rig();
        var app = rig.Processes.Add(2000, "inkwell.exe");
        rig.Processes.Add(2001, "quillpad.exe");
        using var actions = rig.Make(new Config { MediaOnLock = MediaOnLock.KeepPlaying, CloseWhenAway = ["inkwell"], CloseWhenBack = ["quillpad"] });
        using var hold = new ManualResetEventSlim();
        Wait(actions.Idle());
        actions.Enqueue(() => hold.Wait());

        actions.Leave(locking: true);
        var first = actions.ComeBack();
        actions.Leave(locking: true);
        hold.Set();
        Wait(first);
        Wait(actions.Idle());

        CollectionAssert.AreEqual(new[] { "ask inkwell.exe" }, rig.Rec.Events, "the first away was over before it began, and the return's close was called off by the second leave");
        rig.Time.WaitForPending(1);
        rig.Time.Advance(TimeSpan.FromSeconds(30));
        Wait(actions.Settled());
        CollectionAssert.AreEqual(new[] { "ask inkwell.exe", "force inkwell.exe" }, rig.Rec.Events);
        Assert.IsTrue(app.Exited);
    }

    [TestMethod]
    public void ChangingTheSettingsWhileAwayStillUndoesWhatLeavingDid()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(new Config { MuteWhenAway = true, MediaOnLock = MediaOnLock.PauseUntilBack });
        actions.Leave(locking: true);
        Wait(actions.Idle());

        actions.Apply(new Config { MediaOnLock = MediaOnLock.KeepPlaying, RunWhenBack = @"C:\Scripts\back.bat" });
        var result = actions.ComeBack();
        Wait(result);
        Wait(actions.Idle());

        CollectionAssert.AreEqual(new[] { "pause", "mute", "unmute", "play again", @"start C:\Scripts\back.bat" }, rig.Rec.Events);
        Assert.IsTrue(result.Result.AnyPlayedAgain);
    }

    [TestMethod]
    public void OneFailingActionDoesNotStopTheOthers()
    {
        using var rig = new AwayFakes.Rig();
        rig.Media.Throws = true;
        rig.UnmuteThrows = true;
        using var actions = rig.Make(new Config { MediaOnLock = MediaOnLock.PauseUntilBack, MuteWhenAway = true, RunWhenAway = @"C:\Scripts\away.bat", RunWhenBack = @"C:\Scripts\back.bat" });

        actions.Leave(locking: true);
        Wait(actions.Idle());
        Wait(actions.ComeBack());
        Wait(actions.Idle());

        CollectionAssert.AreEqual(new[] { "pause", "mute", @"start C:\Scripts\away.bat", @"start C:\Scripts\back.bat" }, rig.Rec.Events);
        Assert.IsTrue(rig.Log.Any(line => line.StartsWith("could not pause what is playing", StringComparison.Ordinal)), string.Join(" | ", rig.Log));
    }

    [TestMethod]
    public void AReturnQueuedBehindAWaitingCloseUnmutesAtOnce()
    {
        using var rig = new AwayFakes.Rig();
        var app = rig.Processes.Add(2000, "inkwell.exe");
        using var actions = rig.Make(new Config { MuteWhenAway = true, MediaOnLock = MediaOnLock.KeepPlaying, CloseWhenAway = ["inkwell"] });
        actions.Leave(locking: true);
        Wait(actions.Idle());
        rig.Time.WaitForPending(1);
        var waitedFrom = rig.Time.GetUtcNow();

        var result = actions.ComeBack();

        Wait(result, "back without the clock moving");
        Wait(actions.Idle());
        Assert.AreEqual(waitedFrom, rig.Time.GetUtcNow());
        CollectionAssert.AreEqual(new[] { "mute", "ask inkwell.exe", "unmute" }, rig.Rec.Events);
        Wait(actions.Settled(), "the force was called off");
        Assert.IsFalse(app.Exited);
    }

    [TestMethod]
    public void ComingBackDuringTheScreensWaitClosesAndOpensNothing()
    {
        using var rig = new AwayFakes.Rig();
        rig.Processes.Add(2000, "inkwell.exe");
        using var actions = rig.Make(new Config
        {
            MediaOnLock = MediaOnLock.KeepPlaying,
            ScreensOffWhenAway = true,
            CloseWhenAway = ["inkwell"],
            OpenWhenAway = [rig.AppToOpen],
        });
        actions.Leave(locking: true);
        rig.Time.WaitForPending(1);

        Wait(actions.ComeBack());
        Wait(actions.Idle());
        rig.Time.Advance(TimeSpan.FromMinutes(1));

        Assert.IsEmpty(rig.Processes.Asked, "no WM_CLOSE");
        Assert.IsEmpty(rig.Starter.Started);
        Assert.IsEmpty(rig.Rec.Events, string.Join(" | ", rig.Rec.Events));
        CollectionAssert.IsSubsetOf(new[] { "screens left on: already back", "already back, so no app is closed", "already back, so no app is opened" }, rig.Log);
    }

    [TestMethod]
    public void AnAwayCloseAsksFirstAndForcesOnlyAfterThirtySeconds()
    {
        using var rig = new AwayFakes.Rig();
        var app = rig.Processes.Add(2000, "inkwell.exe", windows: 2);
        using var actions = rig.Make(new Config { MediaOnLock = MediaOnLock.KeepPlaying, CloseWhenAway = ["inkwell"] });
        actions.Leave(locking: true);
        Wait(actions.Idle());
        rig.Time.WaitForPending(1);

        rig.Time.Advance(TimeSpan.FromSeconds(29));
        Assert.IsFalse(app.Exited, "not yet");
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Wait(actions.Settled());

        CollectionAssert.AreEqual(new[] { "ask inkwell.exe", "ask inkwell.exe", "force inkwell.exe" }, rig.Rec.Events);
        Assert.IsTrue(app.Exited);
    }

    [TestMethod]
    public void ComingBackWithinThirtySecondsCallsOffTheForce()
    {
        using var rig = new AwayFakes.Rig();
        var app = rig.Processes.Add(2000, "inkwell.exe");
        using var actions = rig.Make(new Config { MediaOnLock = MediaOnLock.KeepPlaying, CloseWhenAway = ["inkwell"] });
        actions.Leave(locking: true);
        Wait(actions.Idle());
        rig.Time.WaitForPending(1);
        rig.Time.Advance(TimeSpan.FromSeconds(20));

        Wait(actions.ComeBack());
        Wait(actions.Settled());
        rig.Time.Advance(TimeSpan.FromMinutes(5));

        Assert.IsFalse(app.Exited);
        CollectionAssert.AreEqual(new[] { "ask inkwell.exe" }, rig.Rec.Events);
        CollectionAssert.Contains(rig.Log, "you are back, so inkwell (pid 2000) stays open");
    }

    [TestMethod]
    public void ABackSideCloseReallyWaitsThirtySecondsThenForcesWhateverReturnSays()
    {
        using var rig = new AwayFakes.Rig();
        var app = rig.Processes.Add(2001, "quillpad.exe");
        using var actions = rig.Make(new Config { MediaOnLock = MediaOnLock.KeepPlaying, CloseWhenBack = ["quillpad"] });
        actions.Leave(locking: true);
        Wait(actions.ComeBack());
        Wait(actions.Idle());
        rig.Time.WaitForPending(1);

        // A ComeBack with nothing to come back from changes nothing.
        _ = actions.ComeBack();
        rig.Time.Advance(TimeSpan.FromSeconds(29));
        Assert.IsFalse(app.Exited);
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Wait(actions.Settled());

        Assert.IsTrue(app.Exited);
        CollectionAssert.AreEqual(new[] { "ask quillpad.exe", "force quillpad.exe" }, rig.Rec.Events);
    }

    // The same program on both lists: each side asks it, waits 30 s and forces it if it is still
    // running, and neither side asks or forces the program it started (a document opened from it).
    [TestMethod]
    public void AProgramOnBothListsIsClosedTheSameWayOnEachSideAndWhatItStartedIsLeftAlone()
    {
        using var rig = new AwayFakes.Rig();
        var first = rig.Processes.Add(2000, "inkwell.exe", start: 1_000, windows: 1);
        var document = rig.Processes.Add(2100, "wordpro.exe", parent: 2000, start: 1_500, windows: 1);
        using var actions = rig.Make(new Config { MediaOnLock = MediaOnLock.KeepPlaying, CloseWhenAway = ["inkwell"], CloseWhenBack = ["inkwell"] });

        actions.Leave(locking: true);
        Wait(actions.Idle());
        rig.Time.WaitForPending(1);
        rig.Time.Advance(TimeSpan.FromSeconds(29));
        Assert.IsFalse(first.Exited, "the away side waits 30 s");
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Wait(actions.Settled());
        Assert.IsTrue(first.Exited, "and then forces it");

        // Started again while you were away, with a document of its own.
        var second = rig.Processes.Add(2001, "inkwell.exe", start: 2_000, windows: 1);
        var another = rig.Processes.Add(2101, "wordpro.exe", parent: 2001, start: 2_500, windows: 1);
        Wait(actions.ComeBack());
        Wait(actions.Idle());
        rig.Time.WaitForPending(1);
        rig.Time.Advance(TimeSpan.FromSeconds(29));
        Assert.IsFalse(second.Exited, "the back side waits as long");
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Wait(actions.Settled());

        Assert.IsTrue(second.Exited, "and forces it the same way");
        CollectionAssert.AreEqual(new[] { "ask inkwell.exe", "force inkwell.exe", "ask inkwell.exe", "force inkwell.exe" }, rig.Rec.Events);
        Assert.IsFalse(document.Exited || another.Exited, "neither side forced what it started");
        Assert.IsFalse(rig.Processes.Asked.Contains(document.Windows[0]) || rig.Processes.Asked.Contains(another.Windows[0]), "nor asked it");
    }

    [TestMethod]
    public void TheNextLeaveCallsOffABackSideForce()
    {
        using var rig = new AwayFakes.Rig();
        var app = rig.Processes.Add(2001, "quillpad.exe");
        using var actions = rig.Make(new Config { MediaOnLock = MediaOnLock.KeepPlaying, CloseWhenBack = ["quillpad"] });
        actions.Leave(locking: true);
        Wait(actions.ComeBack());
        Wait(actions.Idle());
        rig.Time.WaitForPending(1);

        actions.Leave(locking: true);
        Wait(actions.Settled());
        rig.Time.Advance(TimeSpan.FromMinutes(1));

        Assert.IsFalse(app.Exited);
        CollectionAssert.AreEqual(new[] { "ask quillpad.exe" }, rig.Rec.Events);
    }

    [TestMethod]
    public void SigningOutCallsOffABackSideForceWithinTwoSeconds()
    {
        using var rig = new AwayFakes.Rig();
        var app = rig.Processes.Add(2001, "quillpad.exe");
        using var actions = rig.Make(new Config { MediaOnLock = MediaOnLock.KeepPlaying, CloseWhenBack = ["quillpad"] });
        actions.Leave(locking: true);
        Wait(actions.ComeBack());
        Wait(actions.Idle());
        rig.Time.WaitForPending(1);

        var clock = Stopwatch.StartNew();
        actions.SessionEnding();
        clock.Stop();
        Wait(actions.Settled());
        rig.Time.Advance(TimeSpan.FromMinutes(1));

        Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(2), $"took {clock.Elapsed}");
        Assert.IsFalse(app.Exited);
        CollectionAssert.AreEqual(new[] { "ask quillpad.exe" }, rig.Rec.Events);
    }

    [TestMethod]
    public void SigningOutWhileAwayUndoesTheMuteWithinTwoSecondsAndForcesNothing()
    {
        using var rig = new AwayFakes.Rig();
        var app = rig.Processes.Add(2000, "inkwell.exe");
        using var actions = rig.Make(new Config { MuteWhenAway = true, MediaOnLock = MediaOnLock.KeepPlaying, CloseWhenAway = ["inkwell"] });
        actions.Leave(locking: true);
        Wait(actions.Idle());
        rig.Time.WaitForPending(1);

        var clock = Stopwatch.StartNew();
        actions.SessionEnding();
        clock.Stop();
        Wait(actions.Settled());
        rig.Time.Advance(TimeSpan.FromMinutes(1));

        Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(2), $"took {clock.Elapsed}");
        Assert.IsFalse(app.Exited);
        CollectionAssert.AreEqual(new[] { "mute", "ask inkwell.exe", "unmute" }, rig.Rec.Events);
    }

    [TestMethod]
    public void AnotherSignInTakingTheScreenUndoesTheMuteAndForcesNothing()
    {
        using var rig = new AwayFakes.Rig();
        var app = rig.Processes.Add(2000, "inkwell.exe");
        using var actions = rig.Make(new Config { MuteWhenAway = true, MediaOnLock = MediaOnLock.KeepPlaying, CloseWhenAway = ["inkwell"] });
        actions.Leave(locking: true);
        Wait(actions.Idle());
        rig.Time.WaitForPending(1);

        actions.ConsoleDisconnected();
        Wait(actions.Idle());
        Wait(actions.Settled());
        rig.Time.Advance(TimeSpan.FromMinutes(1));
        Wait(actions.ComeBack());
        Wait(actions.Idle());

        Assert.IsFalse(app.Exited);
        CollectionAssert.AreEqual(new[] { "mute", "ask inkwell.exe", "unmute" }, rig.Rec.Events, "unmuted once, at the switch");
    }

    // Only another sign-in taking the screen undoes the mute; locking, unlocking and a remote
    // sign-in leave the screen with this one.
    [TestMethod]
    public void OnlyASwitchToAnotherSignInCountsAsOne()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(new Config { MuteWhenAway = true, MediaOnLock = MediaOnLock.KeepPlaying });
        actions.Leave(locking: true);
        Wait(actions.Idle());

        foreach (var reason in Enum.GetValues<SessionSwitchReason>().Where(r => r != SessionSwitchReason.ConsoleDisconnect)) actions.SessionSwitched(reason);
        Wait(actions.Idle());
        CollectionAssert.AreEqual(new[] { "mute" }, rig.Rec.Events, "still muted");

        actions.SessionSwitched(SessionSwitchReason.ConsoleDisconnect);
        Wait(actions.Idle());
        CollectionAssert.AreEqual(new[] { "mute", "unmute" }, rig.Rec.Events);
    }

    // The real app listens to Windows' sign-out and switch-user notices, and lets go of them when it
    // quits, so a closed app is never called.
    [TestMethod]
    public void WindowsNoticesAreListenedToAndLetGoOfOnDispose()
    {
        using var rig = new AwayFakes.Rig { ListenToWindows = true };
        var actions = rig.Make(new Config());
        Assert.IsTrue(actions.Listening);

        actions.Dispose();

        Assert.IsFalse(actions.Listening);
        actions.Dispose();
        Assert.IsFalse(actions.Listening, "a second Dispose does nothing");
    }

    [TestMethod]
    public void WithoutTheNoticesNothingIsListenedTo()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(new Config());
        Assert.IsFalse(actions.Listening);
    }

    [TestMethod]
    public void DisposeCallsOffABackSideForceAndEverythingAfterItIsNothing()
    {
        using var rig = new AwayFakes.Rig();
        var app = rig.Processes.Add(2001, "quillpad.exe");
        var actions = rig.Make(new Config { MediaOnLock = MediaOnLock.PauseUntilBack, CloseWhenBack = ["quillpad"] });
        actions.Leave(locking: true);
        Wait(actions.ComeBack());
        Wait(actions.Idle());
        rig.Time.WaitForPending(1);

        actions.Dispose();
        Wait(actions.Settled());
        rig.Time.Advance(TimeSpan.FromMinutes(1));
        actions.Leave(locking: true);
        var after = actions.ComeBack();
        actions.PlayReminderSound();
        actions.Dispose();

        Assert.IsFalse(app.Exited);
        Assert.IsTrue(after.IsCompleted);
        Assert.AreSame(ReturnResult.Nothing, after.Result);
        CollectionAssert.AreEqual(new[] { "pause", "play again", "ask quillpad.exe" }, rig.Rec.Events);
    }

    [TestMethod]
    public void QuittingWhileAwayGivesComeBackItsAnswer()
    {
        using var rig = new AwayFakes.Rig();
        var actions = rig.Make(new Config { MediaOnLock = MediaOnLock.PauseUntilBack });
        using var hold = new ManualResetEventSlim();
        actions.Leave(locking: true);
        Wait(actions.Idle());
        actions.Enqueue(() => hold.Wait(TimeSpan.FromSeconds(5)));
        var result = actions.ComeBack();

        actions.Dispose();
        hold.Set();

        Wait(result, "never left hanging");
    }

    [TestMethod]
    public void TheReminderSoundIsTheOneChosenUnderBreaksAndIsNotQueued()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(new Config { BreakSound = BreakSound.Off });
        using var hold = new ManualResetEventSlim();
        Wait(actions.Idle());
        actions.Enqueue(() => hold.Wait(TimeSpan.FromSeconds(5)));

        actions.PlayReminderSound();
        Assert.IsEmpty(rig.Rec.Events, "off plays nothing");

        actions.Apply(new Config { BreakSound = BreakSound.SteppedOut });
        actions.PlayReminderSound();
        CollectionAssert.AreEqual(new[] { "sound BreakSteppedOut" }, rig.Rec.Events, "played at once, while the queue is held");
        hold.Set();
    }

    [TestMethod]
    public void TheReminderSoundInADryRunIsOnlyLogged()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(new Config { BreakSound = BreakSound.SteppedOut }, dryRun: true);

        actions.PlayReminderSound();

        Assert.IsEmpty(rig.Rec.Events);
        CollectionAssert.Contains(rig.Log, "WOULD play the reminder sound (dry run)");
    }

    [TestMethod]
    public void WhileTheTestsBlockItTheAppsOwnConstructorGetsPartsThatRefuseEverything()
    {
        Assert.IsTrue(SessionLock.Blocked);
        using var actions = new AwayActions(dryRun: false);
        var parts = actions.Parts;

        Assert.IsInstanceOfType<AwayParts.Refused>(parts.Sound);
        Assert.IsInstanceOfType<AwayParts.Refused>(parts.Media);
        Assert.IsInstanceOfType<AwayParts.Refused>(parts.Starter);
        Assert.IsInstanceOfType<AwayParts.Refused>(parts.Processes);
        Assert.IsInstanceOfType<AwayParts.Refused>(parts.NewScreens());
        Assert.IsFalse(parts.ListenToWindows);
        Assert.ThrowsExactly<InvalidOperationException>(() => parts.Sound.Play(Sound.Of(SoundClip.AwaySteppedOut)));
        Assert.ThrowsExactly<InvalidOperationException>(() => parts.Processes.PostClose(new IntPtr(1)));
        Assert.ThrowsExactly<InvalidOperationException>(() => parts.Starter.Start(new ProcessStartInfo("x.exe")));
        var mute = parts.NewMute();
        Assert.ThrowsExactly<InvalidOperationException>(() => mute.Leave(locking: true));

        actions.Apply(new Config { SoundWhenAway = ActionSound.SteppedOut, MuteWhenAway = true, ScreensOffWhenAway = true, BreakSound = BreakSound.SteppedOut });
        actions.PlayReminderSound();
        actions.Leave(locking: true);
        Wait(actions.ComeBack());
    }

    [TestMethod]
    public void TheRealPlayerAndMediaRefuseWhileTheTestsBlockThem()
    {
        Assert.IsTrue(SessionLock.Blocked);
        Assert.ThrowsExactly<InvalidOperationException>(() => new WavPlayer().Play(Sound.Of(SoundClip.AwayDrift)));
        Assert.ThrowsExactly<InvalidOperationException>(() => new WavPlayer().PlayAndWait(Sound.Of(SoundClip.AwayDrift)));
        Assert.ThrowsExactly<InvalidOperationException>(() => new WindowsMedia().PausePlaying());
    }

    [TestMethod]
    public void AMuteLeftOverFromAnEarlierRunIsUndoneFirst()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(new Config());

        Wait(actions.Idle());

        Assert.AreEqual(1, rig.LeftoversUndone);
    }
}
