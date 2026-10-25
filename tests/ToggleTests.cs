using System.Drawing.Imaging;

namespace SteppedOut.Tests;

// The switch: the welcome's classic look kept as it was, look C for the settings window, and the
// On/Off word beside it. Alone: drawing uses the shared font cache, and some of these switch the
// language or the palette for a moment.
[TestClass]
[DoNotParallelize]
public class ToggleTests
{
    static Bitmap Drawn(Toggle toggle)
    {
        toggle.BackColor = Theme.Current.Card;
        toggle.Size = toggle.GetPreferredSize(Size.Empty);
        var picture = new Bitmap(toggle.Width, toggle.Height, PixelFormat.Format32bppArgb);
        toggle.DrawToBitmap(picture, new Rectangle(Point.Empty, toggle.Size));
        return picture;
    }

    static bool Near(Color a, Color b, int tolerance = 6) =>
        Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;

    static void InPalette(bool dark, Action test)
    {
        try
        {
            Theme.UseForShots(dark, 1f);
            test();
        }
        finally
        {
            Theme.Choose(ThemeChoice.System, TextSize.System);
        }
    }

    static void InLanguage(string lang, Action test)
    {
        bool arabic = Lang.Current.RightToLeft;
        try
        {
            Lang.Use(lang);
            test();
        }
        finally
        {
            Lang.Use(arabic ? "ar" : "en");
        }
    }

    // What the welcome window and AccessibilityTests rely on stays as it was.
    [TestMethod]
    public void ANewSwitchIsTheClassicOneTheWelcomeHas()
    {
        var toggle = new Toggle();
        Assert.AreEqual(ToggleLook.Classic, toggle.Look);
        Assert.IsFalse(toggle.ShowLabel);
        Assert.IsFalse(toggle.Checked);
        Assert.AreEqual(new Size(48, 28), toggle.Size);
        Assert.AreEqual(new Size(toggle.LogicalToDeviceUnits(48), toggle.LogicalToDeviceUnits(28)), toggle.GetPreferredSize(Size.Empty));
        Assert.AreEqual(AccessibleRole.CheckButton, toggle.AccessibleRole);
        Assert.IsTrue(toggle.TabStop);
        Assert.AreEqual(Automation.CheckBoxType, ((Automation.IRawElementProviderSimple)toggle.AutomationProvider!).GetPropertyValue(Automation.ControlTypeProperty));
    }

    // Look C's track is 40 by 20 and compact's 34 by 18, each with 4 px around it for the focus ring.
    [TestMethod]
    public void LookCIsFortyByTwentyAndCompactThirtyFourByEighteen()
    {
        var toggle = new Toggle { Look = ToggleLook.LookC };
        Assert.AreEqual(new Size(toggle.LogicalToDeviceUnits(48), toggle.LogicalToDeviceUnits(28)), toggle.GetPreferredSize(Size.Empty));
        Assert.AreEqual(toggle.GetPreferredSize(Size.Empty), toggle.Size, "the size follows the look");
        toggle.Look = ToggleLook.Compact;
        Assert.AreEqual(new Size(toggle.LogicalToDeviceUnits(42), toggle.LogicalToDeviceUnits(26)), toggle.GetPreferredSize(Size.Empty));
        Assert.AreEqual(toggle.GetPreferredSize(Size.Empty), toggle.Size);
    }

    // The word's column is as wide as the longer word, so the switch is the same size on and off, in
    // English and in Arabic (غير مفعّل is much longer than مفعّل).
    [TestMethod]
    public void TheWordTakesAColumnAsWideAsTheLongerWord()
    {
        foreach (var lang in new[] { "en", "ar" })
        {
            InLanguage(lang, () =>
            {
                var font = Theme.Body(9.75f, new Toggle().DeviceDpi);
                const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
                int on = TextRenderer.MeasureText(Lang.Current.V1.On, font, Size.Empty, flags).Width;
                int off = TextRenderer.MeasureText(Lang.Current.V1.Off, font, Size.Empty, flags).Width;
                foreach (var look in new[] { ToggleLook.LookC, ToggleLook.Compact })
                {
                    var toggle = new Toggle { Look = look, ShowLabel = true };
                    var plain = new Toggle { Look = look };
                    var size = toggle.GetPreferredSize(Size.Empty);
                    Assert.AreEqual(plain.GetPreferredSize(Size.Empty).Width + Math.Max(on, off) + toggle.LogicalToDeviceUnits(4), size.Width, $"{lang} {look}");
                    toggle.Checked = true;
                    Assert.AreEqual(size, toggle.GetPreferredSize(Size.Empty), $"{lang} {look}: on and off");
                }
            });
        }
    }

