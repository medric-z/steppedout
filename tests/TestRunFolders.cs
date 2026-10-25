namespace SteppedOut.Tests;

// Scratch folders the tests make under %TEMP%. Each test removes its own, but a run that crashed or
// was stopped leaves them; the next run removes those once they are a day old, so a run still going
// in another worktree keeps its own. A tray made in a test keeps its settings in
// SteppedOut-blocked\<pid>, which goes when the run ends.
static class TestRunFolders
{
    public static readonly string Scratch = Path.Combine(Path.GetTempPath(), "SteppedOut.Tests");

    public static readonly string Blocked = Path.Combine(Path.GetTempPath(), "SteppedOut-blocked");

    public static readonly TimeSpan Age = TimeSpan.FromDays(1);

    // Removes the folders in root last written before cutoff. One still in use stays.
    public static int RemoveOlder(string root, DateTime cutoffUtc)
    {
        if (!Directory.Exists(root)) return 0;
        int removed = 0;
        foreach (var folder in new DirectoryInfo(root).EnumerateDirectories())
        {
            try
            {
                if (folder.LastWriteTimeUtc >= cutoffUtc) continue;
                folder.Delete(recursive: true);
                removed++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        return removed;
    }

    public static void AtStart()
    {
        var cutoff = DateTime.UtcNow - Age;
        RemoveOlder(Scratch, cutoff);
        RemoveOlder(Blocked, cutoff);
    }

    public static void AtEnd()
    {
        try
        {
            string own = Path.Combine(Blocked, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (Directory.Exists(own)) Directory.Delete(own, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}

[TestClass]
public class TestRunFoldersTests
{
    [TestMethod]
    public void OnlyFoldersADayOldAreRemoved()
    {
        string root = Path.Combine(TestRunFolders.Scratch, Guid.NewGuid().ToString("N"));
        try
        {
            var old = Directory.CreateDirectory(Path.Combine(root, "crashed"));
            File.WriteAllText(Path.Combine(old.FullName, "config.json"), "{}");
            var recent = Directory.CreateDirectory(Path.Combine(root, "running"));
            var now = DateTime.UtcNow;
            old.LastWriteTimeUtc = now - TimeSpan.FromDays(2);
            recent.LastWriteTimeUtc = now - TimeSpan.FromHours(1);

            Assert.AreEqual(1, TestRunFolders.RemoveOlder(root, now - TestRunFolders.Age));

            Assert.IsFalse(Directory.Exists(old.FullName), "a crashed run's folder goes");
            Assert.IsTrue(Directory.Exists(recent.FullName), "one that may be in use stays");
            Assert.AreEqual(0, TestRunFolders.RemoveOlder(Path.Combine(root, "none"), now), "no folder, nothing to do");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
