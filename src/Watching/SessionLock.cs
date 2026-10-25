using System.Runtime.InteropServices;

namespace SteppedOut;

static class SessionLock
{
    static volatile bool blocked;

    // Set by the tests and the render tool, which run on a PC someone is using. While it is set, a
    // lock request throws instead of locking the session.
    internal static bool Blocked
    {
        get => blocked;
        set => blocked = value;
    }

    // LockWorkStation returns as soon as the request is queued, not when the lock is in place.
    // A true return only means "asked". The session-lock notification is the real confirmation,
    // and the Watcher listens for that instead of trusting the return value. False means Windows
    // turned the request down, so nothing will lock; the Watcher asks once more.
    public static bool TryRequest()
    {
        if (blocked) throw new InvalidOperationException("Locking the session is blocked in this process.");
        if (Native.LockWorkStation()) return true;
        Log.Write("LockWorkStation failed, error " + Marshal.GetLastWin32Error());
        return false;
    }

    // Only needed at startup; after that the session-switch notifications keep the state current.
    public static bool IsLocked()
    {
        if (!Native.WTSQuerySessionInformationW(IntPtr.Zero, Native.WtsCurrentSession, Native.WtsSessionInfoEx, out IntPtr buffer, out uint bytes))
        {
            return false;
        }
        try
        {
            // WTSINFOEXW is a DWORD Level followed by an 8-byte-aligned WTSINFOEX_LEVEL1, whose third
            // field, SessionFlags, lands at offset 16. 0 means locked and 1 unlocked on Windows 8 and
            // later; Windows 7 had the two swapped, which this tool does not support.
            return bytes >= 20 && Marshal.ReadInt32(buffer, 16) == 0;
        }
        finally
        {
            Native.WTSFreeMemory(buffer);
        }
    }
}
