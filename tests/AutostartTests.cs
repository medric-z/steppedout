using Microsoft.Win32;

namespace SteppedOut.Tests;

[TestClass]
[DoNotParallelize]
public class AutostartTests
{
    // A scratch key, so the tests never touch the real Run key. Its StartupApproved key sits in the
    // same scratch tree, so the real one is never touched either.
    const string TestKey = @"Software\SteppedOut.Tests\Run";

    [TestCleanup]
    public void Cleanup() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\SteppedOut.Tests", throwOnMissingSubKey: false);

    [TestMethod]
    public void EnableWritesTheQuotedExePathAndDisableRemovesIt()
    {
        Assert.IsFalse(Autostart.IsEnabled(TestKey));

        Autostart.Enable(TestKey);
        Assert.IsTrue(Autostart.IsEnabled(TestKey));
        using (var key = Registry.CurrentUser.OpenSubKey(TestKey))
        {
            string? value = key?.GetValue(AppInfo.Name) as string;
            Assert.AreEqual(Autostart.Command(), value);
            StringAssert.StartsWith(value, "\"");
            StringAssert.EndsWith(value, "\"");
        }

        Autostart.Disable(TestKey);
        Assert.IsFalse(Autostart.IsEnabled(TestKey));
    }

    [TestMethod]
    public void AValueForAnotherCopyReadsAsOffAndEnableRewritesIt()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(TestKey))
        {
            key.SetValue(AppInfo.Name, @"""C:\Users\me\Downloads\SteppedOut.exe""");
        }
        Assert.IsFalse(Autostart.IsEnabled(TestKey));

        Autostart.Enable(TestKey);
        Assert.IsTrue(Autostart.IsEnabled(TestKey));
        using (var key = Registry.CurrentUser.OpenSubKey(TestKey))
        {
            Assert.AreEqual(Autostart.Command(), key?.GetValue(AppInfo.Name) as string);
        }
    }

    [TestMethod]
    public void TheSameExeMatchesWithOrWithoutQuotesInAnyCase()
    {
        string exe = @"C:\Users\me\AppData\Local\SteppedOut\SteppedOut.exe";
        Assert.IsTrue(Autostart.Starts(@"""C:\Users\me\AppData\Local\SteppedOut\SteppedOut.exe""", exe));
        Assert.IsTrue(Autostart.Starts(@"c:\users\ME\appdata\local\steppedout\steppedout.exe ", exe));
        Assert.IsFalse(Autostart.Starts(@"""C:\Users\me\Downloads\SteppedOut.exe""", exe));
        Assert.IsFalse(Autostart.Starts(@"""C:\Users\me\AppData\Local\SteppedOut\SteppedOut.exe""", null));
    }

    [TestMethod]
    public void DisableOnAMissingKeyIsHarmless()
    {
        Autostart.Disable(TestKey + "Missing");
        Assert.IsFalse(Autostart.IsEnabled(TestKey + "Missing"));
    }

    [TestMethod]
    public void AnEntryForAnotherCopyIsStillAnEntry()
    {
        Assert.IsFalse(Autostart.HasEntry(TestKey));
        using (var key = Registry.CurrentUser.CreateSubKey(TestKey))
        {
            key.SetValue(AppInfo.Name, @"""C:\Users\me\Downloads\SteppedOut.exe""");
        }
        Assert.IsTrue(Autostart.HasEntry(TestKey));
        Autostart.Disable(TestKey);
        Assert.IsFalse(Autostart.HasEntry(TestKey));
    }

    [TestMethod]
    public void AnEntryForAFileThatIsGoneIsNoticed()
    {
        string gone = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "SteppedOut.exe");
        using (var key = Registry.CurrentUser.CreateSubKey(TestKey))
        {
            key.SetValue(AppInfo.Name, $"\"{gone}\"");
        }
        Assert.IsTrue(Autostart.PointsAtMissingFile(TestKey));

        Autostart.Enable(TestKey);
        Assert.IsFalse(Autostart.PointsAtMissingFile(TestKey));
    }

    [TestMethod]
    public void ArgumentsAfterTheExeDoNotChangeWhichExeStarts()
    {
        string exe = @"C:\Users\me\AppData\Local\SteppedOut\SteppedOut.exe";
        Assert.IsTrue(Autostart.Starts(@"""C:\Users\me\AppData\Local\SteppedOut\SteppedOut.exe"" --dry-run", exe));
        Assert.IsTrue(Autostart.Starts(@"C:\Users\me\AppData\Local\SteppedOut\SteppedOut.EXE --idle 5", exe));
        Assert.IsFalse(Autostart.Starts(@"""C:\Users\me\Downloads\SteppedOut.exe"" --dry-run", exe));
        Assert.AreEqual(("C:\\a b\\SteppedOut.exe", "--dry-run --idle=3"), Autostart.Split(@"  ""C:\a b\SteppedOut.exe""  --dry-run --idle=3 "));
        Assert.AreEqual(("C:\\a b\\SteppedOut.exe", ""), Autostart.Split(@"""C:\a b\SteppedOut.exe"));
    }

    [TestMethod]
    public void AnEntryForThisCopyWithArgumentsIsOnAndNotMissing()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(TestKey))
        {
            key.SetValue(AppInfo.Name, Autostart.Command() + " --dry-run");
        }
        Assert.IsTrue(Autostart.IsEnabled(TestKey));
        Assert.IsFalse(Autostart.PointsAtMissingFile(TestKey));
    }

    [TestMethod]
    public void TheOffFlagLivesBesideTheRunKey()
    {
        Assert.AreEqual(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", Autostart.ApprovedKey(Autostart.RunKey));
        Assert.AreEqual(@"Software\SteppedOut.Tests\Explorer\StartupApproved\Run", Autostart.ApprovedKey(TestKey));
    }

    [TestMethod]
    public void AnEntryTurnedOffInWindowsReadsAsOffAndEnableClearsTheFlag()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(TestKey))
        {
            key.SetValue(AppInfo.Name, Autostart.Command() + " --dry-run");
        }
        using (var approved = Registry.CurrentUser.CreateSubKey(Autostart.ApprovedKey(TestKey)))
        {
            approved.SetValue(AppInfo.Name, new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }
        Assert.IsFalse(Autostart.IsEnabled(TestKey));
        Assert.IsTrue(Autostart.HasEntry(TestKey));

        Autostart.Enable(TestKey);
        Assert.IsTrue(Autostart.IsEnabled(TestKey));
        using (var key = Registry.CurrentUser.OpenSubKey(TestKey))
        {
            Assert.AreEqual(Autostart.Command() + " --dry-run", key?.GetValue(AppInfo.Name) as string);
        }
        using (var approved = Registry.CurrentUser.OpenSubKey(Autostart.ApprovedKey(TestKey)))
        {
            Assert.IsNull(approved?.GetValue(AppInfo.Name));
        }
    }

    [TestMethod]
    public void AnEntryTurnedOnInWindowsStaysOn()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(TestKey))
        {
            key.SetValue(AppInfo.Name, Autostart.Command());
        }
        using (var approved = Registry.CurrentUser.CreateSubKey(Autostart.ApprovedKey(TestKey)))
        {
            approved.SetValue(AppInfo.Name, new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }
        Assert.IsTrue(Autostart.IsEnabled(TestKey));
    }

    [TestMethod]
    public void AnEntryForAFileThatIsGoneButTurnedOffInWindowsIsNotTakenOver()
    {
        string gone = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "SteppedOut.exe");
        using (var key = Registry.CurrentUser.CreateSubKey(TestKey))
        {
            key.SetValue(AppInfo.Name, $"\"{gone}\"");
        }
        using (var approved = Registry.CurrentUser.CreateSubKey(Autostart.ApprovedKey(TestKey)))
        {
            approved.SetValue(AppInfo.Name, new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }
        Assert.IsFalse(Autostart.PointsAtMissingFile(TestKey));
    }

    [TestMethod]
    public void TakingOverAnEntryKeepsItsArguments()
    {
        string gone = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "SteppedOut.exe");
        using (var key = Registry.CurrentUser.CreateSubKey(TestKey))
        {
            key.SetValue(AppInfo.Name, $"\"{gone}\" --dry-run");
        }
        Assert.IsTrue(Autostart.PointsAtMissingFile(TestKey));

        Autostart.Enable(TestKey);
        Assert.IsTrue(Autostart.IsEnabled(TestKey));
        using (var key = Registry.CurrentUser.OpenSubKey(TestKey))
        {
            Assert.AreEqual(Autostart.Command() + " --dry-run", key?.GetValue(AppInfo.Name) as string);
        }
    }

    // Read and Choose with no startup task are the normal exe: the same Run entry as above, and the
    // same steps the tray app took before Choose existed.

    [TestMethod]
    public void WithoutATaskReadIsTheRunEntry()
    {
        Assert.AreEqual(AutostartState.Off, Autostart.Read(null, TestKey));
        using (var key = Registry.CurrentUser.CreateSubKey(TestKey))
        {
            key.SetValue(AppInfo.Name, Autostart.Command() + " --dry-run");
        }
        Assert.AreEqual(AutostartState.On, Autostart.Read(null, TestKey));
        using (var approved = Registry.CurrentUser.CreateSubKey(Autostart.ApprovedKey(TestKey)))
        {
            approved.SetValue(AppInfo.Name, new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }
        Assert.AreEqual(AutostartState.Off, Autostart.Read(null, TestKey), "the normal exe takes an entry turned off in Windows back, so it is plain Off");
    }

    [TestMethod]
    public void WithoutATaskChooseWritesAndRemovesTheSameEntryAsEnableAndDisable()
    {
        Assert.AreEqual(AutostartState.On, Autostart.Choose(true, null, TestKey));
        using (var key = Registry.CurrentUser.OpenSubKey(TestKey))
        {
            Assert.AreEqual(Autostart.Command(), key?.GetValue(AppInfo.Name) as string);
        }
        Assert.AreEqual(AutostartState.Off, Autostart.Choose(false, null, TestKey));
        Assert.IsFalse(Autostart.HasEntry(TestKey));
        Assert.AreEqual(AutostartState.Off, Autostart.Choose(false, null, TestKey), "off twice is fine");
    }

    [TestMethod]
    public void WithoutATaskChooseOnKeepsArgumentsAndClearsTheOffFlag()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(TestKey))
        {
            key.SetValue(AppInfo.Name, Autostart.Command() + " --dry-run");
        }
        using (var approved = Registry.CurrentUser.CreateSubKey(Autostart.ApprovedKey(TestKey)))
        {
            approved.SetValue(AppInfo.Name, new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }
        Assert.AreEqual(AutostartState.On, Autostart.Choose(true, null, TestKey));
        using (var key = Registry.CurrentUser.OpenSubKey(TestKey))
        {
            Assert.AreEqual(Autostart.Command() + " --dry-run", key?.GetValue(AppInfo.Name) as string);
        }
        using (var approved = Registry.CurrentUser.OpenSubKey(Autostart.ApprovedKey(TestKey)))
        {
            Assert.IsNull(approved?.GetValue(AppInfo.Name));
        }
    }

    [TestMethod]
    public void WithoutATaskChooseOnLeavesAnEntryThatIsAlreadyOnAsItIs()
    {
        // The same exe in other letters is already on, so nothing is rewritten.
        string upper = Autostart.Command().ToUpperInvariant();
        using (var key = Registry.CurrentUser.CreateSubKey(TestKey))
        {
            key.SetValue(AppInfo.Name, upper);
        }
        Assert.AreEqual(AutostartState.On, Autostart.Choose(true, null, TestKey));
        using (var check = Registry.CurrentUser.OpenSubKey(TestKey))
        {
            Assert.AreEqual(upper, check?.GetValue(AppInfo.Name) as string);
        }
    }

    [TestMethod]
    public void WithoutATaskChooseOffRemovesAnotherCopysEntryAndOnPointsItHere()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(TestKey))
        {
            key.SetValue(AppInfo.Name, @"""X:\Elsewhere\SteppedOut.exe""");
        }
        Assert.AreEqual(AutostartState.Off, Autostart.Read(null, TestKey));
        Assert.AreEqual(AutostartState.Off, Autostart.Choose(false, null, TestKey));
        Assert.IsFalse(Autostart.HasEntry(TestKey));

        using (var key = Registry.CurrentUser.CreateSubKey(TestKey))
        {
            key.SetValue(AppInfo.Name, @"""X:\Elsewhere\SteppedOut.exe""");
        }
        Assert.AreEqual(AutostartState.On, Autostart.Choose(true, null, TestKey));
        using (var check = Registry.CurrentUser.OpenSubKey(TestKey))
        {
            Assert.AreEqual(Autostart.Command(), check?.GetValue(AppInfo.Name) as string);
        }
    }

    [TestMethod]
    public void TheCallsWithoutAKeyPathAreTheNormalExesInATestRun()
    {
        // Reads only: the real Run key is never written by a test.
        Assert.IsNull(Packaged.StartupTask);
        Assert.AreEqual(Autostart.PointsAtMissingFile(Autostart.RunKey), Autostart.PointsAtMissingFile());
        Assert.AreEqual(Autostart.IsEnabled(Autostart.RunKey) ? AutostartState.On : AutostartState.Off, Autostart.Read());
    }

    // Not known yet, while Windows has not answered: not on, not a choice to make now, and no note,
    // so the row keeps its usual line while the switch waits greyed out.
    [TestMethod]
    public void ReadingIsNeitherOnNorChangeableAndSaysNothing()
    {
        Assert.IsFalse(AutostartState.Reading.IsOn());
        Assert.IsFalse(AutostartState.Reading.CanChange());
        Assert.IsNull(AutostartState.Reading.Note(English.Text.V1));
        Assert.IsNull(AutostartState.Reading.Note(Arabic.Text.V1));
        Assert.AreEqual(AutostartState.Reading, Enum.GetValues<AutostartState>()[^1], "added last, so no saved number changes meaning");
    }
}
