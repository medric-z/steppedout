using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using SteppedOut;

// Sheets of the tray, for comparing against the design sketch: the menu (on with the pause list
// open, paused, and with a very long headline) at 100%, 150% and Large text, the status box in each
// state the sketch does not draw, and the icons (each state at 16, 20 and 24 px on a light and a
// dark taskbar, zoomed, and with minutes). The menu is built as the tray builds it and drawn without
// being shown; the pause list is drawn at the size it has and where it opens beside the menu, as
// Windows shows it, not laid out again. No tray icon is made. Pictures are named
// tray-<what>-<language>-<theme>.png; the icon sheets, the same in every language and theme,
// tray-icons-taskbar-<light|dark>.png. The checks run with every sheet, and in check mode.
static class TraySheet
{
    static string outDir = "";
    static bool iconsDrawn;
    static int failures;
    static TextWriter? report;
    static bool firstRun = true;

    public static void Run(string outDir, string lang, string theme)
    {
        TraySheet.outDir = outDir;
        string file = Path.Combine(outDir, "tray-check.txt");
        if (firstRun)
        {
            File.Delete(file);
            firstRun = false;
        }
        using (report = File.AppendText(file))
        {
            Menus($"{lang}-{theme}", theme == "dark", pictures: true);
            if (!iconsDrawn)
            {
                Icons(darkTaskbar: false);
                Icons(darkTaskbar: true);
                iconsDrawn = true;
            }
        }
        report = null;
        Console.WriteLine(failures == 0 ? "tray: all checks passed" : $"tray: {failures} checks failed");
    }

    // The checks without pictures: both languages, light, at 100%, 150%, 200% and Large text.
    // Gives how many failed.
    public static int Check()
    {
        failures = 0;
        foreach (var lang in new[] { "en", "ar" })
        {
            Lang.Use(lang);
            Theme.UseForShots(false);
            Menus($"{lang}-light", dark: false, pictures: false);
        }
        Console.WriteLine(failures == 0 ? "tray: all checks passed" : $"tray: {failures} checks failed");
        return failures;
    }

    static void Is(bool ok, string what)
    {
        string line = (ok ? "ok   " : "FAIL ") + what;
        Console.WriteLine(line);
        report?.WriteLine(line);
        if (!ok) failures++;
    }

    // ---- States ----

    // The sketch's: 28 minutes left of 30, away 1 hr 20 min today.
    static LockStatus On() => new()
    {
        Kind = LockState.On,
        Time = LockTime.Plain(30),
        IdleSeconds = 120,
        SecondsLeft = 28 * 60,
        Today = new TodayCounts(2, TimeSpan.FromMinutes(80)),
    };

    static LockStatus Paused() => new()
    {
        Kind = LockState.Paused,
        Time = LockTime.Plain(30),
        PausedUntil = DateTime.Today.AddHours(17).AddMinutes(12),
        Today = new TodayCounts(2, TimeSpan.FromMinutes(80)),
    };

    // A headline as long as it gets: never locking while an app with a 200-character name is open.
    static LockStatus LongName() => new()
    {
        Kind = LockState.Never,
        Time = LockTime.Compute(30, [new ActiveEffect(Effect.Never, EffectSource.ForApp(new string('W', 200)))]),
        Today = new TodayCounts(0, TimeSpan.FromHours(10)),
    };

    const string Keys = "Ctrl+Alt+L";

    // ---- The menu ----

    enum Scale { Normal, Dpi150, Dpi200, LargeText }

    static string Suffix(Scale s) => s switch { Scale.Dpi150 => "-150", Scale.Dpi200 => "-200", Scale.LargeText => "-large", _ => "" };

