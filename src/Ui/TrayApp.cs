using Microsoft.Win32;

namespace SteppedOut;

// The app in the tray: it makes every part, wires them together, and is the settings window's host.
// Everything runs on the UI thread. Work that finishes on another thread (coming back, the Store
// package's startup task) comes back through Post, which does nothing once the app is closing.
sealed class TrayApp : ApplicationContext, ISettingsHost
{
    readonly TrayParts parts;
    readonly Watcher watcher;
    readonly Options options;
    readonly ConfigReload reload;
    readonly EventWaitHandle? showRequest;
    readonly AwayActions actions;
    readonly Toast toast;
    readonly TrayMenu trayMenu;
    readonly ITrayIcon icon;
    readonly TrayIconSlot slot;
    readonly TrayNotes notes;
    readonly TrayLive live;
    readonly TrayAutostart startup;
    readonly ConfigWatcher? configWatcher;
    readonly HotKey? hotKey;
    readonly ControllerInput? controller;
    Config config;
    SettingsForm? settings;
    WelcomeForm? welcome;
    // The language the tray menu shows.
    Strings labeled;
    // The keyboard layouts installed when the shortcut was last checked.
    IReadOnlyList<IntPtr>? layoutsSeen;
    bool configBroken;
    // Running on the defaults because the file has not loaded since start-up.
    bool onDefaults;
    bool listening;
    bool statusFailed;
    // The last save of the settings failed; the note that said so, in the language it was shown in.
    bool saveFailed;
    string? saveFailedNote;
    // The note that the locking may have stopped (Watcher.TicksFailing), in the language it was shown in.
    string? failingNote;
    // The lock shortcut is not taken until the welcome finishes (D46).
    bool shortcutWaits;
    // "Choose another shortcut…" is open: no shortcut is held, and none is pressed (I1).
    bool choosingShortcut;
    // Settings were applied while it was open, so the shortcut in them is taken as it closes.
    bool appliedWhileChoosing;
    // The new shortcut it saved and holds, which the page's edit brings into the settings after it.
    string? keptWhileChoosing;
    // Set first thing when the app starts closing; read from other threads by Post.
    volatile bool quitting;
    volatile bool disposed;

    public TrayApp(Watcher watcher, Config config, Options options, bool configBroken, bool configSkipped, EventWaitHandle showRequest)
        : this(watcher, config, options, configBroken, configSkipped, showRequest, TrayParts.Real)
    {
    }

