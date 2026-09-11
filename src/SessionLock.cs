using System.Runtime.InteropServices;

namespace SteppedOut;

static class SessionLock
{
    // LockWorkStation returns as soon as the request is queued, not when the lock is in place.
    // A true return only means "asked". The session-lock notification is the real confirmation,
    // and the Watcher listens for that instead of trusting the return value.
    public static void Request()
    {
        if (!Native.LockWorkStation()) Log.Write("LockWorkStation failed, error " + Marshal.GetLastWin32Error());
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
