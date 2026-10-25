using Microsoft.Win32;

namespace SteppedOut;

// What the watcher reads from Windows and the clocks. The tests give their own, so nothing they do
// locks the session, reads the real idle time or writes today.json.
sealed record WatchSeams
{
    // Null: the input tracker reads Windows' last-input time and the cursor.
    public Func<InputSample>? Sample { get; init; }

    public Func<TimeSpan> Monotonic { get; init; } = () => Clock.Now;

    // The clock time with its offset from UTC, so today's away time is the time that passed even
    // across a daylight saving change.
    public Func<DateTimeOffset> Wall { get; init; } = () => DateTimeOffset.Now;

    // False when Windows turned the request down.
    public Func<bool> RequestLock { get; init; } = SessionLock.TryRequest;

    public Func<bool> AcceptsNotifications { get; init; } = Native.AcceptsNotifications;

    public Func<bool> SessionIsLocked { get; init; } = SessionLock.IsLocked;

    public Sources Sources { get; init; } = Sources.Real;

    // Where today's counts are kept from Start on; null keeps them in memory only.
    public string? StatsPath { get; init; } = DailyStats.DefaultPath;

    // Where the tick's own failures are written: the log, unless a test listens.
    public Action<string> ErrorLog { get; init; } = Log.Write;

    public static WatchSeams Real { get; } = new();
}

sealed class Watcher : IDisposable
{
    // How long after stepping out input does not count as coming back.
    internal static readonly TimeSpan HandLeaving = TimeSpan.FromSeconds(3);
    // How long a break note waits after a countdown is cancelled, so it does not come up in the
    // moment the countdown note goes, as if it were the same note.
    internal static readonly TimeSpan BreakNoteAfterCountdown = TimeSpan.FromSeconds(1.5);
    // How old the last look may be when the Why window asks for it.
    static readonly TimeSpan ExplainAge = TimeSpan.FromSeconds(1.5);
    // How long after a lock request Windows turned down it is asked for once more.
    internal static readonly TimeSpan LockRetryAfter = TimeSpan.FromSeconds(5);
    // A gap between ticks this long means no tick ran because the PC slept or hibernated (D47). Ticks
    // come a second apart, a busy PC stretches that to a few seconds, and Windows calls a window that
    // has not answered for 5 s Not responding. Six times that is no hold-up a working PC has, so one
    // never starts the lock time over; and a sleep shorter than this that Windows does not report
    // (one with Modern Standby may not) moves the lock at most this much sooner.
    internal static readonly TimeSpan SleptAfter = TimeSpan.FromSeconds(30);
    // How long after Windows says the PC is going to sleep this app may still be running. With
    // Modern Standby the screen goes off and the session locks as the sleep begins, and desktop apps
    // may run on for a while before Windows stops them; a notice older than this was for a sleep
    // that did not come.
    internal static readonly TimeSpan StandbyLag = TimeSpan.FromMinutes(10);
    // Windows fires a window timer on its 15.6 ms timer grid, rounding the interval up to a whole
    // number of steps: 100 ms is really 109.4 ms. 125 ms is exactly 8 steps, and 1000 ms exactly 64.
    // While the session is locked the cursor is hidden and nothing is asked, so the timer only
    // keeps the tick going.
    internal const int SampleMilliseconds = 125;
    internal const int LockedMilliseconds = 1000;
    // A timer event this much before the tick is due counts as on time, so a one-second timer that
    // fires a hair early does not put the tick off to the event after.
    const double TickEarly = 0.06;
    // Ticks that fail this many times, without this many good ones in a row between them, mean the
    // locking may have stopped (TicksFailing); this many good ones in a row mean it works again.
    internal const int FailingAfter = 10;

