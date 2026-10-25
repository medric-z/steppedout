using System.Diagnostics;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace SteppedOut;

// An app someone picked: the name the app lists match on, and where it came from when that is known.
// Path is the program file for an app that was running or chosen from the files; Aumid is the Start
// menu id of a Store or other packaged app.
sealed record PickedApp(string Name, string? Path, string? Aumid);

// The words the picker's menu shows. The header is optional: the settings pages show none.
sealed record PickerText(string ChooseAnother, string FileFilter, string? Header = null);

// One running process as the picker sees it. Any member may throw: the process can exit, or refuse to
// be looked at, at any moment.
interface IRunningProcess : IDisposable
{
    int Id { get; }
    string Name { get; }
    IntPtr MainWindow { get; }
    string MainWindowTitle { get; }
}

// Where the running apps come from: Windows for real, a stand-in in tests. Any member may throw.
interface IProcessSource
{
    IEnumerable<IRunningProcess> Running();
    bool IsHiddenOrTool(IntPtr window);
    string? ImagePath(int processId);
    string? Aumid(int processId);

    // The window that shows a process with no main window of its own, and its title: the frame
    // ApplicationFrameHost shows a Store app in, such as Calculator. None for any other process.
    (IntPtr Window, string Title) FrameFor(int processId) => (IntPtr.Zero, "");
}

// "Add app": a menu of the apps open right now that have a window of their own, and "Choose another
// app…" for any program file. Nobody has to know an exe name, and nothing in here ever throws to the
// window that opened it: an app that exits while it is being listed, or a call Windows refuses, just
// leaves that app out.
static class AppPicker
{
    // As many running apps as fit in a menu without scrolling.
    public const int MaxListed = 15;

    // Never listed: SteppedOut itself, and ApplicationFrameHost, which shows Settings, Calculator and
    // the other built-in apps, so its name never means one of them.
    static readonly string[] NeverListed = [AppInfo.Name, "ApplicationFrameHost"];

    // Opens the menu under the button. Picking an app calls picked; the apps already in the list are
    // left out of the menu, and picking one of them from the files does nothing.
    public static void Show(Control button, IEnumerable<string> already, Action<PickedApp> picked, PickerText text,
        IProcessSource? source = null, Func<string?>? chooseFile = null)
    {
        ContextMenuStrip? menu = null;
        try
        {
            menu = Menu(button, already, picked, text, source, chooseFile);
            MenuLook.ShowBelow(menu, button);
        }
        catch (Exception e) when (Guarded(e))
        {
            Log.Write("could not open the app list: " + e.Message);
            menu?.Dispose();
        }
    }

    // The menu Show opens, built but not shown, so tests can look at it and press its items.
    internal static ContextMenuStrip Menu(Control button, IEnumerable<string> already, Action<PickedApp> picked, PickerText text,
        IProcessSource? source = null, Func<string?>? chooseFile = null)
    {
        var listedAlready = Safe(() => already.ToList(), []);
        var menu = MenuLook.NewMenu(button.RightToLeft);
        try
        {
            Fill(menu, button, listedAlready, picked, text, source, chooseFile);
            return menu;
        }
        catch
        {
            menu.Dispose();
            throw;
        }
    }

    static void Fill(ContextMenuStrip menu, Control button, List<string> listedAlready, Action<PickedApp> picked, PickerText text,
        IProcessSource? source, Func<string?>? chooseFile)
    {
        var images = new List<Image>();
        menu.Disposed += (_, _) =>
        {
            foreach (var image in images) image.Dispose();
        };

        if (text.Header is { } header)
        {
            var item = MenuLook.Item(header);
            item.Enabled = false;
            menu.Items.Add(item);
        }

        // Each app with its icon in the margin where other menus show a check mark; nothing here is
        // ever checked.
        int size = Safe(() => button.LogicalToDeviceUnits(16), 16);
        menu.ImageScalingSize = new Size(size, size);
        menu.ShowImageMargin = true;
        menu.ShowCheckMargin = false;
        foreach (var app in Running(source ?? WindowsProcesses.Instance, listedAlready))
        {
            var item = MenuLook.Item(app.Name, () => Hand(app, listedAlready, picked));
            var image = IconFor(app, size, Theme.Current.Subtle);
            images.Add(image);
            item.Image = image;
            menu.Items.Add(item);
        }

        if (menu.Items.Count > 0) menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(MenuLook.Item(text.ChooseAnother, () =>
        {
            string? file = Safe(chooseFile ?? (() => ChooseFile(button, text.FileFilter)), null);
            if (FromFile(file) is { } app) Hand(app, listedAlready, picked);
        }));
    }

