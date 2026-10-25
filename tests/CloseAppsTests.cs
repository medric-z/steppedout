using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SteppedOut.Tests;

// Closing apps can lose work, so this is tested hardest. Every name here is made up; no real app's
// name may appear, because matching is by name and a real one would close the windows of whoever
// runs the tests. The real tests at the end close only a little program they build and start
// themselves under a name made from a new GUID, through a wrapper that refuses any other process.
[TestClass]
public class CloseAppsTests
{
    static void Wait(Task task, string what = "done in time") => AwayFakes.Wait(task, what);

    sealed class Run : IDisposable
    {
        readonly List<string> log = new();

        public AwayFakes.Recorder Rec { get; } = new();

        public AwayFakes.ManualTime Time { get; } = new();

        public AwayFakes.FakeProcesses Api { get; }

        public Run() => Api = new AwayFakes.FakeProcesses(Rec);

        public List<string> Log
        {
            get
            {
                lock (log) return log.ToList();
            }
        }

        public CloseApps Close(params string[] names) => Through(Api, names);

        // The same, through a wrapper round the made-up list.
        public CloseApps Through(IProcessApi api, params string[] names) => On(whenAway: true, api, names);

        // For one side: when you leave, or when you are back. Both close the same way.
        public CloseApps On(bool whenAway, params string[] names) => On(whenAway, Api, names);

        public CloseApps On(bool whenAway, IProcessApi api, params string[] names) => new(names, whenAway, api, Time, TimeSpan.FromSeconds(30), line =>
        {
            lock (log) log.Add(line);
        });

        public List<string> Asked => Rec.Starting("ask ");

        public List<string> Forced => Rec.Starting("force ");

        public void Dispose()
        {
        }
    }

    [TestMethod]
    public void OnlyListedAppsInThisSignInAreAskedWhateverFormTheNameIsWrittenIn()
    {
        using var run = new Run();
        run.Api.Add(2000, "inkwell.exe").ClosesWhenAsked = true;
        run.Api.Add(2001, "Q7APP.EXE").ClosesWhenAsked = true;
        run.Api.Add(2002, "zylophane.exe").ClosesWhenAsked = true;
        run.Api.Add(2003, "bystander.exe");
        run.Api.Add(2004, "inkwell.exe", session: 2);

        var done = run.Close("Inkwell", "\"C:\\Tools\\q7app.exe\"", " ZYLOPHANE.EXE ").Close(CancellationToken.None);
        Wait(done);

        CollectionAssert.AreEquivalent(new[] { "ask inkwell.exe", "ask Q7APP.EXE", "ask zylophane.exe" }, run.Asked);
        Assert.IsEmpty(run.Forced);
        Assert.AreEqual(0, run.Time.Pending, "everything closed, so the wait is over");
    }

    [TestMethod]
    public void WindowsOwnProcessesAreNeverClosedEvenWhenListed()
    {
        using var run = new Run();
        run.Api.Add(2000, "explorer.exe");
        run.Api.Add(2001, "dwm.exe");
        run.Api.Add(2002, "ApplicationFrameHost.exe");

        var done = run.Close("explorer", "DWM.exe", "applicationframehost").Close(CancellationToken.None);

        Assert.IsTrue(done.IsCompleted);
        Assert.IsEmpty(run.Rec.Events);
        Assert.AreEqual(0, run.Api.Opened, "not even opened");
        CollectionAssert.Contains(run.Log, "never closes explorer: Windows needs it");

        // Listed next to an app that is closed, they still are not.
        run.Api.Add(2003, "inkwell.exe").ClosesWhenAsked = true;
        Wait(run.Close("explorer", "inkwell", "dwm").Close(CancellationToken.None));
        CollectionAssert.AreEqual(new[] { "ask inkwell.exe" }, run.Rec.Events);
        Assert.AreEqual(1, run.Api.Opened);
    }

    [TestMethod]
    public void TheGuardListHoldsEveryPartOfWindowsThatRunsInsideASignIn()
    {
        string[] plan =
        [
            "explorer", "dwm", "csrss", "winlogon", "lsass", "svchost", "sihost", "ShellExperienceHost", "StartMenuExperienceHost",
            "ctfmon", "conhost", "ApplicationFrameHost", "RuntimeBroker", "SearchHost", "TextInputHost", "taskhostw", "fontdrvhost",
            "LogonUI", "LockApp", "dllhost", "SystemSettings", "SecurityHealthSystray", "backgroundTaskHost", "wininit", "services",
            "smss", "audiodg",
        ];

        foreach (string name in plan)
        {
            Assert.Contains(name, CloseApps.Guarded, name);
            Assert.Contains(name.ToUpperInvariant(), CloseApps.Guarded, name + " in capitals");
        }
    }

    [TestMethod]
    public void SteppedOutIsNeverClosedByItsProcessOrByItsProgramFileWhateverTheFileIsCalled()
    {
        using var run = new Run();
        run.Api.Own.Exe = "SteppedOut-win-x64.exe";
        run.Api.Own.Path = @"C:\Downloads\SteppedOut-win-x64.exe";
        run.Api.OwnPath = @"C:\Downloads\SteppedOut-win-x64.exe";
        run.Api.Add(2000, "SteppedOut-win-x64.exe", path: @"C:\DOWNLOADS\steppedout-win-x64.exe");
        run.Api.Add(2001, "SteppedOut-win-x64.exe", path: @"C:\Other\SteppedOut-win-x64.exe").ClosesWhenAsked = true;

        Wait(run.Close("SteppedOut-win-x64").Close(CancellationToken.None));

        CollectionAssert.AreEqual(new[] { "ask SteppedOut-win-x64.exe" }, run.Asked);
        Assert.HasCount(1, run.Api.Asked);
        Assert.AreEqual(run.Api.Get(2001).Windows[0], run.Api.Asked[0], "only the program at another path");
        CollectionAssert.Contains(run.Log, "left SteppedOut-win-x64 (pid 1000) open: it is SteppedOut");
        CollectionAssert.Contains(run.Log, "left SteppedOut-win-x64 (pid 2000) open: it is SteppedOut");
    }

    [TestMethod]
    public void EveryProgramSteppedOutWasStartedFromIsLeftOpenWithEveryCopyOfIt()
    {
        using var run = new Run();
        run.Api.Own.ParentId = 900;
        run.Api.Add(800, "shellhost2.exe", parent: 0, start: 50);
        run.Api.Add(900, "termhost.exe", parent: 800, start: 100);
        // Another window of the same terminal, which is a process of its own.
        run.Api.Add(901, "termhost.exe", parent: 0, start: 100).ClosesWhenAsked = true;
        run.Api.Add(2000, "inkwell.exe").ClosesWhenAsked = true;

        Wait(run.Close("termhost", "shellhost2", "inkwell").Close(CancellationToken.None));

        CollectionAssert.AreEqual(new[] { "ask inkwell.exe" }, run.Asked, "only the program SteppedOut was not started from");
        Assert.AreEqual(1, run.Api.Opened, "the others are not even opened");
        CollectionAssert.Contains(run.Log, "left termhost (pid 900, 901) open: SteppedOut was started from it");
        CollectionAssert.Contains(run.Log, "left shellhost2 (pid 800) open: SteppedOut was started from it");
    }

