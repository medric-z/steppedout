using System.Drawing;
using System.Runtime.InteropServices;

namespace SteppedOut.Tests;

// The tray icon's pixel art, its minutes, its size for the tray, the icon handles it is shown with,
// and its tooltip. Bitmaps only: no tray icon is ever shown. The handle counts are per process, so
// these run one at a time.
[TestClass]
[DoNotParallelize]
public class TrayIconsTests
{
    static readonly TrayIconState[] States = [TrayIconState.On, TrayIconState.Paused, TrayIconState.CountingDown];

    // The sketch's 16 px icons (tray-sketch.html, symbols tray, tray-p and tray-c), copied as they are.
    const string SketchTile = "M2 0h12v1h1v1h1v12h-1v1h-1v1H2v-1H1v-1H0V2h1V1h1z";
    const string SketchLock = "M6 2h4v1H6zM5 3h2v1H5zM9 3h2v1H9zM4 4h2v3H4zM10 4h2v3h-2zM3 7h10v7H3z";
    const string SketchKeyhole = "M7 8h2v1H7zM7 9h3v1H7zM7 10h2v1H7zM6 11h2v2H6z";
    const string SketchBars = "M5 8h2v5H5zM9 8h2v5H9z";

    [TestMethod]
    public void EveryStateSizeAndTaskbarIsDrawnAtItsSizeInItsTwoColors()
    {
        foreach (var state in States)
        {
            foreach (int size in new[] { 16, 20, 24, 28, 32, 36, 40, 48 })
            {
                foreach (bool dark in new[] { false, true })
                {
                    using var art = TrayIcons.Art(state, size, dark);
                    Assert.AreEqual(new Size(size, size), art.Size, $"{state} {size}");
                    var (tile, glyph) = TrayArt.Colors(state, dark);
                    var seen = Pixels(art).Where(c => c.A != 0).Select(c => c.ToArgb()).Distinct().ToHashSet();
                    CollectionAssert.AreEquivalent(new[] { tile.ToArgb(), glyph.ToArgb() }, seen.ToArray(), $"{state} {size} {(dark ? "dark" : "light")}: only the tile and the padlock, no smoothing");
                    Assert.AreEqual(0, art.GetPixel(0, 0).A, "the rounded corner is clear");
                    // Up to the edge, or to the clear border of a size drawn from a smaller grid.
                    Assert.AreEqual(tile.ToArgb(), art.GetPixel(size / 2, TrayArt.LayoutFor(size).Inset).ToArgb(), "the tile reaches the top of the art");
                }
            }
        }
    }

    // D23: always the brand greens; counting down is the bright green on both taskbars, so it is not
    // the same as On; paused takes the taskbar's grays.
    [TestMethod]
    public void TheColorsAreD23s()
    {
        Assert.AreEqual((Color.FromArgb(0x0F, 0x6B, 0x54).ToArgb(), Color.FromArgb(0xF4, 0xF1, 0xEA).ToArgb()), Argb(TrayArt.Colors(TrayIconState.On, false)));
        Assert.AreEqual((Color.FromArgb(0x4F, 0xC9, 0xA4).ToArgb(), Color.FromArgb(0x0B, 0x2A, 0x22).ToArgb()), Argb(TrayArt.Colors(TrayIconState.CountingDown, false)));
        Assert.AreEqual((Color.FromArgb(0xC4, 0xC4, 0xC4).ToArgb(), Color.FromArgb(0x5F, 0x5F, 0x5F).ToArgb()), Argb(TrayArt.Colors(TrayIconState.Paused, false)));
        Assert.AreEqual((Color.FromArgb(0x5A, 0x5A, 0x5A).ToArgb(), Color.FromArgb(0xC5, 0xC5, 0xC5).ToArgb()), Argb(TrayArt.Colors(TrayIconState.Paused, true)));
        foreach (int size in new[] { 16, 20, 24 })
        {
            Same(TrayIcons.Art(TrayIconState.On, size, false), TrayIcons.Art(TrayIconState.On, size, true), "On is the same on both taskbars");
            Same(TrayIcons.Art(TrayIconState.CountingDown, size, false), TrayIcons.Art(TrayIconState.CountingDown, size, true), "counting down is the same on both taskbars");
            Assert.IsFalse(Equal(TrayIcons.Art(TrayIconState.On, size, false), TrayIcons.Art(TrayIconState.CountingDown, size, false)), "counting down differs from On");
            Assert.IsFalse(Equal(TrayIcons.Art(TrayIconState.Paused, size, false), TrayIcons.Art(TrayIconState.Paused, size, true)), "paused follows the taskbar");
        }
    }

