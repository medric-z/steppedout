using System.Diagnostics;

namespace SteppedOut.Tests;

// Finding programs by name, and the app rules built on it.
[TestClass]
public class ProcessRuleTests
{
    static readonly int Mine = ProcessRule.MySession;

    static TopWindow Window(int process, bool visible = true, bool minimized = false, bool cloaked = false, bool onAMonitor = true, bool tool = false) =>
        new(new IntPtr(process * 10), process, visible, minimized, cloaked, tool, onAMonitor);

    static List<WhyLine> Check(AppRules rules, FakeWorld world)
    {
        var lines = new List<WhyLine>();
        rules.Check(world.Look(TimeSpan.FromSeconds(1)), lines);
        return lines;
    }

    static AppRules Apps(params AppRule[] list)
    {
        var rules = new AppRules();
        rules.Set(list);
        return rules;
    }

    [TestMethod]
    public void NormalizesNamesPathsAndExtensions()
    {
        Assert.AreEqual("obs64", ProcessRule.Normalize("obs64"));
        Assert.AreEqual("obs64", ProcessRule.Normalize(" obs64.exe "));
        Assert.AreEqual("obs64", ProcessRule.Normalize(@"C:\Program Files\obs-studio\bin\64bit\obs64.exe"));
        Assert.AreEqual("obs64", ProcessRule.Normalize(@" ""C:\Program Files\obs-studio\bin\64bit\obs64.exe"" "), "Explorer's Copy as path");
        Assert.AreEqual("", ProcessRule.Normalize("   "));
    }

    [TestMethod]
    public void FindsEveryNamedProgramRunningInThisSession()
    {
        var processes = new[]
        {
            new RunningProcess(1, "obs64", Mine),
            new RunningProcess(2, "Chrome", Mine),
            new RunningProcess(3, "chrome", Mine),
            new RunningProcess(4, "blender", Mine + 1),
            new RunningProcess(5, "notes", Mine),
        };
        var names = new HashSet<string>(new[] { "OBS64", "chrome", "blender" }, StringComparer.OrdinalIgnoreCase);
        CollectionAssert.AreEquivalent(new[] { "obs64", "Chrome" }, ProcessRule.Running(processes, names).ToArray(),
            "all matches, each once, and not one that runs only in another session");
        Assert.IsEmpty(ProcessRule.Running(processes, new HashSet<string>()));
    }

    [TestMethod]
    public void FindsThisProcessInTheRealList()
    {
        var me = Process.GetCurrentProcess();
        var found = Native.Processes().Where(p => p.Id == me.Id).ToList();
        Assert.HasCount(1, found);
        Assert.AreEqual(me.ProcessName, found[0].Name);
        Assert.AreEqual(Mine, found[0].Session);
    }

    [TestMethod]
    public void TheRealProcessTreeKnowsThisProcess()
    {
        Assert.IsTrue(Native.ParentIds().ContainsKey(Environment.ProcessId));
    }

    [TestMethod]
    public void TheRealWindowListDoesNotThrow()
    {
        var windows = Native.TopLevelWindows();
        Assert.IsTrue(windows.All(w => w.ProcessId > 0));
        Assert.IsTrue(windows.Where(w => !w.Visible).All(w => !w.OnScreen));
    }

    [TestMethod]
    public void AnAppRuleAddsItsEffectOnceHoweverManyProcessesItRuns()
    {
        var world = new FakeWorld();
        for (int i = 0; i < 12; i++) world.Run(100 + i, "chrome");
        CollectionAssert.AreEqual(new[] { new WhyLine(Signal.App, Effect.Extra(30), "chrome") }, Check(Apps(new AppRule("chrome.exe", Effect.Extra(30))), world));
    }

    [TestMethod]
    public void EachAppRuleCountsOnItsOwn()
    {
        var world = new FakeWorld();
        world.Run(1, "obs64");
        world.Run(2, "render");
        var lines = Check(Apps(new AppRule("obs64", Effect.Never), new AppRule("render", Effect.Sooner(50)), new AppRule("absent", Effect.Cap(5))), world);
        CollectionAssert.AreEqual(new[] { new WhyLine(Signal.App, Effect.Never, "obs64"), new WhyLine(Signal.App, Effect.Sooner(50), "render") }, lines);
    }

    [TestMethod]
    public void TheFirstRuleForAnAppWins()
    {
        var world = new FakeWorld();
        world.Run(1, "chrome");
        var rules = Apps(new AppRule("chrome", Effect.Extra(30)), new AppRule(@"C:\x\CHROME.exe", Effect.Sooner(50)), new AppRule(" ", Effect.Never), new AppRule("x", Effect.None));
        CollectionAssert.AreEqual(new[] { new WhyLine(Signal.App, Effect.Extra(30), "chrome") }, Check(rules, world));
    }

    [TestMethod]
    public void AnAppRunningOnlyForAnotherUserDoesNotCount()
    {
        var world = new FakeWorld();
        world.Processes.Add(new RunningProcess(1, "obs64", Mine + 1));
        Assert.IsEmpty(Check(Apps(new AppRule("obs64", Effect.Never)), world));
    }

    [TestMethod]
    public void OnScreenNeedsAWindowThatCanBeSeen()
    {
        var rules = Apps(new AppRule("obs64", Effect.Never, AppWhen.OnScreen));
        var world = new FakeWorld();
        world.Run(1, "obs64");
        Assert.IsEmpty(Check(rules, world), "running with no window");

        world.Windows.Add(Window(1, minimized: true));
        world.Windows.Add(Window(1, visible: false));
        world.Windows.Add(Window(1, cloaked: true));
        world.Windows.Add(Window(1, onAMonitor: false));
        Assert.IsEmpty(Check(rules, world), "minimized, hidden, on another virtual desktop or off every screen");

        world.Windows.Add(Window(1, tool: true));
        CollectionAssert.AreEqual(new[] { new WhyLine(Signal.App, Effect.Never, "obs64") }, Check(rules, world), "any window that can be seen");
    }

    [TestMethod]
    public void OnScreenGoesByTheWindowsProcess()
    {
        var world = new FakeWorld();
        world.Run(1, "obs64");
        world.Run(2, "notes");
        world.Windows.Add(Window(2));
        Assert.IsEmpty(Check(Apps(new AppRule("obs64", Effect.Never, AppWhen.OnScreen)), world));
        CollectionAssert.AreEquivalent(new[] { "notes" }, AppRules.OnScreen(world.Windows, world.Processes).ToArray());
    }

    [TestMethod]
    public void ARuleWhenRunningNeedsNoWindow()
    {
        var world = new FakeWorld();
        world.Run(1, "obs64");
        world.Failing.Add("windows");
        CollectionAssert.AreEqual(new[] { new WhyLine(Signal.App, Effect.Never, "obs64") }, Check(Apps(new AppRule("obs64", Effect.Never)), world),
            "the windows are not even read");
    }
}