    // The audit's case: SteppedOut opened from a browser's download list, and that browser is on the
    // list. Its main process is SteppedOut's parent; its tab and graphics processes have no window to
    // ask and would be forced at 30 s, crashing every tab. The whole browser is left alone instead,
    // on either side, while the other listed programs are closed as usual.
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void TheProgramSteppedOutWasStartedFromIsNeverAskedOrForcedNorAnyOfItsProcesses(bool whenAway)
    {
        using var run = new Run();
        run.Api.Own.ParentId = 900;
        run.Api.Add(800, "shellx.exe", parent: 0, start: 10, windows: 0);
        var main = run.Api.Add(900, "browsr.exe", parent: 800, start: 100, windows: 1);
        var renderer = run.Api.Add(901, "browsr.exe", parent: 900, start: 200, windows: 0);
        var gpu = run.Api.Add(902, "browsr.exe", parent: 900, start: 200, windows: 0);
        var otherWindow = run.Api.Add(903, "browsr.exe", parent: 0, start: 300, windows: 1);
        var elsewhere = run.Api.Add(904, "browsr.exe", parent: 0, start: 300, windows: 1, session: 2);
        var stubborn = run.Api.Add(2000, "inkwell.exe", windows: 1);

        var done = run.On(whenAway, "browsr", "inkwell").Close(CancellationToken.None);
        run.Time.Advance(TimeSpan.FromSeconds(30));
        Wait(done);

        Assert.IsFalse(main.Exited || renderer.Exited || gpu.Exited || otherWindow.Exited || elsewhere.Exited, string.Join(" | ", run.Log));
        CollectionAssert.AreEqual(new[] { "ask inkwell.exe", "force inkwell.exe" }, run.Rec.Events);
        Assert.IsTrue(stubborn.Exited);
        Assert.AreEqual(1, run.Api.Opened, "no process of the browser was even opened");
        Assert.HasCount(1, run.Log.Where(line => line.Contains("browsr")).ToList(), "one line for the browser: " + string.Join(" | ", run.Log));
        CollectionAssert.Contains(run.Log, "left browsr (pid 900, 901, 902 and 1 more) open: SteppedOut was started from it", "a short line, however many processes");
        Assert.AreEqual(run.Api.Opened, run.Api.Disposed, "every handle given back");
    }

    // Started from another copy of SteppedOut's own program, with that program on the list: the line
    // names the copy it was started from, never SteppedOut's own process, which is no ancestor.
    [TestMethod]
    public void StartedFromAnotherCopyOfItselfTheLineNamesThatCopyAndNotItself()
    {
        using var run = new Run();
        run.Api.Own.ParentId = 900;
        run.Api.Add(900, "SteppedOut.exe", parent: 0, start: 100, path: @"C:\Tools\SteppedOut.exe");

        Wait(run.Close("SteppedOut").Close(CancellationToken.None));

        Assert.IsEmpty(run.Asked);
        CollectionAssert.AreEqual(new[] { "left SteppedOut (pid 900) open: SteppedOut was started from it" }, run.Log);
    }

    [TestMethod]
    public void AParentNumberALaterProcessGotIsNotAnAncestor()
    {
        using var run = new Run();
        // SteppedOut started at 500; its parent exited, and a process started at 700 got the number.
        run.Api.Own.ParentId = 900;
        run.Api.Add(900, "termhost.exe", parent: 0, start: 700).ClosesWhenAsked = true;

        Wait(run.Close("termhost").Close(CancellationToken.None));

        CollectionAssert.AreEqual(new[] { "ask termhost.exe" }, run.Asked);
    }

    [TestMethod]
    public void AParentWhoseStartTimeCannotBeReadCountsAsAnAncestor()
    {
        using var run = new Run();
        run.Api.Own.ParentId = 900;
        run.Api.Add(900, "termhost.exe", parent: 0, start: null);

        Wait(run.Close("termhost").Close(CancellationToken.None));

        Assert.IsEmpty(run.Asked);
        CollectionAssert.Contains(run.Log, "left termhost (pid 900) open: SteppedOut was started from it");
    }

    [TestMethod]
    public void RunThroughAHostProgramEveryCopyOfThatHostIsLeftAlone()
    {
        // As when SteppedOut runs as a .dll through the shared runtime's host.
        using var run = new Run();
        run.Api.Own.Exe = "hostling.exe";
        run.Api.Own.Path = @"C:\Runtime\hostling.exe";
        run.Api.OwnPath = @"C:\Runtime\hostling.exe";
        run.Api.Add(2000, "hostling.exe", path: @"C:\Runtime\hostling.exe");

        Wait(run.Close("hostling").Close(CancellationToken.None));

        Assert.IsEmpty(run.Asked);
        CollectionAssert.Contains(run.Log, "left hostling (pid 2000) open: it is SteppedOut");
    }

