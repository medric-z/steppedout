namespace SteppedOut.Tests;

// One copy runs per sign-in, and a second start asks it to open its settings. Each test uses a
// name of its own, never AppInfo.MutexName, so a real copy can start while the tests run.
[TestClass]
public class InstanceChannelTests
{
    static string NewName() => @"Local\SteppedOut.Tests-" + Guid.NewGuid().ToString("N");

    [TestMethod]
    public void TheFirstClaimWinsAndASecondIsTurnedAway()
    {
        string name = NewName();
        using var first = InstanceChannel.Claim(name, out string? firstNote);
        Assert.IsNotNull(first);
        Assert.IsNull(firstNote);

        var second = Task.Run(() => InstanceChannel.Claim(name, out string? note) is null ? note : "claimed").Result;

        Assert.AreEqual("another instance is already running, exiting", second);
    }

    [TestMethod]
    public void ASecondStartsAskReachesTheCopyThatRuns()
    {
        string name = NewName();
        using var running = InstanceChannel.Claim(name, out _);
        Assert.IsNotNull(running);
        Assert.IsFalse(running.ShowRequest.WaitOne(0), "nothing asked yet");

        using (var ask = InstanceChannel.OpenShowRequest(name))
        {
            Assert.IsNotNull(ask);
            ask.Set();
        }

        Assert.IsTrue(running.ShowRequest.WaitOne(TimeSpan.FromSeconds(5)), "the copy that runs was asked");
        Assert.IsFalse(running.ShowRequest.WaitOne(0), "one ask opens the settings once");
    }

    [TestMethod]
    public void WithNoCopyRunningThereIsNobodyToAsk()
    {
        Assert.IsNull(InstanceChannel.OpenShowRequest(NewName()));
    }

    [TestMethod]
    public void AClaimAfterTheCopyThatRanQuitWorksAgain()
    {
        string name = NewName();
        InstanceChannel.Claim(name, out _)!.Dispose();

        var again = Task.Run(() =>
        {
            using var channel = InstanceChannel.Claim(name, out string? note);
            return (Claimed: channel is not null, note);
        }).Result;

        Assert.IsTrue(again.Claimed);
        Assert.IsNull(again.note);
        Assert.IsNull(InstanceChannel.OpenShowRequest(name), "the ask went with it");
    }

    [TestMethod]
    public void TheRealNamesAreTheAppsOwn()
    {
        Assert.AreEqual(@"Local\SteppedOut.Show", InstanceChannel.ShowName(AppInfo.MutexName), "as earlier versions named it, so a second start of either reaches the other");
    }
}

// What each kind of start does with the instance and the files.
[TestClass]
public class StartPlanTests
{
    [TestMethod]
    public void OnceClaimsNothingAndWritesNoFile()
    {
        var once = new Options { Once = true };
        Assert.IsFalse(StartPlan.ClaimsInstance(once));
        Assert.AreEqual(new StartPlan(ReadsWithRetries: false, WritesSettings: false, WritesLog: false), StartPlan.For(once, ownsInstance: false));
        Assert.AreEqual(new StartPlan(ReadsWithRetries: false, WritesSettings: false, WritesLog: false), StartPlan.For(once with { DryRun = true }, ownsInstance: false));
    }

    [TestMethod]
    public void ASecondStartReadsOnceAndLeavesTheSettingsAlone()
    {
        foreach (var options in new[] { new Options(), new Options { DryRun = true, IdleMinutes = 5 } })
        {
            Assert.IsTrue(StartPlan.ClaimsInstance(options));
            Assert.AreEqual(new StartPlan(ReadsWithRetries: false, WritesSettings: false, WritesLog: true), StartPlan.For(options, ownsInstance: false));
        }
    }

    [TestMethod]
    public void TheCopyThatRunsReadsWithRetriesAndWrites()
    {
        foreach (var options in new[] { new Options(), new Options { DryRun = true }, new Options { IdleMinutes = 0.5 } })
        {
            Assert.IsTrue(StartPlan.ClaimsInstance(options));
            Assert.AreEqual(new StartPlan(ReadsWithRetries: true, WritesSettings: true, WritesLog: true), StartPlan.For(options, ownsInstance: true));
        }
    }

