namespace SteppedOut.Tests;

[TestClass]
public class ShortcutTests
{
    [TestMethod]
    [DataRow("Ctrl+Alt+L")]
    [DataRow("Ctrl+Alt+Shift+F24")]
    [DataRow("Alt+Shift+K")]
    [DataRow("Ctrl+Shift+0")]
    [DataRow("Ctrl+F1")]
    [DataRow("Alt+9")]
    [DataRow("Ctrl+Alt+F12")]
    public void RoundTrips(string text)
    {
        Assert.IsTrue(Shortcut.TryParse(text, out Shortcut s));
        Assert.AreEqual(text, s.ToString());
        Assert.AreEqual(s, Shortcut.Parse(s.ToString()));
        Assert.AreEqual(text, Shortcut.Normalize(text));
    }

    [TestMethod]
    [DataRow("alt+ctrl+l", "Ctrl+Alt+L")]
    [DataRow(" Control + Alt + l ", "Ctrl+Alt+L")]
    [DataRow("shift+alt+ctrl+f5", "Ctrl+Alt+Shift+F5")]
    [DataRow("CTRL+ALT+7", "Ctrl+Alt+7")]
    public void ReadsAnyCaseAndOrderAndWritesTheUsualForm(string text, string expected)
    {
        Assert.AreEqual(expected, Shortcut.Normalize(text));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("L")]
    [DataRow("F5")]
    [DataRow("Shift+L")]
    [DataRow("Win+L")]
    [DataRow("Ctrl+Win+L")]
    [DataRow("Ctrl+Windows+L")]
    [DataRow("Ctrl+Alt")]
    [DataRow("Ctrl+Alt+")]
    [DataRow("Ctrl++L")]
    [DataRow("+L")]
    [DataRow("Ctrl+Alt+L+K")]
    [DataRow("Ctrl+Ctrl+L")]
    [DataRow("Ctrl+Alt+Tab")]
    [DataRow("Ctrl+Alt+Delete")]
    [DataRow("Ctrl+Alt+Space")]
    [DataRow("Ctrl+Alt+F0")]
    [DataRow("Ctrl+Alt+F25")]
    [DataRow("Ctrl+Alt+F01")]
    [DataRow("Ctrl+Alt+Ł")]
    [DataRow("Ctrl+Alt+١")]
    [DataRow("Alt+F4")]
    [DataRow("L+Ctrl")]
    [DataRow("Ctrl Alt L")]
    public void RejectsWhatIsNotAShortcut(string? text)
    {
        Assert.IsFalse(Shortcut.TryParse(text, out _));
        Assert.IsNull(Shortcut.Parse(text));
        Assert.AreEqual("", Shortcut.Normalize(text));
    }

    [TestMethod]
    public void TheDefaultIsCtrlAltL()
    {
        Assert.AreEqual("Ctrl+Alt+L", Shortcut.Default.ToString());
        // The one the Extras list offers; a new install has none until one is picked.
        Assert.AreEqual("", new Config().LockShortcut);
        Assert.AreEqual((uint)'L', Shortcut.Default.VirtualKey);
        Assert.AreEqual(0x2u | 0x1u, (uint)Shortcut.Default.Modifiers);
    }

    [TestMethod]
    public void AKeyPressMakesAShortcut()
    {
        Assert.IsTrue(Shortcut.TryFromKeys(Keys.Control | Keys.Alt | Keys.L, out Shortcut s));
        Assert.AreEqual("Ctrl+Alt+L", s.ToString());
        Assert.IsTrue(Shortcut.TryFromKeys(Keys.Control | Keys.Alt | Keys.Shift | Keys.F24, out s));
        Assert.AreEqual("Ctrl+Alt+Shift+F24", s.ToString());
        Assert.IsTrue(Shortcut.TryFromKeys(Keys.Alt | Keys.D3, out s));
        Assert.AreEqual("Alt+3", s.ToString());
    }

    [TestMethod]
    public void AKeyPressThatIsNotAShortcutMakesNone()
    {
        // Only the modifiers so far.
        Assert.IsFalse(Shortcut.TryFromKeys(Keys.Control | Keys.ControlKey, out _));
        Assert.IsFalse(Shortcut.TryFromKeys(Keys.Control | Keys.Alt | Keys.Menu, out _));
        // A bare key, or one with only Shift.
        Assert.IsFalse(Shortcut.TryFromKeys(Keys.L, out _));
        Assert.IsFalse(Shortcut.TryFromKeys(Keys.Shift | Keys.L, out _));
        // Keys that are not a letter, number or F key.
        Assert.IsFalse(Shortcut.TryFromKeys(Keys.Control | Keys.Alt | Keys.Space, out _));
        Assert.IsFalse(Shortcut.TryFromKeys(Keys.Control | Keys.Alt | Keys.NumPad1, out _));
        Assert.IsFalse(Shortcut.TryFromKeys(Keys.Alt | Keys.F4, out _));
        // The Windows key.
        Assert.IsFalse(Shortcut.TryFromKeys(Keys.Control | Keys.Alt | Keys.L, out _, winHeld: true));
    }

    [TestMethod]
    public void IsWrittenTheSameWayInEveryCulture()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            Assert.AreEqual("Ctrl+Alt+I", Shortcut.Normalize("ctrl+alt+i"));
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            Assert.AreEqual("Ctrl+Alt+F12", Shortcut.Normalize("Ctrl+Alt+F12"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    [TestMethod]
    public void DisplayStaysLeftToRightInsideArabicText()
    {
        string shown = Shortcut.Default.Display;
        Assert.AreEqual("‎Ctrl+Alt+L‎", shown);
        Assert.AreEqual("Ctrl+Alt+L", shown.Trim('‎'));
    }

    [TestMethod]
    public void IsAllowedNeedsCtrlOrAlt()
    {
        Assert.IsTrue(Shortcut.IsAllowed(ShortcutModifiers.Ctrl, Keys.L));
        Assert.IsTrue(Shortcut.IsAllowed(ShortcutModifiers.Alt | ShortcutModifiers.Shift, Keys.L));
        Assert.IsFalse(Shortcut.IsAllowed(ShortcutModifiers.Shift, Keys.L));
        Assert.IsFalse(Shortcut.IsAllowed(ShortcutModifiers.None, Keys.F5));
        Assert.IsFalse(Shortcut.IsAllowed((ShortcutModifiers)0x8 | ShortcutModifiers.Ctrl, Keys.L));
        Assert.IsTrue(new Shortcut(ShortcutModifiers.Ctrl | ShortcutModifiers.Alt, Keys.L).IsValid);
        Assert.IsFalse(default(Shortcut).IsValid);
    }
}