    // In this order, all on the UI thread. A part that fails to start (the shortcut, the controller,
    // watching the file, Windows' notices, start with Windows) is written to the log and left out:
    // the tray still comes up.
    internal TrayApp(Watcher watcher, Config config, Options options, bool configBroken, bool configSkipped, EventWaitHandle? showRequest, TrayParts parts)
    {
        this.parts = parts;
        this.watcher = watcher;
        this.config = config;
        this.options = options;
        this.configBroken = configBroken;
        onDefaults = configBroken;
        this.showRequest = showRequest;
        reload = new ConfigReload(parts.ConfigPath, () => this.config);
        live = new TrayLive(watcher.KeepLive);

        // The look first, so the menu and every window are drawn in it. Program chose the language.
        parts.ChooseTheme(config);

        // The notes' window before anything that listens to Windows: making the first control gives
        // this thread the WinForms synchronization context, so the away actions' SystemEvents
        // handlers, the config watcher and this app's own SystemEvents handlers all run on it. Post
        // goes through this window too, so it needs its handle.
        toast = parts.NewToast();
        _ = toast.Handle;
        toast.Use(config);

        actions = parts.NewActions(options.DryRun);
        actions.Apply(config);

        startup = new TrayAutostart(parts.Autostart, Post);
        startup.Changed += OnStartupChanged;

        labeled = Lang.Current;
        trayMenu = new TrayMenu(labeled) { StateSource = () => View().Menu };
        trayMenu.PauseChosen += PauseChosen;
        // After the menu has closed, so the window it opens takes the keyboard.
        trayMenu.PauseForChosen += () => Post(PauseForChosen);
        trayMenu.TurnBackOnChosen += ResumeChosen;
        trayMenu.SnoozeChosen += SnoozeChosen;
        trayMenu.LockNowChosen += LockNowChosen;
        trayMenu.SettingsChosen += OpenSettings;
        // The menu is still handling the click, and quitting disposes it.
        trayMenu.QuitChosen += () => Post(Quit);
        trayMenu.Opened += () => live.Hold("menu");
        trayMenu.Closed += () => live.Release("menu");

        // The picture and the words are in place before the icon shows.
        icon = parts.NewIcon(trayMenu.Menu);
        slot = new TrayIconSlot(made => icon.Icon = made);
        icon.Clicked += OpenSettings;
        ShowView();
        icon.Visible = true;

        notes = new TrayNotes(watcher, toast, actions, () => this.config, Post);
        notes.Attach();

        // After the notes' window exists, so these call back on this thread.
        try
        {
            configWatcher = new ConfigWatcher(parts.ConfigPath);
            configWatcher.Changed += ReloadConfig;
        }
        catch (Exception e)
        {
            Log.Write("config.json is not watched for changes: " + e.Message);
        }
        if (parts.ListenToWindows)
        {
            try
            {
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                SystemEvents.SessionEnding += OnSessionEnding;
                SystemEvents.SessionEnded += OnSessionEnded;
                listening = true;
            }
            catch (Exception e)
            {
                Log.Write("Windows' notices are not heard: " + e.Message);
            }
        }

        // The first read happens whatever the take-over check does, or the row stays on Reading.
        try
        {
            startup.TakeOverIfMissing();
        }
        catch (Exception e)
        {
            Log.Write("start with Windows could not be checked for a copy that is gone: " + e.Message);
        }
        try
        {
            startup.Refresh();
        }
        catch (Exception e)
        {
            Log.Write("start with Windows could not be read: " + e.Message);
        }

        // The icon, the tooltip and the settings window's status follow the watcher's every tick,
        // and every pause, turn back on and lock now at once.
        watcher.Updated += RefreshView;
        watcher.TicksFailing += OnTicksFailing;
        parts.StartWatcher(watcher);

        // On from the start, except on a run that opens with the welcome (D46): the welcome says
        // nothing of the shortcut, and taking it from Windows can take it from another app before the
        // person knows SteppedOut is there. It is taken once the welcome finishes and saves; a quit
        // before that leaves it off. D9 only makes it on by default.
        shortcutWaits = !config.Welcomed && !configBroken;
        try
        {
            hotKey = parts.NewHotKey();
            hotKey.Pressed += OnHotKey;
            hotKey.ResultChanged += OnShortcutResult;
            if (shortcutWaits) Log.Write("lock shortcut: taken once the welcome window is finished");
            else hotKey.Apply(config.LockShortcut);
            layoutsSeen = parts.Layouts.Installed();
        }
        catch (Exception e)
        {
            Log.Write("lock shortcut not set up: " + e.Message);
        }
        try
        {
            controller = parts.NewController(watcher.NoteInput);
            controller.SetEnabled(config.CountController);
        }
        catch (Exception e)
        {
            Log.Write("game controller not set up: " + e.Message);
        }

        // A config file that did not load is not a first run: the welcome window would save over it.
        if (!config.Welcomed && !configBroken) ShowWelcome();
        if (configBroken) SayConfigNotUsed();
        else if (configSkipped) SayConfigSkipped();
        ShowView();
    }

    // ---- For the tests ----

    internal TrayMenu Menu => trayMenu;
    internal Toast Toast => toast;
    internal ITrayIcon TrayIcon => icon;
    internal TrayIconSlot Slot => slot;
    internal TrayNotes Notes => notes;
    internal TrayAutostart Startup => startup;
    internal HotKey? HotKey => hotKey;
    internal ControllerInput? Controller => controller;
    internal AwayActions Actions => actions;
    internal SettingsForm? Settings => settings;
    internal WelcomeForm? Welcome => welcome;
    internal Config Config => config;
    internal bool Quitting => quitting;

