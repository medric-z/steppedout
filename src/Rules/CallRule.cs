using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SteppedOut;

// On a call nobody types, yet nobody has left. Windows keeps a per-user record of which apps are
// using the microphone, the same one the privacy settings page and the taskbar's microphone icon
// read: an app whose LastUsedTimeStop is 0 has it open right now. Reading it needs no admin rights.
//
// Windows only writes the stop time when an app lets go of the microphone properly. A PC that lost
// power or was shut down mid-call, or an app that crashed, leaves a record that looks open for good,
// and Discord, which moves to a new folder with each update, gathers them. So a record from before
// this sign-in is ignored, and a record only counts while its app runs in this sign-in and started
// before the record did: a desktop app by its program, a Store app (new Teams, WhatsApp, Phone
// Link) by any program from its package.
sealed class CallRule : Rule
{
    const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";
    // The clock may be corrected in the first moments after signing in, which can put a call
    // started then a little before the sign-in.
    static readonly long Slack = TimeSpan.FromMinutes(1).Ticks;
    // When this sign-in began, on the monotonic clock. The tray app often starts before Windows has
    // set the clock (a PC whose hardware clock keeps UTC, say), so the sign-in is not kept as a
    // wall-clock time: it is worked out once as a time since the sign-in and turned back into the
    // wall clock at each scan, which moves it along with any correction, as the records are.
    static readonly Lazy<TimeSpan> SignedIn = new(() => SignedInOnClock(SignedInAt(), DateTime.UtcNow.ToFileTimeUtc(), Clock.Now));

    public override string Name => "call";

    // What a call does to the lock time. None still reports the call: its sound and the display
    // request a video call makes are the call's, not music or video.
    public Effect Effect { get; set; }