    readonly System.Windows.Forms.Timer timer = new() { Interval = SampleMilliseconds };
    readonly InputTracker tracker = new();
    readonly WatchSeams seams;
    readonly LockEngine engine = new();
    readonly RuleSet rules;
    readonly Scans scans;
    readonly Evaluator evaluator;
    readonly Reminders reminders = new();
    readonly bool dryRun;
    DailyStats stats;
    decimal normalMinutes;
    bool lockWhenAway;
    double closeFrom;
    int snoozeMinutes;
    bool sessionLocked;
    bool subscribed;
    // When the next tick is due, in seconds on the Now clock; null before the first.
    double? nextTickAt;
    // Ticks that threw since FailingAfter worked in a row, the ones that worked since the last that
    // threw, and how the log hears of them.
    int failedTicks;
    int goodTicks;
    readonly ErrorRepeats tickErrors;
    int ticksSinceAll = PollSchedule.FarTicks;
    int liveViews;
    // These are seconds on the Now clock. A pause until turned back on ends at infinity. The Now
    // clock keeps counting while the PC sleeps, so a pause of a day or a week ends when it said it
    // would, slept through or not, and a pause that ended during sleep is over on waking. A pause
    // is only held here: quitting SteppedOut or restarting the PC ends it.
    double? pausedUntil;
    double? awaySince;
    // The away since awaySince asked for a lock, at the start or later with Lock now.
    bool awayLocks;
    // When a lock request Windows turned down is asked for once more; null when none is waiting.
    double? lockRetryAt;
    // Windows turned down the lock for this away twice, so nothing is asking for it any more.
    bool lockGivenUp;
    double? lastTickAt;
    // When the PC last woke from sleep, until there is input after it: the idle time counts from
    // then, since Windows' last input time does not move on waking (D47).
    double? wokeAt;
    // When the session locked, while it is locked.
    double? lockedAt;
    // When Windows last said the PC is going to sleep, until the wake or an unlock.
    double? sleepNoticeAt;
    double holdComeBackUntil;
    double holdBreakNoteUntil;
    double lastIdleSeconds;
    double lastHeartbeat = double.NegativeInfinity;
    Evaluation last;
    LockTime? loggedTime;

    public Watcher(Config config, bool dryRun)
        : this(config, dryRun, WatchSeams.Real)
    {
    }

    internal Watcher(Config config, bool dryRun, WatchSeams seams)
    {
        this.seams = seams;
        this.dryRun = dryRun;
        rules = new RuleSet(config);
        scans = new Scans(seams.Sources);
        evaluator = new Evaluator(() => rules.Active, scans);
        // In memory until Start, so a watcher that is made and never started writes no today.json.
        stats = DailyStats.Load(null, seams.Wall());
        last = Evaluation.Nothing(Mono);
        tickErrors = new ErrorRepeats("tick failed", seams.ErrorLog, () => Mono);
        Use(config);
        LockStatus = LockStatus.Starting(normalMinutes) with { DryRun = dryRun };
        timer.Tick += (_, _) => TimerFired();
    }

    public bool IsPaused => pausedUntil is double until && Now < until;

    // The status, the lock time and what makes it up, today's counts: updated every second.
    public LockStatus LockStatus { get; private set; }

    public event Action<int>? CountdownStarted;
    public event Action<int>? CountdownTick;
    public event Action? CountdownEnded;

    // Raised when this tool decides you have left, just before it locks, and when you are back from
    // that. The first value is true when it will lock; the second when that lock is its own, at the
    // lock time, rather than Lock now or the shortcut: only that lock gets "At the lock time".
    public event Action<bool, bool>? WentAway;
    public event Action<TimeSpan>? CameBack;

    // A break or an eye break reminder is due.
    public event Action<Reminder>? ReminderDue;

    // True when the tick has thrown FailingAfter times, without FailingAfter good ticks in a row
    // between them, so the locking may have stopped; false when the tick has worked FailingAfter
    // times in a row after that. Once each way.
    public event Action<bool>? TicksFailing;

    // After every tick and every Restate: LockStatus may have changed. On the UI thread.
    internal event Action? Updated;

    // The last look the rules took, for the tests.
    internal Evaluation LastLook => last;

    // How often the timer fires now, for the tests.
    internal int TimerInterval => timer.Interval;

    // How many KeepLive leases are held now, for the tests.
    internal int LiveViews => liveViews;