    // The settings folder is made before the app can log or catch anything. One that cannot be made,
    // here because a file has its name, gives a reason instead of ending the app without a word.
    [TestMethod]
    public void ASettingsFolderThatCannotBeMadeGivesAReasonInsteadOfThrowing()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "SteppedOut.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            string folder = Path.Combine(scratch, "SteppedOut");
            Assert.IsNull(StartPlan.MakeFolder(folder), "made");
            Assert.IsTrue(Directory.Exists(folder));
            Assert.IsNull(StartPlan.MakeFolder(folder), "already there");

            string taken = Path.Combine(scratch, "Taken");
            File.WriteAllText(taken, "a file, not a folder");
            string? problem = StartPlan.MakeFolder(taken);
            Assert.IsNotNull(problem);
            StringAssert.StartsWith(problem, "could not make the settings folder " + taken + ": ");
            Assert.IsTrue(File.Exists(taken), "the file is left as it is");
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }
}

// --once and a second start read config.json without writing it, even when there is none.
[TestClass]
public class ReadOnlyLoadTests
{
    string dir = "";

    [TestInitialize]
    public void Setup()
    {
        dir = Path.Combine(Path.GetTempPath(), "SteppedOut.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(dir, recursive: true);

    [TestMethod]
    public void WithNoFileTheDefaultsAreUsedAndNoFileIsMade()
    {
        string path = Path.Combine(dir, "config.json");

        var config = Config.Load(path, makeIfMissing: false, out string? problem, out string? warning);

        Assert.IsNull(problem);
        Assert.IsNull(warning);
        Assert.IsTrue(config.SameAs(Config.FirstStart()), "a brand-new install's settings");
        Assert.IsEmpty(Directory.GetFileSystemEntries(dir), "nothing was written");
    }

    [TestMethod]
    public void WithNoFolderNoFolderIsMade()
    {
        string folder = Path.Combine(dir, "SteppedOut");
        Config.Load(Path.Combine(folder, "config.json"), makeIfMissing: false, out _, out _);
        Assert.IsFalse(Directory.Exists(folder));
    }

    [TestMethod]
    public void AFileThereIsReadAsByTheCopyThatRuns()
    {
        string path = Path.Combine(dir, "config.json");
        File.WriteAllText(path, """{ "configVersion": 1, "idleMinutes": 7, "welcomed": true }""");
        var before = File.GetLastWriteTimeUtc(path);

        var config = Config.Load(path, makeIfMissing: false, out string? problem, out _);

        Assert.IsNull(problem);
        Assert.AreEqual(7, config.IdleMinutes);
        Assert.AreEqual(before, File.GetLastWriteTimeUtc(path));
        CollectionAssert.AreEqual(new[] { path }, Directory.GetFileSystemEntries(dir));
    }

    // The copy that runs still makes the file, so there is something to hand-edit.
    [TestMethod]
    public void TheCopyThatRunsStillMakesTheFile()
    {
        string path = Path.Combine(dir, "config.json");
        Config.Load(path, out _, out _);
        Assert.IsTrue(File.Exists(path));
    }
}

// The log's starting line names the commit the exe was built from, so a test build and the release
// candidate, both 1.0.0, tell apart.
[TestClass]
public class BuildInfoTests
{
    [TestMethod]
    public void TheCommitAfterTheVersionIsKept()
    {
        Assert.AreEqual("fe559e9", BuildInfo.CommitOf("1.0.0+fe559e95fd4cd646505a8bf825ecdf176101d606"));
        Assert.AreEqual("abcdef0", BuildInfo.CommitOf("1.0.0-rc.1+ABCDEF0123"));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("1.0.0")]
    [DataRow("1.0.0+")]
    [DataRow("1.0.0+local")]
    [DataRow("1.0.0+abc12")]
    public void WithoutACommitThereIsNone(string? informational) => Assert.IsNull(BuildInfo.CommitOf(informational));

    // Built from a git checkout, as the release is, the build has one.
    [TestMethod]
    public void ThisBuildHasOne()
    {
        bool checkout = false;
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null && !checkout; folder = folder.Parent)
        {
            string git = Path.Combine(folder.FullName, ".git");
            checkout = Directory.Exists(git) || File.Exists(git);
        }
        if (!checkout) Assert.Inconclusive("not built from a git checkout");
        Assert.IsNotNull(BuildInfo.Commit);
        Assert.AreEqual($" (build {BuildInfo.Commit})", BuildInfo.Label);
    }
}
