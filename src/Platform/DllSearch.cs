using System.Runtime.InteropServices;

// Every DLL this app calls into is part of Windows. Without this, a DLL of the same name dropped
// next to the exe (wtsapi32.dll, xinput1_4.dll, dwmapi.dll and so on) would be loaded instead of
// Windows' own and run inside SteppedOut at every sign-in.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace SteppedOut;

static class DllSearch
{
    const uint LoadLibrarySearchSystem32 = 0x00000800;

    // The same for every DLL loaded by name after this, by Windows or by .NET on the app's behalf:
    // only System32 is searched, never the exe's folder or the current one. Called first thing in
    // Main. False when Windows refused, which leaves the attribute above as the guard.
    public static bool OnlySystem32() => SetDefaultDllDirectories(LoadLibrarySearchSystem32);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetDefaultDllDirectories(uint directoryFlags);
}
