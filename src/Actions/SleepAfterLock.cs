using System.Runtime.InteropServices;

namespace SteppedOut;

// What "Lock and sleep" needs from Windows, so the tests can count instead.
interface ISleeper
{
    // Whether this PC has a sleep an app can ask for (S1 to S3). A PC with Modern Standby has none:
    // it goes to standby when its screens go off.
    bool CanSleep { get; }

    // Asks Windows to sleep now. False when Windows turned it down.
    bool Sleep();

    // Whether the session is locked now.
    bool SessionLocked();
}

// The real sleep. Sleep, never hibernate, and the wake events are left as Windows has them: no wake
// timer is set and none is turned off.
sealed class PowerSleep : ISleeper
{
    public bool CanSleep
    {
        get
        {
            // SYSTEM_POWER_CAPABILITIES is 76 bytes; SystemS1, S2 and S3 are its 4th to 6th.
            var capabilities = new byte[128];
            if (!GetPwrCapabilities(capabilities)) return false;
            return capabilities[3] != 0 || capabilities[4] != 0 || capabilities[5] != 0;
        }
    }

    public bool Sleep()
    {
        // The tests and the screenshot tool run on a PC someone is using.
        if (SessionLock.Blocked) throw new InvalidOperationException("Putting the PC to sleep is blocked in this process.");
        if (SetSuspendState(hibernate: false, force: false, wakeEventsDisabled: false)) return true;
        Log.Write("SetSuspendState failed, error " + Marshal.GetLastWin32Error());
        return false;
    }

    public bool SessionLocked() => SessionLock.IsLocked();

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate, [MarshalAs(UnmanagedType.U1)] bool force,
        [MarshalAs(UnmanagedType.U1)] bool wakeEventsDisabled);

    [DllImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    static extern bool GetPwrCapabilities([Out] byte[] capabilities);
}
