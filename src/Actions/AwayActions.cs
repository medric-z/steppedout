using System.Collections.Concurrent;
using Microsoft.Win32;

namespace SteppedOut;

// Everything the away actions reach outside this process. The tests give their own; every part is
// required, so a test that leaves one out does not compile.
sealed class AwayParts
{
    public required TimeProvider Time { get; init; }

    public required Action<string> Log { get; init; }

    public required ISoundPlayer Sound { get; init; }

    public required IMediaControl Media { get; init; }

    public required Func<MuteSound> NewMute { get; init; }

    // A mute an earlier run left in place, undone at start and tried again while you are here.
    public required Action UndoLeftoverMute { get; init; }

    public required Action RetryLeftoverMute { get; init; }

    // Called on the UI thread, in Apply.
    public required Func<IScreens> NewScreens { get; init; }

    // Sleep after SteppedOut's own lock, for "Lock and sleep".
    public required ISleeper Sleeper { get; init; }

    public required IStarter Starter { get; init; }

    public required IProcessApi Processes { get; init; }

    public required TimeSpan CloseWait { get; init; }

    // Signing out and another sign-in taking the screen.
    public required bool ListenToWindows { get; init; }

    public static AwayParts Real() => new()
    {
        Time = TimeProvider.System,
        Log = global::SteppedOut.Log.Write,
        Sound = new WavPlayer(),
        Media = new WindowsMedia(),
        NewMute = () => new MuteSound(),
        UndoLeftoverMute = MuteSound.UndoLeftover,
        RetryLeftoverMute = MuteSound.RetryLeftover,
        NewScreens = () => new PowerScreens(),
        Sleeper = new PowerSleep(),
        Starter = StarterFor(Elevation.Full),
        Processes = new Win32ProcessApi(),
        CloseWait = CloseApps.DefaultWait,
        ListenToWindows = true,
    };

    // A copy with an administrator's full rights starts nothing from the settings file (Elevation).
    internal static IStarter StarterFor(bool elevated) => elevated ? new ElevatedStarter() : new ProcessStarter();

    // While SessionLock.Blocked is set (the tests and the screenshot tool, on a PC someone is using),
    // every part refuses, so nothing can be heard, muted, turned off, started or closed even if one
    // of them builds the real thing by mistake.
    public static AwayParts Refusing() => new()
    {
        Time = TimeProvider.System,
        Log = global::SteppedOut.Log.Write,
        Sound = Refused.Instance,
        Media = Refused.Instance,
        NewMute = () => new MuteSound(Path.Combine(Path.GetTempPath(), "SteppedOut-blocked-" + Guid.NewGuid().ToString("N")), () => throw Refused.Error, _ => throw Refused.Error),
        UndoLeftoverMute = () => { },
        RetryLeftoverMute = () => { },
        NewScreens = () => Refused.Instance,
        Sleeper = Refused.Instance,
        Starter = Refused.Instance,
        Processes = Refused.Instance,
        CloseWait = CloseApps.DefaultWait,
        ListenToWindows = false,
    };

    internal sealed class Refused : ISoundPlayer, IMediaControl, IScreens, ISleeper, IStarter, IProcessApi
    {
        public static readonly Refused Instance = new();

        public static InvalidOperationException Error => new("Away actions are blocked in this process.");

        public void Play(Sound sound) => throw Error;

        public void PlayAndWait(Sound sound) => throw Error;

        public MediaSessions.Paused PausePlaying() => throw Error;

        public List<string> PlayAgain(MediaSessions.Paused paused) => throw Error;

        public void Release(MediaSessions.Paused paused) => throw Error;

        public bool TurnOff() => throw Error;

        public uint? LastInputTick() => throw Error;

        public uint Now => throw Error;

        public bool CanSleep => throw Error;

        public bool Sleep() => throw Error;

        public bool SessionLocked() => throw Error;

        public void Start(System.Diagnostics.ProcessStartInfo start) => throw Error;

        public int OwnId => Environment.ProcessId;

        public string? OwnPath => Environment.ProcessPath;

        public IReadOnlyList<ProcessEntry> Snapshot() => throw Error;

        public int? SessionOf(int processId) => throw Error;

        public long? StartedAt(int processId) => throw Error;

        public IProcessHandle? Open(int processId, out int error) => throw Error;

        public IReadOnlyList<AppWindow> Windows() => throw Error;