    // Windows' notices (settings, signing out) are listened to.
    internal bool Listening => listening;

    // What the tray showed last.
    internal TrayViewState? LastView { get; private set; }

    // How many times settings were applied.
    internal int Applied { get; private set; }

    // How many times a note said the settings could not be saved.
    internal int SaveFailedNotes { get; private set; }

    // ---- ISettingsHost ----

    public void PauseChosen(TimeSpan? length)
    {
        if (Stopping) return;
        watcher.Pause(length);
    }

    // Custom… in the tray menu's pause list: the Pause for window, in the middle of the screen.
    void PauseForChosen()
    {
        if (Stopping) return;
        PauseForWindow.Open(null, Lang.Current, length => PauseChosen(length));
    }

    public void ResumeChosen()
    {
        if (Stopping) return;
        watcher.Resume();
    }

    // The menu, the status box and the shortcut all come here: one path, so the away sound plays
    // for each (D12).
    public void LockNowChosen()
    {
        if (Stopping) return;
        watcher.LockNow();
    }

    // Snooze this reminder in the tray menu: what the note's own Snooze does, and the note goes.
    void SnoozeChosen()
    {
        if (Stopping) return;
        toast.PressSnooze();
    }

    // Never locks and never touches the countdown; a real countdown showing wins.
    public void TryItChosen()
    {
        if (Stopping) return;
        if (!toast.ShowPreview()) Log.Write("try it: a real countdown is showing");
    }

    public void PreviewSound(Sound sound)
    {
        if (Stopping) return;
        actions.PreviewSound(sound);
    }

    // Done when this returns, so a window that tries a shortcut reads how it went at once. No save
    // and no log line: the window tries shortcuts and puts the old one back through here.
    public void ShortcutChanged(string shortcut)
    {
        if (Stopping || hotKey is null) return;
        hotKey.Apply(shortcut);
    }

    // Opening lets go of the shortcut, so pressing it in the box types it there instead of locking
    // the PC (Windows hands a registered shortcut to this app, never to the window in front). The
    // window puts one back as it closes; settings applied meanwhile (a waiting edit, a changed file)
    // are not missed: their shortcut is taken after that, unless the window saved a new one, which
    // is newer and held already.
    public void ChoosingShortcut(bool open)
    {
        if (Stopping) return;
        if (open)
        {
            choosingShortcut = true;
            appliedWhileChoosing = false;
            keptWhileChoosing = null;
            hotKey?.Apply("");
            return;
        }
        if (!choosingShortcut) return;
        choosingShortcut = false;
        bool kept = keptWhileChoosing is not null;
        keptWhileChoosing = null;
        if (!appliedWhileChoosing) return;
        appliedWhileChoosing = false;
        if (!kept && !shortcutWaits) hotKey?.Apply(config.LockShortcut);
    }

    public void ShortcutKept(string shortcut)
    {
        if (choosingShortcut) keptWhileChoosing = shortcut;
    }

    // The window already handed the settings with the new language to ConfigEdited.
    public void LanguageChanged(string language)
    {
        if (Stopping) return;
        Relabel();
        ShowView();
    }

    public HotKeyResult ShortcutResult => hotKey?.Result ?? HotKeyResult.Ok;

    public event Action? ShortcutResultChanged;

    public IReadOnlyList<WhyLine> Explain() => Stopping ? [] : watcher.Explain();

    // While quitting the edit is only written: nothing is applied to parts on their way out.
    public void ConfigEdited(Config config)
    {
        if (disposed) return;
        Save(config, "settings changed");
    }

