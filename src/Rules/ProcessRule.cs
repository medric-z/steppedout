using System.Diagnostics;

namespace SteppedOut;

// Finding programs by name, for the app rules: "obs64", "obs64.exe" and a full path all name the
// same program.
static class ProcessRule
{
    internal static readonly int MySession = Process.GetCurrentProcess().SessionId;

    // Every name in names that a process in this session runs under, each once. The list covers
    // the whole machine: services, and other people still signed in behind a switch of user. Only a
    // program in this session says the person here is busy.
    public static HashSet<string> Running(IEnumerable<RunningProcess> processes, IReadOnlySet<string> names)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0) return found;
        foreach (var process in processes)
        {
            if (process.Session == MySession && names.Contains(process.Name)) found.Add(process.Name);
        }
        return found;
    }

    // Accepts "obs64", "obs64.exe" or a full path, since people paste all three. Explorer's
    // "Copy as path" puts the path in quotes.
    internal static string Normalize(string name)
    {
        string file = Path.GetFileName(name.Trim().Trim('"').Trim());
        return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
    }
}
