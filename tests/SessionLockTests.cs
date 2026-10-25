namespace SteppedOut.Tests;

// The tests run on a PC someone is using, so nothing in them may lock the session. This is set
// before any test runs, and a lock request then throws instead.
[TestClass]
public class TestRunSetup
{
    [AssemblyInitialize]
    public static void BlockSessionLock(TestContext context)
    {
        SessionLock.Blocked = true;
        // The DLL search the app sets first thing in Main, so every test runs with it.
        Assert.IsTrue(DllSearch.OnlySystem32(), "Windows took the DLL search");
        // English, whatever language Windows is shown in, so the suite gives the same result on an
        // Arabic Windows; the tests that check Arabic choose it themselves.
        Lang.Use("en");
        TestRunFolders.AtStart();
    }

    [AssemblyCleanup]
    public static void RemoveScratch() => TestRunFolders.AtEnd();
}

[TestClass]
public class SessionLockTests
{
    [TestMethod]
    public void ARequestThrowsInsteadOfLockingWhileBlocked()
    {
        Assert.IsTrue(SessionLock.Blocked, "set for the whole test run");
        Assert.ThrowsExactly<InvalidOperationException>(() => SessionLock.TryRequest());
        Assert.AreEqual<Func<bool>>(SessionLock.TryRequest, WatchSeams.Real.RequestLock, "the watcher asks this way, so the guard covers it");
    }

    // TryRequest is the one way to ask Windows for the lock, so the guard above covers every lock.
    [TestMethod]
    public void TheOnlyLockRequestIsTheOneTheGuardCovers()
    {
        var asks = typeof(SessionLock).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Where(m => m.Name.Contains("Request", StringComparison.Ordinal)).Select(m => m.Name).ToList();
        CollectionAssert.AreEqual(new[] { nameof(SessionLock.TryRequest) }, asks);
    }