    // Kept for the next time the window opens. Saved as any edit is, but nothing is applied again
    // or logged, since how the app works did not change. While config.json did not load, or a change
    // to it is still being read, the tab is only kept in memory: a click on a tab never writes over a
    // hand edit. The next save writes it.
    public void TabChosen(int tab)
    {
        if (Stopping || tab == config.SettingsTab) return;
        var fresh = config.Copy();
        fresh.SettingsTab = tab;
        fresh.Clamp();
        if (configBroken || reload.Waiting)
        {
            config = fresh;
            return;
        }
        SaveWent(Write(fresh));
    }

    public bool StartsWithWindows => startup.State.IsOn();

    public AutostartState StartWithWindowsState => startup.State;

    public event Action? StartWithWindowsChanged;

    public void StartWithWindowsChosen(bool on)
    {
        if (Stopping) return;
        startup.Choose(on);
    }

    public LockStatus Status => watcher.LockStatus;

    // From the Extras tab, once its click is done.
    public void ShowWelcomeChosen() => Post(ShowWelcomeAgain);

    public void RemoveFilesChosen() => Post(RemoveFilesAndQuit);

    public bool SaveFailed => saveFailed;

    public event Action? StatusChanged;

    bool Stopping => quitting || disposed;

    // ---- What the tray shows ----

    TrayViewState View() =>
        TrayView.From(watcher.LockStatus, watcher.IsPaused, config, hotKey is { IsRegistered: true } key ? key.Shortcut : null, Lang.Current,
            toast.IsShowing && toast.SnoozeShown);

    // The icon and its words. The icon is drawn again only when it looks different.
    void ShowView()
    {
        var view = View();
        LastView = view;
        slot.Show(view.Icon, TrayIcons.SizeFor(TrayIcons.CurrentDpi()), Theme.TaskbarIsDark(), view.Minutes);
        icon.Text = view.Tooltip;
    }

    // After every tick of the watcher and at once after a pause, a turn back on, a lock now and new
    // settings (Watcher.Updated).
    internal void RefreshView()
    {
        if (Stopping) return;
        ShowView();
        // Opened and Closed can come apart; what is on screen decides.
        live.Set("menu", trayMenu.IsOpen);
        CheckLayouts();
        // Checked here, on the UI thread, rather than waited on from another one: a second a little
        // late is fine, and nothing can arrive after the tray has closed.
        if (showRequest is not null && showRequest.WaitOne(0)) ShowSelf();
        RaiseStatus();
    }

    // Each listener on its own: one that fails, such as a page on its way out, stops neither the
    // others nor the tray.
    void RaiseStatus()
    {
        if (StatusChanged is not { } listeners) return;
        foreach (var listener in listeners.GetInvocationList())
        {
            try
            {
                ((Action)listener)();
            }
            catch (Exception e)
            {
                if (statusFailed) continue;
                statusFailed = true;
                Log.Write("status not shown (said once): " + e);
            }
        }
    }

    // A keyboard layout added since the shortcut was checked may type it (D9): checked again at once,
    // not only when the settings open.
    void CheckLayouts()
    {
        if (hotKey is null || shortcutWaits) return;
        IReadOnlyList<IntPtr> now;
        try
        {
            now = parts.Layouts.Installed();
        }
        catch (Exception)
        {
            return;
        }
        if (layoutsSeen is { } seen && seen.Count == now.Count && now.All(seen.Contains)) return;
        layoutsSeen = now;
        hotKey.Recheck();
    }

    void Relabel()
    {
        if (Lang.Current == labeled) return;
        labeled = Lang.Current;
        trayMenu.Relabel(labeled);
    }

    // The exe was started again (see Program.AlreadyRunning). The icon is added again too, in case
    // Explorer was too busy to take it when this copy started and it never appeared.
    void ShowSelf()
    {
        Log.Write("started again: opening the settings");
        icon.Visible = false;
        icon.Visible = true;
        OpenSettings();
    }

    // ---- Notices from the parts and from Windows ----