    [TestMethod]
    public void AnAppThatRunsAsAdministratorIsLeftOpenWithoutAWaitOrAForce()
    {
        using var run = new Run();
        run.Api.Add(2000, "inkwell.exe").OpenError = 5;
        run.Api.Add(2001, "quillpad.exe").OpenError = 31;

        var done = run.Close("inkwell", "quillpad").Close(CancellationToken.None);

        Assert.IsTrue(done.IsCompleted, "nothing to wait for");
        Assert.AreEqual(0, run.Time.Pending);
        Assert.IsEmpty(run.Rec.Events);
        CollectionAssert.Contains(run.Log, "left inkwell (pid 2000) open: Windows denied access to it (it may run as administrator)", "access denied says what is known");
        CollectionAssert.Contains(run.Log, "left quillpad (pid 2001) open: Windows would not give access to it, error 31");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void AnAppThatRunsAsAdministratorIsLeftOpenAtOnceEvenWhenWindowsGivesAHandle(bool whenAway)
    {
        // SteppedOut runs as administrator too, so opening works; the token still says no.
        using var run = new Run();
        run.Api.Add(2000, "inkwell.exe");
        run.Api.Add(2001, "quillpad.exe");
        var api = new Tweaked(run.Api);
        api.Elevation[2000] = true;
        api.Elevation[2001] = null;

        var done = run.On(whenAway, api, "inkwell", "quillpad").Close(CancellationToken.None);

        Assert.IsTrue(done.IsCompleted, "nothing to wait for");
        Assert.AreEqual(0, run.Time.Pending);
        Assert.IsEmpty(run.Rec.Events, "not asked and not forced");
        CollectionAssert.Contains(run.Log, "left inkwell (pid 2000) open: it runs as administrator");
        CollectionAssert.Contains(run.Log, "left quillpad (pid 2001) open: could not tell whether it runs as administrator");
        Assert.AreEqual(2, run.Api.Opened);
        Assert.AreEqual(run.Api.Opened, run.Api.Disposed, "every handle given back");
    }

    [TestMethod]
    public void AnAppThatRunsAsAdministratorDoesNotHoldUpTheOthers()
    {
        using var run = new Run();
        var admin = run.Api.Add(2000, "inkwell.exe");
        run.Api.Add(2001, "quillpad.exe");
        var api = new Tweaked(run.Api);
        api.Elevation[2000] = true;

        var done = run.Through(api, "inkwell", "quillpad").Close(CancellationToken.None);
        run.Time.Advance(TimeSpan.FromSeconds(30));
        Wait(done);

        CollectionAssert.AreEqual(new[] { "ask quillpad.exe", "force quillpad.exe" }, run.Rec.Events);
        Assert.IsFalse(admin.Exited);
        CollectionAssert.Contains(run.Log, "left inkwell (pid 2000) open: it runs as administrator");
    }

    // The audit's other case (D53): a listed program that does not close in time, say a browser whose
    // page asks "Leave site?" with nobody there, is forced at 30 s. What it started is a program of its
    // own: a document opened from its downloads, with a window, a helper with none, one that runs as
    // administrator, and what those started. None of them is opened, asked or forced, on either side.
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void WhatAListedProgramStartedIsNeverAskedOrForcedEvenWhenTheProgramItselfIs(bool whenAway)
    {
        using var run = new Run();
        var browser = run.Api.Add(2000, "browsr.exe", start: 1_000, windows: 1);
        var document = run.Api.Add(2100, "wordpro.exe", parent: 2000, start: 1_500, windows: 1);
        var helper = run.Api.Add(2101, "inkhelper.exe", parent: 2000, start: 1_500, windows: 0);
        var updater = run.Api.Add(2102, "inkupdater.exe", parent: 2000, start: 1_500, windows: 0);
        var grandchild = run.Api.Add(2200, "inkrender.exe", parent: 2100, start: 1_600, windows: 1);
        var api = new Tweaked(run.Api);
        api.Elevation[2102] = true;

        var done = run.On(whenAway, api, "browsr").Close(CancellationToken.None);
        run.Time.Advance(TimeSpan.FromSeconds(29));
        CollectionAssert.AreEqual(new[] { "ask browsr.exe" }, run.Rec.Events, "the ask: only the listed program's own window");
        run.Time.Advance(TimeSpan.FromSeconds(1));
        Wait(done);

        Assert.IsTrue(browser.Exited, "the listed program is forced");
        CollectionAssert.AreEqual(new[] { "ask browsr.exe", "force browsr.exe" }, run.Rec.Events);
        Assert.IsFalse(document.Exited || helper.Exited || updater.Exited || grandchild.Exited, string.Join(" | ", run.Log));
        Assert.AreEqual(1, run.Api.Opened, "only the listed program was opened");
        Assert.AreEqual(1, run.Api.Snapshots, "the force takes no second look for what it started");
        CollectionAssert.Contains(run.Log, "closed browsr (pid 2000) by force: still running after 30 s");
        Assert.IsFalse(run.Log.Any(line => line.Contains("wordpro") || line.Contains("inkhelper") || line.Contains("inkupdater") || line.Contains("inkrender")), string.Join(" | ", run.Log));
        Assert.AreEqual(run.Api.Opened, run.Api.Disposed, "every handle given back");
    }

    // The ask step on its own: a listed program that closes when asked leaves what it started running.
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void AListedProgramThatClosesWhenAskedLeavesWhatItStartedRunning(bool whenAway)
    {
        using var run = new Run();
        var launcher = run.Api.Add(2000, "launchr.exe", start: 1_000, windows: 1);
        launcher.ClosesWhenAsked = true;
        var game = run.Api.Add(2100, "gamex.exe", parent: 2000, start: 1_500, windows: 1);

        var done = run.On(whenAway, "launchr").Close(CancellationToken.None);
        Wait(done, "over without the clock moving");
        run.Time.Advance(TimeSpan.FromMinutes(1));

        Assert.IsTrue(launcher.Exited);
        Assert.IsFalse(game.Exited);
        CollectionAssert.AreEqual(new[] { "ask launchr.exe" }, run.Rec.Events);
        Assert.DoesNotContain(game.Windows[0], run.Api.Asked, "the game's window was never asked");
        CollectionAssert.Contains(run.Log, "launchr (pid 2000) closed");
    }

    // A listed program runs as several processes: each window process is asked, the ones with no
    // window are told they are forced in 30 s, and the ones still running then are forced, one by one.
    // A copy that closed when asked is not, and a program of another name it started is never touched.
    // The log has one line for each program and what happened to it, not one for each process.
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void EveryProcessOfAListedProgramIsAskedAndOnlyThoseStillRunningAreForced(bool whenAway)
    {
        using var run = new Run();
        var main = run.Api.Add(2000, "browsr.exe", start: 1_000, windows: 2);
        var renderer = run.Api.Add(2001, "browsr.exe", parent: 2000, start: 1_100, windows: 0);
        var gpu = run.Api.Add(2002, "browsr.exe", parent: 2000, start: 1_100, windows: 0);
        var second = run.Api.Add(2003, "browsr.exe", parent: 0, start: 1_200, windows: 1);
        second.ClosesWhenAsked = true;
        var download = run.Api.Add(2100, "wordpro.exe", parent: 2000, start: 1_500, windows: 1);
        var bystander = run.Api.Add(3000, "inkwell.exe", windows: 1);

        var done = run.On(whenAway, "browsr").Close(CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "ask browsr.exe", "ask browsr.exe", "ask browsr.exe" }, run.Rec.Events, "both windows of the first, the one of the second");
        run.Time.Advance(TimeSpan.FromSeconds(30));
        Wait(done);

        CollectionAssert.AreEqual(new[] { "ask browsr.exe", "ask browsr.exe", "ask browsr.exe", "force browsr.exe", "force browsr.exe", "force browsr.exe" }, run.Rec.Events);
        Assert.IsTrue(main.Exited && renderer.Exited && gpu.Exited && second.Exited);
        Assert.IsFalse(download.Exited || bystander.Exited);
        CollectionAssert.AreEqual(new[]
        {
            "asked browsr (pid 2000, 2003) to close: 3 windows",
            $"2 processes of browsr (pid 2001, 2002) have no window to ask; they are closed by force in 30 s unless {(whenAway ? "you are back" : "you leave")} first",
            "browsr (pid 2003) closed",
            "closed browsr (pid 2000, 2001, 2002) by force: still running after 30 s",
        }, run.Log);
        Assert.AreEqual(4, run.Api.Opened, "the four processes of the listed program, and nothing else");
        Assert.AreEqual(run.Api.Opened, run.Api.Disposed, "every handle given back");
    }

    [TestMethod]
    public void ANumberAnotherProgramGotBeforeTheHandleWasOpenedIsLeftAlone()
    {
        using var run = new Run();
        run.Api.Add(2000, "inkwell.exe", path: @"C:\Apps\bystander.exe");

        var done = run.Close("inkwell").Close(CancellationToken.None);

        Assert.IsTrue(done.IsCompleted);
        Assert.IsEmpty(run.Rec.Events);
        CollectionAssert.Contains(run.Log, "left inkwell (pid 2000) open: it is no longer the same program");
        Assert.AreEqual(run.Api.Opened, run.Api.Disposed, "every handle given back");
    }

    [TestMethod]
    public void EveryWindowIsAskedFirstAndOnlyWhatIsStillRunningAfterThirtySecondsIsForced()
    {
        using var run = new Run();
        var stubborn = run.Api.Add(2000, "inkwell.exe", windows: 3);
        run.Api.Add(2001, "quillpad.exe", windows: 2).ClosesWhenAsked = true;

        var done = run.Close("inkwell", "quillpad").Close(CancellationToken.None);
        Assert.AreEqual(1, run.Time.Pending);
        run.Time.Advance(TimeSpan.FromSeconds(29.9));
        Assert.IsFalse(stubborn.Exited);
        run.Time.Advance(TimeSpan.FromSeconds(0.1));
        Wait(done);

        CollectionAssert.AreEqual(new[] { "ask inkwell.exe", "ask inkwell.exe", "ask inkwell.exe", "ask quillpad.exe", "ask quillpad.exe", "force inkwell.exe" }, run.Rec.Events);
        CollectionAssert.Contains(run.Log, "asked inkwell (pid 2000) to close: 3 windows");
        CollectionAssert.Contains(run.Log, "quillpad (pid 2001) closed");
        CollectionAssert.Contains(run.Log, "closed inkwell (pid 2000) by force: still running after 30 s");
        Assert.AreEqual(run.Api.Opened, run.Api.Disposed, "every handle given back");
    }

    [TestMethod]
    public void AnAppThatCannotBeAskedStillGetsTheWaitAndTheForceAndTheNextAppIsStillAsked()
    {
        using var run = new Run();
        var broken = run.Api.Add(2000, "inkwell.exe");
        broken.AskFails = true;
        run.Api.Add(2001, "quillpad.exe").ClosesWhenAsked = true;

        var done = run.Close("inkwell", "quillpad").Close(CancellationToken.None);
        Assert.AreEqual(1, run.Time.Pending, "the wait started");
        run.Time.Advance(TimeSpan.FromSeconds(30));
        Wait(done);

        CollectionAssert.AreEqual(new[] { "ask inkwell.exe", "ask quillpad.exe", "force inkwell.exe" }, run.Rec.Events);
        CollectionAssert.Contains(run.Log, "could not ask inkwell (pid 2000) to close: the window is gone");
        CollectionAssert.Contains(run.Log, "quillpad (pid 2001) closed");
        CollectionAssert.Contains(run.Log, "closed inkwell (pid 2000) by force: still running after 30 s");
        Assert.AreEqual(run.Api.Opened, run.Api.Disposed, "every handle given back");
    }

