namespace SteppedOut.Tests;

[TestClass]
public class JitterFilterTests
{
    const int Threshold = 5;

    [TestMethod]
    public void NoInputIsNoInput()
    {
        Assert.IsFalse(JitterFilter.IsRealInput(false, false, 100, Threshold));
    }

    [TestMethod]
    public void TypingWithAStillCursorCounts()
    {
        Assert.IsTrue(JitterFilter.IsRealInput(true, false, 0, Threshold));
    }

    [TestMethod]
    public void SensorTwitchIsIgnored()
    {
        Assert.IsFalse(JitterFilter.IsRealInput(true, false, 1, Threshold));
        Assert.IsFalse(JitterFilter.IsRealInput(true, false, 3, Threshold));
        Assert.IsFalse(JitterFilter.IsRealInput(true, false, 4, Threshold));
    }

    [TestMethod]
    public void RealMouseMovementCounts()
    {
        Assert.IsTrue(JitterFilter.IsRealInput(true, false, 5, Threshold));
        Assert.IsTrue(JitterFilter.IsRealInput(true, false, 400, Threshold));
    }

    [TestMethod]
    public void AWindowChangeCountsWhateverMovedTheCursor()
    {
        Assert.IsTrue(JitterFilter.IsRealInput(true, true, 2, Threshold));
    }

    [TestMethod]
    public void FilterOffAcceptsEverything()
    {
        Assert.IsTrue(JitterFilter.IsRealInput(true, false, 2, 0));
        Assert.IsTrue(JitterFilter.IsRealInput(true, false, 2, 1));
    }
}
