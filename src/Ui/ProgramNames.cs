using System.Diagnostics;
using System.Security;

namespace SteppedOut;

// The name a program gives itself, such as "Paint" for mspaint.exe or "Google Chrome" for
// chrome.exe: the description in its program file, when it gives a short one. The app rules on the
// Locking tab and the app chips on the Away and back tab show apps by it.
// Each file is read once, away from the window, and kept, so a program on a network share or a
// sleeping drive never holds the window up: a list waits a moment for all its names together, shows
// any still being read by the names the rules match on, and puts each one in when it comes.
sealed class ProgramNames(Func<string, string?> read)
{
    public static readonly ProgramNames Shared = new(Describe);

    // How long a list waits for its names, all together, before it shows.
    public static readonly TimeSpan Moment = TimeSpan.FromMilliseconds(100);

    const int Kept = 500;
    readonly Dictionary<string, Task<string?>> reads = new(StringComparer.OrdinalIgnoreCase);

    // Starts reading each program file not read yet, and waits at most wait for them all.
    public void Wait(IEnumerable<string?> paths, TimeSpan wait)
    {
        var pending = paths.Select(Read).OfType<Task<string?>>().Where(read => !read.IsCompleted).ToArray();
        if (pending.Length == 0) return;
        try
        {
            Task.WaitAll(pending, wait);
        }
        catch (AggregateException)
        {
            // A read that failed gives no name.
        }
    }

    // The program's own name once its file is read, or null: for a file that gives none and for
    // anything that is not a full path. While the file is still being read, null too, and later is
    // called with the name on the control's thread once it is read, if it gives one.
    public string? Name(string? path, Control owner, Action<string> later)
    {
        if (Read(path) is not { } read) return null;
        if (read.IsCompleted) return read.IsCompletedSuccessfully ? read.Result : null;
        Then(read, owner, name =>
        {
            if (name is not null) later(name);
        });
        return null;
    }

    // Runs what a finished task leads to on the control's thread: through that thread's window
    // message loop, or through the control once it has a window. Nothing runs after the control has
    // gone, or when the task failed.
    internal static void Then<T>(Task<T> task, Control owner, Action<T> then)
    {
        var loop = SynchronizationContext.Current as WindowsFormsSynchronizationContext;
        task.ContinueWith(done =>
        {
            if (!done.IsCompletedSuccessfully) return;
            void Run()
            {
                if (!owner.IsDisposed) then(done.Result);
            }
            try
            {
                if (loop is not null) loop.Post(_ => Run(), null);
                else if (owner.IsHandleCreated) owner.BeginInvoke(Run);
            }
            catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
            {
                // The window has gone: nothing to show the name in.
            }
        }, TaskScheduler.Default);
    }

    Task<string?>? Read(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string file = path.Trim().Trim('"');
        if (!FullPath(file)) return null;
        lock (reads)
        {
            if (reads.TryGetValue(file, out var kept)) return kept;
            if (reads.Count >= Kept) reads.Clear();
            var read = Task.Run(() => Safely(file));
            reads[file] = read;
            return read;
        }
    }

    string? Safely(string file)
    {
        try
        {
            return read(file);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }

    static bool FullPath(string file)
    {
        try
        {
            return Path.IsPathFullyQualified(file);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // The description in the program file, when it is short enough to be a name.
    internal static string? Describe(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string? described = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
            return !string.IsNullOrEmpty(described) && described.Length <= 60 ? described : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException)
        {
            return null;
        }
    }
}