    static void Menus(string tag, bool dark, bool pictures)
    {
        foreach (var scale in new[] { Scale.Normal, Scale.Dpi150, Scale.Dpi200, Scale.LargeText })
        {
            if (scale == Scale.LargeText) Theme.UseForShots(dark, 1.2f);
            try
            {
                using var tray = new TrayMenu(Lang.Current);
                if (scale == Scale.Dpi150) tray.Dpi = 144;
                if (scale == Scale.Dpi200) tray.Dpi = 192;
                string name = $"tray-menu-{tag}{Suffix(scale)}";

                tray.Apply(new TrayMenuState(On(), Paused: false, LockingOn: true, Keys));
                CheckMenu(tray, $"{name} on");
                CheckPauseList(tray, $"{name} pause list");
                if (pictures && scale != Scale.Dpi200)
                {
                    tray.PauseRow.Select();
                    Save(WithList(tray), $"tray-menu-on-{tag}{Suffix(scale)}");
                }

                tray.Apply(new TrayMenuState(Countdown(secondsLeft: 9), Paused: false, LockingOn: true, Keys));
                CheckMenu(tray, $"{name} countdown, 9 seconds");

                tray.Apply(new TrayMenuState(Paused(), Paused: true, LockingOn: true, Keys));
                CheckMenu(tray, $"{name} paused");
                if (pictures && scale == Scale.Normal)
                {
                    using var menu = DrawMenu(tray.Menu, tray.Dpi);
                    Save(Beside([menu], tray.Dpi, false), $"tray-menu-paused-{tag}");
                }

                // A break note with Snooze is showing: Snooze this reminder is the first row.
                tray.Apply(new TrayMenuState(On(), Paused: false, LockingOn: true, Keys, Snooze: true));
                CheckMenu(tray, $"{name} snooze");
                Is(tray.Menu.GetNextItem(null, ArrowDirection.Down) == tray.SnoozeRow, $"{name} snooze: the keys start on {tray.SnoozeRow.Text}");
                if (pictures && scale == Scale.Normal)
                {
                    using var menu = DrawMenu(tray.Menu, tray.Dpi);
                    Save(Beside([menu], tray.Dpi, false), $"tray-menu-snooze-{tag}");
                }

                tray.Apply(new TrayMenuState(LongName(), Paused: false, LockingOn: true, Shortcut: null));
                CheckMenu(tray, $"{name} long app name, no shortcut");
                if (pictures && scale == Scale.Normal)
                {
                    using var menu = DrawMenu(tray.Menu, tray.Dpi);
                    Save(Beside([menu], tray.Dpi, false), $"tray-menu-longname-{tag}");
                }

                if (scale == Scale.Normal)
                {
                    States(tag, pictures);
                    AsTheAppFillsIt(tag);
                }
            }
            finally
            {
                if (scale == Scale.LargeText) Theme.UseForShots(dark);
            }
        }
    }

