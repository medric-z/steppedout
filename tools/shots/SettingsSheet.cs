using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using SteppedOut;

// The settings window: the status box and the seven main rows for each of the sketch's four cases
// (nothing going on, music and full screen, a call, on battery), the states the sketch does not
// draw, each tab, Large text, 150%, and a rebuild in the other language; and the checks that
// nothing in it is cut off, unnamed, out of order or out of place.
// The app is a stand-in that writes down what it is asked. Pictures are named
// settings-<what>-<language>-<theme>.png; settings-main-* is the part the sketch's
// ref/settings-main-* shows. The checks go to settings-check.txt.
static class SettingsSheet
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

    static string outDir = "";
    static bool pictures;
    static bool firstRun = true;
    static int failures;
    static TextWriter? report;

    public static void Run(string outDir, string lang, string theme)
    {
        SettingsSheet.outDir = outDir;
        pictures = true;
        string file = Path.Combine(outDir, "settings-check.txt");
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
        Console.WriteLine(failures == before ? "settings: all checks passed" : $"settings: {failures - before} checks failed");
    }

    // The same windows and checks without pictures, for the window checks: both languages, light.
    // Returns how many checks failed.
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
        Console.WriteLine(failures == 0 ? "settings: all checks passed" : $"settings: {failures} checks failed");
        return failures;
    }

    static void Is(bool ok, string what)
    {
        string line = (ok ? "ok   " : "FAIL ") + what;
        Console.WriteLine(line);
        report?.WriteLine(line);
        if (!ok) failures++;
    }

    static void Note(string what)
    {
        Console.WriteLine("     " + what);
        report?.WriteLine("     " + what);
    }

    // ---- The app ----

    // What the window asks of the app, written down. The language is taken as the tray takes it;
    // the colors stay as the render tool set them.
    sealed class Host : ISettingsHost
    {
        public readonly List<string> Calls = new();
        public readonly List<Config> Edited = new();
        public bool StartsWithWindows { get; set; } = true;
        public AutostartState StartWithWindowsState => StartsWithWindows ? AutostartState.On : AutostartState.Off;
        public event Action? StartWithWindowsChanged
        {
            add { }
            remove { }
        }
        public LockStatus Status { get; set; } = LockStatus.Starting(30);

        public void PauseChosen(TimeSpan? length) => Calls.Add("pause");
        public void ResumeChosen() => Calls.Add("resume");
        public void LockNowChosen() => Calls.Add("lock");
        public void TryItChosen() => Calls.Add("try");
        public void ShortcutChanged(string shortcut) => Calls.Add("shortcut");

        public void LanguageChanged(string language)
        {
            Calls.Add("language " + language);
            Lang.Use(language);
        }

        public HotKeyResult ShortcutResult => HotKeyResult.Ok;

        public event Action? ShortcutResultChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<WhyLine> Explain() => [];

        public void ConfigEdited(Config config)
        {
            Calls.Add("edited");
            Edited.Add(config);
        }

        public void StartWithWindowsChosen(bool on)
        {
            Calls.Add("start with windows");
            StartsWithWindows = on;
        }

        public event Action? StatusChanged;

        public bool SaveFailed { get; set; }

        public void Tell(LockStatus status)
        {
            Status = status;
            StatusChanged?.Invoke();
        }
    }

    // The sketch's settings: 30 minutes, +1 hour for music or video, never on a call, +2 hours for
    // full screen, a 30 second warning, music paused until you're back, and Start with Windows on.
    static Config Sample() => new()
    {
        IdleMinutes = 30,
        MusicVideo = Effect.Extra(60),
        OnCall = Effect.Never,
        FullScreen = Effect.Extra(120),
        GraceSeconds = 30,
        MediaOnLock = MediaOnLock.PauseUntilBack,
        OnBattery = Effect.Sooner(50),
    };

    // Two minutes idle, 3 locks and 1 hr 20 min away today, as the sketch shows.
    static LockStatus Sim(params ActiveEffect[] effects)
    {
        var time = LockTime.Compute(30, effects);
        return new LockStatus
        {
            Kind = time.NeverLocks ? LockState.Never : LockState.On,
            Time = time,
            IdleSeconds = 120,
            SecondsLeft = time.TotalSeconds is decimal total ? (double)total - 120 : 0,
            Today = new TodayCounts(3, TimeSpan.FromMinutes(80)),
        };
    }

    static readonly (string Name, Func<LockStatus> Status)[] Sims =
    [
        ("none", () => Sim()),
        ("media+fs", () => Sim(new ActiveEffect(Effect.Extra(60), EffectSource.MusicOrVideo), new ActiveEffect(Effect.Extra(120), EffectSource.FullScreen))),
        ("call", () => Sim(new ActiveEffect(Effect.Never, EffectSource.Call))),
        ("bat", () => Sim(new ActiveEffect(Effect.Sooner(50), EffectSource.Battery))),
    ];

    // Names of apps nobody chose, which the windows must never fill in by themselves.
    static readonly string[] Unasked = ["OBS", "Zoom", "WhatsApp", "Chrome", "Teams", "Discord", "Spotify", "Steam", "Slack", "VLC"];

    // ---- The scenes ----

    static void Each(string tag, bool dark)
    {
        foreach (var (name, status) in Sims) MainRows(tag, name, status());
        States(tag);
        Tabs(tag);
        Unlisted(tag);
        Large(tag, dark);
        foreach (float scale in new[] { 1.5f, 2f, 2.25f }) TextSize(tag, dark, scale);
        NotSaved(tag, dark);
        Dpi150(tag);
        Flip(tag, dark);
        Debounce(tag);
    }

    static (SettingsForm Form, Host Host) Open(Config config, LockStatus status)
    {
        var host = new Host { Status = status };
        var f = new SettingsForm(config, host);
        Shots.Place(f);
        f.Show();
        Pump(150);
        return (f, host);
    }

    static void Close(SettingsForm f, Host? host, string name)
    {
        if (host is not null)
        {
            Is(f.EditsWhileFilling == 0, $"{name}: no edits while the window filled itself ({f.EditsWhileFilling})");
            Is(host.Edited.Count == 0, $"{name}: nothing handed over without a choice ({host.Edited.Count})");
        }
        Shots.Foreground(f, name);
        f.Close();
        f.Dispose();
        Pump(30);
    }

    static void MainRows(string tag, string sim, LockStatus status)
    {
        var (f, host) = Open(Sample(), status);
        string name = $"settings-main-{sim}-{tag}";
        var drawn = Draw(f);
        Window(name, f, host);
        if (sim == "none") Inspect($"settings-window-{tag}", f, drawn, 96);
        var text = Lang.Current;
        Is(f.StatusBox.Headline == text.Headline(status), $"{name}: the headline is \"{f.StatusBox.Headline}\"");
        Is(f.StatusBox.WhyShown == (text.Why(status) != ""), $"{name}: the why line {(f.StatusBox.WhyShown ? "shows: " + f.StatusBox.Why : "is hidden")}");
        var squeezed = f.StatusBox.Controls.Cast<Control>().Where(c => c.Visible && Asks(c).Height > c.Height).ToList();
        Is(squeezed.Count == 0, $"{name}: everything in the status box is as tall as it asks{Few(squeezed.Select(c => $"{Describe(c)} {c.Bounds} wants {Asks(c)}"))}");
        StatusButtons(name, f);
        var lines = f.StatusBox.Controls.OfType<StatusBox.TextLine>().Where(t => t.Visible && t.Text.Length > 0 && t.Height > t.HeightFor(100000)).ToList();
        Is(lines.Count == 0, $"{name}: the headline, the sentence and the why line are one line each, as the sketch has them{Few(lines.Select(t => $"\"{t.Text}\" in {t.Width} px"))}");
        Note($"{name}: status box {f.StatusBox.Size}: " + string.Join(" ", f.StatusBox.Controls.Cast<Control>().Select(c => $"{c.GetType().Name}{c.Bounds}")));
        if (pictures)
        {
            using var window = Grab(f.Handle);
            Save(window, $"settings-window-{sim}-{tag}");
            using var main = Crop(f, window, f.StatusBox, MainStack(f));
            Save(main, name);
        }
        Close(f, host, name);
    }

    const string LongProgramName = "Quarterly Planning Workbook Editor";

    // The states the sketch does not draw, as the status box and the rows under it.
    static void States(string tag)
    {
        var plain = LockTime.Plain(30);
        var states = new (string Name, Config Config, LockStatus Status)[]
        {
            ("paused", Sample(), new LockStatus { Kind = LockState.Paused, Time = plain, PausedUntil = DateTime.Today.AddHours(15).AddMinutes(30), Today = new TodayCounts(3, TimeSpan.FromMinutes(80)) }),
            ("paused-forever", Sample(), new LockStatus { Kind = LockState.Paused, Time = plain, Today = new TodayCounts(3, TimeSpan.FromMinutes(80)) }),
            ("countdown", Sample(), new LockStatus { Kind = LockState.Countdown, Time = plain, IdleSeconds = 1791, SecondsLeft = 9, Today = new TodayCounts(3, TimeSpan.FromMinutes(80)) }),
            ("never-app", Sample(), Sim(new ActiveEffect(Effect.Never, EffectSource.ForApp("notepad")))),
            // A program with a long name: the headline stays one line, cut with "...", as the tray menu cuts it.
            ("never-longname", Sample(), Sim(new ActiveEffect(Effect.Never, EffectSource.ForApp(LongProgramName)))),
            ("lockingoff", Make(c => c.LockWhenAway = false), Sim() with { Kind = LockState.LockingOff }),
            ("dryrun", Sample(), Sim() with { DryRun = true }),
            ("away", Sample(), new LockStatus { Kind = LockState.Away, Time = plain, Today = new TodayCounts(4, TimeSpan.FromMinutes(81)) }),
            ("locked", Sample(), new LockStatus { Kind = LockState.SessionLocked, Time = plain, Today = new TodayCounts(4, TimeSpan.FromMinutes(81)) }),
            ("starting", Sample(), LockStatus.Starting(30)),
            // Ordinary counts for a long day, which in Arabic need more room than the sketch's.
            ("counts", Sample(), Sim() with { Today = new TodayCounts(12, TimeSpan.FromMinutes(320)) }),
        };
        var text = Lang.Current;
        int oneLine = 0;
        foreach (var (state, config, status) in states)
        {
            var (f, host) = Open(config, status);
            string name = $"settings-state-{state}-{tag}";
            var drawn = Draw(f);
            Fits(name, drawn);
            Is(f.StatusBox.Headline == text.Headline(status, config.LockWhenAway), $"{name}: the headline is \"{f.StatusBox.Headline}\"");
            bool paused = status.Kind == LockState.Paused;
            Is(f.StatusBox.PauseButton.Text == (paused ? text.V1.TurnBackOn : text.V1.Pause) && f.StatusBox.PauseButton.Chevron == !paused,
                $"{name}: the first button reads {f.StatusBox.PauseButton.Text}");
            StatusButtons(name, f);
            var lines = f.StatusBox.Controls.OfType<StatusBox.TextLine>().Where(t => t.Visible && t.Text.Length > 0).Select(t => t.Height / Math.Max(1, t.HeightFor(100000)));
            Note($"{name}: tone {f.StatusBox.Tone}, box {f.StatusBox.Height} px, lines {string.Join("/", lines)}");
            if (pictures)
            {
                using var window = Grab(f.Handle);
                using var part = Crop(f, window, f.StatusBox, f.StatusBox);
                Save(part, name);
            }
            var headline = f.StatusBox.Controls.OfType<StatusBox.TextLine>().First(t => t.Text == f.StatusBox.Headline);
            if (state == "never-app") oneLine = f.StatusBox.Height;
            if (state == "never-longname")
            {
                Is(headline.Height == headline.HeightFor(headline.Width) && headline.Cut && f.StatusBox.Height == oneLine,
                    $"{name}: the headline stays one line, cut with an ellipsis, the box as tall as with a short name ({f.StatusBox.Height} and {oneLine} px)");
                Is(headline.Tip == f.StatusBox.Headline && headline.AccessibilityObject.Name == f.StatusBox.Headline,
                    $"{name}: the whole headline is in its tip and read out by screen readers");
            }
            else Is(!headline.Cut && headline.Tip is null, $"{name}: the headline is whole, with no tip");
            if (state == "counts")
            {
                // As tall as the box with the sketch's own day: the counts never stack.
                var counts = f.StatusBox.Controls.OfType<StatusBox.Stat>().ToList();
                int longDay = f.StatusBox.Height;
                bool beside = counts.Count == 2 && counts[0].Top == counts[1].Top;
                string where = f.StatusBox.ButtonsUp ? "up on the headline's line" : "in the middle";
                host.Tell(Sim());
                Pump(50);
                Is(beside && longDay == f.StatusBox.Height,
                    $"{name}: 5 hr 20 min away and 12 locks stay side by side, the box as tall as for the sketch's day ({longDay} and {f.StatusBox.Height} px, buttons {where})");
            }
            Close(f, host, name);
        }

        // Turned back on from paused, and paused again, in the same window: the box keeps its size
        // as its words change, and the rows under it move with it.
        // Nothing in the box moves: Pause and Turn back on are one size, so the words keep their
        // width, the box its height and the rows their place.
        var (g, gh) = Open(Sample(), Sim());
        string told = $"settings-state-told-{tag}";
        var box = g.StatusBox;
        (int, Rectangle, Rectangle, Rectangle, int) Places() => (box.Height, box.PauseButton.Bounds, box.LockNowButton.Bounds,
            box.Controls.OfType<StatusBox.TextLine>().First(t => t.Text == box.Sentence).Bounds, MainStack(g).Top);
        var on = Places();
        foreach (var paused in new[] { new LockStatus { Kind = LockState.Paused, Time = plain, Today = new TodayCounts(3, TimeSpan.FromMinutes(80)) },
            new LockStatus { Kind = LockState.Paused, Time = plain, PausedUntil = DateTime.Today.AddHours(15).AddMinutes(30), Today = new TodayCounts(3, TimeSpan.FromMinutes(80)) } })
        {
            gh.Tell(paused);
            Pump(100);
            Is(box.PauseButton.Text == text.V1.TurnBackOn, $"{told}: Pause turns into Turn back on when the app says it paused");
            var now = Places();
            Is(now == on, $"{told}: the box, the buttons, the sentence and the rows stay where they were ({now} for {on})");
            StatusButtons(told, g);
        }
        gh.Tell(Sim());
        Pump(100);
        Is(box.PauseButton.Text == text.V1.Pause && Places() == on, $"{told}: and back, all in the same place ({Places()} for {on})");
        Is(MainStack(g).Top >= box.Bottom, $"{told}: the rows stay under the box");
        Close(g, gh, told);
    }

    // Each tab with its page, scrolled to the end of the window. The window keeps the height it
    // opened at, whatever each page's own height: only the scroll range follows the page.
    static void Tabs(string tag)
    {
        var (f, host) = Open(Sample(), Sim());
        int opened = f.ClientSize.Height;
        Is(opened == SettingsHeight(f), $"settings-tabs-{tag}: opens as tall as it shows on the first tab, up to most of the screen ({opened})");
        for (int i = 0; i < f.Tabs.Parts.Count; i++)
        {
            string name = $"settings-tab{i}-{tag}";
            if (i > 0) Choose(f.Tabs, i);
            Pump(100);
            Is(f.Tab == i && f.Tabs.Selected == i && f.PageControl is { Parent: not null }, $"{name}: the tab shows its page");
            var scroller = f.Scroller;
            int height = f.ContentHeight;
            scroller.AutoScrollPosition = new Point(0, height);
            Pump(80);
            Is(f.ClientSize.Height == opened, $"{name}: the window keeps its height ({f.ClientSize.Height} for {opened})");
            Scroll(name, f);
            var drawn = Draw(f);
            Fits(name, drawn);
            if (pictures)
            {
                using var window = Grab(f.Handle);
                Save(window, name);
            }
        }
        Close(f, host, "settings-tabs");

        // Opened again on the tab used last, Breaks here, with the strip showing it chosen.
        var last = Sample();
        last.SettingsTab = 3;
        var (again, againHost) = Open(last, Sim());
        Pump(100);
        Is(again.Tab == 3 && again.Tabs.Selected == 3 && again.PageControl is { Parent: not null }, $"settings-lasttab-{tag}: opens on the tab used last");
        if (pictures)
        {
            using var window = Grab(again.Handle);
            Save(window, $"settings-lasttab-{tag}");
        }
        Close(again, againHost, "settings-lasttab");
    }

    // Times that are none of the list's own show as themselves, never as Custom.
    static void Unlisted(string tag)
    {
        var config = Make(c =>
        {
            c.IdleMinutes = 2;
            c.MusicVideo = Effect.Extra(45);
            c.OnCall = Effect.Extra(90);
            c.FullScreen = Effect.Extra(240);
            c.GraceSeconds = 20;
        });
        var (f, host) = Open(config, Sim());
        string name = $"settings-unlisted-{tag}";
        var text = Lang.Current;
        var m = f.Main;
        var shown = new[] { m.LockAfter!.Text, m.MusicVideo!.Text, m.OnCall!.Text, m.FullScreen!.Text, m.Warning!.Text };
        var want = new[] { text.OptionDuration(2), text.ExtraLabel(45), text.ExtraLabel(90), text.ExtraLabel(240), text.SecondsOption(20) };
        Is(shown.SequenceEqual(want) && !shown.Contains(text.V1.Custom), $"{name}: the lists show {string.Join(", ", shown)}");
        var drawn = Draw(f);
        Fits(name, drawn);
        if (pictures)
        {
            using var window = Grab(f.Handle);
            using var main = Crop(f, window, f.StatusBox, MainStack(f));
            Save(main, name);
        }
        Close(f, host, name);
    }

    static void Large(string tag, bool dark)
    {
        Theme.UseForShots(dark, 1.2f);
        try
        {
            var (f, host) = Open(Sample(), Sim());
            string name = $"settings-window-{tag}-large";
            var drawn = Draw(f);
            Window(name, f, host);
            Inspect(name, f, drawn, 96);
            Is(Theme.Body(10f).SizeInPoints is > 11.99f and < 12.01f, $"{name}: Large text makes 10 pt text 12 pt");
            if (pictures)
            {
                using var window = Grab(f.Handle);
                Save(window, name);
            }
            Close(f, host, name);
        }
        finally
        {
            Theme.UseForShots(dark, 1f);
        }
    }

    // Windows' text size at 150%, 200% and 225% (Settings > Accessibility > Text size): every tab,
    // each checked as the window is, the bottom of its page in view.
    static void TextSize(string tag, bool dark, float scale)
    {
        Theme.UseForShots(dark, scale);
        string size = $"text{scale * 100:0}";
        try
        {
            var (f, host) = Open(Sample(), Sim());
            // The top first: the status box, the rows under it and the tabs.
            string top = $"settings-top-{tag}-{size}";
            Pump(100);
            var first = Draw(f);
            Inspect(top, f, first, 96);
            StatusButtons(top, f);
            Whole(top, f, scale);
            if (pictures)
            {
                using var window = Grab(f.Handle);
                Save(window, top);
                // And the tabs, scrolled into view.
                f.Scroller.AutoScrollPosition = new Point(0, Math.Max(0, f.Tabs.Top - f.Scroller.AutoScrollPosition.Y - f.LogicalToDeviceUnits(40)));
                Pump(80);
                using var scrolled = Grab(f.Handle);
                using var tabs = Crop(f, scrolled, f.Tabs, f.Tabs);
                Save(tabs, $"settings-tabs-{tag}-{size}");
                f.Scroller.AutoScrollPosition = Point.Empty;
                Pump(50);
            }
            for (int i = 0; i < f.Tabs.Parts.Count; i++)
            {
                string name = $"settings-tab{i}-{tag}-{size}";
                if (i > 0) Choose(f.Tabs, i);
                Pump(100);
                f.Scroller.AutoScrollPosition = new Point(0, f.ContentHeight);
                Pump(80);
                var drawn = Draw(f);
                Inspect(name, f, drawn, 96);
                if (pictures)
                {
                    using var window = Grab(f.Handle);
                    Save(window, name);
                }
            }
            Close(f, host, $"settings-{tag}-{size}");
        }
        finally
        {
            Theme.UseForShots(dark, 1f);
        }
    }

    // At every text size the status box shows its words and counts whole, the buttons under the
    // counts once the counts no longer fit beside them (Windows' 200% and 225%); a tab whose words end
    // in an ellipsis has all of them in its tip.
    static void Whole(string name, SettingsForm f, float scale)
    {
        var box = f.StatusBox;
        var headline = box.Controls.OfType<StatusBox.TextLine>().First(t => t.Text == box.Headline);
        var counts = box.Controls.OfType<StatusBox.Stat>().Where(c => c.Visible).ToList();
        var cut = counts.Where(c => c.Width < c.GetPreferredSize(Size.Empty).Width).ToList();
        Is(!headline.Cut && cut.Count == 0, $"{name}: the headline and the counts show whole{Few(cut.Select(Describe))}");
        Is(box.ButtonsDown == scale >= 2f, $"{name}: the buttons are {(box.ButtonsDown ? "under the counts" : "beside the words")}");
        var parts = new Control[] { headline, box.PauseButton, box.LockNowButton }.Concat(counts).ToList();
        var meet = new List<string>();
        for (int i = 0; i < parts.Count; i++)
            for (int j = i + 1; j < parts.Count; j++)
                if (parts[i].Bounds.IntersectsWith(parts[j].Bounds)) meet.Add($"{Describe(parts[i])} and {Describe(parts[j])}");
        Is(meet.Count == 0, $"{name}: the headline, the counts and the buttons do not meet{Few(meet)}");
        var tabs = f.Tabs;
        var untipped = Enumerable.Range(0, tabs.Parts.Count).Where(i => (tabs.TipOf(i) is null) == TabCut(tabs, i)).Select(i => tabs.Parts[i].Text).ToList();
        Is(untipped.Count == 0, $"{name}: a tab has a tip exactly when its words end in an ellipsis{Few(untipped)}");
    }

    static bool TabCut(TabStrip tabs, int i)
    {
        var drawn = new List<(string Text, Font Font, Rectangle Box, TextFormatFlags Flags)>();
        KitControl.TextDrawn = (c, text, font, box, flags) =>
        {
            if (c == tabs.Parts[i]) drawn.Add((text, font, box, flags));
        };
        try
        {
            var part = tabs.Parts[i];
            using var picture = new Bitmap(Math.Max(1, part.Width), Math.Max(1, part.Height));
            part.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
        }
        finally
        {
            KitControl.TextDrawn = null;
        }
        return drawn.Any(d => d.Flags.HasFlag(TextFormatFlags.WordEllipsis));
    }

    // A save Windows refused: the footer says so in place of "Changes are saved automatically.". At
    // 100% and 150% text it fits beside the version; at Windows' 200% and 225% the version goes under
    // it. Either way the two never meet and every word shows.
    static void NotSaved(string tag, bool dark)
    {
        foreach (float scale in new[] { 1f, 1.5f, 2f, 2.25f })
        {
            Theme.UseForShots(dark, scale);
            try
            {
                var host = new Host { Status = Sim(), SaveFailed = true };
                var f = new SettingsForm(Sample(), host);
                Shots.Place(f);
                f.Show();
                Pump(150);
                string name = $"settings-not-saved-{tag}{(scale == 1f ? "" : $"-text{scale * 100:0}")}";
                f.Scroller.AutoScrollPosition = new Point(0, f.ContentHeight);
                Pump(80);
                var footer = f.FooterLine!;
                Is(footer.Saved == Lang.Current.V1.SaveFailedFooter, $"{name}: the footer says the changes could not be saved");
                if (scale <= 1.5f) Is(footer.Fits, $"{name}: it fits beside the version in {footer.Width} px");
                host.SaveFailed = false;
                f.SaveStateChanged();
                Is(footer.Saved == Lang.Current.V1.AutoSaved, $"{name}: a save that works puts \"{Lang.Current.V1.AutoSaved}\" back");
                host.SaveFailed = true;
                f.SaveStateChanged();
                Pump(50);
                f.Scroller.AutoScrollPosition = new Point(0, f.ContentHeight);
                Is(footer.Top >= 0 && footer.Bottom <= f.Scroller.ClientSize.Height, $"{name}: scrolled to the end, the whole footer shows ({footer.Bounds} in {f.Scroller.ClientSize})");
                var lines = Draw(f).Where(d => d.Control == footer).Distinct().ToList();
                var whole = new Rectangle(Point.Empty, footer.Size);
                var outside = lines.Where(d => !whole.Contains(d.Box)).ToList();
                Is(lines.Count == 2, $"{name}: the footer draws its two lines ({lines.Count})");
                Is(outside.Count == 0, $"{name}: both lie inside the footer's {footer.Size}{Few(outside.Select(d => $"\"{d.Text}\" in {d.Box}"))}");
                Fits(name + " footer", lines);
                bool meet = lines.Count == 2 && lines[0].Box.IntersectsWith(lines[1].Box);
                Is(!meet, $"{name}: the saved line and the version do not meet{(meet ? Few(lines.Select(d => $"{d.Box}")) : "")}");
                if (pictures)
                {
                    using var window = Grab(f.Handle);
                    Save(window, name);
                }
                Close(f, host, name);
            }
            finally
            {
                Theme.UseForShots(dark, 1f);
            }
        }
    }

    static void Dpi150(string tag)
    {
        var (f, host) = Open(Sample(), Sim());
        string name = $"settings-window-{tag}-150";
        int normalColumn = f.ColumnWidth;
        Rescale(f, 144);
        Pump(200);
        f.Scroller.PerformLayout();
        f.Scroller.PerformLayout();
        Pump(100);
        var drawn = Draw(f);
        Window(name, f, host);
        Inspect(name, f, drawn, 144);
        Is(f.ColumnWidth == normalColumn * 3 / 2, $"{name}: the column is 1.5 times as wide ({f.ColumnWidth} for {normalColumn})");
        if (pictures)
        {
            using var window = Grab(f.Handle);
            Save(window, name);
        }
        Close(f, host, name);
    }

    // Built again in place: a language picked from the pills keeps the tab and the scroll and puts
    // the focus back on the pills; new colors keep the focus where it was.
    static void Flip(string tag, bool dark)
    {
        string original = Lang.Current.RightToLeft ? "ar" : "en";
        string other = original == "ar" ? "en" : "ar";
        var (f, host) = Open(Make(c => c.Language = original), Sim());
        string name = $"settings-flip-{tag}";
        // Short enough to scroll.
        f.ClientSize = new Size(f.ClientSize.Width, f.LogicalToDeviceUnits(560));
        Pump(80);
        Choose(f.Tabs, 1);
        Pump(80);
        f.Scroller.AutoScrollPosition = new Point(0, f.LogicalToDeviceUnits(300));
        Pump(80);
        int scroll = -f.Scroller.AutoScrollPosition.Y;
        Choose(f.Pills, original == "en" ? 1 : 0);
        Pump(300);
        Is(host.Calls.SequenceEqual(["edited", "language " + other]), $"{name}: the language goes out at once ({string.Join(", ", host.Calls)})");
        Is(f.Text == Lang.Current.V1.SettingsTitle && f.RightToLeftLayout == (other == "ar"), $"{name}: built again in the language picked");
        Is(f.Tab == 1, $"{name}: on the same tab ({f.Tab})");
        Is(SameScroll(f, scroll), $"{name}: at the same scroll ({-f.Scroller.AutoScrollPosition.Y} for {scroll})");
        Is(f.ActiveControl is Part part && f.Pills.Contains(part), $"{name}: the focus is on the pills ({f.ActiveControl?.GetType().Name} {f.ActiveControl?.AccessibleName})");
        var drawn = Draw(f);
        Inspect(name, f, drawn, 96);
        if (pictures)
        {
            using var window = Grab(f.Handle);
            Save(window, name);
        }

        // New colors, with the focus on the warning's list.
        f.Main.Warning!.Select();
        Theme.UseForShots(!dark);
        f.RebuildIfStale();
        Pump(300);
        Is(f.ActiveControl == f.Main.Warning, $"{name}-colors: the focus stays on the warning's list ({f.ActiveControl?.GetType().Name} {f.ActiveControl?.AccessibleName})");
        Is(f.Tab == 1 && SameScroll(f, scroll), $"{name}-colors: on the same tab, at the same scroll ({-f.Scroller.AutoScrollPosition.Y} for {scroll})");
        Is(f.BackColor == Theme.Current.Window, $"{name}-colors: in the new colors");
        Theme.UseForShots(dark);
        f.RebuildIfStale();
        Pump(200);
        Is(host.Edited.Count == 1 && f.EditsWhileFilling == 0, $"{name}: building again hands nothing more over ({host.Edited.Count})");
        f.Close();
        f.Dispose();
        Lang.Use(original);
    }

    // A few quick choices make one save, a moment after the last.
    static void Debounce(string tag)
    {
        var (f, host) = Open(Sample(), Sim());
        string name = $"settings-save-{tag}";
        var text = Lang.Current;
        Pick(f.Main.LockAfter!, text.OptionDuration(10));
        Pick(f.Main.Warning!, text.SecondsOption(60));
        Is(host.Edited.Count == 0, $"{name}: nothing goes out at once");
        Pump(SettingsForm.SaveDelay + 400);
        Is(host.Edited.Count == 1 && host.Edited[0].IdleMinutes == 10 && host.Edited[0].GraceSeconds == 60, $"{name}: one save with both changes ({host.Edited.Count})");
        Pick(f.Main.LockAfter!, text.OptionDuration(30));
        f.Close();
        Is(host.Edited.Count == 2 && host.Edited[1].IdleMinutes == 30, $"{name}: closing hands over the change still waiting ({host.Edited.Count})");
        f.Dispose();
    }

    // ---- Checks ----

    // Scrolled where it was, or as far as the window now goes when the other language is shorter.
    static bool SameScroll(SettingsForm f, int was)
    {
        int furthest = Math.Max(0, f.ContentHeight - f.Scroller.ClientSize.Height);
        return Math.Abs(-f.Scroller.AutoScrollPosition.Y - Math.Min(was, furthest)) <= 1;
    }

    // The window's size and the place of its parts.
    static void Window(string name, SettingsForm f, Host host)
    {
        int bar = SystemInformation.GetVerticalScrollBarWidthForDpi(f.DeviceDpi);
        Is(f.ClientSize.Width == f.LogicalToDeviceUnits(780) + bar, $"{name}: 780 px wide and a scroll bar ({f.ClientSize.Width})");
        Is(f.ColumnWidth == f.LogicalToDeviceUnits(740), $"{name}: the column is 740 px ({f.ColumnWidth})");
        var area = WorkArea.Of(Screen.FromControl(f));
        Is(f.ClientSize.Height <= area.Height * 9 / 10, $"{name}: no taller than most of the screen ({f.ClientSize.Height} of {area.Height})");
        Is(f.ClientSize.Height == SettingsHeight(f), $"{name}: as tall as what it shows, up to that ({f.ClientSize.Height}, content {f.ContentHeight})");
        var scroller = f.Scroller;
        var column = new[] { (Control)f.StatusBox, MainStack(f), f.Tabs, f.PageControl! };
        int left = (scroller.ClientSize.Width - f.ColumnWidth) / 2;
        Is(column.All(c => c.Width == f.ColumnWidth && Math.Abs(c.Left - left) <= 1), $"{name}: the status box, the rows, the tabs and the page share the column in the middle");
        var seenColumn = Seen(f.StatusBox);
        var pills = Seen(f.Pills);
        var client = f.RectangleToScreen(f.ClientRectangle);
        if (scroller.AutoScrollPosition.Y == 0)
        {
            Is(pills.Top == client.Top && Math.Abs(pills.Right - seenColumn.Right) <= 1, $"{name}: the language hangs from the top at the right ({pills} in {seenColumn})");
            int top = Seen(f.StatusBox).Top - client.Top;
            Is(top == Math.Max(f.LogicalToDeviceUnits(46), f.Pills.Height + f.LogicalToDeviceUnits(10)), $"{name}: the status box starts {top} px down");
        }
        Is(MainStack(f).Top - f.StatusBox.Bottom == f.LogicalToDeviceUnits(8), $"{name}: the rows start 8 px under the status box");
        Is(f.Tabs.Top - MainStack(f).Bottom == f.LogicalToDeviceUnits(14), $"{name}: the tabs start 14 px under the rows");
        Scroll(name, f);
        var words = Inside(f, post: false).SelectMany(c => new[] { c.Text, c.AccessibleName ?? "" }).ToList();
        var unasked = Unasked.Where(app => words.Any(w => w.Contains(app, StringComparison.OrdinalIgnoreCase))).ToList();
        Is(unasked.Count == 0, $"{name}: no app anybody did not pick{Few(unasked)}");
    }

    // The height OnLoad gives: what the window shows, up to most of the screen.
    static int SettingsHeight(SettingsForm f)
    {
        var area = WorkArea.Of(Screen.FromControl(f));
        return Math.Min(Math.Max(f.ContentHeight, f.LogicalToDeviceUnits(480)), area.Height * 9 / 10);
    }

    // What a control asks for. The status box gives its two buttons the sketch's size itself, as the
    // kit's KitButton has only the welcome's larger one, so theirs is what the box gives them;
    // StatusButtons checks that their words fit it.
    static Size Asks(Control c) => c is KitButton b && b.Parent is StatusBox box ? box.RoomFor(b) : c.GetPreferredSize(Size.Empty);

    // The status box's buttons and counts: the buttons the sketch's 32 px with their words inside
    // what KitButton draws them in (all but its 20 px chevron room), and the counts at least 12 px
    // from the buttons wherever they share a line.
    static void StatusButtons(string name, SettingsForm f)
    {
        var box = f.StatusBox;
        var buttons = new[] { box.PauseButton, box.LockNowButton };
        var cut = buttons.Where(b =>
        {
            var font = b.Primary ? Theme.Strong(10f, b.DeviceDpi) : Theme.Body(10f, b.DeviceDpi);
            var words = TextRenderer.MeasureText(b.Text, font);
            return words.Width > b.Width - (b.Chevron ? b.LogicalToDeviceUnits(20) : 0) - 2 || words.Height > b.Height - 2;
        }).ToList();
        Is(cut.Count == 0 && buttons.All(b => b.Height >= f.LogicalToDeviceUnits(32) && b.Size == box.RoomFor(b)),
            $"{name}: Pause and Lock now are {box.PauseButton.Size} and {box.LockNowButton.Size}, their words inside{Few(cut.Select(Describe))}");
        var counts = box.Controls.OfType<StatusBox.Stat>().Where(c => c.Visible).ToList();
        var close = new List<string>();
        foreach (var c in counts)
        {
            foreach (var b in buttons)
            {
                bool sameLine = c.Top < b.Bottom && b.Top < c.Bottom;
                int apart = Math.Max(c.Left - b.Right, b.Left - c.Right);
                if (sameLine && apart < f.LogicalToDeviceUnits(12)) close.Add($"{Describe(c)} {apart} px from {Describe(b)}");
            }
        }
        Is(close.Count == 0, $"{name}: the counts keep 12 px from the buttons{Few(close)}");
    }

    // The scroll bar against what the window holds: shown exactly when it does not fit, and reaching its end.
    static void Scroll(string name, SettingsForm f)
    {
        var page = f.Scroller;
        int total = f.ContentHeight;
        bool fits = total <= page.ClientSize.Height;
        bool ok = page.VerticalScroll.Visible != fits && (fits || page.VerticalScroll.Maximum + 1 == total);
        Is(ok, $"{name}: the scroll bar is {(page.VerticalScroll.Visible ? "shown" : "hidden")} for {total} px in {page.ClientSize.Height}{(page.VerticalScroll.Visible ? $" and reaches {page.VerticalScroll.Maximum + 1}" : "")}");
    }

    internal static List<(Control Control, string Text, Font Font, Rectangle Box, TextFormatFlags Flags)> Draw(Form f)
    {
        var drawn = new List<(Control, string, Font, Rectangle, TextFormatFlags)>();
        KitControl.TextDrawn = (c, text, font, box, flags) => drawn.Add((c, text, font, box, flags));
        try
        {
            f.Refresh();
            // A window off the screen is not painted by Refresh; printing it paints every control.
            using var printed = Grab(f.Handle);
        }
        finally
        {
            KitControl.TextDrawn = null;
        }
        return drawn;
    }

    static void Fits(string name, List<(Control Control, string Text, Font Font, Rectangle Box, TextFormatFlags Flags)> drawn)
    {
        var cut = drawn.Distinct().Where(d => !TextFits(d.Text, d.Font, d.Box, d.Flags)).ToList();
        Is(cut.Count == 0, $"{name}: the {drawn.Distinct().Count()} pieces of hand-drawn text fit their boxes{Few(cut.Select(d => $"\"{d.Text}\" {Measure(d.Text, d.Font, d.Box, d.Flags)} in {d.Box.Size}"))}");
    }

    // As the kit's sheet checks its controls, for the whole window.
    static void Inspect(string name, SettingsForm f, List<(Control Control, string Text, Font Font, Rectangle Box, TextFormatFlags Flags)> drawn, int dpi)
    {
        var all = Inside(f, post: false).Where(Shown).ToList();
        bool rtl = f.RightToLeft == RightToLeft.Yes;

        // Nothing sticks out of what holds it; what scrolls is held by the whole of its range.
        var outside = all.Where(c => c.Parent is { } p && !Holds(p).Contains(c.Bounds)).ToList();
        Is(outside.Count == 0, $"{name}: every control is inside what holds it{Few(outside.Select(c => $"{Describe(c)} {c.Bounds} in {Holds(c.Parent!)}"))}");

        Fits(name, drawn);

        var labels = all.OfType<Label>().Where(l => l.Text.Length > 0 && l.Width > 0).ToList();
        var shortLabels = labels.Where(l => Wrapped(l) > l.Height).ToList();
        Is(shortLabels.Count == 0, $"{name}: the {labels.Count} labels are tall enough for their words{Few(shortLabels.Select(l => $"\"{l.Text}\" needs {Wrapped(l)} has {l.Height}"))}");
        var sized = all.Where(c => c is KitButton or Segmented or LanguagePills or Toggle or DropDown).ToList();
        var squeezed = sized.Where(c => Asks(c) is var want && (want.Width > c.Width || want.Height > c.Height)).ToList();
        Is(squeezed.Count == 0, $"{name}: the {sized.Count} buttons, lists and switches have the room they ask for{Few(squeezed.Select(c => $"{Describe(c)} wants {Asks(c)} has {c.Size}"))}");

        var reachable = all.Where(c => c.CanSelect || c is Part).ToList();
        var unnamed = reachable.Where(c => string.IsNullOrWhiteSpace(c.AccessibilityObject.Name)).ToList();
        Is(unnamed.Count == 0, $"{name}: the {reachable.Count} controls a key reaches all have a name{Few(unnamed.Select(Describe))}");

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
        Is(backwards.Count == 0 && stops.Count > 0 && f.Pills.Contains(stops[0]), $"{name}: Tab visits the {stops.Count} stops in reading order, from the language{Few(backwards)}");

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
        Is(misordered.Count == 0, $"{name}: the parts of {groups.Count} groups are in order{(rtl ? " (tabs and languages left to right)" : "")}{Few(misordered)}");

        var rows = all.OfType<SettingRow>().ToList();
        var wrongSide = new List<string>();
        foreach (var row in rows)
        {
            var title = row.Controls.OfType<Label>().FirstOrDefault();
            var control = row.Controls.Cast<Control>().FirstOrDefault(c => c is not Label && c != row.Below);
            if (title is null || control is null) continue;
            var t = Seen(title);
            var c = Seen(control);
            if (rtl ? c.Right > t.Left + 1 : c.Left < t.Right - 1) wrongSide.Add($"\"{row.Title}\"");
        }
        Is(wrongSide.Count == 0, $"{name}: in the {rows.Count} rows the control is after the words{(rtl ? ", on the left" : ", on the right")}{Few(wrongSide)}");

        // Try it before the warning's list, from the start of the line.
        var tryIt = Seen(f.Main.TryIt!);
        var warning = Seen(f.Main.Warning!);
        Is(rtl ? tryIt.Left >= warning.Right : tryIt.Right <= warning.Left, $"{name}: Try it comes before the warning's list");

        var off = all.Where(c => c.DeviceDpi != dpi).ToList();
        Is(f.DeviceDpi == dpi && off.Count == 0, $"{name}: the window and its {all.Count} controls are at {dpi * 100 / 96}%{Few(off.Select(c => $"{Describe(c)} at {c.DeviceDpi}"))}");
    }

    internal static Rectangle Holds(Control parent) => parent is ScrollableControl { AutoScroll: true } scroller
        ? new Rectangle(scroller.AutoScrollPosition, new Size(scroller.ClientSize.Width, Math.Max(scroller.ClientSize.Height, scroller.DisplayRectangle.Height)))
        : parent.ClientRectangle;

    // ---- Helpers ----

    static Config Make(Action<Config> change)
    {
        var c = Sample();
        change(c);
        return c;
    }

    static Control MainStack(SettingsForm f) => f.Main.Rows[0].Parent!;

    static void Pick(DropDown list, string text)
    {
        using var menu = list.BuildMenu();
        menu.Items.OfType<ToolStripMenuItem>().First(item => item.Text == text).PerformClick();
    }

    static void Choose(PartGroup group, int part) => typeof(PartGroup).GetMethod("Choose", Any)!.Invoke(group, [group.Parts[part]]);

    internal static Rectangle Seen(Control c) => c.RectangleToScreen(c.ClientRectangle);

    internal static bool Shown(Control c)
    {
        for (var at = c; at is not null; at = at.Parent) if (!at.Visible) return false;
        return true;
    }

    internal static IEnumerable<Control> Inside(Control parent, bool post)
    {
        foreach (Control child in parent.Controls)
        {
            if (!post) yield return child;
            foreach (var deeper in Inside(child, post)) yield return deeper;
            if (post) yield return child;
        }
    }

    internal static List<Control> TabStops(Form f)
    {
        var stops = new List<Control>();
        Control? at = null;
        for (int i = 0; i < 500; i++)
        {
            at = f.GetNextControl(at, true);
            if (at is null) break;
            if (at.CanSelect && at.TabStop && Shown(at)) stops.Add(at);
        }
        return stops;
    }

    internal static Size Measure(string text, Font font, Rectangle box, TextFormatFlags flags) =>
        flags.HasFlag(TextFormatFlags.WordBreak)
            ? TextRenderer.MeasureText(text, font, new Size(Math.Max(1, box.Width), int.MaxValue), flags & ~(TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter))
            : TextRenderer.MeasureText(text, font, Size.Empty, (flags | TextFormatFlags.SingleLine) & ~(TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak));

    // Words that end in "..." when they do not fit, as the headline with a long program name, are cut on
    // purpose; the states check that one apart. Their height still has to fit.
    internal static bool TextFits(string text, Font font, Rectangle box, TextFormatFlags flags)
    {
        var size = Measure(text, font, box, flags);
        return size.Height <= box.Height + 1
            && (flags.HasFlag(TextFormatFlags.WordBreak) || flags.HasFlag(TextFormatFlags.EndEllipsis) || size.Width <= box.Width + 1);
    }

    internal static int Wrapped(Label l)
    {
        int width = Math.Max(1, l.ClientSize.Width - l.Padding.Horizontal);
        // A row's words, drawn and measured without Windows' end room.
        if (l is RowLabel { Tight: true } words) return words.HeightFor(width) + l.Padding.Vertical;
        int rows = Shapes.WrappedHeight(l.Text, l.Font, width);
        int box = TextRenderer.MeasureText(l.Text, l.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl).Height;
        return Math.Max(rows, box) + l.Padding.Vertical;
    }

    internal static string Describe(Control c) =>
        $"{c.GetType().Name}{(string.IsNullOrEmpty(c.AccessibleName) ? string.IsNullOrEmpty(c.Text) ? "" : $" \"{c.Text}\"" : $" \"{c.AccessibleName}\"")}";

    internal static string Few(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count == 0 ? "" : ": " + string.Join("; ", list.Take(4)) + (list.Count > 4 ? $"; and {list.Count - 4} more" : "");
    }

    internal static void Pump(int ms)
    {
        var until = DateTime.Now.AddMilliseconds(ms);
        do
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        while (DateTime.Now < until);
    }

    // ---- Pictures ----

    static void Save(Bitmap picture, string name)
    {
        picture.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
        Console.WriteLine($"{name}: {picture.Width}x{picture.Height}");
    }

    // The part of the window's picture from one control's top to another's bottom, across the column.
    static Bitmap Crop(SettingsForm f, Bitmap window, Control top, Control bottom)
    {
        GetWindowRect(f.Handle, out var w);
        if (DwmGetWindowAttribute(f.Handle, 9, out var r, Marshal.SizeOf<RECT>()) != 0) r = w;
        var a = Seen(top);
        var b = Seen(bottom);
        var box = Rectangle.FromLTRB(Math.Min(a.Left, b.Left) - r.L, a.Top - r.T, Math.Max(a.Right, b.Right) - r.L, b.Bottom - r.T);
        box.Intersect(new Rectangle(0, 0, window.Width, window.Height));
        return window.Clone(box, window.PixelFormat);
    }

    // The window as it shows, drawn by itself: PrintWindow, flipped back for a mirrored window, and
    // cropped to what DWM shows of it.
    internal static Bitmap Grab(IntPtr handle)
    {
        Pump(80);
        GetWindowRect(handle, out var w);
        if (DwmGetWindowAttribute(handle, 9, out var r, Marshal.SizeOf<RECT>()) != 0) r = w;
        var crop = new Rectangle(r.L, r.T, r.R - r.L, r.B - r.T);
        using var whole = new Bitmap(w.R - w.L, w.B - w.T);
        using (var g = Graphics.FromImage(whole))
        {
            for (int i = 0; i < 2; i++)
            {
                IntPtr dc = g.GetHdc();
                PrintWindow(handle, dc, 2);
                g.ReleaseHdc(dc);
                if (i == 0) Pump(80);
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

    // ---- 150% ----

    // A move to a monitor at another scaling, as WinForms does it: every control is told the new DPI
    // from the inside out, then the window gets WM_DPICHANGED.
    internal static void Rescale(Form f, int dpi)
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
}
