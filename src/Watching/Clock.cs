namespace SteppedOut;

// A clock that only moves forward, for measuring lengths of time. DateTime.Now jumps an hour at
// daylight saving changes and whenever the clock is set.
static class Clock
{
    public static TimeSpan Now => TimeSpan.FromMilliseconds(Environment.TickCount64);
}
