namespace SteppedOut.Tests;

[TestClass]
public class CallRuleTests
{
    [TestMethod]
    public void InUseOnlyWhileStartedAndNotStopped()
    {
        Assert.IsTrue(CallRule.InUse(133_000_000_000_000_000L, 0L, 0L));
        Assert.IsFalse(CallRule.InUse(133_000_000_000_000_000L, 133_000_000_100_000_000L, 0L));
        Assert.IsFalse(CallRule.InUse(0L, 0L, 0L), "never used");
        Assert.IsFalse(CallRule.InUse(null, null, 0L));
        Assert.IsFalse(CallRule.InUse(1, 0, 0L), "values that are not 64-bit numbers are ignored");
    }

    [TestMethod]
    public void ARecordLeftOpenBeforeThisSignInIsNotACall()
    {
        long signedIn = 134_000_000_000_000_000L;

        Assert.IsFalse(CallRule.InUse(signedIn - TimeSpan.FromDays(30).Ticks, 0L, signedIn), "left open by a PC that lost power mid-call");
        Assert.IsTrue(CallRule.InUse(signedIn + TimeSpan.FromMinutes(5).Ticks, 0L, signedIn));
    }

    [TestMethod]
    public void ARecordOnlyCountsWhileItsProgramRunsSinceBeforeIt()
    {
        string me = System.Diagnostics.Process.GetCurrentProcess().ProcessName;

        Assert.IsTrue(CallRule.RunningSince(me, DateTime.UtcNow.ToFileTimeUtc()));
        Assert.IsFalse(CallRule.RunningSince(me, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc()), "started after the record: an earlier copy left it open");
        Assert.IsFalse(CallRule.RunningSince("SteppedOutNoSuchProgram", DateTime.UtcNow.ToFileTimeUtc()), "not running at all");
    }

    // A Store call app (new Teams, WhatsApp, Phone Link) that crashed or was ended mid-call leaves its
    // record open; it only counts while a program from its package runs since before the record.
    [TestMethod]
    public void AStoreAppRecordOnlyCountsWhileAProgramFromItsPackageRunsSinceBeforeIt()
    {
        const string family = "Contoso.CallApp_8wekyb3d8bbwe";
        int me = Environment.ProcessId;
        string? FamilyOf(int id) => id == me ? family : null;
        long now = DateTime.UtcNow.ToFileTimeUtc();

        Assert.IsTrue(CallRule.FamilyRunningSince(family, now, FamilyOf));
        Assert.IsTrue(CallRule.FamilyRunningSince("CONTOSO.CALLAPP_8wekyb3d8bbwe", now, FamilyOf), "families are not case sensitive");
        Assert.IsFalse(CallRule.FamilyRunningSince(family, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc(), FamilyOf),
            "started after the record: the copy on the call crashed and was started again");
        Assert.IsFalse(CallRule.FamilyRunningSince(family, now, _ => null), "nothing from the package runs: it crashed or was ended mid-call");
        Assert.IsFalse(CallRule.FamilyRunningSince("Contoso.Other_8wekyb3d8bbwe", now, FamilyOf));
    }

    // The sign-in is kept on the monotonic clock. The tray app may start before Windows sets the
    // clock: a clock an hour fast that is set back made new calls look older than the sign-in, and
    // one an hour slow that is set forward let records from before the sign-in count.
    [TestMethod]
    [DataRow(1, DisplayName = "a clock an hour fast, set back")]
    [DataRow(-1, DisplayName = "a clock an hour slow, set forward")]
    public void TheSignInMovesWithAClockCorrectedAfterTheStart(int hoursOff)
    {
        long hour = TimeSpan.FromHours(1).Ticks, minute = TimeSpan.FromMinutes(1).Ticks;
        long real = new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
        long off = hoursOff * hour;
        var monoAtSignIn = TimeSpan.FromHours(3);

        // Windows stamps the sign-in with the wrong clock, and the tray app starts 2 minutes later.
        long signedIn = real + off;
        var anchor = CallRule.SignedInOnClock(signedIn, signedIn + 2 * minute, monoAtSignIn + TimeSpan.FromMinutes(2));
        Assert.AreEqual(monoAtSignIn, anchor);

        // The clock is right again 20 minutes after the sign-in, when a call starts.
        long now = real + 20 * minute;
        long notBefore = CallRule.NotBefore(now, monoAtSignIn + TimeSpan.FromMinutes(20), anchor);
        long fixedFromTheStart = signedIn - minute;
        Assert.IsTrue(CallRule.InUse(now, 0L, notBefore), "a call after the sign-in counts");
        long stale = real - 30 * minute;
        Assert.IsFalse(CallRule.InUse(stale, 0L, notBefore), "a record left open before the sign-in does not");
        if (hoursOff > 0) Assert.IsFalse(CallRule.InUse(now, 0L, fixedFromTheStart), "the call was ignored with the sign-in fixed at the start");
        else Assert.IsTrue(CallRule.InUse(stale, 0L, fixedFromTheStart), "the stale record counted with the sign-in fixed at the start");
    }

    [TestMethod]
    public void ASignInThatReadsAsLaterThanNowIsTakenAsNow()
    {
        long now = new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
        var mono = TimeSpan.FromHours(3);
        Assert.AreEqual(mono, CallRule.SignedInOnClock(now + TimeSpan.FromHours(1).Ticks, now, mono));
    }