        public IReadOnlyList<AppWindow> ChildrenOf(IntPtr window) => throw Error;

        public bool PostClose(IntPtr window) => throw Error;
    }
}

// Does what you chose for when you leave and when you are back, in a fixed order, on one background
// thread, so a slow media app never holds up the tray icon. Media control is a WinRT API that wants
// the multithreaded apartment, and the thread stays alive so the paused sessions it holds stay valid
// until you are back.
//
//   Away: sound (only when the PC locks), pause media, mute, screens off, close apps, open apps, script,
//         and last the sleep, for "Lock and sleep" when the lock is SteppedOut's own.
//   Back: unmute, sound (only after a lock), play media again, close apps, open apps, script.
//
// "At the lock time" adds to SteppedOut's own lock only, not to Lock now or the shortcut:
// "Lock and turn off the screen" turns the screens off in their place in the order, as the away
// action does; "Lock and sleep" waits for the away sound, for the apps being closed and for the
// lock itself, then asks Windows to sleep.
//
// Nothing on that thread waits for long. The 30 s that closing apps gives them, and the force after
// it, run on their own, so a Return queued behind them unmutes at once, and signing out still gets its
// 2 s. Each side has its own way to be called off: coming back calls off what leaving started, and
// leaving again, signing out, another sign-in taking the screen or quitting calls off what coming back
// started.
sealed class AwayActions : IDisposable
{
    static readonly TimeSpan EndingWait = TimeSpan.FromSeconds(2);
    static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(5);

    // How long "Lock and sleep" waits for the lock to be in place, and how often it looks. The lock
    // comes within a moment; one Windows turned down is asked for again after Watcher.LockRetryAfter,
    // which this outlasts. Coming back ends the wait at once.
    internal static readonly TimeSpan SleepWaitsForLock = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan LockLookEvery = TimeSpan.FromMilliseconds(250);

    readonly BlockingCollection<Action> queue = new();
    readonly Thread thread;
    readonly bool dryRun;
    readonly AwayParts parts;
    readonly Action<string> log;
    readonly object gate = new();
    // The waits closing apps left running, for the tests to wait on.
    readonly List<Task> pending = new();
    readonly HashSet<TaskCompletionSource<ReturnResult>> returning = new();

    // Only read and changed on the queue thread.
    Plan plan = Plan.Nothing;
    // The away whose actions are in place: its away side has run and its back side not yet.
    Away? inPlace;

    // Under gate.
    Away? away;
    CancellationTokenSource? backSide;
    Sound? reminder;
    bool disposed;
    // The own sound files the log has already said can't be played, so each is said once.
    readonly HashSet<string> toldUnplayable = new(StringComparer.OrdinalIgnoreCase);

    public AwayActions(bool dryRun)
        : this(dryRun, SessionLock.Blocked ? AwayParts.Refusing() : AwayParts.Real())
    {
    }

