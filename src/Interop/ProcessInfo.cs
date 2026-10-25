using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SteppedOut;

// What Windows says about a running process and its window, asked with the least access that works,
// so it also answers for apps that run as administrator. Every call here may meet a process that has
// just exited, a handle Windows refuses, or a file with no icon; each one answers null or false
// instead of throwing.
static class ProcessInfo
{
    const uint QueryLimitedInformation = 0x1000;
    const int ErrorSuccess = 0;
    const int ErrorInsufficientBuffer = 122;
    const uint DwmwaCloaked = 14;
    const int GwlExStyle = -20;
    const int WsExToolWindow = 0x80;
    const int MaxIcons = 200;

    static readonly object iconsLock = new();
    static readonly Dictionary<string, Bitmap?> icons = new(StringComparer.OrdinalIgnoreCase);

    // The full path of the program the process runs, or null when Windows won't say.
    public static string? ImagePath(int processId) =>
        WithProcess(processId, handle =>
        {
            int capacity = 1024;
            while (capacity <= 32768)
            {
                var buffer = new char[capacity];
                int size = buffer.Length;
                if (QueryFullProcessImageName(handle, 0, buffer, ref size)) return size > 0 ? new string(buffer, 0, size) : null;
                if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer) return null;
                capacity *= 4;
            }
            return null;
        });

    // The app's id in the Start menu (its AUMID) for a Store or other packaged app, else null.
    public static string? Aumid(int processId) =>
        WithProcess(processId, handle => AskForString((ref int length, char[]? buffer) => GetApplicationUserModelId(handle, ref length, buffer)));

    // The package family a packaged app belongs to, else null. Calls and music match a packaged app's
    // sound to the app by this, never by its name.
    public static string? PackageFamilyName(int processId) =>
        WithProcess(processId, handle => AskForString((ref int length, char[]? buffer) => GetPackageFamilyName(handle, ref length, buffer)));

    // Windows the shell keeps out of sight, such as the one behind the emoji panel or an app on another
    // virtual desktop, and tool windows, such as an overlay, which have no taskbar button.
    public static bool IsHiddenOrTool(IntPtr window)
    {
        if (window == IntPtr.Zero) return true;
        try
        {
            if (DwmGetWindowAttribute(window, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            return (GetWindowLong(window, GwlExStyle) & WsExToolWindow) != 0;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    // The program's own icon at size × size pixels, or null when the file has none or can't be read.
    // The image is the caller's to dispose; the cache keeps its own copy, so reading the same file
    // again for every menu or row costs nothing.
    public static Bitmap? Icon(string? path, int size)
    {
        if (string.IsNullOrWhiteSpace(path) || size <= 0 || size > 256) return null;
        string key = size + "|" + path;
        lock (iconsLock)
        {
            if (icons.TryGetValue(key, out var kept)) return CopyLocked(kept);
        }
        // Read without the lock, so a file slow to read holds up only the one who asked for it, not
        // every icon read meanwhile.
        var read = Extract(path, size);
        lock (iconsLock)
        {
            if (icons.TryGetValue(key, out var kept))
            {
                // Read meanwhile by another.
                read?.Dispose();
                return CopyLocked(kept);
            }
            if (icons.Count >= MaxIcons) ClearIconsLocked();
            icons[key] = read;
            return CopyLocked(read);
        }
    }

    static Bitmap? CopyLocked(Bitmap? kept)
    {
        if (kept is null) return null;
        try
        {
            return new Bitmap(kept);
        }
        catch (Exception e) when (e is ArgumentException or ExternalException or OutOfMemoryException)
        {
            return null;
        }
    }

    // Forgets every icon read so far and frees their memory.
    public static void ClearIcons()
    {
        lock (iconsLock) ClearIconsLocked();
    }

    internal static int CachedIcons
    {
        get { lock (iconsLock) return icons.Count; }
    }

    static void ClearIconsLocked()
    {
        foreach (var image in icons.Values) image?.Dispose();
        icons.Clear();
    }

    static Bitmap? Extract(string path, int size)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var icon = System.Drawing.Icon.ExtractIcon(path, 0, size) ?? System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            using var full = icon.ToBitmap();
            if (full.Width == size && full.Height == size) return new Bitmap(full);
            var scaled = new Bitmap(size, size);
            using var g = Graphics.FromImage(scaled);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(full, new Rectangle(0, 0, size, size));
            return scaled;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or ExternalException or OutOfMemoryException or NotSupportedException)
        {
            return null;
        }
    }

    delegate int StringCall(ref int length, char[]? buffer);

    // The appmodel calls first say how long the answer is, then fill a buffer that long.
    static string? AskForString(StringCall call)
    {
        int length = 0;
        int result = call(ref length, null);
        if (result != ErrorInsufficientBuffer || length <= 0 || length > 4096) return null;
        var buffer = new char[length];
        if (call(ref length, buffer) != ErrorSuccess) return null;
        int end = Array.IndexOf(buffer, '\0');
        string text = new(buffer, 0, end >= 0 ? end : buffer.Length);
        return text.Length > 0 ? text : null;
    }

    static string? WithProcess(int processId, Func<SafeProcessHandle, string?> ask)
    {
        if (processId <= 0) return null;
        try
        {
            using var handle = OpenProcess(QueryLimitedInformation, false, processId);
            return handle.IsInvalid ? null : ask(handle);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // GetPackageFamilyName and GetApplicationUserModelId came with Windows 8.
            return null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, [Out] char[] name, ref int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetApplicationUserModelId(SafeProcessHandle process, ref int length, [Out] char[]? id);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetPackageFamilyName(SafeProcessHandle process, ref int length, [Out] char[]? name);

    [DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out int value, uint size);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    static extern int GetWindowLong(IntPtr window, int index);
}
