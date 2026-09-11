using System.Diagnostics;

namespace SteppedOut;

sealed class TrayApp : ApplicationContext
{
    readonly Watcher watcher;
    readonly Options options;
    readonly NotifyIcon icon;
    readonly CountdownForm toast = new();
    readonly ToolStripMenuItem pauseItem = new("Pause");
    readonly ToolStripMenuItem autostartItem = new("Start with Windows");
    readonly System.Windows.Forms.Timer refresh = new() { Interval = 1000 };
    readonly ConfigWatcher configWatcher;
    Config config;
    SettingsForm? settings;
    bool ignoreNextFileChange;

    public TrayApp(Watcher watcher, Config config, Options options)
    {
        this.watcher = watcher;
        this.config = config;
        this.options = options;

        watcher.CountdownStarted += toast.ShowCountdown;
        watcher.CountdownTick += toast.SetSeconds;
        watcher.CountdownEnded += toast.HideCountdown;
        toast.LockNowClicked += watcher.LockNow;

        var pauseFor = new ToolStripMenuItem("Pause for");
        pauseFor.DropDownItems.Add("1 hour", null, (_, _) => watcher.Pause(TimeSpan.FromHours(1)));
        pauseFor.DropDownItems.Add("4 hours", null, (_, _) => watcher.Pause(TimeSpan.FromHours(4)));
        pauseItem.Click += (_, _) => TogglePause();
        autostartItem.Click += (_, _) => ToggleAutostart();

        var menu = new ContextMenuStrip();
        menu.Items.Add(pauseItem);
        menu.Items.Add(pauseFor);
        menu.Items.Add("Lock now", null, (_, _) => watcher.LockNow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings...", null, (_, _) => OpenSettings());
        menu.Items.Add("Open config folder", null, (_, _) => OpenConfigFolder());
        menu.Items.Add(autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());
        menu.Opening += (_, _) =>
        {
            pauseItem.Text = watcher.IsPaused ? "Resume" : "Pause";
            autostartItem.Checked = Autostart.IsEnabled();
        };

        icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = AppInfo.Name,
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.DoubleClick += (_, _) => OpenSettings();

        configWatcher = new ConfigWatcher(AppInfo.ConfigPath);
        configWatcher.Changed += ReloadConfig;

        refresh.Tick += (_, _) => icon.Text = Tooltip();
        refresh.Start();
        watcher.Start();
    }

    string Tooltip()
    {
        string text = $"{AppInfo.Name}: {watcher.Status}{(options.DryRun ? " (dry run)" : "")}";
        return text.Length <= 127 ? text : text[..127];
    }

    static Icon LoadIcon()
    {
        using var stream = typeof(TrayApp).Assembly.GetManifestResourceStream("icon.ico")!;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    void TogglePause()
    {
        if (watcher.IsPaused) watcher.Resume();
        else watcher.Pause(null);
    }

    void OpenSettings()
    {
        if (settings is { IsDisposed: false })
        {
            settings.Activate();
            return;
        }
        settings = new SettingsForm(config, icon.Icon!);
        settings.Applied += fresh =>
        {
            fresh.Clamp();
            config = fresh;
            ignoreNextFileChange = true;
            try
            {
                config.Save(AppInfo.ConfigPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Write("could not save config: " + e.Message);
            }
            ApplyConfig("settings saved");
        };
        settings.Show();
    }

    void ReloadConfig()
    {
        if (ignoreNextFileChange)
        {
            ignoreNextFileChange = false;
            return;
        }
        var fresh = Config.Load(AppInfo.ConfigPath, out string? problem);
        if (problem is not null)
        {
            Log.Write(problem);
            return;
        }
        config = fresh;
        ApplyConfig("config file changed");
    }

    void ApplyConfig(string why)
    {
        // A threshold given on the command line stays in force across reloads.
        if (options.IdleMinutes is double idle) config.IdleMinutes = idle;
        watcher.Apply(config);
        Log.SetLimit(config.LogMaxKilobytes * 1024L);
        Log.Write($"{why}: idle {config.IdleMinutes} min, grace {config.GraceSeconds} s");
    }

    void ToggleAutostart()
    {
        try
        {
            if (Autostart.IsEnabled())
            {
                Autostart.Disable();
                Log.Write("start with Windows: off");
            }
            else
            {
                Autostart.Enable();
                Log.Write("start with Windows: on, " + Autostart.Command());
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Write("could not change the Run entry: " + e.Message);
        }
    }

    static void OpenConfigFolder()
    {
        Directory.CreateDirectory(AppInfo.ConfigDir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppInfo.ConfigDir}\"") { UseShellExecute = true });
    }

    void Quit()
    {
        Log.Write("quit from the menu");
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            configWatcher.Dispose();
            refresh.Dispose();
            icon.Visible = false;
            icon.Dispose();
            toast.Dispose();
            settings?.Dispose();
        }
        base.Dispose(disposing);
    }
}
