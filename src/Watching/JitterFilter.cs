namespace SteppedOut;

// Decides whether input that Windows reported in the last second means a person is present.
//
// Windows only says when the last input happened, not what it was, and this tool refuses to
// install a keyboard hook to find out. So the filter reasons from the one thing it can read
// without observing content, the cursor position:
//
//   moved 0 px        keyboard, wheel or a click, none of which a twitchy sensor can produce
//   moved 1..N-1 px   the twitch of a mouse sensor, ignored
//   moved N px+       a hand on the mouse
//   window changed    real, whatever moved the cursor
//
// A key pressed in the same second as a 2 px twitch is discarded with the twitch; the next key
// counts. A sensor that never moves the cursor a full pixel looks like typing and keeps the
// session unlocked. Both are accepted costs of not reading keystrokes.
static class JitterFilter
{
    public static bool IsRealInput(bool inputReported, bool foregroundChanged, int cursorMovedPx, int jitterPixels)
    {
        if (!inputReported) return false;
        if (jitterPixels <= 1 || foregroundChanged) return true;
        return cursorMovedPx == 0 || cursorMovedPx >= jitterPixels;
    }
}
