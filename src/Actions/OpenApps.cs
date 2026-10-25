using System.ComponentModel;
using System.Diagnostics;

namespace SteppedOut;

// Opens the apps you chose when you leave or when you are back. Each is a program's full path, or
// shell:AppsFolder\ and the app's ID for a Store app, the way the Start menu opens it. Nothing else
// is opened: no web address, no command with arguments, no script (scripts have their own setting).
sealed class OpenApps
{
    public const string AppsFolder = @"shell:AppsFolder\";

    readonly List<string> apps;
    readonly bool whenAway;
    readonly IStarter starter;
    readonly Action<string> log;

    public OpenApps(IEnumerable<string> apps, bool whenAway, IStarter starter, Action<string> log)
    {
        this.apps = apps.Select(app => (app ?? "").Trim().Trim('"').Trim()).Where(app => app.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        this.whenAway = whenAway;
        this.starter = starter;
        this.log = log;
    }

    public string Name => $"open {string.Join(", ", apps)} {Side}";

    string Side => whenAway ? "when away" : "when back";

    // What a dry run says in place of Open: the apps it would open, with after behind them, then
    // each it would not, with the reason Open would give, so a dry run never promises an app the
    // live one refuses.
    public IEnumerable<string> DryRun(string after = "")
    {
        var opens = new List<string>();
        var not = new List<string>();
        foreach (string app in apps)
        {
            string? problem = StartInfo(app, File.Exists, out string why) is null ? why : starter.Refuses;
            if (problem is null) opens.Add(app);
            else not.Add($"NOT open {app} {Side}: {problem}");
        }
        if (opens.Count > 0) yield return $"open {string.Join(", ", opens)} {Side}{after}";
        foreach (string line in not) yield return line;
    }

    public bool Any => apps.Count > 0;

    // The apps that are also being closed, apart from the rest, so they can be opened once that
    // close is over. closing is asked about each program; a Store app's ID names no program, so it
    // is never held back.
    public (OpenApps Now, OpenApps OnceClosed) Split(Func<string, bool> closing)
    {
        var later = apps.Where(app => !app.StartsWith(AppsFolder, StringComparison.OrdinalIgnoreCase) && closing(app)).ToList();
        var now = apps.Where(app => !later.Contains(app, StringComparer.OrdinalIgnoreCase)).ToList();
        return (new OpenApps(now, whenAway, starter, log), new OpenApps(later, whenAway, starter, log));
    }

    // Stops before the next app once the token is cancelled.
    public void Open(CancellationToken token)
    {
        foreach (string app in apps)
        {
            if (token.IsCancellationRequested)
            {
                log($"{(whenAway ? "you are back" : "the opening was called off")}, so {app} is not opened");
                continue;
            }
            var start = StartInfo(app, File.Exists, out string problem);
            if (start is null)
            {
                log($"did not open {app}: {problem}");
                continue;
            }
            try
            {
                starter.Start(start);
                log("opened " + app);
            }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException)
            {
                log($"could not open {app}: {e.Message}");
            }
        }
    }

    // How an entry is opened, or null with the reason it is not.
    internal static ProcessStartInfo? StartInfo(string entry, Func<string, bool> fileExists, out string problem) =>
        StartInfo(entry, fileExists, LocalPaths.DriveTypeOf, out problem);

    // drive tells what is behind a drive letter (a mapped network drive, none); the tests give their own.
    internal static ProcessStartInfo? StartInfo(string entry, Func<string, bool> fileExists, Func<char, DriveType> drive, out string problem)
    {
        problem = "";
        // Explorer's "Copy as path" puts the path in quotes.
        string app = (entry ?? "").Trim().Trim('"').Trim();
        if (app.Length == 0 || app.Any(char.IsControl) || app.Contains('"'))
        {
            problem = "it is not a program";
            return null;
        }
        if (app.StartsWith(AppsFolder, StringComparison.OrdinalIgnoreCase))
        {
            string id = app[AppsFolder.Length..];
            if (id.Length == 0 || id.Contains("..", StringComparison.Ordinal))
            {
                problem = "it names no app";
                return null;
            }
            return new ProcessStartInfo(AppsFolder + id) { UseShellExecute = true };
        }
        // Not a program on another PC, by a network path or on a mapped network drive, which would
        // reach out to it, nor one on a drive letter not connected now; not a device path, or a
        // relative one, which would depend on where SteppedOut was started.
        var place = LocalPaths.Place(app, drive);
        if (LocalPaths.Elsewhere(app, place) is { } elsewhere)
        {
            problem = elsewhere + ", and only a program on this PC or a Store app opens here";
            return null;
        }
        if (place != PathPlace.ThisPC)
        {
            problem = "only a program's full path or a Store app opens here";
            return null;
        }
        if (!Path.GetExtension(app).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            problem = "only programs (.exe) and Store apps open here";
            return null;
        }
        if (!fileExists(app))
        {
            problem = "the file is not there";
            return null;
        }
        return new ProcessStartInfo(app)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(app) ?? "",
        };
    }
}
