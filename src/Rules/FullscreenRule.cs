using System.Runtime.InteropServices;

namespace SteppedOut;

// A foreground window that covers its whole monitor is taken to be a game, a video or a presentation.
sealed class FullscreenRule : Rule
{
    const uint SpiGetScreenSaverRunning = 0x0072;
    const int GwlStyle = -16;
    const uint WsCaption = 0x00C00000;
    const uint WsThickFrame = 0x00040000;

    // Windows that always span the monitor and mean the opposite of "busy": the desktop, the
    // taskbar and the screen saver.
    static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "WindowsScreenSaverClass",
    };

    // Programs whose fullscreen window is not a game or a video. While the lock screen is up there
    // is nothing left to lock. Windows' own views cover the monitor with no caption too: Task View
    // and File Explorer in F11 (explorer), the full-screen Start menu and search; left open with
    // nobody there, they would stop the lock. A remote desktop or virtual machine window is work on
    // another computer, and this one still has to lock when the person at it walks away. Taking a
    // screenshot covers the monitor for a moment with Windows' screenshot overlay, drawn by the
    // Snipping Tool or, where it is not installed, Windows' own capture host.
    static readonly HashSet<string> NotPlaying = new(StringComparer.OrdinalIgnoreCase)
    {
        "LockApp",
        "SnippingTool",
        "ScreenClippingHost",
        "explorer",
        "StartMenuExperienceHost",
        "ShellExperienceHost",
        "SearchHost",
        "SearchApp",
        "SearchUI",
        "mstsc",
        "msrdc",
        "vmconnect",
        "vmware",
        "vmplayer",
        "vmrc",
        "VirtualBoxVM",
    };

    public override string Name => "fullscreen";

    public Effect Effect { get; set; }

    public override void Check(Scene scene, List<WhyLine> lines)
    {
        if (scene.Read.FullScreen() is string process) lines.Add(new WhyLine(Signal.FullScreen, Effect, process));
    }

    // The program whose window covers its whole monitor in the foreground, or null. No foreground
    // window at all, as for a moment while switching windows, reads as none; the evaluator holds a
    // full screen seen a moment ago for a few seconds, so that moment never starts a countdown.
    public static string? Read()
    {
        // A screen saver covers the monitor too, but it only starts once nobody is there. Not every
        // .scr uses the standard window class, so Windows is asked as well.
        if (SystemParametersInfo(SpiGetScreenSaverRunning, 0, out int saverRunning, 0) && saverRunning != 0) return null;

        IntPtr window = Native.GetForegroundWindow();
        if (window == IntPtr.Zero) return null;
        if (IsShellWindow(Native.ClassName(window))) return null;

        IntPtr monitor = Native.MonitorFromWindow(window, Native.MonitorDefaultToNearest);
        var info = new Native.MONITORINFO { cbSize = (uint)Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(monitor, ref info)) return null;

        // DWM's frame bounds leave out the invisible resize borders that GetWindowRect includes; with
        // those borders every maximized window would look bigger than its monitor.
        if (Native.DwmGetWindowAttribute(window, Native.DwmwaExtendedFrameBounds, out Native.RECT bounds, (uint)Marshal.SizeOf<Native.RECT>()) != 0
            && !Native.GetWindowRect(window, out bounds))
        {
            return null;
        }

        if (!Covers(bounds, info.rcMonitor)) return null;

        // On a monitor with no taskbar, or one set to hide, a maximized window fills the monitor
        // as well. Games, players and browsers drop the title bar and resize border when they go
        // fullscreen; an ordinary maximized window keeps them.
        if (IsOrdinaryMaximized(IsZoomed(window), (uint)Native.GetWindowLong(window, GwlStyle))) return null;

        Native.GetWindowThreadProcessId(window, out uint pid);
        string process = Native.ProcessName(pid);
        return IsNotPlaying(process) ? null : process;
    }

    internal static bool IsShellWindow(string className) => ShellClasses.Contains(className);

    internal static bool IsNotPlaying(string process) => NotPlaying.Contains(process);

    internal static bool Covers(Native.RECT window, Native.RECT monitor) =>
        window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;

    internal static bool IsOrdinaryMaximized(bool zoomed, uint style) =>
        zoomed && ((style & WsCaption) == WsCaption || (style & WsThickFrame) != 0);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SystemParametersInfo(uint action, uint param, out int value, uint winIni);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsZoomed(IntPtr window);
}