    // The scan itself, with the records given: a record from before the sign-in is never a call
    // (Q14), and a Store app's record counts only while a program from its package runs since before
    // it, a desktop app's only while its program does (Q5). Each check is the scan's own, so leaving
    // one out of it fails here.
    [TestMethod]
    public void TheScanCountsOnlyRecordsSinceTheSignInWhoseAppRunsSinceBeforeThem()
    {
        var mono = TimeSpan.FromHours(5);
        long now = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
        var signedIn = mono - TimeSpan.FromHours(2);
        long call = now - TimeSpan.FromMinutes(10).Ticks;
        long stale = now - TimeSpan.FromDays(3).Ticks;
        const string Family = "Contoso.CallApp_8wekyb3d8bbwe";
        const string Exe = "C:#Apps#Inkwell#inkwell.exe";
        var askedFamily = new List<(string, long)>();
        var askedProgram = new List<(string, long)>();
        bool familyRuns = true, programRuns = true;
        IReadOnlyList<CallApp> Scan(params MicRecord[] records) => CallRule.Scan(records, now, mono, signedIn,
            (program, latest) => { askedProgram.Add((program, latest)); return programRuns; },
            (family, latest) => { askedFamily.Add((family, latest)); return familyRuns; });

        var both = Scan(new MicRecord(Family, null, call, 0L), new MicRecord("NonPackaged", Exe, call, 0L));
        CollectionAssert.AreEqual(new[] { new CallApp("CallApp", Family: Family), new CallApp("inkwell", ExePath: @"C:\Apps\Inkwell\inkwell.exe") }, both.ToList());
        long slack = TimeSpan.FromMinutes(1).Ticks;
        CollectionAssert.AreEqual(new[] { (Family, call + slack) }, askedFamily, "the Store app's package is asked about, with the record's start");
        CollectionAssert.AreEqual(new[] { ("inkwell", call + slack) }, askedProgram);

        Assert.IsEmpty(Scan(new MicRecord(Family, null, stale, 0L), new MicRecord("NonPackaged", Exe, stale, 0L)), "left open before the sign-in");
        Assert.IsEmpty(Scan(new MicRecord(Family, null, call, call + 5L), new MicRecord("NonPackaged", Exe, null, 0L)), "stopped, or never started");

        familyRuns = false;
        Assert.IsEmpty(Scan(new MicRecord(Family, null, call, 0L)), "nothing from the package runs since before the record");
        programRuns = false;
        familyRuns = true;
        Assert.IsEmpty(Scan(new MicRecord("NonPackaged", Exe, call, 0L)), "the program does not run since before the record");
    }

    [TestMethod]
    public void NamesDesktopAppsAndKeepsTheirProgramFile()
    {
        Assert.AreEqual("Discord", CallRule.ExeName("C:#Users#me#AppData#Local#Discord#app-1.0.9#Discord.exe"));
        Assert.AreEqual("obs64", CallRule.ExeName("C:#Program Files#obs-studio#bin#64bit#obs64.exe"));
        Assert.AreEqual(@"C:\Program Files\obs-studio\bin\64bit\obs64.exe", CallRule.ExePath("C:#Program Files#obs-studio#bin#64bit#obs64.exe"));
    }

    [TestMethod]
    public void NamesStoreApps()
    {
        Assert.AreEqual("SkypeApp", CallRule.PackageName("Microsoft.SkypeApp_kzf8qxf38zg5c"));
        Assert.AreEqual("MSTeams", CallRule.PackageName("MSTeams_8wekyb3d8bbwe"));
    }

    [TestMethod]
    public void EveryAppOnACallIsReportedOnce()
    {
        var world = new FakeWorld();
        world.Call(30, "Teams");
        world.Calls.Add(new CallApp("Discord", ExePath: @"C:\Discord\Discord.exe"));
        world.Calls.Add(new CallApp("Teams", ExePath: @"C:\Old\Teams.exe"));
        var lines = new List<WhyLine>();
        new CallRule { Effect = Effect.Extra(30) }.Check(world.Look(TimeSpan.FromSeconds(1)), lines);
        CollectionAssert.AreEqual(new[] { new WhyLine(Signal.Call, Effect.Extra(30), "Teams"), new WhyLine(Signal.Call, Effect.Extra(30), "Discord") }, lines);
    }

    [TestMethod]
    public void ACallThatChangesNothingIsStillReported()
    {
        var world = new FakeWorld();
        world.Call(30, "Teams");
        var lines = new List<WhyLine>();
        new CallRule().Check(world.Look(TimeSpan.FromSeconds(1)), lines);
        CollectionAssert.AreEqual(new[] { new WhyLine(Signal.Call, Effect.None, "Teams") }, lines);
        Assert.IsEmpty(Evaluator.Effects(lines), "and adds nothing to the lock time");
    }

    [TestMethod]
    public void ReadingTheRealRecordsDoesNotThrow()
    {
        var calls = CallRule.Scan();
        Assert.IsTrue(calls.All(c => c.Name.Length > 0 && (c.ExePath is not null || c.Family is not null)));
    }
}
