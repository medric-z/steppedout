namespace SteppedOut.Tests;

// D50: nothing carries over from before 1.0. A config.json whose configVersion is missing or below 1
// loads as the defaults, with the welcome. The copy that runs keeps it as config.json.before-1.0
// (with the time in the name when that is taken) and writes a fresh file; --once and a second start
// leave it exactly as it is.
[TestClass]
public class FreshStartTests
{
    // A scratch folder, so the tests never touch the real config.json.
    string dir = "";
    string path = "";

    // The clock for a backup's name.
    static readonly DateTime When = new(2026, 10, 20, 17, 5, 0);

    // config.json as 0.1.0 wrote it with its defaults (the v0.1.0 tag).
    const string ZeroOne = "{\r\n  \"idleMinutes\": 15,\r\n  \"graceSeconds\": 15,\r\n  \"jitterFilter\": true,\r\n  \"jitterPixels\": 5,\r\n"
        + "  \"suppressWhileAudioPlaying\": true,\r\n  \"suppressWhileFullscreen\": true,\r\n  \"suppressWhileDisplayRequested\": true,\r\n"
        + "  \"neverLockWhileRunning\": [],\r\n  \"audioPeakThreshold\": 0.01,\r\n  \"audioSilenceSeconds\": 30,\r\n  \"logMaxKilobytes\": 512\r\n}\r\n";

    // As the builds between 0.1.0 and 1.0 wrote it: welcomed, in Arabic, 30 minutes, more time while
    // something plays.
    const string Dev = """
        {
          "language": "ar",
          "idleMinutes": 30,
          "graceSeconds": 15,
          "lockWhenAway": true,
          "whilePlaying": "moreTime",
          "whileFullscreen": "dontLock",
          "whileOnCall": "dontLock",
          "moreTimeMinutes": 60,
          "neverLockWhileRunning": ["obs64"],
          "pauseMedia": true,
          "muteWhenAway": true,
          "screensOffWhenAway": false,
          "runWhenAway": "",
          "runWhenBack": "",
          "showTimeAway": true,
          "breakReminderMinutes": 0,
          "jitterFilter": true,
          "jitterPixels": 5,
          "audioPeakThreshold": 0.01,
          "audioSilenceSeconds": 30,
          "logMaxKilobytes": 512,
          "welcomed": true
        }

        """;

    // Another copy's file in the same shape, which an older copy may write back at any time.
    static readonly string Other = Dev.Replace("\"language\": \"ar\"", "\"language\": \"en\"").Replace("\"idleMinutes\": 30", "\"idleMinutes\": 60");

    // A file with a version below 1.
    const string VersionZero = "{ \"configVersion\": 0, \"language\": \"ar\", \"idleMinutes\": 30, \"lockShortcut\": \"Ctrl+Alt+K\", \"welcomed\": true }";

    static readonly string[] OldNames =
    {
        "whilePlaying", "whileFullscreen", "whileOnCall", "moreTimeMinutes", "neverLockWhileRunning", "pauseMedia", "breakReminderMinutes",
        "suppressWhile",
    };

