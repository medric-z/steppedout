using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace SteppedOut;

// Whether this copy runs from the Microsoft Store package (MSIX) or is the normal exe, and the two
// things a package does differently: where its files really are, and how it starts with Windows.
// For the normal exe every member gives what the app used before there was a package.
static unsafe class Packaged
{
    const int ErrorSuccess = 0;
    const int ErrorInsufficientBuffer = 122;
    internal const int AppModelErrorNoPackage = 15700;

    // A process starts in its package or without one and never changes, so this is asked once.
    public static bool IsPackaged { get; } = FromResult(CurrentPackageResult());

    // Such as 12345Publisher.SteppedOut_abcd1234efgh5; null for the normal exe.
    public static string? FamilyName { get; } = IsPackaged ? ReadFamilyName() : null;

    // The package's start-with-Windows switch; null for the normal exe, which uses its Run entry.
    public static IStartupTask? StartupTask { get; } = IsPackaged ? new StartupTaskApi() : null;

    // GetCurrentPackageFullName with no room for the name: a packaged process is told the name does
    // not fit, the normal exe that it has no package. Anything else, an error included, counts as
    // the normal exe, so an odd answer never moves the app off the paths it has always used.
    internal static bool FromResult(int result) => result is ErrorSuccess or ErrorInsufficientBuffer;

    // The folder that holds config.json, the log and today.json, as Explorer sees it, for "Open
    // folder". The normal exe uses %APPDATA%\SteppedOut, as it always has.
    //
    // A package (Windows 10 1903 and later) writes each file it creates under %APPDATA% to its own
    // folder, %LOCALAPPDATA%\Packages\<family>\LocalCache\Roaming, while the app still sees it at
    // %APPDATA%; Explorer, which runs outside the package, does not. To open a file Windows looks
    // in the package's folder first, and when the file is only in the real %APPDATA%, it changes
    // that file in place. So a config.json the normal exe had already saved stays where it was and
    // is the package's settings too; then that folder is the one to open.
    public static string SettingsFolder() =>
        SettingsFolder(IsPackaged, FamilyName, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppInfo.ConfigDir, path => File.Exists(path) || Directory.Exists(path));

    internal static string SettingsFolder(bool packaged, string? family, string localAppData, string configDir, Func<string, bool> exists)
    {
        if (!packaged || string.IsNullOrEmpty(family) || string.IsNullOrEmpty(localAppData)) return configDir;
        string own = Path.Combine(localAppData, "Packages", family, "LocalCache", "Roaming", AppInfo.Name);
        string config = Path.GetFileName(AppInfo.ConfigPath);
        if (exists(Path.Combine(own, config))) return own;
        if (exists(Path.Combine(configDir, config))) return configDir;
        // No settings saved yet: the package's folder once it has written anything (the log comes
        // first), and until then the plain folder, so Explorer never gets a folder that isn't there.
        return exists(own) ? own : configDir;
    }