    static void CheckMenu(TrayMenu tray, string name)
    {
        var menu = tray.Menu;
        Lay(menu);
        bool rtl = Lang.Current.RightToLeft;
        var px = new TrayMetrics(tray.Dpi);
        var rows = new[] { tray.SnoozeRow, tray.PauseRow, tray.TurnBackOnRow, tray.LockNowRow, tray.SettingsRow, tray.QuitRow }.Where(r => r.Available).ToList();

        Is(menu.Width >= px.MenuWidth, $"{name}: the menu is at least 252 px at its scaling ({menu.Width} >= {px.MenuWidth})");
        Is(menu.Items.Cast<ToolStripItem>().Where(i => i.Available).All(i => i.Bounds.Left >= 0 && i.Bounds.Right <= menu.Width && i.Bounds.Bottom <= menu.Height),
            $"{name}: every row is inside the menu");
        Is(rows.All(r => r.Height >= px.Px(32) && r.Height >= px.Text.Height + px.Px(10)), $"{name}: rows are at least 32 px and taller than their words ({string.Join(",", rows.Select(r => r.Height))})");
        Is(rows.All(r => r.Width == menu.Width - 1) && tray.StatusItem.Width == menu.Width - 1, $"{name}: rows and the status box span the menu");
        Is(menu.Items.Cast<ToolStripItem>().Where(i => i.Available && i is not ToolStripSeparator).All(i => !string.IsNullOrWhiteSpace(i.AccessibilityObject.Name)),
            $"{name}: every item has an accessible name");
        Is(tray.StatusItem.AccessibilityObject.Role == AccessibleRole.StaticText && !tray.StatusItem.CanSelect, $"{name}: the status box is read as text and the keys pass over it");
        Is(menu.GetNextItem(null, ArrowDirection.Down) == rows[0], $"{name}: the keys start on {rows[0].Text}");
        Is(!menu.Items.Cast<ToolStripItem>().Any(i => i.Text == Lang.Current.V1.Language), $"{name}: no Language row");

        foreach (var row in rows)
        {
            var p = row.Parts;
            bool inside = p.Fill.Contains(p.Glyph) && p.Fill.Contains(p.Text) && (p.Trail.Width == 0 || p.Fill.Contains(p.Trail));
            bool order = rtl
                ? p.Glyph.Left >= p.Text.Right && (p.Trail.Width == 0 || p.Trail.Right <= p.Text.Left)
                : p.Glyph.Right <= p.Text.Left && (p.Trail.Width == 0 || p.Trail.Left >= p.Text.Right);
            int needed = TrayRow.Measure(row.Text ?? "", px.Text);
            Is(inside && order && needed <= p.Text.Width,
                $"{name}: {row.Text}: glyph, words and {(p.Trail.Width == 0 ? "nothing else" : "arrow or shortcut")} in order {(rtl ? "right to left" : "left to right")}, words fit ({needed} <= {p.Text.Width})");
        }
        if (tray.LockNowRow.ShortcutKeyDisplayString is { Length: > 0 } keys)
        {
            Is(keys.StartsWith('\u200E') && !TrayMenuLook.ShortcutFlags.HasFlag(TextFormatFlags.RightToLeft), $"{name}: the shortcut is drawn left to right");
            Is((tray.LockNowRow.AccessibilityObject.Name ?? "").Contains(Keys), $"{name}: Lock now's accessible name has the shortcut ({tray.LockNowRow.AccessibilityObject.Name})");
        }

        // The status box: the ring and the words inside it, and nothing drawn past the words.
        var status = tray.StatusItem;
        var parts = status.Parts;
        var (headline, label, value) = status.Lines();
        Is(parts.Box.Contains(parts.Ring) && parts.Box.Contains(parts.Text) && parts.Text.Contains(headline) && parts.Text.Contains(label) && parts.Text.Contains(value)
            && parts.Box.Bottom <= status.Height, $"{name}: the status box holds the ring and both lines");
        Is(rtl ? parts.Ring.Left > parts.Text.Left : parts.Ring.Right < parts.Text.Left, $"{name}: the ring is on the {(rtl ? "right" : "left")} of the words");
        Is(headline.Height >= px.Headline.Height && value.Height >= px.Small.Height, $"{name}: the lines are as tall as their words");
        int ink = InkBottom(status.Headline, px.Headline, rtl);
        Is(headline.Height >= ink && label.Top >= headline.Bottom,
            $"{name}: the headline's lowest mark, {ink} px down, is inside its {headline.Height} px line, above the time away");
        Is(TrayRow.Measure(status.AwayLabel, px.Small) + TrayRow.Measure(status.AwayValue, px.SmallStrong) <= parts.Text.Width,
            $"{name}: the time away fits ({status.AwayLabel} {status.AwayValue})");
        using var picture = new Bitmap(menu.Width, menu.Height);
        menu.DrawToBitmap(picture, new Rectangle(0, 0, menu.Width, menu.Height));
        Is(OnlyFill(picture, status, parts), $"{name}: nothing in the status box past the ring and the words (a long headline is cut, not run over)");
    }

