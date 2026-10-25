namespace SteppedOut;

// The icon in the notification area, as the tray app uses it: the picture, the words on hover,
// whether it is there, and a left click. The real one is a NotifyIcon; the tests and a process that
// must not touch the tray use one that only remembers.
interface ITrayIcon : IDisposable
{
    Icon? Icon { get; set; }

    // At most 127 characters (NotifyIcon's limit); TrayIcons.Tooltip keeps to it.
    string Text { get; set; }

    bool Visible { get; set; }

    // A left click on the icon. The right click opens the menu by itself.
    event Action? Clicked;
}

sealed class NotifyTrayIcon : ITrayIcon
{
    readonly NotifyIcon icon;

    public NotifyTrayIcon(ContextMenuStrip menu)
    {
        icon = new NotifyIcon { Text = AppInfo.Name, ContextMenuStrip = menu };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) Clicked?.Invoke();
        };
    }

    public Icon? Icon
    {
        get => icon.Icon;
        set => icon.Icon = value;
    }

    public string Text
    {
        get => icon.Text;
        set => icon.Text = value;
    }

    public bool Visible
    {
        get => icon.Visible;
        set => icon.Visible = value;
    }

    public event Action? Clicked;

    public void Dispose() => icon.Dispose();
}

// Remembers what it was given and puts nothing in the tray: the tray app's icon while
// SessionLock.Blocked is set, so a test or the render tool never adds an icon to someone's taskbar.
sealed class InertTrayIcon : ITrayIcon
{
    public Icon? Icon { get; set; }

    public string Text { get; set; } = AppInfo.Name;

    public bool Visible { get; set; }

    public bool Disposed { get; private set; }

    public event Action? Clicked;

    // A left click, for the tests.
    internal void Click() => Clicked?.Invoke();

    public void Dispose()
    {
        Disposed = true;
        Visible = false;
    }
}
