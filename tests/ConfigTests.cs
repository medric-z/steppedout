namespace SteppedOut.Tests;

[TestClass]
public class ConfigTests
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
    public void Cleanup() => Directory.Delete(dir, recursive: true);

    [TestMethod]
    public void DefaultsAreTheDocumentedOnes()
    {
        var c = new Config();
        Assert.AreEqual(15, c.IdleMinutes);
        Assert.AreEqual(15, c.GraceSeconds);
        Assert.AreEqual(512, c.LogMaxKilobytes);
        Assert.IsTrue(c.LockWhenAway);
        Assert.IsFalse(c.MuteWhenAway);
        Assert.IsFalse(c.ScreensOffWhenAway);
        Assert.IsTrue(c.ShowTimeAway);
        Assert.AreEqual("auto", c.Language);
        Assert.IsFalse(c.Welcomed);
    }

    [TestMethod]
    public void MissingFileIsCreatedWithDefaults()
    {
        string nested = Path.Combine(dir, "sub", "config.json");
        var c = Config.Load(nested, out string? problem);
        Assert.IsNull(problem);
        Assert.IsTrue(File.Exists(nested));
        Assert.AreEqual(15, c.IdleMinutes);
        string text = File.ReadAllText(nested);
        StringAssert.Contains(text, "\"configVersion\": 1");
        StringAssert.Contains(text, "\"idleMinutes\": 15");
        StringAssert.Contains(text, "\"musicVideo\": \"never\"");
        Assert.DoesNotContain("whilePlaying", text, "the settings 1.0 replaced are never written");
        Assert.DoesNotContain("suppressWhile", text);
    }

    [TestMethod]
    public void TheFileStaysReadable()
    {
        new Config { RunWhenAway = @"C:\أدوات\away & back.bat", LockShortcut = "Ctrl+Alt+L" }.Save(path);
        string text = File.ReadAllText(path);
        StringAssert.Contains(text, "\"lockShortcut\": \"Ctrl+Alt+L\"");
        StringAssert.Contains(text, @"""runWhenAway"": ""C:\\أدوات\\away & back.bat""");
        Assert.AreEqual(@"C:\أدوات\away & back.bat", Config.Load(path, out _).RunWhenAway);
    }

    // The settings that kept their names; the ones 1.0 added have their own round trip below.
    [TestMethod]
    public void RoundTripsThroughTheFile()
    {
        new Config
        {
            IdleMinutes = 2.5,
            GraceSeconds = 3,
            LogMaxKilobytes = 128,
            AudioPeakThreshold = 0.05,
            AudioSilenceSeconds = 9,
            JitterFilter = false,
            JitterPixels = 9,
            LockWhenAway = false,
            MuteWhenAway = true,
            ScreensOffWhenAway = true,
            RunWhenAway = @"C:\tools\away.bat",
            RunWhenBack = @"C:\tools\back.lnk",
            ShowTimeAway = false,
            Language = "ar",
            Welcomed = true,
        }.Save(path);
        var c = Config.Load(path, out string? problem);
        Assert.IsNull(problem);
        Assert.AreEqual(2.5, c.IdleMinutes);
        Assert.AreEqual(3, c.GraceSeconds);
        Assert.AreEqual(128, c.LogMaxKilobytes);
        Assert.AreEqual(0.05, c.AudioPeakThreshold);
        Assert.AreEqual(9, c.AudioSilenceSeconds);
        Assert.IsFalse(c.JitterFilter);
        Assert.AreEqual(9, c.JitterPixels);
        Assert.IsFalse(c.LockWhenAway);
        Assert.IsTrue(c.MuteWhenAway);
        Assert.IsTrue(c.ScreensOffWhenAway);
        Assert.AreEqual(@"C:\tools\away.bat", c.RunWhenAway);
        Assert.AreEqual(@"C:\tools\back.lnk", c.RunWhenBack);
        Assert.IsFalse(c.ShowTimeAway);
        Assert.AreEqual("ar", c.Language);
        Assert.IsTrue(c.Welcomed);
    }

    [TestMethod]
    public void CommentsTrailingCommasAndUnknownKeysAreTolerated()
    {
        File.WriteAllText(path, "{\n  // hand edited\n  \"configVersion\": 1,\n  \"idleMinutes\": 3,\n  \"graceSeconds\": 7,\n  \"somethingNew\": true,\n}\n");
        var c = Config.Load(path, out string? problem, out string? warning);
        Assert.IsNull(problem);
        Assert.AreEqual(3, c.IdleMinutes);
        Assert.AreEqual(7, c.GraceSeconds);
        StringAssert.Contains(warning, "'somethingNew'");
    }

    [TestMethod]
    public void NamesAreReadWhateverTheirCase()
    {
        File.WriteAllText(path, "{ \"CONFIGVERSION\": 1, \"IdleMinutes\": 5, \"LOCKWHENAWAY\": false, \"MusicVideo\": \"later 30\" }");
        var c = Config.Load(path, out string? problem, out string? warning);
        Assert.IsNull(problem);
        Assert.IsNull(warning);
        Assert.IsNull(c.Old, "the version is found whatever its case");
        Assert.AreEqual(5, c.IdleMinutes);
        Assert.IsFalse(c.LockWhenAway);
        Assert.AreEqual(Effect.Extra(30), c.MusicVideo);
    }

    [TestMethod]
    public void AMisspelledNameIsAWarningAndTheRestLoads()
    {
        File.WriteAllText(path, "{ \"configVersion\": 1, \"clozeWhenAway\": [\"obs64\"], \"idleMinutes\": 3, \"welcomed\": true, \"suppressWhileAudioPlaying\": true }");
        var c = Config.Load(path, out string? problem, out string? warning);
        Assert.IsNull(problem, "a typo must not get the whole file set aside");
        Assert.AreEqual(3, c.IdleMinutes);
        Assert.IsTrue(c.Welcomed);
        Assert.IsNotNull(warning);
        StringAssert.Contains(warning, "'clozeWhenAway'");
        Assert.DoesNotContain("welcomed", warning);
        StringAssert.Contains(warning, "'suppressWhileAudioPlaying'", "a name from before 1.0 is as unknown as a typo (D50)");
    }

    [TestMethod]
    public void MistakesNameTheLineCountedFromOne()
    {
        File.WriteAllText(path, "{\n  \"configVersion\": 1,\n  \"idleMinutes\": \"5\"\n}\n");
        Config.Load(path, out string? problem);
        StringAssert.Contains(problem, "line 3 (idleMinutes): ");
        Assert.DoesNotContain("LineNumber", problem!);

        File.WriteAllText(path, "{\n  \"runWhenAway\": \"C:\\Users\\me\\away.lnk\"\n}\n");
        Config.Load(path, out problem);
        StringAssert.Contains(problem, "line 2 (runWhenAway): ");

        File.WriteAllText(path, "{\n  \"graceSeconds\": \"20\",\n  \"configVersion\": 1\n}\n");
        Config.Load(path, out problem);
        StringAssert.Contains(problem, "line 2 (graceSeconds): expected a number");

        File.WriteAllText(path, "{ this is not json");
        Config.Load(path, out problem);
        StringAssert.Contains(problem, ": line 1: ");
    }

    [TestMethod]
    public void DecimalsAndHugeNumbersInWholeNumberSettingsAreRoundedAndClamped()
    {
        File.WriteAllText(path, "{ \"configVersion\": 1, \"graceSeconds\": 20.0, \"jitterPixels\": 7.5, \"audioSilenceSeconds\": 99999999999, \"logMaxKilobytes\": -1e20 }");
        var c = Config.Load(path, out string? problem);
        Assert.IsNull(problem);
        Assert.AreEqual(20, c.GraceSeconds);
        Assert.AreEqual(8, c.JitterPixels);
        Assert.AreEqual(600, c.AudioSilenceSeconds);
        Assert.AreEqual(64, c.LogMaxKilobytes);

        File.WriteAllText(path, "{ \"configVersion\": 1, \"graceSeconds\": \"20\" }");
        Config.Load(path, out problem);
        Assert.IsNotNull(problem, "a number in quotes is still a mistake the log reports");
    }

    [TestMethod]
    public void BrokenFileIsReportedAndLeftAlone()
    {
        File.WriteAllText(path, "{ this is not json");
        var c = Config.Load(path, out string? problem);
        Assert.IsNotNull(problem);
        Assert.AreEqual(15, c.IdleMinutes);
        Assert.AreEqual("{ this is not json", File.ReadAllText(path));
    }

    [TestMethod]
    public void AFileThatCannotBeOpenedSaysWhy()
    {
        new Config().Save(path);
        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        Config.Load(path, out string? problem);
        StringAssert.StartsWith(problem, $"could not read {path}: ");
        Assert.IsFalse(string.IsNullOrWhiteSpace(problem![$"could not read {path}: ".Length..]), problem);
    }

    [TestMethod]
    public void AReasonIsNeverEmpty()
    {
        Assert.AreEqual("disk gone", Config.Reason(new IOException("disk gone")));
        Assert.AreEqual("IOException 0x80131620", Config.Reason(new IOException("")));
        StringAssert.StartsWith(Config.Reason(new UnauthorizedAccessException(" ")), "UnauthorizedAccessException 0x");
        StringAssert.StartsWith(Config.Reason(new System.Text.Json.JsonException("")), "JsonException 0x");
    }

    [TestMethod]
    public void AFileAnotherProgramHasOpenForWritingStillLoads()
    {
        new Config { IdleMinutes = 7 }.Save(path);
        // As an antivirus or a backup tool may hold a new file, sharing it.
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        var c = Config.Load(path, out string? problem);
        Assert.IsNull(problem);
        Assert.AreEqual(7, c.IdleMinutes);
    }

    [TestMethod]
    public void OutOfRangeValuesAreClamped()
    {
        var c = new Config
        {
            IdleMinutes = -5,
            GraceSeconds = 99999,
            LogMaxKilobytes = 1,
            JitterPixels = 0,
            Language = "fr",
            RunWhenAway = "  \"C:\\a b\\x.exe\"  ",
        };
        c.Clamp();
        Assert.AreEqual(1, c.JitterPixels);
        Assert.AreEqual(0.05, c.IdleMinutes);
        Assert.AreEqual(600, c.GraceSeconds);
        Assert.AreEqual(64, c.LogMaxKilobytes);
        Assert.AreEqual("auto", c.Language);
        Assert.AreEqual(@"C:\a b\x.exe", c.RunWhenAway);
    }

    [TestMethod]
    public void SameAsComparesEverySetting()
    {
        var c = new Config { CloseWhenAway = { "notepad" } };
        Assert.IsTrue(c.SameAs(c.Copy()));
        var language = c.Copy();
        language.Language = "en";
        Assert.IsFalse(c.SameAs(language));
        var apps = c.Copy();
        apps.CloseWhenAway.Add("mspaint");
        Assert.IsFalse(c.SameAs(apps));
        var rules = c.Copy();
        rules.AppRules.Add(new AppRule("notepad", Effect.Never));
        Assert.IsFalse(c.SameAs(rules));
        var idle = c.Copy();
        idle.IdleMinutes = 15.5;
        Assert.IsFalse(c.SameAs(idle));
        var music = c.Copy();
        music.MusicVideo = Effect.Extra(60);
        Assert.IsFalse(c.SameAs(music));
    }

    // The names 1.0 replaced mean nothing in a 1.0 file (D50): each is reported as unknown, none
    // changes a setting, and a save leaves them out.
    [TestMethod]
    public void TheNamesFromBefore10AreUnknownInA10File()
    {
        string[] names = { "whilePlaying", "whileFullscreen", "whileOnCall", "moreTimeMinutes", "neverLockWhileRunning", "pauseMedia", "breakReminderMinutes", "suppressWhileFullscreen" };
        File.WriteAllText(path, "{ \"configVersion\": 1, \"whilePlaying\": \"lockAnyway\", \"whileFullscreen\": \"moreTime\", \"whileOnCall\": \"moreTime\", "
            + "\"moreTimeMinutes\": 5, \"neverLockWhileRunning\": [\"notepad\"], \"pauseMedia\": false, \"breakReminderMinutes\": 30, \"suppressWhileFullscreen\": true }");
        var c = Config.Load(path, out string? problem, out string? warning);
        Assert.IsNull(problem);
        Assert.IsNull(c.Old, "a 1.0 file with old names in it is still a 1.0 file");
        foreach (string name in names) StringAssert.Contains(warning, $"'{name}'");
        Assert.IsTrue(new Config().SameAs(c));

        c.Save(path);
        string text = File.ReadAllText(path);
        foreach (string name in names) Assert.DoesNotContain(name, text, $"{name} is written");
        Assert.IsFalse(File.Exists(path + FreshStart.BackupSuffix), "a 1.0 file is never kept as one from before 1.0");
    }

    // A brand-new install, with no settings file yet, starts with break reminders on every 2 hours,
    // and writes them down; the sounds stay off.
    [TestMethod]
    public void ANewInstallStartsWithBreakRemindersEveryTwoHours()
    {
        var c = Config.Load(path, out string? problem);
        Assert.IsNull(problem);
        Assert.IsTrue(c.BreakReminders);
        Assert.AreEqual(120, c.BreakMinutes);
        Assert.AreEqual(ActionSound.Off, c.SoundWhenAway);
        Assert.AreEqual(ActionSound.Off, c.SoundWhenBack);
        Assert.AreEqual(BreakSound.Off, c.BreakSound);
        Assert.IsFalse(c.EyeBreaks, "eye breaks stay as they are");
        Assert.AreEqual(10, c.SnoozeMinutes, "snooze stays as it is");
        Assert.IsTrue(c.SameAs(Config.FirstStart()));

        var again = Config.Load(path, out problem);
        Assert.IsNull(problem);
        Assert.IsTrue(again.BreakReminders, "the file written says so");
        Assert.AreEqual(120, again.BreakMinutes);
    }

    // An existing file keeps what it has: one written before the keys existed, or with reminders
    // off, never gets them turned on.
    [TestMethod]
    public void AnExistingFileNeverGetsBreakRemindersTurnedOn()
    {
        File.WriteAllText(path, "{ \"configVersion\": 1, \"idleMinutes\": 20 }");
        var c = Config.Load(path, out string? problem);
        Assert.IsNull(problem);
        Assert.IsFalse(c.BreakReminders, "a file from before the key");
        Assert.AreEqual(60, c.BreakMinutes);

        File.WriteAllText(path, "{ \"configVersion\": 1, \"breakReminders\": false, \"breakMinutes\": 45 }");
        c = Config.Load(path, out problem);
        Assert.IsNull(problem);
        Assert.IsFalse(c.BreakReminders);
        Assert.AreEqual(45, c.BreakMinutes);
    }

    // The sounds are off however the settings start: plain defaults or a brand-new install.
    [TestMethod]
    public void SoundsAreOffByDefault()
    {
        foreach (var c in new[] { new Config(), Config.FirstStart() })
        {
            Assert.AreEqual(ActionSound.Off, c.SoundWhenAway);
            Assert.AreEqual(ActionSound.Off, c.SoundWhenBack);
            Assert.AreEqual(BreakSound.Off, c.BreakSound);
        }
    }

    [TestMethod]
    public void NewSettingsHaveTheDocumentedDefaults()
    {
        var c = new Config();
        Assert.AreEqual(Config.CurrentVersion, c.ConfigVersion);
        Assert.AreEqual(Effect.Never, c.MusicVideo);
        Assert.AreEqual(Effect.Never, c.FullScreen);
        Assert.AreEqual(Effect.Never, c.OnCall);
        Assert.AreEqual(Effect.None, c.OnBattery);
        Assert.IsEmpty(c.AppRules);
        Assert.AreEqual(MediaOnLock.PauseUntilBack, c.MediaOnLock);
        Assert.IsTrue(c.CountController);
        Assert.AreEqual(ActionSound.Off, c.SoundWhenAway);
        Assert.AreEqual(ActionSound.Off, c.SoundWhenBack);
        Assert.IsEmpty(c.OpenWhenAway);
        Assert.IsEmpty(c.OpenWhenBack);
        Assert.IsEmpty(c.CloseWhenAway);
        Assert.IsEmpty(c.CloseWhenBack);
        Assert.IsFalse(c.BreakReminders);
        Assert.AreEqual(60, c.BreakMinutes);
        Assert.AreEqual(BreakSound.Off, c.BreakSound);
        Assert.IsFalse(c.EyeBreaks);
        Assert.AreEqual(10, c.SnoozeMinutes);
        Assert.AreEqual("", c.LockShortcut, "no shortcut until one is picked");
        Assert.AreEqual(ThemeChoice.System, c.Theme);
        Assert.AreEqual(TextSize.System, c.TextSize);
        Assert.IsFalse(c.MinutesOnTrayIcon);
        Assert.IsNull(c.NotificationPlace);
        Assert.AreEqual(0, c.SettingsTab, "the settings open on Locking the first time");
        Assert.AreEqual(WhenLocking.Lock, c.WhenLocking, "only the lock until another is picked");
    }

    // "At the lock time" is read by its name in any case, and a value it does not know, or a
    // number out of the enum set in code, is the default.
    [TestMethod]
    public void WhenLockingIsReadLeniently()
    {
        foreach (var (written, expected) in new[]
        {
            ("\"lock\"", WhenLocking.Lock),
            ("\"lockScreenOff\"", WhenLocking.LockScreenOff),
            ("\"LockSleep\"", WhenLocking.LockSleep),
            ("\"lockscreenoff\"", WhenLocking.LockScreenOff),
        })
        {
            File.WriteAllText(path, $"{{ \"configVersion\": 1, \"whenLocking\": {written} }}");
            var c = Config.Load(path, out string? problem, out string? warning);
            Assert.IsNull(problem);
            Assert.IsNull(warning, written);
            Assert.AreEqual(expected, c.WhenLocking, written);
        }
        var odd = new Config { WhenLocking = (WhenLocking)7 };
        odd.Clamp();
        Assert.AreEqual(WhenLocking.Lock, odd.WhenLocking);
    }

    [TestMethod]
    public void TheDefaultFileHoldsEverySettingButTheUnpickedPlace()
    {
        string[] expected =
        {
            "configVersion", "language", "idleMinutes", "graceSeconds", "lockWhenAway", "whenLocking", "musicVideo", "fullScreen", "onCall",
            "onBattery", "appRules", "countController", "mediaOnLock", "muteWhenAway", "screensOffWhenAway", "soundWhenAway",
            "soundWhenAwayFile", "openWhenAway", "closeWhenAway", "runWhenAway", "showTimeAway", "soundWhenBack", "soundWhenBackFile",
            "openWhenBack", "closeWhenBack", "runWhenBack", "breakReminders", "breakMinutes", "breakSound", "breakSoundFile", "eyeBreaks", "snoozeMinutes", "lockShortcut", "theme",
            "textSize", "minutesOnTrayIcon", "settingsTab", "jitterFilter", "jitterPixels", "audioPeakThreshold",
            "audioSilenceSeconds", "logMaxKilobytes", "welcomed",
        };
        new Config().Save(path);
        using var file = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        CollectionAssert.AreEquivalent(expected, file.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [TestMethod]
    public void EveryNewSettingRoundTripsThroughTheFile()
    {
        var rules = new List<AppRule>
        {
            new("notepad.exe", Effect.Never, AppWhen.Running, @"C:\Windows\notepad.exe"),
            new("mspaint", Effect.Extra(30), AppWhen.OnScreen),
            new("CalculatorApp", Effect.Sooner(25), Aumid: "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"),
            new("charmap", Effect.Cap(10)),
        };
        new Config
        {
            MusicVideo = Effect.Extra(45),
            FullScreen = Effect.None,
            OnCall = Effect.Extra(30),
            OnBattery = Effect.Sooner(50),
            AppRules = rules,
            MediaOnLock = MediaOnLock.KeepPlaying,
            CountController = false,
            SoundWhenAway = ActionSound.Own,
            SoundWhenAwayFile = "C:/Sounds/rain.wav",
            SoundWhenBack = ActionSound.SteppedOut,
            SoundWhenBackFile = "C:/Sounds/birds.wav",
            OpenWhenAway = { @"C:\Windows\notepad.exe" },
            OpenWhenBack = { @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" },
            CloseWhenAway = { "notepad" },
            CloseWhenBack = { "mspaint", "charmap" },
            BreakReminders = true,
            BreakMinutes = 45,
            BreakSound = BreakSound.Tide,
            EyeBreaks = true,
            SnoozeMinutes = 30,
            LockShortcut = "Ctrl+Alt+K",
            Theme = ThemeChoice.Dark,
            TextSize = TextSize.Large,
            MinutesOnTrayIcon = true,
            NotificationPlace = NotePosition.TopLeft,
            SettingsTab = 3,
            WhenLocking = WhenLocking.LockScreenOff,
        }.Save(path);
        var c = Config.Load(path, out string? problem, out string? warning);
        Assert.IsNull(problem);
        Assert.IsNull(warning);
        Assert.AreEqual(Config.CurrentVersion, c.ConfigVersion);
        Assert.AreEqual(Effect.Extra(45), c.MusicVideo);
        Assert.AreEqual(Effect.None, c.FullScreen);
        Assert.AreEqual(Effect.Extra(30), c.OnCall);
        Assert.AreEqual(Effect.Sooner(50), c.OnBattery);
        CollectionAssert.AreEqual(rules, c.AppRules);
        Assert.AreEqual(MediaOnLock.KeepPlaying, c.MediaOnLock);
        Assert.IsFalse(c.CountController);
        Assert.AreEqual(ActionSound.Own, c.SoundWhenAway);
        Assert.AreEqual("C:/Sounds/rain.wav", c.SoundWhenAwayFile);
        Assert.AreEqual(ActionSound.SteppedOut, c.SoundWhenBack);
        Assert.AreEqual("C:/Sounds/birds.wav", c.SoundWhenBackFile, "the file is kept while another sound is chosen");
        Assert.AreEqual("", c.BreakSoundFile);
        CollectionAssert.AreEqual(new[] { @"C:\Windows\notepad.exe" }, c.OpenWhenAway);
        CollectionAssert.AreEqual(new[] { @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" }, c.OpenWhenBack);
        CollectionAssert.AreEqual(new[] { "notepad" }, c.CloseWhenAway);
        CollectionAssert.AreEqual(new[] { "mspaint", "charmap" }, c.CloseWhenBack);
        Assert.IsTrue(c.BreakReminders);
        Assert.AreEqual(45, c.BreakMinutes);
        Assert.AreEqual(BreakSound.Tide, c.BreakSound);
        Assert.IsTrue(c.EyeBreaks);
        Assert.AreEqual(30, c.SnoozeMinutes);
        Assert.AreEqual("Ctrl+Alt+K", c.LockShortcut);
        Assert.AreEqual(ThemeChoice.Dark, c.Theme);
        Assert.AreEqual(TextSize.Large, c.TextSize);
        Assert.IsTrue(c.MinutesOnTrayIcon);
        Assert.AreEqual(NotePosition.TopLeft, c.NotificationPlace);
        Assert.AreEqual(3, c.SettingsTab);
        Assert.AreEqual(WhenLocking.LockScreenOff, c.WhenLocking);

        string text = File.ReadAllText(path);
        foreach (string written in new[]
        {
            "\"musicVideo\": \"later 45\"", "\"fullScreen\": \"none\"", "\"onBattery\": \"sooner 50\"", "\"mediaOnLock\": \"keepPlaying\"",
            "\"soundWhenAway\": \"own\"", "\"soundWhenBack\": \"steppedOut\"", "\"breakSound\": \"tide\"", "\"lockShortcut\": \"Ctrl+Alt+K\"", "\"theme\": \"dark\"",
            "\"textSize\": \"large\"", "\"notificationPlace\": \"topLeft\"", "\"settingsTab\": 3", "\"whenLocking\": \"lockScreenOff\"", "\"app\": \"notepad.exe\"",
            "\"effect\": \"later 30\"", "\"when\": \"onScreen\"", "\"effect\": \"always 10\"", "\"aumid\": \"Microsoft.WindowsCalculator_8wekyb3d8bbwe!App\"",
        })
        {
            StringAssert.Contains(text, written);
        }

        var battery = new Config { OnBattery = Effect.Cap(10) };
        battery.Save(path);
        StringAssert.Contains(File.ReadAllText(path), "\"onBattery\": \"always 10\"");
        Assert.AreEqual(Effect.Cap(10), Config.Load(path, out _).OnBattery);
    }

    // Nothing in a file from before 1.0 is read (D50): it loads as the defaults, the welcome
    // among them, and says where it comes from.
    [TestMethod]
    public void AFileFromBefore10LoadsAsTheDefaults()
    {
        foreach (var (text, shape) in new[]
        {
            ("{ \"idleMinutes\": 30, \"welcomed\": true, \"language\": \"ar\" }", "0.x"),
            ("{ \"idleMinutes\": 30, \"suppressWhileFullscreen\": true }", "0.1"),
            ("{ \"configVersion\": 0, \"idleMinutes\": 30, \"welcomed\": true }", "configVersion 0"),
            ("{ \"configVersion\": 0.5, \"idleMinutes\": \"thirty\" }", "configVersion 0.5"),
        })
        {
            File.WriteAllText(path, text);
            var c = Config.Load(path, out string? problem, out string? warning);
            Assert.IsNull(problem, text);
            Assert.IsNull(warning, "nothing in it is read, so nothing in it is a mistake");
            Assert.IsTrue(new Config().SameAs(c), text);
            Assert.IsFalse(c.Welcomed);
            Assert.AreEqual(Config.CurrentVersion, c.ConfigVersion);
            Assert.AreEqual(shape, c.Old?.Shape, text);
            Assert.AreEqual(text, c.Old!.Text);
            Assert.AreEqual(text, File.ReadAllText(path), "loading changes nothing on disk");
        }

        File.WriteAllText(path, "{ \"configVersion\": 1, \"idleMinutes\": 30 }");
        var current = Config.Load(path, out _);
        Assert.IsNull(current.Old);
        Assert.AreEqual(30, current.IdleMinutes);
        File.WriteAllText(path, "{ \"configVersion\": 2, \"idleMinutes\": 30 }");
        Assert.IsNull(Config.Load(path, out _).Old, "a later version is no file from before 1.0");
    }

    [TestMethod]
    public void UntilAPlaceIsPickedNotesKeepToTheShellsSideAndNothingIsSaved()
    {
        var rightToLeft = Config.Parse("{ \"idleMinutes\": 5 }", path, shellRightToLeft: true, out string? problem, out _);
        Assert.IsNull(problem);
        Assert.IsNull(rightToLeft.NotificationPlace);
        Assert.AreEqual(NotePosition.BottomLeft, rightToLeft.NotificationPlaceInUse);
        Assert.AreEqual(NotePosition.BottomLeft, rightToLeft.Copy().NotificationPlaceInUse);
        var leftToRight = Config.Parse("{ \"idleMinutes\": 5 }", path, shellRightToLeft: false, out _, out _);
        Assert.AreEqual(NotePosition.BottomRight, leftToRight.NotificationPlaceInUse);
        Assert.AreEqual(Lang.ShellIsRightToLeft() ? NotePosition.BottomLeft : NotePosition.BottomRight, new Config().NotificationPlaceInUse);

        rightToLeft.Save(path);
        Assert.DoesNotContain("notificationPlace", File.ReadAllText(path));
        Assert.IsTrue(rightToLeft.SameAs(leftToRight.Copy()), "an unpicked place is not part of the settings");

        rightToLeft.NotificationPlace = NotePosition.TopRight;
        rightToLeft.Save(path);
        StringAssert.Contains(File.ReadAllText(path), "\"notificationPlace\": \"topRight\"");
        var picked = Config.Parse(File.ReadAllText(path), path, shellRightToLeft: true, out _, out _);
        Assert.AreEqual(NotePosition.TopRight, picked.NotificationPlaceInUse);
    }

    [TestMethod]
    [DataRow("countController", "\"yes\"")]
    [DataRow("closeWhenAway", "\"notepad\"")]
    [DataRow("breakMinutes", "\"abc\"")]
    [DataRow("appRules", "[{\"app\": 5}]")]
    [DataRow("musicVideo", "30")]
    public void EachOfTheseMistakesGivesTheDefaultAndAWarning(string key, string value) => BadValueGivesTheDefault(key, value);

    // The sounds of earlier builds (click, chime, soft chime) were dropped; a file that still names one
    // gets SteppedOut's own sound of that set, quietly, as a choice the app itself once wrote.
    [TestMethod]
    [DataRow("soundWhenAway", "click")]
    [DataRow("soundWhenAway", "chime")]
    [DataRow("soundWhenBack", "Chime")]
    [DataRow("soundWhenBack", "CLICK")]
    [DataRow("breakSound", "softChime")]
    [DataRow("breakSound", "softchime")]
    public void AnOldSoundNameGivesSteppedOutsOwnSound(string key, string old)
    {
        File.WriteAllText(path, $"{{ \"configVersion\": 1, \"{key}\": \"{old}\" }}");
        var c = Config.Load(path, out string? problem, out string? warning);
        Assert.IsNull(problem);
        Assert.IsNull(warning, "an old sound name is not a mistake to report");
        Assert.AreEqual(key == "breakSound" ? BreakSound.SteppedOut : BreakSound.Off, c.BreakSound);
        Assert.AreEqual(key == "soundWhenAway" ? ActionSound.SteppedOut : ActionSound.Off, c.SoundWhenAway);
        Assert.AreEqual(key == "soundWhenBack" ? ActionSound.SteppedOut : ActionSound.Off, c.SoundWhenBack);
        c.Save(path);
        StringAssert.Contains(File.ReadAllText(path), $"\"{key}\": \"steppedOut\"", "the next save writes the new name");
    }

    [TestMethod]
    public void EverySoundChoiceReadsBackByItsName()
    {
        foreach (var sound in Enum.GetValues<ActionSound>())
        {
            File.WriteAllText(path, $"{{ \"configVersion\": 1, \"soundWhenAway\": \"{Lenient.Name(sound)}\", \"soundWhenAwayFile\": \"C:/a.wav\", \"soundWhenBack\": \"{Lenient.Name(sound)}\", \"soundWhenBackFile\": \"C:/b.wav\" }}");
            var c = Config.Load(path, out _, out string? warning);
            Assert.IsNull(warning, sound.ToString());
            Assert.AreEqual(sound, c.SoundWhenAway);
            Assert.AreEqual(sound, c.SoundWhenBack);
        }
        foreach (var sound in Enum.GetValues<BreakSound>())
        {
            File.WriteAllText(path, $"{{ \"configVersion\": 1, \"breakSound\": \"{Lenient.Name(sound)}\", \"breakSoundFile\": \"C:/c.wav\" }}");
            var c = Config.Load(path, out _, out string? warning);
            Assert.IsNull(warning, sound.ToString());
            Assert.AreEqual(sound, c.BreakSound);
        }
    }

    // Your own sound with no file is SteppedOut's own, which is what it would play.
    [TestMethod]
    public void YourOwnSoundWithNoFileIsSteppedOutsOwn()
    {
        File.WriteAllText(path, "{ \"configVersion\": 1, \"soundWhenAway\": \"own\", \"soundWhenBack\": \"own\", \"soundWhenBackFile\": \"  \\\"C:/b.wav\\\" \", \"breakSound\": \"own\" }");
        var c = Config.Load(path, out _, out _);
        Assert.AreEqual(ActionSound.SteppedOut, c.SoundWhenAway);
        Assert.AreEqual(ActionSound.Own, c.SoundWhenBack);
        Assert.AreEqual("C:/b.wav", c.SoundWhenBackFile, "quotes and spaces around the path go");
        Assert.AreEqual(BreakSound.SteppedOut, c.BreakSound);
    }

    [TestMethod]
    [DataRow("configVersion", "\"one\"")]
    [DataRow("musicVideo", "\"sometimes\"")]
    [DataRow("musicVideo", "\"sooner 50\"")]
    [DataRow("musicVideo", "\"later\"")]
    [DataRow("fullScreen", "true")]
    [DataRow("onCall", "[\"never\"]")]
    [DataRow("onBattery", "\"later 30\"")]
    [DataRow("onBattery", "\"never\"")]
    [DataRow("appRules", "{\"app\": \"notepad\"}")]
    [DataRow("appRules", "\"notepad\"")]
    [DataRow("mediaOnLock", "\"pause\"")]
    [DataRow("mediaOnLock", "1")]
    [DataRow("countController", "1")]
    [DataRow("soundWhenAway", "\"loud\"")]
    [DataRow("soundWhenBack", "null")]
    [DataRow("openWhenAway", "5")]
    [DataRow("openWhenBack", "{\"path\": \"notepad.exe\"}")]
    [DataRow("closeWhenBack", "true")]
    [DataRow("breakReminders", "\"on\"")]
    [DataRow("breakMinutes", "null")]
    [DataRow("breakSound", "\"chime\"")]
    [DataRow("breakSound", "\"click\"")]
    [DataRow("soundWhenAway", "\"softChime\"")]
    [DataRow("soundWhenAwayFile", "5")]
    [DataRow("breakSoundFile", "[]")]
    [DataRow("eyeBreaks", "1")]
    [DataRow("snoozeMinutes", "\"10\"")]
    [DataRow("lockShortcut", "5")]
    [DataRow("lockShortcut", "null")]
    [DataRow("theme", "\"sepia\"")]
    [DataRow("theme", "\"2\"")]
    [DataRow("textSize", "3")]
    [DataRow("whenLocking", "\"hibernate\"")]
    [DataRow("whenLocking", "1")]
    [DataRow("whenLocking", "true")]
    [DataRow("settingsTab", "\"2\"")]
    [DataRow("settingsTab", "true")]
    [DataRow("minutesOnTrayIcon", "\"false\"")]
    [DataRow("notificationPlace", "\"middle\"")]
    [DataRow("notificationPlace", "2")]
    public void ABadValueInANewSettingGivesTheDefaultAndAWarning(string key, string value) => BadValueGivesTheDefault(key, value);

    // The rest of the file still loads, nothing is set aside, and the log names the setting.
    void BadValueGivesTheDefault(string key, string value)
    {
        string version = key == "configVersion" ? "" : "\"configVersion\": 1, ";
        File.WriteAllText(path, $"{{ {version}\"idleMinutes\": 7, \"{key}\": {value} }}");
        var c = Config.Load(path, out string? problem, out string? warning);
        Assert.IsNull(problem, "the file is not set aside as broken");
        Assert.AreEqual(7, c.IdleMinutes);
        StringAssert.Contains(warning, $"{key} (");
        if (key == "configVersion")
        {
            // A version that is there but is no number is a hand edit to a 1.0 file, not a file
            // from before 1.0: the rest of it loads, and it is not kept or replaced (D50).
            Assert.AreEqual(Config.CurrentVersion, c.ConfigVersion);
            Assert.IsNull(c.Old);
            Assert.AreEqual(FreshOutcome.NotNeeded, FreshStart.Apply(c, path, persist: true).Outcome);
        }
        Assert.IsTrue(c.SameAs(new Config { IdleMinutes = 7 }), $"{key} keeps its default");
    }

    [TestMethod]
    public void AReloadWithMistakesInNewSettingsStillLoads()
    {
        new Config().Save(path);
        var reload = new ConfigReload(path, () => new Config());
        File.WriteAllText(path, "{ \"configVersion\": 1, \"idleMinutes\": 7, \"countController\": \"yes\", \"closeWhenAway\": \"notepad\", \"breakMinutes\": \"abc\", "
            + "\"appRules\": [{\"app\": 5}], \"musicVideo\": 30 }");
        Assert.AreEqual(Reloaded.Loaded, reload.Try(first: true, out var fresh, out string? problem, out string? warning));
        Assert.IsNull(problem);
        Assert.AreEqual(7, fresh!.IdleMinutes);
        foreach (string key in new[] { "countController", "closeWhenAway", "breakMinutes", "appRules", "musicVideo" })
        {
            StringAssert.Contains(warning, key + " (");
        }
        Assert.IsFalse(File.Exists(path + ".broken"));
    }

    [TestMethod]
    public void ABadItemInAListLeavesOutOnlyThatItem()
    {
        File.WriteAllText(path, """
            {
              "configVersion": 1,
              "closeWhenAway": ["notepad", 5, null, {"x": 1}, "mspaint"],
              "appRules": [
                { "app": "notepad.exe", "effect": "never" },
                { "app": 5, "effect": "never" },
                "mspaint",
                { "app": "mspaint", "effect": "none" },
                { "app": "charmap", "effect": "later 30", "when": "sometimes" },
                { "effect": "never" },
                { "app": "charmap" },
                { "App": "charmap", "Effect": "Sooner 25", "When": "OnScreen", "Path": null, "note": "passed over" }
              ]
            }
            """);
        var c = Config.Load(path, out string? problem, out string? warning);
        Assert.IsNull(problem);
        CollectionAssert.AreEqual(new[] { "notepad", "mspaint" }, c.CloseWhenAway);
        CollectionAssert.AreEqual(new[] { new AppRule("notepad.exe", Effect.Never), new AppRule("charmap", Effect.Sooner(25), AppWhen.OnScreen) }, c.AppRules);
        StringAssert.Contains(warning, "closeWhenAway (left out 5, which is not text)");
        StringAssert.Contains(warning, "closeWhenAway (left out an object, which is not text)");
        StringAssert.Contains(warning, "appRules (left out rule 2: \"app\" is 5, not text)");
        StringAssert.Contains(warning, "appRules (left out rule 3: \"mspaint\" is not a rule)");
        StringAssert.Contains(warning, "appRules (left out rule 4: \"effect\" is \"none\", not never, later N, sooner P or always N)");
        StringAssert.Contains(warning, "appRules (left out rule 5: \"when\" is \"sometimes\", not running or onScreen)");
        StringAssert.Contains(warning, "appRules (left out rule 6: it names no app)");
        StringAssert.Contains(warning, "appRules (left out rule 7: it has no effect)");
        Assert.DoesNotContain("rule 1", warning!);
        Assert.DoesNotContain("rule 8", warning!);
    }

    [TestMethod]
    public void ANullInANewSettingGivesItsDefault()
    {
        File.WriteAllText(path, "{ \"configVersion\": 1, \"lockShortcut\": null, \"openWhenAway\": null, \"appRules\": null, \"countController\": null, \"notificationPlace\": null }");
        var c = Config.Load(path, out string? problem, out string? warning);
        Assert.IsNull(problem);
        Assert.AreEqual("", c.LockShortcut);
        Assert.IsEmpty(c.OpenWhenAway);
        Assert.IsEmpty(c.AppRules);
        Assert.IsTrue(c.CountController);
        Assert.IsNull(c.NotificationPlace);
        StringAssert.Contains(warning, "lockShortcut (null is not text)");
        StringAssert.Contains(warning, "countController (null is not true or false)");
        Assert.DoesNotContain("notificationPlace", warning!, "null is how an unpicked place may be written");
    }

    // Button color was a setting in the first 1.0 builds and is gone: a file that still has it, with
    // any value, loads with no warning, so no "part skipped" note, and the next save leaves it out.
    [TestMethod]
    [DataRow("\"purple\"")]
    [DataRow("\"system\"")]
    [DataRow("\"red\"")]
    [DataRow("5")]
    [DataRow("null")]
    public void TheRetiredButtonColorLoadsQuietly(string value)
    {
        File.WriteAllText(path, $"{{ \"configVersion\": 1, \"idleMinutes\": 7, \"ButtonColor\": {value}, \"theme\": \"dark\" }}");
        var c = Config.Load(path, out string? problem, out string? warning);
        Assert.IsNull(problem);
        Assert.IsNull(warning);
        Assert.AreEqual(7, c.IdleMinutes);
        Assert.AreEqual(ThemeChoice.Dark, c.Theme);
        c.Save(path);
        Assert.IsFalse(File.ReadAllText(path).Contains("buttonColor", StringComparison.OrdinalIgnoreCase), "left out on saving");

        // A name nobody ever wrote is still reported.
        File.WriteAllText(path, $"{{ \"configVersion\": 1, \"buttonColour\": {value} }}");
        Config.Load(path, out _, out warning);
        StringAssert.Contains(warning, "'buttonColour'");
    }

    // The shortcut is off for a new install. A file that has one keeps it, Ctrl+Alt+L included, and
    // a file without the name gets off.
    [TestMethod]
    public void TheShortcutIsOffUnlessTheFileHasOne()
    {
        Assert.AreEqual("", new Config().LockShortcut);
        File.WriteAllText(path, "{ \"configVersion\": 1, \"idleMinutes\": 7 }");
        Assert.AreEqual("", Config.Load(path, out _).LockShortcut);
        File.WriteAllText(path, "{ \"configVersion\": 1, \"lockShortcut\": \"Ctrl+Alt+L\" }");
        Assert.AreEqual("Ctrl+Alt+L", Config.Load(path, out _).LockShortcut);
        File.WriteAllText(path, "{ \"configVersion\": 1, \"lockShortcut\": \"\" }");
        Assert.AreEqual("", Config.Load(path, out _).LockShortcut);
        // Missing from the file: none is made, and the app holds none (FreshStart's defaults too).
        File.Delete(path);
        Assert.AreEqual("", Config.Load(path, out _).LockShortcut);
        StringAssert.Contains(File.ReadAllText(path), "\"lockShortcut\": \"\"");
    }

    // The tab the settings open on: a whole number from 0 (Locking) to 4 (Extras), out of range
    // brought to the nearest, and read leniently: anything else is the default with a warning.
    [TestMethod]
    [DataRow("0", 0)]
    [DataRow("3", 3)]
    [DataRow("4", 4)]
    [DataRow("9", 4)]
    [DataRow("-2", 0)]
    [DataRow("2.6", 3)]
    public void TheSettingsTabIsReadAndKeptInRange(string value, int expected)
    {
        File.WriteAllText(path, $"{{ \"configVersion\": 1, \"settingsTab\": {value} }}");
        var c = Config.Load(path, out string? problem, out string? warning);
        Assert.IsNull(problem);
        Assert.IsNull(warning);
        Assert.AreEqual(expected, c.SettingsTab);
        Assert.AreEqual(Config.MaxSettingsTab, SettingsForm.TabCount - 1, "one for each tab");
    }

    [TestMethod]
    public void TopLeftIsReadAndWritten()
    {
        File.WriteAllText(path, "{ \"configVersion\": 1, \"notificationPlace\": \"TopLeft\" }");
        var c = Config.Load(path, out _, out string? warning);
        Assert.IsNull(warning);
        Assert.AreEqual(NotePosition.TopLeft, c.NotificationPlace);
        c.Save(path);
        StringAssert.Contains(File.ReadAllText(path), "\"notificationPlace\": \"topLeft\"");
    }

    [TestMethod]
    public void NewChoicesAreReadWhateverTheirCase()
    {
        File.WriteAllText(path, "{ \"configVersion\": 1, \"theme\": \"DARK\", \"mediaOnLock\": \"PauseStayPaused\", \"musicVideo\": \"LATER 90\", \"notificationPlace\": \"bottomleft\" }");
        var c = Config.Load(path, out string? problem, out string? warning);
        Assert.IsNull(problem);
        Assert.IsNull(warning);
        Assert.AreEqual(ThemeChoice.Dark, c.Theme);
        Assert.AreEqual(MediaOnLock.PauseStayPaused, c.MediaOnLock);
        Assert.AreEqual(Effect.Extra(90), c.MusicVideo);
        Assert.AreEqual(NotePosition.BottomLeft, c.NotificationPlace);
    }

    [TestMethod]
    public void DecimalsInNewWholeNumberSettingsAreRounded()
    {
        File.WriteAllText(path, "{ \"configVersion\": 1, \"breakMinutes\": 44.6, \"snoozeMinutes\": 30.0 }");
        var c = Config.Load(path, out string? problem, out string? warning);
        Assert.IsNull(problem);
        Assert.IsNull(warning);
        Assert.AreEqual(45, c.BreakMinutes);
        Assert.AreEqual(30, c.SnoozeMinutes);
    }

    [TestMethod]
    public void ACopySharesNoList()
    {
        var c = new Config
        {
            AppRules = { new AppRule("notepad", Effect.Never) },
            OpenWhenAway = { "a" },
            OpenWhenBack = { "b" },
            CloseWhenAway = { "c" },
            CloseWhenBack = { "d" },
        };
        var copy = c.Copy();
        copy.AppRules.Add(new AppRule("mspaint", Effect.Extra(15)));
        copy.OpenWhenAway.Add("x");
        copy.OpenWhenBack.Add("x");
        copy.CloseWhenAway.Add("x");
        copy.CloseWhenBack.Add("x");
        Assert.HasCount(1, c.AppRules);
        Assert.HasCount(1, c.OpenWhenAway);
        Assert.HasCount(1, c.OpenWhenBack);
        Assert.HasCount(1, c.CloseWhenAway);
        Assert.HasCount(1, c.CloseWhenBack);
    }
}