    // How far below the top of its line a text's lowest drawn mark reaches, such as the two marks
    // Arabic sets under the last letter of "seconds", which Windows leaves out of the line's height.
    static int InkBottom(string words, Font font, bool rtl)
    {
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.Top | TextFormatFlags.NoClipping;
        var size = TextRenderer.MeasureText(words, font, Size.Empty, flags);
        using var picture = new Bitmap(size.Width + 20, size.Height * 2);
        using (var g = Graphics.FromImage(picture))
        {
            g.Clear(Color.White);
            TextRenderer.DrawText(g, words, font, new Rectangle(0, 0, picture.Width, picture.Height), Color.Black,
                flags | (rtl ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left));
        }
        for (int y = picture.Height - 1; y >= 0; y--)
        {
            for (int x = 0; x < picture.Width; x++)
            {
                if (picture.GetPixel(x, y).R < 192) return y + 1;
            }
        }
        return 0;
    }

    // The menu, the tooltip and the minutes as the tray app makes them from the status and the
    // settings (TrayView), through the menu's own state source as when it opens: the shortcut in the
    // settings beside Lock now, none when it is off, minutes only while counting and only when
    // chosen, and a tooltip Windows takes (127 characters at most, right to left in Arabic).
    static void AsTheAppFillsIt(string tag)
    {
        var text = Lang.Current;
        var config = new Config { MinutesOnTrayIcon = true };
        foreach (var (what, status, paused) in new[] { ("on", On(), false), ("paused", Paused(), true), ("long app name", LongName(), false) })
            foreach (var shortcut in new[] { Keys, "" })
            {
                string name = $"tray-app-{tag} {what}, shortcut {(shortcut.Length == 0 ? "off" : shortcut)}";
                config.LockShortcut = shortcut;
                var dry = status with { DryRun = true };
                var view = TrayView.From(dry, paused, config, shortcut.Length == 0 ? null : shortcut, text);
                string headline = text.Headline(dry, config.LockWhenAway);
                using var tray = new TrayMenu(text) { StateSource = () => view.Menu };
                tray.RaiseOpenedForTests();
                Is(tray.State == view.Menu, $"{name}: the menu shows the app's state when it opens");
                Is((tray.LockNowRow.ShortcutKeyDisplayString ?? "").Length > 0 == (shortcut.Length > 0), $"{name}: the shortcut beside Lock now only while it is on");
                Is(view.Tooltip.Length <= 127, $"{name}: the tooltip fits ({view.Tooltip.Length} <= 127)");
                Is(!text.RightToLeft || (view.Tooltip[0] == '‫' && view.Tooltip[^1] == '‬'), $"{name}: the tooltip reads in the language's direction");
                Is(view.Tooltip.TrimStart('‫').StartsWith(AppInfo.Name + " · " + headline[..Math.Min(20, headline.Length)], StringComparison.Ordinal), $"{name}: the tooltip is the app's name and the headline");
                Is((view.Minutes is not null) == (status.Kind == LockState.On), $"{name}: minutes on the icon only while counting ({view.Minutes ?? "none"})");
                Is(view.Icon == TrayIcons.StateOf(status, paused), $"{name}: the icon's state is the status's");
                Lay(tray.Menu);
                Is(!tray.Menu.Items.Cast<ToolStripItem>().Any(i => i.Text == text.V1.Language), $"{name}: no Language row");
            }
    }

    // The status box, away from its edge, its ring and its words, is all one color.
    static bool OnlyFill(Bitmap picture, TrayStatusItem status, StatusParts parts)
    {
        var at = status.Bounds.Location;
        var box = parts.Box;
        box.Offset(at);
        var ring = parts.Ring;
        ring.Offset(at);
        ring.Inflate(1, 1);
        var text = parts.Text;
        text.Offset(at);
        var inner = Rectangle.Inflate(box, -(status.Metrics.StatusCorner + 1), -2);
        int fill = picture.GetPixel(inner.Left, inner.Top).ToArgb();
        for (int y = inner.Top; y < inner.Bottom; y++)
            for (int x = inner.Left; x < inner.Right; x++)
                if (!ring.Contains(x, y) && !text.Contains(x, y) && picture.GetPixel(x, y).ToArgb() != fill) return false;
        return true;
    }

