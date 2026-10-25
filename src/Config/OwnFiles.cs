using System.Text.RegularExpressions;

namespace SteppedOut;

// The files SteppedOut itself writes in its settings folder, for "Remove SteppedOut's files": the
// settings (config.json), the copy kept of one that did not load (.broken), the copies kept of a
// file from before 1.0 (.before-1.0, then with the date and time), today's counts (today.json), the
// log and its older half, the note of a mute still to undo, and the file a save writes first (~ and
// .tmp). Each is matched by its whole name; nothing else in the folder is touched, nothing in a
// folder inside it, and the folder itself goes only when that leaves it empty.
static partial class OwnFiles
{
    static readonly string Settings = Path.GetFileName(AppInfo.ConfigPath);
    static readonly string Backup = Settings + FreshStart.BackupSuffix;
    static readonly string LogName = Path.GetFileName(AppInfo.LogPath);

    // By their whole names.
    public static IReadOnlyList<string> Names { get; } =
    [
        Settings,
        Settings + ".broken",
        Backup,
        DailyStats.FileName,
        LogName,
        Path.GetFileName(RollingLog.OldPath(AppInfo.LogPath)),
        MuteSound.MarkerName,
    ];

    // A backup with the date and time in its name, as FreshStart names a second one: the date, the
    // time, then -2 to -99 when that one was taken too.
    [GeneratedRegex(@"^config\.json\.before-1\.0\.\d{8}-\d{4}(-\d{1,2})?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StampedBackup();

    // Whether SteppedOut wrote a file of this name in its folder.
    public static bool IsOwn(string name)
    {
        // The file a save writes first, beside the one it replaces (WholeFile.Temporary).
        if (name.StartsWith('~') && name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            string saved = name[1..^4];
            return saved.Equals(Settings, StringComparison.OrdinalIgnoreCase) || IsBackup(saved);
        }
        return Names.Contains(name, StringComparer.OrdinalIgnoreCase) || IsBackup(name);
    }

    static bool IsBackup(string name) => name.Equals(Backup, StringComparison.OrdinalIgnoreCase) || StampedBackup().IsMatch(name);

    // The folder the app keeps its files in. The tests and the screenshot tool, on a PC someone is
    // using, never remove anything there. A test that proves it points this at a folder of its own,
    // so even that test never names the real one.
    internal static Func<string> RealFolder { get; set; } = () => AppInfo.ConfigDir;

    internal static bool IsTheRealFolder(string folder) => string.Equals(Full(folder), Full(RealFolder()), StringComparison.OrdinalIgnoreCase);

    // While SessionLock.Blocked is set (the tests and the screenshot tool, on a PC someone is using),
    // only a folder inside the temp folder is ever emptied, where their own folders are, and never
    // the app's own: any other folder, or another way of writing the real one, is refused.
    internal static bool MayRemove(string folder)
    {
        if (!SessionLock.Blocked) return true;
        string full = Full(folder);
        return !IsTheRealFolder(full) && full.StartsWith(Full(Path.GetTempPath()) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    static string Full(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

    // SteppedOut's own files in folder, as they are now.
    public static List<string> In(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        return new DirectoryInfo(folder).EnumerateFiles().Where(file => IsOwn(file.Name)).Select(file => file.Name).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Deletes SteppedOut's own files in folder, then the folder when nothing else is left in it.
    // Gives back the names it deleted; failed tells why one could not be.
    public static List<string> Remove(string folder, Action<string> failed)
    {
        if (!MayRemove(folder)) throw new InvalidOperationException("Removing SteppedOut's files is blocked in this process.");
        var removed = new List<string>();
        foreach (string name in In(folder))
        {
            try
            {
                File.Delete(Path.Combine(folder, name));
                removed.Add(name);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failed($"could not delete {name}: {e.Message}");
            }
        }
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            failed($"could not delete the folder: {e.Message}");
        }
        return removed;
    }

    // A dry run: says what Remove would delete, and deletes nothing.
    public static void WouldRemove(string folder, Action<string> log)
    {
        foreach (string name in In(folder)) log($"WOULD delete {name} (dry run)");
    }
}
