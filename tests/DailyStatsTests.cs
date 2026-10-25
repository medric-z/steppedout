using System.Text.Json;

namespace SteppedOut.Tests;

[TestClass]
public class DailyStatsTests
{
    static readonly DateTime Day = new(2026, 10, 20);

    static DateTime At(int hour, int minute, int second = 0, int day = 0) => Day.AddDays(day).Add(new TimeSpan(hour, minute, second));

    static DailyStats Memory(DateTime now) => DailyStats.Load(null, now);

    static string TempFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "SteppedOut.Tests." + Guid.NewGuid().ToString("N"));
        return Path.Combine(dir, DailyStats.FileName);
    }

    static void Clean(string path)
    {
        try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    static (string date, int locks, long away) Read(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.HasCount(3, root.EnumerateObject().ToList(), "only the date and the two numbers");
        return (root.GetProperty("date").GetString()!, root.GetProperty("locks").GetInt32(), root.GetProperty("awaySeconds").GetInt64());
    }

    [TestMethod]
    public void StartsAtZero()
    {
        var s = Memory(At(9, 0));
        Assert.AreEqual((0, TimeSpan.Zero), s.Today(At(9, 0)));
    }

    [TestMethod]
    public void CountsLocksAndAddsUpLockedTime()
    {
        var s = Memory(At(9, 0));
        s.Locked(At(10, 0));
        s.Unlocked(At(10, 30));
        s.Locked(At(12, 0));
        s.Unlocked(At(13, 0));
        Assert.AreEqual((2, TimeSpan.FromMinutes(90)), s.Today(At(14, 0)));
    }

    // A lock that came as the PC went to sleep counts from the wake, across midnight too, and an
    // away that is not under way is left alone.
    [TestMethod]
    public void AwayFromMovesTheStartOfTheAwayUnderWay()
    {
        var s = Memory(At(9, 0));
        s.Locked(At(10, 0));
        s.AwayFrom(At(11, 0));
        s.Unlocked(At(11, 5));
        Assert.AreEqual((1, TimeSpan.FromMinutes(5)), s.Today(At(12, 0)));

        s.AwayFrom(At(12, 0));
        Assert.AreEqual((1, TimeSpan.FromMinutes(5)), s.Today(At(12, 30)), "nothing under way");

        s.Locked(At(23, 50));
        s.AwayFrom(At(7, 0, day: 1));
        s.Unlocked(At(7, 10, day: 1));
        Assert.AreEqual((0, TimeSpan.FromMinutes(10)), s.Today(At(8, 0, day: 1)));
    }

    [TestMethod]
    public void TimeAwayWithLockingOffCountsAsAwayButNotAsALock()
    {
        var s = Memory(At(9, 0));
        s.WentAway(At(10, 0), At(10, 15));
        s.CameBack(At(10, 45));
        Assert.AreEqual((0, TimeSpan.FromMinutes(45)), s.Today(At(11, 0)));
    }

    [TestMethod]
    public void ADryRunWouldLockNeverCounts()
    {
        // A dry run steps out and logs WOULD LOCK, but the session never locks, so only the time
        // away is counted.
        var s = Memory(At(9, 0));
        s.WentAway(At(10, 0), At(10, 15));
        s.CameBack(At(10, 20));
        s.WentAway(At(11, 0), At(11, 15));
        s.CameBack(At(11, 20));
        Assert.AreEqual(0, s.Today(At(12, 0)).Locks);
    }

    [TestMethod]
    public void AnAutomaticLockCountsOnceAndItsTimeOnce()
    {
        // Stepping out (idle since 10:00), then the session locks at 10:15 and unlocks at 11:00.
        var s = Memory(At(9, 0));
        s.WentAway(At(10, 0), At(10, 15));
        s.Locked(At(10, 15));
        s.Unlocked(At(11, 0));
        s.CameBack(At(11, 0));
        Assert.AreEqual((1, TimeSpan.FromMinutes(60)), s.Today(At(11, 5)), "matches You were away 1 hr");
    }

    [TestMethod]
    public void WinLCountsAsALock()
    {
        var s = Memory(At(9, 0));
        s.Locked(At(9, 30));
        s.Unlocked(At(9, 40));
        Assert.AreEqual((1, TimeSpan.FromMinutes(10)), s.Today(At(10, 0)));
    }

    [TestMethod]
    public void AwayTimeSoFarIsIncludedWhileStillAway()
    {
        var s = Memory(At(9, 0));
        s.Locked(At(10, 0));
        Assert.AreEqual((1, TimeSpan.FromMinutes(20)), s.Today(At(10, 20)));
    }

    [TestMethod]
    public void ResetsAtLocalMidnight()
    {
        var s = Memory(At(9, 0));
        s.Locked(At(10, 0));
        s.Unlocked(At(11, 0));
        s.Tick(At(23, 59, 59));
        Assert.AreEqual((1, TimeSpan.FromHours(1)), s.Today(At(23, 59, 59)));
        s.Tick(At(0, 0, 0, day: 1));
        Assert.AreEqual((0, TimeSpan.Zero), s.Today(At(0, 0, 0, day: 1)));
    }

    [TestMethod]
    public void ALockOverMidnightIsSplit()
    {
        var s = Memory(At(9, 0));
        s.Locked(At(23, 0));
        Assert.AreEqual((1, new TimeSpan(0, 59, 59)), s.Today(At(23, 59, 59)));
        s.Tick(At(0, 10, 0, day: 1));
        s.Unlocked(At(0, 30, 0, day: 1));
        Assert.AreEqual((0, TimeSpan.FromMinutes(30)), s.Today(At(1, 0, 0, day: 1)), "only the part after midnight, and the lock was yesterday's");
    }

    [TestMethod]
    public void AStretchOverMidnightIsSplitEvenWithNoTickInBetween()
    {
        var s = Memory(At(9, 0));
        s.WentAway(At(22, 0), At(22, 15));
        s.CameBack(At(7, 0, 0, day: 1));
        Assert.AreEqual((0, TimeSpan.FromHours(7)), s.Today(At(8, 0, 0, day: 1)));
    }

    [TestMethod]
    public void AwayAllWeekendCountsOnlyToday()
    {
        var s = Memory(At(17, 0));
        s.Locked(At(18, 0));
        s.Unlocked(At(9, 0, 0, day: 3));
        Assert.AreEqual((0, TimeSpan.FromHours(9)), s.Today(At(9, 0, 0, day: 3)));
    }

    [TestMethod]
    public void IdleThatBeganYesterdayCountsFromMidnight()
    {
        var s = Memory(At(0, 5, 0, day: 1));
        s.WentAway(At(23, 50), At(0, 5, 0, day: 1));
        s.CameBack(At(0, 20, 0, day: 1));
        Assert.AreEqual((0, TimeSpan.FromMinutes(20)), s.Today(At(0, 30, 0, day: 1)));
    }

    // Across a daylight saving change the wall clock jumps; the time away is what really passed. In
    // autumn 01:30 summer time to 01:10 winter time is 40 minutes, not none; in spring 01:50 winter
    // time to 03:10 summer time is 20 minutes, not 80.
    [TestMethod]
    public void AnAwayAcrossADaylightSavingChangeCountsTheTimeThatPassed()
    {
        var summer = TimeSpan.FromHours(1);
        var winter = TimeSpan.Zero;
        var autumn = new DateTime(2026, 10, 25);
        var s = DailyStats.Load(null, new DateTimeOffset(autumn.AddHours(1), summer));
        s.Locked(new DateTimeOffset(autumn.Add(new TimeSpan(1, 30, 0)), summer));
        s.Unlocked(new DateTimeOffset(autumn.Add(new TimeSpan(1, 10, 0)), winter));
        Assert.AreEqual((1, TimeSpan.FromMinutes(40)), s.Today(new DateTimeOffset(autumn.AddHours(2), winter)));

        var spring = new DateTime(2026, 3, 29);
        s = DailyStats.Load(null, new DateTimeOffset(spring.AddHours(1), winter));
        s.WentAway(new DateTimeOffset(spring.Add(new TimeSpan(1, 50, 0)), winter), new DateTimeOffset(spring.Add(new TimeSpan(1, 55, 0)), winter));
        s.CameBack(new DateTimeOffset(spring.Add(new TimeSpan(3, 10, 0)), summer));
        Assert.AreEqual((0, TimeSpan.FromMinutes(20)), s.Today(new DateTimeOffset(spring.AddHours(4), summer)));
    }

    [TestMethod]
    public void AClockSetBackNeverMakesNegativeTime()
    {
        var s = Memory(At(10, 0));
        s.Locked(At(10, 0));
        s.Unlocked(At(9, 0));
        Assert.AreEqual((1, TimeSpan.Zero), s.Today(At(9, 0)));
    }

    [TestMethod]
    public void SavesOnLockAndUnlockAndQuit()
    {
        string path = TempFile();
        try
        {
            var s = DailyStats.Load(path, At(9, 0));
            s.Locked(At(10, 0));
            Assert.AreEqual(("2026-10-20", 1, 0L), Read(path));
            s.Unlocked(At(10, 30));
            Assert.AreEqual(("2026-10-20", 1, 1800L), Read(path));
            s.WentAway(At(11, 0), At(11, 0));
            s.CameBack(At(11, 10));
            s.Quit(At(11, 10, 1));
            Assert.AreEqual(("2026-10-20", 1, 2400L), Read(path));
        }
        finally { Clean(path); }
    }

    [TestMethod]
    public void OtherwiseSavesAtMostOnceAMinute()
    {
        string path = TempFile();
        try
        {
            var s = DailyStats.Load(path, At(9, 0));
            s.Locked(At(10, 0));
            File.Delete(path);
            s.Tick(At(10, 0, 30));
            Assert.IsFalse(File.Exists(path), "saved 30 s ago");
            s.Tick(At(10, 1));
            Assert.AreEqual(("2026-10-20", 1, 60L), Read(path), "the time away so far, in case of a crash");
            File.Delete(path);
            s.Tick(At(10, 1, 59));
            Assert.IsFalse(File.Exists(path));
        }
        finally { Clean(path); }
    }

    // A today.json that cannot be written, here held open by another program for ten hours while the
    // PC is locked, is said in the log once and then once an hour, not every minute, and tried less
    // often; once it can be written again it is saved within RetryAtMost and the log says so.
    [TestMethod]
    public void AFileThatCannotBeWrittenIsNotLoggedEveryMinute()
    {
        string path = TempFile();
        var said = new List<string>();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var s = DailyStats.Load(path, At(9, 0), said.Add);
            var now = At(9, 0);
            using (new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                s.Locked(now);
                int tries = 0, failed = s.Failures;
                for (int i = 0; i < 10 * 60 * 60; i++)
                {
                    now = now.AddSeconds(1);
                    s.Tick(now);
                    if (s.Failures != failed) tries++;
                    failed = s.Failures;
                }
                Assert.IsInRange(10, 11, said.Count, "once, then once an hour: " + string.Join(" | ", said));
                StringAssert.StartsWith(said[0], "could not save today.json: ");
                StringAssert.Contains(said[1], "tries in a row");
                Assert.IsLessThanOrEqualTo(4 * 10 + 4, tries, "tried at most every 15 minutes");
            }
            said.Clear();
            for (int i = 0; i < DailyStats.RetryAtMost.TotalSeconds; i++)
            {
                now = now.AddSeconds(1);
                s.Tick(now);
            }
            Assert.AreEqual(0, s.Failures);
            Assert.HasCount(1, said);
            StringAssert.StartsWith(said[0], "saved today.json again after ");
            Assert.AreEqual(1, Read(path).locks);
        }
        finally { Clean(path); }
    }

    [TestMethod]
    public void NothingChangedMeansNothingWritten()
    {
        string path = TempFile();
        try
        {
            var s = DailyStats.Load(path, At(9, 0));
            s.Tick(At(9, 0));
            Assert.IsTrue(File.Exists(path), "a missing file is replaced");
            File.Delete(path);
            s.Tick(At(9, 5));
            s.Tick(At(9, 10));
            Assert.IsFalse(File.Exists(path));
        }
        finally { Clean(path); }
    }

    [TestMethod]
    public void TheFileIsOverwrittenInPlaceAndNeverGrows()
    {
        string path = TempFile();
        try
        {
            var s = DailyStats.Load(path, At(9, 0));
            s.Locked(At(9, 0));
            long size = new FileInfo(path).Length;
            for (int i = 0; i < 50; i++)
            {
                s.Unlocked(At(9, i, 30));
                s.Locked(At(9, i + 1));
            }
            Assert.IsLessThanOrEqualTo(size + 6, new FileInfo(path).Length);
            Assert.AreEqual(51, Read(path).locks);
            Assert.HasCount(1, Directory.GetFiles(Path.GetDirectoryName(path)!), "no temporary or extra files");
        }
        finally { Clean(path); }
    }

    [TestMethod]
    public void TodaysFileIsReadAtStart()
    {
        string path = TempFile();
        try
        {
            var before = DailyStats.Load(path, At(9, 0));
            before.Locked(At(9, 0));
            before.Unlocked(At(9, 45));
            before.Quit(At(9, 46));

            var after = DailyStats.Load(path, At(12, 0));
            Assert.AreEqual((1, TimeSpan.FromMinutes(45)), after.Today(At(12, 0)));
            after.Locked(At(12, 0));
            after.Unlocked(At(12, 15));
            Assert.AreEqual(("2026-10-20", 2, 3600L), Read(path));
        }
        finally { Clean(path); }
    }

    [TestMethod]
    public void YesterdaysFileIsIgnoredAndReplaced()
    {
        string path = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """{"date":"2026-10-19","locks":7,"awaySeconds":5000}""");
            var s = DailyStats.Load(path, At(8, 0));
            Assert.AreEqual((0, TimeSpan.Zero), s.Today(At(8, 0)));
            s.Tick(At(8, 0));
            Assert.AreEqual(("2026-10-20", 0, 0L), Read(path));
        }
        finally { Clean(path); }
    }

    [TestMethod]
    public void AMissingOrBadFileIsIgnoredAndReplaced()
    {
        string[] bad =
        [
            "",
            "not json",
            "[]",
            "null",
            """{"date":"2026-10-20"}""",
            """{"date":"20/10/2026","locks":1,"awaySeconds":1}""",
            """{"date":5,"locks":1,"awaySeconds":1}""",
            """{"date":"2026-10-20","locks":"three","awaySeconds":1}""",
            """{"date":"2026-10-20","locks":-1,"awaySeconds":1}""",
            """{"date":"2026-10-20","locks":1.5,"awaySeconds":1}""",
            """{"date":"2026-10-20","locks":1,"awaySeconds":-5}""",
            """{"date":"2026-10-20","locks":1,"awaySeconds":999999999}""",
            """{"date":"2026-10-20","locks":1,"awaySeconds":true}""",
            """{"date":"2026-10-20","locks":1,"awaySeconds":1""",
        ];
        foreach (string text in bad)
        {
            string path = TempFile();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text);
                var s = DailyStats.Load(path, At(8, 0));
                Assert.AreEqual((0, TimeSpan.Zero), s.Today(At(8, 0)), text);
                s.Tick(At(8, 0));
                Assert.AreEqual(("2026-10-20", 0, 0L), Read(path), text);
            }
            finally { Clean(path); }
        }

        string missing = TempFile();
        try
        {
            var s = DailyStats.Load(missing, At(8, 0));
            Assert.AreEqual((0, TimeSpan.Zero), s.Today(At(8, 0)));
            s.Tick(At(8, 0));
            Assert.AreEqual(("2026-10-20", 0, 0L), Read(missing));
        }
        finally { Clean(missing); }
    }

    [TestMethod]
    public void AFileThatCannotBeWrittenIsNeverAnError()
    {
        string path = TempFile();
        try
        {
            // A folder where the file should be: every write fails.
            Directory.CreateDirectory(path);
            var s = DailyStats.Load(path, At(8, 0));
            s.Locked(At(9, 0));
            s.Unlocked(At(9, 30));
            s.Tick(At(9, 31));
            s.Quit(At(9, 32));
            Assert.AreEqual((1, TimeSpan.FromMinutes(30)), s.Today(At(9, 32)));
        }
        finally { Clean(path); }
    }

    [TestMethod]
    public void ExtraKeysInTheFileDoNotMatter()
    {
        Assert.AreEqual((new DateTime(2026, 10, 20), 3, 4800.0),
            DailyStats.Parse("""{"date":"2026-10-20","locks":3,"awaySeconds":4800,"note":"hand edit"}"""));
    }

    [TestMethod]
    public void TheFileUsesInvariantDigits()
    {
        var culture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            string text = DailyStats.Format(new DateTime(2026, 10, 20), 3, TimeSpan.FromSeconds(4800.4));
            Assert.AreEqual("""{"date":"2026-10-20","locks":3,"awaySeconds":4800}""" + Environment.NewLine, text);
        }
        finally { Thread.CurrentThread.CurrentCulture = culture; }
    }
}
