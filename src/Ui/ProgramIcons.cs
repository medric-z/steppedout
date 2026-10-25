using System.Runtime.InteropServices;

namespace SteppedOut;

// A program's own icon, for the app rules on the Locking tab. Read away from the window, as its name
// is (ProgramNames), so a program on a sleeping drive never holds the window up: a list waits a
// moment for its icons together, a line whose icon is still being read shows the apps glyph, and
// the icon goes in when it comes. Only a file on this PC is read (LocalPaths). Each file is read
// once for each size, and kept.
sealed class ProgramIcons(Func<string, int, Bitmap?> read)
{
    public static readonly ProgramIcons Shared = new(ProcessInfo.Icon);

    const int Kept = 200;
    readonly Dictionary<string, Task<Bitmap?>> reads = new(StringComparer.OrdinalIgnoreCase);

    // Starts reading each icon not read yet, and waits at most wait for them all.
    public void Wait(IEnumerable<string?> paths, int size, TimeSpan wait)
    {
        var pending = paths.Select(path => Read(path, size)).OfType<Task<Bitmap?>>().Where(read => !read.IsCompleted).ToArray();
        if (pending.Length == 0) return;
        try
        {
            Task.WaitAll(pending, wait < TimeSpan.Zero ? TimeSpan.Zero : wait);
        }
        catch (AggregateException)
        {
            // A read that failed gives no icon.
        }
    }

    // The program's icon at size × size pixels once its file is read, as a copy the caller disposes;
    // null for a file that has none and for anything that is not a full path on this PC. While the
    // file is still being read, null too, and later is handed a copy on the control's thread once it
    // is read, if it has an icon.
    public Bitmap? Icon(string? path, int size, Control owner, Action<Bitmap> later)
    {
        if (Read(path, size) is not { } read) return null;
        if (read.IsCompleted) return Copy(read);
        ProgramNames.Then(read, owner, _ =>
        {
            if (Copy(read) is { } image) later(image);
        });
        return null;
    }

    Task<Bitmap?>? Read(string? path, int size)
    {
        if (string.IsNullOrWhiteSpace(path) || size <= 0 || size > 256) return null;
        string file = path.Trim().Trim('"');
        if (!LocalPaths.IsFullPathOnThisPC(file)) return null;
        string key = size + "|" + file;
        lock (reads)
        {
            if (reads.TryGetValue(key, out var kept)) return kept;
            if (reads.Count >= Kept) ForgetLocked();
            var task = Task.Run(() => Safely(file, size));
            reads[key] = task;
            return task;
        }
    }

    Bitmap? Safely(string file, int size)
    {
        try
        {
            return read(file, size);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }

    Bitmap? Copy(Task<Bitmap?> read)
    {
        if (!read.IsCompletedSuccessfully || read.Result is not { } image) return null;
        lock (reads)
        {
            try
            {
                return new Bitmap(image);
            }
            catch (Exception e) when (e is ArgumentException or ExternalException or OutOfMemoryException)
            {
                // Forgotten meanwhile: the glyph stays.
                return null;
            }
        }
    }

    void ForgetLocked()
    {
        foreach (var read in reads.Values)
        {
            if (read.IsCompletedSuccessfully) read.Result?.Dispose();
        }
        reads.Clear();
    }
}