    // The pause list as Windows shows it: at the size it has, not laid out again first, which is the
    // size it would like and at least the sketch's 188 px; beside the menu with the sketch's gap on
    // either side, never over it.
    static void CheckPauseList(TrayMenu tray, string name)
    {
        var list = tray.PauseList;
        var px = new TrayMetrics(tray.Dpi);
        int preferred = list.GetPreferredSize(Size.Empty).Width;
        Is(list.Width == preferred && list.Width >= px.PauseWidth,
            $"{name}: shown at the width it would like, at least the sketch's ({list.Width}, would like {preferred}, sketch {px.PauseWidth})");
        Is(tray.PauseChoices.Where(i => i.Available).All(i => i.Bounds.Left >= 0 && i.Bounds.Right <= list.Width && i.Bounds.Bottom <= list.Height),
            $"{name}: every choice is inside the list");
        var menu = new Rectangle(1000, 500, tray.Menu.Width, tray.Menu.Height);
        int first = tray.PauseChoices.First(i => i.Available).Bounds.Top;
        foreach (bool toLeft in new[] { true, false })
        {
            var at = TrayMenu.PauseListBounds(menu, menu.Top + tray.PauseRow.Bounds.Top, list.Size, first, toLeft, new Rectangle(0, 0, 4000, 3000), px.PauseGap);
            int gap = toLeft ? menu.Left - at.Right : at.Left - menu.Right;
            Is(!at.IntersectsWith(menu) && gap == px.PauseGap, $"{name}: opened to the {(toLeft ? "left" : "right")}, {gap} px from the menu (sketch {px.PauseGap}) and not over it");
        }
    }

    // The menu with its pause list open where it opens: to the left of a tray on the right, and in
    // Arabic to the right of a tray on the left, where a right-to-left Windows puts the tray.
    // The tray menu and its pause list, each drawn as Windows shows it, and where the list stands
    // against the menu's top left, as the Store screenshot shows them.
    internal static (Bitmap Menu, Bitmap List, Point ListAt) StoreMenu()
    {
        using var tray = new TrayMenu(Lang.Current);
        tray.Apply(new TrayMenuState(On(), Paused: false, LockingOn: true, Keys));
        Lay(tray.Menu);
        tray.PauseRow.Select();
        int dpi = tray.Dpi;
        var px = new TrayMetrics(dpi);
        var menu = DrawMenu(tray.Menu, dpi);
        var list = DrawMenu(tray.PauseList, dpi, lay: false);
        int first = tray.PauseChoices.First(i => i.Available).Bounds.Top;
        var listAt = TrayMenu.PauseListBounds(new Rectangle(Point.Empty, menu.Size), tray.PauseRow.Bounds.Top, list.Size, first,
            toLeft: !Lang.Current.RightToLeft, new Rectangle(-4000, -4000, 8000, 8000), px.PauseGap);
        return (menu, list, listAt.Location);
    }

