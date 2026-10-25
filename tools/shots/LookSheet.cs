using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SteppedOut;

// Sheets of the look pieces (the logo, the ring, the switches and the palettes), each in every size
// and state, for comparing against the design sketch, and the checks that go with them: the logo is
// the same in Arabic and never mirrored, the ring's arc runs the reading way, a switch's word never
// moves its track, and the colors keep their contrast. Pictures are named
// look-<what>-<language>-<theme>.png and the checks go to look-check.txt. With LOOK_REF set to the
// folder of the sketch's renders, compare/ gets the ring and the switches beside the sketch's.
static class LookSheet
{
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    const int ExStyle = -20;
    const int LayoutRtlStyle = 0x00400000;
    const uint RenderFullContent = 2;

    // 2 of 30 minutes gone is the sketch's ring: "locks in 28 min".
    static readonly float[] Fractions = { 0f, 2f / 30, 0.25f, 0.5f, 0.75f, 1f };

    static string outDir = "";
    static bool pictures;
    static bool firstRun = true;
    static int failures;
    static TextWriter? report;

    public static void Run(string outDir, string lang, string theme)
    {
        LookSheet.outDir = outDir;
        pictures = true;
        string file = Path.Combine(outDir, "look-check.txt");
        if (firstRun)
        {
            File.Delete(file);
            firstRun = false;
        }
        using (report = File.AppendText(file))
        {
            Each($"{lang}-{theme}");
        }
        report = null;
        Console.WriteLine(failures == 0 ? "look: all checks passed" : $"look: {failures} checks failed");
    }

    // The same checks without pictures, for the window checks: both languages, light and dark.
    // Returns how many failed, and leaves the light palette in use.
    public static int Check()
    {
        pictures = false;
        failures = 0;
        foreach (var lang in new[] { "en", "ar" })
        {
            foreach (var dark in new[] { false, true })
            {
                Lang.Use(lang);
                Theme.UseForShots(dark);
                Each($"{lang}-{(dark ? "dark" : "light")}");
            }
        }
        Theme.UseForShots(false);
        Console.WriteLine(failures == 0 ? "look: all checks passed" : $"look: {failures} checks failed");
        return failures;
    }

    static void Each(string tag)
    {
        Say($"--- {tag}");
        bool rtl = Lang.Current.RightToLeft;
        Logos(tag);
        var ring = Rings(tag, rtl);
        ContrastRings(tag, rtl);
        var toggles = Toggles(tag, rtl, 1f);
        bool dark = Theme.Current.IsDark;
        // Large text, and Windows' text size at 150%, 200% and 225%.
        foreach (var (scale, suffix) in new[] { (1.2f, "-large"), (1.5f, "-text150"), (2f, "-text200"), (2.25f, "-text225") })
        {
            Theme.UseForShots(dark, scale);
            Toggles(tag + suffix, rtl, scale).Picture.Dispose();
        }
        Theme.UseForShots(dark);
        Palettes(tag);
        if (pictures) Compare(tag, rtl, ring, toggles);
        ring.Picture.Dispose();
        toggles.Picture.Dispose();
    }

    static void Is(bool ok, string what)
    {
        Say((ok ? "ok   " : "FAIL ") + what);
        if (!ok) failures++;
    }

    static void Say(string line)
    {
        Console.WriteLine(line);
        report?.WriteLine(line);
    }

    // ---- The logo ----

    static readonly int[] LogoSizes = { 16, 20, 22, 24, 32, 48, 52, 64, 128 };

