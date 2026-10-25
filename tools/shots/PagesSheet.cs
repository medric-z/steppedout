using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using SteppedOut;

// Sheets of the Appearance, Breaks and Extras tabs (each page alone in a host window), of the Locking
// tab with its shortcut's line, of the Extras tab's Custom… boxes, and of the Shortcut, Why and
// question windows, for comparing against the
// design sketch. Pictures are named
// pages-<what>-<language>-<theme>[-150|-large].png. The checks (nothing outside what holds it, no
// cut-off words, names, Tab order, the control at the end of each row, the edits each control makes,
// no edit from building or refilling a page, the logo the right way round) go to pages-check.txt.
// With PAGES_REF set to the folder of the sketch's renders, compare/ gets each tab beside the
// sketch's. The windows get a stand-in host that never registers a shortcut.
static class PagesSheet
{
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, ref RECT lParam);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT r, int size);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    const int ExStyle = -20;
    const int LayoutRtlStyle = 0x00400000;
    const int WmDpiChanged = 0x02E0;
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    enum Size3 { Normal, Dpi150, LargeText }

    static string Suffix(Size3 size) => size switch { Size3.Dpi150 => "-150", Size3.LargeText => "-large", _ => "" };

    static string outDir = "";
    static bool pictures;
    static bool firstRun = true;
    static int failures;
    static TextWriter? report;
    static readonly Dictionary<string, int> normalHeights = new();

    // What the app would be doing, for the windows that ask: shortcuts are taken or refused as a
    // scene says, and nothing is ever registered.
    sealed class PagesHost : ISettingsHost
    {
        public HotKeyResult ShortcutResult { get; set; } = HotKeyResult.Ok;
        public event Action? ShortcutResultChanged;
        public Func<string, HotKeyResult> Register { get; set; } = _ => HotKeyResult.Ok;
        public List<string> Applied { get; } = new();
        public IReadOnlyList<WhyLine> Lines { get; set; } = [];
        public LockStatus Status { get; set; } = new() { Kind = LockState.On, Time = LockTime.Plain(15), SecondsLeft = 14 * 60 };
        public event Action? StatusChanged;
        public bool StartsWithWindows => false;
        public AutostartState StartWithWindowsState => StartsWithWindows ? AutostartState.On : AutostartState.Off;
        public event Action? StartWithWindowsChanged
        {
            add { }
            remove { }
        }

        public void ShortcutChanged(string shortcut)
        {
            Applied.Add(shortcut);
            ShortcutResult = shortcut.Length == 0 ? HotKeyResult.Ok : Register(shortcut);
            ShortcutResultChanged?.Invoke();
        }

        // "Choose another shortcut…" said it opened (true) or closed (false).
        public List<bool> Choosing { get; } = new();

        public void ChoosingShortcut(bool open) => Choosing.Add(open);

        public IReadOnlyList<WhyLine> Explain() => Lines;

        public void PauseChosen(TimeSpan? length) => Is(false, "nothing pauses while drawing");
        public void ResumeChosen() => Is(false, "nothing turns back on while drawing");
        public void LockNowChosen() => Is(false, "nothing locks while drawing");
        public void TryItChosen() => Is(false, "nothing previews while drawing");
        public void LanguageChanged(string language) => Is(false, "the language stays while drawing");
        public void ConfigEdited(Config config) => Is(false, "pages hand their edits to the window, not the app");
        public void StartWithWindowsChosen(bool on) => Is(false, "start with Windows stays while drawing");
        public void ShowWelcomeChosen() => Is(false, "no welcome opens while drawing");
        public void RemoveFilesChosen() => Is(false, "nothing is removed while drawing");

        public void Tell() => StatusChanged?.Invoke();
    }

    public static void Run(string outDir, string lang, string theme)
    {
        PagesSheet.outDir = outDir;
        pictures = true;
        failures = 0;
        string file = Path.Combine(outDir, "pages-check.txt");
        if (firstRun)
        {
            File.Delete(file);
            firstRun = false;
        }
        using (report = File.AppendText(file))
        {
            Each($"{lang}-{theme}", theme == "dark");
        }
        report = null;
        Console.WriteLine(failures == 0 ? "pages: all checks passed" : $"pages: {failures} checks failed");
    }

    // The same sheets and checks without pictures, for the window checks: both languages, light, at
    // 100%, 150% and Large text. Returns how many checks failed.
    public static int Check()
    {
        pictures = false;
        failures = 0;
        foreach (var lang in new[] { "en", "ar" })
        {
            Lang.Use(lang);
            Theme.UseForShots(false);
            Each($"{lang}-light", dark: false);
        }
        Console.WriteLine(failures == 0 ? "pages: all checks passed" : $"pages: {failures} checks failed");
        return failures;
    }

    // Windows' own apps where a scene needs an app.
    static readonly WhyLine[] SampleLines =
    [
        new(Signal.Sound, Effect.Extra(60), "wmplayer"),
        new(Signal.Display, Effect.Extra(60), "wmplayer"),
        new(Signal.App, Effect.Never, "notepad"),
        new(Signal.Battery, Effect.Sooner(50)),
        new(Signal.Call, Effect.None, "", Counts: false),
    ];

    static void Each(string tag, bool dark)
    {
        bool rtl = Lang.Current.RightToLeft;
        foreach (var size in new[] { Size3.Normal, Size3.Dpi150, Size3.LargeText })
        {
            Page("appearance", tag, size, rtl, new Config(), _ => new AppearanceTab(), null);
            Page("breaks", tag, size, rtl, new Config(), _ => new BreaksTab(), null);
            Page("breaks-on", tag, size, rtl, BreaksOn(), _ => new BreaksTab(), null);
            Page("breaks-own", tag, size, rtl, BreaksOwn(), _ => new BreaksTab(), null);
            Page("advanced", tag, size, rtl, new Config(), h => new AdvancedTab(h, Opened), null);
            Page("locking-inuse", tag, size, rtl, WithShortcut(), h => new LockingTab(h), h => h.ShortcutResult = HotKeyResult.InUse);
            Page("locking-typesaletter", tag, size, rtl, WithShortcut(), h => new LockingTab(h), h => h.ShortcutResult = HotKeyResult.TypesALetter);
            Page("advanced-handset", tag, size, rtl, HandSet(), h => new AdvancedTab(h, Opened), null);
            Page("advanced-custom", tag, size, rtl, CustomSet(), h => new AdvancedTab(h, Opened), null);
            Questions(tag, size);
            CustomBoxes(tag, size, rtl);
        }
        // In a window that does not mirror itself, each control flips its own drawing.
        if (rtl)
        {
            Page("breaks-on-unmirrored", tag, Size3.Normal, false, BreaksOn(), _ => new BreaksTab(), null);
            Page("locking-inuse-unmirrored", tag, Size3.Normal, false, WithShortcut(), h => new LockingTab(h), h => h.ShortcutResult = HotKeyResult.InUse);
        }
        Picked(tag, dark, rtl);
        Shortcuts(tag);
        PauseFors(tag);
        Whys(tag);
        Edits(tag);
        Compare(tag);
    }

    static void Opened(string folder) => Is(false, "no folder opens while drawing");

    static Config BreaksOn() => new()
    {
        BreakReminders = true,
        BreakMinutes = 90,
        BreakSound = BreakSound.SteppedOut,
        EyeBreaks = true,
        SnoozeMinutes = 30,
    };

    // Reminders with your own sound, by a long file name.
    static Config BreaksOwn() => new()
    {
        BreakReminders = true,
        BreakSound = BreakSound.Own,
        BreakSoundFile = @"D:\Sounds\A long recording of soft rain on the window at night.wav",
    };

    // The shortcut picked, for its line: a new install has none.
    static Config WithShortcut() => new() { LockShortcut = "Ctrl+Alt+L" };

    // Values set by hand in the file: a loudness with a fraction of a percent, a silence and a log
    // size not on the lists, and tiny moves counted.
    static Config HandSet() => new()
    {
        AudioPeakThreshold = 0.002,
        AudioSilenceSeconds = 45,
        JitterFilter = false,
        LogMaxKilobytes = 100,
    };

    // Values from each list's Custom….
    static Config CustomSet() => new()
    {
        JitterPixels = 7,
        AudioPeakThreshold = 0.04,
        AudioSilenceSeconds = 45,
        LogMaxKilobytes = 2048,
    };

    static void Is(bool ok, string what)
    {
        string line = (ok ? "ok   " : "FAIL ") + what;
        Console.WriteLine(line);
        report?.WriteLine(line);
        if (!ok) failures++;
    }

    static int P(Control c, int logical) => c.LogicalToDeviceUnits(logical);

    // ---- Making and drawing a page ----

    // The page in a window as wide as the settings window, 20 px in from each side, as under the sketch's tabs.
    static (Form Form, Control Page, ISettingsPage Tab, List<Func<Config, Config>> Edits, PagesHost Host) Make(
        Config config, bool mirrored, Func<PagesHost, ISettingsPage> make, Action<PagesHost>? before)
    {
        var f = new Form
        {
            AutoScaleDimensions = new SizeF(96F, 96F),
            AutoScaleMode = AutoScaleMode.Dpi,
            FormBorderStyle = FormBorderStyle.None,
            Font = Theme.Body(10f),
            RightToLeft = Lang.Direction,
            RightToLeftLayout = mirrored,
            Text = "pages",
        };
        Theme.Current.Dress(f);
        Shots.Place(f);
        var host = new PagesHost();
        before?.Invoke(host);
        var edits = new List<Func<Config, Config>>();
        var tab = make(host);
        tab.Edited += edits.Add;
        var page = tab.Build(config, Lang.Current);
        f.Controls.Add(page);
        f.ClientSize = new Size(P(f, 780), P(f, 400));
        Arrange(f, page);
        f.Show();
        Pump(150);
        // Again once shown: until then every control reads as hidden, and the panel leaves hidden rows out.
        Arrange(f, page);
        Pump(50);
        return (f, page, tab, edits, host);
    }

    static void Arrange(Form f, Control page)
    {
        int width = f.ClientSize.Width - P(f, 40);
        int height = page.GetPreferredSize(new Size(width, 0)).Height;
        page.Bounds = new Rectangle(P(f, 20), P(f, 16), width, height);
        f.ClientSize = new Size(f.ClientSize.Width, page.Bottom + P(f, 20));
        page.PerformLayout();
    }

    static void Page(string what, string tag, Size3 size, bool mirrored, Config config, Func<PagesHost, ISettingsPage> make, Action<PagesHost>? before)
    {
        string name = $"pages-{what}-{tag}{Suffix(size)}";
        if (size == Size3.LargeText) Theme.UseForShots(Theme.Current.IsDark, 1.2f);
        try
        {
            var (f, page, tab, edits, _) = Make(config, mirrored, make, before);
            if (size == Size3.Dpi150)
            {
                Rescale(f, 144);
                f.ClientSize = new Size(P(f, 780), f.ClientSize.Height);
                Arrange(f, page);
                Pump(150);
            }
            var drawn = new List<(Control Control, string Text, Font Font, Rectangle Box, TextFormatFlags Flags)>();
            KitControl.TextDrawn = (c, text, font, box, flags) => drawn.Add((c, text, font, box, flags));
            f.Refresh();
            using var picture = GrabAll(f, page);
            KitControl.TextDrawn = null;
            Shots.Foreground(f, name);
            Inspect(name, f, drawn, size, config, page: true);
            LogoUnmirrored(name, f, picture);
            WordsAtTheirEdge(name, f, picture);
            tab.Refill(config);
            Pump(30);
            Is(edits.Count == 0, $"{name}: building, drawing and refilling the page asked for no change");
            if (pictures)
            {
                picture.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
                Console.WriteLine($"{name}: {picture.Width}x{picture.Height}");
            }
            f.Close();
            f.Dispose();
            Pump(50);
        }
        finally
        {
            if (size == Size3.LargeText) Theme.UseForShots(Theme.Current.IsDark, 1f);
        }
    }

    // A look picked on the Appearance tab, the other mode with Large text and the notes top left: the
    // app uses it at once and builds the page again in it, which is what this draws.
    static void Picked(string tag, bool dark, bool rtl)
    {
        var picked = new Config { Theme = dark ? ThemeChoice.Light : ThemeChoice.Dark, TextSize = TextSize.Large, NotificationPlace = NotePosition.TopLeft };
        Theme.UseForShots(!dark, 1.2f);
        try
        {
            Page("appearance-picked", tag, Size3.Normal, rtl, picked, _ => new AppearanceTab(), null);
        }
        finally
        {
            Theme.UseForShots(dark, 1f);
        }
    }

    // ---- The shortcut window ----

    static void Shortcuts(string tag)
    {
        // Nothing pressed yet.
        Dialog("shortcut-empty", tag, host => new ShortcutDialog(host, "Ctrl+Alt+L", Lang.Current, _ => HotKeyResult.Ok, () => false), null);
        // A letter on its own.
        Dialog("shortcut-letter", tag, host => new ShortcutDialog(host, "Ctrl+Alt+L", Lang.Current, _ => HotKeyResult.Ok, () => false),
            d => d.Take(Keys.L));
        // A shortcut that types a letter on one of the keyboards.
        Dialog("shortcut-typesaletter", tag, host => new ShortcutDialog(host, "Ctrl+Alt+L", Lang.Current, _ => HotKeyResult.TypesALetter, () => false),
            d => d.Take(Keys.Control | Keys.Alt | Keys.E));
        // Another app has it: Save says so and the window stays.
        Dialog("shortcut-inuse", tag, host =>
        {
            host.Register = s => s == "Ctrl+Alt+K" ? HotKeyResult.InUse : HotKeyResult.Ok;
            return new ShortcutDialog(host, "Ctrl+Alt+L", Lang.Current, _ => HotKeyResult.Ok, () => false);
        }, d =>
        {
            d.Take(Keys.Control | Keys.Alt | Keys.K);
            d.Save();
        });
        // One that can be saved.
        Dialog("shortcut-accepted", tag, host => new ShortcutDialog(host, "Ctrl+Alt+L", Lang.Current, _ => HotKeyResult.Ok, () => false),
            d => d.Take(Keys.Control | Keys.Shift | Keys.F9));
    }

    // ---- The questions before Reset… and Remove… ----

    static void Questions(string tag, Size3 size)
    {
        var v = Lang.Current.V1;
        if (size == Size3.LargeText) Theme.UseForShots(Theme.Current.IsDark, 1.2f);
        try
        {
            foreach (var (what, question) in new[]
            {
                ("question-reset", new Question(v.ResetTitle, v.ResetBody, v.Reset)),
                ("question-remove", new Question(v.RemoveTitle, v.RemoveBody, v.RemoveAndQuit)),
            })
            {
                string name = what + Suffix(size);
                Dialog(name, tag, _ => new QuestionWindow(question, Lang.Current), d =>
                {
                    if (size == Size3.Dpi150) SettingsSheet.Rescale(d, 144);
                    Is(d.ActiveControl == d.Dismiss && d.AcceptButton is null && d.CancelButton == d.Dismiss,
                        $"pages-{name}-{tag}: the keyboard starts on Cancel, Enter does not go ahead and Escape cancels");
                    Is(d.GoAhead.Text == question.GoAhead && d.Title.Text == question.Title && d.Body.Text == question.Body,
                        $"pages-{name}-{tag}: the title, what it means and the button that goes ahead");
                    var go = Seen(d.GoAhead);
                    var cancel = Seen(d.Dismiss);
                    bool rtl = d.RightToLeft == RightToLeft.Yes;
                    Is(rtl ? cancel.Right <= go.Left : go.Right <= cancel.Left,
                        $"pages-{name}-{tag}: Cancel is last in reading order, as in Windows' own questions ({go} and {cancel})");
                });
            }
        }
        finally
        {
            if (size == Size3.LargeText) Theme.UseForShots(Theme.Current.IsDark, 1f);
        }
    }

    // ---- The Extras tab's Custom… boxes ----

    // Each list's Custom… box as it opens and after a number past its range, beside each other.
    static void CustomBoxes(string tag, Size3 size, bool rtl)
    {
        string name = $"pages-advanced-customboxes-{tag}{Suffix(size)}";
        if (size == Size3.LargeText) Theme.UseForShots(Theme.Current.IsDark, 1.2f);
        try
        {
            var (f, page, _, edits, _) = Make(new Config(), rtl, h => new AdvancedTab(h, Opened), null);
            var lists = new[] { "ignoreTinyMoves", "loudness", "silence", "logSize" }.Select(n => All<DropDown>(page).Single(d => d.Name == n)).ToList();
            var parts = new List<Bitmap>();
            var boxes = new List<CustomBox>();
            foreach (var list in lists)
            {
                list.Presenter = box =>
                {
                    Shots.Place(box);
                    box.Show(f);
                    boxes.Add(box);
                };
                list.OpenCustom(0);
                Pump(200);
                var box = boxes[^1];
                if (size == Size3.Dpi150) Rescale(box, 144);
                box.PerformLayout();
                Pump(150);
                parts.Add(Grab(box.Handle));
                Inspect($"{name}: the {list.Name} box", box, new(), size, new Config(), page: false);
                box.Field.Controls.OfType<TextBox>().Single().Text = "100000";
                Pump(150);
                parts.Add(Grab(box.Handle));
                Inspect($"{name}: the {list.Name} box past its range", box, new(), size, new Config(), page: false);
                box.Key(Keys.Escape);
                Pump(200);
                Is(box.IsDisposed, $"{name}: Escape closes the {list.Name} box");
            }
            Is(edits.Count == 0, $"{name}: opening and leaving the boxes asked for no change");
            if (pictures)
            {
                using var sheet = Beside(parts, P(f, 16));
                sheet.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
                Console.WriteLine($"{name}: {sheet.Width}x{sheet.Height}");
            }
            foreach (var part in parts) part.Dispose();
            Close(f);
        }
        finally
        {
            if (size == Size3.LargeText) Theme.UseForShots(Theme.Current.IsDark, 1f);
        }
    }

    // Pictures side by side on the window's colour, in reading order.
    static Bitmap Beside(List<Bitmap> parts, int gap)
    {
        var sheet = new Bitmap(parts.Sum(p => p.Width) + gap * (parts.Count + 1), parts.Max(p => p.Height) + gap * 2);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Theme.Current.Window);
        int x = gap;
        foreach (var part in Lang.Current.RightToLeft ? Enumerable.Reverse(parts) : parts)
        {
            g.DrawImage(part, x, gap, part.Width, part.Height);
            x += part.Width + gap;
        }
        return sheet;
    }

    // ---- The Pause for window ----

    static void PauseFors(string tag)
    {
        static void Never(TimeSpan length) => Is(false, "nothing pauses while drawing");
        // As it opens: 3 hours, and the range under it.
        Dialog("pausefor", tag, _ => new PauseForWindow(Lang.Current, Never), d =>
            Is(d.Line == Lang.Current.V1.PauseForRange && d.PauseButton.Enabled, "pausefor: opens with a number and the range"));
        // Too long: the nearest, said under the box.
        Dialog("pausefor-clamped", tag, _ => new PauseForWindow(Lang.Current, Never), d =>
        {
            d.Number.Value = "30";
            using var menu = d.Unit.BuildMenu();
            menu.Items.OfType<ToolStripMenuItem>().ElementAt(2).PerformClick();
            Is(d.Line == string.Format(Lang.Current.V1.PauseForClamped, Lang.Current.PauseLength(PauseForWindow.Longest)), "pausefor-clamped: says the nearest");
        });
        // No number: Pause is off.
        Dialog("pausefor-empty", tag, _ => new PauseForWindow(Lang.Current, Never), d =>
        {
            d.Number.Value = "";
            Is(!d.PauseButton.Enabled, "pausefor-empty: Pause is off without a number");
        });
    }

    // ---- The Why window ----

    static void Whys(string tag)
    {
        Dialog("why-nothing", tag, host => new WhyDialog(host, () => true, Lang.Current), null);
        Dialog("why-lines", tag, host =>
        {
            host.Lines = SampleLines;
            return new WhyDialog(host, () => true, Lang.Current);
        }, null);
        Dialog("why-paused", tag, host =>
        {
            host.Status = new LockStatus { Kind = LockState.Paused, Time = LockTime.Plain(15), PausedUntil = DateTime.Today.AddHours(17).AddMinutes(5) };
            host.Lines = [SampleLines[0]];
            return new WhyDialog(host, () => true, Lang.Current);
        }, null);
        // Many rows: the list scrolls rather than grow past the screen.
        Dialog("why-many", tag, host =>
        {
            host.Lines = Enumerable.Range(1, 30).Select(i => new WhyLine(Signal.App, Effect.Extra(i), "notepad" + i)).ToList();
            return new WhyDialog(host, () => true, Lang.Current);
        }, null);
        // The same moved to a monitor at 150%, where a scroll bar is wider.
        Dialog("why-many-150", tag, host =>
        {
            host.Lines = Enumerable.Range(1, 60).Select(i => new WhyLine(Signal.App, Effect.Extra(i), "notepad" + i)).ToList();
            return new WhyDialog(host, () => true, Lang.Current);
        }, d => SettingsSheet.Rescale(d, 144));
    }

    static void Dialog<T>(string what, string tag, Func<PagesHost, T> make, Action<T>? after) where T : Form
    {
        string name = $"pages-{what}-{tag}";
        var host = new PagesHost();
        var d = make(host);
        Shots.Place(d);
        d.Show();
        Pump(150);
        if (after is not null)
        {
            after(d);
            Pump(100);
        }
        var drawn = new List<(Control Control, string Text, Font Font, Rectangle Box, TextFormatFlags Flags)>();
        KitControl.TextDrawn = (c, text, font, box, flags) => drawn.Add((c, text, font, box, flags));
        d.Refresh();
        using var picture = Grab(d.Handle, 2);
        KitControl.TextDrawn = null;
        Shots.Foreground(d, name);
        Inspect(name, d, drawn, Size3.Normal, new Config(), page: false);
        var area = Screen.PrimaryScreen!.WorkingArea;
        Is(d.Height <= area.Height, $"{name}: no taller than the screen ({d.Height} of {area.Height})");
        if (what == "shortcut-inuse")
        {
            Is(host.Applied.SequenceEqual(["Ctrl+Alt+K", ""]), $"{name}: Save tried Ctrl+Alt+K, then let go of it, holding nothing while the window is open ({string.Join(", ", host.Applied)})");
            Is(d.Visible && d.DialogResult == DialogResult.None, $"{name}: the window stays open");
        }
        if (d is ShortcutDialog)
            Is(host.Choosing.SequenceEqual([true]), $"{name}: the app let go of the shortcut as the window showed ({string.Join(", ", host.Choosing)})");
        if (d is WhyDialog why)
        {
            var scroller = why.Scroller;
            var wide = scroller.Controls.Cast<Control>().Where(c => c.Right > scroller.ClientSize.Width || c.Left < 0).ToList();
            Is(!scroller.HorizontalScroll.Visible && wide.Count == 0, $"{name}: the rows fit across the card, beside its scroll bar when it has one{Few(wide.Select(Describe))}");
            bool scrolls = scroller.VerticalScroll.Visible;
            var inside = Rectangle.Inflate(why.List.ClientRectangle, -P(why, 1), scrolls ? -P(why, 6) : 0);
            Is(inside.Contains(scroller.Bounds) && scroller.TabStop == scrolls,
                $"{name}: the rows{(scrolls ? " and their scroll bar" : "")} sit inside the card's edge{(scrolls ? ", clear of its corners, and Tab reaches them" : ", and Tab passes them by")} ({scroller.Bounds} in {why.List.ClientRectangle})");
            if (what == "why-many-150") Is(scrolls, $"{name}: sixty rows scroll");
            // Where each row sits on the screen: the thing at the reading start, what it does at the end.
            bool rtl = why.RightToLeft == RightToLeft.Yes;
            var card = Seen(why.List);
            var rows = scroller.Controls.OfType<Label>().ToList();
            if (why.Lines.Count % 2 == 1) rows = rows.Skip(1).ToList();
            var backwards = new List<string>();
            for (int i = 0; i + 1 < rows.Count; i += 2)
            {
                Rectangle thing = Seen(rows[i]), effect = Seen(rows[i + 1]);
                int fromStart = rtl ? card.Right - thing.Right : thing.Left - card.Left;
                bool ok = (rtl ? effect.Right <= thing.Left : thing.Right <= effect.Left) && fromStart <= P(why, 12) + P(why, 2);
                if (!ok) backwards.Add($"\"{rows[i].Text}\" {thing} and \"{rows[i + 1].Text}\" {effect} in {card}");
            }
            if (rows.Count > 0) Is(backwards.Count == 0, $"{name}: the {rows.Count / 2} rows start at the reading start, each thing before what it does{Few(backwards)}");
        }
        if (d is ShortcutDialog shortcut)
        {
            var box = Seen(shortcut.Box);
            var inside = d.RectangleToScreen(d.ClientRectangle);
            int before = box.Left - inside.Left, behind = inside.Right - box.Right;
            Is(Math.Abs(before - behind) <= P(d, 2), $"{name}: the key box is centered in the window, so it reads the same either way ({before} and {behind})");
        }
        if (pictures)
        {
            picture.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
            Console.WriteLine($"{name}: {picture.Width}x{picture.Height}");
        }
        d.Close();
        d.Dispose();
        Pump(50);
    }

    // ---- Edits, in a real window ----

    // What each control asks for once the page is on the screen, through the way a person uses it.
    static void Edits(string tag)
    {
        static Config Apply(List<Func<Config, Config>> edits, Config start)
        {
            var c = start.Copy();
            foreach (var edit in edits) c = edit(c.Copy());
            edits.Clear();
            return c;
        }
        static T Of<T>(SettingRow row) where T : Control => row.Controls.OfType<T>().First(c => c != row.Below);
        static void Click(DropDown list, int row)
        {
            using var menu = list.BuildMenu();
            menu.Items.OfType<ToolStripMenuItem>().ElementAt(row).PerformClick();
        }

        {
            var (f, page, tab, edits, _) = Make(new Config(), Lang.Current.RightToLeft, _ => new AppearanceTab(), null);
            var rows = All<SettingRow>(page).ToList();
            Is(rows.Count == 4, $"pages-edits-{tag}: Appearance has 4 rows ({rows.Count})");
            var mode = Of<Segmented>(rows[0]);
            mode.Choose(mode.Parts[1]);
            Is(Apply(edits, new Config()).Theme == ThemeChoice.Dark, $"pages-edits-{tag}: Dark edits theme");
            var size = Of<Segmented>(rows[1]);
            size.Choose(size.Parts[2]);
            Is(Apply(edits, new Config()).TextSize == TextSize.Large, $"pages-edits-{tag}: Large edits textSize");
            Of<Toggle>(rows[2]).Checked = true;
            Is(Apply(edits, new Config()).MinutesOnTrayIcon, $"pages-edits-{tag}: the switch edits minutesOnTrayIcon");
            Click(Of<DropDown>(rows[3]), 3);
            Is(Apply(edits, new Config()).NotificationPlace == NotePosition.TopLeft, $"pages-edits-{tag}: Top left edits notificationPlace");
            Close(f);
        }
        {
            var (f, page, tab, edits, _) = Make(new Config(), Lang.Current.RightToLeft, _ => new BreaksTab(), null);
            var rows = All<SettingRow>(page).ToList();
            Is(rows.Count == 5, $"pages-edits-{tag}: Breaks has 5 rows ({rows.Count})");
            Of<Toggle>(rows[0]).Checked = true;
            Is(Apply(edits, new Config()).BreakReminders, $"pages-edits-{tag}: the switch edits breakReminders");
            Click(Of<DropDown>(rows[1]), 3);
            Is(Apply(edits, new Config()).BreakMinutes == 90, $"pages-edits-{tag}: 90 minutes edits breakMinutes");
            var pill = All<NotePreview>(page).Single();
            Is(pill.Tail == Lang.Current.AtThePcFor(TimeSpan.FromMinutes(90)), $"pages-edits-{tag}: the preview says 90 minutes at the PC ({pill.Tail})");
            var sound = Of<DropDown>(rows[2]);
            var v = Lang.Current.V1;
            Is(sound.Choices.SequenceEqual([v.Off, v.SoundSteppedOut, v.SoundDawn, v.SoundMeadow, v.SoundLantern, v.SoundTide]) && sound.Rows[^1] == v.YourOwnSound,
                $"pages-edits-{tag}: the reminder sound: Off, SteppedOut and four more, then Your own sound…");
            Click(sound, 3);
            Is(Apply(edits, new Config()).BreakSound == BreakSound.Meadow, $"pages-edits-{tag}: Meadow edits breakSound");
            tab.Refill(BreaksOwn());
            Pump(30);
            Is(edits.Count == 0, $"pages-edits-{tag}: a Breaks refill asks for no change");
            Is(Equals(sound.Value, BreakSound.Own) && sound.Rows[^2] == sound.Text, $"pages-edits-{tag}: your own sound shows by its file's name ({sound.Text})");
            if (pictures)
            {
                using var menu = sound.BuildMenu();
                using var drawn = LockingSheet.DrawMenu(menu, f);
                drawn.Save(Path.Combine(outDir, $"pages-breaks-sound-menu-{tag}.png"), ImageFormat.Png);
                Console.WriteLine($"pages-breaks-sound-menu-{tag}: {drawn.Width}x{drawn.Height}");
            }
            tab.Refill(BreaksOn());
            Pump(30);
            Is(edits.Count == 0, $"pages-edits-{tag}: a Breaks refill asks for no change");
            Close(f);
        }
        {
            var (f, page, tab, edits, host) = Make(new Config(), Lang.Current.RightToLeft, h => new AdvancedTab(h, Opened), null);
            var rows = All<SettingRow>(page).ToList();
            Is(rows.Count == 9, $"pages-edits-{tag}: Extras has 9 rows ({rows.Count})");
            Is(!All<ShortcutNote>(page).Any() && rows.All(r => r.Title != Lang.Current.V1.Shortcut), $"pages-edits-{tag}: the shortcut is on the Locking tab, not here");
            Click(Of<DropDown>(rows[0]), 0);
            Is(!Apply(edits, new Config()).JitterFilter, $"pages-edits-{tag}: Off edits jitterFilter");
            Click(Of<DropDown>(rows[0]), 3);
            var under10 = Apply(edits, new Config { JitterFilter = false });
            Is(under10.JitterFilter && under10.JitterPixels == 10, $"pages-edits-{tag}: Under 10 pixels edits jitterFilter and jitterPixels");
            Click(Of<DropDown>(rows[1]), 2);
            Is(Apply(edits, new Config()).AudioPeakThreshold == 0.1, $"pages-edits-{tag}: Only loud sounds edits audioPeakThreshold");
            Click(Of<DropDown>(rows[2]), 0);
            Is(Apply(edits, new Config()).AudioSilenceSeconds == 10, $"pages-edits-{tag}: 10 seconds edits audioSilenceSeconds");
            Click(Of<DropDown>(rows[4]), 3);
            Is(Apply(edits, new Config()).LogMaxKilobytes == 4096, $"pages-edits-{tag}: 4 MB edits logMaxKilobytes");
            Is(host.Applied.Count == 0, $"pages-edits-{tag}: nothing on Extras registers a shortcut");
            Close(f);
        }
        {
            // The Locking tab's shortcut and At the lock time, with a shortcut picked earlier.
            var v = Lang.Current.V1;
            var withKeys = WithShortcut();
            var (f, page, tab, edits, host) = Make(withKeys, Lang.Current.RightToLeft, h => new LockingTab(h), null);
            var rows = All<SettingRow>(page).ToList();
            SettingRow Titled(string title) => rows.Single(r => r.Title == title);
            Click(Of<DropDown>(Titled(v.Shortcut)), 1);
            Is(Apply(edits, withKeys.Copy()).LockShortcut == "" && host.Applied.SequenceEqual([""]), $"pages-edits-{tag}: Off edits lockShortcut and turns it off at once");
            Click(Of<DropDown>(Titled(v.AtLockTime)), 2);
            var sleep = Apply(edits, new Config());
            Is(sleep.LockWhenAway && sleep.WhenLocking == WhenLocking.LockSleep, $"pages-edits-{tag}: Lock and sleep edits whenLocking");
            Click(Of<DropDown>(Titled(v.AtLockTime)), 3);
            var off = Apply(edits, new Config { WhenLocking = WhenLocking.LockSleep });
            Is(!off.LockWhenAway && off.WhenLocking == WhenLocking.LockSleep, $"pages-edits-{tag}: Don't lock edits lockWhenAway and keeps whenLocking");
            host.ShortcutResult = HotKeyResult.InUse;
            tab.Refill(withKeys);
            Pump(30);
            var note = All<ShortcutNote>(page).Single();
            Is(edits.Count == 0 && note.Wanted, $"pages-edits-{tag}: a refill shows the in-use line and asks for no change");
            tab.Refill(new Config { LockWhenAway = false });
            Pump(30);
            Is(edits.Count == 0 && Of<DropDown>(Titled(v.AtLockTime)).Text == v.DontLock, $"pages-edits-{tag}: locking turned off elsewhere shows Don't lock and asks for no change");
            Close(f);
        }
    }

    static void Close(Form f)
    {
        f.Close();
        f.Dispose();
        Pump(50);
    }

    // ---- Checks ----

    static void Inspect(string name, Form f, List<(Control Control, string Text, Font Font, Rectangle Box, TextFormatFlags Flags)> drawn,
        Size3 size, Config config, bool page)
    {
        var all = Inside(f, post: false).Where(c => Visible(c)).ToList();
        bool rtl = f.RightToLeft == RightToLeft.Yes;

        var outside = all.Where(c => c.Parent is { } p && p is not ScrollableControl { AutoScroll: true } && !p.ClientRectangle.Contains(c.Bounds)).ToList();
        Is(outside.Count == 0, $"{name}: every control is inside what holds it{Few(outside.Select(c => $"{Describe(c)} {c.Bounds} in {c.Parent!.ClientSize}"))}");

        var icons = drawn.Distinct().Where(d => d.Font.Name == Theme.Icons().Name).ToList();
        var cutIcons = icons.Where(d => !Fits(d.Text, d.Font, d.Box, d.Flags)).ToList();
        if (icons.Count > 0)
            Is(cutIcons.Count == 0, $"{name}: the {icons.Count} icons beside headings and notes are whole{Few(cutIcons.Select(d => $"{Describe(d.Control)} {Measure(d.Text, d.Font, d.Box, d.Flags)} in {d.Box.Size}"))}");

        var cut = drawn.Distinct().Where(d => !Fits(d.Text, d.Font, d.Box, d.Flags)).ToList();
        Is(cut.Count == 0, $"{name}: the {drawn.Distinct().Count()} pieces of hand-drawn text fit their boxes{Few(cut.Select(d => $"\"{d.Text}\" {Measure(d.Text, d.Font, d.Box, d.Flags)} in {d.Box.Size}"))}");

        var labels = all.OfType<Label>().Where(l => l.Text.Length > 0 && l.Width > 0).ToList();
        var shortLabels = labels.Where(l => Wrapped(l) > l.Height).ToList();
        Is(shortLabels.Count == 0, $"{name}: the {labels.Count} labels are tall enough for their words{Few(shortLabels.Select(l => $"\"{l.Text}\" needs {Wrapped(l)} has {l.Height}"))}");

        var sized = all.Where(c => c is KitButton or Toggle || c is DropDown { ButtonWidth: 0 } || c is Segmented).ToList();
        var squeezed = sized.Where(c => c.GetPreferredSize(Size.Empty) is var want && (want.Width > c.Width || want.Height > c.Height)).ToList();
        Is(squeezed.Count == 0, $"{name}: the {sized.Count} buttons, lists and switches have the room they ask for{Few(squeezed.Select(c => $"{Describe(c)} wants {c.GetPreferredSize(Size.Empty)} has {c.Size}"))}");

        var reachable = all.Where(c => c.CanSelect || c is Part).ToList();
        var unnamed = reachable.Where(c => string.IsNullOrWhiteSpace(c.AccessibilityObject.Name)).ToList();
        Is(unnamed.Count == 0, $"{name}: the {reachable.Count} controls a key reaches all have a name{Few(unnamed.Select(Describe))}");

        // The switch's On or Off is drawn beside it, and screen readers hear the state instead.
        var switches = all.OfType<Toggle>().ToList();
        var v = Lang.Current.V1;
        var said = switches.Where(s => s.AccessibilityObject.Name is { } n && (n.EndsWith(v.On, StringComparison.Ordinal) || n.EndsWith(v.Off, StringComparison.Ordinal))).ToList();
        Is(said.Count == 0, $"{name}: the {switches.Count} switches' names leave out the On or Off beside them{Few(said.Select(Describe))}");

        var stops = TabStops(f);
        var backwards = new List<string>();
        for (int i = 1; i < stops.Count; i++)
        {
            var a = Seen(stops[i - 1]);
            var b = Seen(stops[i]);
            bool below = b.Top >= a.Bottom - 2;
            bool along = b.Top < a.Bottom && a.Top < b.Bottom && (rtl ? b.Right <= a.Left + 2 : b.Left >= a.Right - 2);
            bool inside = a.Contains(b) || b.Contains(a);
            if (!below && !along && !inside) backwards.Add($"{Describe(stops[i - 1])} then {Describe(stops[i])}");
        }
        Is(backwards.Count == 0, $"{name}: Tab visits the {stops.Count} stops in reading order{Few(backwards)}");

        var groups = all.OfType<PartGroup>().Where(g => g.Parts.Count > 1).ToList();
        var misordered = new List<string>();
        foreach (var group in groups)
        {
            var seen = group.Parts.Select(p => Seen(p)).ToList();
            bool leftToRight = group.ReadsLeftToRight || !rtl;
            for (int i = 1; i < seen.Count; i++)
            {
                bool sameLine = seen[i].Top < seen[i - 1].Bottom && seen[i - 1].Top < seen[i].Bottom;
                bool next = sameLine ? (leftToRight ? seen[i].Left >= seen[i - 1].Right - 1 : seen[i].Right <= seen[i - 1].Left + 1) : seen[i].Top >= seen[i - 1].Bottom;
                if (!next) misordered.Add($"{Describe(group)} part {i}");
            }
        }
        Is(misordered.Count == 0, $"{name}: the parts of {groups.Count} choices run from the start of the line{Few(misordered)}");

        if (!page) return;

        var rows = all.OfType<SettingRow>().ToList();
        var wrongSide = new List<string>();
        foreach (var row in rows)
        {
            var title = row.Controls.OfType<Label>().First();
            var control = row.Controls.Cast<Control>().FirstOrDefault(c => c is not Label && c != row.Below);
            if (control is null) continue;
            var t = Seen(title);
            var c = Seen(control);
            if (rtl ? c.Right > t.Left + 1 : c.Left < t.Right - 1) wrongSide.Add($"\"{row.Title}\"");
        }
        Is(wrongSide.Count == 0, $"{name}: in the {rows.Count} rows the control is after the words{(rtl ? ", on the left" : ", on the right")}{Few(wrongSide)}");

        // The rows' words start at the page's start edge, the right in Arabic, and the heads, the preview and the shortcut line with them.
        var panel = all.OfType<FlatPanel>().Single();
        var pageBox = Seen(panel);
        int Start(Rectangle r) => rtl ? pageBox.Right - r.Right : r.Left - pageBox.Left;
        var starts = rows.Select(r => Start(Seen(r.Controls.OfType<Label>().First())))
            .Concat(all.OfType<PageHeading>().Select(h => Start(Seen(h))))
            .Concat(all.OfType<NotePreview>().Select(p => Start(Seen(p))))
            .Concat(all.OfType<ShortcutNote>().Select(n => Start(Seen(n))))
            .ToList();
        Is(starts.All(s => s == P(f, 20)), $"{name}: rows, heads, the preview and the shortcut line start {P(f, 20)} px in from the page's {(rtl ? "right" : "left")} edge{Few(starts.Where(s => s != P(f, 20)).Select(s => s.ToString()))}");

        // The switches, lists, choices and buttons end level with each other.
        var ends = rows.Select(r => r.Controls.Cast<Control>().FirstOrDefault(c => c is DropDown or KitButton or Segmented or Toggle))
            .Where(c => c is not null).Select(c => Seen(c!)).Select(b => rtl ? b.Left - pageBox.Left : pageBox.Right - b.Right).ToList();
        Is(ends.Count > 0 && ends.Max() - ends.Min() <= P(f, 4), $"{name}: the controls end level with each other ({ends.Min()} to {ends.Max()} px from the edge)");

        if (config.SameAs(new Config()))
        {
            string[] samples = ["OBS", "Zoom", "WhatsApp", "Google Chrome", "Spotify", "Discord", "Steam", "Microsoft Teams"];
            var words = all.Select(c => c.Text).Concat(drawn.Select(d => d.Text)).ToList();
            var found = samples.Where(s => words.Any(w => w.Contains(s, StringComparison.OrdinalIgnoreCase))).ToList();
            Is(found.Count == 0, $"{name}: no example app on the page{Few(found)}");
        }

        if (size == Size3.Dpi150)
        {
            var other = all.Where(c => c.DeviceDpi != 144).ToList();
            Is(f.DeviceDpi == 144 && other.Count == 0, $"{name}: the window and its {all.Count} controls are at 150% (144 DPI){Few(other.Select(c => $"{Describe(c)} at {c.DeviceDpi}"))}");
        }
        string key = name.Replace("-150", "").Replace("-large", "");
        if (size == Size3.Normal) normalHeights[key] = f.ClientSize.Height;
        else if (normalHeights.TryGetValue(key, out int normal))
        {
            double ratio = (double)f.ClientSize.Height / normal;
            if (size == Size3.Dpi150) Is(ratio is > 1.35 and < 1.65, $"{name}: 1.5 times as tall as at 100% ({ratio:0.00})");
            else Is(ratio > 1.01, $"{name}: taller with Large text ({ratio:0.00})");
        }
    }

    // The rows' words begin where their boxes do, so the rule that rows and heads start 20 px in holds for what
    // is drawn, not only for the boxes: the first column of each title's and explanation's ink is at most 1 px
    // in from its start edge.
    static void WordsAtTheirEdge(string name, Form f, Bitmap picture)
    {
        GetWindowRect(f.Handle, out var w);
        bool rtl = f.RightToLeft == RightToLeft.Yes;
        var far = new List<string>();
        int count = 0;
        foreach (var words in Inside(f, post: false).OfType<RowLabel>().Where(l => l.Tight && l.Visible && l.Text.Length > 0 && l.Width > 0 && l.Height > 0))
        {
            var seen = Seen(words);
            int top = Math.Max(0, seen.Top - w.T), bottom = Math.Min(picture.Height, seen.Bottom - w.T);
            int edge = rtl ? seen.Right - 1 - w.L : seen.Left - w.L;
            if (top >= bottom || edge < 0 || edge >= picture.Width) continue;
            var back = picture.GetPixel(edge, top);
            int gap = -1;
            for (int d = 0; d < P(f, 8) && gap < 0; d++)
            {
                int x = rtl ? edge - d : edge + d;
                for (int y = top; y < bottom; y++)
                {
                    var p = picture.GetPixel(x, y);
                    if (Math.Abs(p.R - back.R) + Math.Abs(p.G - back.G) + Math.Abs(p.B - back.B) <= 150) continue;
                    gap = d;
                    break;
                }
            }
            count++;
            if (gap < 0 || gap > P(f, 1)) far.Add($"\"{words.Text}\" {gap} px");
        }
        Is(count > 0 && far.Count == 0, $"{name}: the {count} row titles and explanations start at their box's {(rtl ? "right" : "left")} edge, level with the heads{Few(far)}");
    }

    // The logo in the break preview is the logo, never its mirror image, in either language and either kind of
    // window.
    static void LogoUnmirrored(string name, Form f, Bitmap picture)
    {
        GetWindowRect(f.Handle, out var w);
        bool rtl = f.RightToLeft == RightToLeft.Yes;
        foreach (var preview in Inside(f, post: false).OfType<NotePreview>().Where(p => p.Visible))
        {
            int side = P(preview, 14), pad = P(preview, 4);
            var seen = Seen(preview);
            int x = (rtl ? seen.Right - pad - side : seen.Left + pad) - w.L;
            int y = seen.Top + (Math.Min(preview.Height, preview.GetPreferredSize(Size.Empty).Height) - side) / 2 - w.T;
            using var logo = Logo.Draw(side);
            int same = 0, other = 0, compared = 0;
            for (int i = 0; i < side; i++)
            {
                for (int j = 0; j < side; j++)
                {
                    var want = logo.GetPixel(i, j);
                    if (want.A < 255) continue;
                    if (x + i < 0 || y + j < 0 || x + i >= picture.Width || y + j >= picture.Height) continue;
                    compared++;
                    var got = picture.GetPixel(x + i, y + j);
                    var turned = logo.GetPixel(side - 1 - i, j);
                    bool near(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) < 40;
                    if (near(got, want)) same++;
                    if (turned.A == 255 && !near(want, turned) && near(got, turned)) other++;
                }
            }
            Is(compared > 0 && same >= compared * 95 / 100 && other == 0,
                $"{name}: the preview's logo is drawn the right way round ({same} of {compared} pixels match, {other} match its mirror image)");
        }
    }

    static Rectangle Seen(Control c) => c.RectangleToScreen(c.ClientRectangle);

    static List<Control> TabStops(Form f)
    {
        var stops = new List<Control>();
        Control? at = null;
        for (int i = 0; i < 500; i++)
        {
            at = f.GetNextControl(at, true);
            if (at is null) break;
            if (at.CanSelect && at.TabStop && Visible(at)) stops.Add(at);
        }
        return stops;
    }

    static bool Visible(Control c)
    {
        for (var at = c; at is not null; at = at.Parent) if (!at.Visible) return false;
        return true;
    }

    static Size Measure(string text, Font font, Rectangle box, TextFormatFlags flags) =>
        flags.HasFlag(TextFormatFlags.WordBreak)
            ? TextRenderer.MeasureText(text, font, new Size(Math.Max(1, box.Width), int.MaxValue), flags & ~(TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter))
            : TextRenderer.MeasureText(text, font, Size.Empty, (flags | TextFormatFlags.SingleLine) & ~(TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak));

    static bool Fits(string text, Font font, Rectangle box, TextFormatFlags flags)
    {
        var size = Measure(text, font, box, flags);
        return size.Height <= box.Height + 1 && (flags.HasFlag(TextFormatFlags.WordBreak) || size.Width <= box.Width + 1);
    }

    static int Wrapped(Label l)
    {
        int width = Math.Max(1, l.ClientSize.Width - l.Padding.Horizontal);
        // A row's words, drawn and measured without Windows' end room.
        if (l is RowLabel { Tight: true } words) return words.HeightFor(width) + l.Padding.Vertical;
        int rows = Shapes.WrappedHeight(l.Text, l.Font, width);
        int box = TextRenderer.MeasureText(l.Text, l.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl).Height;
        return Math.Max(rows, box) + l.Padding.Vertical;
    }

    static string Describe(Control c) =>
        $"{c.GetType().Name}{(string.IsNullOrEmpty(c.AccessibleName) ? string.IsNullOrEmpty(c.Text) ? "" : $" \"{c.Text}\"" : $" \"{c.AccessibleName}\"")}";

    static string Few(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count == 0 ? "" : ": " + string.Join("; ", list.Take(4)) + (list.Count > 4 ? $"; and {list.Count - 4} more" : "");
    }

    static IEnumerable<T> All<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T match) yield return match;
            foreach (var deeper in All<T>(child)) yield return deeper;
        }
    }

    static IEnumerable<Control> Inside(Control parent, bool post)
    {
        foreach (Control child in parent.Controls)
        {
            if (!post) yield return child;
            foreach (var deeper in Inside(child, post)) yield return deeper;
            if (post) yield return child;
        }
    }

    // ---- Drawing ----

    // The window as it shows, drawn by itself with PrintWindow, flipped back for a mirrored window. A
    // window with a title bar needs the full content asked for (2) to draw it; a bare one is drawn as
    // the settings window's other sheets draw theirs (0), which comes back mirrored as the window is.
    static Bitmap Grab(IntPtr handle, uint how = 0)
    {
        Pump(60);
        GetWindowRect(handle, out var w);
        if (DwmGetWindowAttribute(handle, 9, out var r, Marshal.SizeOf<RECT>()) != 0) r = w;
        var crop = new Rectangle(r.L, r.T, r.R - r.L, r.B - r.T);
        using var whole = new Bitmap(w.R - w.L, w.B - w.T);
        using (var g = Graphics.FromImage(whole))
        {
            for (int i = 0; i < 2; i++)
            {
                IntPtr dc = g.GetHdc();
                PrintWindow(handle, dc, how);
                g.ReleaseHdc(dc);
                if (i == 0) Pump(60);
            }
        }
        bool mirrored = (GetWindowLong(handle, ExStyle) & LayoutRtlStyle) != 0;
        if (mirrored) whole.RotateFlip(RotateFlipType.RotateNoneFlipX);
        int left = mirrored ? w.R - r.R : r.L - w.L;
        var bmp = new Bitmap(crop.Width, crop.Height);
        using (var g2 = Graphics.FromImage(bmp))
            g2.DrawImage(whole, new Rectangle(0, 0, crop.Width, crop.Height), new Rectangle(left, r.T - w.T, crop.Width, crop.Height), GraphicsUnit.Pixel);
        return bmp;
    }

    // Windows keeps a window no taller than the screen, so a page taller than that (at 150% or a big text size) is drawn
    // in goes: as it is, then moved up a screenful at a time, each go adding the page below what is drawn so far, with a
    // little overlap so nothing fixed at the window's bottom is left between two goes.
    static Bitmap GrabAll(Form f, Control page)
    {
        var first = Grab(f.Handle);
        int want = page.Bottom + P(f, 20);
        if (want <= first.Height) return first;
        GetWindowRect(f.Handle, out var w);
        int top = Math.Max(0, page.Parent!.RectangleToScreen(page.Bounds).Top - w.T);
        int overlap = P(f, 40), height = first.Height;
        if (height - top - overlap <= 0) top = overlap = 0;
        var whole = new Bitmap(first.Width, want);
        int start = page.Top;
        using (var g = Graphics.FromImage(whole))
        {
            g.DrawImage(first, 0, 0, first.Width, first.Height);
            first.Dispose();
            try
            {
                for (int covered = height; covered < want;)
                {
                    int shift = Math.Min(want - height, covered - top - overlap);
                    page.Top = start - shift;
                    f.Refresh();
                    using var next = Grab(f.Handle);
                    g.DrawImage(next, new Rectangle(0, top + shift, next.Width, next.Height - top),
                        new Rectangle(0, top, next.Width, next.Height - top), GraphicsUnit.Pixel);
                    covered = next.Height + shift;
                }
            }
            finally
            {
                page.Top = start;
                f.Refresh();
            }
        }
        return whole;
    }

    static void Pump(int ms)
    {
        var until = DateTime.Now.AddMilliseconds(ms);
        do
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        while (DateTime.Now < until);
    }

    // ---- 150% ----

    // A move to a monitor at another scaling, as WinForms does it: every control is told the new DPI from the
    // inside out, then the window gets WM_DPICHANGED. Both monitors here are at 100%, so the move is only told.
    static void Rescale(Form f, int dpi)
    {
        foreach (var c in Inside(f, post: true).ToList()) Before(c, dpi);
        var b = f.Bounds;
        int old = f.DeviceDpi;
        var r = new RECT { L = b.Left, T = b.Top, R = b.Left + b.Width * dpi / old, B = b.Top + b.Height * dpi / old };
        SendMessage(f.Handle, WmDpiChanged, (IntPtr)((dpi << 16) | dpi), ref r);
        foreach (var c in Inside(f, post: false).ToList()) After(c);
    }

    static readonly PropertyInfo? DpiInternal = typeof(Control).GetProperty("DeviceDpiInternal", Any);
    static readonly PropertyInfo? OriginalDpi = typeof(Control).GetProperty("OriginalDeviceDpiInternal", Any);
    static readonly MethodInfo? CurrentFont = typeof(Control).GetMethod("GetCurrentFontAndDpi", Any);
    static readonly MethodInfo? FontSet = typeof(Control).GetMethod("IsFontSet", Any);
    static readonly MethodInfo? ScaledFont = typeof(Control).GetMethod("GetScaledFont", Any);
    static readonly PropertyInfo? ScaledControlFont = typeof(Control).GetProperty("ScaledControlFont", Any);
    static readonly MethodInfo? RescaleConstants = typeof(Control).GetMethod("RescaleConstantsForDpi", Any);
    static readonly MethodInfo? BeforeParent = typeof(Control).GetMethod("OnDpiChangedBeforeParent", Any);
    static readonly MethodInfo? AfterParent = typeof(Control).GetMethod("OnDpiChangedAfterParent", Any);

    static void Before(Control c, int dpi)
    {
        int old = (int)DpiInternal!.GetValue(c)!;
        OriginalDpi!.SetValue(c, old);
        var args = new object[] { 0 };
        var font = (Font)CurrentFont!.Invoke(c, args)!;
        int fontDpi = (int)args[0];
        DpiInternal.SetValue(c, dpi);
        if (fontDpi != dpi && (bool)FontSet!.Invoke(c, null)!) ScaledControlFont!.SetValue(c, ScaledFont!.Invoke(c, [font, dpi, fontDpi]));
        RescaleConstants!.Invoke(c, [old, dpi]);
        BeforeParent!.Invoke(c, [EventArgs.Empty]);
    }

    static void After(Control c) => AfterParent!.Invoke(c, [EventArgs.Empty]);

    // ---- Beside the sketch ----

    // The sketch's tabs 2, 3 and 4 (its tabs and the panel under them, cut from the window render) beside each picture.
    static void Compare(string tag)
    {
        string? folder = Environment.GetEnvironmentVariable("PAGES_REF");
        if (!pictures || string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(Path.Combine(outDir, "compare"));
        foreach (var (tab, whats) in new[] { ("tab2", new[] { "appearance", "appearance-picked" }), ("tab3", new[] { "breaks", "breaks-on" }), ("tab4", new[] { "advanced", "advanced-handset" }) })
        {
            string sketch = Path.Combine(folder, $"settings-{tab}-none-{tag}.png");
            if (!File.Exists(sketch))
            {
                Console.WriteLine($"compare {tab} {tag}: no sketch render to compare with");
                continue;
            }
            using var whole = new Bitmap(sketch);
            // The window render: the tabs start at 696 and the footer takes the last 40.
            int top = Math.Min(696, whole.Height - 1);
            using var a = whole.Clone(new Rectangle(0, top, whole.Width, Math.Max(1, whole.Height - top - 40)), whole.PixelFormat);
            foreach (var what in whats)
            {
                string mine = Path.Combine(outDir, $"pages-{what}-{tag}.png");
                if (!File.Exists(mine)) continue;
                using var b = new Bitmap(mine);
                int gap = 24;
                using var both = new Bitmap(a.Width + b.Width + gap * 3, Math.Max(a.Height, b.Height) + gap * 2);
                using (var g = Graphics.FromImage(both))
                {
                    g.Clear(Color.FromArgb(0x80, 0x80, 0x80));
                    g.DrawImage(a, gap, gap, a.Width, a.Height);
                    g.DrawImage(b, gap * 2 + a.Width, gap, b.Width, b.Height);
                }
                both.Save(Path.Combine(outDir, "compare", $"{what}-vs-sketch-{tag}.png"), ImageFormat.Png);
                Console.WriteLine($"compare/{what}-vs-sketch-{tag}.png");
            }
        }
    }
}
