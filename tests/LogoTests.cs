using System.Drawing.Imaging;

namespace SteppedOut.Tests;

// The logo and the ring drawn around it. Alone: drawing uses the shared font and logo caches, and
// some of these switch the language or the palette for a moment.
[TestClass]
[DoNotParallelize]
public class LogoTests
{
    static int Differences(Bitmap a, Bitmap b, Rectangle box)
    {
        int count = 0;
        for (int y = box.Top; y < box.Bottom; y++)
            for (int x = box.Left; x < box.Right; x++)
                if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb()) count++;
        return count;
    }

    static bool Near(Color a, Color b, int tolerance = 12) =>
        Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;

    [TestMethod]
    public void AnArabicLogoIsTheSameAsTheEnglishOne()
    {
        bool arabic = Lang.Current.RightToLeft;
        try
        {
            foreach (int size in new[] { 16, 22, 32, 52, 64, 128 })
            {
                Lang.Use("en");
                using var english = Logo.Draw(size);
                Lang.Use("ar");
                using var inArabic = Logo.Draw(size);
                Assert.AreEqual(0, Differences(english, inArabic, new Rectangle(0, 0, size, size)), $"{size} px");
            }
        }
        finally
        {
            Lang.Use(arabic ? "ar" : "en");
        }
    }

    // The tile is the logo's green in the dark palette too; the print is cut out of the padlock
    // from 20 px up, and left out below.
    [TestMethod]
    public void TheTileStaysGreenAndThePrintShowsFromTwentyPixels()
    {
        try
        {
            Theme.UseForShots(true, 1f);
            using var big = Logo.Draw(128);
            Assert.AreEqual(Logo.Tile.ToArgb(), big.GetPixel(64, 10).ToArgb(), "the tile");
            Assert.AreEqual(Logo.Glyph.ToArgb(), big.GetPixel(40, 100).ToArgb(), "the padlock");
            // The middle of the print, upright around (64, 82) and tipped, is the tile's green.
            Assert.AreEqual(Logo.Tile.ToArgb(), big.GetPixel(64, 74).ToArgb(), "the print's front");
            Assert.AreEqual(Logo.Tile.ToArgb(), big.GetPixel(63, 95).ToArgb(), "the print's heel");
            // Tipped 16 degrees anticlockwise: the heel sits right of the front's line.
            Assert.IsTrue(Near(big.GetPixel(70, 98), Logo.Tile, 40), "the heel tips right");
            Assert.IsTrue(Near(big.GetPixel(56, 98), Logo.Glyph, 40), "and leaves the padlock left of it");

            using var twenty = Logo.Draw(20);
            Assert.IsTrue(Near(twenty.GetPixel(10, 11), Logo.Tile, 60), "a 20 px logo has the print");
            using var sixteen = Logo.Draw(16);
            Assert.IsTrue(Near(sixteen.GetPixel(8, 10), Logo.Glyph, 30), "a 16 px logo leaves it out");
        }
        finally
        {
            Theme.Choose(ThemeChoice.System, TextSize.System);
        }
    }

    static Theme Palette => Theme.Light;

    static Bitmap Ring(float fraction, bool rightToLeft, bool mirrored, int side = 84)
    {
        var picture = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(picture);
        g.Clear(Palette.Hero);
        if (mirrored)
        {
            // What a window laid out right to left does to everything drawn in it.
            g.TranslateTransform(side, 0);
            g.ScaleTransform(-1, 1);
        }
        SteppedOut.Ring.Draw(g, new Rectangle(0, 0, side, side), fraction, rightToLeft, mirrored, Palette);
        return picture;
    }

    static (int Left, int Right, int Top, int Bottom) Arc(Bitmap ring)
    {
        int left = 0, right = 0, top = 0, bottom = 0;
        int side = ring.Width, logo = (int)Math.Round(side * 0.62f), start = (int)Math.Round(side / 2f - logo / 2f);
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                if (x >= start && x < start + logo && y >= start && y < start + logo) continue;
                if (!Near(ring.GetPixel(x, y), Palette.Accent, 24)) continue;
                if (x < side / 2) left++;
                else right++;
                if (y < side / 2) top++;
                else bottom++;
            }
        return (left, right, top, bottom);
    }

    // The arc runs clockwise from 12 o'clock, and the other way in Arabic, whether the ring turns its
    // own arc round or the window mirrors it; the logo inside is never mirrored.
    [TestMethod]
    public void TheArcRunsTheReadingWayAndTheLogoNeverMirrors()
    {
        using var english = Ring(0.25f, rightToLeft: false, mirrored: false);
        using var arabic = Ring(0.25f, rightToLeft: true, mirrored: false);
        using var arabicWindow = Ring(0.25f, rightToLeft: true, mirrored: true);

        var cw = Arc(english);
        Assert.IsTrue(cw.Right > 10 * cw.Left && cw.Top > 10 * cw.Bottom, $"a quarter clockwise from 12: {cw}");
        foreach (var (name, picture) in new[] { ("turned by the ring", arabic), ("mirrored by the window", arabicWindow) })
        {
            var ccw = Arc(picture);
            Assert.IsTrue(ccw.Left > 10 * ccw.Right && ccw.Top > 10 * ccw.Bottom, $"{name}: a quarter anticlockwise from 12: {ccw}");
            Assert.AreEqual(cw.Left + cw.Right, ccw.Left + ccw.Right, (cw.Left + cw.Right) / 20.0, $"{name}: as long");
            // The tile and padlock, inside the track, pixel for pixel.
            int logo = (int)Math.Round(84 * 0.62f), start = (84 - logo) / 2;
            Assert.AreEqual(0, Differences(english, picture, new Rectangle(start + 4, start + 4, logo - 8, logo - 8)), $"{name}: the logo");
        }
    }

    [TestMethod]
    public void AnEmptyRingHasNoArcAndAFullOneGoesAllRound()
    {
        foreach (int side in new[] { SteppedOut.Ring.Large, SteppedOut.Ring.Small })
        {
            using var empty = Ring(0f, false, false, side);
            var none = Arc(empty);
            Assert.AreEqual(0, none.Left + none.Right, $"{side}: empty");
            using var full = Ring(1f, false, false, side);
            var all = Arc(full);
            Assert.IsTrue(all.Left > 0 && all.Right > 0 && all.Top > 0 && all.Bottom > 0, $"{side}: full {all}");
            Assert.AreEqual(all.Left, all.Right, all.Left / 10.0, $"{side}: even");
            using var started = Ring(1f / 30, false, false, side);
            Assert.IsGreaterThan(0, Arc(started).Right, $"{side}: a minute of 30 shows");
        }
    }

    // The four contrast themes Windows ships (C:\Windows\Resources\Ease of Access Themes): Window,
    // WindowText, Hilight and HilightText.
    static readonly (string Name, Theme Theme)[] ContrastThemes =
    {
        ("hcblack", Theme.HighContrastOf(Color.FromArgb(32, 32, 32), Color.FromArgb(255, 255, 255), Color.FromArgb(142, 227, 240), Color.FromArgb(38, 59, 80))),
        ("hcwhite", Theme.HighContrastOf(Color.FromArgb(255, 250, 239), Color.FromArgb(61, 61, 61), Color.FromArgb(144, 57, 9), Color.FromArgb(255, 245, 227))),
        ("hc1", Theme.HighContrastOf(Color.FromArgb(45, 50, 54), Color.FromArgb(255, 255, 255), Color.FromArgb(161, 191, 222), Color.FromArgb(33, 45, 59))),
        ("hc2", Theme.HighContrastOf(Color.FromArgb(0, 0, 0), Color.FromArgb(255, 255, 255), Color.FromArgb(214, 180, 253), Color.FromArgb(43, 43, 43))),
    };

    // How much of a pixel a stroke of this color covers on this background, from 0 to 1.
    static double Cover(Color pixel, Color stroke, Color background)
    {
        double dr = stroke.R - background.R, dg = stroke.G - background.G, db = stroke.B - background.B;
        double along = (pixel.R - background.R) * dr + (pixel.G - background.G) * dg + (pixel.B - background.B) * db;
        return Math.Clamp(along / (dr * dr + dg * dg + db * db), 0, 1);
    }

    // The width of the track (left of the logo) and of the arc (right of it) along the middle row of a
    // ring half full, which runs clockwise from 12 to 6 o'clock.
    static (double Track, double Arc) Widths(Theme theme, int side)
    {
        using var picture = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(picture))
        {
            g.Clear(theme.Hero);
            SteppedOut.Ring.Draw(g, new Rectangle(0, 0, side, side), 0.5f, false, false, theme);
        }
        int logo = (int)Math.Round(side * 0.62f), start = (int)Math.Round(side / 2f - logo / 2f), y = side / 2;
        double track = 0, arc = 0;
        for (int x = 0; x < start; x++) track += Cover(picture.GetPixel(x, y), theme.HeroEdge, theme.Hero);
        for (int x = start + logo; x < side; x++) arc += Cover(picture.GetPixel(x, y), theme.Accent, theme.Hero);
        return (track, arc);
    }

    // In high contrast the arc's highlight and the track's text color are only 1.4 to 1.9:1 apart, so
    // the track is a thin line and the arc at least twice as thick; each still stands 3:1 from the
    // window. The other palettes keep a track as thick as the arc.
    [TestMethod]
    public void InHighContrastTheArcIsThickerThanItsTrack()
    {
        foreach (var (name, theme) in ContrastThemes.Append(("this PC's system colors", Theme.HighContrast())))
        {
            Assert.IsGreaterThanOrEqualTo(3.0, Theme.Contrast(theme.Accent, theme.Hero), $"{name}: arc against the window");
            Assert.IsGreaterThanOrEqualTo(3.0, Theme.Contrast(theme.HeroEdge, theme.Hero), $"{name}: track against the window");
            foreach (int side in new[] { SteppedOut.Ring.Large, SteppedOut.Ring.Small })
            {
                var (track, arc) = Widths(theme, side);
                Assert.IsGreaterThanOrEqualTo(0.8, track, $"{name} {side}: a track still shows ({track:0.00} px)");
                Assert.IsGreaterThanOrEqualTo(2 * track, arc, $"{name} {side}: arc {arc:0.00} px, track {track:0.00} px");
            }
        }
        // The colors alone would not do: worked out from the shipped themes.
        foreach (var (name, theme) in ContrastThemes)
            Assert.IsLessThan(3.0, Theme.Contrast(theme.Accent, theme.HeroEdge), $"{name}: arc against track");
        foreach (var theme in new[] { Theme.Light, Theme.Dark })
            foreach (int side in new[] { SteppedOut.Ring.Large, SteppedOut.Ring.Small })
            {
                var (track, arc) = Widths(theme, side);
                Assert.AreEqual(arc, track, 0.5, $"{(theme.IsDark ? "dark" : "light")} {side}: as thick");
            }
    }

    static LockStatus Status(LockState kind, decimal minutes, double idleSeconds) =>
        new() { Kind = kind, Time = LockTime.Plain(minutes), IdleSeconds = idleSeconds };

    // Idle time over the lock time while it counts; empty while it never locks or is paused (D19).
    [TestMethod]
    public void TheRingShowsHowMuchOfTheLockTimeHasPassed()
    {
        Assert.AreEqual(0.2f, SteppedOut.Ring.FractionOf(Status(LockState.On, 30, 6 * 60)), 1e-6f);
        Assert.AreEqual(0.5f, SteppedOut.Ring.FractionOf(Status(LockState.Countdown, 1, 30)), 1e-6f);
        Assert.AreEqual(1f, SteppedOut.Ring.FractionOf(Status(LockState.Countdown, 1, 75)), "a late countdown is full");
        Assert.AreEqual(0.1f, SteppedOut.Ring.FractionOf(Status(LockState.LockingOff, 10, 60)), 1e-6f);
        Assert.AreEqual(0f, SteppedOut.Ring.FractionOf(Status(LockState.Paused, 30, 600)));
        Assert.AreEqual(0f, SteppedOut.Ring.FractionOf(Status(LockState.Never, 30, 600)));
        Assert.AreEqual(0f, SteppedOut.Ring.FractionOf(Status(LockState.Starting, 30, 0)));
        Assert.AreEqual(1f, SteppedOut.Ring.FractionOf(Status(LockState.Away, 30, 1900)));
        Assert.AreEqual(1f, SteppedOut.Ring.FractionOf(Status(LockState.SessionLocked, 30, 0)));
        var never = new LockStatus { Kind = LockState.On, Time = LockTime.Compute(30, new[] { new ActiveEffect(Effect.Never, EffectSource.Call) }), IdleSeconds = 60 };
        Assert.AreEqual(0f, SteppedOut.Ring.FractionOf(never), "no lock time");

        var ring = new SteppedOut.Ring { Fraction = 2f };
        Assert.AreEqual(1f, ring.Fraction);
        ring.Fraction = float.NaN;
        Assert.AreEqual(0f, ring.Fraction);
        Assert.AreEqual(84, ring.Diameter);
        Assert.AreEqual(36, new SteppedOut.Ring(SteppedOut.Ring.Small).Diameter);
        Assert.IsFalse(ring.TabStop, "nothing to do with the keyboard");
    }
}
