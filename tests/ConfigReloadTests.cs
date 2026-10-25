namespace SteppedOut.Tests;

[TestClass]
public class ConfigReloadTests
{
    // A scratch folder, so the tests never touch the real config.json.
    string dir = "";
    string path = "";

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
        foreach (string file in Directory.EnumerateFiles(dir)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(dir, recursive: true);
    }

    // A file from before 1.0, as an older copy writes it back.
    const string Old = "{ \"language\": \"ar\", \"idleMinutes\": 30, \"whilePlaying\": \"moreTime\", \"moreTimeMinutes\": 60, \"welcomed\": true }";

    string[] Backups() => Directory.EnumerateFiles(dir, "config.json" + FreshStart.BackupSuffix + "*").ToArray();

    // What a copy that is still writing, or an antivirus looking at the new file, does to a reader.
    FileStream Hold() => new(path, FileMode.Open, FileAccess.Read, FileShare.None);

    // Another program's save: the file is written without telling the reload about it.
    string WriteOutside(Config config) => config.Save(path);

    static Reloaded Try(ConfigReload reload, bool first = false) => reload.Try(first, out _, out _, out _);

    [TestMethod]
    public void AFileHeldOpenIsReadAgainRatherThanTakenForBroken()
    {
        WriteOutside(new Config { IdleMinutes = 7 });
        var reload = new ConfigReload(path, () => new Config());
        using (Hold())
        {
            Assert.AreEqual(Reloaded.TryAgain, Try(reload, first: true));
            Assert.AreEqual(Reloaded.TryAgain, Try(reload));
        }
        Assert.AreEqual(Reloaded.Loaded, reload.Try(false, out var fresh, out string? problem, out _));
        Assert.IsNull(problem);
        Assert.AreEqual(7, fresh!.IdleMinutes);
    }

    [TestMethod]
    public void AnEmptyOrCutOffFileIsReadAgain()
    {
        string full = WriteOutside(new Config { IdleMinutes = 7 });
        var reload = new ConfigReload(path, () => new Config());
        File.WriteAllText(path, "");
        Assert.AreEqual(Reloaded.TryAgain, Try(reload, first: true));
        // A copy sets the size first, so a reader can find it full of zeros.
        File.WriteAllBytes(path, new byte[full.Length]);
        Assert.AreEqual(Reloaded.TryAgain, Try(reload));
        File.WriteAllText(path, full[..40]);
        Assert.AreEqual(Reloaded.TryAgain, Try(reload));
        File.WriteAllText(path, full);
        Assert.AreEqual(Reloaded.Loaded, reload.Try(false, out var fresh, out _, out _));
        Assert.AreEqual(7, fresh!.IdleMinutes);
    }

    [TestMethod]
    public void AFileThatStaysBrokenIsBrokenAfterTheTries()
    {
        File.WriteAllText(path, "{ this is not json");
        var reload = new ConfigReload(path, () => new Config());
        Assert.AreEqual(Reloaded.TryAgain, Try(reload, first: true));
        for (int i = 2; i < ConfigReload.Tries; i++) Assert.AreEqual(Reloaded.TryAgain, Try(reload));
        Assert.AreEqual(Reloaded.Broken, reload.Try(false, out var fresh, out string? problem, out _));
        Assert.IsNull(fresh);
        StringAssert.Contains(problem, ": line 1: ");
        Assert.AreEqual("{ this is not json", File.ReadAllText(path));
    }

    [TestMethod]
    public void AFileThatStaysHeldIsBrokenAndSaysWhy()
    {
        WriteOutside(new Config { IdleMinutes = 7 });
        var reload = new ConfigReload(path, () => new Config());
        using var held = Hold();
        Assert.AreEqual(Reloaded.TryAgain, Try(reload, first: true));
        for (int i = 2; i < ConfigReload.Tries; i++) Assert.AreEqual(Reloaded.TryAgain, Try(reload));
        Assert.AreEqual(Reloaded.Broken, reload.Try(false, out _, out string? problem, out _));
        StringAssert.StartsWith(problem, $"could not read {path}: ");
        Assert.IsFalse(string.IsNullOrWhiteSpace(problem![$"could not read {path}: ".Length..]), problem);
    }

