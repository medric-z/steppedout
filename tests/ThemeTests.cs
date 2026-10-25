namespace SteppedOut.Tests;

[TestClass]
public class ThemeTests
{
    // The palettes and text size as Windows and the defaults have them, after a test that chose others.
    static void Restore() => Theme.Choose(ThemeChoice.System, TextSize.System);

    // One test, and alone: the font cache is shared and not made for several threads at once, and
    // the text size changes every font in it.
    [TestMethod]
    [DoNotParallelize]
    public void FontsFollowTheMonitorScalingAndTheTextSizeMultipliesIt()
    {
        try
        {
            Theme.UseForShots(false, 1f);
            Assert.AreSame(Theme.Body(10f), Theme.Body(10f, Theme.SystemDpi));
            Assert.AreSame(Theme.Strong(12f), Theme.Strong(12f, Theme.SystemDpi));
            Assert.AreSame(Theme.Icons(13f), Theme.Icons(13f, Theme.SystemDpi));

            Assert.AreEqual(15f, Theme.Body(10f, Theme.SystemDpi * 3 / 2).Size, 0.001f);
            Assert.AreEqual(18f, Theme.Strong(12f, Theme.SystemDpi * 3 / 2).Size, 0.001f);
            Assert.AreEqual(6.5f, Theme.Icons(13f, Theme.SystemDpi / 2).Size, 0.001f);

            // Large text: 120% on top of the monitor's 150%.
            Theme.UseForShots(false, 1.2f);
            Assert.AreEqual(12f, Theme.Body(10f).Size, 0.001f);
            Assert.AreEqual(18f, Theme.Body(10f, Theme.SystemDpi * 3 / 2).Size, 0.001f);
            Assert.AreEqual(21.6f, Theme.Strong(12f, Theme.SystemDpi * 3 / 2).Size, 0.001f);
            Assert.AreSame(Theme.Body(10f), Theme.Body(10f, Theme.SystemDpi));

            // Small text, and back: the same fonts as before.
            Theme.UseForShots(false, 0.9f);
            Assert.AreEqual(9f, Theme.Body(10f).Size, 0.001f);
            var normal = Theme.Body(10f);
            Theme.UseForShots(false, 1f);
            Assert.AreEqual(10f, Theme.Body(10f).Size, 0.001f);
            Assert.AreNotSame(normal, Theme.Body(10f));
        }
        finally
        {
            Restore();
        }
    }

    [TestMethod]
    public void TheTextSizesAreNinetyAHundredAndAHundredAndTwentyPercentAndSystemFollowsWindows()
    {
        Assert.AreEqual(0.9f, Theme.ScaleFor(TextSize.Small, 150));
        Assert.AreEqual(1f, Theme.ScaleFor(TextSize.Medium, 150));
        Assert.AreEqual(1.2f, Theme.ScaleFor(TextSize.Large, 150));
        Assert.AreEqual(1f, Theme.ScaleFor(TextSize.System, 100));
        Assert.AreEqual(1.5f, Theme.ScaleFor(TextSize.System, 150));
        Assert.AreEqual(2.25f, Theme.ScaleFor(TextSize.System, 225));
        // Out of Windows' own range: the nearest end.
        Assert.AreEqual(1f, Theme.ScaleFor(TextSize.System, 0));
        Assert.AreEqual(2.25f, Theme.ScaleFor(TextSize.System, 500));
    }

    [TestMethod]
    public void AFixedThemeBeatsWindowsAppModeAndSystemFollowsIt()
    {
        Assert.IsFalse(Theme.UsesDark(ThemeChoice.Light, appsUseLightTheme: 0), "Light, with Windows dark");
        Assert.IsTrue(Theme.UsesDark(ThemeChoice.Dark, appsUseLightTheme: 1), "Dark, with Windows light");
        Assert.IsTrue(Theme.UsesDark(ThemeChoice.System, appsUseLightTheme: 0));
        Assert.IsFalse(Theme.UsesDark(ThemeChoice.System, appsUseLightTheme: 1));
    }

