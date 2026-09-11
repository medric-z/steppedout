using Microsoft.Win32;

namespace SteppedOut;

interface ILockSuppressor
{
    string Name { get; }

    // A short reason while locking should be held off, otherwise null.
    string? Check();
}

sealed class Watcher : IDisposable
{
    readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    readonly InputTracker tracker = new();
    readonly LockEngine engine;
    readonly List<ILockSuppressor> rules = new();
    readonly HashSet<string> failedRules = new();
    readonly bool dryRun;
    bool sessionLocked;
    int subTick;
    DateTime? pausedUntil;
    DateTime lastHeartbeat = DateTime.MinValue;

    public Watcher(Config config, bool dryRun)
    {
        engine = new LockEngine(config.IdleMinutes * 60, config.GraceSeconds);
        this.dryRun = dryRun;
        Apply(config);
        timer.Tick += (_, _) => OnTimer();
        sessionLocked = SessionLock.IsLocked();
        if (sessionLocked) Log.Write("started while the session is locked");
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public bool IsPaused => pausedUntil is DateTime until && DateTime.Now < until;
    public string Status { get; private set; } = "starting";

    public event Action<int>? CountdownStarted;
    public event Action<int>? CountdownTick;
    public event Action? CountdownEnded;

    public void Start() => timer.Start();

    public void Apply(Config config)
    {
        engine.IdleThresholdSeconds = config.IdleMinutes * 60;
        engine.GraceSeconds = config.GraceSeconds;
        tracker.JitterPixels = config.JitterFilter ? config.JitterPixels : 0;
        rules.Clear();
        rules.AddRange(Rules.Build(config));
        failedRules.Clear();
    }

    public void Pause(TimeSpan? duration)
    {
        pausedUntil = duration is TimeSpan d ? DateTime.Now + d : DateTime.MaxValue;
        Log.Write(duration is TimeSpan span ? $"paused for {span.TotalHours:0.#} h" : "paused until resumed");
    }

    public void Resume()
    {
        pausedUntil = null;
        Log.Write("resumed");
    }

    public void LockNow()
    {
        CountdownEnded?.Invoke();
        engine.Reset();
        if (dryRun)
        {
            Log.Write("WOULD LOCK (dry run), requested from the menu");
            return;
        }
        Log.Write("locking, requested from the menu");
        SessionLock.Request();
    }

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        timer.Dispose();
    }

    void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
                sessionLocked = true;
                Log.Write("session locked");
                break;
            case SessionSwitchReason.SessionUnlock:
                sessionLocked = false;
                // A face or fingerprint unlock may produce no keyboard or mouse event, so without this the
                // idle clock could still hold its pre-lock value and the tool would lock again at once.
                tracker.MarkInputNow();
                engine.Reset();
                Log.Write("session unlocked, idle clock reset");
                break;
        }
    }

    // The cursor is sampled ten times a second; everything else happens once a second.
    void OnTimer()
    {
        tracker.SampleCursor();
        if (++subTick < 10) return;
        subTick = 0;
        Tick();
    }

    void Tick()
    {
        if (pausedUntil is DateTime until && until != DateTime.MaxValue && DateTime.Now >= until)
        {
            pausedUntil = null;
            Log.Write("pause ended");
        }

        var sample = tracker.Sample();
        string? suppressor = Evaluate();
        var ev = engine.Step(sample.IdleSeconds, sample.RealInput, suppressor, IsPaused, sessionLocked);
        switch (ev)
        {
            case EngineEvent.CountdownStarted:
                Log.Write($"idle for {Format(sample.IdleSeconds)}{IgnoredNote()}, locking in {engine.CountdownRemaining} s");
                CountdownStarted?.Invoke(engine.CountdownRemaining);
                break;
            case EngineEvent.CountdownTick:
                CountdownTick?.Invoke(engine.CountdownRemaining);
                break;
            case EngineEvent.CountdownCancelled:
                Log.Write("countdown cancelled: " + engine.CancelReason);
                CountdownEnded?.Invoke();
                break;
            case EngineEvent.SuppressionChanged:
                Log.Write($"idle for {Format(sample.IdleSeconds)}, not locking: {engine.Suppressor}");
                break;
            case EngineEvent.LockRequested:
                CountdownEnded?.Invoke();
                if (dryRun)
                {
                    Log.Write($"WOULD LOCK (dry run) after {Format(sample.IdleSeconds)} idle");
                }
                else
                {
                    Log.Write($"locking after {Format(sample.IdleSeconds)} idle");
                    SessionLock.Request();
                }
                break;
        }

        Status = DescribeStatus(sample);
        if ((DateTime.Now - lastHeartbeat).TotalMinutes >= 10)
        {
            lastHeartbeat = DateTime.Now;
            Log.Write($"heartbeat: idle {Format(sample.IdleSeconds)}, {Status}");
        }
    }

    string IgnoredNote() =>
        tracker.IgnoredSinceLastInput > 0 ? $" ({tracker.IgnoredSinceLastInput} movements under {tracker.JitterPixels} px ignored)" : "";

    string? Evaluate()
    {
        foreach (var rule in rules)
        {
            try
            {
                string? reason = rule.Check();
                if (reason is not null) return reason;
            }
            catch (Exception e)
            {
                if (failedRules.Add(rule.Name)) Log.Write($"rule {rule.Name} failed, will keep trying: {e.Message}");
            }
        }
        return null;
    }

    string DescribeStatus(InputSample sample)
    {
        if (sessionLocked) return "session locked";
        if (IsPaused) return pausedUntil == DateTime.MaxValue ? "paused" : $"paused until {pausedUntil:HH:mm}";
        return engine.Phase switch
        {
            Phase.Locked => dryRun ? "would have locked, waiting for input" : "locked",
            Phase.Countdown => $"locking in {engine.CountdownRemaining} s",
            _ when engine.Suppressor is not null && sample.IdleSeconds >= engine.IdleThresholdSeconds => "not locking: " + engine.Suppressor,
            _ => "locks in " + Format(engine.IdleThresholdSeconds - sample.IdleSeconds),
        };
    }

    public static string Format(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }
}