    public override void Check(Scene scene, List<WhyLine> lines)
    {
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in scene.Calls)
        {
            if (named.Add(app.Name)) lines.Add(new WhyLine(Signal.Call, Effect, app.Name));
        }
    }

    // Every program that has the microphone open right now, read from Windows' records. Costs a few
    // registry reads and a process lookup per open record, so the scene keeps it for a few seconds.
    public static IReadOnlyList<CallApp> Scan() =>
        Scan(Records(), DateTime.UtcNow.ToFileTimeUtc(), Clock.Now, SignedIn.Value, RunningSince,
            (family, latest) => FamilyRunningSince(family, latest, ProcessInfo.PackageFamilyName));

    // The records as Scan reads them, with the clocks, the sign-in and the running programs given:
    // a record counts only when it is open, began after the sign-in (NotBefore), and its app runs
    // since before it, a desktop app by its program (running) and a Store app by any program from
    // its package (familyRunning).
    internal static IReadOnlyList<CallApp> Scan(IEnumerable<MicRecord> records, long nowWall, TimeSpan nowMono, TimeSpan signedIn,
        Func<string, long, bool> running, Func<string, long, bool> familyRunning)
    {
        var found = new List<CallApp>();
        long notBefore = NotBefore(nowWall, nowMono, signedIn);
        foreach (var record in records)
        {
            if (record.Start is not long start || !InUse(start, record.Stop, notBefore)) continue;
            if (record.Exe is null)
            {
                if (familyRunning(record.Key, start + Slack)) found.Add(new CallApp(PackageName(record.Key), Family: record.Key));
                continue;
            }
            string program = ExeName(record.Exe);
            if (running(program, start + Slack)) found.Add(new CallApp(program, ExePath: ExePath(record.Exe)));
        }
        return found;
    }

    // Windows' records, one at a time. Store apps have a key each, named by their package family;
    // everything else sits under NonPackaged, one key per exe, named by its path.
    static IEnumerable<MicRecord> Records()
    {
        using var root = Registry.CurrentUser.OpenSubKey(ConsentStore);
        if (root is null) yield break;
        foreach (string name in root.GetSubKeyNames())
        {
            using var app = root.OpenSubKey(name);
            if (app is null) continue;
            if (name != "NonPackaged")
            {
                yield return new MicRecord(name, null, app.GetValue("LastUsedTimeStart"), app.GetValue("LastUsedTimeStop"));
                continue;
            }
            foreach (string exe in app.GetSubKeyNames())
            {
                using var entry = app.OpenSubKey(exe);
                if (entry is null) continue;
                yield return new MicRecord(name, exe, entry.GetValue("LastUsedTimeStart"), entry.GetValue("LastUsedTimeStop"));
            }
        }
    }

    // The sign-in on the monotonic clock, from its wall-clock time and the wall and monotonic clocks
    // now. A sign-in that reads as later than now (the clock was set back since) is taken as now.
    internal static TimeSpan SignedInOnClock(long signedInWall, long nowWall, TimeSpan nowMono) =>
        nowMono - TimeSpan.FromTicks(Math.Max(0, nowWall - signedInWall));

    // The earliest record start that can still be live: the sign-in in today's wall-clock time, less
    // the slack.
    internal static long NotBefore(long nowWall, TimeSpan nowMono, TimeSpan signedIn) =>
        nowWall - (nowMono - signedIn).Ticks - Slack;

    // start and stop are the record's FILETIMEs; notBefore is the earliest start that can still be live.
    internal static bool InUse(object? start, object? stop, long notBefore) =>
        start is long begun && begun != 0 && begun >= notBefore && stop is long ended && ended == 0;

    // A program by this name runs in this session and started no later than latest, a FILETIME. A
    // record left open by a copy that crashed and was started again, or by the version Discord has
    // since updated from, has none: the copy running now started after it.
    internal static bool RunningSince(string name, long latest)
    {
        var processes = Process.GetProcessesByName(name);
        try
        {
            foreach (var process in processes)
            {
                if (process.SessionId != ProcessRule.MySession) continue;
                try
                {
                    if (process.StartTime.ToFileTimeUtc() <= latest) return true;
                }
                catch (Win32Exception)
                {
                    // Its start time cannot be read, so it may well be the one on the call.
                    return true;
                }
                catch (InvalidOperationException)
                {
                    // Exited while being looked at.
                }
            }
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
        return false;
    }

    // A program from this Store package runs in this session and started no later than latest, a
    // FILETIME. familyOf gives a process's package family, or null for a desktop program. A record
    // left open by a Store app that crashed or was ended mid-call has none.
    internal static bool FamilyRunningSince(string family, long latest, Func<int, string?> familyOf)
    {
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.SessionId != ProcessRule.MySession) continue;
                    if (!string.Equals(familyOf(process.Id), family, StringComparison.OrdinalIgnoreCase)) continue;
                    if (process.StartTime.ToFileTimeUtc() <= latest) return true;
                }
                catch (Win32Exception)
                {
                    // Its start time cannot be read, so it may well be the one on the call.
                    return true;
                }
                catch (InvalidOperationException)
                {
                    // Exited while being looked at.
                }
            }
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
        return false;
    }

    // When this sign-in began, as a FILETIME like the records'. WTSINFOEXW is a DWORD Level followed
    // by an 8-byte-aligned WTSINFOEX_LEVEL1, whose LogonTime comes after three 4-byte fields and the
    // station, user and domain names (33, 21 and 18 characters): offset 168. When Windows cannot
    // say, the time the PC started is the next best thing.
    static long SignedInAt()
    {
        long now = DateTime.UtcNow.ToFileTimeUtc();
        if (Native.WTSQuerySessionInformationW(IntPtr.Zero, Native.WtsCurrentSession, Native.WtsSessionInfoEx, out IntPtr buffer, out uint bytes))
        {
            try
            {
                long logon = bytes >= 176 ? Marshal.ReadInt64(buffer, 168) : 0;
                if (logon > 0 && logon < now) return logon;
            }
            finally
            {
                Native.WTSFreeMemory(buffer);
            }
        }
        return now - Environment.TickCount64 * TimeSpan.TicksPerMillisecond;
    }

    // "C:#Program Files#Discord#app-1.0.9#Discord.exe" names the exe with # for each backslash.
    internal static string ExePath(string key) => key.Replace('#', '\\');

    internal static string ExeName(string key) => ProcessRule.Normalize(ExePath(key));

    // "Microsoft.SkypeApp_kzf8qxf38zg5c" is a package family: the name, then the publisher hash.
    internal static string PackageName(string key)
    {
        string family = key.Split('_')[0];
        return family[(family.LastIndexOf('.') + 1)..];
    }
}

// One of Windows' microphone records: the key it is under (a Store app's package family, or
// NonPackaged), the program's key under NonPackaged ("C:#Program Files#…#app.exe") or null for a
// Store app, and its LastUsedTimeStart and LastUsedTimeStop as read.
sealed record MicRecord(string Key, string? Exe, object? Start, object? Stop);

// A program that has the microphone open right now. ExePath is a desktop app's program file, from
// its record; Family is a Store app's package family, its record's name. The call's own sound is
// matched to it by these, never by name.
readonly record struct CallApp(string Name, string? ExePath = null, string? Family = null);
