namespace SteppedOut.Tests;

// config.json and its backup are written whole or not at all: a write that fails part way leaves the
// file there before as it was, never a cut-off file under a backup's name, and never the file the
// bytes were written to first. The bytes are the ones File.WriteAllText wrote before.
[TestClass]
public class WholeFileTests
{
    sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SteppedOut-whole-" + Guid.NewGuid().ToString("N"));

        public Folder() => Directory.CreateDirectory(Path);

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public string[] Names() => Directory.GetFiles(Path).Select(System.IO.Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

        public void Dispose()
        {
            WholeFile.Midway = null;
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    // A write that stops after half its bytes, as a full disk or a crash would leave it.
    static void FailMidway() => WholeFile.Midway = _ => throw new IOException("There is not enough space on the disk.");

    // A 1.0 file, so a save over it keeps no copy of it.
    static readonly string OldText = "{\n  \"configVersion\": 1,\n  \"idleMinutes\": 30\n}\n";

    [TestMethod]
    public void ASaveWritesTheSameBytesAsBefore()
    {
        using var folder = new Folder();
        var config = new Config { IdleMinutes = 30, Language = "ar", RunWhenAway = @"C:\مجلد\script.cmd", LockShortcut = "Ctrl+Alt+L" };
        string path = folder.File("config.json");
        string text = config.Save(path);

        string reference = folder.File("reference.json");
        File.WriteAllText(reference, text);
        CollectionAssert.AreEqual(File.ReadAllBytes(reference), File.ReadAllBytes(path));
        CollectionAssert.AreEqual(new[] { "config.json", "reference.json" }, folder.Names(), "nothing else is left in the folder");

        // And over a file that is there, as every later save is.
        config.IdleMinutes = 45;
        text = config.Save(path);
        File.WriteAllText(reference, text);
        CollectionAssert.AreEqual(File.ReadAllBytes(reference), File.ReadAllBytes(path));
        CollectionAssert.AreEqual(new[] { "config.json", "reference.json" }, folder.Names());
    }

    [TestMethod]
    public void TheSavedTextReadsBackAsItself()
    {
        using var folder = new Folder();
        string path = folder.File("config.json");
        string text = new Config { IdleMinutes = 0.5, MusicVideo = Effect.Extra(45), OnBattery = Effect.Sooner(50) }.Save(path);
        var read = Config.Parse(Config.Read(path), path, out string? problem, out string? warning);
        Assert.IsNull(problem);
        Assert.IsNull(warning);
        Assert.AreEqual(text, read.Save(folder.File("again.json")), "Serialize(Parse(text)) == text");
    }

    [TestMethod]
    public void ASaveThatFailsMidwayLeavesTheOldFile()
    {
        using var folder = new Folder();
        string path = folder.File("config.json");
        File.WriteAllText(path, OldText);
        var before = File.ReadAllBytes(path);

        FailMidway();
        Assert.ThrowsExactly<IOException>(() => new Config { IdleMinutes = 45 }.Save(path));

        CollectionAssert.AreEqual(before, File.ReadAllBytes(path), "the old file, every byte");
        CollectionAssert.AreEqual(new[] { "config.json" }, folder.Names(), "no file is left behind");
    }

    [TestMethod]
    public void AFirstSaveThatFailsMidwayLeavesNoFile()
    {
        using var folder = new Folder();
        string path = folder.File("config.json");
        FailMidway();
        Assert.ThrowsExactly<IOException>(() => new Config().Save(path));
        Assert.IsEmpty(folder.Names(), "neither a cut-off config.json nor anything else");
    }

    // A program that has the file open without letting it be replaced: the save fails as it did
    // before, the file is as it was, and nothing is left next to it.
    [TestMethod]
    public void ASaveThatCannotReplaceTheFileLeavesItAndNothingElse()
    {
        using var folder = new Folder();
        string path = folder.File("config.json");
        File.WriteAllText(path, OldText);
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            Assert.ThrowsExactly<IOException>(() => new Config().Save(path), "the replace says the file is in use");
        }
        Assert.AreEqual(OldText, File.ReadAllText(path));
        CollectionAssert.AreEqual(new[] { "config.json" }, folder.Names());
    }

    // Config.Read shares the file for deletion, as do many programs that only read it, and a save
    // still replaces it while it is open that way.
    [TestMethod]
    public void ASaveReplacesTheFileWhileItIsReadWithSharing()
    {
        using var folder = new Folder();
        string path = folder.File("config.json");
        File.WriteAllText(path, OldText);
        string text;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            text = new Config { IdleMinutes = 45 }.Save(path);
        }
        Assert.AreEqual(text, File.ReadAllText(path));
        CollectionAssert.AreEqual(new[] { "config.json" }, folder.Names());
    }

