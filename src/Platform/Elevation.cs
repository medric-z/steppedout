using System.Runtime.InteropServices;
using System.Security.Principal;

namespace SteppedOut;

// Whether this copy runs with an administrator's full rights, as after "Run as administrator" or a
// start from an admin terminal. The Run entry and Explorer start it with the normal rights of the
// sign-in.
static class Elevation
{
    const int TokenElevationTypeClass = 18;

    // Read once: a process keeps the rights it started with.
    public static bool Full { get; } = Read() == TokenElevation.Full;

    // The log line at start for a copy with full rights.
    public const string StartLine = "running as administrator: the apps to open and the scripts to run are not started while it does; start SteppedOut without admin rights";

    static TokenElevation Read()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return GetTokenInformation(identity.Token, TokenElevationTypeClass, out int type, sizeof(int), out _)
                ? (TokenElevation)type
                : TokenElevation.Default;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return TokenElevation.Default;
        }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetTokenInformation(IntPtr token, int informationClass, out int information, int length, out int returned);
}

// TOKEN_ELEVATION_TYPE. Default is a sign-in without UAC's split rights (UAC off, or a standard
// user): every program of the sign-in has the same rights, so nothing is gained by refusing.
enum TokenElevation
{
    Default = 1,
    Full = 2,
    Limited = 3,
}

// Stands in for the starter in a copy with full rights. The apps to open and the scripts to run
// come from config.json, which any program of the sign-in can write without admin rights, so
// starting them from here would hand those rights on. Each is refused and logged instead.
sealed class ElevatedStarter : IStarter
{
    public const string Reason = "SteppedOut runs as administrator, so it opens no app and runs no script; start it without admin rights";

    public void Start(System.Diagnostics.ProcessStartInfo start) => throw new InvalidOperationException(Reason);

    public string? Refuses => Reason;
}