    public void Start()
    {
        stats = DailyStats.Load(seams.StatsPath, seams.Wall());
        // Subscribed here, after the tray's windows exist, not in the constructor: SystemEvents only
        // calls back on the UI thread if that thread had a window when it subscribed. Before that,
        // the handler runs on SystemEvents' own thread, and a note shown from there never hides.
        sessionLocked = seams.SessionIsLocked();
        if (sessionLocked) Log.Write("started while the session is locked");
        SetTimer();
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        subscribed = true;
        timer.Start();
    }

    // New settings. The rules are asked again under them at once, so the status the tray asks for
    // right after (Restate) and the tooltip show them, not the old ones until the next tick. Nothing
    // is asked while the session is locked; the tick after the unlock asks every rule.
    public void Apply(Config config)
    {
        Use(config);
        if (sessionLocked) return;
        last = evaluator.Evaluate(Mono, all: true, fresh: false);
        ticksSinceAll = 0;
    }

    void Use(Config config)
    {
        normalMinutes = LockTime.NormalMinutes(config.IdleMinutes);
        engine.WarningSeconds = Math.Max(0, config.GraceSeconds);
        tracker.JitterPixels = config.JitterFilter ? config.JitterPixels : 0;
        lockWhenAway = config.LockWhenAway;
        reminders.Apply(config.BreakReminders, config.BreakMinutes, config.EyeBreaks);
        snoozeMinutes = config.SnoozeMinutes;
        rules.Apply(config);
        evaluator.Reset();
        closeFrom = PollSchedule.CloseFrom(config);
        // Every rule is asked again on the next tick, under the new settings.
        ticksSinceAll = PollSchedule.FarTicks;
    }

    public void Pause(TimeSpan? duration)
    {
        pausedUntil = duration is TimeSpan d ? Now + d.TotalSeconds : double.PositiveInfinity;
        Log.Write(PauseText(duration));
        Restate();
    }

    internal static string PauseText(TimeSpan? duration) =>
        duration is TimeSpan span ? $"paused for {span.TotalMinutes:0} min" : "paused until resumed";

    public void Resume()
    {
        pausedUntil = null;
        Log.Write("resumed");
        Restate();
    }

    // From the tray menu: always locks, even with locking turned off for the automatic case,
    // because that is exactly what was asked for. Asked again while already locking, by a second
    // click or the shortcut right after, it changes nothing. After stepping out without a lock, it
    // adds the lock to that same away. After Windows turned the lock down twice, nothing is locking
    // any more, so it asks Windows again, with one more try after that, as at first.
    public void LockNow()
    {
        if (!sessionLocked && awaySince is not null && awayLocks && lockGivenUp)
        {
            lockGivenUp = false;
            tracker.IgnoreInputSoFar();
            holdComeBackUntil = Now + HandLeaving.TotalSeconds;
            Lock("requested from the menu again, after Windows turned the lock down");
            Restate();
            return;
        }
        if (sessionLocked || (awaySince is not null && awayLocks))
        {
            Log.Write("lock now: " + (sessionLocked ? "the session is already locked" : "already locking"));
            return;
        }
        CountdownEnded?.Invoke();
        engine.GoAway();
        // The click that asked for this is not someone coming back.
        tracker.IgnoreInputSoFar();
        StepOut("requested from the menu", Now, forceLock: true);
        Restate();
    }

    // The status again, at once, after a pause, a turn back on or a lock now, so the tray and the
    // status box never show the state from before the click for a second. The tray also calls it
    // after new settings.
    internal void Restate()
    {
        LockStatus = Describe(TimeNow(), lastIdleSeconds, seams.Wall());
        Updated?.Invoke();
    }

    // Saves the day's counts now, as quitting does, for Windows signing out, which can end the
    // process before it quits.
    internal void SaveStats() => stats.Quit(seams.Wall());

    // Today's counts are no longer saved, not even as the watcher ends, for "Remove SteppedOut's
    // files", which deletes them next. They go on in memory until then.
    internal void ForgetToday() => stats = DailyStats.Load(null, seams.Wall());

    // From the Snooze button on a reminder: it comes back after the snooze time in the settings.
    public void Snooze(ReminderKind kind)
    {
        reminders.Snooze(kind, Now, snoozeMinutes);
        Log.Write($"{(kind == ReminderKind.Break ? "break" : "eye break")} reminder snoozed for {snoozeMinutes} min");
    }

