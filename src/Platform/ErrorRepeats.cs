namespace SteppedOut;

// An error that comes back again and again, such as one thrown on every tick, is written to the log
// in full the first time and then only counted: one short line an hour while it keeps coming, and
// the whole of it again when a different one comes. Written in full every time, it would push
// everything else out of the 512 KB log within minutes, its own first stack trace included. One
// that comes back between tries that work, every other tick say, is the same run of errors, only
// counted; the run ends once the errors have stayed away for QuietFor.
sealed class ErrorRepeats(string what, Action<string> write, Func<TimeSpan> clock)
{
    public static readonly TimeSpan SayAgainAfter = TimeSpan.FromHours(1);
    public static readonly TimeSpan QuietFor = TimeSpan.FromMinutes(1);

    // The error written last, the times it came since without a line, when that line was, and when
    // the error last came.
    string? key;
    int unsaid;
    TimeSpan saidAt;
    TimeSpan cameAt;

    public void Note(Exception error)
    {
        string now = Key(error);
        var at = clock();
        cameAt = at;
        if (now != key)
        {
            SayUnsaid();
            key = now;
            saidAt = at;
            write($"{what}: {error}");
            return;
        }
        unsaid++;
        if (at - saidAt < SayAgainAfter && at >= saidAt) return;
        write($"{what} again, {unsaid} {(unsaid == 1 ? "time" : "times")} since the last line: {error.GetType().FullName}: {error.Message}");
        unsaid = 0;
        saidAt = at;
    }

    // A try worked. Once no error has come for QuietFor, the errors have stopped: the count so far
    // goes in the log, and the next error is written in full.
    public void Worked()
    {
        if (key is null) return;
        var at = clock();
        if (at - cameAt < QuietFor && at >= cameAt) return;
        SayUnsaid();
        key = null;
    }

    void SayUnsaid()
    {
        if (unsaid > 0) write($"{what}: the same error came {unsaid} more {(unsaid == 1 ? "time" : "times")}");
        unsaid = 0;
    }

    static string Key(Exception error) => error.GetType().FullName + "\n" + error.Message + "\n" + error.StackTrace;
}
