using Microsoft.Win32;

namespace SteppedOut.Tests;

// A --dry-run copy never changes Start with Windows: the entry it wrote would have no --dry-run, so
// the next sign-in would start a copy that really locks. It says in the log what it would do.
[TestClass]
[DoNotParallelize]
public class AutostartDryRunTests
{
    const string Tree = @"Software\SteppedOut.Tests.DryRun";
    const string TestKey = Tree + @"\Run";

    [TestCleanup]
    public void Cleanup() => Registry.CurrentUser.DeleteSubKeyTree(Tree, throwOnMissingSubKey: false);

    static string? Value()
    {
        using var key = Registry.CurrentUser.OpenSubKey(TestKey);
        return key?.GetValue(AppInfo.Name) as string;
    }

    [TestMethod]
    public void TurningItOnInADryRunWritesNothingAndSaysSo()
    {
        var log = new List<string>();

        Assert.AreEqual(AutostartState.Off, Autostart.WouldChoose(true, null, TestKey, log.Add));

        Assert.IsNull(Value(), "no Run entry was written");
        CollectionAssert.AreEqual(new[] { "WOULD turn start with Windows on (dry run)" }, log);
    }

    [TestMethod]
    public void TurningItOffInADryRunLeavesTheEntry()
    {
        Autostart.Enable(TestKey);
        string? before = Value();
        var log = new List<string>();

        Assert.AreEqual(AutostartState.On, Autostart.WouldChoose(false, null, TestKey, log.Add));
        Assert.AreEqual(AutostartState.On, Autostart.WouldChoose(true, null, TestKey, log.Add), "already on: nothing to say");

        Assert.AreEqual(before, Value());
        CollectionAssert.AreEqual(new[] { "WOULD turn start with Windows off (dry run)" }, log);
    }

    [TestMethod]
    public void AnEntryForAFileThatIsGoneIsNotTakenOverInADryRun()
    {
        string gone = $"\"{Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "SteppedOut.exe")}\"";
        using (var key = Registry.CurrentUser.CreateSubKey(TestKey))
        {
            key.SetValue(AppInfo.Name, gone);
        }
        var log = new List<string>();

        Assert.IsFalse(Autostart.TakesOver(TestKey, dryRun: true, log.Add));
        Assert.AreEqual(gone, Value());
        CollectionAssert.AreEqual(new[] { "WOULD point start with Windows at this copy: it starts a file that is gone (dry run)" }, log);

        Assert.IsTrue(Autostart.TakesOver(TestKey, dryRun: false, log.Add), "a normal run takes it over");
        Assert.HasCount(1, log);
    }

    [TestMethod]
    public void TheStorePackagesTaskIsNotAskedInADryRun()
    {
        var task = new FakeTask(StartupTaskState.Disabled);
        var log = new List<string>();

        Assert.AreEqual(AutostartState.Off, Autostart.WouldChoose(true, task, TestKey, log.Add));

        CollectionAssert.AreEqual(new[] { "State" }, task.Calls);
        CollectionAssert.AreEqual(new[] { "WOULD turn start with Windows on (dry run)" }, log);
    }

    // Program sets it from --dry-run; it starts off.
    [TestMethod]
    public void ANormalRunChangesItAsBefore()
    {
        Assert.IsFalse(Autostart.DryRun);
    }

    sealed class FakeTask(StartupTaskState state) : IStartupTask
    {
        public List<string> Calls { get; } = new();

        public StartupTaskState State()
        {
            Calls.Add("State");
            return state;
        }

        public StartupTaskState RequestEnable()
        {
            Calls.Add("RequestEnable");
            return StartupTaskState.Enabled;
        }

        public void Disable() => Calls.Add("Disable");
    }
}