    // The apps open right now with a titled window of their own, by name, each name once, in order,
    // without the ones already in the list.
    public static IReadOnlyList<PickedApp> Running(IProcessSource source, IEnumerable<string> already, int max = MaxListed)
    {
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in Safe(() => already.ToList(), []))
            if (Key(name) is { Length: > 0 } key) skip.Add(key);

        int self = Environment.ProcessId;
        var found = new SortedDictionary<string, PickedApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Each(source))
        {
            try
            {
                try
                {
                    string name = Key(process.Name);
                    if (name.Length == 0 || skip.Contains(name) || found.ContainsKey(name) || Excluded(name) || process.Id == self) continue;
                    int id = process.Id;
                    IntPtr window = process.MainWindow;
                    string title = window == IntPtr.Zero ? "" : process.MainWindowTitle;
                    if (window == IntPtr.Zero) (window, title) = source.FrameFor(id);
                    if (window == IntPtr.Zero || string.IsNullOrWhiteSpace(title) || source.IsHiddenOrTool(window)) continue;
                    found.Add(name, new PickedApp(name, Safe(() => source.ImagePath(id), null), Safe(() => source.Aumid(id), null)));
                }
                catch (Exception e) when (Guarded(e))
                {
                    // Exited or refused while being looked at: leave it out.
                }
            }
            finally
            {
                Safe(() => { process.Dispose(); return true; }, false);
            }
        }
        return found.Values.Take(Math.Max(0, max)).ToList();
    }

    // The app a program file stands for, or null for anything that isn't a program. A shortcut has
    // already become its target: the dialog follows links.
    public static PickedApp? FromFile(string? file)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        try
        {
            string path = file.Trim().Trim('"');
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
            string name = Key(path);
            return name.Length == 0 || Excluded(name) ? null : new PickedApp(name, path, null);
        }
        catch (Exception e) when (Guarded(e))
        {
            return null;
        }
    }

    // Whether the list holds the app already, compared by name whatever the case, with or without
    // ".exe" or a folder.
    public static bool Contains(IEnumerable<string> names, string app)
    {
        string key = Key(app);
        return key.Length > 0 && Safe(() => names.Any(name => Key(name).Equals(key, StringComparison.OrdinalIgnoreCase)), false);
    }

    // The app's own icon, or the apps glyph for a packaged app and for one whose icon can't be read.
    // Always an image size × size pixels, which the caller disposes.
    public static Bitmap IconFor(PickedApp app, int size, Color glyphColor)
    {
        size = Math.Clamp(size, 1, 256);
        if (app.Aumid is null && ProcessInfo.Icon(app.Path, size) is { } icon) return icon;
        return AppsGlyph(size, glyphColor);
    }

    // The apps glyph that stands in for an app's own icon, size × size pixels, which the caller disposes.
    public static Bitmap AppsGlyph(int size, Color color) => Glyph(Glyphs.Apps, Math.Clamp(size, 1, 256), color);

    static Bitmap Glyph(string glyph, int size, Color color)
    {
        var image = new Bitmap(size, size);
        try
        {
            using var g = Graphics.FromImage(image);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var font = new Font(Theme.Icons().FontFamily, size * 0.8f, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);
            using var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), format);
        }
        catch (Exception e) when (Guarded(e))
        {
            // A blank square is better than no row.
        }
        return image;
    }

    // Hands the app on once, unless the list has it already. A mistake in the caller is written to the
    // log, not thrown at the menu, which would end the app.
    static void Hand(PickedApp app, IReadOnlyCollection<string> already, Action<PickedApp> picked)
    {
        if (Contains(already, app.Name)) return;
        try
        {
            picked(app);
        }
        catch (Exception e) when (Guarded(e))
        {
            Log.Write($"could not add {app.Name}: {e.Message}");
        }
    }

    static string? ChooseFile(Control owner, string filter)
    {
        using var dialog = new OpenFileDialog { Filter = filter, DereferenceLinks = true, CheckFileExists = true, Multiselect = false };
        return dialog.ShowDialog(owner.FindForm()) == DialogResult.OK ? dialog.FileName : null;
    }

    static bool Excluded(string name) => NeverListed.Contains(name, StringComparer.OrdinalIgnoreCase);

    static string Key(string name) => ProcessRule.Normalize(name ?? "");

    // Walks the source one process at a time, stopping quietly where it fails.
    static IEnumerable<IRunningProcess> Each(IProcessSource source)
    {
        IEnumerator<IRunningProcess>? walk = null;
        try
        {
            walk = source.Running().GetEnumerator();
        }
        catch (Exception e) when (Guarded(e))
        {
            yield break;
        }
        try
        {
            while (true)
            {
                IRunningProcess? next = null;
                try
                {
                    if (!walk.MoveNext()) yield break;
                    next = walk.Current;
                }
                catch (Exception e) when (Guarded(e))
                {
                    yield break;
                }
                if (next is not null) yield return next;
            }
        }
        finally
        {
            Safe(() => { walk.Dispose(); return true; }, false);
        }
    }

    static T Safe<T>(Func<T> ask, T otherwise)
    {
        try
        {
            return ask();
        }
        catch (Exception e) when (Guarded(e))
        {
            return otherwise;
        }
    }

    // Everything except the failures nobody can carry on from.
    static bool Guarded(Exception e) => e is not (OutOfMemoryException or StackOverflowException or AccessViolationException);

    // The processes Windows has right now.
    internal static IProcessSource Windows => WindowsProcesses.Instance;

    sealed class WindowsProcesses : IProcessSource
    {
        public static readonly WindowsProcesses Instance = new();

        public IEnumerable<IRunningProcess> Running() => Process.GetProcesses().Select(p => (IRunningProcess)new WindowsProcess(p));

        public bool IsHiddenOrTool(IntPtr window) => ProcessInfo.IsHiddenOrTool(window);

        public string? ImagePath(int processId) => ProcessInfo.ImagePath(processId);

        public string? Aumid(int processId) => ProcessInfo.Aumid(processId);

        // The Store apps' frames on screen, by the app's own process, looked at once for a whole list
        // of processes rather than once for each.
        readonly object gate = new();
        long framesAt;
        Dictionary<int, (IntPtr Window, string Title)>? frames;

        public (IntPtr Window, string Title) FrameFor(int processId)
        {
            Dictionary<int, (IntPtr Window, string Title)> now;
            lock (gate)
            {
                if (frames is null || Environment.TickCount64 - framesAt > 1000)
                {
                    frames = Frames();
                    framesAt = Environment.TickCount64;
                }
                now = frames;
            }
            return now.TryGetValue(processId, out var frame) ? frame : (IntPtr.Zero, "");
        }

        // A frame is the one top-level window whose app is not the process that made it: Windows
        // gives it the app's process (see Native.TopLevelWindows).
        static Dictionary<int, (IntPtr Window, string Title)> Frames()
        {
            var found = new Dictionary<int, (IntPtr Window, string Title)>();
            foreach (var window in Native.TopLevelWindows())
            {
                if (!window.Visible || window.Cloaked || window.Tool) continue;
                Native.GetWindowThreadProcessId(window.Handle, out uint madeBy);
                if (madeBy == 0 || madeBy == window.ProcessId) continue;
                found.TryAdd(window.ProcessId, (window.Handle, Title(window.Handle)));
            }
            return found;
        }

        static string Title(IntPtr window)
        {
            var buffer = new char[512];
            int length = GetWindowText(window, buffer, buffer.Length);
            return length > 0 ? new string(buffer, 0, length) : "";
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
        static extern int GetWindowText(IntPtr window, [Out] char[] text, int capacity);
    }

    sealed class WindowsProcess(Process process) : IRunningProcess
    {
        public int Id => process.Id;
        public string Name => process.ProcessName;
        public IntPtr MainWindow => process.MainWindowHandle;
        public string MainWindowTitle => process.MainWindowTitle;
        public void Dispose() => process.Dispose();
    }
}
