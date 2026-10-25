using Microsoft.Win32;

namespace SteppedOut;

// Start with Windows. The normal exe uses the one registry entry this tool ever changes:
// HKCU\...\Run\SteppedOut; setting it also clears the off flag Windows may keep for it. The Store
// package can't use that entry, because Windows keeps a package's registry writes to itself, so it
// uses its startup task instead (Packaged.StartupTask). Read, Choose and PointsAtMissingFile
// answer for both; the members with a key path are the Run entry's.
static class Autostart
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // Set by Program for a --dry-run copy. Start with Windows is then only logged, never changed: the
    // entry such a copy wrote would have no --dry-run, so the next sign-in would start a copy that
    // really locks.
    public static bool DryRun { get; set; }

    // Whether this copy starts with Windows, and when the Store package's task is held off or on by
    // Windows, why. For ISettingsHost.StartsWithWindows (IsOn) and the row's reason.
    public static AutostartState Read() => Read(Packaged.StartupTask, RunKey);

    // The person flipped the switch. Gives the state after, which for the Store package can differ
    // from the choice: a task Windows holds off or on stays as it is (CanChange says when).
    public static AutostartState Choose(bool on) =>
        DryRun ? WouldChoose(on, Packaged.StartupTask, RunKey, Log.Write) : Choose(on, Packaged.StartupTask, RunKey);

    // A dry run: says what the switch would change and gives the state as it is, which stays.
    internal static AutostartState WouldChoose(bool on, IStartupTask? task, string keyPath, Action<string> log)
    {
        var now = Read(task, keyPath);
        if (now.CanChange() && now.IsOn() != on) log($"WOULD turn start with Windows {(on ? "on" : "off")} (dry run)");
        return now;
    }

    public static bool IsOn(this AutostartState state) => state is AutostartState.On or AutostartState.OnByPolicy;

    // Whether flipping the switch can change anything. Only the person, in Windows, or the PC's
    // administrator can change the other states.
    public static bool CanChange(this AutostartState state) => state is AutostartState.Off or AutostartState.On;

    // The line under the switch when Windows holds it; null when the switch says it all, which is
    // always the case for the normal exe.
    public static string? Note(this AutostartState state, V1Text text) => state switch
    {
        AutostartState.OffInWindows => text.StartupOffInWindows,
        AutostartState.OffByPolicy => text.StartupOffByPolicy,
        AutostartState.OnByPolicy => text.StartupOnByPolicy,
        _ => null,
    };

    // No task means the normal exe.
    internal static AutostartState Read(IStartupTask? task, string keyPath)
    {
        if (task is null) return IsEnabled(keyPath) ? AutostartState.On : AutostartState.Off;
        try
        {
            return From(task.State());
        }
        catch (Exception e)
        {
            Log.Write("could not read the startup task: " + e.Message);
            return AutostartState.Off;
        }
    }

    internal static AutostartState Choose(bool on, IStartupTask? task, string keyPath)
    {
        if (task is null)
        {
            SetRunEntry(on, keyPath);
            return IsEnabled(keyPath) ? AutostartState.On : AutostartState.Off;
        }
        try
        {
            var before = From(task.State());
            if (!before.CanChange() || before.IsOn() == on) return before;
            if (on)
            {
                var after = From(task.RequestEnable());
                Log.Write(after.IsOn() ? "start with Windows: on, through the startup task" : $"start with Windows: Windows kept it off ({after})");
                return after;
            }
            task.Disable();
            Log.Write("start with Windows: off");
            return From(task.State());
        }
        catch (Exception e)
        {
            Log.Write("could not change the startup task: " + e.Message);
            return Read(task, keyPath);
        }
    }

    internal static AutostartState From(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled => AutostartState.On,
        StartupTaskState.DisabledByUser => AutostartState.OffInWindows,
        StartupTaskState.DisabledByPolicy => AutostartState.OffByPolicy,
        StartupTaskState.EnabledByPolicy => AutostartState.OnByPolicy,
        _ => AutostartState.Off,
    };

    // What the tray app did before Choose existed, unchanged. Off removes the entry even when another
    // copy wrote it; on points it at this copy.
    static void SetRunEntry(bool on, string keyPath)
    {
        if (on ? IsEnabled(keyPath) : !HasEntry(keyPath)) return;
        try
        {
            if (on)
            {
                Enable(keyPath);
                Log.Write("start with Windows: on, " + Command());
            }
            else
            {
                Disable(keyPath);
                Log.Write("start with Windows: off");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Write("could not change the Run entry: " + e.Message);
        }
    }

    // A package is never pointed at by a Run entry, and one it took over would start nothing. Asked
    // only to take such an entry over, so a dry run says it would and answers no.
    public static bool PointsAtMissingFile() => Packaged.StartupTask is null && TakesOver(RunKey, DryRun, Log.Write);

    internal static bool TakesOver(string keyPath, bool dryRun, Action<string> log)
    {
        if (!PointsAtMissingFile(keyPath)) return false;
        if (!dryRun) return true;
        log("WOULD point start with Windows at this copy: it starts a file that is gone (dry run)");
        return false;
    }

    // On only when the value starts this copy and Windows has not turned it off. One left behind by
    // a copy in another folder reads as off, so turning it on points it here.
    public static bool IsEnabled(string keyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(AppInfo.Name) is string command && Starts(command, Environment.ProcessPath)
            && !TurnedOffInWindows(keyPath);
    }

    // Whether any copy, this one or one in another folder, starts with Windows.
    public static bool HasEntry(string keyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(AppInfo.Name) is string;
    }

    // An entry for a copy that was deleted or moved starts nothing at sign-in. One turned off in
    // Windows is left alone: taking it over would turn it back on.
    public static bool PointsAtMissingFile(string keyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(AppInfo.Name) is string command && !File.Exists(Split(command).Exe)
            && !TurnedOffInWindows(keyPath);
    }

    // Arguments added to the entry by hand, such as --dry-run, stay when it is pointed here. An off
    // flag set in Windows is cleared, or the entry would still start nothing.
    public static void Enable(string keyPath)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(keyPath))
        {
            string arguments = key.GetValue(AppInfo.Name) is string old ? Split(old).Arguments : "";
            key.SetValue(AppInfo.Name, arguments.Length > 0 ? Command() + " " + arguments : Command());
        }
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey(keyPath), writable: true);
        approved?.DeleteValue(AppInfo.Name, throwOnMissingValue: false);
    }

    public static void Disable(string keyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(AppInfo.Name, throwOnMissingValue: false);
    }

    public static string Command() => $"\"{Environment.ProcessPath}\"";

    internal static bool Starts(string command, string? exe) =>
        exe is not null && string.Equals(Split(command).Exe, exe, StringComparison.OrdinalIgnoreCase);

    // Task Manager and Settings > Apps > Startup turn an entry off without removing it. They write a
    // binary value of the same name under Explorer\StartupApproved, and an odd first byte means off.
    static bool TurnedOffInWindows(string keyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey(keyPath));
        return key?.GetValue(AppInfo.Name) is byte[] { Length: > 0 } flag && (flag[0] & 1) == 1;
    }

    // ...\CurrentVersion\Run is approved under ...\CurrentVersion\Explorer\StartupApproved\Run. A
    // scratch key in the tests gets its own beside it, so they never touch the real one.
    internal static string ApprovedKey(string keyPath)
    {
        int slash = keyPath.LastIndexOf('\\');
        return keyPath[..slash] + @"\Explorer\StartupApproved" + keyPath[slash..];
    }

    // A Run value is the exe, in quotes when its path has spaces, then any arguments. Without
    // quotes the exe ends at the first ".exe".
    internal static (string Exe, string Arguments) Split(string command)
    {
        string text = command.Trim();
        int end;
        if (text.StartsWith('"'))
        {
            end = text.IndexOf('"', 1);
            return end < 0 ? (text[1..], "") : (text[1..end], text[(end + 1)..].Trim());
        }
        end = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return end < 0 ? (text, "") : (text[..(end + 4)], text[(end + 4)..].Trim());
    }
}

// What the Start with Windows row shows. The normal exe is only ever Off or On: turning it on also
// takes back an entry Windows turned off. The Store package's task can be held by Windows.
enum AutostartState
{
    Off,
    On,
    // Turned off in Settings, Apps, Startup or in Task Manager; only the person can turn it on there.
    OffInWindows,
    // Turned off by the PC's administrator, or the PC does not run startup apps.
    OffByPolicy,
    // Turned on by the PC's administrator, who keeps it on.
    OnByPolicy,
    // Not known yet: Windows has not answered the first read, or a change asked for is not finished
    // (the Store package's task can take seconds). Not on, and not changeable until it is known; the
    // switch keeps what it shows, greyed out, so it never claims Off or jumps back meanwhile.
    Reading,
}
