namespace SteppedOut;

// Which windows to ask to close for one process.
//
// A desktop app's windows belong to its own process. A Store app's do not: its content is a
// CoreWindow inside a frame window that ApplicationFrameHost owns, and when the app is minimized
// Windows may take the CoreWindow out of the frame. The frame is what the close button belongs to, so
// a frame is asked when the CoreWindow inside it belongs to the process, or, for a CoreWindow taken
// out, when a frame with no CoreWindow of its own carries the same title. ApplicationFrameHost itself
// is never closed (see CloseApps.Guarded); its frames are only reached this way.
//
// Closing looks at every visible window, minimized or on another virtual desktop included, so an app
// out of sight still shows its Save prompt instead of being forced 30 s later without one. That is
// why this is not the "on screen" helper app rules use, which leaves those windows out.
static class AppWindows
{
    public const string FrameClass = "ApplicationFrameWindow";
    public const string CoreClass = "Windows.UI.Core.CoreWindow";

    // In the order Windows stacks them, top first. A dialog is always above the window it belongs to,
    // so it is asked first: a Save As box is cancelled before its window is asked, which then shows its
    // own Save prompt.
    public static List<IntPtr> ToClose(int processId, IReadOnlyList<AppWindow> windows, Func<IntPtr, IReadOnlyList<AppWindow>> childrenOf)
    {
        var takenOut = new HashSet<string>(StringComparer.Ordinal);
        foreach (var window in windows)
        {
            if (window.ProcessId == processId && window.ClassName == CoreClass && window.Title.Length > 0) takenOut.Add(window.Title);
        }
        var result = new List<IntPtr>();
        foreach (var window in windows)
        {
            // A CoreWindow is not a window of its own to close; its frame is.
            if (window.ClassName == CoreClass) continue;
            if (window.ProcessId == processId || (window.ClassName == FrameClass && FrameBelongsTo(processId, window, childrenOf, takenOut)))
                result.Add(window.Handle);
        }
        return result;
    }

    static bool FrameBelongsTo(int processId, AppWindow frame, Func<IntPtr, IReadOnlyList<AppWindow>> childrenOf, HashSet<string> takenOut)
    {
        bool anyCore = false;
        foreach (var child in childrenOf(frame.Handle))
        {
            if (child.ClassName != CoreClass) continue;
            if (child.ProcessId == processId) return true;
            anyCore = true;
        }
        return !anyCore && frame.Title.Length > 0 && takenOut.Contains(frame.Title);
    }
}