    [TestMethod]
    public void ANewChangeStartsTheTriesOver()
    {
        File.WriteAllText(path, "");
        var reload = new ConfigReload(path, () => new Config());
        Assert.AreEqual(Reloaded.TryAgain, Try(reload, first: true));
        for (int i = 2; i < ConfigReload.Tries; i++) Assert.AreEqual(Reloaded.TryAgain, Try(reload));
        Assert.AreEqual(Reloaded.TryAgain, Try(reload, first: true));
    }

    [TestMethod]
    public void TheAppsOwnSaveIsNotLoadedBack()
    {
        var reload = new ConfigReload(path, () => new Config());
        reload.Saved(new Config { IdleMinutes = 7 }.Save(path));
        Assert.AreEqual(Reloaded.Unchanged, Try(reload, first: true));
    }

    [TestMethod]
    public void AnEditRightAfterTheAppsOwnSaveIsLoaded()
    {
        var reload = new ConfigReload(path, () => new Config());
        string own = new Config { IdleMinutes = 7 }.Save(path);
        reload.Saved(own);
        // Within the same half second, so the watcher reports one change for both.
        WriteOutside(new Config { IdleMinutes = 9 });
        Assert.AreEqual(Reloaded.Loaded, reload.Try(true, out var fresh, out _, out _));
        Assert.AreEqual(9, fresh!.IdleMinutes);
        // The app now runs on 9, so the file going back to what it saved is a change too.
        File.WriteAllText(path, own);
        Assert.AreEqual(Reloaded.Loaded, reload.Try(true, out fresh, out _, out _));
        Assert.AreEqual(7, fresh!.IdleMinutes);
    }

    [TestMethod]
    public void TheAppsOwnSaveStillHeldIsNotBroken()
    {
        var reload = new ConfigReload(path, () => new Config());
        reload.Saved(new Config { IdleMinutes = 7 }.Save(path));
        using var held = Hold();
        Assert.AreEqual(Reloaded.TryAgain, Try(reload, first: true));
        for (int i = 2; i < ConfigReload.Tries; i++) Assert.AreEqual(Reloaded.TryAgain, Try(reload));
        Assert.AreEqual(Reloaded.Unchanged, Try(reload));
    }

    // After a save Windows refused, the settings in use are not the file's. The file as it was then
    // is not loaded over them, after a change event with nothing changed or written again with the
    // same text; a real change is.
    [TestMethod]
    public void AfterARefusedSaveOnlyARealChangeIsLoaded()
    {
        string before = WriteOutside(new Config { IdleMinutes = 7 });
        var reload = new ConfigReload(path, () => new Config());
        reload.Saved(null);
        Assert.AreEqual(Reloaded.Unchanged, Try(reload, first: true), "nothing changed");
        File.WriteAllText(path, before);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        Assert.AreEqual(Reloaded.Unchanged, Try(reload, first: true), "the same text written again");
        WriteOutside(new Config { IdleMinutes = 9 });
        Assert.AreEqual(Reloaded.Loaded, reload.Try(true, out var fresh, out _, out _));
        Assert.AreEqual(9, fresh!.IdleMinutes);
        // The file's settings are in use now, so the file going back is a change again.
        File.WriteAllText(path, before);
        Assert.AreEqual(Reloaded.Loaded, Try(reload, first: true));
    }

    // A save refused because another program holds the file, which could not be read either: while
    // the file keeps its size and time, nothing wrote it, held or not.
    [TestMethod]
    public void AfterASaveRefusedOverAHeldFileItIsNotLoadedWhileNothingWroteIt()
    {
        WriteOutside(new Config { IdleMinutes = 7 });
        ConfigReload reload;
        using (Hold())
        {
            reload = new ConfigReload(path, () => new Config());
            reload.Saved(null);
            Assert.AreEqual(Reloaded.TryAgain, Try(reload, first: true));
            for (int i = 2; i < ConfigReload.Tries; i++) Assert.AreEqual(Reloaded.TryAgain, Try(reload));
            Assert.AreEqual(Reloaded.Unchanged, Try(reload), "still held, not broken");
        }
        Assert.AreEqual(Reloaded.Unchanged, Try(reload, first: true), "let go");
        WriteOutside(new Config { IdleMinutes = 9 });
        Assert.AreEqual(Reloaded.Loaded, Try(reload, first: true));
    }

