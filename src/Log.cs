namespace SteppedOut;

sealed class RollingLog
{
    readonly object gate = new();
    readonly string path;
    readonly string oldPath;

    public RollingLog(string path, long maxBytes)
    {
        this.path = path;
        oldPath = Path.ChangeExtension(path, ".old.log");
        MaxBytes = maxBytes;
    }

    public long MaxBytes { get; set; }

    public void Write(string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
        lock (gate)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length >= MaxBytes) File.Move(path, oldPath, overwrite: true);
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A log that cannot be written is not worth crashing the tool over.
            }
        }
    }
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
}
