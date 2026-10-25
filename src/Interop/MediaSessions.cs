using System.Runtime.InteropServices;

namespace SteppedOut;

// Pauses what is playing and later plays exactly that again, through the media controls Windows
// shows in the volume flyout (GlobalSystemMediaTransportControlsSessionManager). It is a WinRT API.
// It is called here through its raw vtables so the exe does not have to carry the Windows SDK
// projection, which is bigger than the rest of the tool. Slot numbers come from Windows.Media.winmd
// and Windows.Foundation.winmd; slots 0 to 5 are IUnknown's and IInspectable's.
//
// Everything here must run on a thread in the multithreaded apartment.
static unsafe class MediaSessions
{
    const string ManagerClass = "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager";
    static readonly Guid ManagerStaticsIid = new("2050C4EE-11A0-57DE-AED7-C97C70338245");
    static readonly Guid AsyncInfoIid = new("00000036-0000-0000-C000-000000000046");

    const int QueryInterfaceSlot = 0;
    const int ReleaseSlot = 2;
    const int RequestAsyncSlot = 6;          // IGlobalSystemMediaTransportControlsSessionManagerStatics
    const int GetSessionsSlot = 7;           // IGlobalSystemMediaTransportControlsSessionManager
    const int GetAtSlot = 6;                 // IVectorView<T>
    const int SizeSlot = 7;
    const int SourceAppSlot = 6;             // IGlobalSystemMediaTransportControlsSession
    const int PlaybackInfoSlot = 9;
    const int TryPlaySlot = 10;
    const int TryPauseSlot = 11;
    const int PlaybackStatusSlot = 7;        // IGlobalSystemMediaTransportControlsSessionPlaybackInfo
    const int StatusSlot = 7;                // IAsyncInfo
    const int ErrorCodeSlot = 8;
    const int CancelSlot = 9;
    const int GetResultsSlot = 8;            // IAsyncOperation<T>

    const int Playing = 4;
    const int AsyncStarted = 0;
    const int AsyncCompleted = 1;
    const int EAbort = unchecked((int)0x80004004);
    public const int RoInitMultithreaded = 1;

    static readonly TimeSpan AnswerWithin = TimeSpan.FromSeconds(3);

    // The sessions this tool paused, kept until they are played again.
    public sealed class Paused
    {
        internal readonly List<(IntPtr Session, string App)> Sessions = new();

        public IEnumerable<string> Apps => Sessions.Select(s => s.App);
    }

    public static Paused PausePlaying()
    {
        var paused = new Paused();
        IntPtr manager = RequestManager();
        try
        {
            IntPtr sessions = GetPointer(manager, GetSessionsSlot);
            try
            {
                uint count = GetUInt32(sessions, SizeSlot);
                for (uint i = 0; i < count; i++) PauseOne(sessions, i, paused);
            }
            finally
            {
                Release(sessions);
            }
        }
        finally
        {
            Release(manager);
        }
        return paused;
    }

    // One app that fails or hangs is logged and skipped. It never costs the others their pause, or
    // the ones already paused their play-again.
    static void PauseOne(IntPtr sessions, uint index, Paused paused)
    {
        IntPtr session = IntPtr.Zero;
        string app = "a media app";
        bool kept = false;
        try
        {
            session = GetAt(sessions, index);
            if (StatusOf(session) != Playing) return;
            // Read before pausing, so a pause that worked is never lost to a failure after it.
            app = SourceApp(session);
            if (AwaitBool(GetPointer(session, TryPauseSlot)))
            {
                paused.Sessions.Add((session, app));
                kept = true;
            }
        }
        catch (TimeoutException)
        {
            // It may still pause a moment later, so it is played again with the rest. Playing
            // something that is already playing does no harm.
            paused.Sessions.Add((session, app));
            kept = true;
            Log.Write($"{app} did not answer within {AnswerWithin.TotalSeconds:0} s; it will be played again in case it paused late");
        }
        catch (Exception e)
        {
            Log.Write($"could not pause {app}: {e.Message}");
        }
        finally
        {
            if (!kept) Release(session);
        }
    }

    // Returns the apps that started playing again. One that was closed in the meantime is skipped.
    public static List<string> PlayAgain(Paused paused)
    {
        var played = new List<string>();
        foreach (var (session, app) in paused.Sessions)
        {
            try
            {
                if (AwaitBool(GetPointer(session, TryPlaySlot))) played.Add(app);
            }
            catch (Exception e)
            {
                Log.Write($"could not play {app} again: {e.Message}");
            }
            finally
            {
                Release(session);
            }
        }
        paused.Sessions.Clear();
        return played;
    }

    // Lets go of the paused sessions without playing them, for "Keep it paused" and for signing
    // out while away. Each one holds a reference that has to be given back, or it leaks.
    public static void Release(Paused paused)
    {
        foreach (var (session, _) in paused.Sessions) Release(session);
        paused.Sessions.Clear();
    }

    static IntPtr RequestManager()
    {
        Check(WindowsCreateString(ManagerClass, (uint)ManagerClass.Length, out IntPtr name));
        try
        {
            Guid iid = ManagerStaticsIid;
            IntPtr statics;
            Check(RoGetActivationFactory(name, &iid, &statics));
            try
            {
                return AwaitPointer(GetPointer(statics, RequestAsyncSlot));
            }
            finally
            {
                Release(statics);
            }
        }
        finally
        {
            WindowsDeleteString(name);
        }
    }

    static int StatusOf(IntPtr session)
    {
        IntPtr info = GetPointer(session, PlaybackInfoSlot);
        try
        {
            return GetInt32(info, PlaybackStatusSlot);
        }
        finally
        {
            Release(info);
        }
    }

    static string SourceApp(IntPtr session)
    {
        IntPtr text = GetPointer(session, SourceAppSlot);
        try
        {
            uint length;
            char* chars = WindowsGetStringRawBuffer(text, &length);
            return chars == null ? "" : new string(chars, 0, (int)length);
        }
        finally
        {
            WindowsDeleteString(text);
        }
    }

    // Media apps answer within milliseconds. One that hangs is given up on after AnswerWithin
    // rather than holding up everything after it.
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
                    throw new TimeoutException($"a media app did not answer within {AnswerWithin.TotalSeconds:0} s");
                }
                Thread.Sleep(10);
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

    static bool AwaitBool(IntPtr operation)
    {
        try
        {
            Wait(operation);
            byte result;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, byte*, int>)Slots(operation)[GetResultsSlot])(operation, &result));
            return result != 0;
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

    static uint GetUInt32(IntPtr instance, int slot)
    {
        uint result;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Slots(instance)[slot])(instance, &result));
        return result;
    }

    static IntPtr GetAt(IntPtr vector, uint index)
    {
        IntPtr result;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slots(vector)[GetAtSlot])(vector, index, &result));
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
    static extern char* WindowsGetStringRawBuffer(IntPtr text, uint* length);

    [DllImport("combase.dll")]
    static extern int RoGetActivationFactory(IntPtr activatableClassId, Guid* iid, IntPtr* factory);

    [DllImport("combase.dll")]
    public static extern int RoInitialize(int initType);
}