    [TestMethod]
    public void AnAppThatClosesWhenAskedEndsTheWaitWithoutAForce()
    {
        using var run = new Run();
        run.Api.Add(2000, "inkwell.exe").ClosesWhenAsked = true;

        var done = run.Close("inkwell").Close(CancellationToken.None);

        Wait(done, "over without the clock moving");
        Assert.IsEmpty(run.Forced);
        Assert.AreEqual(0, run.Time.Pending, "its timer is gone");
    }

    [TestMethod]
    public void AnAppWithNoWindowGetsTheWaitAndThenTheForce()
    {
        using var run = new Run();
        var tray = run.Api.Add(2000, "trayling.exe", windows: 0);

        var done = run.Close("trayling").Close(CancellationToken.None);
        run.Time.Advance(TimeSpan.FromSeconds(30));
        Wait(done);

        Assert.IsTrue(tray.Exited);
        CollectionAssert.AreEqual(new[] { "force trayling.exe" }, run.Rec.Events);
        CollectionAssert.Contains(run.Log, "trayling (pid 2000) has no window to ask; it is closed by force in 30 s unless you are back first");
    }

    [TestMethod]
    public void CallingItOffBeforeTheThirtySecondsLeavesEveryAppOpen()
    {
        using var run = new Run();
        var app = run.Api.Add(2000, "inkwell.exe");
        using var back = new CancellationTokenSource();

        var done = run.Close("inkwell").Close(back.Token);
        run.Time.Advance(TimeSpan.FromSeconds(10));
        back.Cancel();
        Wait(done);
        run.Time.Advance(TimeSpan.FromMinutes(1));

        Assert.IsFalse(app.Exited);
        Assert.IsEmpty(run.Forced);
        CollectionAssert.Contains(run.Log, "you are back, so inkwell (pid 2000) stays open");
    }

    [TestMethod]
    public void AlreadyCalledOffNothingIsAsked()
    {
        using var run = new Run();
        run.Api.Add(2000, "inkwell.exe");
        using var back = new CancellationTokenSource();
        back.Cancel();

        var done = run.Close("inkwell").Close(back.Token);

        Assert.IsTrue(done.IsCompleted);
        Assert.IsEmpty(run.Rec.Events);
        Assert.AreEqual(0, run.Api.Opened);
    }

    [TestMethod]
    public void AnAppThatExitsJustAsTheForceComesIsReportedClosed()
    {
        using var run = new Run();
        run.Api.Add(2000, "inkwell.exe").ExitsAtTheForce = true;
        run.Api.Add(2001, "quillpad.exe").KillError = 5;

        var done = run.Close("inkwell", "quillpad").Close(CancellationToken.None);
        run.Time.Advance(TimeSpan.FromSeconds(30));
        Wait(done);

        CollectionAssert.Contains(run.Log, "inkwell (pid 2000) closed");
        CollectionAssert.Contains(run.Log, "could not close quillpad (pid 2001) by force, error 5");
    }

    [TestMethod]
    public void ComingBackJustBeforeTheForceLeavesTheAppOpen()
    {
        using var run = new Run();
        var app = run.Api.Add(2000, "inkwell.exe", start: 1_000);
        var helper = run.Api.Add(2100, "inkhelper.exe", parent: 2000, start: 1_100, windows: 0);
        using var back = new CancellationTokenSource();
        // The return lands on the very last look before the force: the second look at whether the
        // app has exited, after the wait and after every other check.
        int checks = 0;
        var api = new Tweaked(run.Api)
        {
            OnExitCheck = () =>
            {
                if (++checks == 2) back.Cancel();
            },
        };

        var done = run.Through(api, "inkwell").Close(back.Token);
        run.Time.Advance(TimeSpan.FromSeconds(30));
        Wait(done);

        Assert.IsTrue(back.IsCancellationRequested, "the return came");
        Assert.IsEmpty(run.Forced);
        Assert.IsFalse(app.Exited);
        Assert.IsFalse(helper.Exited);
        CollectionAssert.Contains(run.Log, "you are back, so inkwell (pid 2000) stays open");
        Assert.AreEqual(run.Api.Opened, run.Api.Disposed, "every handle given back");
    }

    [TestMethod]
    public void ADryRunListsWhatItWouldCloseAndTouchesNothing()
    {
        using var run = new Run();
        var app = run.Api.Add(2000, "inkwell.exe", windows: 2);

        run.Close("inkwell", "explorer").DryRun();
        run.Close("nothingrunning").DryRun();
        run.Time.Advance(TimeSpan.FromMinutes(1));

        Assert.IsEmpty(run.Rec.Events);
        Assert.IsFalse(app.Exited);
        Assert.AreEqual(0, run.Time.Pending);
        CollectionAssert.Contains(run.Log, "WOULD close inkwell (pid 2000), 2 windows (dry run)");
        CollectionAssert.Contains(run.Log, "WOULD close nothing when away: no process of nothingrunning to close (dry run)");
        Assert.AreEqual(run.Api.Opened, run.Api.Disposed);
    }

    [TestMethod]
    public void AStoreAppIsAskedThroughItsFrameAndNothingElseOfTheFrameHost()
    {
        var frame = new AppWindow(new IntPtr(10), 50, AppWindows.FrameClass, "Ledger");
        var otherFrame = new AppWindow(new IntPtr(11), 50, AppWindows.FrameClass, "Sketchbook");
        var hostOwn = new AppWindow(new IntPtr(12), 50, "HostPopup", "");
        var children = new Dictionary<IntPtr, IReadOnlyList<AppWindow>>
        {
            [frame.Handle] = [new AppWindow(new IntPtr(20), 2000, AppWindows.CoreClass, "Ledger")],
            [otherFrame.Handle] = [new AppWindow(new IntPtr(21), 3000, AppWindows.CoreClass, "Sketchbook")],
        };
        AppWindow[] windows = [hostOwn, frame, otherFrame];
        IReadOnlyList<AppWindow> ChildrenOf(IntPtr window) => children.TryGetValue(window, out var list) ? list : [];

        CollectionAssert.AreEqual(new[] { frame.Handle }, AppWindows.ToClose(2000, windows, ChildrenOf));
        CollectionAssert.AreEqual(new[] { otherFrame.Handle }, AppWindows.ToClose(3000, windows, ChildrenOf));
        Assert.IsEmpty(AppWindows.ToClose(4000, windows, ChildrenOf));
    }

    [TestMethod]
    public void AMinimizedStoreAppIsFoundByItsFramesTitleAndItsCoreWindowIsNeverAskedItself()
    {
        var takenOut = new AppWindow(new IntPtr(20), 2000, AppWindows.CoreClass, "Ledger");
        var emptyFrame = new AppWindow(new IntPtr(10), 50, AppWindows.FrameClass, "Ledger");
        var busyFrame = new AppWindow(new IntPtr(11), 50, AppWindows.FrameClass, "Ledger");
        var children = new Dictionary<IntPtr, IReadOnlyList<AppWindow>>
        {
            [busyFrame.Handle] = [new AppWindow(new IntPtr(21), 3000, AppWindows.CoreClass, "Ledger")],
        };
        IReadOnlyList<AppWindow> ChildrenOf(IntPtr window) => children.TryGetValue(window, out var list) ? list : [];

        var toClose = AppWindows.ToClose(2000, [takenOut, emptyFrame, busyFrame], ChildrenOf);

        CollectionAssert.AreEqual(new[] { emptyFrame.Handle }, toClose);
    }

    [TestMethod]
    public void WindowsAreAskedTopFirstSoADialogGoesBeforeItsWindow()
    {
        var dialog = new AppWindow(new IntPtr(30), 2000, "#32770", "Save As");
        var other = new AppWindow(new IntPtr(31), 3000, "Main", "Elsewhere");
        var main = new AppWindow(new IntPtr(32), 2000, "Main", "Notes");
        var tool = new AppWindow(new IntPtr(33), 2000, "Palette", "");

        var toClose = AppWindows.ToClose(2000, [dialog, other, main, tool], _ => []);

        CollectionAssert.AreEqual(new[] { dialog.Handle, main.Handle, tool.Handle }, toClose);
    }