    internal AwayActions(bool dryRun, AwayParts parts)
    {
        this.dryRun = dryRun;
        this.parts = parts;
        log = parts.Log;
        thread = new Thread(Run) { IsBackground = true, Name = "away actions" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        // First of all, a mute an earlier run left in place because the PC restarted or the tool
        // died while you were away.
        queue.Add(parts.UndoLeftoverMute);
        if (parts.ListenToWindows)
        {
            SystemEvents.SessionEnding += OnSessionEnding;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            Listening = true;
        }
    }

    internal AwayParts Parts => parts;

    // Whether Windows' sign-out and switch-user notices are listened to, for the tests.
    internal bool Listening { get; private set; }

    // One away, from Leave to Return.
    sealed class Away(bool locking, bool byItself)
    {
        // The lock is SteppedOut's own, at the lock time, so "At the lock time" applies.
        public bool ByItself { get; } = locking && byItself;

        // Whether the PC locks this time. Read and changed on the queue thread only: an away that
        // began without a lock gets one with Lock now, and its back side then follows the lock.
        public bool Locking { get; set; } = locking;

        // Under gate: a lock was asked for, when leaving or later. At most one upgrade is queued.
        public bool LockAsked { get; set; } = locking;

        // Cancelled the moment you are back, before the queue gets to the Return, so an action still
        // waiting to happen gives up.
        public CancellationTokenSource Calls { get; } = new();

        // What its away side did that the back side undoes. Set on the queue thread.
        public PauseMedia? Media { get; set; }

        public MuteSound? Mute { get; set; }

        public ScreensOff? Screens { get; set; }
    }

    // The actions the settings ask for. Taking one out is deleting its line in Build.
    sealed class Plan
    {
        public static readonly Plan Nothing = new();

        public Sound? AwaySound { get; init; }

        public PauseMedia? Media { get; init; }

        public MuteSound? Mute { get; init; }

        public ScreensOff? Screens { get; init; }

        // What comes after SteppedOut's own lock, and the screens it turns off: the away action's
        // own when that is on, so they are never turned off twice.
        public WhenLocking WhenLocking { get; init; }

        public ScreensOff? LockScreens { get; init; }

        public CloseApps? CloseAway { get; init; }

        public OpenApps? OpenAway { get; init; }

        // Apps also on the list to close when away, opened once that close is over.
        public OpenApps? OpenAwayOnceClosed { get; init; }

        public RunProgram? RunAway { get; init; }

        public Sound? BackSound { get; init; }

        public CloseApps? CloseBack { get; init; }

        public OpenApps? OpenBack { get; init; }

        public OpenApps? OpenBackOnceClosed { get; init; }

        public RunProgram? RunBack { get; init; }
    }

    // Called on the UI thread, where the screens action makes its window.
    public void Apply(Config config)
    {
        var fresh = Build(config);
        var reminderSound = SoundClips.Choose(config.BreakSound, config.BreakSoundFile ?? "", log, toldUnplayable);
        lock (gate) reminder = reminderSound;
        TryQueue(() => plan = fresh);
    }

    Plan Build(Config config)
    {
        var closeAway = new CloseApps(config.CloseWhenAway ?? [], whenAway: true, parts.Processes, parts.Time, parts.CloseWait, log);
        var (openAway, openAwayOnceClosed) = new OpenApps(config.OpenWhenAway ?? [], whenAway: true, parts.Starter, log).Split(closeAway.Closes);
        var closeBack = new CloseApps(config.CloseWhenBack ?? [], whenAway: false, parts.Processes, parts.Time, parts.CloseWait, log);
        var (openBack, openBackOnceClosed) = new OpenApps(config.OpenWhenBack ?? [], whenAway: false, parts.Starter, log).Split(closeBack.Closes);
        string runAway = config.RunWhenAway ?? "";
        string runBack = config.RunWhenBack ?? "";
        var screens = config.ScreensOffWhenAway ? new ScreensOff(parts.NewScreens(), parts.Time, log) : null;
        var whenLocking = Enum.IsDefined(config.WhenLocking) ? config.WhenLocking : WhenLocking.Lock;
        return new Plan
        {
            AwaySound = SoundClips.Choose(config.SoundWhenAway, config.SoundWhenAwayFile ?? "", away: true, log, toldUnplayable),
            Media = config.MediaOnLock == MediaOnLock.KeepPlaying ? null : new PauseMedia(parts.Media, stayPaused: config.MediaOnLock == MediaOnLock.PauseStayPaused, log),
            Mute = config.MuteWhenAway ? parts.NewMute() : null,
            Screens = screens,
            WhenLocking = whenLocking,
            LockScreens = whenLocking == WhenLocking.Lock ? null : screens ?? new ScreensOff(parts.NewScreens(), parts.Time, log),
            CloseAway = closeAway.Any ? closeAway : null,
            OpenAway = openAway.Any ? openAway : null,
            OpenAwayOnceClosed = openAwayOnceClosed.Any ? openAwayOnceClosed : null,
            RunAway = runAway.Length > 0 ? new RunProgram(runAway, whenAway: true, parts.Starter, log) : null,
            BackSound = SoundClips.Choose(config.SoundWhenBack, config.SoundWhenBackFile ?? "", away: false, log, toldUnplayable),
            CloseBack = closeBack.Any ? closeBack : null,
            OpenBack = openBack.Any ? openBack : null,
            OpenBackOnceClosed = openBackOnceClosed.Any ? openBackOnceClosed : null,
            RunBack = runBack.Length > 0 ? new RunProgram(runBack, whenAway: false, parts.Starter, log) : null,
        };
    }

    // locking: the PC locks this time, by itself, with Lock now or with the shortcut.
    public void Leave(bool locking) => Leave(locking, byItself: false);

    // byItself: that lock is SteppedOut's own, at the lock time (Watcher.WentAway), so "When it's
    // time to lock" adds the screen off or the sleep to it.
    public void Leave(bool locking, bool byItself)
    {
        Away leaving;
        CancellationTokenSource? calledOff = null;
        bool upgrade = false;
        lock (gate)
        {
            if (disposed) return;
            if (away is { } current)
            {
                // "Lock now" while already away would pause again and lose the list of what was
                // playing. After an away without a lock, it only adds what the lock brings, once.
                if (!locking || current.LockAsked) return;
                current.LockAsked = true;
                leaving = current;
                upgrade = true;
            }
            else
            {
                leaving = new Away(locking, byItself);
                away = leaving;
                calledOff = backSide;
                backSide = null;
            }
        }
        if (upgrade)
        {
            // Queued, so it comes after the away side this away has already queued.
            TryQueue(() => DoLockUpgrade(leaving));
            return;
        }
        // Leaving again calls off a force still waiting from when you came back.
        calledOff?.Cancel();
        TryQueue(() => DoAway(leaving));
    }

    // Undoes what leaving did and does what you chose for coming back. Completes, on another thread,
    // with the apps whose music or video is playing again, once that is known; never fails, and after
    // Dispose, or without an away to come back from, completes at once with nothing.
    public Task<ReturnResult> ComeBack()
    {
        Away back;
        CancellationToken calls;
        var done = new TaskCompletionSource<ReturnResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (disposed || away is null) return Task.FromResult(ReturnResult.Nothing);
            back = away;
            away = null;
            var side = new CancellationTokenSource();
            backSide = side;
            calls = side.Token;
            returning.Add(done);
        }
        back.Calls.Cancel();
        if (!TryQueue(() => DoBack(back, calls, done))) Finish(done, ReturnResult.Nothing);
        return done.Task;
    }

