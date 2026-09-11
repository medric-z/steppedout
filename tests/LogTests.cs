namespace SteppedOut.Tests;

[TestClass]
public class LogTests
{
    [TestMethod]
    public void RollsOverWhenFull()
    {
        string dir = Path.Combine(Path.GetTempPath(), "SteppedOut.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "test.log");
            var log = new RollingLog(path, maxBytes: 200);
            for (int i = 0; i < 20; i++) log.Write("line " + i + new string('x', 20));

            Assert.IsTrue(File.Exists(Path.Combine(dir, "test.old.log")), "old log should exist after rollover");
            Assert.IsLessThan(300L, new FileInfo(path).Length, "current log should have been restarted");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void LinesStartWithATimestamp()
    {
        string dir = Path.Combine(Path.GetTempPath(), "SteppedOut.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "test.log");
            new RollingLog(path, maxBytes: 1 << 20).Write("hello");
            string line = File.ReadAllLines(path)[0];
            StringAssert.Matches(line, new System.Text.RegularExpressions.Regex(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d hello$"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