    static Bitmap WithList(TrayMenu tray)
    {
        int dpi = tray.Dpi;
        var px = new TrayMetrics(dpi);
        using var menu = DrawMenu(tray.Menu, dpi);
        using var list = DrawMenu(tray.PauseList, dpi, lay: false);
        var menuAt = new Rectangle(Point.Empty, menu.Size);
        int first = tray.PauseChoices.First(i => i.Available).Bounds.Top;
        var listAt = TrayMenu.PauseListBounds(menuAt, tray.PauseRow.Bounds.Top, list.Size, first, toLeft: !Lang.Current.RightToLeft,
            new Rectangle(-4000, -4000, 8000, 8000), px.PauseGap);
        var all = Rectangle.Union(menuAt, listAt);
        int pad = 16 * dpi / 96;
        var sheet = new Bitmap(all.Width + 2 * pad, all.Height + 2 * pad);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Theme.Current.Window);
        g.DrawImageUnscaled(menu, menuAt.X - all.X + pad, menuAt.Y - all.Y + pad);
        g.DrawImageUnscaled(list, listAt.X - all.X + pad, listAt.Y - all.Y + pad);
        return sheet;
    }

    // ---- The status box in the states the sketch does not draw ----

    static TodayCounts Today => new(2, TimeSpan.FromMinutes(80));

    static LockStatus Countdown(bool dryRun = false, int secondsLeft = 20) => new()
    {
        Kind = LockState.Countdown,
        Time = LockTime.Plain(30),
        IdleSeconds = 30 * 60 - secondsLeft,
        SecondsLeft = secondsLeft,
        DryRun = dryRun,
        Today = Today,
    };

    static LockStatus Plain(LockState kind) => new()
    {
        Kind = kind,
        Time = LockTime.Plain(30),
        IdleSeconds = kind is LockState.LockingOff ? 120 : 0,
        SecondsLeft = kind is LockState.LockingOff ? 28 * 60 : 0,
        Today = Today,
    };

    static LockStatus NeverOnACall() => new()
    {
        Kind = LockState.Never,
        Time = LockTime.Compute(30, [new ActiveEffect(Effect.Never, EffectSource.Call)]),
        Today = Today,
    };

    // Each state's box, cut from its menu, in a column with the state's name beside it, and checked
    // for its tone (D20): green while it counts or something keeps it from locking, amber while the
    // countdown runs, gray while paused, away or locked, a dry run as what it shows.
    static void States(string tag, bool pictures)
    {
        var t = Theme.Current;
        bool hc = SystemInformation.HighContrast;
        var samples = new (string Name, LockStatus Status, bool Paused, bool LockingOn, Tone Tone, Color Fill)[]
        {
            ("on", On(), false, true, Tone.On, t.Hero),
            ("starting", LockStatus.Starting(30) with { Today = Today }, false, true, Tone.On, t.Hero),
            ("countdown", Countdown(), false, true, Tone.Busy, t.WarningSoft),
            ("countdown, 9 seconds", Countdown(secondsLeft: 9), false, true, Tone.Busy, t.WarningSoft),
            ("dry run, countdown", Countdown(dryRun: true), false, true, Tone.Busy, t.WarningSoft),
            ("never (on a call)", NeverOnACall(), false, true, Tone.On, t.Hero),
            ("locking off", Plain(LockState.LockingOff), false, false, Tone.On, t.Hero),
            ("paused until 5:12 PM", Paused(), true, true, Tone.Off, t.Window),
            ("paused", Plain(LockState.Paused), true, true, Tone.Off, t.Window),
            ("away", Plain(LockState.Away), false, true, Tone.Off, t.Window),
            ("locked", Plain(LockState.SessionLocked), false, true, Tone.Off, t.Window),
        };
        var crops = new List<(string Name, Bitmap Picture)>();
        try
        {
            foreach (var (label, status, paused, lockingOn, tone, fill) in samples)
            {
                using var tray = new TrayMenu(Lang.Current);
                tray.Apply(new TrayMenuState(status, paused, lockingOn, Keys));
                string name = $"tray-states-{tag} {label}";
                CheckMenu(tray, name);
                var item = tray.StatusItem;
                Is(item.Tone == tone, $"{name}: the box is {tone} ({item.Headline})");
                using var picture = DrawMenu(tray.Menu, tray.Dpi);
                var box = item.Parts.Box;
                box.Offset(item.Bounds.Location);
                var inside = picture.GetPixel(box.Left + box.Width / 2, box.Top + 3);
                var expected = hc ? TrayStatusItem.Colors(tone).Fill : fill;
                Is(inside.ToArgb() == expected.ToArgb(), $"{name}: filled {expected.Name} ({inside.Name})");
                if (pictures)
                {
                    var cut = new Rectangle(0, item.Bounds.Top, picture.Width, item.Bounds.Height);
                    crops.Add((label, picture.Clone(cut, picture.PixelFormat)));
                }
            }
            if (!pictures) return;
            using var font = new Font("Segoe UI", 9f);
            const int labels = 150, pad = 12;
            int width = labels + crops.Max(c => c.Picture.Width) + 2 * pad;
            var sheet = new Bitmap(width, crops.Sum(c => c.Picture.Height + 4) + 2 * pad);
            using (var g = Graphics.FromImage(sheet))
            {
                g.Clear(Theme.Current.Window);
                int y = pad;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using var ink = new SolidBrush(Theme.Current.Subtle);
                using var middle = new StringFormat { LineAlignment = StringAlignment.Center };
                foreach (var (label, crop) in crops)
                {
                    g.DrawString(label, font, ink, new RectangleF(pad, y, labels - 8, crop.Height), middle);
                    g.DrawImageUnscaled(crop, pad + labels, y);
                    y += crop.Height + 4;
                }
            }
            Save(sheet, $"tray-states-{tag}");
        }
        finally
        {
            foreach (var (_, crop) in crops) crop.Dispose();
        }
    }

    // Laid out at its size as when shown, without showing it.
    static void Lay(ToolStripDropDown menu)
    {
        menu.PerformLayout();
        var preferred = menu.GetPreferredSize(Size.Empty);
        menu.Size = new Size(Math.Max(preferred.Width, menu.MinimumSize.Width), preferred.Height);
        menu.PerformLayout();
    }

    // Draws a menu that is never shown, rounded and edged as Windows 11 shows it; laid out first, or
    // at the size it has (the pause list, which Windows shows at its size).
    static Bitmap DrawMenu(ToolStripDropDown menu, int dpi, bool lay = true)
    {
        if (lay) Lay(menu);
        using var flat = new Bitmap(menu.Width, menu.Height);
        menu.DrawToBitmap(flat, new Rectangle(0, 0, menu.Width, menu.Height));
        var shaped = new Bitmap(menu.Width, menu.Height);
        using var g = Graphics.FromImage(shaped);
        g.Clear(Theme.Current.Window);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Shapes.Rounded(new RectangleF(0.5f, 0.5f, menu.Width - 1, menu.Height - 1), 8f * dpi / 96);
        using (var brush = new TextureBrush(flat)) g.FillPath(brush, path);
        using var pen = new Pen(Theme.Current.ControlEdge);
        g.DrawPath(pen, path);
        return shaped;
    }

    // Pictures side by side, top-aligned, on the window color; in Arabic the order is reversed, as a
    // tray on the left opens the pause list to the menu's right.
    static Bitmap Beside(List<Bitmap> parts, int dpi, bool rtl)
    {
        int gap = 16 * dpi / 96;
        var sheet = new Bitmap(parts.Sum(p => p.Width) + gap * (parts.Count + 1), parts.Max(p => p.Height) + gap * 2);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Theme.Current.Window);
        int x = gap;
        foreach (var part in rtl ? Enumerable.Reverse(parts) : parts)
        {
            g.DrawImage(part, x, gap, part.Width, part.Height);
            x += part.Width + gap;
        }
        return sheet;
    }

    static void Save(Bitmap bitmap, string name)
    {
        using (bitmap) bitmap.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
        Console.WriteLine($"{name}: saved");
    }

    // ---- The icons ----

    // Per state and size: the icon as the tray shows it, the same 8 times as big, and with 5, 28 and
    // 99 minutes (as shown, and 3 times as big). Then the sketch's own cards: each state at 16 px and
    // its 16 px art 3 times as big, as the reference crops show them.
    static void Icons(bool darkTaskbar)
    {
        var states = new[] { TrayIconState.On, TrayIconState.Paused, TrayIconState.CountingDown };
        int[] sizes = [16, 20, 24];
        string[] minutes = ["5", "28", "99"];
        const int zoom = 8, gap = 12;
        var back = darkTaskbar ? Color.FromArgb(0x20, 0x20, 0x20) : Color.FromArgb(0xF3, 0xF3, 0xF3);
        var ink = darkTaskbar ? Color.White : Color.FromArgb(0x1A, 0x1A, 0x1A);
        int cell = 24 * zoom + gap;
        int zoomTop = 30 + 24 + gap;
        int minutesTop = zoomTop + 24 * zoom + gap;
        int height = minutesTop + 24 + minutes.Length * (24 * 3 + 4) + gap;
        using var sheet = new Bitmap(gap + states.Length * sizes.Length * cell, height);
        using var font = new Font("Segoe UI", 9f);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(back);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            int x = gap;
            foreach (var state in states)
            {
                foreach (int size in sizes)
                {
                    using var icon = TrayIcons.Art(state, size, darkTaskbar);
                    TextRenderer.DrawText(g, $"{state} {size}", font, new Point(x, 8), ink);
                    g.DrawImageUnscaled(icon, x, 30);
                    g.DrawImage(icon, new Rectangle(x, zoomTop, size * zoom, size * zoom));
                    TextRenderer.DrawText(g, string.Join(" · ", minutes), font, new Point(x, minutesTop), ink);
                    int y = minutesTop + 24;
                    foreach (var m in minutes)
                    {
                        using var numbered = TrayIcons.Art(state, size, darkTaskbar, m);
                        g.DrawImageUnscaled(numbered, x, y);
                        g.DrawImage(numbered, new Rectangle(x + size + gap, y, size * 3, size * 3));
                        y += 24 * 3 + 4;
                    }
                    x += cell;
                }
            }
        }
        string taskbar = darkTaskbar ? "dark" : "light";
        sheet.Save(Path.Combine(outDir, $"tray-icons-taskbar-{taskbar}.png"), ImageFormat.Png);
        Console.WriteLine($"tray-icons-taskbar-{taskbar}: saved");

        // The sizes for 175% to 300%: as the tray shows them and 4 times as big, plain and with 28.
        int[] large = [28, 32, 36, 40, 48];
        const int big = 4;
        int bigCell = 48 + gap + 48 * big + gap;
        using (var more = new Bitmap(gap + states.Length * large.Length * bigCell, 30 + 48 + gap + 2 * (48 * big + gap)))
        {
            using (var g = Graphics.FromImage(more))
            {
                g.Clear(back);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                int x = gap;
                foreach (var state in states)
                {
                    foreach (int size in large)
                    {
                        TextRenderer.DrawText(g, $"{state} {size}", font, new Point(x, 8), ink);
                        int y = 30;
                        foreach (var m in new string?[] { null, "28" })
                        {
                            using var icon = TrayIcons.Art(state, size, darkTaskbar, m);
                            g.DrawImageUnscaled(icon, x, y);
                            g.DrawImage(icon, new Rectangle(x + size + gap, y, size * big, size * big));
                            y += 48 * big + gap;
                        }
                        x += bigCell;
                    }
                }
            }
            more.Save(Path.Combine(outDir, $"tray-icons-large-taskbar-{taskbar}.png"), ImageFormat.Png);
        }
        Console.WriteLine($"tray-icons-large-taskbar-{taskbar}: saved");

        const int card = 40 + 14 + 48 + 80;
        using var cards = new Bitmap(states.Length * card, 64);
        using (var c = Graphics.FromImage(cards))
        {
            c.Clear(back);
            c.InterpolationMode = InterpolationMode.NearestNeighbor;
            c.PixelOffsetMode = PixelOffsetMode.Half;
            int x = 12;
            foreach (var state in states)
            {
                using var art = TrayIcons.Art(state, 16, darkTaskbar);
                c.DrawImageUnscaled(art, x + 12, 24);
                c.DrawImage(art, new Rectangle(x + 54, 8, 48, 48));
                x += card;
            }
        }
        cards.Save(Path.Combine(outDir, $"tray-icons-cards-{taskbar}.png"), ImageFormat.Png);
        Console.WriteLine($"tray-icons-cards-{taskbar}: saved");
    }
}