    // While a window shows the status live (the tray menu, the settings window, the Why window),
    // every rule is asked every second. Dispose what this returns when the window closes; windows
    // open at the same time each hold their own.
    public IDisposable KeepLive()
    {
        liveViews++;
        ticksSinceAll = PollSchedule.FarTicks;
        return new LiveView(this);
    }

    // Input that Windows does not count as keyboard or mouse input, such as a game controller, at
    // nowMs on the Environment.TickCount64 clock.
    public void NoteInput(long nowMs) => tracker.NoteInput(nowMs);

    // What is changing the lock time right now, counted or not, for the Why window, which asks once
    // a second. It is the look the watcher decides by, taken again only when older than a moment.
    public IReadOnlyList<WhyLine> Explain()
    {
        if (!sessionLocked && Mono - last.At > ExplainAge)
        {
            last = evaluator.Evaluate(Mono, all: true, fresh: false);
            ticksSinceAll = 0;
        }
        return last.Lines;
    }

    public void Dispose()
    {
        if (subscribed)
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
        subscribed = false;
        timer.Dispose();
        scans.Dispose();
        stats.Quit(seams.Wall());
    }

    void OnSessionSwitch(object sender, SessionSwitchEventArgs e) => SessionSwitched(e.Reason);

    internal void SessionSwitched(SessionSwitchReason reason)
    {
        switch (reason)
        {
            case SessionSwitchReason.SessionLock:
                // Every lock counts for today, by this app, by Win+L or by anything else (D11).
                if (!sessionLocked)
                {
                    stats.Locked(seams.Wall());
                    lockedAt = Now;
                }
                sessionLocked = true;
                SetTimer();
                lockRetryAt = null;
                lockGivenUp = false;
                Log.Write("session locked");
                // The countdown note goes now, not at the next tick: an unlock may come before that
                // tick, and nothing would end the note then.
                if (engine.SessionLocked()) EndCountdown("session locked");
                break;
            case SessionSwitchReason.SessionUnlock:
                if (sessionLocked) stats.Unlocked(seams.Wall());
                sessionLocked = false;
                lockedAt = null;
                SetTimer();
                // A face or fingerprint unlock may produce no keyboard or mouse event, so without this the
                // idle clock could still hold its pre-lock value and the tool would lock again at once.
                tracker.MarkInputNow();
                // A countdown still running here means the lock was never reported.
                if (engine.Reset()) EndCountdown("session unlocked");
                ticksSinceAll = PollSchedule.FarTicks;
                // Someone is here: a notice before now was for a sleep that did not come.
                sleepNoticeAt = null;
                Log.Write("session unlocked, idle clock reset");
                ComeBack();
                break;
        }
    }

