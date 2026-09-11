using System.Diagnostics;

namespace SteppedOut;

// Never lock while one of the named programs is running: a render, a recording, a long call.
sealed class ProcessRule : ILockSuppressor
{
    static readonly TimeSpan ScanEvery = TimeSpan.FromSeconds(5);
    readonly HashSet<string> names;
    DateTime scannedAt = DateTime.MinValue;
    string? running;

    public ProcessRule(IEnumerable<string> processNames)
    {
        names = new HashSet<string>(processNames.Select(Normalize).Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase);
    }

    public string Name => "processes";

    public string? Check()
    {
        if (names.Count == 0) return null;

        // Listing every process costs a few milliseconds, so it is not done on every tick.
        if (DateTime.Now - scannedAt >= ScanEvery)
        {
            scannedAt = DateTime.Now;
            running = null;
            var processes = Process.GetProcesses();
            try
            {
                foreach (var process in processes)
                {
                    if (names.Contains(process.ProcessName))
                    {
                        running = process.ProcessName;
                        break;
                    }
                }
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }

        return running is null ? null : "process running: " + running;
    }

    // Accepts "obs64", "obs64.exe" or a full path, since people paste all three.
    internal static string Normalize(string name)
    {
        string file = Path.GetFileName(name.Trim());
        return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
    }
}
