namespace SteppedOut.Tests;

// An error that comes back every second is written in full once, then counted once an hour, so it
// never pushes its own first stack trace out of the log.
[TestClass]
public class ErrorRepeatsTests
{
    static Exception Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException e)
        {
            return e;
        }
    }

    [TestMethod]
    public void TheSameErrorEverySecondForTwoHoursIsThreeLines()
    {
        var said = new List<string>();
        var now = TimeSpan.Zero;
        var repeats = new ErrorRepeats("tick failed", said.Add, () => now);
        for (int i = 0; i <= 2 * 60 * 60; i++)
        {
            repeats.Note(Thrown("broken"));
            now += TimeSpan.FromSeconds(1);
        }
        Assert.HasCount(3, said, string.Join(" | ", said));
        StringAssert.StartsWith(said[0], "tick failed: System.InvalidOperationException: broken");
        StringAssert.Contains(said[0], nameof(Thrown), "the first one in full, with its stack trace");
        Assert.AreEqual("tick failed again, 3600 times since the last line: System.InvalidOperationException: broken", said[1]);
    }

    [TestMethod]
    public void ADifferentErrorIsWrittenInFullAfterTheCountOfTheOneBefore()
    {
        var said = new List<string>();
        var now = TimeSpan.Zero;
        var repeats = new ErrorRepeats("unhandled", said.Add, () => now);
        repeats.Note(Thrown("first"));
        repeats.Note(Thrown("first"));
        repeats.Note(Thrown("first"));
        repeats.Note(Thrown("second"));
        Assert.HasCount(3, said);
        Assert.AreEqual("unhandled: the same error came 2 more times", said[1]);
        StringAssert.StartsWith(said[2], "unhandled: System.InvalidOperationException: second");
    }

    // Once the errors have stayed away for a while, the count goes in the log and the next one is
    // written in full again.
    [TestMethod]
    public void AfterTheErrorsStopTheNextIsWrittenInFullAgain()
    {
        var said = new List<string>();
        var now = TimeSpan.Zero;
        var repeats = new ErrorRepeats("tick failed", said.Add, () => now);
        repeats.Note(Thrown("broken"));
        repeats.Note(Thrown("broken"));
        now += ErrorRepeats.QuietFor;
        repeats.Worked();
        Assert.AreEqual("tick failed: the same error came 1 more time", said[1]);
        repeats.Worked();
        Assert.HasCount(2, said, "nothing more to say");
        repeats.Note(Thrown("broken"));
        Assert.HasCount(3, said);
        StringAssert.StartsWith(said[2], "tick failed: System.InvalidOperationException: broken");
    }

    // An error that fails only some of the tries, with good ones between, is still one run of
    // errors: written in full once, then counted, never in full after each good try.
    [TestMethod]
    public void AnErrorBetweenGoodTriesIsWrittenInFullOnce()
    {
        var said = new List<string>();
        var now = TimeSpan.Zero;
        var repeats = new ErrorRepeats("tick failed", said.Add, () => now);
        for (int second = 0; second < 2 * 60 * 60; second++)
        {
            if (second % 2 == 0) repeats.Note(Thrown("broken"));
            else repeats.Worked();
            now += TimeSpan.FromSeconds(1);
        }
        Assert.HasCount(2, said, string.Join(" | ", said.Select(line => line.Split('\n')[0])));
        Assert.AreEqual(1, said.Count(line => line.Contains(nameof(Thrown), StringComparison.Ordinal)), "one stack trace");
        Assert.AreEqual("tick failed again, 1800 times since the last line: System.InvalidOperationException: broken", said[1]);
    }
}
