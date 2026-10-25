namespace SteppedOut.Tests;

// The app picker lists what is open from a stand-in process source, so these tests never depend on
// what runs on the machine, and never close or start anything. The names are made up.
// One at a time: the icons and menus fill the shared font cache, which is not made for several threads.
[TestClass]
[DoNotParallelize]
public class AppPickerTests
{
    static readonly PickerText Words = new("Choose another app…", "Programs|*.exe");

    sealed class Fake(int id, string name, IntPtr window, string title, bool helper = false) : IRunningProcess
    {
        public int Id => id;
        public string Name => name;
        public IntPtr MainWindow => window;
        public string MainWindowTitle => title;
        public bool Helper => helper;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    sealed class Source(params Fake[] processes) : IProcessSource
    {
        public IEnumerable<IRunningProcess> Running() => processes;
        public bool IsHiddenOrTool(IntPtr window) => processes.First(p => p.MainWindow == window).Helper;
        public string? ImagePath(int processId) => @"C:\Apps\" + processes.First(p => p.Id == processId).Name + ".exe";
        public string? Aumid(int processId) => null;
    }

    // Throws from every call it can be asked.
    sealed class Broken : IRunningProcess
    {
        public int Id => throw new InvalidOperationException("exited");
        public string Name => throw new InvalidOperationException("exited");
        public IntPtr MainWindow => throw new System.ComponentModel.Win32Exception(5);
        public string MainWindowTitle => throw new NotSupportedException();
        public void Dispose() => throw new ObjectDisposedException("process");
    }

    sealed class Throwing(bool list) : IProcessSource
    {
        public IEnumerable<IRunningProcess> Running() => list ? [new Broken(), new Broken()] : throw new System.ComponentModel.Win32Exception(5);
        public bool IsHiddenOrTool(IntPtr window) => throw new InvalidOperationException();
        public string? ImagePath(int processId) => throw new UnauthorizedAccessException();
        public string? Aumid(int processId) => throw new EntryPointNotFoundException();
    }

    // Lists the good process, then fails, as Windows can when a process exits mid-listing.
    sealed class FailsHalfway(Fake good) : IProcessSource
    {
        public IEnumerable<IRunningProcess> Running()
        {
            yield return good;
            throw new InvalidOperationException("the list changed");
        }

        public bool IsHiddenOrTool(IntPtr window) => false;
        public string? ImagePath(int processId) => throw new UnauthorizedAccessException();
        public string? Aumid(int processId) => throw new UnauthorizedAccessException();
    }

    // Store apps run with no window of their own: each shows in a frame ApplicationFrameHost makes,
    // given here by the app's process.
    sealed class Framed(Fake[] processes, Dictionary<int, (IntPtr Window, string Title)> frames) : IProcessSource
    {
        public IEnumerable<IRunningProcess> Running() => processes;
        public bool IsHiddenOrTool(IntPtr window) => window == new IntPtr(3099);
        public string? ImagePath(int processId) => @"C:\Program Files\WindowsApps\" + processes.First(p => p.Id == processId).Name + ".exe";
        public string? Aumid(int processId) => processId == 21 ? "SampleCo.SampleCalc_abc123xyz!App" : null;
        public (IntPtr Window, string Title) FrameFor(int processId) => frames.TryGetValue(processId, out var frame) ? frame : (IntPtr.Zero, "");
    }

    static Fake App(int id, string name, bool helper = false) => new(id, name, new IntPtr(1000 + id), name + " window", helper);