    // For break and eye-break reminders: the sound chosen under Breaks, started now and not waited
    // for. Not queued: the away actions' thread may be busy, and a reminder never comes while you
    // are away.
    public void PlayReminderSound()
    {
        Sound? chosen;
        lock (gate) chosen = reminder;
        if (chosen is null) return;
        if (dryRun)
        {
            log("WOULD play the reminder sound (dry run)");
            return;
        }
        Step("play the reminder sound", () => parts.Sound.Play(chosen));
    }

    // A sound picked in the settings, played once so it can be heard before it is kept. Not queued,
    // as a reminder is not. In the tests and the screenshot tool the parts refuse it, as they refuse
    // every sound, and a dry run only says it would.
    public void PreviewSound(Sound sound)
    {
        lock (gate)
            if (disposed) return;
        if (dryRun)
        {
            log($"WOULD play {sound} to hear it (dry run)");
            return;
        }
        Step("play the sound picked in the settings", () => parts.Sound.Play(sound));
    }

    void DoAway(Away leaving)
    {
        var plan = this.plan;
        var back = leaving.Calls.Token;
        inPlace = leaving;
        if (dryRun)
        {
            DryRunAway(plan, leaving);
            return;
        }
        var after = After(plan, leaving);
        bool sleeping = after == WhenLocking.LockSleep;
        if (leaving.Locking && plan.AwaySound is { } sound)
        {
            // Waited for when the sound is about to be muted, or the mute would cut it off, and
            // when the PC is about to sleep.
            bool wait = plan.Mute is not null || sleeping;
            Step("play the away sound", () =>
            {
                if (wait) parts.Sound.PlayAndWait(sound);
                else parts.Sound.Play(sound);
            });
        }
        if (plan.Media is { } media)
        {
            leaving.Media = media;
            Step(media.Name, media.Leave);
        }
        if (plan.Mute is { } mute)
        {
            leaving.Mute = mute;
            Step(mute.Name, () => mute.Leave(leaving.Locking));
        }
        // The away action's screens off, or the same for "Lock and turn off the screen".
        if ((plan.Screens ?? (after == WhenLocking.LockScreenOff ? plan.LockScreens : null)) is { } screens)
        {
            leaving.Screens = screens;
            Step(screens.Name, () => screens.Leave(leaving.Locking, back));
        }
        // Back during the wait for the screens, or before the queue got here: your apps stay as they are.
        var closing = Task.CompletedTask;
        Task? reopening = null;
        if (plan.CloseAway is { } close)
        {
            if (back.IsCancellationRequested) log("already back, so no app is closed");
            else Track(closing = close.Close(back));
        }
        if (plan.OpenAway is not null || plan.OpenAwayOnceClosed is not null)
        {
            if (back.IsCancellationRequested) log("already back, so no app is opened");
            else
            {
                if (plan.OpenAway is { } open) Step(open.Name, () => open.Open(back));
                if (plan.OpenAwayOnceClosed is { } reopen) reopening = OpenOnceClosed(closing, reopen, back);
            }
        }
        if (plan.RunAway is { } run) Step(run.Name, run.Start);
        if (sleeping) SleepOnceClosed(leaving, plan, closing, reopening, back);
    }

