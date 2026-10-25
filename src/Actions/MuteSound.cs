namespace SteppedOut;

// Mutes the speakers while you are away and unmutes them when you are back, unless they were
// already muted when you left.
//
// Windows keeps a device muted through a restart. So while this tool has the sound muted, a small
// file in the config folder names the device, and if the PC restarts, you sign out or the tool dies
// while you are away, the next start finds the file and unmutes that device. A device that would not
// unmute when you came back, because it was switched off or unplugged meanwhile, stays named there
// as well. It is tried again every few seconds while you are at the PC, so it is unmuted soon after
// it is back on, and also when you next leave, when Windows signs out while you are away, and at the
// next start.
sealed class MuteSound
{
    // The file's name, in the settings folder.
    internal const string MarkerName = "muted-by-" + AppInfo.Name;

    static readonly string MarkerPath = Path.Combine(AppInfo.ConfigDir, MarkerName);

    readonly string markerPath;
    readonly Func<Wasapi.MutedOutput?> muteDefaultOutput;
    readonly Func<string, bool> unmuteIfMuted;
    Wasapi.MutedOutput? muted;

    public MuteSound()
        : this(MarkerPath, () => Unblocked(Wasapi.MuteDefaultOutput), device => Unblocked(() => Wasapi.UnmuteIfMuted(device)))
    {
    }

    internal MuteSound(string markerPath, Func<Wasapi.MutedOutput?> muteDefaultOutput, Func<string, bool> unmuteIfMuted)
    {
        this.markerPath = markerPath;
        this.muteDefaultOutput = muteDefaultOutput;
        this.unmuteIfMuted = unmuteIfMuted;
    }

    public string Name => "mute the sound";

    public void Leave(bool locking)
    {
        // Muting again would find the sound already muted and lose track of the first mute.
        if (muted is not null) return;
        // A device that would not unmute last time may be back. This comes before the mute, or it
        // would undo the mute it just made.
        UndoLeftover(markerPath, unmuteIfMuted);
        muted = muteDefaultOutput();
        if (muted is null) return;
        Log.Write("sound muted");
        Mark(muted.DeviceId, stillMuted: true);
    }

    public void Return()
    {
        if (muted is null) return;
        var device = muted;
        muted = null;
        try
        {
            Unmute(device);
        }
        catch (Exception e)
        {
            // The marker still names the device, so it is tried again later.
            Log.Write("could not unmute the sound, will try again: " + e.Message);
            return;
        }
        Mark(device.DeviceId, stillMuted: false);
        Log.Write("sound unmuted");
    }

    // Windows would keep a mute after the session is over, this time's or one that would not undo
    // before.
    public void Ending()
    {
        Return();
        UndoLeftover(markerPath, unmuteIfMuted);
    }

    // When the connection the mute was made on has broken, the device is looked up again by its id.
    // False from that means it is not muted or Windows no longer knows it; nothing is left to undo.
    void Unmute(Wasapi.MutedOutput device)
    {
        try
        {
            device.Unmute();
        }
        catch (Exception)
        {
            unmuteIfMuted(device.DeviceId);
        }
    }

    // Adds the device to the marker or drops it from there, keeping any other device it names.
    void Mark(string device, bool stillMuted)
    {
        if (ReadMarker(markerPath) is not { } devices) return;
        devices.RemoveAll(other => other == device);
        if (stillMuted) devices.Add(device);
        WriteMarker(markerPath, devices);
    }

    // Run once at start, whatever the settings say now: the mute belongs to an earlier run.
    public static void UndoLeftover()
    {
        if (!SessionLock.Blocked) UndoLeftover(MarkerPath, Wasapi.UnmuteIfMuted);
    }

    // Tried every few seconds while you are at the PC, so a device that stays away is not logged
    // each time.
    public static void RetryLeftover()
    {
        if (!SessionLock.Blocked) UndoLeftover(MarkerPath, Wasapi.UnmuteIfMuted, quiet: true);
    }

    // The tests and the screenshot tool run on a PC someone is using, and the speakers are his.
    static T Unblocked<T>(Func<T> call) =>
        SessionLock.Blocked ? throw new InvalidOperationException("Muting is blocked in this process.") : call();

    internal static void UndoLeftover(string markerPath, Func<string, bool> unmuteIfMuted, bool quiet = false)
    {
        if (!File.Exists(markerPath) || ReadMarker(markerPath) is not { } devices) return;
        var stillMuted = new List<string>();
        foreach (string device in devices)
        {
            try
            {
                if (unmuteIfMuted(device)) Log.Write("sound unmuted, left muted earlier");
            }
            catch (Exception e)
            {
                // It stays in the file, so the next try unmutes it.
                if (!quiet) Log.Write("could not unmute the sound left muted earlier: " + e.Message);
                stillMuted.Add(device);
            }
        }
        // Left alone when nothing changed, or the retries would write it every few seconds.
        if (stillMuted.Count < devices.Count || devices.Count == 0) WriteMarker(markerPath, stillMuted);
    }

    // One device per line; an earlier version wrote a single id with no line end. Null when the file
    // is there but cannot be read, so it is left as it is.
    static List<string>? ReadMarker(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            return [.. File.ReadAllLines(path).Select(line => line.Trim()).Where(line => line.Length > 0)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write("could not read " + path + ": " + e.Message);
            return null;
        }
    }

    // The file goes once it names no device.
    internal static void WriteMarker(string path, List<string> devices)
    {
        if (devices.Count == 0)
        {
            DeleteMarker(path);
            return;
        }
        try
        {
            File.WriteAllLines(path, devices);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write("could not note the mute in " + path + ": " + e.Message);
        }
    }

    static void DeleteMarker(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write("could not delete " + path + ": " + e.Message);
        }
    }
}
