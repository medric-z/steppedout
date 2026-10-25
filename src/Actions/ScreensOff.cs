namespace SteppedOut;

// What turning the screens off needs from Windows, so the tests can count instead.
interface IScreens
{
    bool TurnOff();

    // When Windows last saw keyboard or mouse input, on its 32-bit tick clock.
    uint? LastInputTick();

    uint Now { get; }
}

// The real screens. Any window of this process will do, since the message is handled by Windows'
// default window code, but it has to stay the same window. A form's does not: WinForms swaps it for
// a new one when, for example, the form's RightToLeft changes. So this keeps a hidden one of its own,
// made once, on the UI thread, whose message loop delivers the message.
sealed class PowerScreens : IScreens
{
    const int Off = 2;
    static NativeWindow? hidden;

    readonly IntPtr window;

    // Made on the UI thread; see AwayActions.Apply.
    public PowerScreens()
    {
        if (hidden is null)
        {
            hidden = new NativeWindow();
            hidden.CreateHandle(new CreateParams());
        }
        window = hidden.Handle;
    }

    public bool TurnOff()
    {
        // The tests and the screenshot tool run on a PC someone is using.
        if (SessionLock.Blocked) throw new InvalidOperationException("Turning the screens off is blocked in this process.");
        return Native.PostMessage(window, Native.WmSysCommand, Native.ScMonitorPower, Off);
    }

    public uint? LastInputTick() => Native.LastInputTick();

    public uint Now => unchecked((uint)Environment.TickCount);
}

// Turns the screens off when you leave. Any key or mouse movement turns them back on, so there is
// nothing to undo. A twitch of the mouse or a bumped desk wakes them too; with a lock, the lock
// screen's own timeout turns them off again, and without one, this does once it has been quiet for
// a while.
sealed class ScreensOff
{
    // A sensor that twitches more often than this leaves the screens on rather than flickering.
    const uint QuietMs = 10_000;
    static readonly TimeSpan LockScreenFade = TimeSpan.FromSeconds(2);
    static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(1);

    readonly IScreens screens;
    readonly TimeProvider time;
    readonly Action<string> log;
    readonly object gate = new();
    ITimer? watch;
    CancellationToken watching;
    // The last input Windows had seen when the screens last went off, on its tick clock.
    uint offAt;

    public ScreensOff(IScreens screens, TimeProvider time, Action<string> log)
    {
        this.screens = screens;
        this.time = time;
        this.log = log;
    }

    public string Name => "turn the screens off";

    // back is cancelled the moment you are back.
    public void Leave(bool locking, CancellationToken back)
    {
        // The lock screen fades in for a moment, and that wakes the screens again; wait it out. If
        // you are back by then, after a quick face unlock for example, the screens stay on.
        // Without a lock, the hand leaving the mouse would wake them, and in that moment input
        // does not count as coming back, so nothing would turn them off again; wait that out too.
        var wait = locking ? LockScreenFade : Watcher.HandLeaving + TimeSpan.FromMilliseconds(500);
        if (Wait(wait, back))
        {
            log("screens left on: already back");
            return;
        }
        if (!screens.TurnOff())
        {
            log("could not turn the screens off");
            return;
        }
        log("screens off");
        if (!locking) Watch(back);
    }

    public void Return() => StopWatching();

    // The PC locked after the screens went off without a lock: the lock screen turns them off by
    // itself from here on, so nothing watches for them waking any more.
    public void Locked() => StopWatching();

    public void Ending() => StopWatching();

    // True when you came back during the wait.
    bool Wait(TimeSpan wait, CancellationToken back)
    {
        try
        {
            Task.Delay(wait, time, back).Wait(CancellationToken.None);
            return false;
        }
        catch (AggregateException) when (back.IsCancellationRequested)
        {
            return true;
        }
    }

    // On a timer, since the away actions' thread cannot wait here: the program to run when away
    // comes after this.
    void Watch(CancellationToken back)
    {
        lock (gate)
        {
            watch?.Dispose();
            watching = back;
            offAt = screens.LastInputTick() ?? 0;
            watch = time.CreateTimer(_ => OffAgainIfIgnored(), null, CheckEvery, CheckEvery);
        }
    }

    void OffAgainIfIgnored()
    {
        lock (gate)
        {
            if (watch is null) return;
            // Input that counts as coming back cancels the token within a second, long before the
            // quiet stretch is over.
            if (watching.IsCancellationRequested)
            {
                StopWatching();
                return;
            }
            if (screens.LastInputTick() is not uint last || !WokenThenQuiet(offAt, last, screens.Now)) return;
            offAt = last;
            try
            {
                if (screens.TurnOff()) log("screens off again: what woke them did not count as coming back");
            }
            catch (Exception e)
            {
                log("could not turn the screens off again: " + e.Message);
            }
        }
    }

    void StopWatching()
    {
        lock (gate)
        {
            watch?.Dispose();
            watch = null;
        }
    }

    // Input came after the screens went off, and none since for the whole quiet stretch. Windows'
    // tick clock wraps after 49.7 days, which the unsigned subtraction allows for. Input another
    // program stamped a little ahead of the clock would wrap the other way; it counts as just now.
    internal static bool WokenThenQuiet(uint offAt, uint lastInput, uint now)
    {
        uint quiet = unchecked(now - lastInput);
        return lastInput != offAt && quiet <= int.MaxValue && quiet >= QuietMs;
    }
}
