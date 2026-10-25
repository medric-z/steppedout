using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SteppedOut;

// Starts programs, so the tests can see what would be started instead.
interface IStarter
{
    // Throws Win32Exception or InvalidOperationException when Windows cannot start it.
    void Start(ProcessStartInfo start);

    // Why this starter starts nothing at all, or null: what a dry run says for each start.
    string? Refuses => null;
}

// The one way the app starts a program. While SessionLock.Blocked is set (the tests and the
// screenshot tool, on a PC someone is using), it throws instead, as a lock request does, so none of
// the person's programs is started even if a test reaches it by mistake. The one start let through
// then is a program a test built for itself, to see a real start work (MadeForATest).
sealed class ProcessStarter : IStarter
{
    // A shell start from the away actions' thread is moved to a single-threaded apartment thread by
    // .NET itself, as the shell wants.
    public void Start(ProcessStartInfo start)
    {
        if (SessionLock.Blocked && !MadeForATest(start.FileName)) throw new InvalidOperationException("Starting programs is blocked in this process.");
        Process.Start(start)?.Dispose();
    }

    // Only where a test builds its own program and copies it: %TEMP%\so<32 hex>\out\so<32 hex>.exe,
    // by its full path. Any other program in the temp folder is refused, as the test host is when the
    // code is kept there, so a test that hands the test host to the real starter starts nothing. The
    // same check lets the real process handle end such a program, and only such a program, while
    // blocked (Win32ProcessApi).
    internal static bool MadeForATest(string file)
    {
        try
        {
            if (!Path.IsPathFullyQualified(file)) return false;
            string full = Path.GetFullPath(file);
            string? output = Path.GetDirectoryName(full);
            string? own = output is null ? null : Path.GetDirectoryName(output);
            string? temp = own is null ? null : Path.GetDirectoryName(own);
            if (output is null || own is null || temp is null) return false;
            return Path.TrimEndingDirectorySeparator(LongForm(temp)).Equals(Path.TrimEndingDirectorySeparator(LongForm(Path.GetFullPath(Path.GetTempPath()))), StringComparison.OrdinalIgnoreCase)
                && OwnName(Path.GetFileName(own))
                && Path.GetFileName(output).Equals("out", StringComparison.OrdinalIgnoreCase)
                && Path.GetExtension(full).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                && OwnName(Path.GetFileNameWithoutExtension(full));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or System.Security.SecurityException or PathTooLongException)
        {
            return false;
        }
    }

    // "so" and a GUID's 32 hex digits, the name a test gives its own folder and program.
    static bool OwnName(string name) => name.Length == 34 && name.StartsWith("so", StringComparison.Ordinal) && Guid.TryParseExact(name.AsSpan(2), "N", out _);

    // A folder by its full names. The temp folder may be set by its short 8.3 names (C:\Users\RUNNER~1\...)
    // while Windows gives a running program's path in full, or the other way round. A folder that is
    // not there stays as it was given.
    static string LongForm(string folder)
    {
        var buffer = new char[1024];
        uint length = GetLongPathName(folder, buffer, (uint)buffer.Length);
        return length > 0 && length < buffer.Length ? new string(buffer, 0, (int)length) : folder;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetLongPathNameW")]
    static extern uint GetLongPathName(string shortPath, [Out] char[] longPath, uint length);
}

// Runs the script you chose when you leave or when you are back. Arguments go in a shortcut (.lnk),
// which also lets a script run minimized. A PowerShell script runs with no window.
sealed class RunProgram
{
    readonly string path;
    readonly bool whenAway;
    readonly IStarter starter;
    readonly Action<string> log;
    readonly Func<char, DriveType> drive;

    // drive tells what is behind a drive letter (a mapped network drive, none); the tests give their own.
    public RunProgram(string path, bool whenAway, IStarter starter, Action<string> log, Func<char, DriveType>? drive = null)
    {
        this.path = path;
        this.whenAway = whenAway;
        this.starter = starter;
        this.log = log;
        this.drive = drive ?? LocalPaths.DriveTypeOf;
    }

    public string Name => $"run {path} {Side}";

    string Side => whenAway ? "when away" : "when back";

    // What a dry run says in place of Start: the run Start would try, or that it would not, with
    // the reason, in the order Start finds it, so a dry run never promises a run the live one does
    // not make. A file that is not there is said as such whatever its kind; the live run then has
    // Windows' own words for it.
    public string DryRun()
    {
        bool ps1 = Path.GetExtension(path).Equals(".ps1", StringComparison.OrdinalIgnoreCase);
        string? problem = Refusal(path, drive)
            ?? (ps1 && !File.Exists(path) ? "the file is not there" : null)
            ?? starter.Refuses
            ?? (!File.Exists(path) ? "the file is not there" : null);
        return problem is null ? Name : $"NOT run {path} {Side}: {problem}";
    }

