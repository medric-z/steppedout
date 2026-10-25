using System.Globalization;

namespace SteppedOut;

// What start-up did about a config.json from before 1.0.
enum FreshOutcome
{
    // A 1.0 file, or none: nothing to do.
    NotNeeded,
    // --once, or a second start while another copy runs: the defaults for this run, the file as it is.
    InMemory,
    // Kept next to it, and a fresh file with the defaults written in its place.
    Written,
    // It could not be kept, or it changed on disk since it was read: left as it is.
    NotKept,
    // It could not be written over: left as it is, or kept already.
    NotWritten,
}

// From is where the file came from (0.1, 0.x, ...), Backup the name of the copy kept of it, and Text
// what was written.
sealed record FreshStartResult(FreshOutcome Outcome, int Version, string? From = null, string? Backup = null, bool BackupWasThere = false,
    string? Text = null, string? Problem = null)
{
    // The one line for the log at start-up.
    public string LogLine => Outcome switch
    {
        FreshOutcome.NotNeeded => $"config version {Version}",
        FreshOutcome.InMemory => $"config.json is from before 1.0 ({From}): this run uses the default settings and leaves the file as it is",
        FreshOutcome.Written => $"config.json was from before 1.0 ({From}): kept it as {Backup}{(BackupWasThere ? ", which was already there" : "")}; "
            + "the settings start fresh with the defaults and the welcome",
        FreshOutcome.NotKept => $"config.json is from before 1.0 ({From}) and could not be kept ({Problem}), so it is left as it is; this run uses the default settings",
        _ => $"config.json is from before 1.0 ({From}) and could not be replaced ({Problem}); "
            + (Backup is null ? "it is left as it is" : $"it is kept as {Backup}") + "; this run uses the default settings",
    };

    // The line --once prints about it, or null.
    public string? OnceLine => Outcome == FreshOutcome.InMemory
        ? $"from before 1.0 ({From}), so this run uses the default settings; the file is left as it is"
        : null;
}

// Nothing carries over from a settings file from before 1.0 (D50): Config.Parse reads none of it and
// gives the defaults, with the welcome to come. The copy that runs keeps the old file next to it, as
// config.json.before-1.0, then writes a fresh file in its place. No backup is ever written over, and
// a file that cannot be kept is never written over either.
static class FreshStart
{
    public const string BackupSuffix = ".before-1.0";

    // Only the copy that runs, the one that owns the instance, writes. --once and a second start
    // use the defaults in memory, so they never change the file under the running copy.
    public static bool SavesAtStart(Options options, bool ownsInstance) => ownsInstance && !options.Once;

    // config is what Config.Load gave. With persist, a file from before 1.0 is kept and a fresh one
    // written; a file that cannot be kept or written still gives the defaults, and the result says
    // why for the log.
    public static FreshStartResult Apply(Config config, string path, bool persist) => Apply(config, path, persist, DateTime.Now);

    internal static FreshStartResult Apply(Config config, string path, bool persist, DateTime now)
    {
        if (config.Old is not { } old) return new(FreshOutcome.NotNeeded, config.ConfigVersion);
        return persist ? Write(config, old, path, now) : new(FreshOutcome.InMemory, config.ConfigVersion, old.Shape);
    }

    static FreshStartResult Write(Config config, OldFile old, string path, DateTime now)
    {
        bool writable = false;
        string? backup = null;
        bool wasThere = false;
        try
        {
            byte[] original = ReadBytes(path);
            // Changed since it was read: the next save keeps that newer file first, if it is old too.
            if (Decode(original) != old.Text)
            {
                return new(FreshOutcome.NotKept, config.ConfigVersion, old.Shape, Problem: "it changed on disk since it was read");
            }
            // A file that cannot be written is found out before the backup, so it does not get a
            // backup it never needed.
            using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { }
            writable = true;
            (backup, wasThere) = KeepOriginal(path, original, now);
            string text = config.Write(path);
            return new(FreshOutcome.Written, config.ConfigVersion, old.Shape, backup, wasThere, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            var outcome = writable && backup is null ? FreshOutcome.NotKept : FreshOutcome.NotWritten;
            return new(outcome, config.ConfigVersion, old.Shape, backup, wasThere, Problem: Config.Reason(e));
        }
    }

    // Before any save over config.json: a file from before 1.0 there, which an older copy can write
    // back at any time, is kept next to it first, by the same names as at start-up, so it is never
    // written over unkept. Gives the backup's name, or null when the file there is not from before
    // 1.0. Throws when it cannot be kept, so the save does not happen.
    internal static string? KeepBeforeSave(string path) => KeepBeforeSave(path, DateTime.Now);

    internal static string? KeepBeforeSave(string path, DateTime now)
    {
        byte[] bytes;
        try
        {
            bytes = ReadBytes(path);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        if (Config.OldShape(Decode(bytes)) is null) return null;
        try
        {
            var (name, wasThere) = KeepOriginal(path, bytes, now);
            if (!wasThere) Log.Write($"kept the config from before 1.0 as {name} before saving over it");
            return name;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new IOException("the config from before 1.0 could not be kept, so it was not saved over: " + Config.Reason(e), e);
        }
    }

    // As Config.Read shares the file, so another program that has it open does not stop the read.
    static byte[] ReadBytes(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var bytes = new MemoryStream();
        file.CopyTo(bytes);
        return bytes.ToArray();
    }

    // The text as Config.Read gives it, so the two can be compared.
    static string Decode(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes));
        return reader.ReadToEnd();
    }

    // Keeps the file from before 1.0 next to it: config.json.before-1.0 the first time, and a copy
    // with the date and time in its name when that one is already there (an older copy wrote the
    // file again), so no backup is ever written over. A backup already there with exactly these
    // bytes is the same file, so it is not made twice.
    static (string Name, bool WasThere) KeepOriginal(string path, byte[] original, DateTime now)
    {
        string first = path + BackupSuffix;
        foreach (string existing in Directory.EnumerateFiles(Path.GetDirectoryName(path)!, Path.GetFileName(first) + "*"))
        {
            bool backup = existing.Equals(first, StringComparison.OrdinalIgnoreCase)
                || existing.StartsWith(first + ".", StringComparison.OrdinalIgnoreCase);
            if (backup && SameBytes(existing, original)) return (Path.GetFileName(existing), true);
        }
        foreach (string name in BackupNames(first, now))
        {
            try
            {
                // Whole or not at all, so a failed copy never leaves a cut-off file under a backup's
                // name, where it would stand for the original.
                WholeFile.CreateNew(name, original);
                return (Path.GetFileName(name), false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && (File.Exists(name) || Directory.Exists(name)))
            {
                // Taken, by a file or a folder: the next name.
            }
        }
        throw new IOException($"no free name for a backup next to {path}");
    }

    static IEnumerable<string> BackupNames(string first, DateTime now)
    {
        yield return first;
        string stamped = first + "." + now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        yield return stamped;
        for (int n = 2; n <= 99; n++) yield return $"{stamped}-{n}";
    }

    static bool SameBytes(string file, byte[] bytes)
    {
        try
        {
            return new FileInfo(file).Length == bytes.Length && ReadBytes(file).AsSpan().SequenceEqual(bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