    [TestMethod]
    public void AStoreAppIsListedByTheFrameItShowsIn()
    {
        var source = new Framed(
            [
                new Fake(21, "SampleCalc", IntPtr.Zero, ""),
                new Fake(22, "SampleBackground", IntPtr.Zero, ""),
                new Fake(23, "SampleUntitled", IntPtr.Zero, ""),
                new Fake(24, "SampleCloaked", IntPtr.Zero, ""),
                App(13, "ApplicationFrameHost"),
                App(10, "Sketchpad"),
            ],
            new()
            {
                [21] = (new IntPtr(3021), "Sample Calculator"),
                [23] = (new IntPtr(3023), " "),
                [24] = (new IntPtr(3099), "Hidden"),
                [10] = (new IntPtr(3010), "Never asked: Sketchpad has a window of its own"),
            });

        var found = AppPicker.Running(source, []);

        CollectionAssert.AreEqual(new[] { "SampleCalc", "Sketchpad" }, Names(found));
        Assert.AreEqual("SampleCo.SampleCalc_abc123xyz!App", found[0].Aumid, "a Store app keeps its Start menu id, so Open apps can start it");
    }

    static List<string> Names(IEnumerable<PickedApp> apps) => apps.Select(a => a.Name).ToList();

    [TestMethod]
    public void ASourceThatThrowsFromEveryCallGivesAnEmptyList()
    {
        Assert.IsEmpty(AppPicker.Running(new Throwing(list: false), []));
        Assert.IsEmpty(AppPicker.Running(new Throwing(list: true), []));
    }

    [TestMethod]
    public void AListThatFailsHalfwayKeepsWhatCameBeforeAndAFailedPathIsJustMissing()
    {
        var good = App(10, "Sketchpad");
        var found = AppPicker.Running(new FailsHalfway(good), []);

        Assert.HasCount(1, found);
        Assert.AreEqual(new PickedApp("Sketchpad", null, null), found[0]);
        Assert.IsTrue(good.Disposed);
    }

    [TestMethod]
    public void AnAlreadyListThatThrowsIsTreatedAsEmpty()
    {
        static IEnumerable<string> Bad()
        {
            yield return "Sketchpad";
            throw new InvalidOperationException();
        }

        Assert.HasCount(1, AppPicker.Running(new Source(App(10, "Sketchpad")), Bad()));
    }

    [TestMethod]
    public void LeavesOutSteppedOutHelpersUntitledAndWindowlessApps()
    {
        var source = new Source(
            App(10, "Sketchpad"),
            App(11, "SteppedOut"),
            App(12, "steppedout"),
            App(13, "ApplicationFrameHost"),
            App(14, "Overlay", helper: true),
            new Fake(15, "Untitled", new IntPtr(2015), "   "),
            new Fake(16, "Windowless", IntPtr.Zero, "Has a title"),
            App(Environment.ProcessId, "TheHarnessItself"));

        CollectionAssert.AreEqual(new[] { "Sketchpad" }, Names(AppPicker.Running(source, [])));
    }

    [TestMethod]
    public void EachNameOnceWhateverTheCaseAndNotTheOnesAlreadyListed()
    {
        var source = new Source(
            App(10, "Writer"),
            App(11, "writer"),
            App(12, "WRITER"),
            App(13, "Sketchpad"),
            App(14, "Mixer"),
            App(15, "Board"));

        CollectionAssert.AreEqual(new[] { "Board", "Mixer", "Sketchpad", "Writer" }, Names(AppPicker.Running(source, [])));
        CollectionAssert.AreEqual(new[] { "Board", "Writer" },
            Names(AppPicker.Running(source, ["SKETCHPAD", @"C:\Tools\mixer.EXE", ""])));
    }

    [TestMethod]
    public void ListsAtMostTheMaximumAndDisposesEveryProcess()
    {
        var processes = Enumerable.Range(0, 40).Select(i => App(100 + i, "App" + i.ToString("D2"))).ToArray();

        var found = AppPicker.Running(new Source(processes), []);

        Assert.HasCount(AppPicker.MaxListed, found);
        Assert.AreEqual("App00", found[0].Name);
        Assert.AreEqual(@"C:\Apps\App00.exe", found[0].Path);
        Assert.IsTrue(processes.All(p => p.Disposed));
    }

