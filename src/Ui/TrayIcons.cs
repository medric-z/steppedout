using System.Globalization;
using System.Runtime.InteropServices;

namespace SteppedOut;

// What the tray icon shows in 1.0 (D23): three states, no "holding" icon.
enum TrayIconState
{
    On,
    Paused,
    CountingDown,
}

// The tray icon (D23): the logo's padlock as pixel art (TrayArt), the minutes on it, the size the
// tray wants it at, and the tooltip.
static class TrayIcons
{
    // The state for a status. paused is Watcher.IsPaused, which changes at once; the status follows
    // up to a second later, so a status that still says paused, or not yet, is overruled by it.
    public static TrayIconState StateOf(LockStatus status, bool paused) =>
        paused ? TrayIconState.Paused : status.Kind == LockState.Countdown ? TrayIconState.CountingDown : TrayIconState.On;

    // The icon as a bitmap, size pixels square. minutes is what StatusText.IconMinutes gives: "1" to
    // "99" is drawn on the padlock in place of its keyhole; null, or anything else, draws the plain
    // icon. Paused never shows minutes.
    public static Bitmap Art(TrayIconState state, int size, bool darkTaskbar, string? minutes = null) =>
        TrayArt.Draw(state, size, darkTaskbar, state == TrayIconState.Paused ? null : Minutes(minutes));

    // "1" to "99" in plain digits, with no sign, space or leading zero, as IconMinutes writes them.
    internal static int? Minutes(string? text)
    {
        if (text is not { Length: 1 or 2 } || text[0] is < '1' or > '9') return null;
        if (text.Length == 2 && text[1] is < '0' or > '9') return null;
        return int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    // The size the tray wants an icon at this scaling: 16 px at 100%, 20 at 125%, 24 at 150%, 32 at
    // 200%. An icon drawn at exactly that size is not stretched again by the taskbar.
    public static int SizeFor(int dpi)
    {
        if (dpi <= 0) dpi = 96;
        try
        {
            int size = GetSystemMetricsForDpi(SmCxSmIcon, (uint)dpi);
            if (size > 0) return size;
        }
        catch (EntryPointNotFoundException)
        {
        }
        return (int)Math.Round(16.0 * dpi / 96, MidpointRounding.AwayFromZero);
    }

    // The scaling of the monitor the taskbar's notification area is on, which can differ from the
    // system's; the system's when it cannot be read.
    public static int CurrentDpi()
    {
        try
        {
            var tray = FindWindow("Shell_TrayWnd", null);
            if (tray != IntPtr.Zero)
            {
                uint dpi = GetDpiForWindow(tray);
                if (dpi > 0) return (int)dpi;
            }
        }
        catch (EntryPointNotFoundException)
        {
        }
        return Theme.SystemDpi;
    }

    // What hovering the icon shows: the app's name and the status headline (Strings.Tooltip).
    public static string Tooltip(Strings text, LockStatus status, bool lockingOn) => text.Tooltip(status, lockingOn);

    // At most room characters, cut between whole letters (never inside a surrogate pair or before a
    // mark that belongs to the letter) and ending in "…" when cut.
    internal static string Fit(string line, int room)
    {
        if (line.Length <= room) return line;
        int end = 0;
        while (end < line.Length)
        {
            int next = StringInfo.GetNextTextElementLength(line, end);
            if (end + next > room - 1) break;
            end += next;
        }
        return line[..end] + "…";
    }

    const int SmCxSmIcon = 49;

    [DllImport("user32.dll")]
    static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string className, string? windowName);
}