    // A program is started the same one way, and that is guarded as the lock request is: a test that
    // reaches the real starter by mistake (AwayParts.StarterFor, Open the folder) starts none of the
    // person's programs. The path is one that is not there, away from the test run's and the temp
    // folder, so the result is the same wherever the code is kept, and even an unguarded start would
    // only fail.
    [TestMethod]
    public void AProgramStartThrowsInsteadOfStartingWhileBlocked()
    {
        Assert.IsTrue(SessionLock.Blocked, "set for the whole test run");
        string missing = Path.Combine(Environment.SystemDirectory, "never-" + Guid.NewGuid().ToString("N") + ".exe");
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => new ProcessStarter().Start(new System.Diagnostics.ProcessStartInfo(missing) { UseShellExecute = true }));
        StringAssert.Contains(error.Message, "blocked");
        Assert.ThrowsExactly<InvalidOperationException>(() => new ProcessStarter().Start(new System.Diagnostics.ProcessStartInfo(@"shell:AppsFolder\Example.Mail_8wekyb3d8bbwe!App") { UseShellExecute = true }));
        Assert.IsInstanceOfType<ProcessStarter>(AwayParts.StarterFor(elevated: false), "the away actions start programs this way");
    }

    // The one program still started while blocked is one a test built for itself and copied, as the
    // Close apps test does: %TEMP%\so<32 hex>\out\so<32 hex>.exe. Nothing else in the temp folder is
    // let through, so with the code kept there the test host is refused too, and a test that hands
    // Environment.ProcessPath to the real starter by mistake starts nothing.
    [TestMethod]
    public void OnlyAProgramATestBuiltForItselfStartsWhileBlocked()
    {
        string temp = Path.GetTempPath();
        static string Own() => "so" + Guid.NewGuid().ToString("N");
        string own = Own();
        Assert.IsTrue(ProcessStarter.MadeForATest(Path.Combine(temp, own, "out", Own() + ".exe")), "a test's own program");

        Assert.IsFalse(ProcessStarter.MadeForATest(Environment.ProcessPath!), "the test host itself");
        Assert.IsFalse(ProcessStarter.MadeForATest(Path.Combine(temp, "clone", "tests", "bin", "Release", "net10.0-windows", "testhost.exe")), "a test host with the code kept in the temp folder");
        Assert.IsFalse(ProcessStarter.MadeForATest(Path.Combine(temp, own + ".exe")), "a program straight in the temp folder");
        Assert.IsFalse(ProcessStarter.MadeForATest(Path.Combine(TestRunFolders.Scratch, Own() + ".exe")), "the test run's scratch folder");
        Assert.IsFalse(ProcessStarter.MadeForATest(Path.Combine(temp, own, "out", "testhost.exe")), "another name");
        Assert.IsFalse(ProcessStarter.MadeForATest(Path.Combine(temp, own, "src", Own() + ".exe")), "another folder");
        Assert.IsFalse(ProcessStarter.MadeForATest(Path.Combine(temp, "soNotAGuid", "out", Own() + ".exe")), "a folder not named for a test");
        Assert.IsFalse(ProcessStarter.MadeForATest(Path.Combine(temp, "clone", own, "out", Own() + ".exe")), "deeper in the temp folder");
        Assert.IsFalse(ProcessStarter.MadeForATest(Path.Combine(temp, "..", own, "out", Own() + ".exe")), "beside the temp folder");
        Assert.IsFalse(ProcessStarter.MadeForATest(Path.Combine(temp, own, "out", "..", "..", Own() + ".exe")), "a path that climbs out");
        Assert.IsFalse(ProcessStarter.MadeForATest(Path.Combine(temp, own, "out", Own() + ".bat")), "only a program");
        Assert.IsFalse(ProcessStarter.MadeForATest(Path.Combine(own, "out", Own() + ".exe")), "only a full path");
        Assert.IsFalse(ProcessStarter.MadeForATest(@"shell:AppsFolder\Example.Mail_8wekyb3d8bbwe!App"));
        Assert.IsFalse(ProcessStarter.MadeForATest(""));
    }

    // Only a full path is let through. A path that is not full is read against the folder the
    // process is in, or the current folder of its drive, so with the process in the temp folder a
    // relative path, one from the root of the drive and one that names only the drive would each
    // come out as a test's own program; they are refused before that.
    [TestMethod]
    [DoNotParallelize]
    public void APathThatIsNotFullIsRefusedEvenFromTheTempFolder()
    {
        string temp = Path.GetFullPath(Path.GetTempPath());
        static string Own() => "so" + Guid.NewGuid().ToString("N");
        string own = Own(), program = Own() + ".exe";
        string full = Path.Combine(temp, own, "out", program);
        Assert.IsTrue(ProcessStarter.MadeForATest(full), "the full path is let through");
        var partial = new List<string> { Path.Combine(own, "out", program), Path.Combine(".", own, "out", program) };
        string root = Path.GetPathRoot(temp) ?? "";
        if (root.Length == 3 && root[1] == ':')
        {
            partial.Add(full[2..]);
            partial.Add(full[..2] + full[temp.Length..]);
        }
        string was = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = temp;
            foreach (string path in partial)
            {
                Assert.AreEqual(full, Path.GetFullPath(path), $"{path} reads as the test's own program from the temp folder");
                Assert.IsFalse(ProcessStarter.MadeForATest(path), $"{path} is not a full path");
            }
        }
        finally
        {
            Environment.CurrentDirectory = was;
        }
    }

    // Program starts go through ProcessStarter only, so the guard above covers every one.
    [TestMethod]
    public void EveryProgramStartGoesThroughTheGuardedStarter()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src", "SteppedOut.csproj"))) root = root.Parent;
        if (root is null)
        {
            Assert.Inconclusive("the source is not next to the tests here");
            return;
        }
        var starts = Directory.EnumerateFiles(Path.Combine(root.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .SelectMany(file => File.ReadLines(file).Select((line, i) => (file, line, i)))
            .Where(found => found.line.Contains("Process.Start(", StringComparison.Ordinal))
            .Select(found => $"{Path.GetFileName(found.file)}:{found.i + 1}")
            .ToList();
        Assert.HasCount(1, starts, string.Join(", ", starts));
        StringAssert.StartsWith(starts[0], "RunProgram.cs:");
    }
}
