using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using SteppedOut;

// Sheets of the Away and back tab (page alone in a host), for comparing against the design sketch. Pictures are named
// tab-away-<what>-<language>-<theme>[-150|-large|-text150].png: "empty" is a new user's settings, "filled" has every switch
// on, a sound on each side, Windows' own apps in the lists and a script on each side. The checks (nothing outside
// what holds it, no cut-off words, names, Tab order, the control at the end of each row, the edits each control
// makes, no edit from showing settings) go to away-check.txt. With AWAY_REF set to the folder of the sketch's
// renders, compare/ gets each picture beside the sketch's tab 1.
static class AwaySheet
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

    enum Size3 { Normal, Dpi150, LargeText, Text150, Text200, Text225 }

    static string Suffix(Size3 size) => size switch
    {
        Size3.Dpi150 => "-150",
        Size3.LargeText => "-large",
        Size3.Text150 => "-text150",
        Size3.Text200 => "-text200",
        Size3.Text225 => "-text225",
        _ => "",
    };

    // The text size each kind of sheet uses: Large is 1.2, and Windows' own text size at 150%, 200%
    // and 225% is 1.5, 2 and 2.25.
    static float TextScale(Size3 size) => size switch
    {
        Size3.LargeText => 1.2f,
        Size3.Text150 => 1.5f,
        Size3.Text200 => 2f,
        Size3.Text225 => 2.25f,
        _ => 1f,
    };

    static string outDir = "";
    static bool pictures;
    static bool firstRun = true;
    static int failures;
    static TextWriter? report;
    static readonly Dictionary<string, int> normalHeights = new();

    public static void Run(string outDir, string lang, string theme)
    {
        AwaySheet.outDir = outDir;
        pictures = true;
        failures = 0;
        string file = Path.Combine(outDir, "away-check.txt");
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
        Console.WriteLine(failures == 0 ? "away: all checks passed" : $"away: {failures} checks failed");
    }

    // The same sheets and checks without pictures, for the window checks: both languages, light, at 100%, 150%, Large text
    // and Windows' text size at 150%. Returns how many checks failed.
    public static int Check()
    {
        pictures = false;
        failures = 0;
        foreach (var lang in new[] { "en", "ar" })
        {
            Lang.Use(lang);
            Theme.UseForShots(false);
            Each($"{lang}-light");
        }
        Console.WriteLine(failures == 0 ? "away: all checks passed" : $"away: {failures} checks failed");
        return failures;
    }

    static void Each(string tag)
    {
        bool rtl = Lang.Current.RightToLeft;
        foreach (var size in new[] { Size3.Normal, Size3.Dpi150, Size3.LargeText, Size3.Text150 })
        {
            Sheet("empty", tag, size, rtl, new Config());
            Sheet("filled", tag, size, rtl, Filled());
            Sheet("paths", tag, size, rtl, Paths());
        }
        // Choose… given a script on another PC and then a file of another kind, and Add app under Open
        // apps given a program on another PC: nothing is kept, and each row says why in the warning
        // color. The pictures also at Windows' text sizes of 200% and 225%, where the time-away
        // preview goes onto two lines.
        foreach (var size in new[] { Size3.Normal, Size3.Dpi150, Size3.Text150 })
            Sheet("refused", tag, size, rtl, new Config(), refusing: true);
        if (pictures)
        {
            foreach (var size in new[] { Size3.Text200, Size3.Text225 })
                Sheet("refused", tag, size, rtl, new Config(), refusing: true);
        }
        // In a window that does not mirror itself, each control flips its own drawing.
        if (rtl) Sheet("filled-unmirrored", tag, Size3.Normal, false, Filled());
        Edits(tag);
        SoundMenus(tag);
        Compare(tag);
    }

    // Every switch on, a sound each side, Windows' own apps and a script each side.
    static Config Filled()
    {
        var c = new Config
        {
            MuteWhenAway = true,
            ScreensOffWhenAway = true,
            SoundWhenAway = ActionSound.SteppedOut,
            ShowTimeAway = true,
            SoundWhenBack = ActionSound.Drift,
            RunWhenAway = @"C:\Scripts\leaving.cmd",
            RunWhenBack = @"C:\Scripts\welcome-back.ps1",
        };
        c.OpenWhenAway.AddRange([NotepadFile, OpenApps.AppsFolder + MediaPlayer]);
        c.CloseWhenAway.AddRange(["notepad", "mspaint"]);
        c.OpenWhenBack.AddRange([NotepadFile, OpenApps.AppsFolder + Paint]);
        c.CloseWhenBack.Add("mspaint");
        return c;
    }

    // Windows' own Store apps, by their ids: the part after ! is the package's own dotted name for one and just App
    // for the other. The Start menu's names for them are fixed here, so the pictures are the same on every PC.
    const string MediaPlayer = "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic";
    const string Paint = "Microsoft.Paint_8wekyb3d8bbwe!App";

    const string NotepadFile = @"C:\Windows\System32\notepad.exe";

    static string? StartMenuName(string id) => id switch
    {
        MediaPlayer => "Media Player",
        Paint => "Paint",
        _ => null,
    };

    // The apps running while the pictures are made, fixed so they are the same on every PC: Notepad,
    // so a Close apps chip shows the name it gives itself, and not Paint, which shows as kept.
    sealed class FixedApps : IProcessSource
    {
        sealed class Notepad : IRunningProcess
        {
            public int Id => 1;
            public string Name => "notepad";
            public IntPtr MainWindow => IntPtr.Zero;
            public string MainWindowTitle => "";
            public void Dispose()
            {
            }
        }

        public IEnumerable<IRunningProcess> Running() => [new Notepad()];
        public bool IsHiddenOrTool(IntPtr window) => true;
        public string? ImagePath(int processId) => NotepadFile;
        public string? Aumid(int processId) => null;
    }

    // Script paths that are hard to show: a network path too long for its line, with no spaces to break it at,
    // and one with Arabic folder names.
    static Config Paths() => new()
    {
        RunWhenAway = @"\\nas\share\Automation\Scripts\NoSpacesAtAllInThisFolderNameBecauseItIsVeryLongIndeedAndKeepsGoing\Another\leaving_v2_final.cmd",
        SoundWhenAway = ActionSound.Own,
        SoundWhenAwayFile = @"D:\Sounds\A long recording of soft rain on the window at night.wav",
        SoundWhenBack = ActionSound.Own,
        SoundWhenBackFile = "D:\\Sounds\\\u0635\u0648\u062A \u0627\u0644\u0645\u0637\u0631.wav",
        RunWhenBack = "D:\\Team\\\u0633\u0627\u0631\u0629\\Documents\\\u0639\u0648\u062F\u0629 \u0627\u0644\u062C\u0647\u0627\u0632.ps1",
    };

    static void Is(bool ok, string what)
    {
        string line = (ok ? "ok   " : "FAIL ") + what;
        Console.WriteLine(line);
        report?.WriteLine(line);
        if (!ok) failures++;
    }

    static int P(Control c, int logical) => c.LogicalToDeviceUnits(logical);

    // ---- Making and drawing a sheet ----

    // The page in a window as wide as the settings window, 20 px in from each side, as under the sketch's tabs.
    static (Form Form, Control Page, AwayTab Tab, List<Func<Config, Config>> Edits) Make(Config config, bool mirrored, bool refusing = false)
    {
        int scripts = 0;
        int sounds = 0;
        var f = new Form
        {
            AutoScaleDimensions = new SizeF(96F, 96F),
            AutoScaleMode = AutoScaleMode.Dpi,
            FormBorderStyle = FormBorderStyle.None,
            Font = Theme.Body(10f),
            RightToLeft = Lang.Direction,
            RightToLeftLayout = mirrored,
            Text = "away",
        };
        Theme.Current.Dress(f);
        Shots.Place(f);
        var edits = new List<Func<Config, Config>>();
        var tab = new AwayTab
        {
            StartMenuName = StartMenuName,
            Processes = new FixedApps(),
            PickApp = refusing ? (_, _, picked) => picked(new PickedApp("tool", @"\\nas\share\Tools\tool.exe", null))
                : (_, _, _) => Is(false, "no app list opens while drawing"),
            ChooseScript = _ =>
            {
                if (refusing) return ++scripts == 1 ? @"\\nas\share\Scripts\leaving.cmd" : @"C:\Scripts\notes.txt";
                Is(false, "no file dialog opens while drawing");
                return null;
            },
            // A sound on another PC for leaving, and one that is not there for coming back.
            ChooseSoundFile = _ =>
            {
                if (refusing) return ++sounds == 1 ? @"\\nas\share\Sounds\rain.wav" : @"C:\Windows\Media\SteppedOut-not-a-real-sound.wav";
                Is(false, "no sound dialog opens while drawing");
                return null;
            },
            Preview = sound => Is(false, $"no sound is played while drawing ({sound})"),
        };
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
        return (f, page, tab, edits);
    }

    static void Arrange(Form f, Control page)
    {
        int width = f.ClientSize.Width - P(f, 40);
        int height = page.GetPreferredSize(new Size(width, 0)).Height;
        page.Bounds = new Rectangle(P(f, 20), P(f, 16), width, height);
        f.ClientSize = new Size(f.ClientSize.Width, page.Bottom + P(f, 20));
        page.PerformLayout();
    }

    static void Sheet(string what, string tag, Size3 size, bool mirrored, Config config, bool refusing = false)
    {
        string name = $"tab-away-{what}-{tag}{Suffix(size)}";
        if (TextScale(size) != 1f) Theme.UseForShots(Theme.Current.IsDark, TextScale(size));
        try
        {
            var (f, page, _, edits) = Make(config, mirrored, refusing);
            if (size == Size3.Dpi150)
            {
                Rescale(f, 144);
                f.ClientSize = new Size(P(f, 780), f.ClientSize.Height);
                Arrange(f, page);
                Pump(150);
            }
            if (refusing)
            {
                Refuse(page);
                Arrange(f, page);
                Pump(50);
            }
            var drawn = new List<(Control Control, string Text, Font Font, Rectangle Box, TextFormatFlags Flags)>();
            KitControl.TextDrawn = (c, text, font, box, flags) => drawn.Add((c, text, font, box, flags));
            f.Refresh();
            using var picture = GrabAll(f, page);
            KitControl.TextDrawn = null;
            Shots.Foreground(f, name);
            Inspect(name, f, drawn, size, config);
            if (refusing) Refused(name, page);
            LogoUnmirrored(name, f, picture);
            Is(edits.Count == 0, $"{name}: building and drawing the page asked for no change");
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
            if (TextScale(size) != 1f) Theme.UseForShots(Theme.Current.IsDark, 1f);
        }
    }

    // Add app under Open apps when away, then Choose… on both script rows.
    static void Refuse(Control page)
    {
        var rows = All<SettingRow>(page).ToList();
        rows[3].Controls.OfType<KitButton>().Single().PerformClick();
        rows[2].Controls.OfType<DropDown>().Single().PressAction(0);
        rows[6].Controls.OfType<DropDown>().Single().PressAction(0);
        foreach (var script in rows.OfType<AwayTab.ScriptRow>()) script.Button.PerformClick();
    }

    static void Refused(string name, Control page)
    {
        var v = Lang.Current.V1;
        var rows = All<SettingRow>(page).ToList();
        var notes = rows.Where(r => r.DetailWarns).Select(r => r.Detail ?? "").ToList();
        Is(notes.SequenceEqual([v.SoundOnAnotherPC, v.AppOnAnotherPC, v.SoundCantPlay, v.ScriptOnAnotherPC, v.ScriptNotRunnable]),
            $"{name}: the two sound rows, Open apps and the two script rows say why nothing was kept, in the warning color ({string.Join(" | ", notes)})");
        var said = rows.Where(r => r.DetailWarns).Select(r => r.Controls.OfType<Control>().Single(c => c is KitButton or DropDown).AccessibleDescription).ToList();
        Is(said.SequenceEqual(notes), $"{name}: and their buttons and lists say it to a screen reader");
        var lists = rows.Select(r => r.Controls.OfType<DropDown>().SingleOrDefault()).OfType<DropDown>().ToList();
        Is(lists.All(l => Equals(l.Value, ActionSound.Off)), $"{name}: the sounds stay as they were");
    }

    // ---- The sound lists ----

    // Both sound lists open, each with your own sound picked earlier, drawn beside each other.
    static void SoundMenus(string tag)
    {
        var (f, page, _, edits) = Make(Paths(), Lang.Current.RightToLeft);
        var v = Lang.Current.V1;
        string name = $"tab-away-sound-menus-{tag}";
        var lists = All<SettingRow>(page).Select(r => r.Controls.OfType<DropDown>().SingleOrDefault()).OfType<DropDown>().ToList();
        Is(lists.Count == 2, $"{name}: two sound lists");
        string[] names = [v.Off, v.SoundSteppedOut, v.SoundDrift, v.SoundPebble, v.SoundGlow, v.SoundRipple];
        foreach (var list in lists)
        {
            Is(list.Choices.Take(6).SequenceEqual(names), $"{name}: {list.AccessibleName}: Off, SteppedOut and four more");
            Is(list.Rows[^1] == v.YourOwnSound && list.Rows.Count == 8, $"{name}: {list.AccessibleName}: your own sound by its name, then Your own sound…");
            Is(Equals(list.Value, ActionSound.Own) && list.Text == list.Rows[6], $"{name}: {list.AccessibleName}: shows the file's name ({list.Text})");
        }
        var menus = lists.Select(l => l.BuildMenu()).ToList();
        var parts = menus.Select(m => LockingSheet.DrawMenu(m, f)).ToList();
        foreach (var menu in menus) menu.Dispose();
        Is(edits.Count == 0, $"{name}: opening the lists asked for no change");
        if (pictures)
        {
            using var sheet = LockingSheet.Beside(parts, Theme.Current.Window, P(f, 16), Lang.Current.RightToLeft);
            sheet.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
            Console.WriteLine($"{name}: {sheet.Width}x{sheet.Height}");
        }
        foreach (var part in parts) part.Dispose();
        f.Close();
        f.Dispose();
        Pump(50);
    }

    // ---- Edits, in a real window ----

    // What each control asks for once the page is on the screen, and that showing settings asks for nothing.
    static void Edits(string tag)
    {
        var (f, page, tab, edits) = Make(new Config(), Lang.Current.RightToLeft);
        try
        {
            var rows = All<SettingRow>(page).ToList();
            Config Apply()
            {
                var c = new Config();
                foreach (var edit in edits) c = edit(c.Copy());
                edits.Clear();
                return c;
            }
            Is(rows.Count == 11, $"away-edits-{tag}: 11 rows ({rows.Count})");
            Toggle Switch(int row) => rows[row].Controls.OfType<Toggle>().Single();
            Switch(0).Checked = true;
            Is(Apply().MuteWhenAway, $"away-edits-{tag}: Mute the PC edits muteWhenAway");
            Switch(1).Checked = true;
            Is(Apply().ScreensOffWhenAway, $"away-edits-{tag}: Turn off the screen edits screensOffWhenAway");
            Switch(5).Checked = false;
            Is(!Apply().ShowTimeAway, $"away-edits-{tag}: Show how long you were away edits showTimeAway");
            var sound = rows[2].Controls.OfType<DropDown>().Single();
            typeof(Control).GetMethod("OnKeyDown", Any)!.Invoke(sound, [new KeyEventArgs(Keys.Down)]);
            Is(Apply().SoundWhenAway == ActionSound.SteppedOut, $"away-edits-{tag}: the away sound list edits soundWhenAway");
            tab.Refill(Filled());
            Pump(50);
            Is(edits.Count == 0, $"away-edits-{tag}: a refill asks for no change");
            tab.Refill(new Config());
            Is(edits.Count == 0, $"away-edits-{tag}: a refill back to the defaults asks for no change");
            Shots.Foreground(f, $"away-edits-{tag}");
        }
        finally
        {
            f.Close();
            f.Dispose();
            Pump(50);
        }
    }

    // ---- Checks ----

    static void Inspect(string name, Form f, List<(Control Control, string Text, Font Font, Rectangle Box, TextFormatFlags Flags)> drawn,
        Size3 size, Config config)
    {
        var all = Inside(f, post: false).Where(c => c.Visible).ToList();
        bool rtl = f.RightToLeft == RightToLeft.Yes;

        var outside = all.Where(c => c.Parent is { } p && !p.ClientRectangle.Contains(c.Bounds)).ToList();
        Is(outside.Count == 0, $"{name}: every control is inside what holds it{Few(outside.Select(c => $"{Describe(c)} {c.Bounds} in {c.Parent!.ClientSize}"))}");

        var cut = drawn.Distinct().Where(d => !Fits(d.Text, d.Font, d.Box, d.Flags)).ToList();
        Is(cut.Count == 0, $"{name}: the {drawn.Distinct().Count()} pieces of hand-drawn text fit their boxes{Few(cut.Select(d => $"\"{d.Text}\" {Measure(d.Text, d.Font, d.Box, d.Flags)} in {d.Box.Size}"))}");

        // D51: both Close apps lists carry the warning, drawn whole (checked with the rest of the text
        // above) in the space the list gives it, with the apps under it.
        string warning = Lang.Current.V1.CloseAppsWarning;
        var closeLists = all.OfType<AwayTab.CloseList>().ToList();
        var warned = closeLists.Where(l => drawn.Any(d => d.Control == l && d.Text == warning && d.Box.Bottom <= l.Chips.Top)).ToList();
        var under = closeLists.Where(l => l.Chips.Top > 0 && l.Chips.Bottom <= l.ClientSize.Height).ToList();
        Is(closeLists.Count == 2 && warned.Count == 2 && under.Count == 2,
            $"{name}: the Close apps warning is drawn above both Close apps lists, the apps under it ({warned.Count} drawn, {under.Count} with the apps under it, of {closeLists.Count})");
        // A screen reader hears it once for each list, on the list of apps, not on the group around it
        // and on Add app as well.
        var saying = all.Where(c => c.AccessibleDescription == warning).ToList();
        Is(saying.Count == 2 && saying.All(c => c is ChipList), $"{name}: a screen reader hears the warning on the two lists of apps only{Few(saying.Select(Describe))}");

        var labels = all.OfType<Label>().Where(l => l.Text.Length > 0 && l.Width > 0).ToList();
        var shortLabels = labels.Where(l => Wrapped(l) > l.Height).ToList();
        Is(shortLabels.Count == 0, $"{name}: the {labels.Count} labels are tall enough for their words{Few(shortLabels.Select(l => $"\"{l.Text}\" needs {Wrapped(l)} has {l.Height}"))}");

        var sized = all.Where(c => c is KitButton or Toggle || c is DropDown { ButtonWidth: 0 }).ToList();
        var squeezed = sized.Where(c => c.GetPreferredSize(Size.Empty) is var want && (want.Width > c.Width || want.Height > c.Height)).ToList();
        Is(squeezed.Count == 0, $"{name}: the {sized.Count} buttons, lists and switches have the room they ask for{Few(squeezed.Select(c => $"{Describe(c)} wants {c.GetPreferredSize(Size.Empty)} has {c.Size}"))}");

        var reachable = all.Where(c => c.CanSelect || c is Part).ToList();
        var unnamed = reachable.Where(c => string.IsNullOrWhiteSpace(c.AccessibilityObject.Name)).ToList();
        Is(unnamed.Count == 0, $"{name}: the {reachable.Count} controls a key reaches all have a name{Few(unnamed.Select(Describe))}");

        // Someone moving with Tab never meets the heads, so no two switches, lists, buttons or app lists share a name.
        var named = all.Where(c => c is Toggle or DropDown or KitButton or ChipList).Select(c => c.AccessibilityObject.Name ?? "").ToList();
        var twice = named.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => $"\"{g.Key}\"").ToList();
        Is(twice.Count == 0, $"{name}: the {named.Count} switches, lists, buttons and app lists all have names of their own{Few(twice)}");

        var stops = TabStops(f);
        var backwards = new List<string>();
        for (int i = 1; i < stops.Count; i++)
        {
            var a = Seen(stops[i - 1]);
            var b = Seen(stops[i]);
            bool below = b.Top >= a.Bottom - 2;
            bool along = b.Top < a.Bottom && a.Top < b.Bottom && (rtl ? b.Right <= a.Left + 2 : b.Left >= a.Right - 2);
            bool inside = a.Contains(b) || b.Contains(a);
            // An app list's chips sit under its words, Add app level with its title: Tab goes Add app, then the chips.
            bool chipsAfterAdd = stops[i - 1] is KitButton && stops[i] is Part && b.Top >= a.Top;
            if (!below && !along && !inside && !chipsAfterAdd) backwards.Add($"{Describe(stops[i - 1])} then {Describe(stops[i])}");
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
        Is(misordered.Count == 0, $"{name}: the chips of {groups.Count} lists are in order from the start of the line{Few(misordered)}");

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

        // The rows' words start at the page's start edge, the right in Arabic, and the previews and heads with them.
        var page = all.OfType<FlatPanel>().Single();
        var pageBox = Seen(page);
        var starts = rows.Select(r => Seen(r.Controls.OfType<Label>().First())).Select(t => rtl ? pageBox.Right - t.Right : t.Left - pageBox.Left)
            .Concat(all.OfType<PageHeading>().Select(h => Seen(h)).Select(h => rtl ? pageBox.Right - h.Right : h.Left - pageBox.Left))
            .Concat(all.OfType<AwayTab.AwayPreview>().Select(p => Seen(p)).Select(p => rtl ? pageBox.Right - p.Right : p.Left - pageBox.Left))
            .ToList();
        Is(starts.All(s => s == P(f, 20)), $"{name}: rows, heads and the preview start {P(f, 20)} px in from the page's {(rtl ? "right" : "left")} edge{Few(starts.Where(s => s != P(f, 20)).Select(s => s.ToString()))}");

        // A chosen script shows on one line with its file's name, and reads left to right in an Arabic window.
        foreach (var script in rows.OfType<AwayTab.ScriptRow>().Where(r => r.Path.Length > 0))
        {
            string shown = script.Detail ?? "";
            var label = script.Controls.OfType<Label>().Skip(1).First();
            int across = TextRenderer.MeasureText(shown, label.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
            string file = Path.GetFileName(script.Path);
            bool marked = shown.StartsWith('‪') && shown.EndsWith('‬');
            bool whole = script.Controls.OfType<KitButton>().Single().AccessibleDescription == script.Path;
            Is(shown.TrimEnd('‬').EndsWith(file, StringComparison.Ordinal) && across <= label.Width && marked == rtl && whole,
                $"{name}: \"{script.Title}\" shows \"{shown}\" on one line ({across} of {label.Width} px), with the file's name{(rtl ? ", left to right" : "")}, and the button describes the whole path");
        }

        // The switches, lists and buttons end level with each other.
        var ends = rows.Select(r => r.Controls.Cast<Control>().FirstOrDefault(c => c is DropDown or KitButton))
            .Where(c => c is not null).Select(c => Seen(c!)).Select(b => rtl ? b.Left - pageBox.Left : pageBox.Right - b.Right).ToList();
        Is(ends.Max() - ends.Min() <= 1, $"{name}: the lists and buttons end level with each other ({ends.Min()} to {ends.Max()} px from the edge)");

        // A Store app in an Open apps list shows the name the Start menu gives it, never its id.
        var store = all.OfType<ChipList>().SelectMany(l => l.Texts.Zip(l.Values))
            .Where(chip => chip.Second is string entry && entry.StartsWith(OpenApps.AppsFolder, StringComparison.OrdinalIgnoreCase)).ToList();
        var notNamed = store.Where(chip => chip.First != StartMenuName(((string)chip.Second!)[OpenApps.AppsFolder.Length..])).ToList();
        if (store.Count > 0)
            Is(notNamed.Count == 0, $"{name}: the {store.Count} Store apps show their Start menu names{Few(notNamed.Select(chip => $"\"{chip.First}\" for {chip.Second}"))}");

        // A program shows the name it gives itself, as on the Locking tab: "Notepad" for notepad.exe,
        // in Close apps too while it runs.
        var notepads = all.OfType<ChipList>().SelectMany(l => l.Texts.Zip(l.Values))
            .Where(chip => chip.Second is string entry && (entry == NotepadFile || entry == "notepad")).ToList();
        var ownless = notepads.Where(chip => ProgramNames.Describe(NotepadFile) is { } own && chip.First != own).ToList();
        if (notepads.Count > 0)
            Is(ownless.Count == 0, $"{name}: the {notepads.Count} chips for Notepad show the name it gives itself{Few(ownless.Select(chip => $"\"{chip.First}\" for {chip.Second}"))}");

        // No sample app from the sketch with a new user's settings.
        if (config.SameAs(new Config()))
        {
            string[] samples = ["OBS", "Zoom", "WhatsApp", "Google Chrome", "Spotify", "Discord", "Steam", "Microsoft Teams"];
            var words = all.Select(c => c.Text).Concat(drawn.Select(d => d.Text))
                .Concat(all.OfType<ChipList>().SelectMany(l => l.Texts)).ToList();
            var found = samples.Where(s => words.Any(w => w.Contains(s, StringComparison.OrdinalIgnoreCase))).ToList();
            Is(found.Count == 0, $"{name}: no example app on the page{Few(found)}");
        }

        if (size == Size3.Dpi150)
        {
            var other = all.Where(c => c.DeviceDpi != 144).ToList();
            Is(f.DeviceDpi == 144 && other.Count == 0, $"{name}: the window and its {all.Count} controls are at 150% (144 DPI){Few(other.Select(c => $"{Describe(c)} at {c.DeviceDpi}"))}");
        }
        string key = name.Replace("-text150", "").Replace("-text200", "").Replace("-text225", "").Replace("-150", "").Replace("-large", "");
        if (size == Size3.Normal) normalHeights[key] = f.ClientSize.Height;
        else if (normalHeights.TryGetValue(key, out int normal))
        {
            double ratio = (double)f.ClientSize.Height / normal;
            if (size == Size3.Dpi150) Is(ratio is > 1.35 and < 1.65, $"{name}: 1.5 times as tall as at 100% ({ratio:0.00})");
            else Is(ratio > 1.01, $"{name}: taller with Large text ({ratio:0.00})");
        }
    }

    // The logo in the time-away preview is the logo, never its mirror image, in either language and either kind of
    // window: at 150% it is big enough to have the shoe print, which only reads one way round.
    static void LogoUnmirrored(string name, Form f, Bitmap picture)
    {
        GetWindowRect(f.Handle, out var w);
        bool rtl = f.RightToLeft == RightToLeft.Yes;
        foreach (var preview in Inside(f, post: false).OfType<AwayTab.AwayPreview>().Where(p => p.Visible))
        {
            int side = P(preview, 14), pad = P(preview, 4);
            var seen = Seen(preview);
            int x = (rtl ? seen.Right - pad - side : seen.Left + pad) - w.L;
            int y = seen.Top + (preview.Height - side) / 2 - w.T;
            using var logo = Logo.Draw(side);
            int same = 0, other = 0, compared = 0;
            for (int i = 0; i < side; i++)
            {
                for (int j = 0; j < side; j++)
                {
                    var want = logo.GetPixel(i, j);
                    if (want.A < 255) continue;
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

    // The window as it shows, drawn by itself with PrintWindow, flipped back for a mirrored window.
    static Bitmap Grab(IntPtr handle)
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
                PrintWindow(handle, dc, 0);
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

    // The sketch's tab 1 (its tabs and the panel under them, cut from the window render) beside each picture.
    static void Compare(string tag)
    {
        string? folder = Environment.GetEnvironmentVariable("AWAY_REF");
        if (!pictures || string.IsNullOrEmpty(folder)) return;
        string sketch = Path.Combine(folder, $"settings-tab1-none-{tag}.png");
        if (!File.Exists(sketch))
        {
            Console.WriteLine($"compare {tag}: no sketch render to compare with");
            return;
        }
        Directory.CreateDirectory(Path.Combine(outDir, "compare"));
        using var whole = new Bitmap(sketch);
        // The window render is 1716 px tall: the tabs start at 696 and the footer takes the last 40.
        int top = Math.Min(696, whole.Height - 1);
        using var a = whole.Clone(new Rectangle(0, top, whole.Width, Math.Max(1, whole.Height - top - 40)), whole.PixelFormat);
        foreach (var what in new[] { "empty", "filled", "paths" })
        {
            string mine = Path.Combine(outDir, $"tab-away-{what}-{tag}.png");
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
            both.Save(Path.Combine(outDir, "compare", $"away-{what}-vs-sketch-{tag}.png"), ImageFormat.Png);
            Console.WriteLine($"compare/away-{what}-vs-sketch-{tag}.png");
        }
    }
}