    // What "At the lock time" adds to this away: only to SteppedOut's own lock.
    static WhenLocking After(Plan plan, Away leaving) => leaving.ByItself ? plan.WhenLocking : WhenLocking.Lock;

    // "Lock and sleep", after everything else this away queued: once the apps being closed have
    // closed or been forced (their wait has its own end), it waits on the queue for the lock. An app
    // closed and opened again is queued to open once its close is over (reopening, done once it is
    // queued), and the sleep is queued after that, never ahead of it.
    void SleepOnceClosed(Away leaving, Plan plan, Task closing, Task? reopening, CancellationToken back)
    {
        if (reopening is null && closing.IsCompleted)
        {
            Step("put the PC to sleep", () => SleepOnceLocked(leaving, plan, back));
            return;
        }
        log("put the PC to sleep, once closing apps is over");
        Track((reopening ?? closing).ContinueWith(_ => TryQueue(() => Step("put the PC to sleep", () => SleepOnceLocked(leaving, plan, back))),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default));
    }

    // Never while you are back or the PC is unlocked: a PC that wakes from this sleep is locked. A PC
    // with no sleep an app can ask for (Modern Standby) turns its screens off instead, which is how
    // such a PC goes to standby; Windows is never asked to hibernate.
    void SleepOnceLocked(Away leaving, Plan plan, CancellationToken back)
    {
        if (back.IsCancellationRequested)
        {
            log("already back, so the PC does not sleep");
            return;
        }
        if (!WaitForLock(back))
        {
            log(back.IsCancellationRequested ? "back before the lock, so the PC does not sleep" : "the PC did not lock, so it does not sleep");
            return;
        }
        if (!parts.Sleeper.CanSleep)
        {
            if (leaving.Screens is not null)
            {
                log("this PC has no sleep an app can ask for; the screens are already off");
                return;
            }
            log("this PC has no sleep an app can ask for, so the screens are turned off instead");
            if (plan.LockScreens is { } screens)
            {
                leaving.Screens = screens;
                screens.Leave(locking: true, back);
            }
            return;
        }
        // Said before the call: Windows only returns from it once the PC has woken again.
        log("asking Windows to sleep");
        log(parts.Sleeper.Sleep() ? "back from the sleep Windows was asked for" : "Windows did not put the PC to sleep");
    }

    // True once the session is locked; false after the wait, or at once when you are back.
    bool WaitForLock(CancellationToken back)
    {
        var until = parts.Time.GetUtcNow() + SleepWaitsForLock;
        while (!back.IsCancellationRequested)
        {
            if (parts.Sleeper.SessionLocked()) return true;
            if (parts.Time.GetUtcNow() >= until) return false;
            try
            {
                Task.Delay(LockLookEvery, parts.Time, back).Wait(CancellationToken.None);
            }
            catch (AggregateException) when (back.IsCancellationRequested)
            {
                return false;
            }
        }
        return false;
    }

    // Lock now after an away without a lock: the away sound and, when you are back, the sound for
    // coming back, as for any lock (D12). Nothing is paused or muted again. The away sound is not
    // played over the mute this away already made, since it would not be heard; undoing the mute for
    // it would let through whatever else is playing. The screens stop being watched: the lock screen
    // turns them off by itself from here on.
    void DoLockUpgrade(Away current)
    {
        var plan = this.plan;
        if (current.Locking) return;
        current.Locking = true;
        if (dryRun)
        {
            if (plan.AwaySound is { } dry && plan.Mute is null) Would($"play the away sound ({dry}), now that the PC locks");
            return;
        }
        if (current.Calls.IsCancellationRequested) return;
        if (plan.AwaySound is { } sound)
        {
            if (current.Mute is not null) log("no away sound for the lock: the sound is already muted");
            else Step("play the away sound", () => parts.Sound.Play(sound));
        }
        current.Screens?.Locked();
    }

