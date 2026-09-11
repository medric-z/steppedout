using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SteppedOut;

// A foreground window that covers its whole monitor is taken to be a game, a video or a presentation.
sealed class FullscreenRule : ILockSuppressor
{
    // Windows that always span the monitor and mean the opposite of "busy": the desktop and the taskbar.
    static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
    };

    public string Name => "fullscreen";

    public string? Check()
    {
        IntPtr window = Native.GetForegroundWindow();
        if (window == IntPtr.Zero) return null;
        if (IsShellWindow(Native.ClassName(window))) return null;

        IntPtr monitor = Native.MonitorFromWindow(window, Native.MonitorDefaultToNearest);
        var info = new Native.MONITORINFO { cbSize = (uint)Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(monitor, ref info)) return null;

        // DWM's frame bounds leave out the invisible resize borders that GetWindowRect includes; with
        // those borders every maximized window would look bigger than its monitor. A maximized
        // window stops at the taskbar, so it does not count unless the taskbar is set to hide.
        if (Native.DwmGetWindowAttribute(window, Native.DwmwaExtendedFrameBounds, out Native.RECT bounds, (uint)Marshal.SizeOf<Native.RECT>()) != 0
            && !Native.GetWindowRect(window, out bounds))
        {
            return null;
        }

        if (!Covers(bounds, info.rcMonitor)) return null;

        // The lock screen is itself a fullscreen app; while it is up there is nothing left to lock.
        string process = ProcessNameOf(window);
        return process == "LockApp" ? null : "fullscreen app: " + process;
    }

    internal static bool IsShellWindow(string className) => ShellClasses.Contains(className);

    internal static bool Covers(Native.RECT window, Native.RECT monitor) =>
        window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;

    static string ProcessNameOf(IntPtr window)
    {
        Native.GetWindowThreadProcessId(window, out uint pid);
        try
        {
            return Process.GetProcessById((int)pid).ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return "pid " + pid;
        }
    }
}