    [TestMethod]
    public void AProgramFileBecomesItsNameAndAnythingElseIsNothing()
    {
        Assert.AreEqual(new PickedApp("Sketchpad", @"C:\Apps\Sketchpad.EXE", null), AppPicker.FromFile(@"C:\Apps\Sketchpad.EXE"));
        Assert.AreEqual(new PickedApp("Sketchpad", @"C:\My Apps\Sketchpad.exe", null), AppPicker.FromFile(@" ""C:\My Apps\Sketchpad.exe"" "));
        Assert.IsNull(AppPicker.FromFile(null));
        Assert.IsNull(AppPicker.FromFile("   "));
        Assert.IsNull(AppPicker.FromFile(@"C:\Notes\list.txt"));
        Assert.IsNull(AppPicker.FromFile(@"C:\Apps\.exe"));
        Assert.IsNull(AppPicker.FromFile(@"C:\Apps\SteppedOut.exe"));
    }

    [TestMethod]
    public void ContainsComparesNamesWhateverTheCaseExtensionOrFolder()
    {
        Assert.IsTrue(AppPicker.Contains(["Sketchpad"], "sketchpad"));
        Assert.IsTrue(AppPicker.Contains([@"C:\Apps\SKETCHPAD.exe"], "Sketchpad.EXE"));
        Assert.IsFalse(AppPicker.Contains(["Sketchpad"], "Sketch"));
        Assert.IsFalse(AppPicker.Contains(["", "  "], ""));
    }

    [TestMethod]
    public void TheMenuOfABrokenSourceStillOffersTheFilesAndNothingEscapes()
    {
        using var button = new Control();
        PickedApp? got = null;
        using var menu = AppPicker.Menu(button, ["Writer"], app => got = app, Words with { Header = "Open right now" }, new Throwing(list: true),
            chooseFile: () => throw new System.ComponentModel.Win32Exception(1223));

        var labels = menu.Items.Cast<ToolStripItem>().Select(i => i.Text).ToList();
        CollectionAssert.AreEqual(new[] { "Open right now", "", "Choose another app…" }, labels);
        Assert.IsFalse(menu.Items[0].Enabled);

        menu.Items[^1].PerformClick();
        Assert.IsNull(got);
    }

    [TestMethod]
    public void PickingARunningAppOrAFileHandsItOnOnce()
    {
        using var button = new Control();
        var got = new List<PickedApp>();
        string? file = @"C:\Apps\Board.exe";
        using var menu = AppPicker.Menu(button, ["Writer"], got.Add, Words, new Source(App(10, "Sketchpad"), App(11, "Writer")), () => file);

        var labels = menu.Items.Cast<ToolStripItem>().Select(i => i.Text).ToList();
        CollectionAssert.AreEqual(new[] { "Sketchpad", "", "Choose another app…" }, labels);
        Assert.IsNotNull(menu.Items[0].Image);

        menu.Items[0].PerformClick();
        menu.Items[^1].PerformClick();
        file = @"C:\Apps\writer.exe";
        menu.Items[^1].PerformClick();
        file = @"C:\Apps\readme.txt";
        menu.Items[^1].PerformClick();
        file = null;
        menu.Items[^1].PerformClick();

        CollectionAssert.AreEqual(new[]
        {
            new PickedApp("Sketchpad", @"C:\Apps\Sketchpad.exe", null),
            new PickedApp("Board", @"C:\Apps\Board.exe", null),
        }, got);
    }

    [TestMethod]
    public void AMistakeInTheCallerIsNotThrownAtTheMenu()
    {
        using var button = new Control();
        using var menu = AppPicker.Menu(button, [], _ => throw new InvalidOperationException("caller bug"), Words, new Source(App(10, "Sketchpad")),
            () => @"C:\Apps\Board.exe");

        menu.Items[0].PerformClick();
        menu.Items[^1].PerformClick();
    }

    [TestMethod]
    public void AnEmptyMenuHasNoSeparator()
    {
        using var button = new Control();
        using var menu = AppPicker.Menu(button, [], _ => { }, Words, new Source(), () => null);

        CollectionAssert.AreEqual(new[] { "Choose another app…" }, menu.Items.Cast<ToolStripItem>().Select(i => i.Text).ToList());
    }