    void OnHotKey()
    {
        if (Stopping) return;
        // A shortcut just tried in "Choose another shortcut…" is held until the window closes; pressed
        // meanwhile, it was meant for the box.
        if (choosingShortcut)
        {
            Log.Write("lock shortcut pressed while a new one is chosen: not locking");
            return;
        }
        Log.Write("lock shortcut pressed");
        watcher.LockNow();
    }

    // The watcher's once-a-second check keeps failing, so the PC may not lock when it should: a note
    // says so once, and stays until the check works again or it is clicked away. The log has the
    // error.
    internal void OnTicksFailing(bool failing)
    {
        if (Stopping) return;
        if (failing)
        {
            var v = Lang.Current.V1;
            failingNote = v.TicksFailingTitle;
            toast.ShowLastingNote(v.TicksFailingTitle, v.TicksFailingDetail);
        }
        else if (failingNote is { } shown)
        {
            toast.HideNote(shown);
            failingNote = null;
        }
    }

    void OnShortcutResult()
    {
        if (Stopping) return;
        ShortcutResultChanged?.Invoke();
    }

    void OnStartupChanged()
    {
        if (Stopping) return;
        if (settings is { IsDisposed: false } open) open.StartWithWindowsChanged();
        StartWithWindowsChanged?.Invoke();
    }

    void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => PreferenceChanged(e.Category);