    // A file left from a write cut off by a crash or a power cut is written over by the next save.
    [TestMethod]
    public void ALeftoverFromACrashIsTakenByTheNextSave()
    {
        using var folder = new Folder();
        string path = folder.File("config.json");
        File.WriteAllText(path, OldText);
        File.WriteAllText(WholeFile.Temporary(path), "{ \"idle");
        string text = new Config().Save(path);
        Assert.AreEqual(text, File.ReadAllText(path));
        CollectionAssert.AreEqual(new[] { "config.json" }, folder.Names());
    }

    [TestMethod]
    public void TheFileWrittenFirstNeverLooksLikeABackup()
    {
        string path = @"C:\x\config.json";
        string temporary = Path.GetFileName(WholeFile.Temporary(path));
        Assert.IsFalse(temporary.StartsWith("config.json", StringComparison.OrdinalIgnoreCase), temporary);
        Assert.IsFalse(Path.GetFileName(WholeFile.Temporary(path + FreshStart.BackupSuffix)).StartsWith("config.json", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(@"C:\x", Path.GetDirectoryName(WholeFile.Temporary(path)), "in the same folder, so the move is a rename");
    }

    [TestMethod]
    public void CreateNewNeverWritesOverAFile()
    {
        using var folder = new Folder();
        string path = folder.File("config.json.before-1.0");
        File.WriteAllText(path, OldText);
        Assert.ThrowsExactly<IOException>(() => WholeFile.CreateNew(path, WholeFile.Bytes("other")));
        Assert.AreEqual(OldText, File.ReadAllText(path));
        CollectionAssert.AreEqual(new[] { "config.json.before-1.0" }, folder.Names());
    }

    // A fresh start keeps the file from before 1.0 first. A backup cut off part way would stand for
    // the original under its name: none is made, the file is not written over, and nothing is left.
    [TestMethod]
    public void ABackupThatFailsMidwayIsNeverLeftCutOff()
    {
        using var folder = new Folder();
        string path = folder.File("config.json");
        string old = "{\n  \"idleMinutes\": 30,\n  \"whilePlaying\": \"moreTime\",\n  \"moreTimeMinutes\": 60,\n  \"welcomed\": true\n}\n";
        File.WriteAllText(path, old);
        var config = Config.Load(path, out string? problem);
        Assert.IsNull(problem);

        FailMidway();
        var result = FreshStart.Apply(config, path, persist: true);

        Assert.AreEqual(FreshOutcome.NotKept, result.Outcome);
        Assert.AreEqual(old, File.ReadAllText(path), "the file is as it was");
        CollectionAssert.AreEqual(new[] { "config.json" }, folder.Names(), "no backup, cut off or whole, and no other file");
        Assert.IsTrue(new Config().SameAs(config), "this run still uses the defaults");

        // The next start makes the whole backup, then writes the fresh file.
        WholeFile.Midway = null;
        var next = Config.Load(path, out problem);
        var written = FreshStart.Apply(next, path, persist: true);
        Assert.AreEqual(FreshOutcome.Written, written.Outcome);
        Assert.AreEqual(old, File.ReadAllText(path + FreshStart.BackupSuffix));
        CollectionAssert.AreEqual(new[] { "config.json", "config.json.before-1.0" }, folder.Names());
    }

    // The backup is made, then the fresh file fails part way: the backup is whole and the file is as it was.
    [TestMethod]
    public void AFreshFileThatFailsMidwayLeavesTheOldFileAndAWholeBackup()
    {
        using var folder = new Folder();
        string path = folder.File("config.json");
        string old = "{\n  \"idleMinutes\": 30,\n  \"whilePlaying\": \"lockAnyway\"\n}\n";
        File.WriteAllText(path, old);
        var config = Config.Load(path, out _);

        int writes = 0;
        WholeFile.Midway = _ =>
        {
            if (++writes == 2) throw new IOException("There is not enough space on the disk.");
        };
        var result = FreshStart.Apply(config, path, persist: true);

        Assert.AreEqual(FreshOutcome.NotWritten, result.Outcome);
        Assert.AreEqual(old, File.ReadAllText(path));
        Assert.AreEqual(old, File.ReadAllText(path + FreshStart.BackupSuffix));
        CollectionAssert.AreEqual(new[] { "config.json", "config.json.before-1.0" }, folder.Names());
    }

    // A later save over a file from before 1.0 that start-up could not replace keeps it first. A
    // failed copy there leaves no cut-off backup, and the save does not happen: the file from
    // before 1.0 is never written over unkept (D50).
    [TestMethod]
    public void TheBackupBeforeALaterSaveIsNeverCutOff()
    {
        using var folder = new Folder();
        string path = folder.File("config.json");
        string old = "{\n  \"idleMinutes\": 30\n}\n";
        File.WriteAllText(path, old);
        var config = Config.Load(path, out _);
        FreshStart.Apply(config, path, persist: false);

        int writes = 0;
        WholeFile.Midway = _ =>
        {
            if (++writes == 1) throw new IOException("There is not enough space on the disk.");
        };
        Assert.ThrowsExactly<IOException>(() => config.Save(path));

        Assert.AreEqual(old, File.ReadAllText(path), "the only copy of the old file stays");
        CollectionAssert.AreEqual(new[] { "config.json" }, folder.Names(), "no cut-off backup");
    }
}

// A save over a linked config.json, or over the one a Store package can only change where it is,
// writes through to the file instead of putting a new file under the name.
[TestClass]
public class LinkedSaveTests
{
    string dir = "";

    // What the save below writes, from a save of the same settings to a file of its own.
    string NewText = "";

    [TestInitialize]
    public void Setup()
    {
        dir = Path.Combine(Path.GetTempPath(), "SteppedOut.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        NewText = new Config { IdleMinutes = 45 }.Save(Path.Combine(dir, "reference", "config.json"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        WholeFile.KeepsInPlace = null;
        WholeFile.Midway = null;
        Directory.Delete(dir, recursive: true);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

    string Old(string name)
    {
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, "{ \"configVersion\": 1, \"idleMinutes\": 30 }");
        return path;
    }

    // A config.json hard linked to a synced copy keeps the link: both names have the new settings.
    [TestMethod]
    public void AHardLinkedFileGetsTheSaveUnderEveryName()
    {
        string path = Old("config.json");
        string other = Path.Combine(dir, "synced.json");
        Assert.IsTrue(CreateHardLink(other, path, IntPtr.Zero), "a hard link needs no rights on NTFS");
        Assert.AreEqual(2, FileLinks.Names(path));

        new Config { IdleMinutes = 45 }.Save(path);

        Assert.AreEqual(NewText, File.ReadAllText(path));
        Assert.AreEqual(NewText, File.ReadAllText(other), "the other name has the new settings too");
        Assert.AreEqual(2, FileLinks.Names(path), "still one file under two names");
        CollectionAssert.AreEquivalent(new[] { "config.json", "synced.json" }, Directory.GetFiles(dir).Select(Path.GetFileName).ToArray());
    }

    // A config.json that is a symbolic link stays one; the file it leads to gets the new settings,
    // written whole as usual next to it.
    [TestMethod]
    public void ASymbolicLinkStaysALinkAndItsFileGetsTheSave()
    {
        string elsewhere = Path.Combine(dir, "synced");
        Directory.CreateDirectory(elsewhere);
        string target = Path.Combine(elsewhere, "settings.json");
        File.WriteAllText(target, "{ \"configVersion\": 1, \"idleMinutes\": 30 }");
        string path = Path.Combine(dir, "config.json");
        try
        {
            File.CreateSymbolicLink(path, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Inconclusive("making a symbolic link needs Developer Mode or admin rights here: " + e.Message);
        }

        new Config { IdleMinutes = 45 }.Save(path);

        Assert.AreEqual(target, new FileInfo(path).LinkTarget, "config.json is still the link");
        Assert.AreEqual(NewText, File.ReadAllText(target));
        CollectionAssert.AreEqual(new[] { "settings.json" }, Directory.GetFiles(elsewhere).Select(Path.GetFileName).ToArray(), "no file of its own left");
    }

    // In the Store package, a config.json only in the real %APPDATA% is written where it is: no file
    // of its own is made, which Windows would put in the package's folder.
    [TestMethod]
    public void AFileTheStorePackageCanOnlyChangeWhereItIsIsWrittenInPlace()
    {
        string path = Old("config.json");
        var written = new List<string>();
        WholeFile.KeepsInPlace = asked => asked == path;
        WholeFile.Midway = written.Add;

        new Config { IdleMinutes = 45 }.Save(path);

        Assert.AreEqual(NewText, File.ReadAllText(path));
        Assert.IsEmpty(written, "no file of its own was written");
        CollectionAssert.AreEqual(new[] { "config.json" }, Directory.GetFiles(dir).Select(Path.GetFileName).ToArray());
    }

    // Shorter settings leave nothing of the longer ones behind.
    [TestMethod]
    public void AShorterSaveInPlaceLeavesNoTail()
    {
        string path = Path.Combine(dir, "config.json");
        File.WriteAllText(path, new string(' ', 20000) + "{}");
        WholeFile.KeepsInPlace = _ => true;

        new Config { IdleMinutes = 45 }.Save(path);

        Assert.AreEqual(NewText, File.ReadAllText(path));
    }

    [TestMethod]
    public void AnOrdinaryFileStillGetsANewFileMovedOverIt()
    {
        string path = Old("config.json");
        var written = new List<string>();
        WholeFile.Midway = written.Add;

        new Config { IdleMinutes = 45 }.Save(path);

        CollectionAssert.AreEqual(new[] { WholeFile.Temporary(path) }, written);
        Assert.AreEqual(1, FileLinks.Names(path));
    }
}

// Which files the Store package can only change where they are.
[TestClass]
public class KeepsInPlaceTests
{
    const string Family = "Publisher.SteppedOut_abcd1234efgh5";
    const string Local = @"C:\Users\someone\AppData\Local";
    const string Roaming = @"C:\Users\someone\AppData\Roaming";
    const string Config = Roaming + @"\SteppedOut\config.json";
    const string Own = Local + @"\Packages\" + Family + @"\LocalCache\Roaming\SteppedOut\config.json";

    static bool Asks(bool packaged, params string[] there) =>
        Packaged.KeepsInPlace(packaged, Family, Local, Roaming, Config, path => there.Contains(path, StringComparer.OrdinalIgnoreCase));

    [TestMethod]
    public void OnlyTheRealFolderInAPackage()
    {
        Assert.IsTrue(Asks(packaged: true, Config), "only in the real folder: in place");
        Assert.IsFalse(Asks(packaged: true, Config, Own), "the package's own copy: a new file moved over it, all in its folder");
        Assert.IsFalse(Asks(packaged: true), "no file yet: the new one goes to the package's folder");
        Assert.IsFalse(Asks(packaged: false, Config), "the normal exe");
    }

    [TestMethod]
    public void AFileOutsideAppDataOrAnUnknownPackageIsLeftToTheUsualSave()
    {
        Assert.IsFalse(Packaged.KeepsInPlace(true, Family, Local, Roaming, @"D:\Elsewhere\config.json", _ => true));
        Assert.IsFalse(Packaged.KeepsInPlace(true, null, Local, Roaming, Config, _ => true));
        Assert.IsFalse(Packaged.KeepsInPlace(true, Family, "", Roaming, Config, _ => true));
        Assert.IsTrue(Packaged.KeepsInPlace(true, Family, Local, Roaming + @"\", Config.ToUpperInvariant(), path => !path.Contains("Packages", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void TheNormalExeNeverWritesInPlaceForThePackage()
    {
        Assert.IsFalse(Packaged.IsPackaged);
        Assert.IsFalse(Packaged.KeepsInPlace(AppInfo.ConfigPath));
    }
}
