using System.Runtime.InteropServices;

namespace SteppedOut;

// What the tray menu shows. Paused is Watcher.IsPaused, which changes the moment Pause or Turn back
// on is chosen; Status follows up to a second later, so the Pause and Turn back on rows follow
// Paused, never Status.Kind. LockingOn is lockWhenAway. Shortcut is the lock
// shortcut while it is registered (HotKey.IsRegistered ? HotKey.Shortcut : null), so one that is
// off, taken by another app or typing a letter is never shown. Snooze is a break or eye-break note
// with Snooze showing now.
sealed record TrayMenuState(LockStatus Status, bool Paused, bool LockingOn, string? Shortcut, bool Snooze = false);

// The tray icon's menu, option 4 of the sketch (D24): a real Windows menu drawn in the settings
// colors. From the top: the status box (ring, headline, time away today), Snooze this reminder while
// a reminder note offers Snooze (the note never takes the keyboard, so this is how the keyboard and
// screen readers reach it), Pause with its list (or Turn back on while paused), Lock now with the
// shortcut, Settings, a line, and Quit SteppedOut.
// There is no Language row. The menu only raises events; it never pauses, locks or quits anything
// itself.
sealed class TrayMenu : IDisposable
{
    readonly ContextMenuStrip menu;
    readonly TrayStatusItem status = new();
    readonly TrayRow snooze = new(Glyphs.Timer);
    readonly TrayRow pause = new(Glyphs.Pause);
    readonly TrayRow turnBackOn = new(Glyphs.Play);
    readonly TrayRow lockNow = new(Glyphs.Lock);
    readonly TrayRow settings = new(Glyphs.Gear);
    readonly TrayLine line = new();
    readonly TrayRow quit = new(Glyphs.Exit) { Muted = true };
    // Widens the pause list's rows, which MenuLook makes, to the list's width (see Widen).
    readonly ToolStripLabel pauseSpacer = new() { AutoSize = false, Available = false };
    readonly System.Windows.Forms.Timer refresh = new() { Interval = 1000 };
    Strings text;
    TrayMetrics metrics;
    string plainShortcut = "";
    int fitted;
    bool pauseToLeft;
    bool statusClicked;
    bool disposed;

