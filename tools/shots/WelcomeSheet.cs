using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using SteppedOut;

// The welcome window: each of its five pages at 100%, 150% and 200% and with Large text, a few
// states its pages go through (a moment picked on the timeline, the battery, Try it, Play, a PC
// without a battery, times of one's own), and in English and light one sheet of the five pages side
// by side; and the checks that nothing in it is cut off, overlapping, unnamed, out of order or out
// of place. Pictures are named welcome-<page or state>-<language>-<theme>[-150|-200|-large].png, with
// -end for a page scrolled to its end; the checks go to welcome-check.txt.
static class WelcomeSheet
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    static string outDir = "";
    static bool pictures;
    static bool firstRun = true;
    static int failures;
    static TextWriter? report;

    public static void Run(string outDir, string lang, string theme)
    {
        WelcomeSheet.outDir = outDir;
        pictures = true;
        string file = Path.Combine(outDir, "welcome-check.txt");
        if (firstRun)
        {
            File.Delete(file);
            firstRun = false;
        }
        int before = failures;
        using (report = File.AppendText(file))
        {
            Each($"{lang}-{theme}", theme == "dark");
        }
        report = null;
        Console.WriteLine(failures == before ? "welcome: all checks passed" : $"welcome: {failures - before} checks failed");
    }

    // The same windows and checks without pictures: both languages, light. Returns how many failed.
    public static int Check()
    {
        pictures = false;
        failures = 0;
        foreach (var lang in new[] { "en", "ar" })
        {
            Lang.Use(lang);
            Theme.UseForShots(false);
            Each($"{lang}-light", false);
        }
        Console.WriteLine(failures == 0 ? "welcome: all checks passed" : $"welcome: {failures} checks failed");
        return failures;
    }

    static void Is(bool ok, string what)
    {
        string line = (ok ? "ok   " : "FAIL ") + what;
        Console.WriteLine(line);
        report?.WriteLine(line);
        if (!ok) failures++;
    }

    // A PC with a battery, so the pages show every question, and Play without gliding, so its
    // picture is the same each time; the PC without one is a state of its own.
    static void Each(string tag, bool dark)
    {
        var battery = WelcomeForm.HasBattery;
        var motion = WelcomeForm.Motion;
        WelcomeForm.HasBattery = () => true;
        WelcomeForm.Motion = () => false;
        try
        {
            Pages(tag, dark);
        }
        finally
        {
            WelcomeForm.HasBattery = battery;
            WelcomeForm.Motion = motion;
        }
    }

    static void Pages(string tag, bool dark)
    {
        var sheet = new List<Bitmap>();
        for (int page = 1; page <= WelcomeForm.PageCount; page++)
            Page(tag, page, 96, "", tag == "en-light" && pictures ? sheet : null);
        if (sheet.Count > 0) Sheet(sheet);
        foreach (int dpi in new[] { 144, 192 })
            for (int page = 1; page <= WelcomeForm.PageCount; page++) Page(tag, page, dpi, $"-{dpi * 100 / 96}");
        Theme.UseForShots(dark, 1.2f);
        try
        {
            for (int page = 1; page <= WelcomeForm.PageCount; page++) Page(tag, page, 96, "-large");
        }
        finally
        {
            Theme.UseForShots(dark, 1f);
        }
        States(tag);
        Wheel(tag);
        AwayAt1080p150(tag);
    }

    static void NoScroll(string name, WelcomeForm w)
    {
        var body = w.PageScroller;
        int needs = WelcomeBody.Measure(body.ClientSize.Width, body.Controls.Cast<Control>().Where(c => c.Visible));
        Is(needs <= body.ClientSize.Height && body.AutoScrollMinSize.Height <= body.ClientSize.Height,
            $"{name}: the page fits with nothing to scroll ({needs} in {body.ClientSize.Height}{(body.Controls.OfType<PageStack>().Any(p => p.Visible && p.ClosedUp) ? ", closed up" : "")})");
    }

    // The away page on a 1920 x 1080 screen at 150% with the taskbar at its usual 48 pixels (72 at
    // 150%): it fits without scrolling. The move to 150% keeps the window's size from 100% scaled up,
    // as Windows suggests it; sized again there as the window sizes itself when it opens at 150%,
    // with the frame Windows gives a window at that scaling, it is no taller than on that screen.
    static void AwayAt1080p150(string tag)
    {
        var area = WorkArea.Of;
        var spot = Shots.Spot();
        WorkArea.Of = _ => new Rectangle(spot.X - 200, spot.Y, 1920, 1080 - 72);
        try
        {
            var w = Open(page: 4, dpi: 144);
            SettingsSheet.Pump(120);
            w.Fit();
            w.PerformLayout();
            SettingsSheet.Pump(120);
            string name = $"welcome-4-{tag}-1080p150";
            Inspect(name, w, 144);
            NoScroll(name, w);
            if (pictures)
            {
                using var picture = SettingsSheet.Grab(w.Handle);
                Save(picture, name);
            }
            Close(w, name);
        }
        finally
        {
            WorkArea.Of = area;
        }
    }

    // ---- The window ----

    // On a page, then moved to a monitor at this scaling. With no settings given, a brand-new install's.
    static WelcomeForm Open(Config? config = null, int page = 1, int dpi = 96)
    {
        var w = new WelcomeForm(config ?? Config.FirstStart(), true)
        {
            // A Custom… box is never shown here.
            Presenter = box => box.Dispose(),
            ChooseShortcut = _ => null,
        };
        Shots.Place(w);
        w.Show();
        SettingsSheet.Pump(150);
        w.GoTo(page);
        SettingsSheet.Pump(80);
        if (dpi != 96)
        {
            SettingsSheet.Rescale(w, dpi);
            SettingsSheet.Pump(250);
        }
        return w;
    }

    static void Close(WelcomeForm w, string name)
    {
        Shots.Foreground(w, name);
        w.CloseWithoutFinishing();
        w.Dispose();
    }

    static void Page(string tag, int page, int dpi, string size, List<Bitmap>? keep = null)
    {
        var w = Open(page: page, dpi: dpi);
        SettingsSheet.Pump(120);
        string name = $"welcome-{page}-{tag}{size}";
        Inspect(name, w, dpi);
        // The away page has nothing to scroll, but at 200% on a screen 1080 pixels tall, which leaves
        // too little room for any page. At 150% it is sized again first, as in AwayAt1080p150.
        if (page == 4 && dpi == 96) NoScroll(name, w);
        if (pictures)
        {
            var picture = SettingsSheet.Grab(w.Handle);
            Save(picture, name);
            if (keep is not null) keep.Add(picture);
            else picture.Dispose();
            ScrollableControl body = w.Pinned ? Get<WelcomeBody>(w, "body") : Get<WelcomeFrame>(w, "frame");
            if (body.VerticalScroll.Visible)
            {
                body.AutoScrollPosition = new Point(0, body.DisplayRectangle.Height);
                SettingsSheet.Pump(80);
                Inspect(name + "-end", w, dpi);
                using var end = SettingsSheet.Grab(w.Handle);
                Save(end, name + "-end");
            }
        }
        if (page == 4 && dpi == 144)
        {
            w.Fit();
            w.PerformLayout();
            SettingsSheet.Pump(80);
            NoScroll(name + " sized as it opens at 150%", w);
        }
        Close(w, name);
    }

    // ---- States ----

    static void States(string tag)
    {
        // Locks picked on the timeline, the shortcut on, and a time of one's own.
        State(tag, "locked", new Config { IdleMinutes = 45, LockShortcut = "Ctrl+Alt+L", MuteWhenAway = true }, w =>
        {
            w.GoTo(2);
            w.Timeline.Choose(w.Timeline.Stops[(int)Moment.Locked]);
        });
        // Try it: the countdown note on the drawn PC.
        State(tag, "try", null, w =>
        {
            w.GoTo(2);
            typeof(WelcomeForm).GetMethod("TryIt", Private)!.Invoke(w, null);
        });
        // No warning, and another shortcut.
        State(tag, "nowarning", new Config { GraceSeconds = 0, LockShortcut = "Ctrl+Shift+F12" }, w => w.GoTo(2));
        // Extras for music and full screen, on battery 50% sooner, Away picked.
        State(tag, "battery", new Config { MusicVideo = Effect.Extra(30), FullScreen = Effect.Extra(120), OnBattery = Effect.Sooner(50) }, w =>
        {
            w.GoTo(3);
            Tick(w, Situation.FullScreen);
            Tick(w, Situation.Battery);
            w.Timeline.Choose(w.Timeline.Stops[(int)Moment.Away]);
        });
        // Every extra at once, past the cap, back on show.
        State(tag, "long", new Config { IdleMinutes = 600, MusicVideo = Effect.Extra(600), OnCall = Effect.Extra(120), FullScreen = Effect.Extra(45) }, w =>
        {
            w.GoTo(3);
            Tick(w, Situation.Call);
            Tick(w, Situation.FullScreen);
            w.Timeline.Choose(w.Timeline.Stops[(int)Moment.Back]);
        });
        // Play, partway through.
        State(tag, "play", new Config { MusicVideo = Effect.Extra(30), ScreensOffWhenAway = true }, w =>
        {
            w.GoTo(3);
            typeof(WelcomeForm).GetMethod("TogglePlay", Private)!.Invoke(w, null);
            w.PlayTick();
            w.PlayTick();
        });
        // Break reminders after a time of one's own, which the Custom… pill says.
        State(tag, "breaks", new Config { BreakReminders = true, BreakMinutes = 50 }, w => w.GoTo(4));
        // A PC without a battery.
        var had = WelcomeForm.HasBattery;
        WelcomeForm.HasBattery = () => false;
        try
        {
            State(tag, "nobattery", null, w => w.GoTo(3));
        }
        finally
        {
            WelcomeForm.HasBattery = had;
        }
    }

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    // The mouse wheel, sent to Next, which holds the keyboard after a move to a page, scrolls the page
    // that has more under what shows, and back up again.
    static void Wheel(string tag)
    {
        const uint wheel = 0x020A;
        var w = Open(page: 3);
        var body = w.PageScroller;
        var next = Get<KitButton>(w, "next");
        bool scrolls = body.AutoScrollMinSize.Height > body.ClientSize.Height;
        Is(scrolls, $"welcome-wheel-{tag}: the third page has more than shows under the stage");
        SendMessage(next.Handle, wheel, (IntPtr)(-120 << 16), IntPtr.Zero);
        SettingsSheet.Pump(80);
        int down = body.AutoScrollPosition.Y;
        Is(down < 0, $"welcome-wheel-{tag}: a notch down on Next scrolls the page ({down})");
        SendMessage(next.Handle, wheel, (IntPtr)(120 << 16), IntPtr.Zero);
        SettingsSheet.Pump(80);
        Is(body.AutoScrollPosition.Y > down, $"welcome-wheel-{tag}: a notch up scrolls it back ({body.AutoScrollPosition.Y})");
        Close(w, $"welcome-wheel-{tag}");
    }

    static void State(string tag, string what, Config? config, Action<WelcomeForm> set)
    {
        var w = Open(config);
        set(w);
        SettingsSheet.Pump(120);
        string name = $"welcome-{what}-{tag}";
        Inspect(name, w, 96);
        if (pictures)
        {
            using var picture = SettingsSheet.Grab(w.Handle);
            Save(picture, name);
        }
        Close(w, name);
    }

    static void Tick(WelcomeForm w, Situation one)
    {
        var chips = w.Stage.Situations;
        if (!chips.IsChecked(one)) chips.Choose(chips.Chips.First(c => Equals(c.Choice, one)));
    }

    // ---- Checks ----

    static void Inspect(string name, WelcomeForm w, int dpi)
    {
        var drawn = SettingsSheet.Draw(w);
        var all = SettingsSheet.Inside(w, post: false).Where(SettingsSheet.Shown).ToList();
        bool rtl = w.RightToLeft == RightToLeft.Yes;

        var outside = all.Where(c => c.Parent is { } p && !SettingsSheet.Holds(p).Contains(c.Bounds)).ToList();
        Is(outside.Count == 0, $"{name}: every control is inside what holds it{Few(outside.Select(c => $"{Describe(c)} {c.Bounds} in {SettingsSheet.Holds(c.Parent!)}"))}");

        var cut = drawn.Distinct().Where(d => !SettingsSheet.TextFits(d.Text, d.Font, d.Box, d.Flags)).ToList();
        Is(cut.Count == 0, $"{name}: the {drawn.Distinct().Count()} pieces of hand-drawn text fit their boxes{Few(cut.Select(d => $"\"{d.Text}\" {SettingsSheet.Measure(d.Text, d.Font, d.Box, d.Flags)} in {d.Box.Size}"))}");

        var labels = all.OfType<Label>().Where(l => l.Text.Length > 0 && l.Width > 0).ToList();
        var shortLabels = labels.Where(l => SettingsSheet.Wrapped(l) > l.Height).ToList();
        Is(shortLabels.Count == 0, $"{name}: the {labels.Count} labels are tall enough for their words{Few(shortLabels.Select(l => $"\"{l.Text}\" needs {SettingsSheet.Wrapped(l)} has {l.Height}"))}");
        var narrowLabels = labels.Where(l => l.Text.Length > 0 && Shapes.Measure(l.Text.Split(' ').OrderByDescending(word => word.Length).First(), l.Font).Width > l.Width + 1).ToList();
        Is(narrowLabels.Count == 0, $"{name}: every label is wider than its longest word{Few(narrowLabels.Select(l => $"\"{l.Text}\" in {l.Width}"))}");

        var sized = all.Where(c => c is KitButton or Toggle or ChoiceChips or StepDots).ToList();
        var squeezed = sized.Where(c => Asks(c) is var want && (want.Width > c.Width || want.Height > c.Height)).ToList();
        Is(squeezed.Count == 0, $"{name}: the {sized.Count} buttons, pills and switches have the room they ask for{Few(squeezed.Select(c => $"{Describe(c)} wants {Asks(c)} has {c.Size}"))}");

        // No two controls side by side cover each other.
        var meet = new List<string>();
        foreach (var parent in all.Prepend(w).Where(c => c.HasChildren))
        {
            var kids = parent.Controls.Cast<Control>().Where(SettingsSheet.Shown).Where(c => c.Width > 0 && c.Height > 0).ToList();
            for (int i = 0; i < kids.Count; i++)
                for (int j = i + 1; j < kids.Count; j++)
                    if (kids[i].Bounds.IntersectsWith(kids[j].Bounds)) meet.Add($"{Describe(kids[i])} and {Describe(kids[j])}");
        }
        Is(meet.Count == 0, $"{name}: no two controls cover each other{Few(meet)}");

        var reachable = all.Where(c => c.CanSelect || c is Part).ToList();
        var unnamed = reachable.Where(c => string.IsNullOrWhiteSpace(c.AccessibilityObject.Name)).ToList();
        Is(unnamed.Count == 0, $"{name}: the {reachable.Count} controls a key reaches all have a name{Few(unnamed.Select(Describe))}");

        var stops = SettingsSheet.TabStops(w);
        var backwards = new List<string>();
        for (int i = 1; i < stops.Count; i++)
        {
            var a = SettingsSheet.Seen(stops[i - 1]);
            var b = SettingsSheet.Seen(stops[i]);
            bool below = b.Top >= a.Bottom - 2;
            bool along = b.Top < a.Bottom && a.Top < b.Bottom && (rtl ? b.Right <= a.Left + 2 : b.Left >= a.Right - 2);
            bool inside = a.Contains(b) || b.Contains(a);
            // The buttons at the bottom come after the page, however far it scrolls under them, and
            // the page after the stage and the timeline held above it.
            bool footer = stops[i].Parent is WelcomeFooter && stops[i - 1].Parent is not WelcomeFooter;
            footer |= w.Pinned && In<WelcomeHead>(stops[i - 1]) && !In<WelcomeHead>(stops[i]);
            if (!below && !along && !inside && !footer) backwards.Add($"{Describe(stops[i - 1])} then {Describe(stops[i])}");
        }
        Is(backwards.Count == 0 && stops.Count > 0, $"{name}: Tab visits the {stops.Count} stops in reading order{Few(backwards)}");
        Is(w.AcceptButton is Control main && stops.Contains(main), $"{name}: Enter's button is one of the stops");

        var groups = all.OfType<PartGroup>().Where(g => g.Parts.Count(p => p.Visible) > 1).ToList();
        var misordered = new List<string>();
        foreach (var group in groups)
        {
            var seen = group.Parts.Where(p => p.Visible).Select(p => SettingsSheet.Seen(p)).ToList();
            for (int i = 1; i < seen.Count; i++)
            {
                bool sameLine = seen[i].Top < seen[i - 1].Bottom && seen[i - 1].Top < seen[i].Bottom;
                bool next = sameLine ? (!rtl ? seen[i].Left >= seen[i - 1].Right - 1 : seen[i].Right <= seen[i - 1].Left + 1) : seen[i].Top >= seen[i - 1].Bottom - 1;
                if (!next && group is not WelcomeTimeline) misordered.Add($"{Describe(group)} part {i}");
            }
        }
        Is(misordered.Count == 0, $"{name}: the parts of {groups.Count} groups are in reading order{Few(misordered)}");

        // The timeline's stops run along it in time, and its names do not meet.
        if (SettingsSheet.Shown(w.Timeline))
        {
            var stopsSeen = w.Timeline.Stops.Where(s => s.Visible).Select(s => SettingsSheet.Seen(s)).ToList();
            bool inTime = stopsSeen.Zip(stopsSeen.Skip(1)).All(p => rtl ? p.Second.Right <= p.First.Left : p.Second.Left >= p.First.Right);
            Is(inTime, $"{name}: the timeline's stops run {(rtl ? "right to left" : "left to right")} without meeting");
            var names = w.Timeline.Names.Select(n => n.Box).ToList();
            var touching = new List<string>();
            for (int i = 0; i < names.Count; i++)
                for (int j = i + 1; j < names.Count; j++)
                    if (names[i].IntersectsWith(names[j])) touching.Add($"{w.Timeline.Names[i].Moment} and {w.Timeline.Names[j].Moment}");
            var outsideNames = names.Where(n => !w.Timeline.ClientRectangle.Contains(n)).ToList();
            Is(touching.Count == 0 && outsideNames.Count == 0, $"{name}: the timeline's {names.Count} names are inside it and do not meet{Few(touching)}");
        }

        // The words beside the drawn PC keep off it.
        if (SettingsSheet.Shown(w.Stage))
        {
            int start = w.Stage.LogicalToDeviceUnits(320);
            var onPicture = w.Stage.Controls.Cast<Control>().Where(SettingsSheet.Shown)
                .Where(c => rtl && !w.Stage.IsMirrored ? c.Right > w.Stage.Width - start : c.Left < start).ToList();
            Is(onPicture.Count == 0, $"{name}: the words beside the PC keep off it{Few(onPicture.Select(Describe))}");
        }

        var area = WorkArea.Of(Screen.FromControl(w));
        Is(w.Height <= area.Height, $"{name}: the window fits the screen's height ({w.Height} in {area.Height})");

        var off = all.Where(c => c.DeviceDpi != dpi).ToList();
        Is(w.DeviceDpi == dpi && off.Count == 0, $"{name}: the window and its {all.Count} controls are at {dpi * 100 / 96}%{Few(off.Select(c => $"{Describe(c)} at {c.DeviceDpi}"))}");

        // Only the accent and the theme's own greys: no orange or warning color drawn as text.
        var t = Theme.Current;
        var allowed = new[] { t.Text, t.Subtle, t.Accent, t.OnAccent };
        var colored = all.OfType<Label>().Where(l => l.Text.Length > 0 && !allowed.Contains(l.ForeColor)).ToList();
        Is(colored.Count == 0, $"{name}: words are in the theme's text colors or the accent{Few(colored.Select(l => $"\"{l.Text}\" {l.ForeColor}"))}");
    }

    static bool In<T>(Control c) where T : Control
    {
        for (var at = c.Parent; at is not null; at = at.Parent) if (at is T) return true;
        return false;
    }

    static Size Asks(Control c) => c is ChoiceChips chips
        ? new Size(0, chips.GetPreferredSize(new Size(c.Width, 0)).Height)
        : c.GetPreferredSize(Size.Empty);

    static string Describe(Control c) => SettingsSheet.Describe(c);

    static string Few(IEnumerable<string> items) => SettingsSheet.Few(items);

    static T Get<T>(object o, string field) => (T)o.GetType().GetField(field, Private)!.GetValue(o)!;

    // ---- Pictures ----

    static void Save(Bitmap picture, string name)
    {
        picture.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
        Console.WriteLine($"{name}: {picture.Width}x{picture.Height}");
    }

    // The five pages side by side, each as tall as it is, on a grey ground.
    static void Sheet(List<Bitmap> pages)
    {
        const int gap = 24;
        int width = pages.Sum(p => p.Width) + gap * (pages.Count + 1);
        int height = pages.Max(p => p.Height) + 2 * gap;
        using var sheet = new Bitmap(width, height);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(0xDD, 0xE1, 0xE4));
            int x = gap;
            foreach (var page in pages)
            {
                g.DrawImage(page, x, gap, page.Width, page.Height);
                x += page.Width + gap;
            }
        }
        Save(sheet, "welcome-pages");
        foreach (var page in pages) page.Dispose();
    }
}
