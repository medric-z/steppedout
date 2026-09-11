using Microsoft.Win32;

namespace SteppedOut;

// The one registry value this tool ever writes: HKCU\...\Run\SteppedOut, and only when asked to.
static class Autostart
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled(string keyPath = RunKey)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(AppInfo.Name) is string;
    }

    public static void Enable(string keyPath = RunKey)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue(AppInfo.Name, Command());
    }

    public static void Disable(string keyPath = RunKey)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(AppInfo.Name, throwOnMissingValue: false);
    }

    public static string Command() => $"\"{Environment.ProcessPath}\"";
}