    // A change on disk still being read again when a save is refused is not known yet, so it still
    // loads once it reads.
    [TestMethod]
    public void AChangeStillBeingReadWhenASaveIsRefusedStillLoads()
    {
        WriteOutside(new Config { IdleMinutes = 7 });
        var reload = new ConfigReload(path, () => new Config());
        File.WriteAllText(path, "");
        Assert.AreEqual(Reloaded.TryAgain, Try(reload, first: true));
        WriteOutside(new Config { IdleMinutes = 9 });
        reload.Saved(null);
        Assert.AreEqual(Reloaded.Loaded, reload.Try(false, out var fresh, out _, out _));
        Assert.AreEqual(9, fresh!.IdleMinutes);
    }

    // Another program's edit that lands between two refused saves, before its change event is read,
    // is not taken as what the file held: the event still loads it, instead of the next save that
    // works writing over it with no word.
    [TestMethod]
    public void AnEditBetweenTwoRefusedSavesStillLoadsWhenItsEventIsRead()
    {
        WriteOutside(new Config { IdleMinutes = 7 });
        var reload = new ConfigReload(path, () => new Config());
        reload.Saved(null);
        WriteOutside(new Config { IdleMinutes = 9 });
        reload.Saved(null);
        Assert.AreEqual(Reloaded.Loaded, reload.Try(true, out var fresh, out _, out _));
        Assert.AreEqual(9, fresh!.IdleMinutes);
    }

    // The same for an edit that lands after the app last read or wrote the file and before the first
    // refused save.
    [TestMethod]
    public void AnEditJustBeforeARefusedSaveStillLoadsWhenItsEventIsRead()
    {
        WriteOutside(new Config { IdleMinutes = 7 });
        var reload = new ConfigReload(path, () => new Config());
        WriteOutside(new Config { IdleMinutes = 9 });
        reload.Saved(null);
        Assert.AreEqual(Reloaded.Loaded, reload.Try(true, out var fresh, out _, out _));
        Assert.AreEqual(9, fresh!.IdleMinutes);
    }

    [TestMethod]
    public void AFileGoneForAMomentIsNotReplacedWithTheDefaults()
    {
        WriteOutside(new Config { IdleMinutes = 7 });
        var reload = new ConfigReload(path, () => new Config());
        File.Delete(path);
        Assert.AreEqual(Reloaded.TryAgain, Try(reload, first: true));
        Assert.IsFalse(File.Exists(path), "an editor's rename would find a file in its way");
        WriteOutside(new Config { IdleMinutes = 9 });
        Assert.AreEqual(Reloaded.Loaded, reload.Try(false, out var fresh, out _, out _));
        Assert.AreEqual(9, fresh!.IdleMinutes);
    }

    [TestMethod]
    public void AFileGoneForGoodIsMadeAgainWithTheDefaults()
    {
        var reload = new ConfigReload(path, () => new Config());
        Assert.AreEqual(Reloaded.TryAgain, Try(reload, first: true));
        for (int i = 2; i < ConfigReload.Tries; i++) Assert.AreEqual(Reloaded.TryAgain, Try(reload));
        Assert.AreEqual(Reloaded.Loaded, reload.Try(false, out var fresh, out string? problem, out _));
        Assert.IsNull(problem);
        Assert.AreEqual(15, fresh!.IdleMinutes);
        Assert.IsTrue(File.Exists(path));
    }

    // The other program lets the file go during the first wait, however busy the PC is.
    [TestMethod]
    public void AtStartAFileHeldForAMomentLoads()
    {
        WriteOutside(new Config { IdleMinutes = 7 });
        var held = Hold();
        var waits = new List<int>();
        var c = ConfigReload.AtStart(path, wait =>
        {
            waits.Add(wait);
            held.Dispose();
        }, out string? problem, out _, out string? retried);
        Assert.IsNull(problem);
        Assert.AreEqual(7, c.IdleMinutes);
        CollectionAssert.AreEqual(new[] { ConfigReload.WaitMilliseconds }, waits, "read again once, after one wait");
        // The log still learns why the first read failed.
        StringAssert.StartsWith(retried, $"could not read {path}: ");
    }

