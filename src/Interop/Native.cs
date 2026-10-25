using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SteppedOut;

static class Native
{
    public const uint AttachParentProcess = unchecked((uint)-1);
    public const uint WtsCurrentSession = 0xFFFFFFFF;
    public const int WtsSessionInfoEx = 25;
    public const uint MonitorDefaultToNearest = 2;
    public const uint DwmwaExtendedFrameBounds = 9;
    public const uint DwmwaCloaked = 14;
    public const int WsExToolWindow = 0x80;
    public const int SystemExecutionState = 16;
    public const uint WmSysCommand = 0x0112;
    public const int WmLButtonDown = 0x0201;
    public const int WmLButtonUp = 0x0202;
    public const int WmLButtonDoubleClick = 0x0203;
    public const int WmSetRedraw = 0x000B;
    public const int GwlStyle = -16;
    public const int WsVisible = 0x10000000;
    // RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_FRAME: the window, everything on it and its scroll bar.
    public const uint RdwRepaintAll = 0x0001 | 0x0004 | 0x0080 | 0x0400;
    public const int ScMonitorPower = 0xF170;
    const int QunsAcceptsNotifications = 5;
    const uint DwmwaUseImmersiveDarkMode = 20;
    const uint DwmwaUseImmersiveDarkModeOld = 19;
    const uint DwmwaWindowCornerPreference = 33;
    const int DwmwcpRoundSmall = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    // When Windows last saw keyboard or mouse input, on its 32-bit tick clock.
    public static uint? LastInputTick()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? info.dwTime : null;
    }

    // A process's program name, or "pid N" once it has exited.
    public static string ProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return "pid " + pid;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool LockWorkStation();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr window, out RECT rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr window, StringBuilder buffer, int capacity);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out RECT value, uint size);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out int value, uint size);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    public static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachConsole(uint processId);

    [DllImport("powrprof.dll")]
    public static extern uint CallNtPowerInformation(int level, IntPtr input, uint inputSize, out uint output, uint outputSize);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSQuerySessionInformationW(IntPtr server, uint sessionId, int infoClass, out IntPtr buffer, out uint bytes);

    [DllImport("wtsapi32.dll")]
    public static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RedrawWindow(IntPtr window, IntPtr update, IntPtr region, uint flags);

    [DllImport("shell32.dll")]
    static extern int SHQueryUserNotificationState(out int state);

    [DllImport("kernel32.dll")]
    public static extern ushort GetUserDefaultUILanguage();

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, uint attribute, ref int value, uint size);

    // Dark title bar: one attribute since Windows 10 20H1, an older number on the builds before it.
    public static void UseDarkTitleBar(IntPtr window, bool dark)
    {
        int value = dark ? 1 : 0;
        if (DwmSetWindowAttribute(window, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
            DwmSetWindowAttribute(window, DwmwaUseImmersiveDarkModeOld, ref value, sizeof(int));
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr window, string? subAppName, string? subIdList);

    // The dark scroll bars File Explorer uses. Windows versions without them keep the light ones.
    public static void UseDarkScrollBars(IntPtr window) => SetWindowTheme(window, "DarkMode_Explorer", null);

    // Small rounded corners on Windows 11; earlier versions ignore the attribute.
    public static void RoundCorners(IntPtr window)
    {
        int corner = DwmwcpRoundSmall;
        DwmSetWindowAttribute(window, DwmwaWindowCornerPreference, ref corner, sizeof(int));
    }

    // False during a fullscreen game or video, a presentation, or Focus / Do not disturb.
    public static bool AcceptsNotifications() =>
        SHQueryUserNotificationState(out int state) != 0 || state == QunsAcceptsNotifications;

    public static string ClassName(IntPtr window)
    {
        var buffer = new StringBuilder(256);
        int length = GetClassName(window, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : "";
    }

    // Every process on the PC with its program name ("chrome", without ".exe") and the sign-in
    // session it runs in. The name and session come from the same list as the id, so a program
    // that runs as administrator is listed like any other.
    public static IReadOnlyList<RunningProcess> Processes()
    {
        var list = new List<RunningProcess>();
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    list.Add(new RunningProcess(process.Id, process.ProcessName, process.SessionId));
                }
                catch (InvalidOperationException)
                {
                    // Exited while being looked at.
                }
            }
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
        return list;
    }

    // Each process's parent, by id. A parent that has exited may have handed its id on to another
    // program, so a caller follows only a few steps up.
    public static IReadOnlyDictionary<int, int> ParentIds()
    {
        var parents = new Dictionary<int, int>();
        IntPtr snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot == InvalidHandle || snapshot == IntPtr.Zero) return parents;
        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            for (bool more = Process32FirstW(snapshot, ref entry); more; more = Process32NextW(snapshot, ref entry))
            {
                parents[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return parents;
    }

    // Every top-level window with what can be seen of it. A Store app's window belongs to
    // ApplicationFrameHost; its ProcessId here is the app's own, taken from the app's window inside
    // the frame, so "Calculator on screen" and closing Calculator's windows both find it.
    public static IReadOnlyList<TopWindow> TopLevelWindows()
    {
        var windows = new List<TopWindow>();
        foreach (var window in Collect(callback => EnumWindows(callback, IntPtr.Zero)))
        {
            GetWindowThreadProcessId(window, out uint pid);
            if (pid == 0) continue;
            if (!IsWindowVisible(window))
            {
                windows.Add(new TopWindow(window, (int)pid, false, false, false, false, false));
                continue;
            }
            bool cloaked = DwmGetWindowAttribute(window, DwmwaCloaked, out int cloak, sizeof(int)) == 0 && cloak != 0;
            bool tool = (GetWindowLong(window, GwlExStyle) & WsExToolWindow) != 0;
            bool onAMonitor = GetWindowRect(window, out RECT bounds) && bounds.Right > bounds.Left && bounds.Bottom > bounds.Top
                && MonitorFromRect(ref bounds, MonitorDefaultToNull) != IntPtr.Zero;
            int owner = (int)pid;
            if (ClassName(window) == FrameWindowClass && AppInFrame(window, pid) is int app) owner = app;
            windows.Add(new TopWindow(window, owner, true, IsIconic(window), cloaked, tool, onAMonitor));
        }
        return windows;
    }

    // The process of the first window inside a Store app's frame that is not the frame host's own.
    static int? AppInFrame(IntPtr frame, uint frameProcess)
    {
        foreach (var child in Collect(callback => EnumChildWindows(frame, callback, IntPtr.Zero)))
        {
            GetWindowThreadProcessId(child, out uint pid);
            if (pid != 0 && pid != frameProcess) return (int)pid;
        }
        return null;
    }

    // The windows an enumeration hands back. The callback only collects: a managed exception
    // thrown back through EnumWindows would end the process, so everything that can fail happens
    // after it returns. The delegate is kept alive until then.
    static List<IntPtr> Collect(Func<EnumWindowsProc, bool> enumerate)
    {
        var found = new List<IntPtr>();
        EnumWindowsProc callback = (window, _) =>
        {
            try
            {
                found.Add(window);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        };
        enumerate(callback);
        GC.KeepAlive(callback);
        return found;
    }

    // Plugged in or not, as Windows' battery icon shows it.
    public static PowerLineStatus PowerLine() => SystemInformation.PowerStatus.PowerLineStatus;

    // Whether the PC has a battery at all; a desktop has none. A battery Windows cannot read counts
    // as one.
    public static bool HasBattery()
    {
        var status = SystemInformation.PowerStatus.BatteryChargeStatus;
        return !status.HasFlag(BatteryChargeStatus.NoSystemBattery) || status.HasFlag(BatteryChargeStatus.Unknown);
    }

    // Windows' "Animation effects" setting (Accessibility > Visual effects). On when it cannot be read.
    public static bool AnimationsOn()
    {
        bool on = true;
        return !SystemParametersInfo(SpiGetClientAreaAnimation, 0, ref on, 0) || on;
    }

    const uint SpiGetClientAreaAnimation = 0x1042;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SystemParametersInfo(uint action, uint param, ref bool value, uint winIni);

    const uint Th32csSnapProcess = 0x00000002;
    static readonly IntPtr InvalidHandle = new(-1);
    const int GwlExStyle = -20;
    const uint MonitorDefaultToNull = 0;
    const string FrameWindowClass = "ApplicationFrameWindow";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    delegate bool EnumWindowsProc(IntPtr window, IntPtr data);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);
}

// A process and the sign-in session it runs in. Name is the program's name without ".exe".
readonly record struct RunningProcess(int Id, string Name, int Session);

// A top-level window. ProcessId is the app's own for a Store app (see Native.TopLevelWindows). Tool
// windows, such as a floating palette, have no taskbar button; OnAMonitor is false for a window
// with no size or one parked off every screen.
readonly record struct TopWindow(IntPtr Handle, int ProcessId, bool Visible, bool Minimized, bool Cloaked, bool Tool, bool OnAMonitor)
{
    // Any window that can be seen on a screen (D6): shown, not minimized, not hidden by the shell
    // (cloaked, as on another virtual desktop), with some of it on a monitor. A window behind
    // another one still counts, and so does a tool window.
    public bool OnScreen => Visible && !Minimized && !Cloaked && OnAMonitor;
}
