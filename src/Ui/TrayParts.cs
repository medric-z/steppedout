namespace SteppedOut;

// Everything the tray app makes or reaches outside itself, so the tests can give their own. The
// real parts are Real. While SessionLock.Blocked is set (the tests and the render tool, on a PC
// someone is using), Real itself is safe: no icon in the tray, no Run entry written, no window shown,
// no controller read, no shortcut registered, no sound played, and a settings file in a scratch
// folder, so a test that forgets one part still touches nothing of the person's.
sealed record TrayParts
{
    // The lock shortcut. HotKey() registers nothing while blocked.
    public required Func<HotKey> NewHotKey { get; init; }

    // The keyboard layouts, asked once a second whether one was added (D9).
    public required IKeyboardLayouts Layouts { get; init; }

    // A game controller counted as input, given Watcher.NoteInput.
    public required Func<Action<long>, ControllerInput> NewController { get; init; }

    // The away and back actions, for a dry run or not.
    public required Func<bool, AwayActions> NewActions { get; init; }

    // The corner notes. Their window is also the one later work is posted through.
    public required Func<Toast> NewToast { get; init; }

    // The icon in the notification area, with this menu.
    public required Func<ContextMenuStrip, ITrayIcon> NewIcon { get; init; }

    public required IAutostart Autostart { get; init; }

    // Theme.Choose: the look the settings ask for. True when it changed.
    public required Func<Config, bool> ChooseTheme { get; init; }

    public required string ConfigPath { get; init; }

    // Shows a window the tray opens (the settings, the welcome) and brings it to the front.
    public required Action<Form> ShowWindow { get; init; }

    // Watcher.Start: today's counts, the session switches and the watcher's timer.
    public required Action<Watcher> StartWatcher { get; init; }

    // Windows' color, sign-out and shutdown notices (SystemEvents).
    public required bool ListenToWindows { get; init; }

    // Runs an action on the UI thread later, for work that finishes on another thread and for
    // what has to wait for a click to finish. Null: through the notes' window.
    public Action<Action>? Post { get; init; }

    public static TrayParts Real => SessionLock.Blocked ? Blocked() : Normal();

    static TrayParts Normal() => new()
    {
        NewHotKey = PlainHotKey,
        Layouts = KeyboardLayouts.Instance,
        NewController = note => new ControllerInput(note),
        NewActions = PlainActions,
        NewToast = () => new Toast(),
        NewIcon = menu => new NotifyTrayIcon(menu),
        Autostart = new WindowsAutostart(),
        ChooseTheme = Theme.Choose,
        ConfigPath = AppInfo.ConfigPath,
        ShowWindow = form =>
        {
            form.Show();
            form.Activate();
        },
        StartWatcher = StartWatching,
        ListenToWindows = true,
    };

    static TrayParts Blocked() => new()
    {
        NewHotKey = PlainHotKey,
        Layouts = KeyboardLayouts.Instance,
        NewController = note => new ControllerInput(note, NoPads.Instance),
        NewActions = PlainActions,
        NewToast = () => new Toast(headless: true),
        NewIcon = _ => new InertTrayIcon(),
        Autostart = new MemoryAutostart(),
        ChooseTheme = Theme.Choose,
        ConfigPath = ScratchConfig(),
        // The window gets its handle, so it works as when shown, and is never shown.
        ShowWindow = form => _ = form.Handle,
        StartWatcher = StartWatching,
        ListenToWindows = true,
    };

    // Both refuse while blocked: HotKey registers nothing and AwayActions does nothing real.
    static HotKey PlainHotKey() => new();

    static AwayActions PlainActions(bool dryRun) => new(dryRun);

    static void StartWatching(Watcher watcher) => watcher.Start();

    // A settings file of this process's own, never the person's.
    static string ScratchConfig()
    {
        string folder = Path.Combine(Path.GetTempPath(), "SteppedOut-blocked", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "config.json");
    }

    // No controller is ever connected.
    internal sealed class NoPads : IPads
    {
        public static readonly NoPads Instance = new();

        public bool TryRead(int slot, out PadState state)
        {
            state = default;
            return false;
        }
    }
}