    // An app on both lists of one side, to start it afresh: opened once its close is over, by itself
    // or by force, or a program that allows one copy would hand the start to the copy that is closing.
    // The wait runs off the queue, so it never holds up what comes after; calling the side off before
    // then leaves the app unopened.
    // Gives back null when it opened at once, or else the wait that queues the open, done once the
    // open is queued.
    Task? OpenOnceClosed(Task closing, OpenApps open, CancellationToken token)
    {
        if (closing.IsCompleted)
        {
            Step(open.Name, () => open.Open(token));
            return null;
        }
        log($"{open.Name}, once closing it is over");
        var queued = closing.ContinueWith(_ => TryQueue(() => Step(open.Name, () => open.Open(token))), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        Track(queued);
        return queued;
    }

    void DoBack(Away back, CancellationToken calls, TaskCompletionSource<ReturnResult> done)
    {
        var result = ReturnResult.Nothing;
        try
        {
            var plan = this.plan;
            if (inPlace == back) inPlace = null;
            if (dryRun)
            {
                DryRunBack(plan, back);
                return;
            }
            back.Screens?.Return();
            if (back.Mute is { } mute) Step("unmute the sound", mute.Return);
            if (back.Locking && plan.BackSound is { } sound) Step("play the sound for coming back", () => parts.Sound.Play(sound));
            if (back.Media is { } media)
            {
                Step("play again what was paused", () =>
                {
                    var played = media.Return();
                    if (played.Count > 0) result = new ReturnResult(played.ToArray());
                });
            }
            Finish(done, result);
            var closing = Task.CompletedTask;
            if (plan.CloseBack is { } close) Track(closing = close.Close(calls));
            if (plan.OpenBack is { } open) Step(open.Name, () => open.Open(calls));
            if (plan.OpenBackOnceClosed is { } reopen) _ = OpenOnceClosed(closing, reopen, calls);
            if (plan.RunBack is { } run) Step(run.Name, run.Start);
        }
        finally
        {
            Finish(done, result);
        }
    }

    void DryRunAway(Plan plan, Away leaving)
    {
        var after = After(plan, leaving);
        if (leaving.Locking && plan.AwaySound is { } sound) Would($"play the away sound ({sound})");
        if (plan.Media is { } media) Would(media.Name);
        if (plan.Mute is { } mute) Would(mute.Name);
        if (plan.Screens is { } screens) Would(screens.Name);
        else if (after == WhenLocking.LockScreenOff && plan.LockScreens is { } lockScreens) Would(lockScreens.Name + " after the lock");
        // Only looks: which running apps would be asked to close.
        plan.CloseAway?.DryRun();
        if (plan.OpenAway is { } open) Would(open.DryRun());
        if (plan.OpenAwayOnceClosed is { } reopen) Would(reopen.DryRun(", once closing it is over"));
        if (plan.RunAway is { } run) Would(run.DryRun());
        if (after == WhenLocking.LockSleep) Would("put the PC to sleep once the lock is in place");
    }

    void DryRunBack(Plan plan, Away back)
    {
        if (back.Locking && plan.BackSound is { } sound) Would($"play the sound for coming back ({sound})");
        plan.CloseBack?.DryRun();
        if (plan.OpenBack is { } open) Would(open.DryRun());
        if (plan.OpenBackOnceClosed is { } reopen) Would(reopen.DryRun(", once closing it is over"));
        if (plan.RunBack is { } run) Would(run.DryRun());
    }

    void Would(string what) => log($"WOULD {what} (dry run)");

    void Would(IEnumerable<string> lines)
    {
        foreach (string line in lines) Would(line);
    }

    void Finish(TaskCompletionSource<ReturnResult> done, ReturnResult result)
    {
        done.TrySetResult(result);
        lock (gate) returning.Remove(done);
    }

    // Windows is signing out or shutting down. The mute would outlast the session, so it is undone
    // now, with a moment for the queue to get to it before Windows carries on. An action still
    // waiting to happen, such as turning the screens off or forcing an app closed, gives up so it
    // does not use that moment.
    void OnSessionEnding(object sender, SessionEndingEventArgs e) => SessionEnding();

    internal void SessionEnding()
    {
        CallOffBothSides("Windows is signing out");
        QueueEnding().Wait(EndingWait);
    }

    // Someone else signing in with Switch user takes the screen from this session. The mute is on the
    // speakers, not on this sign-in, so they would find the sound off; it is undone as at sign-out,
    // and once you are back there is nothing left to unmute. No app is forced closed meanwhile.
    void OnSessionSwitch(object sender, SessionSwitchEventArgs e) => SessionSwitched(e.Reason);

    // Locking, unlocking and a remote sign-in leave the screen with this sign-in: nothing to do.
    internal void SessionSwitched(SessionSwitchReason reason)
    {
        if (reason == SessionSwitchReason.ConsoleDisconnect) ConsoleDisconnected();
    }

    internal void ConsoleDisconnected()
    {
        CallOffBothSides("another sign-in took the screen");
        TryQueue(() =>
        {
            if (inPlace is not null) log("another sign-in took the screen, undoing what would outlast this one");
        });
        _ = QueueEnding();
    }

    void CallOffBothSides(string why)
    {
        CancellationTokenSource? awaySide, backSideNow;
        lock (gate)
        {
            awaySide = away?.Calls;
            backSideNow = backSide;
            backSide = null;
        }
        if (awaySide is null && backSideNow is null) return;
        if (Pending()) log($"{why}: apps still waiting to close are left open");
        awaySide?.Cancel();
        backSideNow?.Cancel();
    }

    Task QueueEnding()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queued = TryQueue(() =>
        {
            try
            {
                if (inPlace is { } current)
                {
                    current.Screens?.Ending();
                    if (current.Mute is { } mute) Step("unmute the sound", mute.Ending);
                }
            }
            finally
            {
                done.TrySetResult();
            }
        });
        if (!queued) done.TrySetResult();
        return done.Task;
    }

