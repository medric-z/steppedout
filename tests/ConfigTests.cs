namespace SteppedOut.Tests;

[TestClass]
public class ConfigTests
{
    static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "SteppedOut.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [TestMethod]
    public void DefaultsAreTheDocumentedOnes()
    {
        var c = new Config();
        Assert.AreEqual(15, c.IdleMinutes);
        Assert.AreEqual(15, c.GraceSeconds);
        Assert.AreEqual(512, c.LogMaxKilobytes);
    }

    [TestMethod]
    public void MissingFileIsCreatedWithDefaults()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "sub", "config.json");
            var c = Config.Load(path, out string? problem);
            Assert.IsNull(problem);
            Assert.IsTrue(File.Exists(path));
            Assert.AreEqual(15, c.IdleMinutes);
            StringAssert.Contains(File.ReadAllText(path), "\"idleMinutes\": 15");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void RoundTripsThroughTheFile()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "config.json");
            new Config { IdleMinutes = 2.5, GraceSeconds = 3, LogMaxKilobytes = 128, SuppressWhileAudioPlaying = false, AudioPeakThreshold = 0.05, AudioSilenceSeconds = 9, SuppressWhileFullscreen = false, NeverLockWhileRunning = { "obs64", "Zoom" }, JitterFilter = false, JitterPixels = 9, SuppressWhileDisplayRequested = false }.Save(path);
            var c = Config.Load(path, out string? problem);
            Assert.IsNull(problem);
            Assert.AreEqual(2.5, c.IdleMinutes);
            Assert.AreEqual(3, c.GraceSeconds);
            Assert.AreEqual(128, c.LogMaxKilobytes);
            Assert.IsFalse(c.SuppressWhileAudioPlaying);
            Assert.AreEqual(0.05, c.AudioPeakThreshold);
            Assert.AreEqual(9, c.AudioSilenceSeconds);
            Assert.IsFalse(c.SuppressWhileFullscreen);
            CollectionAssert.AreEqual(new[] { "obs64", "Zoom" }, c.NeverLockWhileRunning);
            Assert.IsFalse(c.JitterFilter);
            Assert.AreEqual(9, c.JitterPixels);
            Assert.IsFalse(c.SuppressWhileDisplayRequested);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void CommentsTrailingCommasAndUnknownKeysAreTolerated()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, "{\n  // hand edited\n  \"idleMinutes\": 3,\n  \"graceSeconds\": 7,\n  \"somethingNew\": true,\n}\n");
            var c = Config.Load(path, out string? problem);
            Assert.IsNull(problem);
            Assert.AreEqual(3, c.IdleMinutes);
            Assert.AreEqual(7, c.GraceSeconds);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void BrokenFileIsReportedAndLeftAlone()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, "{ this is not json");
            var c = Config.Load(path, out string? problem);
            Assert.IsNotNull(problem);
            Assert.AreEqual(15, c.IdleMinutes);
            Assert.AreEqual("{ this is not json", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void OutOfRangeValuesAreClamped()
    {
        var c = new Config { IdleMinutes = -5, GraceSeconds = 99999, LogMaxKilobytes = 1, JitterPixels = 0 };
        c.Clamp();
        Assert.AreEqual(1, c.JitterPixels);
        Assert.AreEqual(0.05, c.IdleMinutes);
        Assert.AreEqual(600, c.GraceSeconds);
        Assert.AreEqual(64, c.LogMaxKilobytes);
    }
}
