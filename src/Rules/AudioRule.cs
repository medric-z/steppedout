using System.Runtime.InteropServices;

namespace SteppedOut;

// "Something is producing sound" is the best admin-free proxy for "a video is playing". The cleaner
// signal would be the power requests that players register (the ones powercfg /requests lists),
// but reading those needs administrator rights, which this tool refuses to ask for. The cost of
// the proxy: background music counts too, so leaving a player running changes the lock time
// unless "music or video" is set to change nothing.
//
// The app on a call makes sound too. That sound is the call, not music (D32): it is matched to the
// call app by its process, never by its name, and left out before the loudest is taken, so music
// from another app during a call still counts.
sealed class AudioRule : Rule
{
    static readonly TimeSpan KeepName = TimeSpan.FromSeconds(10);
    // How far up the process tree a sound's process is followed to find the call app: a call app
    // that plays its sound from a helper process, such as a browser engine, is found through it.
    const int ParentSteps = 3;
    float threshold;
    TimeSpan silenceHold;
    TimeSpan? lastHeard;
    string lastProcess = "";
    TimeSpan lastProcessLoudest;

    public AudioRule(Effect effect, double peakThreshold, int silenceSeconds)
    {
        Set(effect, peakThreshold, silenceSeconds);
    }

    public override string Name => "audio";

    public Effect Effect { get; private set; }

    // What it heard so far stays: it is a fact about the PC, not about the settings.
    public void Set(Effect effect, double peakThreshold, int silenceSeconds)
    {
        Effect = effect;
        threshold = (float)peakThreshold;
        silenceHold = TimeSpan.FromSeconds(Math.Max(0, silenceSeconds));
    }

    public override void Check(Scene scene, List<WhyLine> lines)
    {
        IReadOnlyList<Wasapi.Session> sessions;
        try
        {
            sessions = scene.Sounds(threshold);
        }
        catch (Exception e) when (e is COMException or TimeoutException)
        {
            // Sound heard a moment ago still counts when Windows cannot be asked this second, or the
            // audio service does not answer in time. Past the hold the error goes on, so one that
            // lasts is logged.
            if (Heard(null, scene.Now) is string held)
            {
                lines.Add(new WhyLine(Signal.Sound, Effect, held));
                return;
            }
            throw;
        }

        var calls = sessions.Count > 0 ? CallsNow(scene) : Array.Empty<CallApp>();
        var callSounds = new List<string>();
        Wasapi.Session? loudest = null;
        foreach (var session in sessions)
        {
            if (calls.Count > 0 && IsCallSound(scene, session.ProcessId, calls))
            {
                if (!callSounds.Contains(session.Process, StringComparer.OrdinalIgnoreCase)) callSounds.Add(session.Process);
                continue;
            }
            if (loudest is not Wasapi.Session l || session.Peak > l.Peak) loudest = session;
        }

        // Seen but not counted, so the Why window can say the sound counts as the call.
        foreach (string process in callSounds) lines.Add(new WhyLine(Signal.Sound, Effect, process, Counts: false));
        if (Heard(loudest?.Process, scene.Now) is string heard) lines.Add(new WhyLine(Signal.Sound, Effect, heard));
    }

    // The call records can fail on their own; the call rule reports that, and music still counts.
    static IReadOnlyList<CallApp> CallsNow(Scene scene)
    {
        try
        {
            return scene.Calls;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return Array.Empty<CallApp>();
        }
    }

    // The sound comes from the call app: its program file is the one the call's record names, or it
    // belongs to the call app's Store package, or a parent of its process does.
    internal static bool IsCallSound(Scene scene, int processId, IReadOnlyList<CallApp> calls)
    {
        bool anyPath = calls.Any(c => c.ExePath is not null);
        bool anyFamily = calls.Any(c => c.Family is not null);
        int id = processId;
        for (int step = 0; step <= ParentSteps && id > 4; step++)
        {
            if (anyPath && scene.Read.ImagePath(id) is string path
                && calls.Any(c => string.Equals(c.ExePath, path, StringComparison.OrdinalIgnoreCase))) return true;
            if (anyFamily && scene.Read.PackageFamily(id) is string family
                && calls.Any(c => string.Equals(c.Family, family, StringComparison.OrdinalIgnoreCase))) return true;
            if (step == ParentSteps) break;
            IReadOnlyDictionary<int, int> parents;
            try
            {
                parents = scene.Parents;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                return false;
            }
            if (!parents.TryGetValue(id, out int parent) || parent == id) break;
            id = parent;
        }
        return false;
    }

    // process is the loudest program right now, or null when nothing counts. Returns the name to
    // show while sound counts as playing, or null.
    internal string? Heard(string? process, TimeSpan now)
    {
        if (process is not null)
        {
            // Two programs at about the same level take turns being the loudest, and naming whichever
            // won this second would log a new reason every second. The name only moves on once the
            // one named has not been the loudest for a few seconds.
            if (process == lastProcess || lastHeard is null || now - lastProcessLoudest >= KeepName)
            {
                lastProcess = process;
                lastProcessLoudest = now;
            }
            lastHeard = now;
            return lastProcess;
        }

        // Quiet scenes and gaps between tracks should not read as "the video ended", so sound
        // counts as playing until it has been quiet for the whole hold.
        return lastHeard is TimeSpan heard && now - heard < silenceHold ? lastProcess : null;
    }
}
