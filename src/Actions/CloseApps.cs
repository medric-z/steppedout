namespace SteppedOut;

// Closes the programs you listed, when you leave or when you are back, and only those. Every process
// of a listed program in this sign-in is asked to close: each of its windows gets the request its
// close button would send, so every document gets its Save prompt. Whatever of those processes is
// still running after the wait is closed by force, unless the side that started it is called off
// first: coming back calls off the closing done when you left, and leaving again calls off the
// closing done when you came back. Both sides close the same way.
//
// A program a listed one started (a game from a launcher, a download opened from a browser, an
// attachment opened from a mail app) is a program of its own: it is never asked and never forced
// here. What a listed program does with it when asked to close is up to that program.
//
// Only processes in this sign-in are touched, never SteppedOut itself (by its process and by its
// program file, whatever the file is called), never a program it was started from (any process of
// it, since its other processes are its tabs, helpers and other windows), and never the parts of
// Windows that would take the desktop or the sign-in with them. A process that runs as administrator
// is left open at once, without a wait: Windows would not let it be asked or forced, and when
// SteppedOut itself runs as administrator and could, it still doesn't.
sealed class CloseApps
{
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(30);

    // Windows' own processes. Most belong to no sign-in and are never seen here; these are the ones
    // that run inside it.
    internal static readonly HashSet<string> Guarded = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "csrss", "winlogon", "wininit", "lsass", "services", "smss", "svchost", "sihost",
        "ShellExperienceHost", "StartMenuExperienceHost", "ShellHost", "ctfmon", "conhost", "ApplicationFrameHost",
        "RuntimeBroker", "SearchHost", "SearchApp", "TextInputHost", "taskhostw", "fontdrvhost", "LogonUI",
        "LockApp", "dllhost", "SystemSettings", "SecurityHealthSystray", "backgroundTaskHost", "audiodg",
        "System", "Registry", "Idle", "smartscreen", "WUDFHost", "UserOOBEBroker", "WidgetService",
    };

    readonly List<string> names;
    readonly HashSet<string> wanted;
    readonly bool whenAway;
    readonly IProcessApi api;
    readonly TimeProvider time;
    readonly TimeSpan wait;
    readonly Action<string> log;

    public CloseApps(IEnumerable<string> apps, bool whenAway, IProcessApi api, TimeProvider time, TimeSpan wait, Action<string> log)
    {
        this.whenAway = whenAway;
        this.api = api;
        this.time = time;
        this.wait = wait;
        this.log = log;
        names = new List<string>();
        wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string app in apps)
        {
            string name = ProcessRule.Normalize(app ?? "");
            if (name.Length == 0 || !wanted.Add(name)) continue;
            names.Add(name);
        }
    }

    public string Name => $"close {string.Join(", ", names)} {(whenAway ? "when away" : "when back")}";

    public bool Any => names.Count > 0;

    // Whether this closes the program at a path or with a name, matched the way running processes are.
    public bool Closes(string program) => wanted.Contains(ProcessRule.Normalize(program ?? ""));

    // Why a close was called off: coming back calls off the closing done when you left, and leaving
    // again, signing out or quitting calls off the closing done when you came back.
    string CalledOff => whenAway ? "you are back" : "the closing was called off";

    sealed record Target(string Name, IProcessHandle Handle);

    // Asks every window of every matching process to close, now, and returns the wait that follows,
    // which runs off the calling thread and forces whatever is left once it is over. Cancelling the
    // token stops the asking and calls off the force. The task never throws.
    public Task Close(CancellationToken token)
    {
        if (token.IsCancellationRequested) return Task.CompletedTask;
        List<Target> targets;
        try
        {
            targets = Find();
        }
        catch (Exception e)
        {
            log($"could not {Name}: {e.Message}");
            return Task.CompletedTask;
        }
        if (targets.Count == 0) return Task.CompletedTask;
        // The windows asked of each process; a process that could not be asked has a line of its
        // own and none here. Like an app with no window, the force after the wait still comes.
        var asked = new Dictionary<Target, int>();
        foreach (var target in targets)
        {
            if (token.IsCancellationRequested) break;
            try
            {
                asked[target] = Ask(target, token);
            }
            catch (Exception e)
            {
                log($"could not ask {Describe(target)} to close: {e.Message}");
            }
        }
        if (token.IsCancellationRequested)
        {
            Stayed(targets);
            Dispose(targets);
            return Task.CompletedTask;
        }
        foreach (var program in ByProgram(targets))
        {
            var withWindows = program.Where(target => asked.GetValueOrDefault(target) > 0).ToList();
            var windowless = program.Where(target => asked.TryGetValue(target, out int windows) && windows == 0).ToList();
            string name = program[0].Name;
            if (withWindows.Count > 0) log($"asked {name} ({Pids(withWindows)}) to close: {Count(withWindows.Sum(target => asked[target]), "window")}");
            if (windowless.Count == 0) continue;
            string unless = $"in {wait.TotalSeconds:0} s unless {(whenAway ? "you are back" : "you leave")} first";
            log(windowless.Count == 1
                ? $"{name} ({Pids(windowless)}) has no window to ask; it is closed by force {unless}"
                : $"{windowless.Count} processes of {name} ({Pids(windowless)}) have no window to ask; they are closed by force {unless}");
        }
        return WaitThenForce(targets, token);
    }

    // The dry run: what would be asked, and nothing else.
    public void DryRun()
    {
        List<Target> targets;
        try
        {
            targets = Find();
        }
        catch (Exception e)
        {
            log($"could not look for the apps to close: {e.Message}");
            return;
        }
        // Nothing found: none of them runs in this sign-in, or each one found has a line above saying
        // why it is left open.
        if (targets.Count == 0) log($"WOULD close nothing {(whenAway ? "when away" : "when back")}: no process of {string.Join(", ", names)} to close (dry run)");
        foreach (var program in ByProgram(targets))
        {
            int windows = program.Sum(target => WindowsOf(target.Handle.Id).Count);
            log($"WOULD close {program[0].Name} ({Pids(program)}), {Count(windows, "window")} (dry run)");
        }
        Dispose(targets);
    }

    // The windows of one process that were asked to close.
    int Ask(Target target, CancellationToken token)
    {
        int asked = 0;
        foreach (var window in WindowsOf(target.Handle.Id))
        {
            if (token.IsCancellationRequested) break;
            if (api.PostClose(window)) asked++;
        }
        return asked;
    }

    List<IntPtr> WindowsOf(int processId) => AppWindows.ToClose(processId, api.Windows(), api.ChildrenOf);

    async Task WaitThenForce(List<Target> targets, CancellationToken token)
    {
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            var allExited = Task.WhenAll(targets.Select(target => target.Handle.WhenExited));
            var over = Task.Delay(wait, time, waiting.Token);
            // Never carries on inside Cancel, on the thread that came back.
            await Task.WhenAny(allExited, over).ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            // Every app closed in time: the timer is not needed any more.
            waiting.Cancel();
            var ended = new Dictionary<Target, Ended>();
            foreach (var target in targets)
            {
                ended[target] = target.Handle.HasExited ? Ended.Closed
                    : token.IsCancellationRequested ? Ended.Stayed
                    : Kill(target, token);
            }
            // One line for each program and outcome.
            foreach (var program in ByProgram(targets))
            {
                string name = program[0].Name;
                if (program.Where(target => ended[target] == Ended.Closed).ToList() is { Count: > 0 } closed) log($"{name} ({Pids(closed)}) closed");
                if (program.Where(target => ended[target] == Ended.Forced).ToList() is { Count: > 0 } forced)
                    log($"closed {name} ({Pids(forced)}) by force: still running after {wait.TotalSeconds:0} s");
                if (program.Where(target => ended[target] == Ended.Stayed).ToList() is { Count: > 0 } stayed) Stayed(stayed);
            }
        }
        catch (Exception e)
        {
            log($"could not finish closing apps: {e.Message}");
        }
        finally
        {
            Dispose(targets);
        }
    }

    enum Ended { Closed, Forced, Stayed, Failed }

    // Only the listed program's own process, by the handle that has named it since it was found.
    // Never a whole tree, and never what it started: those are programs of their own. A force that
    // fails says so here, in a line of its own.
    Ended Kill(Target target, CancellationToken token)
    {
        var handle = target.Handle;
        if (handle.HasExited) return Ended.Closed;
        // The last look at the token, with nothing left between it and TerminateProcess. The gap
        // cannot be closed fully: the return is noticed on another thread, which cancels without
        // waiting for anyone, and TerminateProcess cannot be made to wait on that. Even then, a
        // return is only noticed at the next tick after the mouse moved, so a force may still land
        // just after a return that has not been noticed yet.
        if (token.IsCancellationRequested) return Ended.Stayed;
        int error = handle.Kill();
        if (error == 0) return Ended.Forced;
        if (handle.HasExited) return Ended.Closed;
        log($"could not close {Describe(target)} by force, error {error}");
        return Ended.Failed;
    }

    // "you are back, so inkwell (pid 2000) stays open", naming each program once.
    void Stayed(List<Target> targets)
    {
        var programs = ByProgram(targets);
        string which = string.Join(", ", programs.Select(program => $"{program[0].Name} ({Pids(program)})"));
        log($"{CalledOff}, so {which} {(programs.Count == 1 ? "stays" : "stay")} open");
    }

    // The matching processes in this sign-in, each held by a handle that keeps naming it.
    List<Target> Find()
    {
        var found = new List<Target>();
        foreach (string name in names.Where(Guarded.Contains)) log($"never closes {name}: Windows needs it");
        if (!names.Any(name => !Guarded.Contains(name))) return found;
        var snapshot = api.Snapshot();
        var keep = Protected(snapshot);
        if (api.SessionOf(api.OwnId) is not int session)
        {
            log("could not tell which apps belong to this sign-in, so none is closed");
            return found;
        }
        var startedFrom = StartedFrom(snapshot, keep, session);
        try
        {
            foreach (var entry in snapshot)
            {
                string name = ProcessRule.Normalize(entry.ExeFile);
                if (!wanted.Contains(name) || Guarded.Contains(name) || startedFrom.Contains(name)) continue;
                if (entry.Id == api.OwnId)
                {
                    log($"left {name} (pid {entry.Id}) open: it is SteppedOut");
                    continue;
                }
                if (api.SessionOf(entry.Id) != session) continue;
                if (Hold(entry.Id, name) is { } handle) found.Add(new Target(name, handle));
            }
        }
        catch
        {
            Dispose(found);
            throw;
        }
        return found;
    }

    // The listed programs SteppedOut was started from, each left open whole: the process it was
    // started from and every other process of that program in this sign-in, which are its tabs, its
    // helpers and its other windows. One line in the log for each.
    HashSet<string> StartedFrom(IReadOnlyList<ProcessEntry> snapshot, HashSet<int> keep, int session)
    {
        var programs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshot)
        {
            if (entry.Id == api.OwnId || !keep.Contains(entry.Id)) continue;
            string name = ProcessRule.Normalize(entry.ExeFile);
            if (wanted.Contains(name) && !Guarded.Contains(name)) programs.Add(name);
        }
        if (programs.Count == 0) return programs;
        var left = programs.ToDictionary(name => name, _ => new List<int>(), StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshot)
        {
            // SteppedOut's own process is not one it was started from, even when it was started from
            // another copy of its own program.
            if (entry.Id == api.OwnId || !left.TryGetValue(ProcessRule.Normalize(entry.ExeFile), out var ids)) continue;
            if (keep.Contains(entry.Id) || api.SessionOf(entry.Id) == session) ids.Add(entry.Id);
        }
        foreach (string name in names.Where(programs.Contains))
        {
            log($"left {name} ({Pids(left[name])}) open: SteppedOut was started from it");
        }
        return programs;
    }

    // Opens the process and checks, through the handle, that it still runs the program it was matched
    // by and is not SteppedOut. Null, with a line in the log, when it is left alone.
    IProcessHandle? Hold(int processId, string name)
    {
        var handle = api.Open(processId, out int error);
        if (handle is not null && handle.Elevated is not false)
        {
            // Windows gives such a handle when SteppedOut runs as administrator too. The app is still
            // never asked or forced, and one whose token cannot be read is treated the same.
            log(handle.Elevated is true
                ? $"left {name} (pid {processId}) open: it runs as administrator"
                : $"left {name} (pid {processId}) open: could not tell whether it runs as administrator");
            handle.Dispose();
            return null;
        }
        if (handle is null)
        {
            log(error switch
            {
                5 => $"left {name} (pid {processId}) open: Windows denied access to it (it may run as administrator)",
                87 => $"{name} (pid {processId}) closed",
                _ => $"left {name} (pid {processId}) open: Windows would not give access to it, error {error}",
            });
            return null;
        }
        string? path = handle.ImagePath;
        string? reason = path is null ? "could not read which program it runs"
            : !string.Equals(ProcessRule.Normalize(path), name, StringComparison.OrdinalIgnoreCase) ? "it is no longer the same program"
            : SamePath(path, api.OwnPath) ? "it is SteppedOut"
            : null;
        if (reason is null) return handle;
        log($"left {name} (pid {processId}) open: {reason}");
        handle.Dispose();
        return null;
    }

    // This process and every process it was started from. A parent number is only trusted when that
    // parent started before its child; a later process that got the number of a parent that has
    // exited is not an ancestor. When a start time cannot be read, the parent counts as one.
    HashSet<int> Protected(IReadOnlyList<ProcessEntry> snapshot)
    {
        var byId = new Dictionary<int, ProcessEntry>();
        foreach (var entry in snapshot) byId.TryAdd(entry.Id, entry);
        var keep = new HashSet<int> { api.OwnId };
        int current = api.OwnId;
        long? currentStart = api.StartedAt(current);
        while (keep.Count < 64 && byId.TryGetValue(current, out var entry))
        {
            int parent = entry.ParentId;
            if (parent <= 0 || keep.Contains(parent) || !byId.ContainsKey(parent)) break;
            long? parentStart = api.StartedAt(parent);
            if (parentStart is long p && currentStart is long c && p > c) break;
            keep.Add(parent);
            current = parent;
            currentStart = parentStart;
        }
        return keep;
    }

    static bool SamePath(string path, string? own)
    {
        if (string.IsNullOrEmpty(own)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(path), Path.GetFullPath(own), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(path, own, StringComparison.OrdinalIgnoreCase);
        }
    }

    static string Describe(Target target) => $"{target.Name} (pid {target.Handle.Id})";

    // The processes found, one list for each listed program, in the order of the list.
    List<List<Target>> ByProgram(List<Target> targets) =>
        names.Select(name => targets.Where(target => target.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList())
            .Where(program => program.Count > 0).ToList();

    static string Pids(List<Target> targets) => Pids(targets.Select(target => target.Handle.Id).ToList());

    // "pid 2000" or "pid 2000, 2001, 2002 and 25 more": the first three, so a program with dozens of
    // processes still gets a short line.
    internal static string Pids(IReadOnlyList<int> ids) =>
        "pid " + string.Join(", ", ids.Take(3)) + (ids.Count > 3 ? $" and {ids.Count - 3} more" : "");

    static string Count(int n, string what) => n == 1 ? $"1 {what}" : $"{n} {what}s";

    static void Dispose(List<Target> targets)
    {
        foreach (var target in targets) target.Handle.Dispose();
    }
}
