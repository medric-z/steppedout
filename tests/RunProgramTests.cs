using System.Diagnostics;

namespace SteppedOut.Tests;

[TestClass]
public class RunProgramTests
{
    [TestMethod]
    public void APowerShellScriptRunsInPowerShellInsteadOfOpeningInNotepad()
    {
        var start = RunProgram.StartInfo(@"C:\Scripts\away script.PS1");

        Assert.IsFalse(start.UseShellExecute);
        Assert.IsTrue(start.CreateNoWindow);
        Assert.AreEqual(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"), start.FileName);
        StringAssert.EndsWith(start.Arguments, @"-File ""C:\Scripts\away script.PS1""");
        Assert.AreEqual(@"C:\Scripts", start.WorkingDirectory);
    }

    [TestMethod]
    public void ProgramsAndShortcutsAreOpenedTheWayExplorerOpensThem()
    {
        foreach (string path in new[] { @"C:\Tools\backup.exe", @"C:\Tools\backup.bat", @"C:\Tools\backup.lnk" })
        {
            var start = RunProgram.StartInfo(path);
            Assert.IsTrue(start.UseShellExecute, path);
            Assert.AreEqual(path, start.FileName);
        }
    }
}

[TestClass]
public class RunProgramStartTests
{
    sealed class FailingStarter : IStarter
    {
        public void Start(ProcessStartInfo start) => throw new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified");
    }

    [TestMethod]
    public void TheScriptGoesToTheStarterAndIsLogged()
    {
        var rec = new AwayFakes.Recorder();
        var log = new List<string>();

        new RunProgram(@"C:\Scripts\away.bat", whenAway: true, new AwayFakes.FakeStarter(rec), log.Add).Start();

        CollectionAssert.AreEqual(new[] { @"start C:\Scripts\away.bat" }, rec.Events);
        CollectionAssert.AreEqual(new[] { @"started C:\Scripts\away.bat" }, log);
    }

    [TestMethod]
    public void AMissingPowerShellScriptIsNotHandedToPowerShell()
    {
        var rec = new AwayFakes.Recorder();
        var log = new List<string>();
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "away.ps1");

        new RunProgram(missing, whenAway: true, new AwayFakes.FakeStarter(rec), log.Add).Start();

        Assert.IsEmpty(rec.Events);
        CollectionAssert.AreEqual(new[] { $"could not start {missing}: the file is not there" }, log);
    }

    [TestMethod]
    public void AStartWindowsRefusesIsLoggedNotThrown()
    {
        var log = new List<string>();

        new RunProgram(@"C:\Scripts\gone.bat", whenAway: false, new FailingStarter(), log.Add).Start();

        CollectionAssert.AreEqual(new[] { @"could not start C:\Scripts\gone.bat: The system cannot find the file specified" }, log);
    }

