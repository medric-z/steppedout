using System.Diagnostics;

namespace SteppedOut.Tests;

[TestClass]
public class ProcessRuleTests
{
    [TestMethod]
    public void NormalizesNamesPathsAndExtensions()
    {
        Assert.AreEqual("obs64", ProcessRule.Normalize("obs64"));
        Assert.AreEqual("obs64", ProcessRule.Normalize(" obs64.exe "));
        Assert.AreEqual("obs64", ProcessRule.Normalize(@"C:\Program Files\obs-studio\bin\64bit\obs64.exe"));
        Assert.AreEqual("", ProcessRule.Normalize("   "));
    }

    [TestMethod]
    public void EmptyListNeverHolds()
    {
        Assert.IsNull(new ProcessRule(new[] { "", "  " }).Check());
    }

    [TestMethod]
    public void FindsARunningProcessCaseInsensitively()
    {
        string self = Process.GetCurrentProcess().ProcessName;
        var rule = new ProcessRule(new[] { self.ToUpperInvariant() + ".EXE" });
        StringAssert.StartsWith(rule.Check(), "process running: ");
    }

    [TestMethod]
    public void IgnoresProcessesThatAreNotRunning()
    {
        Assert.IsNull(new ProcessRule(new[] { "steppedout-no-such-process-" + Guid.NewGuid().ToString("N") }).Check());
    }
}
