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
        Assert.IsFalse(FullscreenRule.IsShellWindow("Chrome_WidgetWin_1"));
        Assert.IsFalse(FullscreenRule.IsShellWindow("Windows.UI.Core.CoreWindow"));
    }
}
