namespace SteppedOut;

// Start with Windows as the tray app reaches it: the Run entry for the normal exe, the startup task
// for the Store package (Autostart), or a stand-in.
interface IAutostart
{
    // Asking Windows can take seconds (the Store package's startup task), so it is asked off the UI
    // thread. The Run entry is one registry read or write, asked at once.
    bool Slow { get; }

    AutostartState Read();

    AutostartState Choose(bool on);

    // A Run entry left by a copy that was deleted or moved.
    bool PointsAtMissingFile();
}

sealed class WindowsAutostart : IAutostart
{
    public bool Slow => Packaged.StartupTask is not null;

    public AutostartState Read() => Autostart.Read();

    public AutostartState Choose(bool on) => Autostart.Choose(on);

    public bool PointsAtMissingFile() => Autostart.PointsAtMissingFile();
}

// Start with Windows kept in memory: what the tray app uses while SessionLock.Blocked is set, so a
// test or the render tool never writes the Run entry of the person using the PC.
sealed class MemoryAutostart : IAutostart
{
    AutostartState state = AutostartState.Off;

    public bool Slow => false;

    public AutostartState Read() => state;

    public AutostartState Choose(bool on) => state = on ? AutostartState.On : AutostartState.Off;

    public bool PointsAtMissingFile() => false;
}

// The state the settings window shows, kept here so the window never waits on Windows. For the
// normal exe every call is done when it returns. For the Store package every call goes to one queue
// off the UI thread, one at a time, and only the answer to the newest call is kept: a read that was
// asked before a choice can't overwrite it. Until a choice's answer is in, the state is Reading; a
// read again keeps what is shown until Windows answers, and is not asked twice while one waits.
sealed class TrayAutostart
{
    readonly IAutostart source;
    // Runs an action on the UI thread later; it does nothing once the app is closing.
    readonly Action<Action> post;
    readonly object gate = new();
    Task queue = Task.CompletedTask;
    int asked;
    // The number of a read still with Windows, or 0.
    int reading;
    bool closed;

    public TrayAutostart(IAutostart source, Action<Action> post)
    {
        this.source = source;
        this.post = post;
    }

    public AutostartState State { get; private set; } = AutostartState.Reading;

    // The state changed. On the UI thread.
    public event Action? Changed;

    // The calls still waiting for Windows, for the tests.
    internal Task Pending
    {
        get
        {
            lock (gate) return queue;
        }
    }

    // A Run entry for a copy that has since been deleted or moved starts nothing at sign-in, so this
    // copy takes it over.
    public void TakeOverIfMissing()
    {
        if (!source.PointsAtMissingFile()) return;
        Log.Write("start with Windows pointed at a file that is gone; it now starts this copy");
        Choose(true);
    }

    // Windows may have changed it since: read it again.
    public void Refresh()
    {
        if (closed) return;
        if (source.Slow)
        {
            lock (gate)
            {
                if (reading != 0 && reading == asked) return;
            }
        }
        Ask(source.Read, read: true);
    }

    public void Choose(bool on) => Ask(() => source.Choose(on), read: false);

    // Nothing more is shown after this; a call still with Windows finishes and is dropped.
    public void Close() => closed = true;

    void Ask(Func<AutostartState> call, bool read)
    {
        if (closed) return;
        int number = ++asked;
        if (!source.Slow)
        {
            Set(Safely(call));
            return;
        }
        if (!read) Set(AutostartState.Reading);
        lock (gate)
        {
            if (read) reading = number;
            queue = queue.ContinueWith(_ =>
            {
                var state = Safely(call);
                lock (gate)
                {
                    if (reading == number) reading = 0;
                }
                post(() =>
                {
                    if (!closed && number == asked) Set(state);
                });
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    // Autostart writes its own log lines and answers for the Store package's errors; anything else
    // that goes wrong reads as off and changeable, so the switch can be tried again.
    static AutostartState Safely(Func<AutostartState> call)
    {
        try
        {
            return call();
        }
        catch (Exception e)
        {
            Log.Write("start with Windows: " + e.Message);
            return AutostartState.Off;
        }
    }

    void Set(AutostartState state)
    {
        if (state == State) return;
        State = state;
        Changed?.Invoke();
    }
}