    public TrayMenu(Strings text)
    {
        this.text = text;
        metrics = new TrayMetrics(Theme.SystemDpi);
        menu = MenuLook.NewMenu(Direction(text));
        menu.Renderer = new TrayMenuLook();
        menu.AccessibleName = AppInfo.Name;
        menu.Items.AddRange(new ToolStripItem[] { status, snooze, pause, turnBackOn, lockNow, settings, line, quit });
        turnBackOn.Available = false;
        snooze.Available = false;

        var list = PauseList;
        list.Renderer = new MenuLook();
        list.ShowImageMargin = false;
        list.ShowCheckMargin = false;
        list.HandleCreated += (_, _) => Edge(list.Handle);
        pause.DropDownOpening += (_, _) => OpenPauseList();
        // After the list's last layout and before its window shows, so it is placed at the size it
        // is shown at.
        list.Opening += (_, _) => PlacePauseList();
        pause.DropDownPlace = PauseListLocation;

        snooze.Click += (_, _) => SnoozeChosen?.Invoke();
        turnBackOn.Click += (_, _) => TurnBackOnChosen?.Invoke();
        lockNow.Click += (_, _) => LockNowChosen?.Invoke();
        settings.Click += (_, _) => SettingsChosen?.Invoke();
        quit.Click += (_, _) => QuitChosen?.Invoke();

        // A click on the status box would close the menu like a click on a command; it is words, so
        // the menu stays open.
        menu.ItemClicked += (_, e) => statusClicked = e.ClickedItem == status;
        menu.Closing += (_, e) =>
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && statusClicked) e.Cancel = true;
            statusClicked = false;
        };
        menu.Opening += (_, _) => Opening();
        menu.Opened += (_, _) =>
        {
            refresh.Start();
            Opened?.Invoke();
        };
        menu.Closed += (_, _) =>
        {
            refresh.Stop();
            Closed?.Invoke();
        };
        refresh.Tick += (_, _) => Pull();
        Relabel(text);
    }

    // The menu, for NotifyIcon.ContextMenuStrip.
    public ContextMenuStrip Menu => menu;

    // Asked for what to show as the menu opens and once a second while it is open. Without it the
    // menu keeps what it showed last.
    public Func<TrayMenuState>? StateSource { get; set; }

    // A pause length was picked: 15 or 30 minutes, 1 or 2 hours, 1 day, or null for until turned
    // back on.
    public event Action<TimeSpan?>? PauseChosen;

    // Custom… in the pause list: the app opens the Pause for window.
    public event Action? PauseForChosen;

    public event Action? TurnBackOnChosen;

    // Snooze this reminder: as the note's own Snooze.
    public event Action? SnoozeChosen;

    public event Action? LockNowChosen;

    public event Action? SettingsChosen;

    public event Action? QuitChosen;

    // The menu is up, so what it shows is asked for every second; and it went away again.
    public event Action? Opened;

    public event Action? Closed;

    // Whether the menu is up now. Opened and Closed can come apart (a closing called off, a menu
    // Explorer tore down), so the tray also asks this once a second.
    public bool IsOpen => OpenForTests ?? menu.Visible;

    // The tests open and close the menu without showing it: null is the real menu.
    internal bool? OpenForTests { get; set; }

    internal void RaiseOpenedForTests()
    {
        OpenForTests = true;
        Pull();
        Opened?.Invoke();
    }

    internal void RaiseClosedForTests()
    {
        OpenForTests = false;
        Closed?.Invoke();
    }

    // The scaling the menu is drawn for: the monitor the pointer is on as it opens. The render tool
    // and the tests set it to draw the menu at other scalings.
    internal int Dpi
    {
        get => metrics.Dpi;
        set
        {
            if (value <= 0 || value == metrics.Dpi) return;
            metrics = new TrayMetrics(value);
            Restyle();
        }
    }

    internal TrayMenuState? State { get; private set; }

    internal TrayStatusItem StatusItem => status;
    internal TrayRow SnoozeRow => snooze;
    internal TrayRow PauseRow => pause;
    internal TrayRow TurnBackOnRow => turnBackOn;
    internal TrayRow LockNowRow => lockNow;
    internal TrayRow SettingsRow => settings;
    internal TrayRow QuitRow => quit;
    internal TrayLine Line => line;
    internal ToolStripDropDownMenu PauseList => (ToolStripDropDownMenu)pause.DropDown;

    // The rows of the pause list, without the spacer that widens them.
    internal IEnumerable<ToolStripItem> PauseChoices => PauseList.Items.Cast<ToolStripItem>().Where(i => i != pauseSpacer);

    // The words in another language. The pause list is the shared MenuLook.PauseChoices, made again.
    public void Relabel(Strings text)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        this.text = text;
        var direction = Direction(text);
        menu.RightToLeft = direction;
        PauseList.RightToLeft = direction;
        var v = text.V1;
        snooze.Text = v.SnoozeReminder;
        pause.Text = v.Pause;
        turnBackOn.Text = v.TurnBackOn;
        lockNow.Text = v.LockNow;
        settings.Text = v.Settings;
        quit.Text = v.QuitSteppedOut;

        var old = PauseList.Items.Cast<ToolStripItem>().Where(i => i != pauseSpacer).ToList();
        PauseList.Items.Clear();
        foreach (var item in old) item.Dispose();
        PauseList.Items.AddRange(MenuLook.PauseChoices(text, length => PauseChosen?.Invoke(length), () => PauseForChosen?.Invoke()));
        PauseList.Items.Add(pauseSpacer);

        if (State is { } state) Apply(state);
        Restyle();
    }

    // The colors, the text size and the scaling as they are now: after a theme, button color or
    // text size change, and when the menu moves to a monitor scaled differently.
    public void Restyle()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var m = metrics;
        menu.Renderer = new TrayMenuLook();
        menu.Font = m.Text;
        foreach (var row in Rows)
        {
            row.Metrics = m;
            row.Font = m.Text;
        }
        status.Metrics = m;
        line.Metrics = m;

        var list = PauseList;
        list.Renderer = new MenuLook();
        list.Font = m.Text;
        foreach (var item in PauseChoices)
        {
            item.Font = m.Text;
            if (item is ToolStripMenuItem) item.Padding = new Padding(0, m.Px(5), 0, m.Px(5));
        }
        Fit(force: true);
        Widen(list, m.PauseWidth);
        if (menu.IsHandleCreated) Edge(menu.Handle);
        if (list.IsHandleCreated) Edge(list.Handle);
        menu.Invalidate();
    }

    IEnumerable<TrayRow> Rows => [snooze, pause, turnBackOn, lockNow, settings, quit];

    // Shows a state: the status box, Pause or Turn back on, and the shortcut beside Lock now.
    internal void Apply(TrayMenuState state)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        State = state;
        var s = state.Status;
        status.Show(text.Headline(s, state.LockingOn), text.V1.AwayToday, text.AwayToday(s.Today.Away), Ring.FractionOf(s), TrayStatusItem.ToneOf(s));
        snooze.Available = state.Snooze;
        pause.Available = !state.Paused;
        turnBackOn.Available = state.Paused;
        // The arrow keys pass over the status box, so the first row also carries it for a screen
        // reader, which reads a row's description after its name.
        snooze.AccessibleDescription = status.Spoken;
        pause.AccessibleDescription = status.Spoken;
        turnBackOn.AccessibleDescription = status.Spoken;
        menu.AccessibleDescription = status.Spoken;

        var shortcut = state.Shortcut is { } keys ? Shortcut.Parse(keys) : null;
        string plain = shortcut?.ToString() ?? "";
        if (plain != plainShortcut)
        {
            plainShortcut = plain;
            // Display carries marks that keep it left to right inside Arabic words.
            lockNow.ShortcutKeyDisplayString = shortcut?.Display ?? "";
            lockNow.ShowShortcutKeys = shortcut is not null;
        }
        Fit();
        // ShortcutKeyDisplayString is not passed to screen readers, so the name carries it.
        lockNow.AccessibleName = plain.Length == 0 ? lockNow.Text : $"{lockNow.Text} {plain}";
    }

    // Which way the pause list opens: toward the middle of the screen, so it is never pushed back
    // over the menu at a screen edge. A tray on the right puts the menu on the right and the list to
    // its left; a tray on the left (a right-to-left Windows, or a taskbar moved left) the other way.
    public static ToolStripDropDownDirection PauseDirection(Rectangle menu, Rectangle workArea) =>
        menu.Left + menu.Width / 2 > workArea.Left + workArea.Width / 2 ? ToolStripDropDownDirection.Left : ToolStripDropDownDirection.Right;

    void OpenPauseList()
    {
        var shown = menu.Bounds;
        var direction = PauseDirection(shown, Screen.FromRectangle(shown).WorkingArea);
        pauseToLeft = direction == ToolStripDropDownDirection.Left;
        pause.DropDownDirection = direction;
    }

    // WinForms works out where the list goes from the size the list would like, and shows it at the
    // size it has, so a list left wider covered the menu's glyphs. As it opens, the list is given
    // the size it would like, which also places it again (PauseListLocation).
    void PlacePauseList()
    {
        var list = PauseList;
        var size = list.GetPreferredSize(Size.Empty);
        if (list.Size != size) list.Size = size;
    }

    // Asked by WinForms each time the open list is placed or sized: where the sketch has it.
    Point PauseListLocation()
    {
        var list = PauseList;
        var size = list.AutoSize ? list.GetPreferredSize(Size.Empty) : list.Size;
        var shown = menu.Bounds;
        int first = PauseChoices.FirstOrDefault(i => i.Available)?.Bounds.Top ?? 0;
        return PauseListBounds(shown, shown.Top + pause.Bounds.Top, size, first, pauseToLeft,
            Screen.FromRectangle(shown).WorkingArea, metrics.PauseGap).Location;
    }

    // Where the pause list goes, in screen pixels: beside the menu on the side it opens to, the
    // sketch's gap away from the menu's edge, its first choice level with Pause, and on the screen.
    internal static Rectangle PauseListBounds(Rectangle menu, int pauseTop, Size list, int firstChoiceTop, bool toLeft, Rectangle workArea, int gap)
    {
        int x = toLeft ? menu.Left - gap - list.Width : menu.Right + gap;
        int y = pauseTop - firstChoiceTop;
        x = Math.Max(workArea.Left, Math.Min(x, workArea.Right - list.Width));
        y = Math.Max(workArea.Top, Math.Min(y, workArea.Bottom - list.Height));
        return new Rectangle(x, y, list.Width, list.Height);
    }

    void Opening()
    {
        Dpi = DpiAt(Cursor.Position);
        Pull();
    }

    void Pull()
    {
        if (StateSource is { } source && !disposed) Apply(source());
    }

    // Rows as wide as the widest needs, and at least the sketch's 252 px; the status box as wide. The
    // status words widen it too, up to a limit past which a headline is cut with "…" (a very long
    // app name). While the menu is open it only grows, so it never jumps narrower and back as the
    // minutes count down. The menu is made that wide with MinimumSize, which alone would leave the
    // rows at their own width.
    void Fit(bool force = false)
    {
        var m = metrics;
        int width = m.MenuWidth;
        foreach (var row in Rows)
            width = Math.Max(width, m.WidthFor(TrayRow.Measure(row.Text ?? "", m.Text), row.TrailWidth));
        width = Math.Max(width, Math.Min(status.NeededMenuWidth, m.Px(MaxStatusWidth)));
        if (menu.Visible) width = Math.Max(width, fitted);
        if (width == fitted && !force) return;
        fitted = width;
        menu.MinimumSize = new Size(width, 0);
        foreach (var row in Rows) row.RowWidth = width - 1;
        status.RowWidth = width - 1;
        menu.PerformLayout();
        // The menu's own measure of its rows can come out wider still; the rows then follow it.
        int natural = menu.GetPreferredSize(Size.Empty).Width;
        if (natural > width)
        {
            fitted = natural;
            menu.MinimumSize = new Size(natural, 0);
            foreach (var row in Rows) row.RowWidth = natural - 1;
            status.RowWidth = natural - 1;
            menu.PerformLayout();
        }
        if (menu.Width < menu.MinimumSize.Width) menu.Size = new Size(menu.MinimumSize.Width, menu.GetPreferredSize(Size.Empty).Height);
    }

    // How wide the status words may make the menu before a headline is cut.
    const int MaxStatusWidth = 420;

    // A menu is as wide as its longest words, and MinimumSize makes the menu wider but not its rows.
    // A row that is never shown, as wide as the room left over, widens the column of words, so every
    // row and its fill spans the list (as DropDown does for its lists).
    void Widen(ToolStripDropDownMenu list, int width)
    {
        pauseSpacer.Size = new Size(0, 0);
        list.MinimumSize = Size.Empty;
        list.PerformLayout();
        int natural = list.GetPreferredSize(Size.Empty).Width;
        if (natural < width)
        {
            pauseSpacer.Width = natural;
            list.PerformLayout();
            int around = list.GetPreferredSize(Size.Empty).Width - natural;
            pauseSpacer.Width = Math.Max(0, width - around);
            list.PerformLayout();
        }
        list.MinimumSize = new Size(width, 0);
        // The list keeps the width of the first try above, wider than it needs, unless it is told
        // its size again; shown, it would then cover part of the menu.
        list.Size = list.GetPreferredSize(Size.Empty);
    }

    static RightToLeft Direction(Strings text) => text.RightToLeft ? RightToLeft.Yes : RightToLeft.No;

    // The scaling of the monitor at a point, or the system's when it cannot be read.
    internal static int DpiAt(Point point)
    {
        try
        {
            var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, MdtEffectiveDpi, out uint x, out _) == 0 && x > 0) return (int)x;
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException)
        {
        }
        return Theme.SystemDpi;
    }

    // Round corners and Windows 11's own edge in the menu's edge color, as MenuLook gives every menu
    // it makes; the pause list is not made by MenuLook, so it needs both. Again after a theme change.
    // High contrast keeps the system's edge. Earlier versions of Windows ignore both.
    static void Edge(IntPtr window)
    {
        int corner = DwmwcpRound;
        DwmSetWindowAttribute(window, DwmwaWindowCornerPreference, ref corner, sizeof(int));
        if (TrayMenuLook.Hc) return;
        int color = ColorTranslator.ToWin32(Theme.Current.ControlEdge);
        DwmSetWindowAttribute(window, DwmwaBorderColor, ref color, sizeof(int));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        refresh.Stop();
        refresh.Dispose();
        var list = PauseList;
        menu.Dispose();
        list.Dispose();
        pauseSpacer.Dispose();
    }

    const int DwmwaWindowCornerPreference = 33;
    const int DwmwaBorderColor = 34;
    const int DwmwcpRound = 2;
    const uint MonitorDefaultToNearest = 2;
    const int MdtEffectiveDpi = 0;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
}
