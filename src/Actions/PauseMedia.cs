namespace SteppedOut;

// What pausing media needs from Windows, so the tests can count what was paused, played and let go.
interface IMediaControl
{
    MediaSessions.Paused PausePlaying();

    // Plays the paused apps again and lets go of them. Returns the apps that are playing again.
    List<string> PlayAgain(MediaSessions.Paused paused);

    // Lets go of the paused apps without playing them.
    void Release(MediaSessions.Paused paused);
}

// The media controls Windows shows in the volume flyout.
sealed class WindowsMedia : IMediaControl
{
    public MediaSessions.Paused PausePlaying()
    {
        // The tests and the screenshot tool run on a PC someone is using.
        if (SessionLock.Blocked) throw new InvalidOperationException("Pausing media is blocked in this process.");
        return MediaSessions.PausePlaying();
    }

    public List<string> PlayAgain(MediaSessions.Paused paused) => MediaSessions.PlayAgain(paused);

    public void Release(MediaSessions.Paused paused) => MediaSessions.Release(paused);
}

// Pauses whatever is playing when you leave. When you are back it plays the same things again, or,
// with "Keep it paused", leaves them paused. Only what was actually playing is touched, so
// music you had paused yourself stays paused. "Don't pause" adds no action at all.
//
// It pauses on every away, with or without a lock, as before 1.0: with locking off, the other away
// actions run too, and what plays while nobody is there is still missed.
sealed class PauseMedia
{
    readonly IMediaControl media;
    readonly bool stayPaused;
    readonly Action<string> log;
    MediaSessions.Paused? paused;

    public PauseMedia(IMediaControl media, bool stayPaused, Action<string> log)
    {
        this.media = media;
        this.stayPaused = stayPaused;
        this.log = log;
    }

    public string Name => stayPaused ? "pause what is playing and leave it paused" : "pause what is playing";

    public void Leave()
    {
        // A second pause would find nothing playing and lose the list of what was.
        if (paused is not null) return;
        paused = media.PausePlaying();
        if (paused.Sessions.Count > 0) log("paused: " + string.Join(", ", paused.Apps));
    }

    // The apps that are playing again.
    public IReadOnlyList<string> Return()
    {
        if (paused is not { } held) return Array.Empty<string>();
        paused = null;
        if (held.Sessions.Count == 0) return Array.Empty<string>();
        if (stayPaused)
        {
            string apps = string.Join(", ", held.Apps);
            media.Release(held);
            log("left paused: " + apps);
            return Array.Empty<string>();
        }
        var played = media.PlayAgain(held);
        if (played.Count > 0) log("playing again: " + string.Join(", ", played));
        return played;
    }
}
