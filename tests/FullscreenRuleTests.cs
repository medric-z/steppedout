namespace SteppedOut.Tests;

[TestClass]
public class FullscreenRuleTests
{
    static Native.RECT Rect(int left, int top, int right, int bottom) => new() { Left = left, Top = top, Right = right, Bottom = bottom };

    static readonly Native.RECT Monitor = Rect(0, 0, 1920, 1080);

    [TestMethod]
    public void ExactMonitorBoundsCount()
    {
        Assert.IsTrue(FullscreenRule.Covers(Rect(0, 0, 1920, 1080), Monitor));
    }

    [TestMethod]
    public void OverhangingWindowCounts()
    {
        Assert.IsTrue(FullscreenRule.Covers(Rect(-8, -8, 1928, 1088), Monitor));
    }

    [TestMethod]
    public void MaximizedWindowAboveTheTaskbarDoesNotCount()
    {
        Assert.IsFalse(FullscreenRule.Covers(Rect(0, 0, 1920, 1032), Monitor));
    }

    [TestMethod]
    public void MaximizedWindowWithATitleBarDoesNotCount()
    {
        const uint overlappedWindow = 0x00CF0000;
        const uint caption = 0x00C00000;
        const uint thickFrame = 0x00040000;
        const uint popup = 0x80000000;
        Assert.IsTrue(FullscreenRule.IsOrdinaryMaximized(true, overlappedWindow), "a maximized window on a monitor without a taskbar");
        Assert.IsTrue(FullscreenRule.IsOrdinaryMaximized(true, popup | thickFrame));
        Assert.IsFalse(FullscreenRule.IsOrdinaryMaximized(true, popup), "a borderless game or player");
        Assert.IsFalse(FullscreenRule.IsOrdinaryMaximized(true, overlappedWindow & ~(caption | thickFrame)), "a browser in F11 fullscreen");
        Assert.IsFalse(FullscreenRule.IsOrdinaryMaximized(false, overlappedWindow), "not maximized, just sized to cover the monitor");
    }

    [TestMethod]
    public void WindowOnAnotherMonitorDoesNotCount()
    {
        Assert.IsFalse(FullscreenRule.Covers(Rect(1920, 0, 3840, 1080), Monitor));
    }

    [TestMethod]
    public void DesktopAndTaskbarAreNotApps()
    {
        Assert.IsTrue(FullscreenRule.IsShellWindow("Progman"));
        Assert.IsTrue(FullscreenRule.IsShellWindow("WorkerW"));
        Assert.IsTrue(FullscreenRule.IsShellWindow("Shell_TrayWnd"));
        Assert.IsTrue(FullscreenRule.IsShellWindow("WindowsScreenSaverClass"), "a screen saver means nobody is there");
        Assert.IsFalse(FullscreenRule.IsShellWindow("Chrome_WidgetWin_1"));
        Assert.IsFalse(FullscreenRule.IsShellWindow("Windows.UI.Core.CoreWindow"));
    }

    [TestMethod]
    public void LockScreenRemoteDesktopAndVirtualMachinesAreNotPlaying()
    {
        Assert.IsTrue(FullscreenRule.IsNotPlaying("LockApp"), "the lock screen");
        Assert.IsTrue(FullscreenRule.IsNotPlaying("mstsc"), "Remote Desktop Connection");
        Assert.IsTrue(FullscreenRule.IsNotPlaying("msrdc"), "the Windows App and Azure Virtual Desktop client");
        Assert.IsTrue(FullscreenRule.IsNotPlaying("vmconnect"), "a Hyper-V console");
        Assert.IsTrue(FullscreenRule.IsNotPlaying("vmware"), "a VMware Workstation console");
        Assert.IsTrue(FullscreenRule.IsNotPlaying("vmplayer"), "a VMware Workstation Player console");
        Assert.IsTrue(FullscreenRule.IsNotPlaying("vmrc"), "VMware Remote Console");
        Assert.IsTrue(FullscreenRule.IsNotPlaying("VirtualBoxVM"), "a VirtualBox console");
        Assert.IsTrue(FullscreenRule.IsNotPlaying("MSTSC"), "process names are not case sensitive");
        Assert.IsFalse(FullscreenRule.IsNotPlaying("vlc"));
        Assert.IsFalse(FullscreenRule.IsNotPlaying("chrome"));
    }

    // Windows' own views that cover the monitor with no caption: left open with nobody there they
    // would stop the lock with the default "never".
    [TestMethod]
    public void WindowsOwnFullScreenViewsAreNotPlaying()
    {
        Assert.IsTrue(FullscreenRule.IsNotPlaying("explorer"), "Task View, and File Explorer in F11");
        Assert.IsTrue(FullscreenRule.IsNotPlaying("StartMenuExperienceHost"), "the full-screen Start menu");
        Assert.IsTrue(FullscreenRule.IsNotPlaying("ShellExperienceHost"), "the full-screen Start menu before Windows 10 1903");
        Assert.IsTrue(FullscreenRule.IsNotPlaying("SearchHost"), "search on Windows 11");
        Assert.IsTrue(FullscreenRule.IsNotPlaying("SearchApp"), "search on Windows 10");
        Assert.IsTrue(FullscreenRule.IsNotPlaying("SearchUI"), "search on Windows 10 before 2004");
    }

    // Taking a screenshot covers the monitor for a moment with Windows' overlay: it is no full screen
    // app, and adds no extra time. Only those two programs: a player stays a full screen app.
    [TestMethod]
    public void WindowsScreenshotOverlayIsNotPlaying()
    {
        Assert.IsTrue(FullscreenRule.IsNotPlaying("SnippingTool"), "the Snipping Tool's overlay, as the log names it");
        Assert.IsTrue(FullscreenRule.IsNotPlaying("snippingtool"), "process names are not case sensitive");
        Assert.IsTrue(FullscreenRule.IsNotPlaying("ScreenClippingHost"), "Windows' own capture host");
        Assert.IsFalse(FullscreenRule.IsNotPlaying("SnippingTool.exe"), "the name is matched as the log shows it, without .exe");
        Assert.IsFalse(FullscreenRule.IsNotPlaying("Snipping"));
        Assert.IsFalse(FullscreenRule.IsNotPlaying("vlc"));
        Assert.IsFalse(FullscreenRule.IsNotPlaying("PowerPnt"));
    }

    [TestMethod]
    public void ReportsTheProgramThatCoversTheScreenWithItsEffect()
    {
        var world = new FakeWorld { FullScreen = "game" };
        var lines = new List<WhyLine>();
        new FullscreenRule { Effect = Effect.Extra(120) }.Check(world.Look(TimeSpan.FromSeconds(1)), lines);
        CollectionAssert.AreEqual(new[] { new WhyLine(Signal.FullScreen, Effect.Extra(120), "game") }, lines);

        world.FullScreen = null;
        lines.Clear();
        new FullscreenRule { Effect = Effect.Extra(120) }.Check(world.Look(TimeSpan.FromSeconds(2)), lines);
        Assert.IsEmpty(lines);
    }

    [TestMethod]
    public void ReadingTheRealForegroundWindowDoesNotThrow()
    {
        string? process = FullscreenRule.Read();
        Assert.IsTrue(process is null || process.Length > 0);
    }
}
