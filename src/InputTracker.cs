using System.Runtime.InteropServices;

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

    // Movements smaller than this do not count as input. 0 turns the filter off.
    public int JitterPixels { get; set; }

    public int IgnoredSinceLastInput { get; private set; }

    // Call often, about every 100 ms. Tracks how far the cursor has strayed from where it was
    // when the current one-second window began.
    public void SampleCursor()
    {
        if (!Native.GetCursorPos(out Native.POINT p)) return;
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
        IntPtr foreground = Native.GetForegroundWindow();
        int moved = maxMovePx;
        bool cursorKnown = windowStarted;
        windowStarted = false;
        maxMovePx = 0;

        if (!primed)
        {
            primed = true;
            lastRawTick = rawTick;
            lastForeground = foreground;
            lastInputMs = now - rawIdleMs;
            return new InputSample(rawIdleMs / 1000.0, rawIdleMs / 1000.0, false);
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

        return new InputSample(rawIdleMs / 1000.0, (now - lastInputMs) / 1000.0, real);
    }

    public void MarkInputNow()
    {
        lastInputMs = Environment.TickCount64;
        IgnoredSinceLastInput = 0;
    }

    static uint RawIdleMilliseconds(out uint lastInputTick)
    {
        var info = new Native.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<Native.LASTINPUTINFO>() };
        if (!Native.GetLastInputInfo(ref info))
        {
            lastInputTick = 0;
            return 0;
        }
        lastInputTick = info.dwTime;
        // dwTime is a 32-bit tick count that wraps every 49.7 days; unsigned subtraction survives the wrap.
        return unchecked((uint)Environment.TickCount - info.dwTime);
    }
}