    [TestInitialize]
    public void Setup()
    {
        dir = Path.Combine(Path.GetTempPath(), "SteppedOut.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        path = Path.Combine(dir, "config.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        WholeFile.Midway = null;
        foreach (string file in Directory.EnumerateFiles(dir)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(dir, recursive: true);
    }

    Config Load()
    {
        var c = Config.Load(path, out string? problem, out _);
        Assert.IsNull(problem);
        return c;
    }

    string[] Backups() => Directory.EnumerateFiles(dir, "config.json" + FreshStart.BackupSuffix + "*").Select(f => Path.GetFileName(f)).Order().ToArray();

    string[] Files() => Directory.EnumerateFiles(dir).Select(f => Path.GetFileName(f)).Order().ToArray();

    static void IsTheDefaults(Config c)
    {
        Assert.IsTrue(new Config().SameAs(c), "nothing in the old file carries over");
        Assert.IsFalse(c.Welcomed, "the welcome shows");
        Assert.AreEqual("auto", c.Language, "not even the language");
    }

    [TestMethod]
    [DataRow(ZeroOne, "0.1")]
    [DataRow(Dev, "0.x")]
    [DataRow(VersionZero, "configVersion 0")]
    public void AFileFromBefore10IsKeptAndTheSettingsStartFresh(string old, string shape)
    {
        byte[] original = WholeFile.Bytes(old);
        File.WriteAllBytes(path, original);
        var c = Load();
        IsTheDefaults(c);

        var result = FreshStart.Apply(c, path, persist: true, When);
        Assert.AreEqual(FreshOutcome.Written, result.Outcome);
        Assert.AreEqual("config.json.before-1.0", result.Backup);
        Assert.IsFalse(result.BackupWasThere);
        Assert.AreEqual($"config.json was from before 1.0 ({shape}): kept it as config.json.before-1.0; the settings start fresh with the defaults and the welcome",
            result.LogLine);
        Assert.IsNull(result.OnceLine);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path + FreshStart.BackupSuffix), "the backup is the file as it was");
        CollectionAssert.AreEqual(new[] { "config.json", "config.json.before-1.0" }, Files(), "nothing else is left next to them");

        string text = File.ReadAllText(path);
        Assert.AreEqual(result.Text, text, "start-up is told what was written");
        foreach (string name in OldNames) Assert.DoesNotContain(name, text, $"{name} is written");
        StringAssert.Contains(text, "\"configVersion\": 1");
        StringAssert.Contains(text, "\"welcomed\": false");
        StringAssert.Contains(text, "\"language\": \"auto\"");
        var next = Load();
        Assert.IsNull(next.Old, "the fresh file is a 1.0 file");
        IsTheDefaults(next);
    }

    [TestMethod]
    public void TheNextStartFindsA10FileAndWritesNothing()
    {
        File.WriteAllText(path, Dev);
        FreshStart.Apply(Load(), path, persist: true, When);
        string text = File.ReadAllText(path);
        var written = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);

