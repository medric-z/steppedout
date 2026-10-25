using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SteppedOut;

// One line of Windows' process list: the process, the one that started it, and its program's file
// name ("app.exe"). A parent number can belong to a later process once the parent has exited, so it
// is only trusted together with the start times.
readonly record struct ProcessEntry(int Id, int ParentId, string ExeFile);

// A window as Windows lists it, with the process that owns it.
readonly record struct AppWindow(IntPtr Handle, int ProcessId, string ClassName, string Title);

// What closing apps needs from Windows, so the tests can give a made-up list of processes and
// windows instead, and the real test can let through only the process it started itself.
interface IProcessApi
{
    int OwnId { get; }

    // The program this process runs from, whatever its file is called now.
    string? OwnPath { get; }

    IReadOnlyList<ProcessEntry> Snapshot();

    // The sign-in a process belongs to, or null when Windows won't say.
    int? SessionOf(int processId);

    // When a process started, in Windows' file-time units, or null when Windows won't say.
    long? StartedAt(int processId);

    // A handle that keeps naming this one process even after it exits, so a later wait or force
    // can never reach another process that got the same number. Null with Windows' error code
    // when it won't give one: 5 means the process runs as administrator or is otherwise protected.
    IProcessHandle? Open(int processId, out int error);

    // The visible top-level windows, in the order Windows stacks them, top first.
    IReadOnlyList<AppWindow> Windows();

    // The windows inside one window, for a Store app's frame.
    IReadOnlyList<AppWindow> ChildrenOf(IntPtr window);

    // Posts WM_CLOSE, the same request as the window's close button. Never waits for an answer. The
    // real one throws instead while SessionLock.Blocked is set, unless the window belongs to a
    // program a test built for itself.
    bool PostClose(IntPtr window);
}

interface IProcessHandle : IDisposable
{
    int Id { get; }

    // Read through the handle itself, so they describe this process and no other.
    string? ImagePath { get; }

    long? StartedAt { get; }

    // Whether the process runs as administrator, read from its token through the handle: true or
    // false, or null when Windows won't say. When SteppedOut itself runs as administrator, Windows
    // gives a handle that could close an elevated app, so this is the only thing that tells.
    // A stand-in that does not say counts as not elevated.
    bool? Elevated => false;

    bool HasExited { get; }

    // Completes when the process exits.
    Task WhenExited { get; }

    // TerminateProcess on this one process, never its children. 0 when it worked, else Windows'
    // error code. The real one throws instead while SessionLock.Blocked is set, unless the process
    // runs a program a test built for itself.
    int Kill();
}

// The real one. It is only ever asked about processes CloseApps matched by name and guarded, and the
// close-apps test wraps it so that it can reach nothing but the stub the test started. Its handle is
// the one way the app ends a process, and PostClose the one way it asks a window to close; both are
// guarded as a lock request and a program start are.
sealed class Win32ProcessApi : IProcessApi
{
    const uint SnapProcess = 0x2;
    const uint Terminate = 0x0001;
    const uint QueryLimitedInformation = 0x1000;
    const uint Synchronize = 0x00100000;
    const uint WmClose = 0x0010;
    const uint TokenQuery = 0x0008;
    const int TokenElevationClass = 20;
    const int ErrorInsufficientBuffer = 122;

    public int OwnId { get; } = Environment.ProcessId;

    public string? OwnPath { get; } = Environment.ProcessPath;