    [TestMethod]
    public void AnIconIsAlwaysAnImageOfTheAskedSize()
    {
        using var packaged = AppPicker.IconFor(new PickedApp("Calc", Environment.ProcessPath, "Some.Package_abc!App"), 24, Color.Gray);
        using var missing = AppPicker.IconFor(new PickedApp("Gone", @"C:\No\Such\Folder\gone.exe", null), 20, Color.Gray);
        using var noPath = AppPicker.IconFor(new PickedApp("Plain", null, null), 16, Color.Gray);
        using var own = AppPicker.IconFor(new PickedApp("Self", Environment.ProcessPath, null), 32, Color.Gray);

        Assert.AreEqual(new Size(24, 24), packaged.Size);
        Assert.AreEqual(new Size(20, 20), missing.Size);
        Assert.AreEqual(new Size(16, 16), noPath.Size);
        Assert.AreEqual(new Size(32, 32), own.Size);
        Assert.IsTrue(HasInk(packaged), "the apps glyph is drawn");
        Assert.IsTrue(HasInk(own), "the program's own icon");
    }

    [TestMethod]
    public void ProcessInfoWorksOnTheCurrentProcess()
    {
        Assert.AreEqual(Environment.ProcessPath, ProcessInfo.ImagePath(Environment.ProcessId), ignoreCase: true);
        Assert.IsNull(ProcessInfo.Aumid(Environment.ProcessId), "the test host is not a packaged app");
        Assert.IsNull(ProcessInfo.PackageFamilyName(Environment.ProcessId));
    }

    [TestMethod]
    public void ProcessInfoAnswersNullForProcessesThatDoNotExist()
    {
        foreach (int id in new[] { 0, -1, int.MaxValue - 3 })
        {
            Assert.IsNull(ProcessInfo.ImagePath(id), id.ToString());
            Assert.IsNull(ProcessInfo.Aumid(id), id.ToString());
            Assert.IsNull(ProcessInfo.PackageFamilyName(id), id.ToString());
        }
        Assert.IsTrue(ProcessInfo.IsHiddenOrTool(IntPtr.Zero));
    }

    [TestMethod]
    public void IconsAreCachedAndEachCallerGetsItsOwnCopy()
    {
        ProcessInfo.ClearIcons();
        Assert.AreEqual(0, ProcessInfo.CachedIcons);

        var first = ProcessInfo.Icon(Environment.ProcessPath, 16);
        Assert.IsNotNull(first);
        first.Dispose();
        using var second = ProcessInfo.Icon(Environment.ProcessPath, 16);
        Assert.IsNotNull(second);
        Assert.AreEqual(new Size(16, 16), second.Size);
        _ = second.GetPixel(8, 8);
        Assert.AreEqual(1, ProcessInfo.CachedIcons);

        Assert.IsNull(ProcessInfo.Icon(@"C:\No\Such\Folder\gone.exe", 16));
        Assert.IsNull(ProcessInfo.Icon(null, 16));
        Assert.IsNull(ProcessInfo.Icon(Environment.ProcessPath, 0));
        Assert.IsNull(ProcessInfo.Icon("bad\0path.exe", 16));

        ProcessInfo.ClearIcons();
        Assert.AreEqual(0, ProcessInfo.CachedIcons);
        _ = second.GetPixel(8, 8);
    }

    [TestMethod]
    public void TheRealProcessListNeverThrowsOrListsItself()
    {
        var found = AppPicker.Running(AppPicker.Windows, []);

        Assert.IsLessThanOrEqualTo(AppPicker.MaxListed, found.Count);
        string self = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        Assert.IsFalse(found.Any(a => a.Name.Equals(self, StringComparison.OrdinalIgnoreCase) || a.Name.Equals("SteppedOut", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(found.Count, found.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    static bool HasInk(Bitmap image)
    {
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if (image.GetPixel(x, y).A > 0) return true;
        return false;
    }
}