    [TestMethod]
    public void AtStartAGoodFileIsReadOnce()
    {
        WriteOutside(new Config { IdleMinutes = 7 });
        var waits = new List<int>();
        var c = ConfigReload.AtStart(path, waits.Add, out string? problem, out _, out string? retried);
        Assert.IsNull(problem);
        Assert.IsNull(retried);
        Assert.AreEqual(7, c.IdleMinutes);
        Assert.IsEmpty(waits);
    }

    [TestMethod]
    public void AtStartABrokenFileIsStillReported()
    {
        File.WriteAllText(path, "{ this is not json");
        var waits = new List<int>();
        var c = ConfigReload.AtStart(path, waits.Add, out string? problem, out _, out string? retried);
        StringAssert.Contains(problem, ": line 1: ");
        StringAssert.Contains(retried, ": line 1: ");
        Assert.AreEqual(15, c.IdleMinutes);
        Assert.AreEqual("{ this is not json", File.ReadAllText(path));
        Assert.HasCount(ConfigReload.Tries - 1, waits, "every try waited for, two seconds in all");
        Assert.IsTrue(waits.All(wait => wait == ConfigReload.WaitMilliseconds));
    }

    [TestMethod]
    public void TheAppsOwnSavePutBackAfterABrokenEditLoads()
    {
        var reload = new ConfigReload(path, () => new Config());
        string own = new Config { IdleMinutes = 7 }.Save(path);
        reload.Saved(own);
        File.WriteAllText(path, "{ this is not json");
        Assert.AreEqual(Reloaded.TryAgain, Try(reload, first: true));
        for (int i = 2; i < ConfigReload.Tries; i++) Assert.AreEqual(Reloaded.TryAgain, Try(reload));
        Assert.AreEqual(Reloaded.Broken, Try(reload));
        // Loaded, not Unchanged, so the tray app knows the file is good again and does not keep it
        // as config.json.broken on the next save.
        File.WriteAllText(path, own);
        Assert.AreEqual(Reloaded.Loaded, reload.Try(true, out var fresh, out _, out _));
        Assert.AreEqual(7, fresh!.IdleMinutes);
    }

    [TestMethod]
    public void ATryThatAsksForAnotherSaysWhy()
    {
        var reload = new ConfigReload(path, () => new Config());
        File.WriteAllText(path, "{ this is not json");
        Assert.AreEqual(Reloaded.TryAgain, reload.Try(true, out _, out string? problem, out _));
        StringAssert.Contains(problem, ": line 1: ");
        File.Delete(path);
        Assert.AreEqual(Reloaded.TryAgain, reload.Try(true, out _, out problem, out _));
        Assert.AreEqual($"could not find {path}", problem);
        WriteOutside(new Config());
        using var held = Hold();
        Assert.AreEqual(Reloaded.TryAgain, reload.Try(true, out _, out problem, out _));
        StringAssert.StartsWith(problem, $"could not read {path}: ");
    }

    // A file from before 1.0 put back while the app runs does not reset the settings in use part way
    // through a session (D50). It is kept next to config.json and the settings in use are written
    // back as the app's own save, so the change that save makes is not loaded again: one apply.
    [TestMethod]
    public void AFileFromBefore10PutBackWhileRunningKeepsTheSettingsInUse()
    {
        var running = new Config { IdleMinutes = 7, Language = "ar", MuteWhenAway = true, Welcomed = true };
        var reload = new ConfigReload(path, () => running);
        reload.Saved(running.Save(path));
        File.WriteAllText(path, Old);

        int applied = 0;
        Assert.AreEqual(Reloaded.Loaded, reload.Try(true, out var fresh, out string? problem, out string? warning));
        applied++;
        Assert.IsNull(problem);
        Assert.IsNull(warning);
        Assert.AreNotSame(running, fresh);
        Assert.IsTrue(running.SameAs(fresh!), "the settings in use, not the defaults and not the old file's");
        Assert.IsTrue(fresh!.Welcomed, "the welcome does not come back");
        Assert.AreEqual(Old, File.ReadAllText(path + FreshStart.BackupSuffix));
        string written = File.ReadAllText(path);
        var onDisk = Config.Parse(written, path, out _, out _);
        Assert.IsNull(onDisk.Old);
        Assert.IsTrue(running.SameAs(onDisk));

        // The watcher's change for that save, perhaps twice, as a save can be seen as two writes.
        for (int i = 0; i < 2; i++)
        {
            if (reload.Try(true, out _, out _, out _) == Reloaded.Loaded) applied++;
        }
        Assert.AreEqual(1, applied);
        Assert.AreEqual(written, File.ReadAllText(path));
        Assert.HasCount(1, Backups());
    }