    // Choose takes the settings; Refresh, on a Windows change, keeps a fixed choice and says whether
    // anything it compares (palette, text size) is different. The accent is always the green.
    [TestMethod]
    [DoNotParallelize]
    public void AChoiceStaysThroughARefreshAndEveryInputCounts()
    {
        if (SystemInformation.HighContrast) Assert.Inconclusive("high contrast uses the system's colors whatever is chosen");
        try
        {
            Theme.Choose(ThemeChoice.Dark, TextSize.Medium);
            Assert.IsTrue(Theme.Current.IsDark);
            Assert.IsFalse(Theme.Refresh(), "nothing changed, so nothing to rebuild");
            Assert.IsTrue(Theme.Current.IsDark, "a refresh keeps Dark whatever Windows says");

            Assert.IsTrue(Theme.Choose(ThemeChoice.Light, TextSize.Medium), "the theme");
            Assert.IsFalse(Theme.Current.IsDark);
            Assert.IsFalse(Theme.Refresh());
            Assert.IsFalse(Theme.Current.IsDark, "a refresh keeps Light whatever Windows says");

            Assert.AreEqual(0x0F6B54, Theme.Current.Accent.ToArgb() & 0xFFFFFF, "the green");
            Assert.IsTrue(Theme.Choose(ThemeChoice.Light, TextSize.Large), "the text size");
            Assert.AreEqual(1.2f, Theme.TextScale);
            Assert.IsFalse(Theme.Choose(ThemeChoice.Light, TextSize.Large), "the same again");
            Assert.IsTrue(Theme.Choose(ThemeChoice.Light, TextSize.Small));
            Assert.AreEqual(0.9f, Theme.TextScale);
        }
        finally
        {
            Restore();
        }
    }

    [TestMethod]
    public void TheTaskbarIsDarkOnWindows10BeforeItHadALightOne()
    {
        Assert.IsTrue(Theme.TaskbarIsDark(17763, systemUsesLightTheme: 1), "1809, with the value set or missing");
        Assert.IsTrue(Theme.TaskbarIsDark(18362, systemUsesLightTheme: 0));
        Assert.IsFalse(Theme.TaskbarIsDark(18362, systemUsesLightTheme: 1));
        Assert.IsFalse(Theme.TaskbarIsDark(26200, systemUsesLightTheme: 1));
    }

    // The settings window's colors, light and dark, as the approved drawing has them. Some differ on
    // purpose: the drawing's switch that is off (BDBDBD with a white knob, 4A4A4A with CFCFCF) stands
    // out only 1.6 to 1.9:1 from the card and its knob, and these reach 3:1 (see the contrast tests
    // below); the dark accent stays today's green, 52C7A5, a shade from the drawing's 4FC9A4.
    [TestMethod]
    [DataRow(nameof(Theme.Hero), 0xE7F2EE, 0x1E3A33)]
    [DataRow(nameof(Theme.HeroEdge), 0xB5D6CA, 0x2F5A4E)]
    [DataRow(nameof(Theme.HeroIn), 0xFFFFFF, 0x192F29)]
    [DataRow(nameof(Theme.Soft), 0xEEF5F2, 0x23423A)]
    [DataRow(nameof(Theme.Off), 0x8C8C8C, 0x747474)]
    [DataRow(nameof(Theme.Knob), 0xFFFFFF, 0x0B2A22)]
    [DataRow(nameof(Theme.KnobOff), 0xFFFFFF, 0xD4D4D4)]
    [DataRow(nameof(Theme.Edge), 0xD4D4D4, 0x383838)]
    [DataRow(nameof(Theme.ControlEdge), 0xC4C4C4, 0x5A5A5A)]
    [DataRow(nameof(Theme.Accent), 0x0F6B54, 0x52C7A5)]
    public void TheSettingsColorsAreTheDrawnOnes(string token, int light, int dark)
    {
        var property = typeof(Theme).GetProperty(token)!;
        Assert.AreEqual(light, ((Color)property.GetValue(Theme.Light)!).ToArgb() & 0xFFFFFF, $"light {token}");
        Assert.AreEqual(dark, ((Color)property.GetValue(Theme.Dark)!).ToArgb() & 0xFFFFFF, $"dark {token}");
        Assert.AreEqual(light, ((Color)property.GetValue(Theme.For(false))!).ToArgb() & 0xFFFFFF, $"green light {token}");
        Assert.AreEqual(dark, ((Color)property.GetValue(Theme.For(true))!).ToArgb() & 0xFFFFFF, $"green dark {token}");
    }

    // High contrast is the system's colors only: every token is one of them, never a fixed color.
    [TestMethod]
    public void EveryTokenHasAHighContrastValueFromTheSystemColors()
    {
        var hc = Theme.HighContrast();
        Assert.IsTrue(hc.IsHighContrast);
        var tokens = typeof(Theme).GetProperties().Where(p => p.PropertyType == typeof(Color)).ToList();
        Assert.HasCount(22, tokens, "a new token needs a high contrast value here too");
        foreach (var token in tokens)
            Assert.IsTrue(((Color)token.GetValue(hc)!).IsSystemColor, $"{token.Name} in high contrast");
        // A switch that is on stays the highlight with its own text color for the knob.
        Assert.AreEqual(SystemColors.Highlight, hc.Accent);
        Assert.AreEqual(SystemColors.HighlightText, hc.Knob);
        Assert.AreEqual(SystemColors.WindowText, hc.Off);
        Assert.AreEqual(SystemColors.Window, hc.KnobOff);
    }

