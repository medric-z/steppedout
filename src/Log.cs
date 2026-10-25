namespace SteppedOut;

sealed class RollingLog
{
    readonly object gate = new();
    readonly string path;
    readonly string oldPath;
    bool closed;

    public RollingLog(string path, long maxBytes)
    {
        this.path = path;
        oldPath = OldPath(path);
        MaxBytes = maxBytes;
    }

    public long MaxBytes { get; set; }

    public void Write(string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
        lock (gate)
        {
            if (closed) return;
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length >= MaxBytes) File.Move(path, oldPath, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A viewer holding the log open can block the move. The log then grows past its
                // limit until a later write can move it, which is better than losing lines.
            }
            try
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A log that cannot be written is not worth crashing the tool over.
            }
        }
    }

    // Nothing more is written, not even by a write that was waiting for the one before it.
    public void Close()
    {
        lock (gate) closed = true;
    }

    // The older half of a log at path: SteppedOut.old.log beside SteppedOut.log.
    public static string OldPath(string path) => Path.ChangeExtension(path, ".old.log");
}

static class Log
{
    static RollingLog? current;

    public static void Open(string path, long maxBytes) => current = new RollingLog(path, maxBytes);

    public static void Write(string message) => current?.Write(message);

    public static void SetLimit(long maxBytes)
    {
        if (current is not null) current.MaxBytes = maxBytes;
    }

    // Stops the log for good, for "Remove SteppedOut's files", which deletes it next: every write
    // after this, from any thread, is dropped.
    public static void Close()
    {
        var log = current;
        current = null;
        log?.Close();
    }
}