    // The word is drawn, not a control: screen readers meet the switch alone, named for its row, and
    // hear its state from the toggle pattern.
    [TestMethod]
    public void ScreenReadersNeverMeetTheWord()
    {
        var toggle = new Toggle { Look = ToggleLook.LookC, ShowLabel = true };
        _ = new SettingRow(null, "Count a game controller", "Buttons and sticks count as using the PC", toggle);
        Assert.AreEqual(0, toggle.Controls.Count);
        var element = (Automation.IRawElementProviderSimple)toggle.AutomationProvider!;
        Assert.AreEqual("Count a game controller", element.GetPropertyValue(Automation.NameProperty));
        Assert.AreEqual(Automation.CheckBoxType, element.GetPropertyValue(Automation.ControlTypeProperty));
        var toggling = (Automation.IToggleProvider)element.GetPatternProvider(Automation.TogglePattern)!;
        toggling.Toggle();
        Assert.IsTrue(toggle.Checked);
        Assert.AreEqual(1, toggling.ToggleState);
    }

    // Look C: a filled grey track with a light knob at the start when off; the accent with its knob
    // at the end when on. The knob's middle and the track beside it, in each palette.
    [TestMethod]
    public void LookCFillsTheTrackAndMovesTheKnob()
    {
        foreach (bool dark in new[] { false, true })
        {
            InPalette(dark, () =>
            {
                var t = Theme.Current;
                var toggle = new Toggle { Look = ToggleLook.LookC };
                int px(int n) => toggle.LogicalToDeviceUnits(n);
                int middle = px(14);
                // Track from 4 to 44; knob 12 across, 4 in from either end.
                int startKnob = px(4 + 4 + 6), endKnob = px(44 - 4 - 6);
                using (var off = Drawn(toggle))
                {
                    Assert.IsTrue(Near(off.GetPixel(startKnob, middle), t.KnobOff), $"dark {dark}: the knob off at the start");
                    Assert.IsTrue(Near(off.GetPixel(endKnob, middle), t.Off), $"dark {dark}: the grey track");
                    Assert.IsTrue(Near(off.GetPixel(px(24), middle), t.Off), $"dark {dark}: filled, not outlined");
                }
                toggle.Checked = true;
                using (var on = Drawn(toggle))
                {
                    Assert.IsTrue(Near(on.GetPixel(endKnob, middle), t.Knob), $"dark {dark}: the knob on at the end");
                    Assert.IsTrue(Near(on.GetPixel(startKnob, middle), t.Accent), $"dark {dark}: the accent track");
                }
            });
        }
    }

    // In Arabic a switch that is on has its knob at the left, as Windows draws it, and its word at the right.
    [TestMethod]
    public void InArabicTheSwitchRunsTheOtherWay()
    {
        InPalette(false, () => InLanguage("ar", () =>
        {
            var t = Theme.Current;
            var toggle = new Toggle { Look = ToggleLook.LookC, ShowLabel = true, Checked = true, RightToLeft = RightToLeft.Yes };
            using var on = Drawn(toggle);
            int middle = toggle.LogicalToDeviceUnits(14);
            // Laid out from the right: track 4 px in from the left edge, the word after it.
            int knob = toggle.LogicalToDeviceUnits(4 + 4 + 6), track = toggle.LogicalToDeviceUnits(4 + 40 - 4 - 6);
            Assert.IsTrue(Near(on.GetPixel(knob, middle), t.Knob), "the knob at the left");
            Assert.IsTrue(Near(on.GetPixel(track, middle), t.Accent), "the track to its right");
            int ink = 0;
            for (int x = toggle.LogicalToDeviceUnits(52); x < on.Width; x++)
                for (int y = 0; y < on.Height; y++)
                    if (Near(on.GetPixel(x, y), t.Text, 60)) ink++;
            Assert.IsGreaterThan(10, ink, "the word right of the track");
        }));
    }

