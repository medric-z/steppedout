namespace SteppedOut;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var options = CommandLine.Parse(args);
        if (options.Error is not null)
        {
            ConsoleOutput.TryAttach();
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

        Directory.CreateDirectory(AppInfo.ConfigDir);
        var config = Config.Load(AppInfo.ConfigPath, out string? configProblem);
        if (options.IdleMinutes is double idleMinutes) config.IdleMinutes = idleMinutes;

        Log.Open(AppInfo.LogPath, config.LogMaxKilobytes * 1024L);
        if (configProblem is not null) Log.Write(configProblem);

        if (options.Once)
        {
            ConsoleOutput.TryAttach();
            OnceReport.Print(config, options, Rules.Build(config));
            return 0;
        }

        using var instance = new Mutex(initiallyOwned: true, AppInfo.MutexName, out bool first);
        if (!first)
        {
            Log.Write("another instance is already running, exiting");
            return 0;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Write("unhandled: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("fatal: " + e.ExceptionObject);
        ApplicationConfiguration.Initialize();

        Log.Write($"{AppInfo.Name} {AppInfo.Version} starting{(options.DryRun ? " (dry run)" : "")}: idle {config.IdleMinutes} min, grace {config.GraceSeconds} s, config {AppInfo.ConfigPath}");
        using var watcher = new Watcher(config, options.DryRun);
        using var app = new TrayApp(watcher, config, options);
        Application.Run(app);
        Log.Write("exiting");
        return 0;
    }
}