    // The WCAG 2 contrast ratio, worked out here as well, so the numbers below do not lean on the
    // app's own sums.
    static double Ratio(Color a, Color b)
    {
        static double Channel(int c)
        {
            double v = c / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    [TestMethod]
    public void ContrastIsTheWcagRatio()
    {
        Assert.AreEqual(21, Theme.Contrast(Color.Black, Color.White), 1e-9);
        Assert.AreEqual(1, Theme.Contrast(Color.Teal, Color.Teal), 1e-9);
        Assert.AreEqual(4.48, Theme.Contrast(Color.FromArgb(0x77, 0x77, 0x77), Color.White), 0.005);
        Assert.AreEqual(Ratio(Color.FromArgb(0x0F, 0x6B, 0x54), Color.White), Theme.Contrast(Color.White, Color.FromArgb(0x0F, 0x6B, 0x54)), 1e-9);
    }

    // The green in each palette: the accent itself, then the contrast of the words on it (at least
    // 4.5:1) and of the accent against the window (at least 3:1), as computed for these exact colors.
    [TestMethod]
    [DataRow(false, 0x0F6B54, 6.46, 5.82)]
    [DataRow(true, 0x52C7A5, 7.79, 7.81)]
    public void TheGreenHasReadableWordsAndStandsOutFromTheWindow(bool dark, int accent, double onAccent, double onWindow)
    {
        var t = Theme.For(dark);
        Assert.AreEqual(accent, t.Accent.ToArgb() & 0xFFFFFF);
        Assert.AreEqual(onAccent, Math.Round(Ratio(t.OnAccent, t.Accent), 2), "words on the accent");
        Assert.AreEqual(onWindow, Math.Round(Ratio(t.Accent, t.Window), 2), "the accent on the window");
        Assert.IsTrue(onAccent >= 4.5 && onWindow >= 3);
    }

    // Everything else that has to stand out, in every palette: the accent as words on the window,
    // the card and its own tints (the why line, the headings, a chosen menu row), the switch's knob
    // on it and the ring's arc against its track (3:1); words and quieter words on every surface
    // (4.5:1); the edge of a box you type in, and a switch that is off with its knob (3:1).
    [TestMethod]
    [DataRow(false, 5.59, 6.46, 4.13, 15.06, 5.88, 3.33, 3.03, 3.36)]
    [DataRow(true, 5.27, 7.36, 3.74, 10.98, 6.63, 3.65, 3.03, 3.15)]
    public void EveryPaletteKeepsItsContrast(bool dark, double accentAsWords, double knobOnAccent, double arcOnTrack,
        double words, double quietWords, double fieldEdge, double switchOff, double knobOff)
    {
        var t = Theme.For(dark);
        var surfaces = new[] { t.Window, t.Card, t.Hero, t.HeroIn, t.Soft, t.AccentSoft };
        Check(accentAsWords, 4.5, surfaces.Min(s => Ratio(t.Accent, s)), "the accent as words");
        Check(knobOnAccent, 3, Ratio(t.Knob, t.Accent), "the knob of a switch that is on");
        Check(arcOnTrack, 3, Ratio(t.Accent, t.HeroEdge), "the ring's arc on its track");
        Check(words, 4.5, surfaces.Append(t.Field).Min(s => Ratio(t.Text, s)), "words");
        Check(quietWords, 4.5, surfaces.Append(t.Field).Min(s => Ratio(t.Subtle, s)), "quieter words");
        Check(fieldEdge, 3, new[] { t.Field, t.Card, t.Window }.Min(s => Ratio(t.FieldLine, s)), "the edge of a box you type in");
        Check(switchOff, 3, new[] { t.Card, t.Window, t.HeroIn }.Min(s => Ratio(t.Off, s)), "a switch that is off");
        Check(knobOff, 3, Ratio(t.KnobOff, t.Off), "its knob");

        static void Check(double expected, double least, double actual, string what)
        {
            Assert.AreEqual(expected, Math.Round(actual, 2), what);
            Assert.IsGreaterThanOrEqualTo(least, actual, what);
        }
    }
}