    // Whether a save has to write the file at path where it is. In a package, a file that is only in
    // the real %APPDATA% is changed in place there, but the new file a save writes first goes to the
    // package's own folder, so moving it over the file would take the settings out of the folder the
    // normal exe and Explorer use, or fail. Such a file is written in place, as before saves went
    // through a file of their own. False for the normal exe and for a file the package made itself.
    public static bool KeepsInPlace(string path) =>
        KeepsInPlace(IsPackaged, FamilyName, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), path, File.Exists);

    internal static bool KeepsInPlace(bool packaged, string? family, string localAppData, string roamingAppData, string path, Func<string, bool> exists)
    {
        if (!packaged || string.IsNullOrEmpty(family) || string.IsNullOrEmpty(localAppData) || string.IsNullOrEmpty(roamingAppData)) return false;
        string full = Path.GetFullPath(path);
        string roaming = Path.TrimEndingDirectorySeparator(Path.GetFullPath(roamingAppData)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(roaming, StringComparison.OrdinalIgnoreCase)) return false;
        string own = Path.Combine(localAppData, "Packages", family, "LocalCache", "Roaming", full[roaming.Length..]);
        return exists(full) && !exists(own);
    }

    // The normal exe never fails here: anything unexpected counts as no package.
    static int CurrentPackageResult()
    {
        try
        {
            uint length = 0;
            return GetCurrentPackageFullName(&length, null);
        }
        catch (Exception)
        {
            return AppModelErrorNoPackage;
        }
    }

    static string? ReadFamilyName()
    {
        try
        {
            uint length = 0;
            if (GetCurrentPackageFamilyName(&length, null) != ErrorInsufficientBuffer || length == 0) return null;
            var buffer = new char[length];
            fixed (char* name = buffer)
            {
                return GetCurrentPackageFamilyName(&length, name) == ErrorSuccess ? new string(name) : null;
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll")]
    static extern int GetCurrentPackageFullName(uint* length, char* name);

    [DllImport("kernel32.dll")]
    static extern int GetCurrentPackageFamilyName(uint* length, char* name);
}

// Windows.ApplicationModel.StartupTaskState, with the same numbers.
enum StartupTaskState
{
    Disabled = 0,
    // Turned off in Settings, Apps, Startup or in Task Manager. Only the person can turn it back on.
    DisabledByUser = 1,
    Enabled = 2,
    // Turned off by the PC's administrator, or the PC does not run startup apps.
    DisabledByPolicy = 3,
    // Turned on by the PC's administrator, who keeps it on.
    EnabledByPolicy = 4,
}

// The package's startup task: the windows.startupTask extension in tools/store/Package.appxmanifest.template,
// which Windows lists under Settings, Apps, Startup. A seam, so the tests can give its states.
interface IStartupTask
{
    StartupTaskState State();

    // For a packaged desktop app this never asks the person anything. A task turned off by the
    // person or by policy stays off, and the state it returns says so.
    StartupTaskState RequestEnable();

    void Disable();
}

// Windows.ApplicationModel.StartupTask called through its raw vtables, the way Interop/MediaSessions.cs
// calls the media controls, so the exe carries no Windows SDK projection and its size stays as it
// was. Slot numbers come from Windows.ApplicationModel.winmd and Windows.Foundation.winmd; slots 0 to
// 5 are IUnknown's and IInspectable's. Each call gets the task afresh on its own thread in the
// multithreaded apartment, because the settings window's thread is single-threaded.
sealed unsafe class StartupTaskApi : IStartupTask
{
    // The manifest's TaskId; a test checks that the two match.
    public const string TaskId = "SteppedOutStartup";

    // A test reads these back from the winmd files in Windows' own WinMetadata folder.
    internal const string ClassName = "Windows.ApplicationModel.StartupTask";
    internal static readonly Guid StaticsIid = new("EE5B60BD-A148-41A7-B26E-E8B88A1E62F8");   // IStartupTaskStatics
    internal static readonly Guid AsyncInfoIid = new("00000036-0000-0000-C000-000000000046"); // IAsyncInfo

    const int QueryInterfaceSlot = 0;
    const int ReleaseSlot = 2;
    internal const int GetAsyncSlot = 7;              // IStartupTaskStatics
    internal const int RequestEnableAsyncSlot = 6;    // IStartupTask
    internal const int DisableSlot = 7;
    internal const int StateSlot = 8;
    internal const int StatusSlot = 7;                // IAsyncInfo
    internal const int ErrorCodeSlot = 8;
    internal const int CancelSlot = 9;
    internal const int GetResultsSlot = 8;            // IAsyncOperation<T>

    const int AsyncStarted = 0;
    const int AsyncCompleted = 1;
    const int EAbort = unchecked((int)0x80004004);
    const int RoInitMultithreaded = 1;

    // Windows answers within milliseconds; one that hangs costs the switch at most this, twice.
    static readonly TimeSpan AnswerWithin = TimeSpan.FromSeconds(3);

    public StartupTaskState State() => OnTask(task => (StartupTaskState)GetInt32(task, StateSlot));

    public StartupTaskState RequestEnable() => OnTask(task => (StartupTaskState)AwaitInt32(GetPointer(task, RequestEnableAsyncSlot)));

    public void Disable() => OnTask(task =>
    {
        Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)Slots(task)[DisableSlot])(task));
        return 0;
    });

    static T OnTask<T>(Func<IntPtr, T> call)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            // The thread is already in the multithreaded apartment; this makes it explicit for
            // WinRT, and each successful call is matched by RoUninitialize.
            bool initialized = RoInitialize(RoInitMultithreaded) >= 0;
            try
            {
                IntPtr task = GetTask();
                try
                {
                    result = call(task);
                }
                finally
                {
                    Release(task);
                }
            }
            catch (Exception e)
            {
                failure = e;
            }
            finally
            {
                if (initialized) RoUninitialize();
            }
        })
        { IsBackground = true, Name = "startup task" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        if (!thread.Join(AnswerWithin * 2)) throw new TimeoutException($"Windows did not answer about the startup task within {(AnswerWithin * 2).TotalSeconds:0} s");
        if (failure is not null) ExceptionDispatchInfo.Throw(failure);
        return result;
    }

    static IntPtr GetTask()
    {
        Check(WindowsCreateString(ClassName, (uint)ClassName.Length, out IntPtr className));
        try
        {
            Check(WindowsCreateString(TaskId, (uint)TaskId.Length, out IntPtr taskId));
            try
            {
                Guid iid = StaticsIid;
                IntPtr statics;
                Check(RoGetActivationFactory(className, &iid, &statics));
                try
                {
                    IntPtr operation;
                    Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slots(statics)[GetAsyncSlot])(statics, taskId, &operation));
                    return AwaitPointer(operation);
                }
                finally
                {
                    Release(statics);
                }
            }
            finally
            {
                WindowsDeleteString(taskId);
            }
        }
        finally
        {
            WindowsDeleteString(className);
        }
    }

    static void Wait(IntPtr operation)
    {
        IntPtr info = QueryInterface(operation, AsyncInfoIid);
        try
        {
            long deadline = Environment.TickCount64 + (long)AnswerWithin.TotalMilliseconds;
            while (true)
            {
                int status = GetInt32(info, StatusSlot);
                if (status == AsyncCompleted) return;
                if (status != AsyncStarted)
                {
                    int error = GetInt32(info, ErrorCodeSlot);
                    Marshal.ThrowExceptionForHR(error < 0 ? error : EAbort);
                }
                if (Environment.TickCount64 > deadline)
                {
                    ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slots(info)[CancelSlot])(info);
                    throw new TimeoutException($"Windows did not answer about the startup task within {AnswerWithin.TotalSeconds:0} s");
                }
                Thread.Sleep(5);
            }
        }
        finally
        {
            Release(info);
        }
    }

    static IntPtr AwaitPointer(IntPtr operation)
    {
        try
        {
            Wait(operation);
            return GetPointer(operation, GetResultsSlot);
        }
        finally
        {
            Release(operation);
        }
    }

    static int AwaitInt32(IntPtr operation)
    {
        try
        {
            Wait(operation);
            return GetInt32(operation, GetResultsSlot);
        }
        finally
        {
            Release(operation);
        }
    }

    static IntPtr* Slots(IntPtr instance) => *(IntPtr**)instance;

    static IntPtr GetPointer(IntPtr instance, int slot)
    {
        IntPtr result;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slots(instance)[slot])(instance, &result));
        return result;
    }

    static int GetInt32(IntPtr instance, int slot)
    {
        int result;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slots(instance)[slot])(instance, &result));
        return result;
    }

    static IntPtr QueryInterface(IntPtr instance, Guid iid)
    {
        IntPtr result;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Slots(instance)[QueryInterfaceSlot])(instance, &iid, &result));
        return result;
    }

    static void Release(IntPtr instance)
    {
        if (instance != IntPtr.Zero) ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slots(instance)[ReleaseSlot])(instance);
    }

    static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    static extern int WindowsCreateString(string source, uint length, out IntPtr text);

    [DllImport("combase.dll")]
    static extern int WindowsDeleteString(IntPtr text);

    [DllImport("combase.dll")]
    static extern int RoGetActivationFactory(IntPtr activatableClassId, Guid* iid, IntPtr* factory);

    [DllImport("combase.dll")]
    static extern int RoInitialize(int initType);

    [DllImport("combase.dll")]
    static extern void RoUninitialize();
}