    // The live run cannot start a file that is not there, whatever its kind, so a dry run says NOT
    // for it, with the reason, as for a .ps1; and WOULD only for one that is there.
    [TestMethod]
    public void ADryRunSaysAFileThatIsNotThereWouldNotRunWhateverItsKind()
    {
        string dir = Path.Combine(Path.GetTempPath(), "SteppedOut.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var starter = new AwayFakes.FakeStarter(new AwayFakes.Recorder());
            foreach (string kind in new[] { ".bat", ".cmd", ".ps1", ".lnk", ".exe" })
            {
                string missing = Path.Combine(dir, "gone" + kind);
                Assert.AreEqual($"NOT run {missing} when away: the file is not there", new RunProgram(missing, whenAway: true, starter, _ => { }).DryRun(), kind);
                string there = Path.Combine(dir, "here" + kind);
                File.WriteAllBytes(there, []);
                Assert.AreEqual($"run {there} when back", new RunProgram(there, whenAway: false, starter, _ => { }).DryRun(), kind);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

[TestClass]
public class OpenAppsTests
{
    static readonly Func<string, bool> Exists = path => path is @"C:\Apps\inkwell.exe" or @"C:\Program Files\Quill Pad\quillpad.exe" or @"C:\Scripts\away.ps1" or @"C:\Scripts\away.bat";

    [TestMethod]
    public void AProgramOpensByItsFullPathTheWayExplorerOpensIt()
    {
        var start = OpenApps.StartInfo(@"C:\Program Files\Quill Pad\quillpad.exe", Exists, out _);

        Assert.IsNotNull(start);
        Assert.IsTrue(start.UseShellExecute);
        Assert.AreEqual(@"C:\Program Files\Quill Pad\quillpad.exe", start.FileName);
        Assert.AreEqual(@"C:\Program Files\Quill Pad", start.WorkingDirectory);
        Assert.AreEqual("", start.Arguments);
    }

    [TestMethod]
    public void APathCopiedWithItsQuotesStillOpens()
    {
        var start = OpenApps.StartInfo(@" ""C:\Apps\inkwell.exe"" ", Exists, out _);

        Assert.IsNotNull(start);
        Assert.AreEqual(@"C:\Apps\inkwell.exe", start.FileName);
    }

    [TestMethod]
    public void AStoreAppOpensThroughTheAppsFolderTheWayTheStartMenuOpensIt()
    {
        var start = OpenApps.StartInfo(@"Shell:AppsFolder\Example.Mail_8wekyb3d8bbwe!App", Exists, out _);

        Assert.IsNotNull(start);
        Assert.IsTrue(start.UseShellExecute);
        Assert.AreEqual(@"shell:AppsFolder\Example.Mail_8wekyb3d8bbwe!App", start.FileName);
        Assert.AreEqual("", start.Arguments);
    }

    [TestMethod]
    public void NothingButAProgramOrAStoreAppIsOpened()
    {
        var refused = new Dictionary<string, string>
        {
            ["https://example.com/page"] = "only a program's full path or a Store app opens here",
            ["inkwell"] = "only a program's full path or a Store app opens here",
            [@"Apps\inkwell.exe"] = "only a program's full path or a Store app opens here",
            [@"\\server\share\inkwell.exe"] = "it is on another PC, by a network path, and only a program on this PC or a Store app opens here",
            [@"\??\C:\Apps\inkwell.exe"] = "only a program's full path or a Store app opens here",
            [@"\Apps\inkwell.exe"] = "only a program's full path or a Store app opens here",
            [@"C:\Apps\inkwell.exe --open-everything"] = "only programs (.exe) and Store apps open here",
            [@"C:\Scripts\away.ps1"] = "only programs (.exe) and Store apps open here",
            [@"C:\Scripts\away.bat"] = "only programs (.exe) and Store apps open here",
            [@"C:\Apps\missing.exe"] = "the file is not there",
            [@"C:\Apps\ink""well.exe"] = "it is not a program",
            ["C:\\Apps\\ink\u0007well.exe"] = "it is not a program",
            ["   "] = "it is not a program",
            [@"shell:AppsFolder\"] = "it names no app",
            [@"shell:AppsFolder\..\..\Windows\inkwell.exe"] = "it names no app",
        };

        foreach (var (entry, why) in refused)
        {
            Assert.IsNull(OpenApps.StartInfo(entry, Exists, out string problem), entry);
            Assert.AreEqual(why, problem, entry);
        }
    }

    // A program on a drive letter mapped to another PC is on that PC, as a network path is; one on
    // a letter not connected now may be a network drive Windows remembers. The log says which.
    [TestMethod]
    public void AProgramOnAMappedNetworkDriveIsNotOpened()
    {
        static DriveType Drives(char letter) => char.ToUpperInvariant(letter) switch
        {
            'Z' => DriveType.Network,
            'Q' => DriveType.NoRootDirectory,
            _ => DriveType.Fixed,
        };
        static bool Anywhere(string path) => true;

        foreach (string entry in new[] { @"Z:\Apps\inkwell.exe", @"z:\Apps\inkwell.exe", @"""Z:\Apps\inkwell.exe""" })
        {
            Assert.IsNull(OpenApps.StartInfo(entry, Anywhere, Drives, out string problem), entry);
            Assert.AreEqual("it is on another PC: Z: is a network drive, and only a program on this PC or a Store app opens here", problem, entry);
        }
        Assert.IsNull(OpenApps.StartInfo(@"q:\Apps\inkwell.exe", Anywhere, Drives, out string gone));
        Assert.AreEqual("it is on Q:, which is not connected to this PC now, and only a program on this PC or a Store app opens here", gone);
        Assert.IsNull(OpenApps.StartInfo(@"\\192.0.2.10\share\inkwell.exe", Anywhere, Drives, out string shared));
        Assert.AreEqual("it is on another PC, by a network path, and only a program on this PC or a Store app opens here", shared);
        Assert.IsNotNull(OpenApps.StartInfo(@"C:\Apps\inkwell.exe", Anywhere, Drives, out _), "a drive on this PC");
    }

    [TestMethod]
    public void EachAppIsOpenedInTurnAndOneThatCannotBeIsLogged()
    {
        var rec = new AwayFakes.Recorder();
        var log = new List<string>();
        string dir = Path.Combine(Path.GetTempPath(), "SteppedOut.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string app = Path.Combine(dir, "inkwell.exe");
        File.WriteAllBytes(app, []);
        try
        {
            new OpenApps([app, "https://example.com", @"shell:AppsFolder\Example.Mail_8wekyb3d8bbwe!App", app.ToUpperInvariant()], whenAway: true, new AwayFakes.FakeStarter(rec), log.Add)
                .Open(CancellationToken.None);

            CollectionAssert.AreEqual(new[] { "start " + app, @"start shell:AppsFolder\Example.Mail_8wekyb3d8bbwe!App" }, rec.Events, "the same app twice opens once");
            CollectionAssert.Contains(log, "did not open https://example.com: only a program's full path or a Store app opens here");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void OnceCalledOffTheRestIsNotOpened()
    {
        var rec = new AwayFakes.Recorder();
        var log = new List<string>();
        using var back = new CancellationTokenSource();
        back.Cancel();

        new OpenApps([@"shell:AppsFolder\Example.Mail_8wekyb3d8bbwe!App"], whenAway: true, new AwayFakes.FakeStarter(rec), log.Add).Open(back.Token);

        Assert.IsEmpty(rec.Events);
        CollectionAssert.AreEqual(new[] { @"you are back, so shell:AppsFolder\Example.Mail_8wekyb3d8bbwe!App is not opened" }, log);
    }
}

// What the script row runs, whether config.json was edited by hand or the path picked with Choose...,
// and how a .ps1 meets the execution policy.
[TestClass]
public class RunProgramRefusalTests
{
    // Z: is a drive letter mapped to another PC here, Q: has no drive behind it now, and every other
    // letter is a drive on this PC.
    static DriveType Drives(char letter) => char.ToUpperInvariant(letter) switch
    {
        'Z' => DriveType.Network,
        'Q' => DriveType.NoRootDirectory,
        _ => DriveType.Fixed,
    };

    static List<string> Run(string path, out List<string> started)
    {
        var rec = new AwayFakes.Recorder();
        var log = new List<string>();
        new RunProgram(path, whenAway: true, new AwayFakes.FakeStarter(rec), log.Add, Drives).Start();
        started = rec.Events.ToList();
        return log;
    }

    // Only a file on this PC by its full path: never a web address, a bare command, a device path
    // or one that depends on the current folder or drive.
    [TestMethod]
    [DataRow("https://example.com/away")]
    [DataRow("notepad")]
    [DataRow(@"Scripts\away.bat")]
    [DataRow(@"\\?\C:\Scripts\away.bat")]
    [DataRow(@"\\.\C:\Scripts\away.bat")]
    [DataRow(@"\??\C:\Scripts\away.bat")]
    [DataRow(@"\Scripts\away.bat")]
    [DataRow("C:away.bat")]
    public void APathThatIsNotAFileOnThisPCIsNotRun(string path)
    {
        var log = Run(path, out var started);

        Assert.IsEmpty(started, "nothing is started");
        CollectionAssert.AreEqual(new[] { $"did not run {path}: only a file on this PC, by its full path, runs here" }, log);
    }

    // Nor a file on another PC, by a network path or on a mapped network drive, which Choose... can
    // browse to, nor one on a letter not connected now, which may be a network drive Windows
    // remembers. The log says why plainly.
    [TestMethod]
    [DataRow(@"\\192.0.2.10\share\away.bat", "it is on another PC, by a network path")]
    [DataRow("//192.0.2.10/share/away.bat", "it is on another PC, by a network path")]
    [DataRow(@"Z:\Scripts\away.bat", "it is on another PC: Z: is a network drive")]
    [DataRow(@"z:\Scripts\away.lnk", "it is on another PC: Z: is a network drive")]
    [DataRow(@"Q:\Scripts\away.bat", "it is on Q:, which is not connected to this PC now")]
    public void AFileOnAnotherPCIsNotRunAndTheLogSaysWhy(string path, string why)
    {
        var log = Run(path, out var started);

        Assert.IsEmpty(started, "nothing is started");
        CollectionAssert.AreEqual(new[] { $"did not run {path}: {why}, and only a file on this PC runs here" }, log);
    }

    // Windows' own answer for a network path, without a stand-in.
    [TestMethod]
    public void TheLogSaysWhyAFileOnAnotherPCIsNotRun()
    {
        Assert.AreEqual("it is on another PC, by a network path, and only a file on this PC runs here", RunProgram.Refusal(@"\\192.0.2.10\share\away.bat"));
        Assert.AreEqual("it is on another PC, by a network path, and only a file on this PC runs here", RunProgram.Refusal("//192.0.2.10/share/away.bat"));
    }

    // Kinds the row does not offer are not run, whatever Windows would open them with.
    [TestMethod]
    [DataRow(@"C:\Scripts\away.vbs")]
    [DataRow(@"C:\Scripts\away.html")]
    [DataRow(@"C:\Scripts\away")]
    [DataRow(@"C:\Scripts\away.bat:hidden")]
    public void AFileOfAnotherKindIsNotRun(string path)
    {
        var log = Run(path, out var started);

        Assert.IsEmpty(started);
        CollectionAssert.AreEqual(new[] { $"did not run {path}: only scripts (.bat, .cmd, .ps1), shortcuts (.lnk) and programs (.exe) run here" }, log);
    }

    [TestMethod]
    [DataRow(@"C:\Scripts\away.bat")]
    [DataRow(@"C:\Scripts\away.CMD")]
    [DataRow(@"C:\Scripts\away.lnk")]
    [DataRow(@"C:\Tools\away.exe")]
    [DataRow(@"D:\أدوات\away & back.bat")]
    public void EveryKindTheRowOffersRuns(string path)
    {
        var log = Run(path, out var started);

        CollectionAssert.AreEqual(new[] { "start " + path }, started);
        CollectionAssert.AreEqual(new[] { "started " + path }, log);
    }

    // AllSigned is kept: someone who chose to run only signed scripts is not overruled.
    [TestMethod]
    public void AnAllSignedPolicyIsKept()
    {
        var start = RunProgram.StartInfo(@"C:\Scripts\away.ps1", () => "AllSigned");

        Assert.AreEqual(@"-NoProfile -NonInteractive -File ""C:\Scripts\away.ps1""", start.Arguments);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("Restricted")]
    [DataRow("RemoteSigned")]
    [DataRow("Unrestricted")]
    public void AnyOtherPolicyIsSetAsideForTheRun(string? policy)
    {
        var start = RunProgram.StartInfo(@"C:\Scripts\away.ps1", () => policy);

        Assert.AreEqual(@"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""C:\Scripts\away.ps1""", start.Arguments);
    }

    // As PowerShell decides: Group Policy for the PC, then for the user, then the user's own, then the PC's.
    [TestMethod]
    public void ThePolicyIsTheFirstOneSetInPowerShellsOrder()
    {
        var set = new Dictionary<string, string?>();
        string? Read(Microsoft.Win32.RegistryHive hive, string key) =>
            set.TryGetValue((hive == Microsoft.Win32.RegistryHive.LocalMachine ? "machine " : "user ") + (key.Contains("Policies") ? "policy" : "own"), out var v) ? v : null;

        Assert.IsNull(PowerShellPolicy.Effective(Read));
        set["machine own"] = "RemoteSigned";
        Assert.AreEqual("RemoteSigned", PowerShellPolicy.Effective(Read));
        set["user own"] = "AllSigned";
        Assert.AreEqual("AllSigned", PowerShellPolicy.Effective(Read));
        set["user policy"] = "Undefined";
        Assert.AreEqual("AllSigned", PowerShellPolicy.Effective(Read), "Undefined is not set");
        set["user policy"] = "Unrestricted";
        Assert.AreEqual("Unrestricted", PowerShellPolicy.Effective(Read));
        set["machine policy"] = "AllSigned";
        Assert.AreEqual("AllSigned", PowerShellPolicy.Effective(Read));
    }

    [TestMethod]
    public void ThePolicyOnThisPCReadsWithoutThrowing()
    {
        string? policy = PowerShellPolicy.Effective();
        Assert.IsTrue(policy is null || policy.Length > 0);
    }
}

// A copy started as administrator starts nothing from config.json, which any program of the
// sign-in can write without admin rights.
[TestClass]
public class ElevatedStartTests
{
    [TestMethod]
    public void AsAdministratorNoAppIsOpenedAndNoScriptRun()
    {
        var log = new List<string>();
        var starter = AwayParts.StarterFor(elevated: true);
        string app = Environment.ProcessPath!;

        new OpenApps([app], whenAway: true, starter, log.Add).Open(CancellationToken.None);
        new RunProgram(@"C:\Scripts\away.bat", whenAway: true, starter, log.Add).Start();

        CollectionAssert.AreEqual(new[]
        {
            $"could not open {app}: {ElevatedStarter.Reason}",
            $@"could not start C:\Scripts\away.bat: {ElevatedStarter.Reason}",
        }, log);
    }

    [TestMethod]
    public void WithTheSignInsOwnRightsTheyStartAsBefore()
    {
        Assert.IsInstanceOfType<ProcessStarter>(AwayParts.StarterFor(elevated: false));
        Assert.IsInstanceOfType<ElevatedStarter>(AwayParts.StarterFor(elevated: true));
    }

    // The test run is not elevated by UAC here, and reading it never throws.
    [TestMethod]
    public void TheRightsOfThisProcessAreRead()
    {
        _ = Elevation.Full;
        StringAssert.Contains(Elevation.StartLine, "running as administrator");
    }
}