    [TestMethod]
    public void NothingInTheAppKillsAWholeProcessTree()
    {
        string src = Path.Combine(Root, "src");
        var offenders = Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"entireProcessTree|\.Kill\s*\(\s*true\s*\)"))
            .ToList();

        Assert.IsEmpty(offenders, string.Join(", ", offenders));
    }

    // Ending a process is guarded as a lock request and a program start are (SessionLockTests): the
    // one call to Windows' TerminateProcess sits in the real handle's Kill, behind the check of
    // SessionLock.Blocked, every force in the app is Close apps' call on that handle, and no other
    // way of ending a process appears anywhere in the app.
    [TestMethod]
    public void EveryForceInTheAppGoesThroughTheOneGuardedHandle()
    {
        Assert.IsTrue(SessionLock.Blocked, "set for the whole test run");
        string src = Path.Combine(Root, "src");
        var files = Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(src, file).Replace('\\', '/'), File.ReadAllText);

        var terminate = files.SelectMany(file => Regex.Matches(file.Value, @"\bTerminateProcess\s*\(").Select(_ => file.Key)).ToList();
        CollectionAssert.AreEqual(new[] { "Actions/ProcessApi.cs", "Actions/ProcessApi.cs" }, terminate, "declared once and called once");
        string api = files["Actions/ProcessApi.cs"];
        int kill = api.IndexOf("public int Kill()", StringComparison.Ordinal);
        int call = kill < 0 ? -1 : api.IndexOf("TerminateProcess(handle", kill, StringComparison.Ordinal);
        Assert.IsGreaterThan(kill, call, "the call is in the real handle's Kill");
        Assert.IsGreaterThanOrEqualTo(0, kill);
        string before = api[kill..call];
        StringAssert.Contains(before, "SessionLock.Blocked", "the guard comes before the call");
        StringAssert.Contains(before, "throw new InvalidOperationException");
        StringAssert.Contains(api, "static extern bool TerminateProcess(", "the other one is the declaration");

        var calls = files.SelectMany(file => Regex.Matches(file.Value, @"\.Kill\s*\(").Select(_ => file.Key)).ToList();
        CollectionAssert.AreEqual(new[] { "Actions/CloseApps.cs" }, calls, "the one force is Close apps' call on the handle");
        var others = files.Where(file => Regex.IsMatch(file.Value, @"TerminateJobObject|NtTerminateProcess|taskkill|Stop-Process|DebugActiveProcess|\bProcess\.Kill"))
            .Select(file => file.Key).ToList();
        Assert.IsEmpty(others, string.Join(", ", others));

        var handles = typeof(IProcessHandle).Assembly.GetTypes()
            .Where(type => typeof(IProcessHandle).IsAssignableFrom(type) && !type.IsInterface).ToList();
        Assert.HasCount(1, handles, string.Join(", ", handles));
        Assert.AreEqual(typeof(Win32ProcessApi), handles[0].DeclaringType, "the only handle in the app is the real, guarded one");
    }

    static string Root
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "src", "SteppedOut.csproj"))) return dir.FullName;
            }
            throw new InvalidOperationException("repository not found");
        }
    }

    // ---- The real tests: a stub built and started by the test, and nothing else. ----

    [ClassCleanup]
    public static void RemoveTheStub() => Stub.Remove();

    // The stub started a program of its own (another copy, under another GUID name, with two windows
    // that ignore WM_CLOSE). The wrapper lets through only the stub, so any reach for that program
    // would show up as refused.
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ARealStubGetsWmCloseOnBothItsWindowsAndClosesByItselfAndWhatItStartedKeepsRunning(bool whenAway)
    {
        using var stub = Stub.Start(ignoreClose: false, withChild: true);
        var guard = new OnlyTheStub(new Win32ProcessApi(), stub);
        var log = new List<string>();
        var close = new CloseApps([stub.Name], whenAway, guard, TimeProvider.System, TimeSpan.FromSeconds(3), line =>
        {
            lock (log) log.Add(line);
        });

        var done = close.Close(CancellationToken.None);

        Wait(done, "closed within the wait");
        Assert.IsTrue(stub.Process.WaitForExit(10_000), "the stub closed itself");
        Assert.AreEqual(0, stub.Process.ExitCode, "it was not forced");
        CollectionAssert.AreEquivalent(stub.Windows, guard.Asked, "both of its windows were asked, and nothing else");
        Assert.HasCount(2, stub.CloseLines());
        Assert.IsEmpty(guard.Killed);
        Assert.IsEmpty(guard.Refused, string.Join(", ", guard.Refused));
        CollectionAssert.Contains(log, $"asked {stub.Name} (pid {stub.Process.Id}) to close: 2 windows");
        Assert.IsFalse(stub.ChildExited(TimeSpan.FromSeconds(1)), "the program the stub started keeps running");
        Assert.IsEmpty(stub.ChildCloseLines(), "and none of its windows was asked");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ARealStubThatIgnoresWmCloseIsForcedAfterBothWereSentButWhatItStartedKeepsRunning(bool whenAway)
    {
        using var stub = Stub.Start(ignoreClose: true, withChild: true);
        var guard = new OnlyTheStub(new Win32ProcessApi(), stub);
        var log = new List<string>();
        var close = new CloseApps([stub.Name], whenAway, guard, TimeProvider.System, TimeSpan.FromSeconds(3), line =>
        {
            lock (log) log.Add(line);
        });

        var done = close.Close(CancellationToken.None);

        Wait(done, "forced after the wait");
        Assert.IsTrue(stub.Process.WaitForExit(10_000), "the stub was forced closed");
        Assert.AreEqual(1, stub.Process.ExitCode, "TerminateProcess's code, so it was the force");
        CollectionAssert.AreEquivalent(stub.Windows, guard.Asked, "only the stub's two windows were asked");
        CollectionAssert.AreEqual(new[] { stub.Process.Id }, guard.Killed.Select(kill => kill.ProcessId).ToList(), "the stub alone");
        Assert.AreEqual(2, guard.Killed[0].ClosesSeenBefore, "both WM_CLOSEs arrived before the force");
        Assert.IsFalse(stub.ChildExited(TimeSpan.FromSeconds(1)), "the program the stub started keeps running");
        Assert.IsEmpty(stub.ChildCloseLines(), "and none of its windows was asked");
        Assert.IsEmpty(guard.Refused, string.Join(", ", guard.Refused));
        CollectionAssert.Contains(log, $"closed {stub.Name} (pid {stub.Process.Id}) by force: still running after 3 s");
    }

    // Two processes of one listed program: both are asked, the one that closes does, and only the
    // one still running at the end of the wait is forced.
    [TestMethod]
    public void EveryRealProcessOfAListedStubIsAskedAndOnlyTheOneStillRunningIsForced()
    {
        using var first = Stub.Start(ignoreClose: false, withChild: false);
        using var second = Stub.Start(ignoreClose: true, withChild: false, exe: first.Exe);
        Assert.AreEqual(first.Name, second.Name, "precondition: one program, two processes");
        var guard = new OnlyTheStub(new Win32ProcessApi(), first, second);
        var log = new List<string>();
        var close = new CloseApps([first.Name], whenAway: true, guard, TimeProvider.System, TimeSpan.FromSeconds(3), line =>
        {
            lock (log) log.Add(line);
        });

        var done = close.Close(CancellationToken.None);

        Wait(done, "forced after the wait");
        Assert.IsTrue(first.Process.WaitForExit(10_000) && second.Process.WaitForExit(10_000), "both ended");
        Assert.AreEqual(0, first.Process.ExitCode, "the first closed when asked");
        Assert.AreEqual(1, second.Process.ExitCode, "the second was forced");
        CollectionAssert.AreEquivalent(first.Windows.Concat(second.Windows).ToList(), guard.Asked, "the four windows of the two processes");
        CollectionAssert.AreEqual(new[] { second.Process.Id }, guard.Killed.Select(kill => kill.ProcessId).ToList());
        Assert.AreEqual(2, guard.Killed[0].ClosesSeenBefore, "both its WM_CLOSEs arrived before the force");
        Assert.IsEmpty(guard.Refused, string.Join(", ", guard.Refused));
    }

    // While SessionLock.Blocked is set, the real handle ends only a program a test built for itself
    // in its own folder. The same stub copied anywhere else is refused, straight on the handle and
    // through Close apps with the wrapper letting it through, and keeps running.
    [TestMethod]
    public void WhileBlockedTheRealForceEndsNothingButATestsOwnProgram()
    {
        Assert.IsTrue(SessionLock.Blocked, "set for the whole test run");
        using var stub = Stub.Start(ignoreClose: true, withChild: false, exe: Stub.CopyElsewhere());
        Assert.IsFalse(ProcessStarter.MadeForATest(stub.Exe), "precondition: this copy is not kept where a test's own program is");
        var api = new Win32ProcessApi();
        using (var handle = api.Open(stub.Process.Id, out int error))
        {
            Assert.IsNotNull(handle, $"error {error}");
            var refused = Assert.ThrowsExactly<InvalidOperationException>(() => handle.Kill());
            StringAssert.Contains(refused.Message, "blocked");
        }

        var guard = new OnlyTheStub(api, stub);
        var log = new List<string>();
        var close = new CloseApps([stub.Name], whenAway: true, guard, TimeProvider.System, TimeSpan.FromSeconds(1), line =>
        {
            lock (log) log.Add(line);
        });
        Wait(close.Close(CancellationToken.None), "over after the wait");

        CollectionAssert.AreEqual(new[] { stub.Process.Id }, guard.Killed.Select(kill => kill.ProcessId).ToList(), "the wrapper passed the force on");
        CollectionAssert.Contains(log, "could not finish closing apps: Ending programs is blocked in this process.");
        Assert.IsFalse(stub.Process.WaitForExit(500), "the program is still running");
    }

    // The same for the close request: while SessionLock.Blocked is set, the real WM_CLOSE reaches
    // only a window of a program a test built for itself. A stub that closes when asked, copied
    // anywhere else, is refused, gets no WM_CLOSE and keeps running.
    [TestMethod]
    public void WhileBlockedTheRealCloseRequestReachesNothingButATestsOwnProgram()
    {
        Assert.IsTrue(SessionLock.Blocked, "set for the whole test run");
        using var stub = Stub.Start(ignoreClose: false, withChild: false, exe: Stub.CopyElsewhere());
        Assert.IsFalse(ProcessStarter.MadeForATest(stub.Exe), "precondition: this copy is not kept where a test's own program is");
        var api = new Win32ProcessApi();

        foreach (var window in stub.Windows)
        {
            var refused = Assert.ThrowsExactly<InvalidOperationException>(() => api.PostClose(window));
            StringAssert.Contains(refused.Message, "blocked");
        }

        Assert.IsFalse(stub.Process.WaitForExit(1000), "the program is still running");
        Assert.IsEmpty(stub.CloseLines(), "and none of its windows was asked");
    }

    [TestMethod]
    public void TheRealHandleReadsWhetherAProcessRunsAsAdministratorFromItsToken()
    {
        // This test's own process, opened only to be read; it is never asked or forced.
        var api = new Win32ProcessApi();
        using var handle = api.Open(Environment.ProcessId, out int error);
        Assert.IsNotNull(handle, $"error {error}");
        Assert.AreEqual(Environment.IsPrivilegedProcess, handle.Elevated);
    }

    [TestMethod]
    public void OpenAppsStartsAProgramByItsFullPath()
    {
        string exe = Stub.CopyForAShellStart(out string ready);
        var log = new List<string>();
        using (Stub.UseThisDotnetForShellStarts())
        {
            // From a thread in the multithreaded apartment, like the away actions' own.
            var opener = new Thread(() => new OpenApps([exe], whenAway: true, new ProcessStarter(), log.Add).Open(CancellationToken.None));
            opener.SetApartmentState(ApartmentState.MTA);
            opener.Start();
            Assert.IsTrue(opener.Join(TimeSpan.FromSeconds(20)), "the shell start returned");
            Assert.IsTrue(SpinWait.SpinUntil(() => File.Exists(ready), TimeSpan.FromSeconds(20)), "the program started: " + string.Join(" | ", log));
        }
        CollectionAssert.Contains(log, "opened " + exe);
        var parts = File.ReadAllText(ready).Split(' ');
        int pid = int.Parse(parts[0]);
        var api = new Win32ProcessApi();
        using var handle = api.Open(pid, out int error);
        Assert.IsNotNull(handle, $"error {error}");
        Assert.AreEqual(exe, handle.ImagePath, true, "the process is the copy this test made");
        try
        {
            // Its own two windows, which it closes when asked. A number that now names a window of
            // any other process is never sent anything.
            foreach (string text in parts[1..3])
            {
                var window = new IntPtr(long.Parse(text));
                Native.GetWindowThreadProcessId(window, out uint owner);
                Assert.AreEqual((uint)pid, owner, "the window still belongs to the stub");
                Native.PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
            }
            Assert.IsTrue(handle.WhenExited.Wait(TimeSpan.FromSeconds(10)), "it closed when asked");
        }
        finally
        {
            if (!handle.HasExited) handle.Kill();
        }
    }

    // A wrapper round the made-up process list that can make a process run as administrator, or run
    // something at the moment a handle is asked whether its process has exited.
    sealed class Tweaked(AwayFakes.FakeProcesses fakes) : IProcessApi
    {
        // A process listed here reports this; any other is not elevated.
        public Dictionary<int, bool?> Elevation { get; } = new();

        public Action? OnExitCheck { get; init; }

        public int OwnId => fakes.OwnId;

        public string? OwnPath => fakes.OwnPath;

        public IReadOnlyList<ProcessEntry> Snapshot() => fakes.Snapshot();

        public int? SessionOf(int processId) => fakes.SessionOf(processId);

        public long? StartedAt(int processId) => fakes.StartedAt(processId);

        public IReadOnlyList<AppWindow> Windows() => fakes.Windows();

        public IReadOnlyList<AppWindow> ChildrenOf(IntPtr window) => fakes.ChildrenOf(window);

        public bool PostClose(IntPtr window) => fakes.PostClose(window);

        public IProcessHandle? Open(int processId, out int error)
        {
            var handle = fakes.Open(processId, out error);
            if (handle is null) return null;
            return new Handle(this, handle, Elevation.TryGetValue(processId, out bool? elevated) ? elevated : false);
        }

        sealed class Handle(Tweaked api, IProcessHandle handle, bool? elevated) : IProcessHandle
        {
            public int Id => handle.Id;

            public string? ImagePath => handle.ImagePath;

            public long? StartedAt => handle.StartedAt;

            public bool? Elevated => elevated;

            public bool HasExited
            {
                get
                {
                    api.OnExitCheck?.Invoke();
                    return handle.HasExited;
                }
            }

            public Task WhenExited => handle.WhenExited;

            public int Kill() => handle.Kill();

            public void Dispose() => handle.Dispose();
        }
    }

    // A wrapper round the real process API that lets through only the stubs this test started: every
    // other process, the programs the stubs started included, is refused and noted, never opened,
    // asked or forced.
    sealed class OnlyTheStub(IProcessApi real, params Stub.Running[] stubs) : IProcessApi
    {
        readonly object gate = new();

        public List<IntPtr> Asked { get; } = new();

        public List<(int ProcessId, int ClosesSeenBefore)> Killed { get; } = new();

        public List<string> Refused { get; } = new();

        bool Allowed(int processId) => stubs.Any(stub => stub.Process.Id == processId);

        public int OwnId => real.OwnId;

        public string? OwnPath => real.OwnPath;

        public IReadOnlyList<ProcessEntry> Snapshot() => real.Snapshot();

        public int? SessionOf(int processId) => real.SessionOf(processId);

        public long? StartedAt(int processId) => real.StartedAt(processId);

        public IReadOnlyList<AppWindow> Windows() => real.Windows();

        public IReadOnlyList<AppWindow> ChildrenOf(IntPtr window) => real.ChildrenOf(window);

        public IProcessHandle? Open(int processId, out int error)
        {
            if (!Allowed(processId))
            {
                lock (gate) Refused.Add($"open {processId}");
                error = 5;
                return null;
            }
            var handle = real.Open(processId, out error);
            return handle is null ? null : new Guarded(this, handle);
        }

        public bool PostClose(IntPtr window)
        {
            Native.GetWindowThreadProcessId(window, out uint owner);
            if (!Allowed((int)owner))
            {
                lock (gate) Refused.Add($"ask window {window} of {owner}");
                return false;
            }
            lock (gate) Asked.Add(window);
            return real.PostClose(window);
        }

        sealed class Guarded(OnlyTheStub guard, IProcessHandle handle) : IProcessHandle
        {
            public int Id => handle.Id;

            public string? ImagePath => handle.ImagePath;

            public long? StartedAt => handle.StartedAt;

            public bool? Elevated => handle.Elevated;

            public bool HasExited => handle.HasExited;

            public Task WhenExited => handle.WhenExited;

            public int Kill()
            {
                if (!guard.Allowed(handle.Id))
                {
                    lock (guard.gate) guard.Refused.Add($"force {handle.Id}");
                    return 5;
                }
                lock (guard.gate) guard.Killed.Add((handle.Id, guard.StubCloses(handle.Id)));
                return handle.Kill();
            }

            public void Dispose() => handle.Dispose();
        }

        int StubCloses(int processId) => stubs.Single(stub => stub.Process.Id == processId).CloseLines().Count;
    }

    // Builds the stub once per test run into a new folder in the temp folder, and starts copies of it,
    // each under a new GUID name. It opens two ordinary top-level windows far off screen that never
    // take the keyboard, writes each WM_CLOSE it gets to a file, and exits once its windows are gone,
    // after a minute, or when the test run ends.
    static class Stub
    {
        static readonly object gate = new();
        static string? folder;
        static string? built;
        static string? problem;

        public sealed class Running : IDisposable
        {
            public required string Name { get; init; }

            public required string Exe { get; init; }

            public required Process Process { get; init; }

            public required IntPtr[] Windows { get; init; }

            public required int ChildId { get; init; }

            public required string ChildName { get; init; }

            public required string LogPath { get; init; }

            public List<string> CloseLines() => CloseLinesIn(LogPath);

            // The WM_CLOSEs the program the stub started got.
            public List<string> ChildCloseLines() => CloseLinesIn(LogPath + ".child");

            static List<string> CloseLinesIn(string path)
            {
                try
                {
                    return File.Exists(path) ? File.ReadAllLines(path).Where(line => line.StartsWith("close", StringComparison.Ordinal)).ToList() : [];
                }
                catch (IOException)
                {
                    return [];
                }
            }

            public bool ChildExited(TimeSpan within)
            {
                var api = new Win32ProcessApi();
                using var handle = api.Open(ChildId, out _);
                if (handle is null) return true;
                if (!string.Equals(Path.GetFileNameWithoutExtension(handle.ImagePath), ChildName, StringComparison.OrdinalIgnoreCase)) return true;
                return handle.WhenExited.Wait(within);
            }

            // Only ever through the stub's own handle, and the child's by its GUID name.
            public void Dispose()
            {
                try
                {
                    if (!Process.HasExited) Process.Kill();
                }
                catch (InvalidOperationException)
                {
                }
                Process.Dispose();
                if (ChildId == 0) return;
                var api = new Win32ProcessApi();
                using var child = api.Open(ChildId, out _);
                if (child is not null && string.Equals(Path.GetFileNameWithoutExtension(child.ImagePath), ChildName, StringComparison.OrdinalIgnoreCase) && !child.HasExited) child.Kill();
            }
        }

        // A new copy, or another process of a copy already running (exe), with its own files.
        public static Running Start(bool ignoreClose, bool withChild, string? exe = null)
        {
            // The stub would run as administrator too, and closing apps rightly leaves it open.
            if (Environment.IsPrivilegedProcess) Assert.Inconclusive("the tests run as administrator, so the stub would too; nothing was started");
            exe ??= Copy();
            string name = Path.GetFileNameWithoutExtension(exe);
            string files = exe + "." + Guid.NewGuid().ToString("N");
            string ready = files + ".ready";
            string log = files + ".log";
            string child = withChild ? Copy() : "";
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(ready);
            start.ArgumentList.Add(log);
            start.ArgumentList.Add(ignoreClose ? "ignore" : "close");
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            if (withChild) start.ArgumentList.Add(child);
            start.Environment["DOTNET_ROOT"] = DotnetRoot;
            var process = Process.Start(start) ?? throw new InvalidOperationException("the stub did not start");
            try
            {
                if (!SpinWait.SpinUntil(() => File.Exists(ready) || process.HasExited, TimeSpan.FromSeconds(20)) || process.HasExited)
                    Assert.Inconclusive("the stub did not come up on this machine; nothing else was touched");
                var parts = File.ReadAllText(ready).Split(' ');
                return new Running
                {
                    Name = name,
                    Exe = exe,
                    Process = process,
                    Windows = [new IntPtr(long.Parse(parts[1])), new IntPtr(long.Parse(parts[2]))],
                    ChildId = int.Parse(parts[3]),
                    ChildName = withChild ? Path.GetFileNameWithoutExtension(child) : "",
                    LogPath = log,
                };
            }
            catch
            {
                if (!process.HasExited) process.Kill();
                process.Dispose();
                throw;
            }
        }

        // A copy for Open apps, which starts it the way Explorer would, with no arguments.
        public static string CopyForAShellStart(out string ready)
        {
            string exe = Copy();
            ready = exe + ".ready";
            return exe;
        }

        // A start through the shell takes its environment from this process, so DOTNET_ROOT is set
        // here only for the start and put back as it was when the result is disposed.
        public static IDisposable UseThisDotnetForShellStarts()
        {
            string? before = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrEmpty(before)) return new Restore(before, set: false);
            Environment.SetEnvironmentVariable("DOTNET_ROOT", DotnetRoot);
            return new Restore(before, set: true);
        }

        sealed class Restore(string? before, bool set) : IDisposable
        {
            public void Dispose()
            {
                if (set) Environment.SetEnvironmentVariable("DOTNET_ROOT", before);
            }
        }

        // Each copy runs the same built program under its own GUID name.
        static string Copy()
        {
            string exe = Built();
            string copy = Path.Combine(Path.GetDirectoryName(exe)!, "so" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(exe, copy);
            return copy;
        }

        // A copy that runs, but not from where a test keeps its own program (a folder beside "out"
        // in the stub's folder), with the files the program needs next to it.
        public static string CopyElsewhere()
        {
            string exe = Built();
            string output = Path.GetDirectoryName(exe)!;
            string elsewhere = Path.Combine(Path.GetDirectoryName(output)!, "elsewhere");
            lock (gate)
            {
                Directory.CreateDirectory(elsewhere);
                foreach (string file in Directory.GetFiles(output, Path.GetFileNameWithoutExtension(exe) + ".*"))
                {
                    string target = Path.Combine(elsewhere, Path.GetFileName(file));
                    if (!File.Exists(target)) File.Copy(file, target);
                }
            }
            string copy = Path.Combine(elsewhere, "so" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(exe, copy);
            return copy;
        }

        static string DotnetRoot => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", ".."));

        static string Built()
        {
            lock (gate)
            {
                if (built is not null) return built;
                if (problem is not null) Assert.Inconclusive(problem);
                try
                {
                    built = Build();
                    return built;
                }
                catch (Exception e)
                {
                    problem = "the stub could not be built here, so the real close test did not run; nothing was touched: " + e.Message;
                    Assert.Inconclusive(problem);
                    throw;
                }
            }
        }

        static string Build()
        {
            folder = Path.Combine(Path.GetTempPath(), "so" + Guid.NewGuid().ToString("N"));
            string src = Path.Combine(folder, "src");
            string output = Path.Combine(folder, "out");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "stub.csproj"), Project);
            File.WriteAllText(Path.Combine(src, "Stub.cs"), Source);
            string assembly = "so" + Guid.NewGuid().ToString("N");
            string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host) ? host : Path.Combine(DotnetRoot, "dotnet.exe");
            var start = new ProcessStartInfo(dotnet)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = src,
            };
            foreach (string argument in new[]
            {
                "build", Path.Combine(src, "stub.csproj"), "-c", "Release", "-o", output, "-p:AssemblyName=" + assembly,
                "-nodeReuse:false", "--disable-build-servers", "-p:UseSharedCompilation=false",
                "-p:ImportDirectoryBuildProps=false", "-p:ImportDirectoryBuildTargets=false", "-p:ImportDirectoryPackagesProps=false",
                "--nologo", "-v", "q",
            }) start.ArgumentList.Add(argument);
            // The test run's own build settings must not leak into this one.
            foreach (string key in start.Environment.Keys.ToList())
            {
                if (key.StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase) || key.StartsWith("VSTEST", StringComparison.OrdinalIgnoreCase) || key.StartsWith("TESTINGPLATFORM", StringComparison.OrdinalIgnoreCase))
                    start.Environment.Remove(key);
            }
            start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            start.Environment["DOTNET_NOLOGO"] = "1";
            start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
            using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(180_000))
            {
                process.Kill();
                throw new TimeoutException("the build took over 3 minutes");
            }
            string exe = Path.Combine(output, assembly + ".exe");
            if (process.ExitCode != 0 || !File.Exists(exe))
            {
                string text = (stdout.Result + stderr.Result).Trim();
                throw new InvalidOperationException($"exit {process.ExitCode}: {text[Math.Max(0, text.Length - 600)..]}");
            }
            return exe;
        }

        public static void Remove()
        {
            lock (gate)
            {
                if (folder is null) return;
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    try
                    {
                        Directory.Delete(folder, recursive: true);
                        return;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        Thread.Sleep(200);
                    }
                }
            }
        }

        const string Project = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>disable</Nullable>
                <ImplicitUsings>disable</ImplicitUsings>
                <InvariantGlobalization>true</InvariantGlobalization>
                <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
                <GenerateDocumentationFile>false</GenerateDocumentationFile>
              </PropertyGroup>
            </Project>
            """;

        const string Source = """
            using System;
            using System.Diagnostics;
            using System.IO;
            using System.Runtime.InteropServices;
            using System.Threading;

            // A stand-in app for the close-apps test: two windows, a note of each WM_CLOSE, nothing else.
            static class Stub
            {
                const uint WmClose = 0x0010;
                const uint WmDestroy = 0x0002;
                const uint WsOverlappedWindow = 0x00CF0000;
                const uint WsExNoActivate = 0x08000000;
                const int SwShowNoActivate = 4;

                delegate IntPtr WndProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

                [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
                struct WNDCLASSEX
                {
                    public uint cbSize;
                    public uint style;
                    public IntPtr lpfnWndProc;
                    public int cbClsExtra;
                    public int cbWndExtra;
                    public IntPtr hInstance;
                    public IntPtr hIcon;
                    public IntPtr hCursor;
                    public IntPtr hbrBackground;
                    public string lpszMenuName;
                    public string lpszClassName;
                    public IntPtr hIconSm;
                }

                [StructLayout(LayoutKind.Sequential)]
                struct MSG
                {
                    public IntPtr hwnd;
                    public uint message;
                    public IntPtr wParam;
                    public IntPtr lParam;
                    public uint time;
                    public int x;
                    public int y;
                    public uint lPrivate;
                }

                static readonly WndProc proc = Handle;
                static string log;
                static bool ignoreClose;
                static int open;

                // ready-file log-file close|ignore watch-pid [child-exe]; with no arguments, the files sit
                // next to the program and it closes when asked.
                static int Main(string[] args)
                {
                    string self = Environment.ProcessPath;
                    string ready = args.Length > 0 ? args[0] : self + ".ready";
                    log = args.Length > 1 ? args[1] : self + ".log";
                    ignoreClose = args.Length > 2 && args[2] == "ignore";
                    int watch = args.Length > 3 ? int.Parse(args[3]) : 0;
                    string child = args.Length > 4 ? args[4] : null;
                    new Thread(() => Watch(watch)) { IsBackground = true }.Start();

                    var cls = new WNDCLASSEX
                    {
                        cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                        lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
                        hInstance = GetModuleHandle(null),
                        lpszClassName = "Stub" + Environment.ProcessId,
                    };
                    if (RegisterClassEx(ref cls) == 0) return 2;
                    var windows = new IntPtr[2];
                    for (int i = 0; i < 2; i++)
                    {
                        windows[i] = CreateWindowEx(WsExNoActivate, cls.lpszClassName, "stub " + i, WsOverlappedWindow,
                            -20000 - 300 * i, -20000, 200, 100, IntPtr.Zero, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
                        if (windows[i] == IntPtr.Zero) return 3;
                        ShowWindow(windows[i], SwShowNoActivate);
                        open++;
                    }
                    int childId = 0;
                    if (child != null)
                    {
                        var start = new ProcessStartInfo(child) { UseShellExecute = false, CreateNoWindow = true };
                        start.ArgumentList.Add(ready + ".child");
                        start.ArgumentList.Add(log + ".child");
                        start.ArgumentList.Add("ignore");
                        start.ArgumentList.Add(watch.ToString());
                        using (var started = Process.Start(start)) childId = started.Id;
                        var until = DateTime.UtcNow.AddSeconds(20);
                        while (!File.Exists(ready + ".child") && DateTime.UtcNow < until) Thread.Sleep(20);
                    }
                    File.WriteAllText(ready + ".tmp", Environment.ProcessId + " " + windows[0].ToInt64() + " " + windows[1].ToInt64() + " " + childId);
                    File.Move(ready + ".tmp", ready);
                    while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
                    {
                        TranslateMessage(ref msg);
                        DispatchMessage(ref msg);
                    }
                    return 0;
                }

                // Never outlives the test run, and never runs for more than a minute.
                static void Watch(int host)
                {
                    try
                    {
                        if (host > 0)
                        {
                            using var process = Process.GetProcessById(host);
                            process.WaitForExit(60_000);
                        }
                        else
                        {
                            Thread.Sleep(60_000);
                        }
                    }
                    catch (ArgumentException)
                    {
                    }
                    Environment.Exit(4);
                }

                static IntPtr Handle(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
                {
                    if (message == WmClose)
                    {
                        File.AppendAllText(log, "close " + window.ToInt64() + Environment.NewLine);
                        if (!ignoreClose) DestroyWindow(window);
                        return IntPtr.Zero;
                    }
                    if (message == WmDestroy)
                    {
                        if (--open == 0) PostQuitMessage(0);
                        return IntPtr.Zero;
                    }
                    return DefWindowProc(window, message, wParam, lParam);
                }

                [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
                static extern IntPtr GetModuleHandle(string name);

                [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
                static extern ushort RegisterClassEx(ref WNDCLASSEX cls);

                [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
                static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

                [DllImport("user32.dll")]
                static extern bool ShowWindow(IntPtr window, int show);

                [DllImport("user32.dll")]
                static extern bool DestroyWindow(IntPtr window);

                [DllImport("user32.dll")]
                static extern void PostQuitMessage(int code);

                [DllImport("user32.dll", CharSet = CharSet.Unicode)]
                static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

                [DllImport("user32.dll", CharSet = CharSet.Unicode)]
                static extern int GetMessage(out MSG msg, IntPtr window, uint min, uint max);

                [DllImport("user32.dll")]
                static extern bool TranslateMessage(ref MSG msg);

                [DllImport("user32.dll", CharSet = CharSet.Unicode)]
                static extern IntPtr DispatchMessage(ref MSG msg);
            }
            """;
    }
}