    public void Start()
    {
        if (Refusal(path, drive) is { } problem)
        {
            log($"did not run {path}: {problem}");
            return;
        }
        var start = StartInfo(path);
        // PowerShell starts whether or not the script is there, and its error would go unseen.
        if (!start.UseShellExecute && !File.Exists(path))
        {
            log($"could not start {path}: the file is not there");
            return;
        }
        try
        {
            starter.Start(start);
            log("started " + path);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            log($"could not start {path}: {e.Message}");
        }
    }

    // What Choose... offers (scripts and shortcuts), and a program, which earlier versions let you pick.
    static readonly string[] Kinds = [".bat", ".cmd", ".ps1", ".lnk", ".exe"];

    // Why a path from the settings file is not run, or null. Only a file on this PC, by its full
    // path, of a kind the row offers: never a web address, a bare command that depends on where
    // SteppedOut was started, or a file on another PC, by a network path or on a mapped network
    // drive, which would reach out to it, nor one on a drive letter that is not connected now.
    // Choose... does not keep a script from another PC either (AwayTab).
    internal static string? Refusal(string path) => Refusal(path, LocalPaths.DriveTypeOf);

    internal static string? Refusal(string path, Func<char, DriveType> drive)
    {
        if (path.Length == 0 || path.Any(char.IsControl) || path.Contains('"')) return "it is not a file";
        var place = LocalPaths.Place(path, drive);
        if (LocalPaths.Elsewhere(path, place) is { } elsewhere) return elsewhere + ", and only a file on this PC runs here";
        if (place != PathPlace.ThisPC) return "only a file on this PC, by its full path, runs here";
        if (!Kinds.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
        {
            return "only scripts (.bat, .cmd, .ps1), shortcuts (.lnk) and programs (.exe) run here";
        }
        return null;
    }

    internal static ProcessStartInfo StartInfo(string path) => StartInfo(path, PowerShellPolicy.Effective);

    // Opening a PowerShell script edits it in Notepad, by Windows' design, so a .ps1 goes to
    // PowerShell itself, with no window. The execution policy is set aside for that run, as
    // Explorer's "Run with PowerShell" does, except when it is AllSigned: someone who chose to run
    // only signed scripts keeps that. One set by Group Policy applies whatever the command line says.
    // -NonInteractive keeps a script that asks a question from waiting where nobody can answer.
    internal static ProcessStartInfo StartInfo(string path, Func<string?> policy)
    {
        if (!Path.GetExtension(path).Equals(".ps1", StringComparison.OrdinalIgnoreCase)) return new ProcessStartInfo(path) { UseShellExecute = true };
        bool allSigned = string.Equals(policy(), "AllSigned", StringComparison.OrdinalIgnoreCase);
        return new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            Arguments = $"-NoProfile -NonInteractive{(allSigned ? "" : " -ExecutionPolicy Bypass")} -File \"{path}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(path) ?? "",
        };
    }
}

// The execution policy Windows PowerShell would use for a script started with no policy of its
// own: the first one set, from Group Policy for the PC and for the user, then the user's own, then
// the PC's. Null when none is set (Restricted on Windows, RemoteSigned on Windows Server).
static class PowerShellPolicy
{
    const string Policies = @"SOFTWARE\Policies\Microsoft\Windows\PowerShell";
    const string ShellIds = @"SOFTWARE\Microsoft\PowerShell\1\ShellIds\Microsoft.PowerShell";

    public static string? Effective() => Effective(Read);

    internal static string? Effective(Func<RegistryHive, string, string?> read)
    {
        foreach (var (hive, key) in new[] { (RegistryHive.LocalMachine, Policies), (RegistryHive.CurrentUser, Policies), (RegistryHive.CurrentUser, ShellIds), (RegistryHive.LocalMachine, ShellIds) })
        {
            string? policy = read(hive, key);
            if (!string.IsNullOrWhiteSpace(policy) && !policy.Trim().Equals("Undefined", StringComparison.OrdinalIgnoreCase)) return policy.Trim();
        }
        return null;
    }

    static string? Read(RegistryHive hive, string key)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var found = root.OpenSubKey(key);
            return found?.GetValue("ExecutionPolicy") as string;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }
}
