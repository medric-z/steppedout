namespace SteppedOut;

readonly record struct InputSample(double RawIdleSeconds, double IdleSeconds, bool RealInput);

// Reads how long the session has gone without keyboard or mouse input. Windows only exposes the
// time of the last input, not what it was, and that is all this class ever asks for. The cursor
// position is sampled alongside it so that sensor jitter can be told apart from a person.
sealed class InputTracker
{
    uint lastRawTick;
    long lastInputMs;
    bool primed;
    IntPtr lastForeground;
    Native.POINT windowStart;
    bool windowStarted;
    int maxMovePx;
    // The latest input noted since the last sample, or none. Written from whatever thread notes it.
    long notedMs = NothingNoted;

    const long NothingNoted = long.MinValue;

    // Movements smaller than this do not count as input. 0 turns the filter off.
    public int JitterPixels { get; set; }

    // Input Windows does not count as keyboard or mouse input, such as a game controller, made at
    // nowMs on the Environment.TickCount64 clock. The next sample counts it as real input from that
    // moment. The jitter filter never sees it: it only ever judges the cursor.
    public void NoteInput(long nowMs)
    {
        long seen = Interlocked.Read(ref notedMs);
        while (nowMs > seen)
        {
            long was = Interlocked.CompareExchange(ref notedMs, nowMs, seen);
            if (was == seen) return;
            seen = was;
        }
    }

    public int IgnoredSinceLastInput { get; private set; }

    // Call often, about every 125 ms. Tracks how far the cursor has strayed from where it was
    // when the current one-second window began.
    public void SampleCursor() => SampleCursor(Cursor());

    internal void SampleCursor(Native.POINT? cursor)
    {
        if (cursor is not Native.POINT p) return;
        if (!windowStarted)
        {
            windowStart = p;
            windowStarted = true;
            return;
        }
        int move = Math.Max(Math.Abs(p.X - windowStart.X), Math.Abs(p.Y - windowStart.Y));
        if (move > maxMovePx) maxMovePx = move;
    }

    // Call once a second.
    public InputSample Sample()
    {
        long now = Environment.TickCount64;
        uint rawIdleMs = RawIdleMilliseconds(out uint rawTick);
        return Sample(now, rawIdleMs, rawTick, Native.GetForegroundWindow(), Cursor());
    }

    // The readings come in as plain values so this can be tested without a desktop.
    internal InputSample Sample(long now, uint rawIdleMs, uint rawTick, IntPtr foreground, Native.POINT? cursor)
    {
        // A program that injects input may stamp it with its own tick count, a little ahead of
        // this PC's, and the subtraction then wraps to about 49.7 days. No real idle time gets
        // near half of that, so it is input from just now.
        if (rawIdleMs > int.MaxValue) rawIdleMs = 0;
        SampleCursor(cursor);
        int moved = maxMovePx;
        bool cursorKnown = windowStarted;
        // The next window starts from this same reading. Left to the next one, 125 ms later, a
        // twitch in between would go unmeasured and pass for a key press.
        StartWindow(cursor);
        long noted = Interlocked.Exchange(ref notedMs, NothingNoted);

        if (!primed)
        {
            primed = true;
            lastRawTick = rawTick;
            lastForeground = foreground;
            lastInputMs = now - rawIdleMs;
            if (noted != NothingNoted) lastInputMs = Math.Max(lastInputMs, Math.Min(noted, now));
            return new InputSample(rawIdleMs / 1000.0, (now - lastInputMs) / 1000.0, false);
        }

        bool reported = rawTick != lastRawTick;
        bool foregroundChanged = foreground != lastForeground;
        lastRawTick = rawTick;
        lastForeground = foreground;

        // With no cursor reading (the lock screen hides it) there is nothing to filter with.
        bool real = cursorKnown ? JitterFilter.IsRealInput(reported, foregroundChanged, moved, JitterPixels) : reported;
        if (real)
        {
            lastInputMs = now - rawIdleMs;
            IgnoredSinceLastInput = 0;
        }
        else if (reported)
        {
            IgnoredSinceLastInput++;
        }

        // Input from elsewhere is real whatever the cursor did.
        if (noted != NothingNoted)
        {
            lastInputMs = Math.Max(lastInputMs, Math.Min(noted, now));
            IgnoredSinceLastInput = 0;
            real = true;
        }

        return new InputSample(rawIdleMs / 1000.0, (now - lastInputMs) / 1000.0, real);
    }

    public void MarkInputNow()
    {
        lastInputMs = Environment.TickCount64;
        IgnoredSinceLastInput = 0;
    }

    // Stepping out by hand takes a click or a key, and that must not read as coming back on the
    // next sample. The idle clock is left alone.
    public void IgnoreInputSoFar()
    {
        RawIdleMilliseconds(out uint rawTick);
        IgnoreInputSoFar(rawTick, Native.GetForegroundWindow(), Cursor());
    }

    internal void IgnoreInputSoFar(uint rawTick, IntPtr foreground, Native.POINT? cursor)
    {
        lastRawTick = rawTick;
        lastForeground = foreground;
        StartWindow(cursor);
        Interlocked.Exchange(ref notedMs, NothingNoted);
    }

    void StartWindow(Native.POINT? cursor)
    {
        windowStart = cursor.GetValueOrDefault();
        windowStarted = cursor is not null;
        maxMovePx = 0;
    }

    static Native.POINT? Cursor() => Native.GetCursorPos(out Native.POINT p) ? p : null;

    static uint RawIdleMilliseconds(out uint lastInputTick)
    {
        uint? tick = Native.LastInputTick();
        lastInputTick = tick ?? 0;
        // The tick count is 32 bits and wraps every 49.7 days; unsigned subtraction survives the wrap.
        return tick is uint t ? unchecked((uint)Environment.TickCount - t) : 0;
    }
}