    public void Dispose()
    {
        List<TaskCompletionSource<ReturnResult>> waiting;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            waiting = returning.ToList();
        }
        if (Listening)
        {
            SystemEvents.SessionEnding -= OnSessionEnding;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            Listening = false;
        }
        CallOffBothSides("quitting");
        // Quitting while away undoes the mute as well. That and whatever is already queued, a
        // Return included, get a moment to finish before the process takes the thread with it.
        _ = QueueEnding();
        queue.CompleteAdding();
        thread.Join(EndingWait);
        foreach (var done in waiting) done.TrySetResult(ReturnResult.Nothing);
    }

    void Run()
    {
        // The thread is already in the multithreaded apartment; this makes it explicit for WinRT.
        MediaSessions.RoInitialize(MediaSessions.RoInitMultithreaded);
        while (!queue.IsCompleted)
        {
            if (queue.TryTake(out var work, RetryEvery))
            {
                Step("finish an away action", work);
            }
            // Nothing to do for a few seconds and you are here: headphones or a screen that would
            // not unmute when you came back may be on again. While the away actions are in place,
            // the mute they made has to stay.
            else if (inPlace is null && !queue.IsAddingCompleted)
            {
                Step("unmute the sound left muted earlier", parts.RetryLeftoverMute);
            }
        }
    }

    bool TryQueue(Action work)
    {
        try
        {
            queue.Add(work);
            return true;
        }
        catch (InvalidOperationException)
        {
            // Disposed: the queue takes nothing more.
            return false;
        }
    }

    void Track(Task wait)
    {
        if (wait.IsCompleted) return;
        lock (gate)
        {
            pending.RemoveAll(task => task.IsCompleted);
            pending.Add(wait);
        }
    }

    bool Pending()
    {
        lock (gate) return pending.Any(task => !task.IsCompleted);
    }

    // For the tests: completes once every wait closing apps started so far is over.
    internal Task Settled()
    {
        lock (gate) return Task.WhenAll(pending.ToArray());
    }

    // For the tests: completes once the queue has done everything added before it.
    internal Task Idle()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryQueue(() => done.TrySetResult())) done.TrySetResult();
        return done.Task;
    }

    // For the tests: holds the queue until the test lets it go.
    internal bool Enqueue(Action work) => TryQueue(work);

    void Step(string name, Action step)
    {
        try
        {
            step();
        }
        catch (Exception e)
        {
            log($"could not {name}: {e.Message}");
        }
    }
}
