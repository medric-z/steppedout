using System.Runtime.InteropServices;

namespace SteppedOut;

// Holds the one icon the tray shows and swaps it when the state, the size, the taskbar or the
// minutes change. Each icon is a Windows icon handle made from the pixel art; the new one is handed
// to the tray first and only then is the old one destroyed, so the tray never shows a destroyed
// icon and a minute changing every minute never leaks one.
sealed class TrayIconSlot : IDisposable
{
    readonly Action<Icon> show;
    Icon? current;
    (TrayIconState State, int Size, bool Dark, int? Minutes)? shown;
    // A failure is written to the log once, not every second while it lasts.
    bool failing;
    bool disposed;

    // show puts an icon in the tray, such as notify => notifyIcon.Icon = notify. It is called on
    // the thread that calls Show.
    public TrayIconSlot(Action<Icon> show) => this.show = show;

    // The icon in the tray now, or null before the first Show.
    public Icon? Current => current;

    // What happens to icon handles, in order: "create", "show", "destroy". For the tests.
    internal Action<string>? Trace { get; set; }

    // How many failures went to the log, for the tests.
    internal int FailuresLogged { get; private set; }

    // Shows the icon for these values unless it is already showing. Gives whether it changed. A
    // failure to make or show the new icon leaves the old one in the tray and gives false, so a
    // once-a-second refresh never stops on it; the log has the first failure of a run of them.
    public bool Show(TrayIconState state, int size, bool darkTaskbar, string? minutes)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var key = (state, size, darkTaskbar, state == TrayIconState.Paused ? null : TrayIcons.Minutes(minutes));
        if (current is not null && shown == key) return false;
        IntPtr handle = IntPtr.Zero;
        Icon? icon = null;
        try
        {
            using (var art = TrayIcons.Art(state, size, darkTaskbar, minutes))
                handle = art.GetHicon();
            Trace?.Invoke("create");
            icon = Icon.FromHandle(handle);
            show(icon);
            Trace?.Invoke("show");
        }
        catch (Exception e) when (e is ExternalException or ArgumentException or InvalidOperationException or ObjectDisposedException or OutOfMemoryException)
        {
            icon?.Dispose();
            if (handle != IntPtr.Zero) Destroy(handle);
            if (!failing)
            {
                failing = true;
                FailuresLogged++;
                Log.Write($"tray icon not changed: {e.GetType().Name}: {e.Message} (said once until it works again)");
            }
            return false;
        }
        failing = false;
        var old = current;
        current = icon;
        shown = key;
        if (old is not null) Release(old);
        return true;
    }

    // Icon.FromHandle does not own its handle, so disposing the Icon leaves it; it is destroyed here.
    void Release(Icon icon)
    {
        IntPtr handle = icon.Handle;
        icon.Dispose();
        Destroy(handle);
    }

    void Destroy(IntPtr handle)
    {
        DestroyIcon(handle);
        Trace?.Invoke("destroy");
    }

    // Destroys the icon shown last. The tray must have stopped showing it (the NotifyIcon hidden or
    // disposed) first.
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (current is not null) Release(current);
        current = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyIcon(IntPtr icon);
}