    void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) => PowerChanged(e.Mode);

    // Windows says the PC is going to sleep, or woke from sleep or hibernation. Not every PC says
    // so, so a long gap between ticks counts as waking too (Tick); seeing both only starts over twice
    // within a moment.
    internal void PowerChanged(PowerModes mode)
    {
        if (mode == PowerModes.Suspend)
        {
            sleepNoticeAt = Now;
            Log.Write("Windows says the PC is going to sleep");
        }
        if (mode != PowerModes.Resume) return;
        double now = Now;
        Log.Write("Windows says the PC woke from sleep; " + Woke(now, lastTickAt ?? now));
        Restate();
    }

    // The PC woke (D47, "sleeping is sleeping"): the lock time starts over from that moment, and the
    // whole of it runs again before any countdown. The idle time counts from the wake until there is
    // input. A countdown that was running ends without the lock or the away actions. A lock or an
    // away from before the sleep stays as it is, and the sleep inside it is time away.
    //
    // With "sign in again when the PC wakes", Windows locks the session as the PC goes to sleep. A
    // lock that began no more than SleptAfter before the sleep is that lock, unless this app had
    // already stepped out. The sleep began at Windows' notice that it was going to sleep, when that
    // came no more than StandbyLag before the last tick (slept): with Modern Standby the app may run
    // on for minutes after the lock and the notice. Without a notice, it began at the last tick. A
    // sleep from ordinary use is not time away, so that lock still counts as a lock, with its time
    // away from the wake. Gives what the wake did, for the log.
    string Woke(double at, double slept)
    {
        double began = sleepNoticeAt is double notice && notice <= slept && slept - notice <= StandbyLag.TotalSeconds ? notice : slept;
        sleepNoticeAt = null;
        if (sessionLocked && awaySince is null && lockedAt is double locked && locked >= began - SleptAfter.TotalSeconds)
        {
            lockedAt = at;
            stats.AwayFrom(seams.Wall());
            return "the session locked as the PC went to sleep, so its time away starts now";
        }
        if (sessionLocked || awaySince is not null || engine.Phase == Phase.Away) return "a lock or an away from before it stays as it is";
        wokeAt = at;
        lastIdleSeconds = 0;
        if (engine.Reset("the PC slept")) EndCountdown("the PC slept");
        // Anything may have changed while it slept: every rule is asked again on the next tick.
        ticksSinceAll = PollSchedule.FarTicks;
        return "the lock time starts over now";
    }

    // The cursor is sampled on every timer event, eight times a second; everything else happens once
    // a second, by the clock rather than by counting events, so a late event or a busy moment never
    // stretches the second: the tick runs on the first event at or after each second since the
    // last, and the countdown shows every number. After a tick more than a second late, as after a
    // sleep, the next is a second after it; the seconds missed are not made up.
    internal void TimerFired()
    {
        tracker.SampleCursor();
        double now = Now;
        if (nextTickAt is double due && now < due - TickEarly) return;
        nextTickAt = nextTickAt is double was && now - was < 1 ? was + 1 : now + 1;
        try
        {
            Tick();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Left to the app's handler, a tick that throws every second would write its stack trace
            // every second, and nothing on screen would say that locking has stopped.
            // An error after some good ticks, every other second say, is still the same trouble: the
            // count goes on until FailingAfter ticks in a row have worked.
            tickErrors.Note(e);
            goodTicks = 0;
            if (++failedTicks == FailingAfter)
            {
                seams.ErrorLog("the tick keeps failing: locking may not work until it works again");
                TicksFailing?.Invoke(true);
            }
            return;
        }
        tickErrors.Worked();
        if (failedTicks == 0 || ++goodTicks < FailingAfter) return;
        if (failedTicks >= FailingAfter)
        {
            seams.ErrorLog("the tick works again");
            TicksFailing?.Invoke(false);
        }
        failedTicks = 0;
        goodTicks = 0;
    }

    // Setting the interval starts the timer's count over, so it is only set when it changes.
    void SetTimer()
    {
        int interval = sessionLocked ? LockedMilliseconds : SampleMilliseconds;
        if (timer.Interval != interval) timer.Interval = interval;
    }

    internal void Tick()
    {
        double now = Now;
        // Ticks come about a second apart. A gap of SleptAfter or more means no tick ran: the PC
        // slept or hibernated, or this process was held up, and the lock time starts over (Woke).
        // The clocks kept counting, and the first input on waking can reach this tick before an idle
        // time does, so the reminders take a gap longer than the eye-break rest as the rest it almost
        // always was rather than as hours at the PC.
        double gap = lastTickAt is double before ? now - before : 0;
        bool rested = gap > EyeBreaks.RestedAfter.TotalSeconds;
        if (gap >= SleptAfter.TotalSeconds)
        {
            Log.Write($"no tick for {Format(gap)}: the PC slept or SteppedOut was held up; " + Woke(now, now - gap)
                + (rested ? "; the break reminders count it as a break" : ""));
        }
        lastTickAt = now;
        if (pausedUntil is double until && now >= until)
        {
            pausedUntil = null;
            Log.Write("pause ended");
        }

        var sample = seams.Sample is { } read ? read() : tracker.Sample();
        double idle = sample.IdleSeconds;
        // Counted from the wake until there is input after it (D47).
        if (wokeAt is double woke)
        {
            double sinceWake = Math.Max(0, now - woke);
            if (idle >= sinceWake) idle = sinceWake;
            else wokeAt = null;
        }
        lastIdleSeconds = idle;
        // Input in the moment after stepping out is not coming back, for the engine either, so it
        // stays away instead of starting a second countdown a lock time later.
        bool input = sample.RealInput && now >= holdComeBackUntil;
        bool paused = IsPaused;

        // The rules keep being asked through a pause: a pause can end with the idle time already past
        // the lock time, and the audio rule must have heard its whole silence hold by then. The engine
        // ignores what they find until the pause is over. Nothing is asked while the session is locked.
        if (!sessionLocked) Look(idle);
        var time = TimeNow();
        var coming = engine.Peek(idle, input, time.Total, paused, sessionLocked);
        if ((coming is EngineEvent.CountdownStarted or EngineEvent.WentAway) && !last.Fresh)
        {
            // These two decide. The process list, the windows and the calls may be a few seconds
            // old, so every rule looks again, afresh, before the countdown shows or the PC locks.
            last = evaluator.Evaluate(Mono, all: true, fresh: true);
            ticksSinceAll = 0;
            time = TimeNow();
        }
        if (!sessionLocked) LogLockTime(time, idle);

        switch (engine.Step(idle, input, time.Total, paused, sessionLocked))
        {
            case EngineEvent.CountdownStarted:
                Log.Write($"idle for {Format(idle)}{IgnoredNote()}, {(lockWhenAway ? "locking" : "stepping out")} in {engine.CountdownRemaining} s; lock time {time}");
                CountdownStarted?.Invoke(engine.CountdownRemaining);
                break;
            case EngineEvent.CountdownTick:
                CountdownTick?.Invoke(engine.CountdownRemaining);
                break;
            case EngineEvent.CountdownCancelled:
                EndCountdown(CancelText(engine.CancelReason, time));
                break;
            case EngineEvent.WentAway:
                CountdownEnded?.Invoke();
                StepOut($"after {Format(idle)} idle", now - idle, forceLock: false);
                break;
        }

        // Back without a lock in between: locking turned off, a dry run, or a lock that failed.
        if (input && !sessionLocked) ComeBack();

        RetryLockIfDue(now);
        RemindIfDue(now, idle, paused, rested);

        var wall = seams.Wall();
        stats.Tick(wall);
        LockStatus = Describe(time, idle, wall);
        if (now - lastHeartbeat >= 10 * 60)
        {
            lastHeartbeat = now;
            Log.Write($"heartbeat: idle {Format(idle)}, {HeartbeatText()}");
        }
        Updated?.Invoke();
    }

    // Every rule every 5 seconds, every second near the lock or while a window shows the status
    // live; the battery every second whatever the idle time (PollSchedule).
    void Look(double idle)
    {
        bool all = PollSchedule.AllRules(idle, closeFrom, ++ticksSinceAll, liveViews > 0);
        if (all) ticksSinceAll = 0;
        last = evaluator.Evaluate(Mono, all, fresh: false);
    }

    LockTime TimeNow() => LockTime.Compute(normalMinutes, last.Effects);

    // One line each time the lock time or what makes it up changes, at any idle time: what support
    // reads first in a log.
    void LogLockTime(LockTime time, double idle)
    {
        if (time.Equals(loggedTime)) return;
        loggedTime = time;
        var seen = last.Lines.Where(l => l.Counts && l.Effect.Kind != EffectKind.None).Select(Evaluator.LogText).Distinct().ToList();
        Log.Write($"idle {Format(idle)}: lock time {time}" + (seen.Count > 0 ? "; " + string.Join(", ", seen) : ""));
    }

    // A countdown that ends without stepping out. Its note goes at once; a break note waits a moment
    // (BreakNoteAfterCountdown), so it does not seem to be the countdown changing its words.
    void EndCountdown(string why)
    {
        Log.Write("countdown cancelled: " + why);
        CountdownEnded?.Invoke();
        holdBreakNoteUntil = Now + Math.Ceiling(BreakNoteAfterCountdown.TotalSeconds);
    }

    static string CancelText(string? reason, LockTime time) => reason switch
    {
        "never" => "never locks now: " + string.Join(", ", time.NeverCauses),
        "later" => "the lock time is now " + time,
        _ => reason ?? "",
    };

    // Asked even while paused, so a break or a locked night during a pause still ends the stretch.
    // Only the note waits: for the pause to end; for a countdown to end, since a note never takes the
    // countdown's place; for a moment after a cancel (EndCountdown); for a call to end; and while
    // Windows holds notifications back. rested: no tick ran for longer than an eye-break rest.
    void RemindIfDue(double now, double idle, bool paused, bool rested)
    {
        if (reminders.Due(now, idle, awaySince is not null || sessionLocked || rested) is not Reminder due) return;
        if (paused || engine.Phase == Phase.Countdown || now < holdBreakNoteUntil || last.CallSeen || !seams.AcceptsNotifications()) return;
        reminders.Shown(due.Kind, now);
        Log.Write(due.Kind == ReminderKind.Break
            ? $"break reminder after {Format(due.SecondsAtThePc)} at the PC"
            : $"eye break reminder after {Format(due.SecondsAtThePc)} of use");
        ReminderDue?.Invoke(due);
    }

    void StepOut(string why, double since, bool forceLock)
    {
        bool locking = forceLock || lockWhenAway;
        if (awaySince is not null)
        {
            // Already stepped out: nothing runs again. The one change is a lock asked for after
            // stepping out without one, which this same away gets, once; the away actions add what
            // a lock brings and undo nothing.
            if (!locking || awayLocks)
            {
                Log.Write($"already away; {why} changes nothing");
                return;
            }
            awayLocks = true;
            holdComeBackUntil = Now + HandLeaving.TotalSeconds;
            WentAway?.Invoke(true, false);
            Lock(why + ", while away without a lock");
            return;
        }
        awaySince = since;
        awayLocks = locking;
        // A key let go or a hand still on the mouse in the next moment is not coming back, so it
        // does not undo the away actions. That covers the hand leaving the tray menu after Lock now
        // as well as the moment a lock takes to arrive after the request. Set before the away
        // actions start, since turning the screens off waits for it to end.
        holdComeBackUntil = Now + HandLeaving.TotalSeconds;
        // Counted from when the idle stretch began, so "Away today" matches "You were away". A dry
        // run counts its away time but never a lock, since nothing locks.
        var wall = seams.Wall();
        stats.WentAway(wall - TimeSpan.FromSeconds(Math.Max(0, Now - since)), wall);
        WentAway?.Invoke(locking, locking && !forceLock);
        if (!locking)
        {
            Log.Write($"away {why}; locking is turned off, so only the away actions run");
            return;
        }
        Lock(why);
    }

    void Lock(string why)
    {
        if (dryRun)
        {
            Log.Write($"WOULD LOCK (dry run) {why}");
            return;
        }
        Log.Write($"locking {why}");
        if (seams.RequestLock()) return;
        lockRetryAt = Now + LockRetryAfter.TotalSeconds;
        Log.Write($"Windows did not take the lock request; asking once more in {LockRetryAfter.TotalSeconds:0} s if still away");
    }

    // Once, and never again for this away: a lock Windows turns down twice is left at that, with the
    // away actions in place, until you are back. An away that asked for a lock still wants it while
    // paused: a pause puts the engine back to watching, but Lock now locks even then, so the retry
    // comes as it would without the pause.
    void RetryLockIfDue(double now)
    {
        if (lockRetryAt is not double at || now < at) return;
        lockRetryAt = null;
        if (sessionLocked || awaySince is null || !awayLocks) return;
        Log.Write("still not locked; asking for the lock once more");
        if (seams.RequestLock()) return;
        lockGivenUp = true;
        Log.Write("Windows did not take the lock request again; the PC stays unlocked until Lock now asks again");
    }

    void ComeBack()
    {
        lockRetryAt = null;
        lockGivenUp = false;
        if (awaySince is not double since) return;
        awaySince = null;
        awayLocks = false;
        var gone = TimeSpan.FromSeconds(Now - since);
        stats.CameBack(seams.Wall());
        Log.Write($"back after {Format(gone.TotalSeconds)}");
        CameBack?.Invoke(gone);
    }

    string IgnoredNote() =>
        tracker.IgnoredSinceLastInput > 0 ? $" ({tracker.IgnoredSinceLastInput} movements under {tracker.JitterPixels} px ignored)" : "";

    LockStatus Describe(LockTime time, double idle, DateTimeOffset wall)
    {
        var kind = sessionLocked ? LockState.SessionLocked
            : IsPaused ? LockState.Paused
            : engine.Phase == Phase.Away ? LockState.Away
            : engine.Phase == Phase.Countdown ? LockState.Countdown
            : time.NeverLocks ? LockState.Never
            : lockWhenAway ? LockState.On
            : LockState.LockingOff;
        double left = kind switch
        {
            LockState.Countdown => Math.Max(0, engine.CountdownEnd - idle),
            LockState.On or LockState.LockingOff when time.TotalSeconds is decimal total => Math.Max(0, (double)total - idle),
            _ => 0,
        };
        var (locks, away) = stats.Today(wall);
        return new LockStatus
        {
            Kind = kind,
            Time = time,
            IdleSeconds = idle,
            SecondsLeft = left,
            PausedUntil = kind == LockState.Paused ? PausedUntil() : null,
            DryRun = dryRun,
            FailedRules = last.Failed,
            Today = new TodayCounts(locks, away),
        };
    }

    // Shown as a time of day, worked out from UTC so that a daylight saving change before then does
    // not move it. Null while paused until turned back on.
    DateTime? PausedUntil() =>
        pausedUntil is double until && !double.IsInfinity(until) ? seams.Wall().AddSeconds(until - Now).LocalDateTime : null;

    TimeSpan Mono => seams.Monotonic();

    double Now => Mono.TotalSeconds;

    sealed class LiveView(Watcher watcher) : IDisposable
    {
        bool released;

        public void Dispose()
        {
            if (released) return;
            released = true;
            watcher.liveViews--;
        }
    }

    // The state now in words for the heartbeat line, with the seconds the countdown has left and
    // what the rules see now.
    internal string HeartbeatText() => LogText(LockStatus, engine.CountdownRemaining, last.Lines);

    // The state in words for the log, which stays in English: someone reading a log sent from
    // another PC sees "locks in 12:30", not the name of a value in the code. A hold names the first
    // thing seen that never locks, and a longer wait the first thing seen that buys more time.
    internal static string LogText(LockStatus status, double countdownLeft, IEnumerable<WhyLine> lines)
    {
        (string Text, string? Reason) said = status.Kind switch
        {
            LockState.SessionLocked => ("session locked", null),
            LockState.Paused => (status.PausedUntil is DateTime until ? $"paused until {until:HH:mm}" : "paused", null),
            LockState.Away => ("away", null),
            LockState.Countdown => ("counting down, " + Format(countdownLeft) + " left", null),
            LockState.Never => ("not locking", Reason(lines, EffectKind.Never)),
            LockState.Starting => ("starting", null),
            _ when status.Time.Extras.Any(e => e.Effect.Amount > 0) => ("waiting longer, locks in " + Format(status.SecondsLeft), Reason(lines, EffectKind.Extra)),
            _ => ("locks in " + Format(status.SecondsLeft), null),
        };
        return said.Reason is null ? said.Text : said.Text + ", " + said.Reason;
    }

    static string? Reason(IEnumerable<WhyLine> lines, EffectKind kind) =>
        lines.Where(l => l.Counts && l.Effect.Kind == kind).Select(Seen).FirstOrDefault(s => s is not null);

    // What a rule saw, as the log words it. The battery has no words here: it only ever brings the
    // lock forward.
    static string? Seen(WhyLine line) => line.Signal switch
    {
        Signal.Sound => "audio playing: " + line.Detail,
        Signal.Display => "an app asked Windows to keep the display on",
        Signal.FullScreen => "fullscreen app: " + line.Detail,
        Signal.Call => "microphone in use: " + line.Detail,
        Signal.App => "process running: " + line.Detail,
        _ => null,
    };

    // Minutes:seconds, or hours:minutes:seconds from one hour on. The hours are counted in full, so a
    // day reads 24:00:00, not 0:00:00.
    public static string Format(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return t.TotalHours >= 1 ? $"{(long)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : t.ToString(@"m\:ss");
    }
}
