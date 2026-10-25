using System.Runtime.InteropServices;

namespace SteppedOut.Tests;

// A tray app made of stand-ins, for TrayAppTests and MenuLookTests. Nothing it makes locks, plays,
// registers a shortcut with Windows, writes the Run entry, reads a controller, shows a window or
// puts an icon in the tray: every part below is a fake, the watcher reads a made-up world on a
// clock the test moves, the settings file is in a folder of its own, and later work waits in a
// queue until the test runs it.
sealed class TrayRig : IDisposable
{
    readonly TimeSpan start = TimeSpan.FromHours(10);
    TrayApp? app;

    public TrayRig(Config? config = null, bool dryRun = true, double? idleOption = null, bool configBroken = false,
        bool configSkipped = false, bool writeConfig = true, Action<TrayRig>? before = null)
    {
        Folder = Path.Combine(Path.GetTempPath(), "SteppedOut.Tests", "tray-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        ConfigPath = Path.Combine(Folder, "config.json");
        Config = config ?? new Config { Welcomed = true };
        if (writeConfig) Config.Save(ConfigPath);
        Options = new Options { DryRun = dryRun, IdleMinutes = idleOption };
        Now = start;
        World.Clock = () => Now;
        // As Program makes it: with the --idle value for this run.
        var run = Config.Copy();
        if (idleOption is double idle) run.IdleMinutes = idle;
        Watcher = new Watcher(run, dryRun, new WatchSeams
        {
            Sample = () =>
            {
                var sample = new InputSample(Idle, Idle, Input);
                Input = false;
                return sample;
            },
            Monotonic = () => Now,
            Wall = () => Wall,
            RequestLock = () =>
            {
                LockRequests++;
                return true;
            },
            AcceptsNotifications = () => true,
            SessionIsLocked = () => false,
            Sources = World.Sources,
            StatsPath = Path.Combine(Folder, "today.json"),
        });
        Watcher.WentAway += (locking, _) => Events.Add(locking ? "away locking" : "away");
        Watcher.CameBack += _ => Events.Add("back");
        Away = new AwayFakes.Rig();
        ShowRequest = new EventWaitHandle(false, EventResetMode.AutoReset);
        Parts = new TrayParts
        {
            NewHotKey = () =>
            {
                Steps.Add("hotkey");
                return new HotKey(Layouts, Keys);
            },
            Layouts = Layouts,
            NewController = note =>
            {
                Steps.Add("controller");
                ControllerNote = note;
                return new ControllerInput(note, Pads);
            },
            NewActions = dry =>
            {
                Steps.Add("actions");
                return new AwayActions(dry, Away.Parts);
            },
            NewToast = () =>
            {
                Steps.Add("toast");
                return new Toast(headless: true);
            },
            NewIcon = menu =>
            {
                Steps.Add("icon");
                Icon.Menu = menu;
                return Icon;
            },
            Autostart = Autostart,
            ChooseTheme = c =>
            {
                Steps.Add("theme");
                return Theme.Choose(c);
            },
            ConfigPath = ConfigPath,
            ShowWindow = form =>
            {
                Steps.Add("window " + form.GetType().Name);
                Shown.Add(form);
                _ = form.Handle;
            },
            StartWatcher = watcher =>
            {
                Steps.Add("watcher");
                if (StartWatcherForReal) watcher.Start();
            },
            ListenToWindows = false,
            Post = action =>
            {
                lock (Posted) Posted.Add((action, Thread.CurrentThread.IsThreadPoolThread));
            },
        };
        Icon.Steps = Steps;
        Keys.Steps = Steps;
        Autostart.Steps = Steps;
        before?.Invoke(this);
        StartedWith = Config;
        if (!SkipStart) Start(configBroken, configSkipped);
    }

    public string Folder { get; }

    public string ConfigPath { get; }

    public Config Config { get; }

    public Config StartedWith { get; private set; }

    public Options Options { get; }

    public TimeSpan Now { get; set; }

    public DateTime Wall { get; set; } = new(2026, 10, 20, 9, 0, 0);

    public double Idle { get; set; }

    public bool Input { get; set; }

    public int LockRequests { get; private set; }

    public FakeWorld World { get; } = new();

    public Watcher Watcher { get; }

    public AwayFakes.Rig Away { get; }

    public EventWaitHandle ShowRequest { get; }

    public TrayParts Parts { get; set; }

    public FakeTrayIcon Icon { get; } = new();

    public FakeKeys Keys { get; } = new();

    public FakeLayouts Layouts { get; } = new();

    public FakePads Pads { get; } = new();

    public FakeAutostart Autostart { get; } = new();

    public Action<long>? ControllerNote { get; private set; }

    // What happened at start-up and after, in order, as the fakes saw it.
    public List<string> Steps { get; } = new();

    // What the watcher raised.
    public List<string> Events { get; } = new();

    public List<Form> Shown { get; } = new();

    public List<(Action Action, bool FromPool)> Posted { get; } = new();

    public bool StartWatcherForReal { get; set; }

    // Set in before, to start the app later with Start.
    public bool SkipStart { get; set; }

    public TrayApp App => app ?? throw new InvalidOperationException("not started");

    public void Start(bool configBroken = false, bool configSkipped = false) =>
        app = new TrayApp(Watcher, Config, Options, configBroken, configSkipped, ShowRequest, Parts);

    // Runs what was posted, and what that posts, as the message loop would.
    public int Drain()
    {
        int ran = 0;
        while (true)
        {
            Action next;
            lock (Posted)
            {
                if (Posted.Count == 0) return ran;
                next = Posted[0].Action;
                Posted.RemoveAt(0);
            }
            next();
            ran++;
        }
    }

    // Waits for work another thread posts, then runs it.
    public void DrainWhen(Func<bool> posted, string what)
    {
        Assert.IsTrue(SpinWait.SpinUntil(() =>
        {
            lock (Posted) return posted();
        }, TimeSpan.FromSeconds(10)), what);
        Drain();
    }

    // One second with nobody at the PC.
    public void Second()
    {
        Now += TimeSpan.FromSeconds(1);
        Wall = Wall.AddSeconds(1);
        Idle++;
        Watcher.Tick();
    }

    // Seconds go by until the idle time reaches idle.
    public void Until(double idle)
    {
        while (Idle < idle) Second();
    }

    // Someone is at the PC again.
    public void Busy()
    {
        Now += TimeSpan.FromSeconds(1);
        Wall = Wall.AddSeconds(1);
        Idle = 0;
        Input = true;
        Watcher.Tick();
    }

    // Time that passes with no tick.
    public void Jump(TimeSpan by)
    {
        Now += by;
        Wall = Wall.Add(by);
    }

    public Config Saved() => Config.Load(ConfigPath, out _, out _);

    public void Dispose()
    {
        try
        {
            app?.Dispose();
        }
        finally
        {
            Watcher.Dispose();
            Away.Dispose();
            ShowRequest.Dispose();
            try
            {
                Directory.Delete(Folder, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

// The tray icon, as far as the app can tell: what it was given, and a left click.
sealed class FakeTrayIcon : ITrayIcon
{
    public List<string>? Steps { get; set; }

    public ContextMenuStrip? Menu { get; set; }

    public int IconsShown { get; private set; }

    public List<bool> VisibleSets { get; } = new();

    public bool Disposed { get; private set; }

    public Icon? Icon
    {
        get => field;
        set
        {
            if (IconsShown == 0) Steps?.Add("picture");
            IconsShown++;
            field = value;
        }
    }

    public string Text
    {
        get => field;
        set
        {
            // NotifyIcon refuses more.
            if (value.Length > 127) throw new ArgumentOutOfRangeException(nameof(value), value.Length, "tooltip too long");
            field = value;
        }
    } = "";

    public bool Visible
    {
        get => field;
        set
        {
            if (value && VisibleSets.Count == 0) Steps?.Add("visible");
            VisibleSets.Add(value);
            field = value;
        }
    }

    public event Action? Clicked;

    public void Click() => Clicked?.Invoke();

    public void Dispose() => Disposed = true;
}

// RegisterHotKey as a test sees it: what is held, and errors on demand. Nothing reaches Windows.
sealed class FakeKeys : IHotKeyApi
{
    public const int Taken = 1409;

    public List<string>? Steps { get; set; }

    public (uint Modifiers, uint Key)? Held { get; private set; }

    public int Registers { get; private set; }

    public int Unregisters { get; private set; }

    // The error Register gives for a shortcut, or 0.
    public Func<Shortcut, int> Error { get; set; } = _ => 0;

    public int Register(IntPtr window, int id, uint modifiers, uint virtualKey)
    {
        Steps?.Add("register");
        Registers++;
        var shortcut = new Shortcut((ShortcutModifiers)(modifiers & 0xF), (Keys)virtualKey);
        int error = Error(shortcut);
        if (error != 0) return error;
        Held = (modifiers & 0xF, virtualKey);
        return 0;
    }

    public void Unregister(IntPtr window, int id)
    {
        Unregisters++;
        Held = null;
    }

    // Whether exactly this shortcut is held, or none for "".
    public bool Holds(string shortcut) =>
        Shortcut.Parse(shortcut) is Shortcut s ? Held == ((uint)s.Modifiers, s.VirtualKey) : Held is null;
}

// Keyboard layouts as a test sets them. A Polish layout types ł with Ctrl+Alt+L.
sealed class FakeLayouts : IKeyboardLayouts
{
    public static readonly IntPtr English = new(0x04090409);
    public static readonly IntPtr Polish = new(0x04150415);

    public List<IntPtr> List { get; } = [English];

    public int Asked { get; private set; }

    public IReadOnlyList<IntPtr> Installed()
    {
        Asked++;
        return List.ToList();
    }

    public string Typed(IntPtr layout, Shortcut s) =>
        layout == Polish && s.HasCtrl && s.HasAlt && s.Key == System.Windows.Forms.Keys.L ? "ł" : "";
}

// No controller, unless a test plugs one in.
sealed class FakePads : IPads
{
    public bool TryRead(int slot, out PadState state)
    {
        state = default;
        return false;
    }
}

// Start with Windows in memory. Slow makes the app ask it off the UI thread; Gate holds those calls
// until the test lets them go.
sealed class FakeAutostart : IAutostart
{
    public List<string>? Steps { get; set; }

    public AutostartState State { get; set; } = AutostartState.Off;

    public bool Slow { get; set; }

    public bool Missing { get; set; }

    public ManualResetEventSlim Gate { get; } = new(true);

    public List<bool> Chosen { get; } = new();

    public int Reads { get; private set; }

    public AutostartState Read()
    {
        Steps?.Add("read");
        Gate.Wait(TimeSpan.FromSeconds(10));
        Reads++;
        return State;
    }

    public AutostartState Choose(bool on)
    {
        Gate.Wait(TimeSpan.FromSeconds(10));
        lock (Chosen) Chosen.Add(on);
        State = on ? AutostartState.On : AutostartState.Off;
        return State;
    }

    public bool Throws { get; set; }

    public bool PointsAtMissingFile()
    {
        Steps?.Add("take over?");
        if (Throws) throw new System.Security.SecurityException("the Run key is locked down");
        return Missing;
    }
}

static class TrayWin
{
    public static int Gdi => (int)GetGuiResources(Process(), 0);

    public static int User => (int)GetGuiResources(Process(), 1);

    public static void Settle()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    // WM_HOTKEY, as Windows sends it when the shortcut is pressed.
    public static void Press(HotKey key) => SendMessage(key.WindowHandle, HotKey.WmHotKey, new IntPtr(0x5E01), IntPtr.Zero);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    static IntPtr Process() => GetCurrentProcess();

    [DllImport("user32.dll")]
    static extern uint GetGuiResources(IntPtr process, uint flags);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