    // Pixel for pixel the sketch's 16 px icon: its tile, its padlock, its keyhole and its pause bars.
    [TestMethod]
    public void TheSixteenPixelArtIsTheSketchs()
    {
        var grid = TrayArt.Grid16;
        Assert.AreEqual(Rasterize(SketchTile), Layer(grid, c => c != '.'));
        Assert.AreEqual(Rasterize(SketchLock), Layer(grid, c => c is 'L' or 'K' or 'P' or 'X'));
        Assert.AreEqual(Rasterize(SketchKeyhole), Layer(grid, c => c is 'K' or 'X'));
        Assert.AreEqual(Rasterize(SketchBars), Layer(grid, c => c is 'P' or 'X'));

        // And drawn so: the tile's color where the tile or a cut-out is, the padlock's elsewhere on it.
        foreach (var (state, holes) in new[] { (TrayIconState.On, SketchKeyhole), (TrayIconState.CountingDown, SketchKeyhole), (TrayIconState.Paused, SketchBars) })
        {
            using var art = TrayIcons.Art(state, 16, false);
            var (tile, glyph) = TrayArt.Colors(state, false);
            var tileMask = Rasterize(SketchTile).Split('\n');
            var lockMask = Rasterize(SketchLock).Split('\n');
            var holeMask = Rasterize(holes).Split('\n');
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    var want = tileMask[y][x] == '#' ? lockMask[y][x] == '#' && holeMask[y][x] != '#' ? glyph : tile : Color.Transparent;
                    Assert.AreEqual(want.A == 0 ? 0 : want.ToArgb(), art.GetPixel(x, y).A == 0 ? 0 : art.GetPixel(x, y).ToArgb(), $"{state} at {x},{y}");
                }
            }
        }
    }

    // The 20 and 24 px grids follow the same shapes: the tile, the shackle, the body and the pause
    // bars are the same on both sides; the keyhole, a tipped shoe print, is not meant to be.
    [TestMethod]
    public void TheLargerGridsAreSymmetricApartFromTheKeyhole()
    {
        foreach (var grid in TrayArt.Grids)
        {
            Assert.HasCount(grid.Size, grid.Rows);
            Assert.IsTrue(grid.Rows.All(r => r.Length == grid.Size), $"{grid.Size}: every row is {grid.Size} wide");
            foreach (var (name, keep) in new (string, Func<char, bool>)[] { ("tile", c => c != '.'), ("padlock", c => c is 'L' or 'K' or 'P' or 'X'), ("bars", c => c is 'P' or 'X') })
            {
                var layer = Layer(grid, keep).Split('\n');
                Assert.IsTrue(layer.All(r => r.SequenceEqual(r.Reverse())), $"{grid.Size}: the {name} is symmetric");
            }
            var keyhole = Layer(grid, c => c is 'K' or 'X');
            Assert.Contains("#", keyhole, $"{grid.Size} has a keyhole");
            Assert.IsTrue(Cut(grid, c => c is 'K' or 'X').All(p => Body(grid).Contains(p)), $"{grid.Size}: the keyhole is inside the body");
            Assert.IsTrue(Cut(grid, c => c is 'P' or 'X').All(p => Body(grid).Contains(p)), $"{grid.Size}: the bars are inside the body");
        }
    }

    // Sizes between the grids are drawn from the largest whole multiple of a grid that fits them, so
    // the tray never has to blur the art.
    [TestMethod]
    public void EachSizeIsDrawnFromAGrid()
    {
        var expect = new Dictionary<int, int> { [16] = 16, [20] = 20, [24] = 24, [28] = 24, [32] = 16, [36] = 16, [40] = 20, [48] = 24, [64] = 16, [12] = 16 };
        foreach (var (size, grid) in expect) Assert.AreEqual(grid, TrayArt.GridFor(size).Size, $"{size}");
        // 32 is the 16 px art with each pixel doubled.
        using var small = TrayIcons.Art(TrayIconState.On, 16, false);
        using var big = TrayIcons.Art(TrayIconState.On, 32, false);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
                Assert.AreEqual(small.GetPixel(x / 2, y / 2).ToArgb(), big.GetPixel(x, y).ToArgb());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TrayIcons.Art(TrayIconState.On, 0, false));
    }

    // Every size from 16 to 64 px, 175% (28) and 225% (36) among them, is a grid's art blown up by a
    // whole number, every pixel of the art a square of the same size, centered with clear pixels
    // around it: no stroke or digit is 1 px wide in one place and 2 in another (I10).
    [TestMethod]
    public void EveryStrokeIsTheSameWidthAtEverySize()
    {
        foreach (int size in Enumerable.Range(16, 49))
        {
            foreach (var minutes in new[] { null, "28" })
            {
                using var art = TrayIcons.Art(TrayIconState.On, size, false, minutes);
                var layout = TrayArt.LayoutFor(size);
                using var plain = TrayIcons.Art(TrayIconState.On, layout.Grid.Size, false, minutes);
                Assert.IsGreaterThanOrEqualTo(1, layout.Scale, $"{size}");
                Assert.IsGreaterThan(size - layout.Grid.Size, layout.Drawn, $"{size}: no smaller grid is drawn while a bigger one fits");
                Assert.IsLessThanOrEqualTo(1, Math.Abs(size - layout.Drawn - 2 * layout.Inset), $"{size}: centered");
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int gx = x - layout.Inset, gy = y - layout.Inset;
                        bool inside = gx >= 0 && gy >= 0 && gx < layout.Drawn && gy < layout.Drawn;
                        int want = inside ? plain.GetPixel(gx / layout.Scale, gy / layout.Scale).ToArgb() : 0;
                        if (art.GetPixel(x, y).ToArgb() != want) Assert.Fail($"{size} {minutes}: pixel {x},{y} is not the art's {gx / Math.Max(1, layout.Scale)},{gy / Math.Max(1, layout.Scale)}");
                    }
                }
            }
        }
        Assert.AreEqual(new TrayArt.Layout(TrayArt.Grid24, 1, 2), TrayArt.LayoutFor(28));
        Assert.AreEqual(new TrayArt.Layout(TrayArt.Grid16, 2, 2), TrayArt.LayoutFor(36));
        Assert.AreEqual(new TrayArt.Layout(TrayArt.Grid20, 2, 0), TrayArt.LayoutFor(40));
    }

    // Minutes 1 to 99 replace the keyhole, inside the body, each number its own picture, centered.
    [TestMethod]
    public void MinutesOneToNinetyNineAreDrawnInTheBody()
    {
        foreach (var grid in TrayArt.Grids)
        {
            var body = Body(grid);
            foreach (var state in new[] { TrayIconState.On, TrayIconState.CountingDown })
            {
                using var plain = TrayIcons.Art(state, grid.Size, false);
                var seen = new HashSet<string>();
                for (int n = 1; n <= 99; n++)
                {
                    string minutes = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    using var art = TrayIcons.Art(state, grid.Size, false, minutes);
                    var changed = Changed(plain, art).ToList();
                    Assert.IsNotEmpty(changed, $"{grid.Size} {minutes}");
                    Assert.IsTrue(changed.All(body.Contains), $"{grid.Size} {state} {minutes}: only the body changes");
                    Assert.IsTrue(seen.Add(string.Join(",", Pixels(art).Select(c => c.ToArgb()))), $"{grid.Size} {minutes} looks like another number");

                    // The digits are the tile-colored pixels in the body; centered within a pixel.
                    var (tile, _) = TrayArt.Colors(state, false);
                    var digits = Points(body).Where(p => art.GetPixel(p.X, p.Y).ToArgb() == tile.ToArgb()).ToList();
                    float middle = (digits.Min(p => p.X) + digits.Max(p => p.X)) / 2f;
                    float bodyMiddle = (body.Left + body.Right - 1) / 2f;
                    Assert.IsLessThanOrEqualTo(1f, Math.Abs(middle - bodyMiddle), $"{grid.Size} {minutes}: centered ({middle} vs {bodyMiddle})");
                    Assert.IsTrue(digits.Min(p => p.X) > body.Left && digits.Max(p => p.X) < body.Right - 1, $"{grid.Size} {minutes}: a pixel of body on each side");
                }
            }
        }
    }

    // Anything that is not "1" to "99" as IconMinutes writes them draws the plain icon and never throws.
    [TestMethod]
    public void AnythingElseDrawsThePlainIcon()
    {
        foreach (int size in new[] { 16, 20, 24, 32 })
        {
            foreach (var minutes in new[] { null, "", "0", "00", "100", "-1", "x", " 5", "5 ", "+5", "05", "٥", "1.5", "99999999999", "1\0" })
                Same(TrayIcons.Art(TrayIconState.On, size, false), TrayIcons.Art(TrayIconState.On, size, false, minutes), $"{size} \"{minutes}\"");
        }
        Assert.AreEqual(1, TrayIcons.Minutes("1"));
        Assert.AreEqual(10, TrayIcons.Minutes("10"));
        Assert.AreEqual(99, TrayIcons.Minutes("99"));
        Assert.IsNull(TrayIcons.Minutes("0"));
        Assert.IsNull(TrayIcons.Minutes("100"));
    }

    // Paused never shows minutes, even when a status that has not caught up still has some.
    [TestMethod]
    public void PausedNeverShowsMinutes()
    {
        foreach (int size in new[] { 16, 20, 24 })
            foreach (bool dark in new[] { false, true })
                Same(TrayIcons.Art(TrayIconState.Paused, size, dark), TrayIcons.Art(TrayIconState.Paused, size, dark, "28"), $"{size}");
    }

    // What IconMinutes gives for a status is what is drawn.
    [TestMethod]
    public void TheStatusMinutesAreDrawn()
    {
        var status = new LockStatus { Kind = LockState.On, Time = LockTime.Plain(30), SecondsLeft = 28 * 60 };
        Assert.AreEqual("28", Strings.IconMinutes(status));
        Assert.AreEqual(28, TrayIcons.Minutes(Strings.IconMinutes(status)));
        Assert.AreEqual(1, TrayIcons.Minutes(Strings.IconMinutes(status with { SecondsLeft = 17 })));
        Assert.IsNull(TrayIcons.Minutes(Strings.IconMinutes(status with { SecondsLeft = 120 * 60 })));
    }

    // Paused comes from the watcher's flag, which is at once; the status follows a second later.
    [TestMethod]
    public void TheStateFollowsThePausedFlagThenTheStatus()
    {
        LockStatus Of(LockState kind) => new() { Kind = kind, Time = LockTime.Plain(30) };
        Assert.AreEqual(TrayIconState.Paused, TrayIcons.StateOf(Of(LockState.On), paused: true), "paused a moment ago");
        Assert.AreEqual(TrayIconState.Paused, TrayIcons.StateOf(Of(LockState.Countdown), paused: true));
        Assert.AreEqual(TrayIconState.On, TrayIcons.StateOf(Of(LockState.Paused), paused: false), "turned back on a moment ago");
        Assert.AreEqual(TrayIconState.CountingDown, TrayIcons.StateOf(Of(LockState.Countdown), paused: false));
        foreach (var kind in new[] { LockState.Starting, LockState.On, LockState.Never, LockState.LockingOff, LockState.Away, LockState.SessionLocked })
            Assert.AreEqual(TrayIconState.On, TrayIcons.StateOf(Of(kind), paused: false), $"{kind}");
    }

    // The size Windows asks for at each scaling, drawn at exactly that size.
    [TestMethod]
    public void EachScalingGetsTheSizeWindowsAsksFor()
    {
        Assert.AreEqual(16, TrayIcons.SizeFor(96));
        Assert.AreEqual(20, TrayIcons.SizeFor(120));
        Assert.AreEqual(24, TrayIcons.SizeFor(144));
        Assert.AreEqual(32, TrayIcons.SizeFor(192));
        foreach (int dpi in new[] { 96, 120, 144, 168, 192, 216, 240, 288 })
        {
            int windows = GetSystemMetricsForDpi(49, (uint)dpi);
            Assert.AreEqual(windows, TrayIcons.SizeFor(dpi), $"{dpi}");
            using var art = TrayIcons.Art(TrayIconState.On, TrayIcons.SizeFor(dpi), false, "28");
            Assert.AreEqual(windows, art.Width, $"{dpi}");
        }
        Assert.AreEqual(16, TrayIcons.SizeFor(0), "an unreadable scaling counts as 100%");
        Assert.IsGreaterThanOrEqualTo(96, TrayIcons.CurrentDpi());
    }

    // 200 changes of the minutes on the icon, through a NotifyIcon that is never shown: the handle
    // counts stay where they were, every replaced icon is destroyed after the new one is shown, and
    // the last one when the slot goes.
    [TestMethod]
    public void TwoHundredMinuteChangesLeaveNoHandlesBehind()
    {
        using var notify = new NotifyIcon();
        var trace = new List<string>();
        using (var slot = new TrayIconSlot(icon => notify.Icon = icon) { Trace = trace.Add })
        {
            for (int i = 0; i < 20; i++) slot.Show(TrayIconState.On, 16 + i % 3 * 4, i % 2 == 0, (i % 99 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var process = Process();
            uint gdi = GetGuiResources(process, 0), user = GetGuiResources(process, 1);
            trace.Clear();
            for (int i = 0; i < 200; i++)
                Assert.IsTrue(slot.Show(TrayIconState.CountingDown, 24, false, (i % 99 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)), $"change {i}");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            int gdiGrew = (int)GetGuiResources(process, 0) - (int)gdi, userGrew = (int)GetGuiResources(process, 1) - (int)user;
            Assert.IsLessThanOrEqualTo(8, gdiGrew, $"GDI objects grew by {gdiGrew}");
            Assert.IsLessThanOrEqualTo(8, userGrew, $"USER objects grew by {userGrew}");
            int made = trace.Count(t => t == "create");
            Assert.AreEqual(made, trace.Count(t => t == "destroy"), "each new icon replaced one, which was destroyed");
            // Always the new one shown first, then the old one destroyed.
            for (int i = 0; i + 2 < trace.Count; i += 3)
                CollectionAssert.AreEqual(new[] { "create", "show", "destroy" }, trace.Skip(i).Take(3).ToArray(), $"step {i / 3}");
            Assert.AreSame(notify.Icon, slot.Current);
            trace.Clear();
        }
        CollectionAssert.AreEqual(new[] { "destroy" }, trace, "the last icon goes with the slot");
    }

    [TestMethod]
    public void TheSameIconIsNotMadeAgain()
    {
        var shown = new List<Icon>();
        var trace = new List<string>();
        using var slot = new TrayIconSlot(shown.Add) { Trace = trace.Add };
        Assert.IsTrue(slot.Show(TrayIconState.On, 16, false, "5"));
        Assert.IsFalse(slot.Show(TrayIconState.On, 16, false, "5"));
        Assert.IsFalse(slot.Show(TrayIconState.On, 16, false, "05".TrimStart('0')), "the same minutes");
        Assert.IsTrue(slot.Show(TrayIconState.On, 16, false, "6"));
        Assert.IsTrue(slot.Show(TrayIconState.On, 20, false, "6"), "a new size");
        Assert.IsTrue(slot.Show(TrayIconState.Paused, 20, false, "6"));
        Assert.IsFalse(slot.Show(TrayIconState.Paused, 20, false, "7"), "paused shows no minutes, so 7 changes nothing");
        Assert.IsTrue(slot.Show(TrayIconState.Paused, 20, true, null), "the taskbar turned dark");
        Assert.HasCount(5, shown);
    }

    // A tray that cannot take the new icon keeps the old one, and the new handle is not leaked.
    [TestMethod]
    public void AFailedSwapKeepsTheOldIcon()
    {
        int calls = 0;
        var trace = new List<string>();
        using var slot = new TrayIconSlot(_ =>
        {
            if (++calls == 2) throw new ObjectDisposedException("tray");
        })
        { Trace = trace.Add };
        Assert.IsTrue(slot.Show(TrayIconState.On, 16, false, null));
        var first = slot.Current;
        trace.Clear();
        Assert.IsFalse(slot.Show(TrayIconState.On, 16, false, "9"));
        Assert.AreSame(first, slot.Current);
        CollectionAssert.AreEqual(new[] { "create", "destroy" }, trace);
        Assert.IsTrue(slot.Show(TrayIconState.On, 16, false, "9"), "tried again on the next refresh");
    }

    // A tray that keeps refusing is written to the log once, not every second; once it works again
    // the next failure is written again.
    [TestMethod]
    public void AFailureThatLastsIsLoggedOnce()
    {
        bool refuse = true;
        using var slot = new TrayIconSlot(_ =>
        {
            if (refuse) throw new ExternalException("tray gone");
        });
        for (int minute = 1; minute <= 30; minute++)
            Assert.IsFalse(slot.Show(TrayIconState.On, 16, false, minute.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.AreEqual(1, slot.FailuresLogged);
        refuse = false;
        Assert.IsTrue(slot.Show(TrayIconState.On, 16, false, "31"));
        refuse = true;
        Assert.IsFalse(slot.Show(TrayIconState.On, 16, false, "32"));
        Assert.IsFalse(slot.Show(TrayIconState.On, 16, false, "33"));
        Assert.AreEqual(2, slot.FailuresLogged);
    }

    [TestMethod]
    public void AClosedSlotRefusesToShow()
    {
        var slot = new TrayIconSlot(_ => { });
        slot.Dispose();
        slot.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => slot.Show(TrayIconState.On, 16, false, null));
    }

    // ---- Tooltip ----

    static LockStatus NeverFor(string app) => new()
    {
        Kind = LockState.Never,
        Time = LockTime.Compute(30, [new ActiveEffect(Effect.Never, EffectSource.ForApp(app))]),
    };

    [TestMethod]
    public void TheTooltipIsTheAppsNameAndTheHeadline()
    {
        var on = new LockStatus { Kind = LockState.On, Time = LockTime.Plain(30), SecondsLeft = 28 * 60 };
        Assert.AreEqual("SteppedOut · On · locks in 28 min", TrayIcons.Tooltip(English.Text, on, lockingOn: true));
        Assert.AreEqual("\u202BSteppedOut · " + Arabic.Text.Headline(on) + "\u202C", TrayIcons.Tooltip(Arabic.Text, on, lockingOn: true));
        Assert.AreEqual("SteppedOut · " + English.Text.Headline(on with { DryRun = true }), TrayIcons.Tooltip(English.Text, on with { DryRun = true }, true));
    }

    // NotifyIcon.Text throws past 127 characters. The longest headline (never locking while an app
    // with a 200-character name is open, in a dry run) fits after the app's name, in English and in
    // Arabic with its marks, and a NotifyIcon takes it. The name is always whole, at the start (I42).
    [TestMethod]
    public void TheLongestTooltipFits()
    {
        using var notify = new NotifyIcon();
        foreach (var text in new[] { English.Text, Arabic.Text })
        {
            foreach (bool dry in new[] { false, true })
            {
                var status = NeverFor(new string('W', 200)) with { DryRun = dry };
                Assert.IsGreaterThan(127, text.Headline(status).Length + (text.RightToLeft ? 2 : 0), "the case is long enough to need cutting");
                string tip = TrayIcons.Tooltip(text, status, lockingOn: true);
                Assert.IsLessThanOrEqualTo(127, tip.Length);
                Assert.EndsWith(text.RightToLeft ? "…\u202C" : "…", tip);
                Assert.StartsWith(text.RightToLeft ? "\u202BSteppedOut · " : "SteppedOut · ", tip);
                StringAssert.Contains(tip, text.Headline(status)[..40], "the headline follows the name");
                notify.Text = tip;
            }
        }
        Assert.AreEqual(127, TrayIcons.Tooltip(English.Text, NeverFor(new string('W', 200)), true).Length, "cut to the full 127");
    }

    // A cut never splits a letter made of two UTF-16 units, wherever the cut lands.
    [TestMethod]
    public void TheCutKeepsLettersWhole()
    {
        using var notify = new NotifyIcon();
        foreach (var text in new[] { English.Text, Arabic.Text })
        {
            for (int at = 60; at <= 140; at++)
            {
                string app = new string('a', at) + "😀" + new string('b', 200);
                string tip = TrayIcons.Tooltip(text, NeverFor(app), lockingOn: true);
                Assert.IsLessThanOrEqualTo(127, tip.Length, $"{at}");
                Assert.IsTrue(WellFormed(tip), $"{(text.RightToLeft ? "ar" : "en")} emoji at {at}: {tip}");
                notify.Text = tip;
            }
        }
        Assert.AreEqual("abc…", TrayIcons.Fit("abcdef", 4));
        Assert.AreEqual("ab…", TrayIcons.Fit("ab😀def", 4), "the emoji does not fit whole, so it goes");
        Assert.AreEqual("ab😀…", TrayIcons.Fit("ab😀def", 5));
        Assert.AreEqual("abcdef", TrayIcons.Fit("abcdef", 6));
    }

    // ---- Helpers ----

    static (int, int) Argb((Color Tile, Color Lock) c) => (c.Tile.ToArgb(), c.Lock.ToArgb());

    static bool WellFormed(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]))
            {
                if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1])) return false;
                i++;
            }
            else if (char.IsLowSurrogate(s[i])) return false;
        }
        return true;
    }

    static IEnumerable<Color> Pixels(Bitmap b)
    {
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
                yield return b.GetPixel(x, y);
    }

    static bool Equal(Bitmap a, Bitmap b)
    {
        using (a)
        using (b)
            return a.Size == b.Size && Pixels(a).Select(c => c.ToArgb()).SequenceEqual(Pixels(b).Select(c => c.ToArgb()));
    }

    static void Same(Bitmap a, Bitmap b, string what) => Assert.IsTrue(Equal(a, b), what);

    static IEnumerable<Point> Changed(Bitmap a, Bitmap b)
    {
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
                if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb()) yield return new Point(x, y);
    }

    static IEnumerable<Point> Points(Rectangle r)
    {
        for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++)
                yield return new Point(x, y);
    }

    // The padlock's body: the rows where the padlock is widest.
    static Rectangle Body(TrayArt.Grid grid)
    {
        static int Width(string row) => row.Count(c => c is 'L' or 'K' or 'P' or 'X');
        int widest = grid.Rows.Max(Width);
        var rows = Enumerable.Range(0, grid.Size).Where(y => Width(grid.Rows[y]) == widest).ToList();
        int left = grid.Rows[rows[0]].IndexOfAny(['L', 'K', 'P', 'X']);
        return new Rectangle(left, rows[0], widest, rows[^1] - rows[0] + 1);
    }

    static IEnumerable<Point> Cut(TrayArt.Grid grid, Func<char, bool> keep)
    {
        for (int y = 0; y < grid.Size; y++)
            for (int x = 0; x < grid.Size; x++)
                if (keep(grid.Rows[y][x])) yield return new Point(x, y);
    }

    static string Layer(TrayArt.Grid grid, Func<char, bool> keep) =>
        string.Join("\n", grid.Rows.Select(r => new string(r.Select(c => keep(c) ? '#' : '.').ToArray())));

    // Fills an SVG path of straight lines (M, h, v, H, V, z) on a 16 px grid, as a browser fills it with
    // shape-rendering crispEdges: a pixel is in when its middle is inside a shape.
    static string Rasterize(string path)
    {
        var shapes = new List<List<PointF>>();
        List<PointF>? shape = null;
        float x = 0, y = 0;
        int i = 0;
        float Number()
        {
            int start = i;
            while (i < path.Length && (char.IsDigit(path[i]) || path[i] is '-' or '.')) i++;
            return float.Parse(path[start..i], System.Globalization.CultureInfo.InvariantCulture);
        }
        while (i < path.Length)
        {
            char c = path[i++];
            switch (c)
            {
                case 'M':
                    x = Number();
                    i++;
                    y = Number();
                    shape = [new(x, y)];
                    shapes.Add(shape);
                    break;
                case 'h': x += Number(); shape!.Add(new(x, y)); break;
                case 'v': y += Number(); shape!.Add(new(x, y)); break;
                case 'H': x = Number(); shape!.Add(new(x, y)); break;
                case 'V': y = Number(); shape!.Add(new(x, y)); break;
                case 'z': break;
                default: throw new FormatException($"{c} in {path}");
            }
        }
        var rows = new List<string>();
        for (int py = 0; py < 16; py++)
        {
            var row = new char[16];
            for (int px = 0; px < 16; px++)
                row[px] = shapes.Any(s => Inside(s, px + 0.5f, py + 0.5f)) ? '#' : '.';
            rows.Add(new string(row));
        }
        return string.Join("\n", rows);
    }

    static bool Inside(List<PointF> polygon, float x, float y)
    {
        bool inside = false;
        for (int a = 0, b = polygon.Count - 1; a < polygon.Count; b = a++)
        {
            var (p, q) = (polygon[a], polygon[b]);
            if (p.Y > y != q.Y > y && x < (q.X - p.X) * (y - p.Y) / (q.Y - p.Y) + p.X) inside = !inside;
        }
        return inside;
    }

    static IntPtr Process() => GetCurrentProcess();

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    [DllImport("user32.dll")]
    static extern uint GetGuiResources(IntPtr process, uint flags);

    [DllImport("user32.dll")]
    static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
