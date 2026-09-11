using Microsoft.Win32;

namespace SteppedOut.Tests;

[TestClass]
[DoNotParallelize]
public class AutostartTests
{
    // A scratch key, so the tests never touch the real Run key.
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
    public void DisableOnAMissingKeyIsHarmless()
    {
        Autostart.Disable(TestKey + "Missing");
        Assert.IsFalse(Autostart.IsEnabled(TestKey + "Missing"));
    }
}