        var result = FreshStart.Apply(Load(), path, persist: true, When.AddDays(1));
        Assert.AreEqual(FreshOutcome.NotNeeded, result.Outcome);
        Assert.AreEqual("config version 1", result.LogLine);
        Assert.AreEqual(written, File.GetLastWriteTimeUtc(path));
        Assert.AreEqual(text, File.ReadAllText(path));
        CollectionAssert.AreEqual(new[] { "config.json.before-1.0" }, Backups());
    }

    // An older copy wrote its file back after the fresh start. That one is kept too, with the time in
    // its name; the first backup is never written over or passed over.
    [TestMethod]
    public void AnotherFileFromBefore10IsKeptWithTheTimeInItsName()
    {
        File.WriteAllText(path, Dev);
        FreshStart.Apply(Load(), path, persist: true, When);

        File.WriteAllText(path, Other);
        var c = Load();
        var result = FreshStart.Apply(c, path, persist: true, When);
        Assert.AreEqual(FreshOutcome.Written, result.Outcome);
        Assert.AreEqual("config.json.before-1.0.20261020-1705", result.Backup);
        StringAssert.Contains(result.LogLine, "kept it as config.json.before-1.0.20261020-1705;");
        Assert.AreEqual(Dev, File.ReadAllText(path + FreshStart.BackupSuffix));
        Assert.AreEqual(Other, File.ReadAllText(path + FreshStart.BackupSuffix + ".20261020-1705"));
        IsTheDefaults(Load());

        // Again in the same minute, another file: a third name, nothing written over.
        string third = Other.Replace("\"idleMinutes\": 60", "\"idleMinutes\": 45");
        File.WriteAllText(path, third);
        Assert.AreEqual("config.json.before-1.0.20261020-1705-2", FreshStart.Apply(Load(), path, persist: true, When).Backup);
        Assert.AreEqual(Other, File.ReadAllText(path + FreshStart.BackupSuffix + ".20261020-1705"));
        Assert.AreEqual(third, File.ReadAllText(path + FreshStart.BackupSuffix + ".20261020-1705-2"));
        CollectionAssert.AreEqual(new[] { "config.json.before-1.0", "config.json.before-1.0.20261020-1705", "config.json.before-1.0.20261020-1705-2" }, Backups());
    }

    // A folder that has the first backup's name takes no file, so the next name is used, as when a
    // file has it. The folder is left as it is.
    [TestMethod]
    public void AFolderWithTheBackupsNameIsPassedOver()
    {
        Directory.CreateDirectory(path + FreshStart.BackupSuffix);
        File.WriteAllText(path, Dev);

        var result = FreshStart.Apply(Load(), path, persist: true, When);

        Assert.AreEqual(FreshOutcome.Written, result.Outcome);
        Assert.AreEqual("config.json.before-1.0.20261020-1705", result.Backup);
        Assert.AreEqual(Dev, File.ReadAllText(path + FreshStart.BackupSuffix + ".20261020-1705"));
        Assert.IsTrue(Directory.Exists(path + FreshStart.BackupSuffix));
        IsTheDefaults(Load());
    }

    // Going back to 0.x means copying the backup over config.json. Starting 1.0 again with that same
    // file needs no second copy of it.
    [TestMethod]
    public void TheSameFileIsNotKeptTwice()
    {
        File.WriteAllText(path, Dev);
        FreshStart.Apply(Load(), path, persist: true, When);
        File.Copy(path + FreshStart.BackupSuffix, path, overwrite: true);

        var result = FreshStart.Apply(Load(), path, persist: true, When);
        Assert.AreEqual(FreshOutcome.Written, result.Outcome);
        Assert.AreEqual("config.json.before-1.0", result.Backup);
        Assert.IsTrue(result.BackupWasThere);
        StringAssert.Contains(result.LogLine, "kept it as config.json.before-1.0, which was already there;");
        CollectionAssert.AreEqual(new[] { "config.json.before-1.0" }, Backups());
        IsTheDefaults(Load());
    }

    [TestMethod]
    public void OnlyTheCopyThatRunsWrites()
    {
        Assert.IsTrue(FreshStart.SavesAtStart(new Options(), ownsInstance: true));
        Assert.IsTrue(FreshStart.SavesAtStart(new Options { DryRun = true, IdleMinutes = 1 }, ownsInstance: true));
        Assert.IsFalse(FreshStart.SavesAtStart(new Options { Once = true }, ownsInstance: false), "--once claims nothing");
        Assert.IsFalse(FreshStart.SavesAtStart(new Options { Once = true }, ownsInstance: true));
        Assert.IsFalse(FreshStart.SavesAtStart(new Options(), ownsInstance: false), "a second start");
    }

    // --once and a second start use the defaults for their own run and leave the file exactly as it
    // was, with no backup.
    [TestMethod]
    [DataRow(true, false)]
    [DataRow(true, true)]
    [DataRow(false, false)]
    public void OnceAndASecondStartLeaveTheFileByteForByte(bool once, bool owns)
    {
        byte[] original = WholeFile.Bytes(Dev);
        File.WriteAllBytes(path, original);
        var written = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);

        var c = Load();
        var result = FreshStart.Apply(c, path, FreshStart.SavesAtStart(new Options { Once = once }, owns));
        Assert.AreEqual(FreshOutcome.InMemory, result.Outcome);
        Assert.AreEqual("config.json is from before 1.0 (0.x): this run uses the default settings and leaves the file as it is", result.LogLine);
        Assert.AreEqual("from before 1.0 (0.x), so this run uses the default settings; the file is left as it is", result.OnceLine);
        IsTheDefaults(c);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        Assert.AreEqual(written, File.GetLastWriteTimeUtc(path));
        CollectionAssert.AreEqual(new[] { "config.json" }, Files());
    }

    // A 1.0 file loads as it is and is never kept or written at start-up.
    [TestMethod]
    public void A10FileLoadsUnchangedAndIsNeverKept()
    {
        new Config { IdleMinutes = 30, Language = "ar", MuteWhenAway = true, Welcomed = true }.Save(path);
        byte[] original = File.ReadAllBytes(path);
        var written = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);

        var c = Load();
        Assert.IsNull(c.Old);
        Assert.AreEqual(30, c.IdleMinutes);
        Assert.AreEqual("ar", c.Language);
        Assert.IsTrue(c.MuteWhenAway);
        Assert.IsTrue(c.Welcomed);
        foreach (bool persist in new[] { true, false })
        {
            var result = FreshStart.Apply(c, path, persist, When);
            Assert.AreEqual(FreshOutcome.NotNeeded, result.Outcome);
            Assert.IsNull(result.OnceLine);
        }
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        Assert.AreEqual(written, File.GetLastWriteTimeUtc(path));

        c.IdleMinutes = 20;
        c.Save(path);
        CollectionAssert.AreEqual(new[] { "config.json" }, Files(), "saving over a 1.0 file keeps nothing");
    }

    // A file that cannot be written gets no backup, so none piles up at every start, and is left as
    // it is; the defaults still load.
    [TestMethod]
    public void AFileThatCannotBeWrittenIsLeftAsItIs()
    {
        File.WriteAllText(path, Dev);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        var c = Load();
        var result = FreshStart.Apply(c, path, persist: true, When);
        Assert.AreEqual(FreshOutcome.NotWritten, result.Outcome);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Problem));
        Assert.IsNull(result.Backup);
        Assert.AreEqual($"config.json is from before 1.0 (0.x) and could not be replaced ({result.Problem}); it is left as it is; this run uses the default settings",
            result.LogLine);
        IsTheDefaults(c);
        Assert.AreEqual(Dev, File.ReadAllText(path));
        CollectionAssert.AreEqual(new[] { "config.json" }, Files());
    }

    [TestMethod]
    public void AFileAnotherProgramHoldsIsLeftAsItIs()
    {
        File.WriteAllText(path, Dev);
        var c = Load();
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.AreEqual(FreshOutcome.NotWritten, FreshStart.Apply(c, path, persist: true, When).Outcome);
        }
        IsTheDefaults(c);
        Assert.AreEqual(Dev, File.ReadAllText(path));
        CollectionAssert.AreEqual(new[] { "config.json" }, Files());
    }

    // A backup that cannot be made means the file is not written over: it is the only copy.
    [TestMethod]
    public void ABackupThatFailsNeverLetsTheFileBeWrittenOver()
    {
        byte[] original = WholeFile.Bytes(Dev);
        File.WriteAllBytes(path, original);
        var c = Load();
        WholeFile.Midway = file =>
        {
            if (file.Contains(FreshStart.BackupSuffix, StringComparison.Ordinal)) throw new IOException("the disk is full");
        };
        var result = FreshStart.Apply(c, path, persist: true, When);
        Assert.AreEqual(FreshOutcome.NotKept, result.Outcome);
        Assert.IsNull(result.Backup);
        Assert.AreEqual("config.json is from before 1.0 (0.x) and could not be kept (the disk is full), so it is left as it is; this run uses the default settings",
            result.LogLine);
        IsTheDefaults(c);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        CollectionAssert.AreEqual(new[] { "config.json" }, Files(), "no cut-off backup and no temporary file are left");
    }

    // Kept but not replaced: the next start finds the same file kept already and replaces it then.
    [TestMethod]
    public void AWriteThatFailsAfterTheBackupLeavesTheFileKept()
    {
        byte[] original = WholeFile.Bytes(Dev);
        File.WriteAllBytes(path, original);
        var c = Load();
        WholeFile.Midway = file =>
        {
            if (!file.Contains(FreshStart.BackupSuffix, StringComparison.Ordinal)) throw new IOException("the disk is full");
        };
        var result = FreshStart.Apply(c, path, persist: true, When);
        WholeFile.Midway = null;
        Assert.AreEqual(FreshOutcome.NotWritten, result.Outcome);
        Assert.AreEqual("config.json.before-1.0", result.Backup);
        Assert.AreEqual("config.json is from before 1.0 (0.x) and could not be replaced (the disk is full); it is kept as config.json.before-1.0; "
            + "this run uses the default settings", result.LogLine);
        IsTheDefaults(c);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path + FreshStart.BackupSuffix));
        CollectionAssert.AreEqual(new[] { "config.json", "config.json.before-1.0" }, Files());

        var next = FreshStart.Apply(Load(), path, persist: true, When);
        Assert.AreEqual(FreshOutcome.Written, next.Outcome);
        Assert.IsTrue(next.BackupWasThere);
        CollectionAssert.AreEqual(new[] { "config.json.before-1.0" }, Backups());
    }

    // A newer edit between the read and the write is not saved over or kept: it is read again.
    [TestMethod]
    public void AFileChangedSinceItWasReadIsLeftAlone()
    {
        File.WriteAllText(path, Dev);
        var c = Load();
        File.WriteAllText(path, Other);
        var result = FreshStart.Apply(c, path, persist: true, When);
        Assert.AreEqual(FreshOutcome.NotKept, result.Outcome);
        StringAssert.Contains(result.Problem, "changed on disk");
        Assert.AreEqual(Other, File.ReadAllText(path));
        Assert.IsEmpty(Backups());
    }

    // Whatever saves over a file from before 1.0 later, the settings window or the welcome after a
    // start that could not replace it, keeps it first, by the same names.
    [TestMethod]
    public void ASaveOverAFileFromBefore10KeepsItFirst()
    {
        byte[] original = WholeFile.Bytes(Dev);
        File.WriteAllBytes(path, original);
        var c = Load();
        c.IdleMinutes = 20;
        c.Save(path);
        CollectionAssert.AreEqual(new[] { "config.json.before-1.0" }, Backups());
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path + FreshStart.BackupSuffix));
        Assert.AreEqual(20, Load().IdleMinutes);

        // The file is 1.0's now, so later saves keep nothing more.
        c.Save(path);
        new Config().Save(path);
        CollectionAssert.AreEqual(new[] { "config.json.before-1.0" }, Backups());

        // Another file from before 1.0 put back: kept with the time in its name before the save.
        File.WriteAllText(path, Other);
        Assert.AreEqual("config.json.before-1.0.20261020-1705", FreshStart.KeepBeforeSave(path, When));
        Assert.AreEqual(Other, File.ReadAllText(path + FreshStart.BackupSuffix + ".20261020-1705"));
        Assert.AreEqual("config.json.before-1.0.20261020-1705", FreshStart.KeepBeforeSave(path, When), "the same file is not kept twice");
        Assert.HasCount(2, Backups());
        Assert.IsNull(FreshStart.KeepBeforeSave(Path.Combine(dir, "missing.json")));
    }

    [TestMethod]
    public void ASaveThatCannotKeepTheOldFileDoesNotHappen()
    {
        File.WriteAllText(path, Dev);
        WholeFile.Midway = file =>
        {
            if (file.Contains(FreshStart.BackupSuffix, StringComparison.Ordinal)) throw new IOException("the disk is full");
        };
        var e = Assert.ThrowsExactly<IOException>(() => new Config { IdleMinutes = 20 }.Save(path));
        Assert.AreEqual("the config from before 1.0 could not be kept, so it was not saved over: the disk is full", e.Message);
        Assert.AreEqual(Dev, File.ReadAllText(path));
        CollectionAssert.AreEqual(new[] { "config.json" }, Files());
    }

    // Text that is not JSON is no file from before 1.0: it takes the broken path and is never kept
    // as one.
    [TestMethod]
    public void ABrokenFileIsNotTakenForOneFromBefore10()
    {
        const string broken = "{ \"whilePlaying\": \"moreTime\", this is not json";
        File.WriteAllText(path, broken);
        var c = Config.Load(path, out string? problem);
        Assert.IsNotNull(problem);
        Assert.IsNull(c.Old);
        Assert.AreEqual(FreshOutcome.NotNeeded, FreshStart.Apply(c, path, persist: true, When).Outcome);
        Assert.IsNull(FreshStart.KeepBeforeSave(path, When));
        Assert.AreEqual(broken, File.ReadAllText(path));
        Assert.IsEmpty(Backups());

        var reload = new ConfigReload(path, () => new Config());
        Assert.AreEqual(Reloaded.TryAgain, reload.Try(true, out _, out _, out _));
        for (int i = 2; i < ConfigReload.Tries; i++) reload.Try(false, out _, out _, out _);
        Assert.AreEqual(Reloaded.Broken, reload.Try(false, out _, out _, out _));
        Assert.IsEmpty(Backups());
    }

    // Only a configVersion missing or below 1 makes a file from before 1.0.
    [TestMethod]
    [DataRow("{ \"idleMinutes\": 5 }", "0.x")]
    [DataRow("{}", "0.x")]
    [DataRow("{ \"suppressWhileFullscreen\": false }", "0.1")]
    [DataRow("{ \"configVersion\": 0 }", "configVersion 0")]
    [DataRow("{ \"configVersion\": -3 }", "configVersion -3")]
    [DataRow("{ \"configVersion\": 0.99 }", "configVersion 0.99")]
    [DataRow("{ \"configVersion\": 1, \"configVersion\": 0 }", "configVersion 0")]
    [DataRow("{ \"configVersion\": 1 }", null)]
    [DataRow("{ \"configVersion\": 1.0 }", null)]
    [DataRow("{ \"configVersion\": 7 }", null)]
    [DataRow("{ \"configVersion\": 0, \"configVersion\": 1 }", null)]
    [DataRow("{ \"configVersion\": \"one\" }", null)]
    [DataRow("{ \"configVersion\": null }", null)]
    [DataRow("[]", null)]
    [DataRow("{ not json", null)]
    public void WhatMakesAFileFromBefore10(string text, string? shape) => Assert.AreEqual(shape, Config.OldShape(text));
}
