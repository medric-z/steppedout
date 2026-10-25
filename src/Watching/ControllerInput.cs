namespace SteppedOut;

// Counts a game controller as input, since Windows does not: a person playing with a controller is
// at the PC. Windows has no event for it, so the pads are asked: a connected one 4 times a second,
// an empty slot every 3 seconds, as asking an empty slot is slow.
//
// Only real use counts: a button pressed or let go, a stick pushed past XInput's dead zone that
// moved since the last reading, or a trigger pulled past 30 or let go from past it, or moved while
// past it. A stick that drifts by itself sends new readings all the time, so new packet numbers
// alone never count, and neither do small changes inside the dead zone, a still stick, or a stick
// or trigger resting past its threshold that only wobbles. A pad that is not connected counts as
// nothing, and the first reading after it connects is only a starting point.
//
// Real use goes to noteInput with the time on the Environment.TickCount64 clock: the tray passes
// Watcher.NoteInput, which hands it to InputTracker.NoteInput.
sealed class ControllerInput : IDisposable
{
    public const int Slots = 4;
    public const int ConnectedPollMs = 250;
    public const int EmptyPollMs = 3000;

    // XInput's own numbers: XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE, _RIGHT_THUMB_DEADZONE and
    // _TRIGGER_THRESHOLD.
    public const int LeftDeadZone = 7849;
    public const int RightDeadZone = 8689;
    public const int TriggerThreshold = 30;

    // How far a stick past the dead zone has to move between readings to count, out of 32767. A
    // worn stick resting past the dead zone wobbles by less than this; a thumb moving it does more
    // within a quarter of a second.
    public const int StickNoise = 1024;

    // The same for a trigger that stays past the threshold, out of 255: one resting part-pulled, as
    // under a controller lying on it, wobbles by a step or two. Crossing the threshold always counts.
    public const int TriggerNoise = 4;

    readonly Action<long> noteInput;
    readonly IPads pads;
    readonly PadState?[] last = new PadState?[Slots];
    readonly long[] nextEmptyRead = new long[Slots];
    readonly object gate = new();
    System.Threading.Timer? timer;
    bool running;
    bool polling;

    public ControllerInput(Action<long> noteInput) : this(noteInput, XInputPads.Instance)
    {
    }

    internal ControllerInput(Action<long> noteInput, IPads pads)
    {
        this.noteInput = noteInput;
        this.pads = pads;
    }

    public bool Running
    {
        get
        {
            lock (gate) return running;
        }
    }

    // Starts or stops asking the controllers, for the "Count a controller as using the PC" setting.
    public void SetEnabled(bool on)
    {
        if (on) Start();
        else Stop();
    }

    public void Start()
    {
        lock (gate)
        {
            if (running) return;
            running = true;
            Array.Clear(last);
            Array.Clear(nextEmptyRead);
            timer = new System.Threading.Timer(_ => OnTimer(), null, 0, Timeout.Infinite);
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            if (!running) return;
            running = false;
            timer?.Dispose();
            timer = null;
        }
    }

    public void Dispose() => Stop();

    void OnTimer()
    {
        lock (gate)
        {
            // One poll at a time; a slow read never piles up behind itself.
            if (!running || polling) return;
            polling = true;
        }
        int delay = ConnectedPollMs;
        try
        {
            long now = Environment.TickCount64;
            Poll(now);
            delay = NextDelayMs(now);
        }
        catch (Exception e)
        {
            Log.Write("game controller: " + e.Message);
            delay = EmptyPollMs;
        }
        lock (gate)
        {
            polling = false;
            if (running) timer?.Change(delay, Timeout.Infinite);
        }
    }

    // One round: asks every connected pad and every empty slot that is due, at nowMs. True when
    // there was real use, which then went to noteInput.
    internal bool Poll(long nowMs)
    {
        bool used = false;
        for (int slot = 0; slot < Slots; slot++)
        {
            if (last[slot] is null && nowMs < nextEmptyRead[slot]) continue;
            bool connected;
            PadState now;
            try
            {
                connected = pads.TryRead(slot, out now);
            }
            catch (Exception e)
            {
                Log.Write("game controller " + (slot + 1) + ": " + e.Message);
                connected = false;
                now = default;
            }
            if (!connected)
            {
                if (last[slot] is not null) Log.Write("game controller " + (slot + 1) + " disconnected");
                last[slot] = null;
                nextEmptyRead[slot] = nowMs + EmptyPollMs;
                continue;
            }
            if (last[slot] is PadState before)
            {
                if (IsRealInput(before, now)) used = true;
            }
            else
            {
                Log.Write("game controller " + (slot + 1) + " connected");
            }
            last[slot] = now;
        }
        if (used) noteInput(nowMs);
        return used;
    }

    // How long to wait after a poll at nowMs: a quarter second while a pad is connected, or until
    // the next empty slot is due.
    internal int NextDelayMs(long nowMs)
    {
        long due = long.MaxValue;
        for (int slot = 0; slot < Slots; slot++)
        {
            if (last[slot] is not null) return ConnectedPollMs;
            due = Math.Min(due, nextEmptyRead[slot]);
        }
        return (int)Math.Clamp(due - nowMs, 1, EmptyPollMs);
    }

    internal bool IsConnected(int slot) => last[slot] is not null;

    // Whether going from one reading to the next was a person using the controller.
    internal static bool IsRealInput(PadState before, PadState now)
    {
        if (before.Buttons != now.Buttons) return true;
        if (TriggerMoved(before.LeftTrigger, now.LeftTrigger) || TriggerMoved(before.RightTrigger, now.RightTrigger)) return true;
        return StickMoved(before.LeftX, before.LeftY, now.LeftX, now.LeftY, LeftDeadZone)
            || StickMoved(before.RightX, before.RightY, now.RightX, now.RightY, RightDeadZone);
    }

    // Pulled past the threshold or let go from past it; or, past it both times, moved by more than a
    // resting trigger wobbles.
    static bool TriggerMoved(byte before, byte now)
    {
        bool wasPast = before > TriggerThreshold;
        bool isPast = now > TriggerThreshold;
        if (wasPast != isPast) return true;
        return isPast && Math.Abs(now - before) > TriggerNoise;
    }

    static bool StickMoved(short beforeX, short beforeY, short x, short y, int deadZone)
    {
        if (!PastDeadZone(x, y, deadZone)) return false;
        return Math.Abs(x - beforeX) > StickNoise || Math.Abs(y - beforeY) > StickNoise;
    }

    // XInput's dead zone is round: the stick is past it when its distance from the middle is.
    static bool PastDeadZone(short x, short y, int deadZone) =>
        (long)x * x + (long)y * y > (long)deadZone * deadZone;
}