    // The colors, the accent, the text size or the taskbar changed in Windows.
    internal void PreferenceChanged(UserPreferenceCategory category)
    {
        if (Stopping) return;
        if (category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.Accessibility)) return;
        if (Theme.Refresh())
        {
            trayMenu.Restyle();
            if (settings is { IsDisposed: false } open) open.RebuildIfStale();
        }
        // Drawn for the taskbar and the icon size as they are now.
        ShowView();
    }

    void OnSessionEnding(object sender, SessionEndingEventArgs e) => SessionEnding();

    // Windows may end the process soon after it signs out, so today's counts are saved now.
    internal void SessionEnding()
    {
        if (Stopping) return;
        watcher.SaveStats();
    }

    void OnSessionEnded(object sender, SessionEndedEventArgs e) => SessionEnded();

    // Windows signs out or shuts down and can end the process as soon as this returns, so this quits
    // as Quit does: today's counts first, then a waiting edit, which is only written (nothing is
    // applied to parts on their way out), then the rest of quitting if there is time.
    internal void SessionEnded()
    {
        if (Stopping) return;
        Log.Write("Windows is signing out or shutting down");
        Shutdown();
        ExitThread();
    }

    // ---- Windows ----

    internal void OpenSettings() => OpenSettings(null);

    // On the tab used last, or on this one when a place in the app names it. A window already open
    // keeps its tab unless one is named.
    internal void OpenSettings(int? tab)
    {
        if (Stopping) return;
        // Each window saves its own copy of the settings, so only one is open at a time. The
        // welcome's Open settings opens this after the welcome has closed.
        if (welcome is { IsDisposed: false } first)
        {
            parts.ShowWindow(first);
            return;
        }
        // A keyboard layout added since may type the shortcut: the shortcut's row on the Locking tab says so when it shows.
        hotKey?.Recheck();
        // Windows' Settings or Task Manager may have changed start with Windows since it was read.
        RereadStartup();
        if (settings is { IsDisposed: false } open)
        {
            open.Refill(config);
            if (tab is int named) open.OpenTab(named);
            if (open.WindowState == FormWindowState.Minimized) open.WindowState = FormWindowState.Normal;
            parts.ShowWindow(open);
            return;
        }
        var form = new SettingsForm(config, this);
        settings = form;
        if (tab is int chosen) form.OpenTab(chosen);
        // The status box shows the status live, and the Why window over it asks every second.
        live.Hold("settings");
        void Gone()
        {
            if (settings != form) return;
            settings = null;
            live.Release("settings");
        }
        form.FormClosed += (_, _) => Gone();
        form.Disposed += (_, _) => Gone();
        // Coming back to the window from Windows' Settings reads it again; the answer refills it.
        form.Activated += (_, _) =>
        {
            if (settings == form) RereadStartup();
        };
        parts.ShowWindow(form);
    }

    void RereadStartup()
    {
        if (Stopping) return;
        try
        {
            startup.Refresh();
        }
        catch (Exception e)
        {
            Log.Write("start with Windows could not be read: " + e.Message);
        }
    }

    // "Show the welcome again": one window at a time, so the settings window closes first, which
    // saves its waiting change, and the welcome opens on the settings in use, with start with
    // Windows as it is now. Closing the welcome keeps what it shows, as it always does.
    internal void ShowWelcomeAgain()
    {
        if (Stopping) return;
        if (welcome is { IsDisposed: false } open)
        {
            parts.ShowWindow(open);
            return;
        }
        if (settings is { IsDisposed: false } form)
        {
            foreach (var owned in form.OwnedForms) owned.Close();
            form.Close();
            if (!form.IsDisposed) form.Dispose();
        }
        settings = null;
        Log.Write("showing the welcome window again");
        ShowWelcome(StartsWithWindows);
    }

    // "Remove SteppedOut's files", asked and answered on the Extras tab: start with Windows is turned
    // off, the app quits as Quit does (today's counts and a waiting change are saved on the way), and
    // then nothing more is written: the files SteppedOut writes in its folder are deleted by name, and
    // the folder too once empty. A dry run only says what it would delete, and quits.
    internal void RemoveFilesAndQuit()
    {
        if (Stopping) return;
        string folder = Path.GetDirectoryName(parts.ConfigPath)!;
        Log.Write("removing SteppedOut's files and quitting");
        try
        {
            parts.Autostart.Choose(false);
        }
        catch (Exception e)
        {
            Log.Write("could not turn off start with Windows: " + e.Message);
        }
        Shutdown();
        if (options.DryRun)
        {
            OwnFiles.WouldRemove(folder, Log.Write);
        }
        else
        {
            watcher.ForgetToday();
            Log.Close();
            OwnFiles.Remove(folder, _ => { });
        }
        ExitThread();
    }

    void ShowWelcome(bool? startsWithWindows = null)
    {
        var form = new WelcomeForm(config.Copy(), startsWithWindows ?? true, this);
        welcome = form;
        form.Finished += (fresh, startWithWindows, openSettings) =>
        {
            if (Stopping) return;
            // Saved and applied with the shortcut taken (D46), then Windows gets the choice: On
            // points the Run entry at this copy, even when another copy's entry was there.
            shortcutWaits = false;
            Save(fresh, "welcome window closed");
            startup.Choose(startWithWindows);
            if (openSettings) Post(OpenSettings);
        };
        form.FormClosed += (_, _) =>
        {
            if (welcome == form) welcome = null;
        };
        parts.ShowWindow(form);
    }

    // Only a hand edit gets config.json wrong, and that person would otherwise wonder why nothing
    // changed. The details are in the log; the note says where to look.
    void SayConfigNotUsed()
    {
        var text = Lang.Current;
        toast.ShowNote(text.ConfigNotUsed, onDefaults ? text.ConfigDefaultsUntilFixed : text.ConfigEarlierUntilFixed, seconds: 15);
    }

    void SayConfigSkipped()
    {
        var text = Lang.Current;
        toast.ShowNote(text.ConfigSkipped, text.ConfigSkippedDetail, seconds: 15);
    }

    // ---- The settings ----

    void Save(Config fresh, string why)
    {
        bool written = Write(fresh);
        if (quitting)
        {
            Log.Write(why + (written ? ": saved on the way out" : ": not saved on the way out"));
            return;
        }
        ApplyConfig(why);
        SaveWent(written);
    }

    // Makes these the settings in use and writes them to config.json. False when Windows refused.
    bool Write(Config fresh)
    {
        config = fresh;
        onDefaults = false;
        string path = parts.ConfigPath;
        if (configBroken || reload.Waiting)
        {
            // The file that did not load, or has not loaded yet, is kept next to the new one, in
            // case it mattered.
            try
            {
                File.Copy(path, path + ".broken", overwrite: true);
                Log.Write("kept the config that did not load as config.json.broken");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Write("could not keep the old config: " + Config.Reason(e));
            }
            configBroken = false;
        }
        bool written = false;
        try
        {
            reload.Saved(config.Save(path));
            written = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            reload.Saved(null);
            Log.Write("could not save config: " + Config.Reason(e));
        }
        return written;
    }

    // A save Windows refuses (another program holds config.json, or the folder can't be written)
    // leaves the new settings in use until quitting. The person is told once, in a note and where
    // the settings window says changes are saved; the next save that works takes both back.
    void SaveWent(bool written)
    {
        if (written != saveFailed) return;
        saveFailed = !written;
        if (saveFailed)
        {
            var v = Lang.Current.V1;
            saveFailedNote = v.SaveFailedTitle;
            SaveFailedNotes++;
            toast.ShowNote(v.SaveFailedTitle, v.SaveFailedDetail, seconds: 15);
        }
        else if (saveFailedNote is { } shown)
        {
            toast.HideNote(shown);
            saveFailedNote = null;
        }
        if (settings is { IsDisposed: false } open) open.SaveStateChanged();
    }

    // first is the first read after a change on disk; a read that may work a moment later is
    // tried again until a newer change starts over.
    internal void ReloadConfig(bool first)
    {
        if (Stopping) return;
        var result = reload.Try(first, out var fresh, out string? problem, out string? warning);
        if (result == Reloaded.Unchanged) return;
        if (result == Reloaded.TryAgain)
        {
            // Once for each change, so the log says why even when a later try works.
            if (first) Log.Write(problem + "; trying again");
            configWatcher?.TryAgain();
            return;
        }
        if (result == Reloaded.Broken)
        {
            Log.Write(problem + (onDefaults ? "; still using the defaults" : "; keeping the earlier settings"));
            // A hand edit that did not load is kept as .broken by the next save, as at startup.
            configBroken = File.Exists(parts.ConfigPath);
            if (configBroken) SayConfigNotUsed();
            return;
        }
        // A note already up is in the language shown until now, which ApplyConfig may change.
        var shown = Lang.Current;
        // Settings that could not be saved give way to the file's when it really changed (ConfigReload
        // does not load it back otherwise), and the person is told, as they are lost.
        bool replaced = saveFailed && !fresh!.SameAs(config);
        if (replaced) Log.Write("config.json was changed outside SteppedOut, so the settings that could not be saved are replaced by it");
        config = fresh!;
        configBroken = false;
        onDefaults = false;
        ApplyConfig("config file changed");
        // The open window shows the file's settings, in its new language or look if they changed.
        if (settings is { IsDisposed: false } open) open.Refill(config);
        // The settings in use are the file's now, so none wait unsaved: a refused save's note and
        // footer line are taken back as a save that works takes them back.
        SaveWent(written: true);
        // The fix is in; a note saying otherwise would be out of date.
        toast.HideNote(shown.ConfigNotUsed);
        if (warning is not null)
        {
            Log.Write(warning);
            SayConfigSkipped();
        }
        else toast.HideNote(shown.ConfigSkipped);
        // Last, as the one note shown: what the person changed is gone.
        if (replaced)
        {
            var v = Lang.Current.V1;
            toast.ShowNote(v.UnsavedReplacedTitle, v.UnsavedReplacedDetail, seconds: 15);
        }
    }

    void ApplyConfig(string why)
    {
        watcher.Apply(ForThisRun(config));
        actions.Apply(config);
        bool newLanguage = Lang.Current != Lang.For(config.Language);
        Lang.Use(config.Language);
        toast.Use(config);
        controller?.SetEnabled(config.CountController);
        // Every keyboard layout is checked again too (D9). Not while a first run's welcome is open,
        // whose language switch saves too (D46), nor while a new shortcut is being chosen (I1).
        if (choosingShortcut) appliedWhileChoosing = true;
        else if (!shortcutWaits) hotKey?.Apply(config.LockShortcut);
        bool newLook = parts.ChooseTheme(config);
        if (Lang.Current != labeled) Relabel();
        else if (newLook) trayMenu.Restyle();
        // Built again in place, once the click or key that brought this has finished.
        if ((newLanguage || newLook) && settings is { IsDisposed: false } open) open.RebuildIfStale();
        Log.SetLimit(config.LogMaxKilobytes * 1024L);
        Log.Write($"{why}: idle {config.IdleMinutes} min, grace {config.GraceSeconds} s, lock {(config.LockWhenAway ? "on" : "off")}");
        Applied++;
        // The status under the new settings now, not at the next tick.
        watcher.Restate();
    }

    // A threshold given on the command line is for this run only: it reaches the watcher and is
    // never written into the file.
    Config ForThisRun(Config saved)
    {
        if (options.IdleMinutes is not double idle) return saved;
        var run = saved.Copy();
        run.IdleMinutes = idle;
        return run;
    }

    // ---- Later work and quitting ----

    // Runs next on the UI thread, after what is running now has finished; from any thread. Nothing
    // runs once the app is closing.
    void Post(Action next)
    {
        if (Stopping) return;
        void Guarded()
        {
            if (!Stopping) next();
        }
        if (parts.Post is { } queue)
        {
            queue(Guarded);
            return;
        }
        if (toast.IsDisposed || !toast.IsHandleCreated) return;
        try
        {
            toast.BeginInvoke(Guarded);
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    internal void Quit()
    {
        if (Stopping) return;
        Log.Write("quit from the menu");
        Shutdown();
        ExitThread();
    }

    // Once, from Quit or Dispose. Quitting is set first, so nothing that comes in meanwhile starts
    // anything. What matters most is saved first: today's counts and an edit still waiting.
    void Shutdown()
    {
        if (quitting) return;
        quitting = true;
        watcher.Updated -= RefreshView;
        watcher.TicksFailing -= OnTicksFailing;
        Step(watcher.SaveStats);
        // The dialogs over the settings window first, then the window: its waiting edit is saved.
        Step(CloseWindows);
        Step(() => hotKey?.Dispose());
        Step(() => controller?.Dispose());
        Step(live.ReleaseAll);
        Step(notes.Detach);
        Step(startup.Close);
        Step(() =>
        {
            if (!listening) return;
            listening = false;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.SessionEnding -= OnSessionEnding;
            SystemEvents.SessionEnded -= OnSessionEnded;
        });
        Step(() => configWatcher?.Dispose());
        Step(() => trayMenu.Menu.Close());
        // The tray lets go of the icon before the slot destroys it.
        Step(() => icon.Visible = false);
        Step(icon.Dispose);
        Step(slot.Dispose);
        Step(trayMenu.Dispose);
        Step(actions.Dispose);
        Step(toast.Dispose);
    }

    void CloseWindows()
    {
        if (settings is { IsDisposed: false } open)
        {
            foreach (var owned in open.OwnedForms) Step(owned.Close);
            Step(open.Close);
            if (!open.IsDisposed) Step(open.Dispose);
        }
        settings = null;
        Step(PauseForWindow.CloseOpen);
        // Quitting is not finishing the welcome: it shows again next time.
        if (welcome is { IsDisposed: false } first)
        {
            Step(first.CloseWithoutFinishing);
            if (!first.IsDisposed) Step(first.Dispose);
        }
        welcome = null;
    }

    static void Step(Action step)
    {
        try
        {
            step();
        }
        catch (Exception e)
        {
            Log.Write("while quitting: " + e.Message);
        }
    }

    // Called twice on Quit: once by WinForms when the message loop ends, once by Program's using.
    protected override void Dispose(bool disposing)
    {
        if (disposed) return;
        if (disposing) Shutdown();
        disposed = true;
        base.Dispose(disposing);
    }
}