    // The welcome's classic switch: an outline and a small knob when off, filled when on.
    [TestMethod]
    public void TheClassicSwitchIsDrawnAsBefore()
    {
        InPalette(false, () =>
        {
            var t = Theme.Current;
            var toggle = new Toggle();
            int middle = toggle.LogicalToDeviceUnits(14);
            using (var off = Drawn(toggle))
            {
                Assert.IsTrue(Near(off.GetPixel(toggle.LogicalToDeviceUnits(24), middle), t.Card), "hollow");
                Assert.IsTrue(Near(off.GetPixel(toggle.LogicalToDeviceUnits(14), middle), t.Subtle, 30), "a knob in the quieter text color");
            }
            toggle.Checked = true;
            using var on = Drawn(toggle);
            Assert.IsTrue(Near(on.GetPixel(toggle.LogicalToDeviceUnits(18), middle), t.Accent), "filled");
            Assert.IsTrue(Near(on.GetPixel(toggle.LogicalToDeviceUnits(34), middle), t.OnAccent), "the knob at the end");
        });
    }

    // A switch that cannot be changed, as Start with Windows while Windows or a policy holds it, looks
    // it in every palette and state: halfway to the card behind it, or the contrast theme's grey (I20).
    [TestMethod]
    public void ADisabledSwitchLooksDisabled()
    {
        foreach (bool dark in new[] { false, true })
        {
            InPalette(dark, () =>
            {
                var t = Theme.Current;
                foreach (var look in new[] { ToggleLook.LookC, ToggleLook.Classic })
                {
                    foreach (bool on in new[] { false, true })
                    {
                        var toggle = new Toggle { Look = look, Checked = on };
                        int middle = toggle.LogicalToDeviceUnits(14);
                        // A point on the track away from the knob: filled when on or in look C.
                        int x = toggle.LogicalToDeviceUnits(on ? 12 : 36);
                        using var enabled = Drawn(toggle);
                        toggle.Enabled = false;
                        using var disabled = Drawn(toggle);
                        string what = $"dark {dark}, {look}, {(on ? "on" : "off")}";
                        if (look == ToggleLook.LookC || on)
                        {
                            var track = on ? t.Accent : t.Off;
                            var half = Color.FromArgb((track.R + t.Card.R) / 2, (track.G + t.Card.G) / 2, (track.B + t.Card.B) / 2);
                            Assert.IsTrue(Near(enabled.GetPixel(x, middle), track), $"{what}: enabled, the track's own color");
                            Assert.IsTrue(Near(disabled.GetPixel(x, middle), half), $"{what}: disabled, halfway to the card ({disabled.GetPixel(x, middle)} for {half})");
                        }
                        bool same = true;
                        for (int px = 0; px < enabled.Width && same; px++)
                            for (int py = 0; py < enabled.Height; py++)
                                if (enabled.GetPixel(px, py) != disabled.GetPixel(px, py)) { same = false; break; }
                        Assert.IsFalse(same, $"{what}: disabled is drawn differently");
                    }
                }
            });
        }
    }

    // A click on the words of a row that ends in a switch that cannot be changed changes nothing; so
    // does a screen reader's press, through UI Automation or MSAA. Neither the row nor the switch
    // shows the hand meanwhile.
    [TestMethod]
    public void ADisabledSwitchIgnoresItsRowAndScreenReaders()
    {
        var toggle = new Toggle { Look = ToggleLook.LookC, Enabled = false };
        int changed = 0;
        toggle.CheckedChanged += (_, _) => changed++;
        using var row = new SettingRow(null, "Start with Windows", "Turned off in Windows Settings", toggle) { Look = RowLook.LookC };
        var click = typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        foreach (Control part in new Control[] { row }.Concat(row.Controls.OfType<Label>()))
            click.Invoke(part, [EventArgs.Empty]);
        ((Automation.IToggleProvider)toggle.AutomationProvider!).Toggle();
        toggle.AccessibilityObject.DoDefaultAction();
        Assert.IsFalse(toggle.Checked);
        Assert.AreEqual(0, changed);
        Assert.AreEqual(Cursors.Default, row.Cursor, "no hand over the row");
        Assert.AreEqual(Cursors.Default, toggle.Cursor, "nor over the switch");
        toggle.Enabled = true;
        Assert.AreEqual(Cursors.Hand, row.Cursor);
        Assert.AreEqual(Cursors.Hand, toggle.Cursor);
        click.Invoke(row, [EventArgs.Empty]);
        Assert.IsTrue(toggle.Checked, "once it can be changed, a click on the row flips it");
        toggle.AccessibilityObject.DoDefaultAction();
        Assert.IsFalse(toggle.Checked, "and a screen reader's press");
        Assert.AreEqual(2, changed);
    }
}