    static void Logos(string tag)
    {
        var t = Theme.Current;
        // Drawn with each language in turn: the logo has no Arabic version.
        var was = Lang.Current.RightToLeft ? "ar" : "en";
        bool same = true;
        foreach (int size in LogoSizes)
        {
            Lang.Use("en");
            using var en = Logo.Draw(size);
            Lang.Use("ar");
            using var ar = Logo.Draw(size);
            same &= Differences(en, ar, new Rectangle(0, 0, size, size), new Rectangle(0, 0, size, size)) == 0;
        }
        Lang.Use(was);
        Is(same, $"logo: drawn in Arabic it is the same, pixel for pixel, at {string.Join(", ", LogoSizes)} px");

        if (!pictures) return;
        int gap = 16;
        int width = gap + LogoSizes.Sum(s => s + gap);
        using var sheet = new Bitmap(width, 128 + 2 * gap + 20);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(t.Window);
            int x = gap;
            foreach (int size in LogoSizes)
            {
                using var logo = Logo.Draw(size);
                g.DrawImageUnscaled(logo, x, gap + 128 - size);
                TextRenderer.DrawText(g, size.ToString(), Theme.Body(8f), new Point(x, gap + 132), t.Subtle);
                x += size + gap;
            }
        }
        Save(sheet, $"look-logo-{tag}");
    }

    // ---- The ring ----

    sealed record Sheet(Bitmap Picture, Dictionary<string, Rectangle> Boxes);

    static Sheet Rings(string tag, bool rtl)
    {
        var t = Theme.Current;
        // The same rings left to right, in a window laid out right to left (as the app's are), and
        // right to left in one that is not, where each ring turns its own arc round.
        var ltr = RingWindow(RightToLeft.No, layout: false);
        var mirrored = RingWindow(RightToLeft.Yes, layout: true);
        var flipped = RingWindow(RightToLeft.Yes, layout: false);

        foreach (var (name, rtlSheet) in new[] { ("mirrored window", mirrored), ("unmirrored window", flipped) })
        {
            int worst = 0;
            foreach (var key in ltr.Boxes.Keys)
                worst = Math.Max(worst, Differences(ltr.Picture, rtlSheet.Picture, LogoBox(ltr.Boxes[key]), LogoBox(rtlSheet.Boxes[key]), InsideTrack(ltr.Boxes[key])));
            Is(worst == 0, $"ring, right to left in a {name}: the logo is the same as left to right, not mirrored ({worst} pixels differ inside the track)");
            var quarterLtr = Sides(ltr, "84-0.25", t.Accent);
            var quarterRtl = Sides(rtlSheet, "84-0.25", t.Accent);
            // The round end at 12 o'clock reaches a little past it.
            Is(quarterLtr.Right > 10 * quarterLtr.Left && quarterRtl.Left > 10 * quarterRtl.Right,
                $"ring, right to left in a {name}: a quarter runs from 12 o'clock to the left (left to right {quarterLtr.Left}/{quarterLtr.Right} accent pixels left/right, right to left {quarterRtl.Left}/{quarterRtl.Right})");
            var arcLtr = Arc(ltr, "84-0.25", t.Accent);
            var arcRtl = Arc(rtlSheet, "84-0.25", t.Accent);
            Is(Math.Abs(arcLtr - arcRtl) <= arcLtr / 20 + 2, $"ring, right to left in a {name}: the quarter arc is as long as left to right ({arcRtl} and {arcLtr} accent pixels)");
        }
        foreach (int size in new[] { Ring.Large, Ring.Small })
        {
            var empty = Sides(ltr, $"{size}-0", t.Accent);
            Is(empty.Left + empty.Right == 0, $"ring {size}: empty shows no accent ({empty.Left + empty.Right} pixels)");
            var full = Quarters(ltr, $"{size}-1", t.Accent);
            Is(full.All(q => q > 0), $"ring {size}: full shows the accent all round ({string.Join("/", full)} pixels by quarter)");
            var started = Sides(ltr, $"{size}-{2f / 30}", t.Accent);
            Is(started.Right > 0, $"ring {size}: 2 minutes of 30 shows a short arc right of 12 o'clock ({started.Right} pixels)");
        }
        Is(Ring.FractionOf(Status(LockState.On, 30, 6 * 60)) == 0.2f, "ring: 6 minutes idle of 30 fills a fifth");
        Is(Ring.FractionOf(Status(LockState.Paused, 30, 6 * 60)) == 0f && Ring.FractionOf(Status(LockState.Never, 30, 6 * 60)) == 0f,
            "ring: empty while paused and while it never locks (D19)");

        var keep = rtl ? mirrored : ltr;
        if (pictures)
        {
            Save(keep.Picture, $"look-ring-{tag}");
            if (rtl) Save(flipped.Picture, $"look-ring-unmirrored-{tag}");
        }
        foreach (var other in new[] { ltr, mirrored, flipped })
            if (other != keep) other.Picture.Dispose();
        return keep;
    }

    // The four contrast themes Windows ships (Ease of Access Themes): Window, WindowText, Hilight and
    // HilightText.
    static readonly (string Name, Theme Theme)[] ContrastThemes =
    {
        ("hcblack", Theme.HighContrastOf(Color.FromArgb(32, 32, 32), Color.FromArgb(255, 255, 255), Color.FromArgb(142, 227, 240), Color.FromArgb(38, 59, 80))),
        ("hcwhite", Theme.HighContrastOf(Color.FromArgb(255, 250, 239), Color.FromArgb(61, 61, 61), Color.FromArgb(144, 57, 9), Color.FromArgb(255, 245, 227))),
        ("hc1", Theme.HighContrastOf(Color.FromArgb(45, 50, 54), Color.FromArgb(255, 255, 255), Color.FromArgb(161, 191, 222), Color.FromArgb(33, 45, 59))),
        ("hc2", Theme.HighContrastOf(Color.FromArgb(0, 0, 0), Color.FromArgb(255, 255, 255), Color.FromArgb(214, 180, 253), Color.FromArgb(43, 43, 43))),
    };

    // The rings in each contrast theme, where the arc and the track are too close in color to tell
    // apart by color alone, so the track is a thin line under a thick arc.
    static void ContrastRings(string tag, bool rtl)
    {
        const int gap = 16, label = 72;
        int width = gap + label + Fractions.Length * (Ring.Large + gap) + Fractions.Length * (Ring.Small + gap);
        int rowHeight = Ring.Large + gap;
        using var picture = new Bitmap(width, ContrastThemes.Length * rowHeight + gap, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(picture))
        {
            g.Clear(Color.White);
            for (int i = 0; i < ContrastThemes.Length; i++)
            {
                var (name, t) = ContrastThemes[i];
                int y = i * rowHeight;
                using (var back = new SolidBrush(t.Window)) g.FillRectangle(back, 0, y, width, rowHeight + (i == ContrastThemes.Length - 1 ? gap : 0));
                TextRenderer.DrawText(g, name, Theme.Body(9f), new Point(gap, y + gap + Ring.Large / 2 - 8), t.Text, t.Window);
                int x = gap + label;
                foreach (int size in new[] { Ring.Large, Ring.Small })
                    foreach (var fraction in Fractions)
                    {
                        Ring.Draw(g, new Rectangle(x, y + gap + (Ring.Large - size) / 2, size, size), fraction, rtl, false, t);
                        x += size + gap;
                    }
            }
        }
        foreach (var (name, t) in ContrastThemes)
            foreach (int size in new[] { Ring.Large, Ring.Small })
            {
                var (track, arc) = Widths(t, size);
                Is(track >= 0.8 && arc >= 2 * track,
                    $"ring {size} in {name}: the arc is {arc:0.0} px thick over a {track:0.0} px track (arc against track {Theme.Contrast(t.Accent, t.HeroEdge):0.00}:1, arc against the window {Theme.Contrast(t.Accent, t.Window):0.00}:1, track against the window {Theme.Contrast(t.HeroEdge, t.Window):0.00}:1)");
            }
        if (pictures) Save(picture, $"look-ring-hc-{tag}");
    }

    // How much of a pixel a stroke of this color covers on this background, from 0 to 1.
    static double Cover(Color pixel, Color stroke, Color background)
    {
        double dr = stroke.R - background.R, dg = stroke.G - background.G, db = stroke.B - background.B;
        double along = (pixel.R - background.R) * dr + (pixel.G - background.G) * dg + (pixel.B - background.B) * db;
        return Math.Clamp(along / (dr * dr + dg * dg + db * db), 0, 1);
    }

    // The width of the track (left of the logo) and of the arc (right of it) along the middle row of a
    // ring half full.
    static (double Track, double Arc) Widths(Theme t, int side)
    {
        using var picture = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(picture))
        {
            g.Clear(t.Hero);
            Ring.Draw(g, new Rectangle(0, 0, side, side), 0.5f, false, false, t);
        }
        int logo = (int)Math.Round(side * 0.62f), start = (int)Math.Round(side / 2f - logo / 2f), y = side / 2;
        double track = 0, arc = 0;
        for (int x = 0; x < start; x++) track += Cover(picture.GetPixel(x, y), t.HeroEdge, t.Hero);
        for (int x = start + logo; x < side; x++) arc += Cover(picture.GetPixel(x, y), t.Accent, t.Hero);
        return (track, arc);
    }

    static LockStatus Status(LockState kind, int minutes, double idleSeconds) =>
        new() { Kind = kind, Time = LockTime.Plain(minutes), IdleSeconds = idleSeconds };

    static Sheet RingWindow(RightToLeft direction, bool layout)
    {
        var f = Window(direction, layout, Theme.Current.Hero);
        var rings = new List<(string Key, Ring Ring)>();
        foreach (int size in new[] { Ring.Large, Ring.Small })
            foreach (var fraction in Fractions)
            {
                var ring = new Ring(size) { Fraction = fraction };
                f.Controls.Add(ring);
                rings.Add(($"{size}-{fraction}", ring));
            }
        f.Show();
        int gap = P(f, 16), x = gap, y = gap, row = 0;
        foreach (var (key, ring) in rings)
        {
            var size = ring.GetPreferredSize(Size.Empty);
            if (ring.Diameter != row)
            {
                if (row != 0) y += P(f, row) + gap;
                row = ring.Diameter;
                x = gap;
            }
            ring.Bounds = new Rectangle(x, y, size.Width, size.Height);
            x += size.Width + gap;
        }
        f.ClientSize = new Size(rings.Where(r => r.Ring.Diameter == Ring.Large).Max(r => r.Ring.Right) + gap, y + P(f, row) + gap);
        return Shoot(f, rings.ToDictionary(r => r.Key, r => (Control)r.Ring));
    }

    // Where Ring.Draw puts the logo in a ring's box.
    static Rectangle LogoBox(Rectangle ring)
    {
        int side = Math.Min(ring.Width, ring.Height);
        int logo = (int)Math.Round(side * 0.62f);
        return new Rectangle(ring.X + (int)Math.Round(ring.Width / 2f - logo / 2f), ring.Y + (int)Math.Round(ring.Height / 2f - logo / 2f), logo, logo);
    }

    // Accent pixels outside the logo, left and right of the ring's middle.
    static (int Left, int Right) Sides(Sheet sheet, string key, Color accent)
    {
        var box = sheet.Boxes[key];
        var logo = LogoBox(box);
        int left = 0, right = 0;
        for (int y = box.Top; y < box.Bottom; y++)
            for (int x = box.Left; x < box.Right; x++)
            {
                if (logo.Contains(x, y) || !Near(sheet.Picture.GetPixel(x, y), accent, 24)) continue;
                if (x < box.Left + box.Width / 2) left++;
                else right++;
            }
        return (left, right);
    }

    static int Arc(Sheet sheet, string key, Color accent)
    {
        var (left, right) = Sides(sheet, key, accent);
        return left + right;
    }

    static int[] Quarters(Sheet sheet, string key, Color accent)
    {
        var box = sheet.Boxes[key];
        var logo = LogoBox(box);
        var count = new int[4];
        for (int y = box.Top; y < box.Bottom; y++)
            for (int x = box.Left; x < box.Right; x++)
            {
                if (logo.Contains(x, y) || !Near(sheet.Picture.GetPixel(x, y), accent, 24)) continue;
                count[(x < box.Left + box.Width / 2 ? 0 : 1) + (y < box.Top + box.Height / 2 ? 0 : 2)]++;
            }
        return count;
    }

    // ---- The switches ----

    static readonly ToggleLook[] Looks = { ToggleLook.Classic, ToggleLook.LookC, ToggleLook.Compact };

    static Sheet Toggles(string tag, bool rtl, float scale)
    {
        var t = Theme.Current;
        var f = Window(Lang.Direction, Lang.Current.RightToLeft, t.Card);
        var made = new List<(string Key, Toggle Toggle)>();
        foreach (var look in Looks)
            foreach (bool label in new[] { false, true })
                foreach (bool on in new[] { false, true })
                {
                    var toggle = new Toggle { Look = look, ShowLabel = label, Checked = on, AccessibleName = "Count a game controller" };
                    f.Controls.Add(toggle);
                    made.Add(($"{look}-{(label ? "label" : "plain")}-{(on ? "on" : "off")}", toggle));
                }
        f.Show();
        int gap = P(f, 16), column = 0, x = gap, y = gap;
        foreach (var (_, toggle) in made) column = Math.Max(column, toggle.GetPreferredSize(Size.Empty).Width);
        foreach (var look in Looks)
        {
            y = gap;
            foreach (var (key, toggle) in made.Where(m => m.Toggle.Look == look))
            {
                // Each switch at the end of its column, as at the end of a row.
                toggle.Size = toggle.GetPreferredSize(Size.Empty);
                toggle.Location = new Point(x + column - toggle.Width, y);
                y += toggle.Height + P(f, 10);
            }
            x += column + gap;
        }
        f.ClientSize = new Size(x, y + gap);

        // What screen readers meet: the switch alone, named for its row, with no word inside it.
        var probe = made.First(m => m.Toggle.ShowLabel).Toggle;
        var name = (probe.AutomationProvider as Automation.IRawElementProviderSimple)?.GetPropertyValue(Automation.NameProperty) as string;
        Is(probe.Controls.Count == 0 && name == "Count a game controller",
            $"switch{Suffix(scale)}: the word is drawn, not a control, so screen readers hear only \"{name}\" and the state");
        int dpi = f.DeviceDpi;
        var classic = new Toggle();
        f.Controls.Add(classic);
        Is(classic.GetPreferredSize(Size.Empty) == new Size(48 * dpi / 96, 28 * dpi / 96),
            $"switch{Suffix(scale)}: the classic switch keeps the welcome window's size, 48 by 28 at 100%");
        f.Controls.Remove(classic);
        classic.Dispose();

        var sheet = Shoot(f, made.ToDictionary(m => m.Key, m => (Control)m.Toggle));
        // Look C, plain and compact; the welcome's classic switch never shows its word.
        foreach (var look in new[] { ToggleLook.LookC, ToggleLook.Compact })
        {
            var onBox = sheet.Boxes[$"{look}-label-on"];
            var offBox = sheet.Boxes[$"{look}-label-off"];
            // The track is looked for at the switch's end, as wide as the switch without its word:
            // the word's softened edges in the dark theme are near the off track's grey.
            int plain = sheet.Boxes[$"{look}-plain-off"].Width;
            Rectangle End(Rectangle box) => rtl ? new Rectangle(box.Left, box.Top, plain, box.Height) : new Rectangle(box.Right - plain, box.Top, plain, box.Height);
            var onTrack = Found(sheet.Picture, End(onBox), t.Accent);
            var offTrack = Found(sheet.Picture, End(offBox), t.Off);
            // The off track's left or right end against the on track's, from the end of the switch.
            int onEnd = rtl ? onTrack.Left - onBox.Left : onBox.Right - onTrack.Right;
            int offEnd = rtl ? offTrack.Left - offBox.Left : offBox.Right - offTrack.Right;
            bool still = !onTrack.IsEmpty && !offTrack.IsEmpty && onBox.Size == offBox.Size && Math.Abs(onEnd - offEnd) <= 1
                && Math.Abs(onTrack.Width - offTrack.Width) <= 2;
            Is(still, $"switch {look}{Suffix(scale)}: the word does not move the track (on {onTrack}, off {offTrack} in boxes {onBox.Size} and {offBox.Size})");
            var words = Found(sheet.Picture, Rectangle.FromLTRB(rtl ? End(offBox).Right : offBox.Left, offBox.Top, rtl ? offBox.Right : End(offBox).Left, offBox.Bottom), t.Text, 30);
            bool before = !words.IsEmpty && (rtl ? words.Left >= offTrack.Right : words.Right <= offTrack.Left) && offBox.Contains(words);
            Is(before, $"switch {look}{Suffix(scale)}: the word sits before the track in reading order, inside the switch (word {words}, track {offTrack})");
            // The word is whole at every text size: its ink, before the track, is as big as the
            // word's drawn on its own with room around it, and clear of the switch's edges, so
            // nothing of it is cut at the sides or under the line («غير مفعّل» at 225%).
            foreach (var (box, on) in new[] { (onBox, true), (offBox, false) })
            {
                var word = on ? Lang.Current.V1.On : Lang.Current.V1.Off;
                var label = rtl ? Rectangle.FromLTRB(End(box).Right, box.Top, box.Right, box.Bottom) : Rectangle.FromLTRB(box.Left, box.Top, End(box).Left, box.Bottom);
                var ink = Found(sheet.Picture, label, t.Text, 30);
                var alone = Alone(word, Theme.Body(Toggle.LabelPoints, probe), rtl, t);
                bool clear = !ink.IsEmpty && ink.Left > box.Left && ink.Top > box.Top && ink.Right < box.Right && ink.Bottom < box.Bottom;
                bool whole = Math.Abs(ink.Width - alone.Width) <= 2 && Math.Abs(ink.Height - alone.Height) <= 2;
                Is(clear && whole, $"switch {look} {(on ? "on" : "off")}{Suffix(scale)}: \"{word}\" is whole, clear of the switch's edges (ink {ink.Size} at {ink.Location} in {box}, drawn alone {alone.Size})");
            }
        }
        var sizes = Looks.Select(look => sheet.Boxes[$"{look}-plain-off"].Size).ToList();
        Is(sizes[1] == new Size(48 * dpi / 96, 28 * dpi / 96) && sizes[2] == new Size(42 * dpi / 96, 26 * dpi / 96),
            $"switch{Suffix(scale)}: look C is 40 by 20 and compact 34 by 18, with room for the focus ring ({sizes[1]}, {sizes[2]})");
        if (pictures) Save(sheet.Picture, $"look-toggle-{tag}");
        return sheet;
    }

    static string Suffix(float scale) => scale == 1f ? "" : $" at {scale * 100:0}% text";

    // The box around a word's ink, drawn as a switch draws it but on its own, with a line of room
    // on every side.
    static Rectangle Alone(string word, Font font, bool rtl, Theme t)
    {
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | (rtl ? TextFormatFlags.RightToLeft : 0);
        var size = TextRenderer.MeasureText(word, font, Size.Empty, flags);
        int room = size.Height;
        using var picture = new Bitmap(size.Width + 2 * room, size.Height + 2 * room);
        using (var g = Graphics.FromImage(picture))
        {
            g.Clear(t.Card);
            TextRenderer.DrawText(g, word, font, new Rectangle(room, room, size.Width, size.Height), t.Text, t.Card, flags);
        }
        return Found(picture, new Rectangle(Point.Empty, picture.Size), t.Text, 30);
    }

    // The box around the pixels near a color.
    static Rectangle Found(Bitmap picture, Rectangle box, Color color, int tolerance = 20)
    {
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        for (int y = box.Top; y < box.Bottom; y++)
            for (int x = box.Left; x < box.Right; x++)
            {
                if (!Near(picture.GetPixel(x, y), color, tolerance)) continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + 1);
                bottom = Math.Max(bottom, y + 1);
            }
        return left == int.MaxValue ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right, bottom);
    }

    // ---- The palettes and their contrast ----

    static void Palettes(string tag)
    {
        bool dark = Theme.Current.IsDark;
        var rows = new List<(string Name, Theme Theme)>();
        // One palette: the accent is always the app's green.
        foreach (var color in new[] { "Green" })
        {
            var p = Theme.For(dark);
            rows.Add((color, p));
            Say($"     {color} {(dark ? "dark" : "light")}: " + string.Join(" ", typeof(Theme).GetProperties().Where(i => i.PropertyType == typeof(Color))
                .Select(i => (i.Name, Value: (Color)i.GetValue(p)!)).Select(c => $"{c.Name}={c.Value.R:X2}{c.Value.G:X2}{c.Value.B:X2}")));
            double onAccent = Theme.Contrast(p.OnAccent, p.Accent), window = Theme.Contrast(p.Accent, p.Window);
            Is(onAccent >= 4.5 && window >= 3, $"palette {color}: words on the accent {onAccent:0.00}:1 (4.5), the accent on the window {window:0.00}:1 (3)");
            double off = Math.Min(Theme.Contrast(p.Off, p.Card), Theme.Contrast(p.Off, p.Window)), knob = Theme.Contrast(p.KnobOff, p.Off), knobOn = Theme.Contrast(p.Knob, p.Accent);
            Is(off >= 3 && knob >= 3 && knobOn >= 3, $"palette {color}: switch off {off:0.00}:1 from the card and window, its knob {knob:0.00}:1, the knob on {knobOn:0.00}:1 (3)");
        }
        if (!pictures) return;

        string[] tokens = { "Accent", "OnAccent", "AccentSoft", "Hero", "HeroEdge", "HeroIn", "Soft", "Knob", "Off", "KnobOff", "Edge", "ControlEdge", "FieldLine", "Window", "Card" };
        int cell = 76, label = 110, top = 30;
        using var sheet = new Bitmap(label + tokens.Length * cell + 10, top + rows.Count * (cell + 8) + 10);
        var t = Theme.Current;
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(t.Window);
            var small = Theme.Body(7f);
            for (int i = 0; i < tokens.Length; i++)
                TextRenderer.DrawText(g, tokens[i], small, new Rectangle(label + i * cell, 8, cell - 4, 20), t.Subtle, TextFormatFlags.HorizontalCenter);
            for (int r = 0; r < rows.Count; r++)
            {
                var (name, p) = rows[r];
                int y = top + r * (cell + 8);
                TextRenderer.DrawText(g, name, Theme.Strong(9f), new Rectangle(8, y, label - 10, cell), t.Text, TextFormatFlags.VerticalCenter);
                for (int i = 0; i < tokens.Length; i++)
                {
                    var color = (Color)typeof(Theme).GetProperty(tokens[i])!.GetValue(p)!;
                    var box = new Rectangle(label + i * cell, y, cell - 6, cell - 26);
                    using (var brush = new SolidBrush(color)) g.FillRectangle(brush, box);
                    using (var pen = new Pen(t.Line)) g.DrawRectangle(pen, box);
                    TextRenderer.DrawText(g, $"{color.R:X2}{color.G:X2}{color.B:X2}", small, new Rectangle(box.X, box.Bottom + 2, box.Width, 18), t.Text, TextFormatFlags.HorizontalCenter);
                    if (tokens[i] == "Accent")
                        TextRenderer.DrawText(g, $"Aa {Theme.Contrast(p.OnAccent, p.Accent):0.0}", Theme.Strong(9f), box, p.OnAccent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    if (tokens[i] == "Off")
                    {
                        using var knob = new SolidBrush(p.KnobOff);
                        g.FillEllipse(knob, box.X + 6, box.Y + 6, 12, 12);
                    }
                }
            }
        }
        Save(sheet, $"look-palette-{tag}");
    }

    // ---- Beside the sketch ----

    // Where the sketch's renders have the ring (the status box, 84 px), the switches with their word
    // (Start with Windows on, Break reminders off) and the tray menu's ring (36 px, light and dark
    // English only).
    static void Compare(string tag, bool rtl, Sheet ring, Sheet toggles)
    {
        string? folder = Environment.GetEnvironmentVariable("LOOK_REF");
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        string theme = Theme.Current.IsDark ? "dark" : "light";
        string lang = rtl ? "ar" : "en";
        string main = Path.Combine(folder, $"settings-main-none-{lang}-{theme}.png");
        string breaks = Path.Combine(folder, $"settings-tab3-none-{lang}-{theme}.png");
        string tray = Path.Combine(folder, $"tray-menu-{theme}.png");
        if (!File.Exists(main) || !File.Exists(breaks)) return;

        var pairs = new List<(string Name, Bitmap Sketch, Bitmap App)>();
        using (var picture = new Bitmap(main))
            pairs.Add(("ring 84, 2 of 30 min", Crop(picture, rtl ? new Rectangle(632, 16, 89, 89) : new Rectangle(18, 16, 89, 89)), Crop(ring.Picture, Around(ring.Boxes[$"84-{2f / 30}"], 2))));
        using (var picture = new Bitmap(breaks))
        {
            pairs.Add(("switch on, with its word", Crop(picture, rtl ? new Rectangle(32, 634, 128, 36) : new Rectangle(640, 634, 110, 36)), Crop(toggles.Picture, Around(toggles.Boxes["LookC-label-on"], 4))));
            pairs.Add(("switch off, with its word", Crop(picture, rtl ? new Rectangle(32, 866, 128, 36) : new Rectangle(640, 866, 110, 36)), Crop(toggles.Picture, Around(toggles.Boxes["LookC-label-off"], 4))));
        }
        if (!rtl && File.Exists(tray))
        {
            using var picture = new Bitmap(tray);
            pairs.Add(("tray ring 36", Crop(picture, new Rectangle(216, 21, 41, 41)), Crop(ring.Picture, Around(ring.Boxes[$"36-{2f / 30}"], 2))));
        }

        const int zoom = 4;
        int gap = 24, caption = 22;
        int sketchWidth = pairs.Max(p => p.Sketch.Width) * zoom;
        int width = sketchWidth + pairs.Max(p => p.App.Width) * zoom + 3 * gap;
        int height = pairs.Sum(p => Math.Max(p.Sketch.Height, p.App.Height) * zoom + caption + gap) + gap;
        using var sheet = new Bitmap(width, height);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(0xDD, 0xE3, 0xEA));
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            int y = gap;
            foreach (var (name, sketch, app) in pairs)
            {
                TextRenderer.DrawText(g, $"{name}: sketch (left), app (right)", Theme.Body(9f), new Point(gap, y), Color.FromArgb(0x4A, 0x55, 0x63));
                y += caption;
                g.DrawImage(sketch, new Rectangle(gap, y, sketch.Width * zoom, sketch.Height * zoom));
                int x = gap * 2 + sketchWidth;
                g.DrawImage(app, new Rectangle(x, y, app.Width * zoom, app.Height * zoom));
                y += Math.Max(sketch.Height, app.Height) * zoom + gap;
                sketch.Dispose();
                app.Dispose();
            }
        }
        Directory.CreateDirectory(Path.Combine(outDir, "compare"));
        sheet.Save(Path.Combine(outDir, "compare", $"look-vs-sketch-{tag}.png"), ImageFormat.Png);
        Console.WriteLine($"compare/look-vs-sketch-{tag}: {sheet.Width}x{sheet.Height}");
    }

    static Rectangle Around(Rectangle box, int by) => Rectangle.Inflate(box, by, by);

    static Bitmap Crop(Bitmap from, Rectangle box)
    {
        box.Intersect(new Rectangle(0, 0, from.Width, from.Height));
        return from.Clone(box, PixelFormat.Format32bppArgb);
    }

    // ---- Windows and pictures ----

    // A plain window off the screen, without a frame, laid out as asked.
    static Form Window(RightToLeft direction, bool layout, Color back)
    {
        var f = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            RightToLeft = direction,
            RightToLeftLayout = layout,
            BackColor = back,
            AutoScaleMode = AutoScaleMode.None,
            Font = Theme.Body(10f),
        };
        Shots.Place(f);
        // A two-pixel mark at the window's own start, to find which way round the picture comes.
        f.Controls.Add(new Panel { Name = "mark", Bounds = new Rectangle(0, 0, 2, 2), BackColor = Mark });
        return f;
    }

    static readonly Color Mark = Color.FromArgb(0xFF, 0x00, 0xFF);

    static int P(Control c, int logical) => logical * c.DeviceDpi / 96;

    // The window drawn into a picture, turned back the right way if it is laid out right to left,
    // with where each control landed in it. Closes the window.
    static Sheet Shoot(Form f, Dictionary<string, Control> controls)
    {
        Pump(300);
        f.Refresh();
        Shots.Foreground(f, "look sheet");
        var handle = f.Handle;
        GetWindowRect(handle, out var w);
        var whole = new Bitmap(w.R - w.L, w.B - w.T);
        using (var g = Graphics.FromImage(whole))
        {
            for (int i = 0; i < 2; i++)
            {
                IntPtr dc = g.GetHdc();
                PrintWindow(handle, dc, RenderFullContent);
                g.ReleaseHdc(dc);
                if (i == 0) Pump(200);
            }
        }
        // A window laid out right to left shows its start at the right. PrintWindow hands some windows
        // over mirrored and others as shown, so the mark decides; then it is painted over.
        bool mirrored = (GetWindowLong(handle, ExStyle) & LayoutRtlStyle) != 0;
        bool markLeft = Near(whole.GetPixel(0, 0), Mark, 2);
        bool markRight = Near(whole.GetPixel(whole.Width - 1, 0), Mark, 2);
        Is(markLeft != markRight, $"look sheet: the window's start mark is found in its picture (left {markLeft}, right {markRight})");
        if (mirrored == markLeft) whole.RotateFlip(RotateFlipType.RotateNoneFlipX);
        using (var g = Graphics.FromImage(whole))
        using (var back = new SolidBrush(f.BackColor))
            g.FillRectangle(back, mirrored ? whole.Width - 2 : 0, 0, 2, 2);
        var boxes = new Dictionary<string, Rectangle>();
        int width = f.ClientSize.Width;
        foreach (var (key, c) in controls)
            boxes[key] = mirrored ? new Rectangle(width - c.Right, c.Top, c.Width, c.Height) : c.Bounds;
        f.Close();
        f.Dispose();
        return new Sheet(whole, boxes);
    }

    static void Save(Bitmap picture, string name)
    {
        picture.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
        Console.WriteLine($"{name}: {picture.Width}x{picture.Height}");
    }

    static void Pump(int ms)
    {
        var until = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < until)
        {
            Application.DoEvents();
            Thread.Sleep(15);
        }
    }

    static bool Near(Color a, Color b, int tolerance) =>
        Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;

    // Pixels that differ by more than a step in any channel between two boxes of the same size,
    // where the box's own pixel (x, y) is kept.
    static int Differences(Bitmap a, Bitmap b, Rectangle inA, Rectangle inB, Func<int, int, bool>? keep = null)
    {
        if (inA.Size != inB.Size) return int.MaxValue;
        int count = 0;
        for (int y = 0; y < inA.Height; y++)
            for (int x = 0; x < inA.Width; x++)
                if ((keep is null || keep(x, y)) && !Near(a.GetPixel(inA.X + x, inA.Y + y), b.GetPixel(inB.X + x, inB.Y + y), 1)) count++;
        return count;
    }

    // Pixels of a ring's logo box that lie inside the track, where only the logo is drawn.
    static Func<int, int, bool> InsideTrack(Rectangle ring)
    {
        var logo = LogoBox(ring);
        float side = Math.Min(ring.Width, ring.Height);
        float inner = side * (0.45f - 0.035f) - 1.5f;
        float cx = ring.X + ring.Width / 2f - logo.X, cy = ring.Y + ring.Height / 2f - logo.Y;
        return (x, y) => (x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy) < inner * inner;
    }
}
