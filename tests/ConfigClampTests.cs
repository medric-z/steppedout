namespace SteppedOut.Tests;

// What Clamp and Copy do to the settings, whether they come from the file or from code.
[TestClass]
public class ConfigClampTests
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

    Config Load()
    {
        var c = Config.Load(path, out string? problem, out _);
        Assert.IsNull(problem);
        return c;
    }

    [TestMethod]
    public void ACopySharesNoListAtAll()
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
        var lists = typeof(Config).GetProperties().Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(List<>)).ToList();
        Assert.HasCount(5, lists, "a new list setting needs its line in Copy");
        foreach (var list in lists)
        {
            Assert.AreNotSame(list.GetValue(c), list.GetValue(copy), list.Name);
            CollectionAssert.AreEqual((System.Collections.ICollection)list.GetValue(c)!, (System.Collections.ICollection)list.GetValue(copy)!, list.Name);
        }
    }

    [TestMethod]
    public void ClampKeepsEveryNewNumberInRange()
    {
        var c = new Config
        {
            MusicVideo = Effect.Extra(0),
            FullScreen = Effect.Extra(5000),
            OnCall = Effect.Extra(-3),
            OnBattery = Effect.Sooner(0),
            BreakMinutes = -7,
            SnoozeMinutes = -7,
            AppRules =
            {
                new AppRule("notepad", Effect.Sooner(99)),
                new AppRule("mspaint", Effect.Cap(0)),
                new AppRule("charmap", Effect.Cap(99999)),
            },
        };
        c.Clamp();
        Assert.AreEqual(Effect.Extra(1), c.MusicVideo);
        Assert.AreEqual(Effect.Extra(1440), c.FullScreen);
        Assert.AreEqual(Effect.Extra(1), c.OnCall);
        Assert.AreEqual(Effect.Sooner(1), c.OnBattery);
        Assert.AreEqual(1, c.BreakMinutes);
        Assert.AreEqual(0, c.SnoozeMinutes);
        CollectionAssert.AreEqual(new[] { Effect.Sooner(95), Effect.Cap(1), Effect.Cap(1440) }, c.AppRules.Select(r => r.Effect).ToArray());

        c.OnBattery = Effect.Sooner(100);
        c.BreakMinutes = 100000;
        c.SnoozeMinutes = 100000;
        c.Clamp();
        Assert.AreEqual(Effect.Sooner(95), c.OnBattery);
        Assert.AreEqual(720, c.BreakMinutes);
        Assert.AreEqual(240, c.SnoozeMinutes);

        // Off, every time on the list, and any other time in range stay as they are, not snapped to the list.
        foreach (int snooze in new[] { 0, 1, 5, 7, 10, 15, 30, 45, 60, 239, 240 })
        {
            c.SnoozeMinutes = snooze;
            c.Clamp();
            Assert.AreEqual(snooze, c.SnoozeMinutes);
        }
        foreach (int minutes in new[] { 3, 5, 45, 240, 719, 720 })
        {
            c.BreakMinutes = minutes;
            c.Clamp();
            Assert.AreEqual(minutes, c.BreakMinutes);
        }
    }

    // Values the lenient readers take, which only Clamp brings into range.
    [TestMethod]
    [DataRow("breakMinutes", "-7", nameof(Config.BreakMinutes), "1")]
    [DataRow("breakMinutes", "1e400", nameof(Config.BreakMinutes), "720")]
    [DataRow("snoozeMinutes", "-7", nameof(Config.SnoozeMinutes), "0")]
    [DataRow("snoozeMinutes", "45", nameof(Config.SnoozeMinutes), "45")]
    [DataRow("snoozeMinutes", "1e400", nameof(Config.SnoozeMinutes), "240")]
    [DataRow("onBattery", "\"always 0\"", nameof(Config.OnBattery), "always 1")]
    [DataRow("onBattery", "\"sooner 100\"", nameof(Config.OnBattery), "sooner 95")]
    [DataRow("musicVideo", "\"later 99999999999\"", nameof(Config.MusicVideo), "later 1440")]
    [DataRow("fullScreen", "\"later 0\"", nameof(Config.FullScreen), "later 1")]
    [DataRow("lockShortcut", "\"Win+L\"", nameof(Config.LockShortcut), "")]
    [DataRow("lockShortcut", "\"L\"", nameof(Config.LockShortcut), "")]
    [DataRow("lockShortcut", "\" Ctrl+Alt+K \"", nameof(Config.LockShortcut), "Ctrl+Alt+K")]
    [DataRow("idleMinutes", "1e400", nameof(Config.IdleMinutes), "15")]
    [DataRow("idleMinutes", "-1e400", nameof(Config.IdleMinutes), "15")]
    [DataRow("idleMinutes", "99999", nameof(Config.IdleMinutes), "1440")]
    public void AValueOutOfRangeInTheFileIsBroughtIntoRange(string key, string value, string property, string expected)
    {
        File.WriteAllText(path, $"{{ \"configVersion\": 1, \"{key}\": {value} }}");
        var c = Config.Load(path, out string? problem, out _);
        Assert.IsNull(problem);
        object? loaded = typeof(Config).GetProperty(property)!.GetValue(c);
        Assert.AreEqual(expected, Convert.ToString(loaded, System.Globalization.CultureInfo.InvariantCulture));
    }

    // A lock time that is no number at all locks after the default time, not after the longest.
    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    public void ALockTimeThatIsNoNumberFallsBackToTheDefault(double idle)
    {
        var c = new Config { IdleMinutes = idle, AudioPeakThreshold = double.NaN };
        c.Clamp();
        Assert.AreEqual(Config.DefaultIdleMinutes, c.IdleMinutes);
        Assert.AreEqual(15, c.IdleMinutes);
        Assert.AreEqual(0.01, c.AudioPeakThreshold);
    }

    [TestMethod]
    public void EffectsASettingCannotHoldFallBackToItsDefault()
    {
        var c = new Config
        {
            MusicVideo = Effect.Cap(10),
            FullScreen = Effect.Sooner(50),
            OnCall = new Effect((EffectKind)42, 5),
            OnBattery = Effect.Never,
        };
        c.Clamp();
        Assert.AreEqual(Effect.Never, c.MusicVideo);
        Assert.AreEqual(Effect.Never, c.FullScreen);
        Assert.AreEqual(Effect.Never, c.OnCall);
        Assert.AreEqual(Effect.None, c.OnBattery);

        c.MusicVideo = new Effect(EffectKind.Never, 5);
        c.OnBattery = new Effect(EffectKind.None, 7);
        c.Clamp();
        Assert.AreEqual(Effect.Never, c.MusicVideo, "never carries no number");
        Assert.AreEqual(Effect.None, c.OnBattery);
    }

    [TestMethod]
    public void ListsAreTrimmedWithoutBlanksOrTheSameEntryTwice()
    {
        var c = new Config
        {
            OpenWhenAway = { @" C:\Tools\a.exe ", @"""C:\TOOLS\A.EXE""", "", null!, "   ", @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" },
            OpenWhenBack = { "x", "X", " y " },
            CloseWhenAway = { "notepad", "Notepad.exe", " mspaint ", "", @"C:\Windows\NOTEPAD.EXE", @"C:\Windows\" },
            CloseWhenBack = null!,
        };
        c.Clamp();
        CollectionAssert.AreEqual(new[] { @"C:\Tools\a.exe", @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" }, c.OpenWhenAway);
        CollectionAssert.AreEqual(new[] { "x", "y" }, c.OpenWhenBack);
        CollectionAssert.AreEqual(new[] { "notepad", "mspaint" }, c.CloseWhenAway);
        Assert.IsEmpty(c.CloseWhenBack);
    }

    [TestMethod]
    public void AppRulesKeepOneRulePerAppTheFirstWinning()
    {
        var c = new Config
        {
            AppRules =
            {
                new AppRule(" notepad ", Effect.Never, AppWhen.Running, " C:\\Windows\\notepad.exe ", "  "),
                new AppRule(@"C:\Windows\notepad.exe", Effect.Extra(30)),
                new AppRule("NOTEPAD.EXE", Effect.Sooner(10), AppWhen.OnScreen),
                new AppRule("mspaint", Effect.None),
                new AppRule("  ", Effect.Never),
                new AppRule(null!, Effect.Never),
                null!,
                new AppRule("charmap", Effect.Extra(5), (AppWhen)9),
                new AppRule("\"mspaint.exe\"", Effect.Extra(5)),
            },
        };
        c.Clamp();
        CollectionAssert.AreEqual(new[]
        {
            new AppRule("notepad", Effect.Never, AppWhen.Running, @"C:\Windows\notepad.exe"),
            new AppRule("charmap", Effect.Extra(5), AppWhen.Running),
            new AppRule("mspaint.exe", Effect.Extra(5)),
        }, c.AppRules);
    }

    // The path only gives the settings window the app's icon and description; one on another PC, or
    // one that is not a full path, is dropped so nothing is read over the network. The rule stays.
    [TestMethod]
    public void AnAppRulesPathOnAnotherPCOrNotFullIsDropped()
    {
        File.WriteAllText(path, """
            { "configVersion": 1, "appRules": [
              { "app": "obs64", "effect": "never", "path": "\\\\192.0.2.10\\share\\obs64.exe" },
              { "app": "mspaint", "effect": "never", "path": "//192.0.2.10/share/mspaint.exe" },
              { "app": "charmap", "effect": "never", "path": "tools\\charmap.exe" },
              { "app": "calc", "effect": "never", "path": "\\\\?\\C:\\Windows\\System32\\calc.exe" },
              { "app": "notepad", "effect": "never", "path": " \"C:\\Windows\\notepad.exe\" " }
            ] }
            """);
        CollectionAssert.AreEqual(new[]
        {
            new AppRule("obs64", Effect.Never),
            new AppRule("mspaint", Effect.Never),
            new AppRule("charmap", Effect.Never),
            new AppRule("calc", Effect.Never),
            new AppRule("notepad", Effect.Never, AppWhen.Running, @"C:\Windows\notepad.exe"),
        }, Load().AppRules);
    }

    // A path on a drive letter not connected now, such as a USB stick that is out, is kept: nothing
    // is read from it while it is not a drive on this PC, and the icon comes back with the stick.
    [TestMethod]
    public void AnAppRulesPathOnALetterNotConnectedNowIsKept()
    {
        var used = DriveInfo.GetDrives().Select(drive => char.ToUpperInvariant(drive.Name[0])).ToHashSet();
        char free = Enumerable.Range('D', 23).Select(letter => (char)letter).Reverse().FirstOrDefault(letter => !used.Contains(letter));
        if (free == default)
        {
            Assert.Inconclusive("every drive letter is in use here");
            return;
        }
        File.WriteAllText(path, $$"""
            { "configVersion": 1, "appRules": [
              { "app": "inkwell", "effect": "never", "path": "{{free}}:\\Apps\\inkwell.exe" }
            ] }
            """);
        CollectionAssert.AreEqual(new[] { new AppRule("inkwell", Effect.Never, AppWhen.Running, free + @":\Apps\inkwell.exe") }, Load().AppRules);
        Assert.IsFalse(LocalPaths.IsFullPathOnThisPC(free + @":\Apps\inkwell.exe"), "and it is not read as a file on this PC");
    }

    [TestMethod]
    public void TheSameAppTwiceInTheFileIsOneRule()
    {
        File.WriteAllText(path, """
            { "configVersion": 1, "appRules": [
              { "app": "obs64", "effect": "never" },
              { "app": "C:\\x\\obs64.exe", "effect": "later 30", "when": "onScreen" },
              { "app": "Obs64.EXE", "effect": "always 10" }
            ] }
            """);
        CollectionAssert.AreEqual(new[] { new AppRule("obs64", Effect.Never) }, Load().AppRules);
    }

    [TestMethod]
    [DataRow("Ctrl+Alt+L")]
    [DataRow("ctrl+alt+l")]
    [DataRow("Ctrl+Shift+F12")]
    [DataRow("Ctrl+Alt+1")]
    [DataRow("Alt+F24")]
    [DataRow("Ctrl + Alt + L")]
    public void AShortcutThatCanBeOneIsKept(string shortcut)
    {
        var c = new Config { LockShortcut = shortcut };
        c.Clamp();
        Assert.AreEqual(shortcut, c.LockShortcut);
    }

    // Clamp reads the shortcut with the same reader that registers it (letters, digits and F1 to
    // F24), so a key name it cannot use turns the shortcut off instead of staying in the file and
    // never working.
    [TestMethod]
    [DataRow("Alt+Shift+D1")]
    [DataRow("Control+Alt+Delete")]
    [DataRow("Ctrl+Alt+Oemcomma")]
    public void AShortcutTheReaderCannotUseIsTurnedOff(string shortcut)
    {
        var c = new Config { LockShortcut = shortcut };
        c.Clamp();
        Assert.AreEqual("", c.LockShortcut);
    }

    [TestMethod]
    [DataRow("L")]
    [DataRow("Ctrl")]
    [DataRow("Ctrl+")]
    [DataRow("+L")]
    [DataRow("Shift+L")]
    [DataRow("Win+L")]
    [DataRow("Ctrl+Win+L")]
    [DataRow("Ctrl+Alt+Shift")]
    [DataRow("Ctrl+Alt+ShiftKey")]
    [DataRow("Ctrl+Alt+LWin")]
    [DataRow("Ctrl+Ctrl+L")]
    [DataRow("Ctrl+Alt+NotAKey")]
    [DataRow("Ctrl+Alt+123")]
    [DataRow("Ctrl+Alt+L+K")]
    [DataRow("Ctrl+Alt+A, B")]
    [DataRow("Ctrl+Alt+ ")]
    public void ABadShortcutTurnsTheShortcutOff(string shortcut)
    {
        var c = new Config { LockShortcut = shortcut };
        c.Clamp();
        Assert.AreEqual("", c.LockShortcut);
    }

    [TestMethod]
    public void ChoicesThatAreNotOnTheListFallBackToTheirDefaults()
    {
        var c = new Config
        {
            MediaOnLock = (MediaOnLock)7,
            SoundWhenAway = (ActionSound)12,
            SoundWhenBack = (ActionSound)(-1),
            BreakSound = (BreakSound)9,
            Theme = (ThemeChoice)8,
            TextSize = (TextSize)8,
            NotificationPlace = (NotePosition)8,
            LockShortcut = null!,
            SettingsTab = 12,
        };
        c.Clamp();
        Assert.AreEqual(MediaOnLock.PauseUntilBack, c.MediaOnLock);
        Assert.AreEqual(ActionSound.Off, c.SoundWhenAway);
        Assert.AreEqual(ActionSound.Off, c.SoundWhenBack);
        Assert.AreEqual(BreakSound.Off, c.BreakSound);
        Assert.AreEqual(ThemeChoice.System, c.Theme);
        Assert.AreEqual(TextSize.System, c.TextSize);
        Assert.IsNull(c.NotificationPlace);
        Assert.AreEqual("", c.LockShortcut);
        Assert.AreEqual(Config.MaxSettingsTab, c.SettingsTab);

        foreach (var place in NotePlace.All)
        {
            c.NotificationPlace = place;
            c.Clamp();
            Assert.AreEqual(place, c.NotificationPlace);
        }
        c.SettingsTab = -3;
        c.Clamp();
        Assert.AreEqual(0, c.SettingsTab);
    }

    [TestMethod]
    public void ClampChangesNothingInTheDefaults()
    {
        var c = new Config();
        c.Clamp();
        Assert.IsTrue(c.SameAs(new Config()));
    }
}
