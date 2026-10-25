namespace SteppedOut.Tests;

[TestClass]
public class CommandLineTests
{
    [TestMethod]
    public void IdleTakesMinutesInEitherForm()
    {
        Assert.AreEqual(0.5, CommandLine.Parse(["--idle", "0.5"]).IdleMinutes);
        Assert.AreEqual(2.5, CommandLine.Parse(["--idle=2.5"]).IdleMinutes);
        Assert.AreEqual(1440, CommandLine.Parse(["--idle", "1440"]).IdleMinutes);
        Assert.IsNull(CommandLine.Parse([]).IdleMinutes);
    }

    [TestMethod]
    public void IdleOutsideTheConfigRangeIsRefused()
    {
        foreach (string value in new[] { "NaN", "Infinity", "-Infinity", "1e999", "1e-9", "0", "-5", "1441", "soon" })
        {
            var options = CommandLine.Parse(["--idle", value]);
            Assert.IsNotNull(options.Error, value);
            Assert.IsNull(options.IdleMinutes, value);
        }
        Assert.IsNotNull(CommandLine.Parse(["--idle"]).Error, "no value at all");
    }

    [TestMethod]
    public void UnknownOptionsAreRefused()
    {
        StringAssert.Contains(CommandLine.Parse(["--fast"]).Error, "--fast");
    }
}
