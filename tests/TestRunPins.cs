using System.Runtime.CompilerServices;

namespace SteppedOut.Tests;

// The tests run the same whatever the PC is set to: Windows shown in English and text at 100%, as
// far as the app can tell. A test or a cleanup that asks for "auto" or the System text size gets
// those, never the PC's Arabic or its bigger text. Pinned as the test assembly loads, before the
// run's own setup and any test.
static class TestRunPins
{
    internal const ushort EnglishUs = 0x0409;

    [ModuleInitializer]
    internal static void PinLanguageAndTextSize()
    {
        Lang.WindowsLanguage = () => EnglishUs;
        Theme.WindowsTextScale = () => 100;
        Lang.Use("auto");
        Theme.Choose(ThemeChoice.System, TextSize.System);
    }
}

[TestClass]
[DoNotParallelize]
public class TestRunPinsTests
{
    // An Arabic Windows with text at 150% reaches the app through the two seams, and the run's own
    // English and 100% are back afterwards.
    [TestMethod]
    public void ThePcsLanguageAndTextSizeReachTheAppOnlyThroughTheSeams()
    {
        Assert.AreSame(English.Text, Lang.For("auto"), "the run is pinned to English");
        Assert.IsFalse(Lang.ShellIsRightToLeft());
        try
        {
            Lang.WindowsLanguage = () => 0x0401;
            Theme.WindowsTextScale = () => 150;
            Assert.AreSame(Arabic.Text, Lang.For("auto"));
            Assert.IsTrue(Lang.ShellIsRightToLeft());
            Theme.Choose(ThemeChoice.System, TextSize.System);
            Assert.AreEqual(1.5f, Theme.TextScale);
        }
        finally
        {
            Lang.WindowsLanguage = () => TestRunPins.EnglishUs;
            Theme.WindowsTextScale = () => 100;
            Lang.Use("en");
            Theme.Choose(ThemeChoice.System, TextSize.System);
        }
        Assert.AreEqual(1f, Theme.TextScale);
    }
}