    // Kept, but it could not be written over: the settings in use stay, and nothing is applied.
    [TestMethod]
    public void AFileFromBefore10ThatCannotBeWrittenOverLeavesTheSettingsInUse()
    {
        var running = new Config { IdleMinutes = 7, Welcomed = true };
        var reload = new ConfigReload(path, () => running);
        File.WriteAllText(path, Old);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        Assert.AreEqual(Reloaded.Unchanged, reload.Try(true, out var fresh, out string? problem, out _));
        Assert.IsNull(fresh);
        Assert.IsNull(problem, "a file that loads is not a broken one");
        Assert.IsFalse(reload.Waiting);
        Assert.AreEqual(Old, File.ReadAllText(path));
        Assert.HasCount(1, Backups());
        Assert.IsFalse(File.Exists(path + ".broken"));

        // Seen again: the same file is not kept twice.
        Assert.AreEqual(Reloaded.Unchanged, Try(reload, first: true));
        Assert.HasCount(1, Backups());
    }

    // A file from before 1.0 that cannot be kept is never written over.
    [TestMethod]
    public void AFileFromBefore10ThatCannotBeKeptIsLeftAsItIs()
    {
        var reload = new ConfigReload(path, () => new Config { IdleMinutes = 7, Welcomed = true });
        File.WriteAllText(path, Old);
        WholeFile.Midway = file =>
        {
            if (file.Contains(FreshStart.BackupSuffix, StringComparison.Ordinal)) throw new IOException("the disk is full");
        };
        try
        {
            Assert.AreEqual(Reloaded.Unchanged, Try(reload, first: true));
        }
        finally
        {
            WholeFile.Midway = null;
        }
        Assert.AreEqual(Old, File.ReadAllText(path));
        Assert.IsEmpty(Backups());
    }

    [TestMethod]
    public void ReloadingA10FileWritesNothing()
    {
        string text = WriteOutside(new Config { IdleMinutes = 7, MusicVideo = Effect.Extra(60) });
        var written = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);
        var reload = new ConfigReload(path, () => new Config());
        Assert.AreEqual(Reloaded.Loaded, reload.Try(true, out var fresh, out _, out _));
        Assert.AreEqual(7, fresh!.IdleMinutes);
        Assert.AreEqual(Effect.Extra(60), fresh.MusicVideo);
        Assert.AreEqual(text, File.ReadAllText(path));
        Assert.AreEqual(written, File.GetLastWriteTimeUtc(path));
        Assert.IsEmpty(Backups());
    }

    [TestMethod]
    public void ASaveDuringTheTriesKnowsTheFileHasNotLoaded()
    {
        var reload = new ConfigReload(path, () => new Config());
        Assert.IsFalse(reload.Waiting);
        File.WriteAllText(path, "{ this is not json");
        Try(reload, first: true);
        Assert.IsTrue(reload.Waiting);
        // A save that failed leaves the file as it was.
        reload.Saved(null);
        Assert.IsTrue(reload.Waiting);
        // After the app's own save the file holds its settings, so a second save keeps nothing.
        reload.Saved(new Config { IdleMinutes = 7 }.Save(path));
        Assert.IsFalse(reload.Waiting);
        File.WriteAllText(path, "");
        Try(reload, first: true);
        Assert.IsTrue(reload.Waiting);
        WriteOutside(new Config { IdleMinutes = 9 });
        Assert.AreEqual(Reloaded.Loaded, Try(reload));
        Assert.IsFalse(reload.Waiting);
        File.WriteAllText(path, "{ this is not json");
        Try(reload, first: true);
        for (int i = 2; i < ConfigReload.Tries; i++) Try(reload);
        Assert.AreEqual(Reloaded.Broken, Try(reload));
        Assert.IsFalse(reload.Waiting);
        // Held, or gone for a moment, the file has not loaded either.
        using (Hold())
        {
            Try(reload, first: true);
            Assert.IsTrue(reload.Waiting);
        }
        File.Delete(path);
        Try(reload, first: true);
        Assert.IsTrue(reload.Waiting);
    }
}