    public IReadOnlyList<ProcessEntry> Snapshot()
    {
        var list = new List<ProcessEntry>();
        using var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot.IsInvalid) return list;
        var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
        if (!Process32First(snapshot, ref entry)) return list;
        do
        {
            list.Add(new ProcessEntry((int)entry.th32ProcessID, (int)entry.th32ParentProcessID, entry.szExeFile ?? ""));
            entry.dwSize = (uint)Marshal.SizeOf<ProcessEntry32>();
        }
        while (Process32Next(snapshot, ref entry));
        return list;
    }

    public int? SessionOf(int processId) => ProcessIdToSessionId((uint)processId, out uint session) ? (int)session : null;

    public long? StartedAt(int processId)
    {
        using var handle = OpenProcess(QueryLimitedInformation, false, processId);
        return handle.IsInvalid ? null : StartTime(handle);
    }

    public IProcessHandle? Open(int processId, out int error)
    {
        var handle = OpenProcess(Terminate | Synchronize | QueryLimitedInformation, false, processId);
        if (handle.IsInvalid)
        {
            error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            return null;
        }
        error = 0;
        return new Handle(processId, handle);
    }

    public IReadOnlyList<AppWindow> Windows()
    {
        var list = new List<AppWindow>();
        EnumWindows((window, _) =>
        {
            if (IsWindowVisible(window)) list.Add(Describe(window));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public IReadOnlyList<AppWindow> ChildrenOf(IntPtr window)
    {
        var list = new List<AppWindow>();
        EnumChildWindows(window, (child, _) =>
        {
            list.Add(Describe(child));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    // While SessionLock.Blocked is set (the tests and the screenshot tool, on a PC someone is using),
    // this throws instead, so no window of the person's programs is asked to close even if a test
    // reaches the real list of windows by mistake. The one window still asked then belongs to a
    // program a test built and copied for itself (ProcessStarter.MadeForATest), as read from the
    // process that owns it.
    public bool PostClose(IntPtr window)
    {
        if (SessionLock.Blocked && !OwnedByATestsProgram(window))
            throw new InvalidOperationException("Closing windows is blocked in this process.");
        return Native.PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero);
    }

    static bool OwnedByATestsProgram(IntPtr window)
    {
        Native.GetWindowThreadProcessId(window, out uint processId);
        if (processId == 0) return false;
        using var handle = OpenProcess(QueryLimitedInformation, false, (int)processId);
        return !handle.IsInvalid && ImagePathOf(handle) is { } path && ProcessStarter.MadeForATest(path);
    }

    static AppWindow Describe(IntPtr window)
    {
        Native.GetWindowThreadProcessId(window, out uint processId);
        return new AppWindow(window, (int)processId, Native.ClassName(window), Title(window));
    }

    // GetWindowText reads a title Windows keeps itself, so a window that is not answering cannot
    // hold this up.
    static string Title(IntPtr window)
    {
        var buffer = new char[512];
        int length = GetWindowText(window, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : "";
    }

    static long? StartTime(SafeProcessHandle handle) =>
        GetProcessTimes(handle, out long created, out _, out _, out _) ? created : null;

    static string? ImagePathOf(SafeProcessHandle handle)
    {
        int capacity = 1024;
        while (capacity <= 32768)
        {
            var buffer = new char[capacity];
            int size = buffer.Length;
            if (QueryFullProcessImageName(handle, 0, buffer, ref size)) return size > 0 ? new string(buffer, 0, size) : null;
            if (Marshal.GetLastPInvokeError() != ErrorInsufficientBuffer) return null;
            capacity *= 4;
        }
        return null;
    }

    // Opens the process's token only to read it, through a handle that has PROCESS_QUERY_LIMITED_INFORMATION.
    static bool? ElevatedOf(SafeProcessHandle handle)
    {
        if (!OpenProcessToken(handle, TokenQuery, out var token))
        {
            token.Dispose();
            return null;
        }
        using (token)
        {
            return GetTokenInformation(token, TokenElevationClass, out int elevated, sizeof(int), out int length) && length == sizeof(int)
                ? elevated != 0
                : null;
        }
    }

    sealed class Handle : IProcessHandle
    {
        readonly SafeProcessHandle handle;
        readonly object gate = new();
        readonly TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RegisteredWaitHandle? registration;
        bool referenced;
        bool disposed;

        public Handle(int id, SafeProcessHandle handle)
        {
            Id = id;
            this.handle = handle;
            ImagePath = ImagePathOf(handle);
            StartedAt = StartTime(handle);
            Elevated = ElevatedOf(handle);
        }

        public int Id { get; }

        public string? ImagePath { get; }

        public long? StartedAt { get; }

        public bool? Elevated { get; }

        public bool HasExited => WaitForSingleObject(handle, 0) == 0;

        // A thread-pool wait on the handle, set up the first time someone asks.
        public Task WhenExited
        {
            get
            {
                lock (gate)
                {
                    if (registration is null && !disposed)
                    {
                        handle.DangerousAddRef(ref referenced);
                        var wait = new ProcessWait(handle.DangerousGetHandle());
                        registration = ThreadPool.RegisterWaitForSingleObject(wait, (_, _) => exited.TrySetResult(), null, Timeout.Infinite, executeOnlyOnce: true);
                    }
                }
                return exited.Task;
            }
        }

        // While SessionLock.Blocked is set (the tests and the screenshot tool, on a PC someone is
        // using), this throws instead, so none of the person's programs is ended even if a test
        // reaches the real handle by mistake. The one process still ended then runs a program a test
        // built and copied for itself (ProcessStarter.MadeForATest), as read through this handle.
        public int Kill()
        {
            if (SessionLock.Blocked && !(ImagePath is { } path && ProcessStarter.MadeForATest(path)))
                throw new InvalidOperationException("Ending programs is blocked in this process.");
            return TerminateProcess(handle, 1) ? 0 : Marshal.GetLastPInvokeError();
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                registration?.Unregister(null);
                if (referenced) handle.DangerousRelease();
            }
            handle.Dispose();
        }
    }

    // Lets the thread pool wait on a process handle it does not own.
    sealed class ProcessWait : WaitHandle
    {
        public ProcessWait(IntPtr process) => SafeWaitHandle = new SafeWaitHandle(process, ownsHandle: false);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessEntry32
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

    delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, [Out] char[] name, ref int size);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, out int information, int length, out int returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    static extern int GetWindowText(IntPtr window, [Out] char[] text, int capacity);
}
