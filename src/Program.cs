using System.Reflection;
using System.Runtime.InteropServices;

namespace SteppedOut;

static class Program
{
    const int AnyProcess = -1;

    [STAThread]
    static int Main(string[] args)
    {
        // Before anything loads a DLL by name: only Windows' own folder is searched.
        DllSearch.OnlySystem32();
        var options = CommandLine.Parse(args);
        if (options.Error is not null)
        {
            // Started from a shortcut, the Run key or Explorer, there is no terminal to print to.
            // Exiting without a word would leave no hint why the tray icon never came up.
            if (!ConsoleOutput.TryAttach())
            {
                ApplicationConfiguration.Initialize();
                MessageBox.Show(options.Error + Environment.NewLine + Environment.NewLine + CommandLine.Usage,
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
            Console.Error.WriteLine(options.Error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(CommandLine.Usage);
            return 2;
        }
        if (options.ShowHelp)
        {
            ConsoleOutput.TryAttach();
            Console.WriteLine(CommandLine.Usage);
            return 0;
        }
        if (options.ShowVersion)
        {
            ConsoleOutput.TryAttach();
            Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
            return 0;
        }

        // Claimed before the settings are read: a second start only asks the copy that runs to open
        // its settings, so it reads them once instead of waiting out a file another program holds.
        // --once runs next to the tray copy and claims nothing.
        string? notClaimed = null;
        string? folderProblem = StartPlan.ClaimsInstance(options) ? StartPlan.MakeFolder(AppInfo.ConfigDir) : null;
        using var instance = StartPlan.ClaimsInstance(options) ? InstanceChannel.Claim(AppInfo.MutexName, out notClaimed) : null;
        var plan = StartPlan.For(options, ownsInstance: instance is not null);
        string? configProblem, configWarning, configRetried = null;
        var config = plan.ReadsWithRetries
            ? ConfigReload.AtStart(AppInfo.ConfigPath, out configProblem, out configWarning, out configRetried)
            : Config.Load(AppInfo.ConfigPath, makeIfMissing: false, out configProblem, out configWarning);
        bool configBroken = configProblem is not null && File.Exists(AppInfo.ConfigPath);
        if (configProblem is not null) configProblem += "; using the defaults";
        // Nothing is read from a file from before 1.0: the settings start fresh, with the welcome
        // (D50). Only this copy, when it is the one that runs, keeps that file next to it and writes
        // a fresh one; --once and a second start use the defaults and leave the file as it is.
        var fresh = FreshStart.Apply(config, AppInfo.ConfigPath, plan.WritesSettings);
        // --idle is for this run only; the tray app keeps it out of the saved file.
        var run = config.Copy();
        if (options.IdleMinutes is double idleMinutes) run.IdleMinutes = idleMinutes;
        Lang.Use(config.Language);

        if (plan.WritesLog)
        {
            Log.Open(AppInfo.LogPath, config.LogMaxKilobytes * 1024L);
            if (folderProblem is not null) Log.Write(folderProblem);
            if (configRetried is not null) Log.Write(configRetried + "; tried again");
            // The version is only worth a line when the settings came from the file.
            Log.Write(configProblem ?? fresh.LogLine);
            if (configWarning is not null) Log.Write(configWarning);
        }

        if (options.Once)
        {
            ConsoleOutput.TryAttach();
            OnceReport.Print(run, options, Rules.Build(run), configProblem, configWarning ?? fresh.OnceLine);
            return 0;
        }

        if (notClaimed is not null)
        {
            Log.Write(notClaimed);
            return AlreadyRunning(args);
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        // The first error in full, then a count, so one that comes back every second does not push
        // its own stack trace out of the log.
        var unhandled = new ErrorRepeats("unhandled", Log.Write, () => Clock.Now);
        Application.ThreadException += (_, e) => unhandled.Note(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("fatal: " + e.ExceptionObject);
        ApplicationConfiguration.Initialize();

        // The Windows build, which download runs on which processor ("X64 on Arm64" is the wrong one)
        // and the display language, so a log sent from another PC explains itself.
        Log.Write($"{AppInfo.Name} {AppInfo.Version}{BuildInfo.Label} starting{(options.DryRun ? " (dry run)" : "")}: idle {run.IdleMinutes} min, grace {config.GraceSeconds} s, "
            + $"Windows {Environment.OSVersion.Version}, {RuntimeInformation.ProcessArchitecture} on {RuntimeInformation.OSArchitecture}, "
            + $"UI language 0x{Native.GetUserDefaultUILanguage():X4}, config {AppInfo.ConfigPath}");
        if (Elevation.Full) Log.Write(Elevation.StartLine);
        // A test copy never changes Start with Windows (Autostart.DryRun).
        Autostart.DryRun = options.DryRun;
        using var watcher = new Watcher(run, options.DryRun);
        using var app = new TrayApp(watcher, config, options, configBroken, configWarning is not null, instance!.ShowRequest);
        Application.Run(app);
        Log.Write("exiting");
        return 0;
    }

    // Starting it again is how most people look for its window, so the copy that runs is asked to
    // open its settings. Options given to this start cannot reach that copy, so the person is told.
    static int AlreadyRunning(string[] args)
    {
        using var show = InstanceChannel.OpenShowRequest(AppInfo.MutexName);
        string options = string.Join(" ", args);
        if (show is null || options != "") Tell(settingsOpened: show is not null, options);
        if (show is not null)
        {
            // Windows lets the program the person just started bring a window to the front, not one
            // that has run for a while, so this start hands that on before asking.
            AllowSetForegroundWindow(AnyProcess);
            show.Set();
            Log.Write("asked the running copy to open its settings");
        }
        return options == "" ? 0 : 1;
    }

    static void Tell(bool settingsOpened, string options)
    {
        // A terminal shows Arabic badly, and the rest of the command line's text is English.
        if (ConsoleOutput.TryAttach())
        {
            Console.Error.WriteLine(English.Text.AlreadyRunningMessage(settingsOpened, options));
            return;
        }
        var text = Lang.Current;
        ApplicationConfiguration.Initialize();
        MessageBox.Show(text.AlreadyRunningMessage(settingsOpened, options), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button1, text.RightToLeft ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool AllowSetForegroundWindow(int processId);
}

// What one start of the exe does with the instance and the files, decided before anything is claimed,
// read or written. Main follows it, and the tests read it.
sealed record StartPlan(bool ReadsWithRetries, bool WritesSettings, bool WritesLog)
{
    // --once runs next to the copy in the tray: it claims nothing.
    public static bool ClaimsInstance(Options options) => !options.Once;

    // The copy that runs reads config.json with retries, as another program may hold it for a
    // moment, makes it with the defaults when there is none, keeps a file from before 1.0 and
    // writes the log. A second start reads it once and writes only its log lines: the settings are
    // the running copy's. --once only reads, and writes no file at all.
    public static StartPlan For(Options options, bool ownsInstance) =>
        options.Once ? new(ReadsWithRetries: false, WritesSettings: false, WritesLog: false)
        : ownsInstance ? new(ReadsWithRetries: true, WritesSettings: FreshStart.SavesAtStart(options, ownsInstance), WritesLog: true)
        : new(ReadsWithRetries: false, WritesSettings: false, WritesLog: true);

    // Makes the settings folder before anything is written there. It runs before the app has a log
    // or catches anything, so a folder that cannot be made (a file by that name, a profile folder on
    // a network share that is offline) must not end it without a word: the app goes on with the
    // defaults, as when config.json cannot be read, and this says why, for the log if it can be
    // written. Null when the folder is there.
    public static string? MakeFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"could not make the settings folder {folder}: {e.Message}";
        }
    }
}

// The commit this exe was built from, which the build writes after the version ("1.0.0+" and the
// commit). Two builds of one version, such as a test build and the release candidate, tell apart by
// it in the log.
static class BuildInfo
{
    public static string? Commit { get; } = CommitOf(typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    // " (build " and the commit's first 7 characters, then ")"; empty for a build that has none.
    public static string Label => Commit is null ? "" : $" (build {Commit})";

    internal static string? CommitOf(string? informationalVersion)
    {
        int plus = informationalVersion?.IndexOf('+') ?? -1;
        if (plus < 0) return null;
        string commit = informationalVersion![(plus + 1)..];
        return commit.Length >= 7 && commit.All(char.IsAsciiHexDigit) ? commit[..7].ToLowerInvariant() : null;
    }
}
