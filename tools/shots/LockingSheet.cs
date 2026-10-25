using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using SteppedOut;

// The Locking tab alone, under its tab, in a small window of its own, the way the settings window
// will hold it: empty as a new user sees it, with sample rules for Windows' own apps, with long app
// names, with values set by hand in the file (Don't lock and a shortcut of one's own among them), and
// its menus. Each is drawn at 100%, and checked at
// 150%, with Large text and with Windows' text size at 150% too. Pictures are named
// tab-locking-<what>-<language>-<theme>[-150|-large|-text150].png
// and the checks go to locking-check.txt. With KIT_REF set to the folder of the sketch's renders, compare/ gets each
// picture beside the sketch's Locking tab.
static class LockingSheet
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

    enum Size3 { Normal, Dpi150, LargeText, Text150 }

    static string Suffix(Size3 size) => size switch { Size3.Dpi150 => "-150", Size3.LargeText => "-large", Size3.Text150 => "-text150", _ => "" };

    static float TextScale(Size3 size) => size switch { Size3.LargeText => 1.2f, Size3.Text150 => 1.5f, _ => 1f };

    static string outDir = "";
    static bool pictures;
    static bool firstRun = true;
    static int failures;
    static TextWriter? report;

    public static void Run(string outDir, string lang, string theme)
    {
        LockingSheet.outDir = outDir;
        pictures = true;
        failures = 0;
        string file = Path.Combine(outDir, "locking-check.txt");
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
        Console.WriteLine(failures == 0 ? "locking: all checks passed" : $"locking: {failures} checks failed");
    }

    // The same pages and checks without pictures, for the window checks: both languages, light.
    // Returns how many checks failed.
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
        Console.WriteLine(failures == 0 ? "locking: all checks passed" : $"locking: {failures} checks failed");
        return failures;
    }

    static void Is(bool ok, string what)
    {
        string line = (ok ? "ok   " : "FAIL ") + what;
        Console.WriteLine(line);
        report?.WriteLine(line);
        if (!ok) failures++;
    }

    // ---- The settings each picture shows ----

    // What the page asks of the app: nothing is registered, and Why and the shortcut window never open
    // while drawing.
    sealed class NoApp : ISettingsHost
    {
        public HotKeyResult ShortcutResult => HotKeyResult.Ok;
#pragma warning disable CS0067
        public event Action? ShortcutResultChanged;
        public event Action? StatusChanged;
        public event Action? StartWithWindowsChanged;
#pragma warning restore CS0067
        public LockStatus Status => new() { Kind = LockState.On, Time = LockTime.Plain(15), SecondsLeft = 14 * 60 };
        public bool StartsWithWindows => false;
        public AutostartState StartWithWindowsState => AutostartState.Off;
        public IReadOnlyList<WhyLine> Explain() => [];
        public void PauseChosen(TimeSpan? length) => Is(false, "nothing pauses while drawing");
        public void ResumeChosen() => Is(false, "nothing turns back on while drawing");
        public void LockNowChosen() => Is(false, "nothing locks while drawing");
        public void TryItChosen() => Is(false, "nothing previews while drawing");
        public void ShortcutChanged(string shortcut) => Is(false, "no shortcut is registered while drawing");
        public void LanguageChanged(string language) => Is(false, "the language stays while drawing");
        public void ConfigEdited(Config config) => Is(false, "pages hand their edits to the window, not the app");
        public void StartWithWindowsChosen(bool on) => Is(false, "start with Windows stays while drawing");
    }

    // A new user's settings: nothing on battery, no rules.
    static Config Empty() => new();

    static string System32(string file) => Path.Combine(Environment.SystemDirectory, file);

    // Two rules on Windows' own apps, as the sketch's first and third: Notepad never locks while open
    // at all, Paint makes it lock 50% sooner while on screen; with the battery 50% sooner on a
    // 30-minute lock time and the game controller off, as the sketch draws them.
    static Config Sample() => new()
    {
        IdleMinutes = 30,
        OnBattery = Effect.Sooner(50),
        CountController = false,
        AppRules =
        [
            new AppRule("notepad", Effect.Never, AppWhen.Running, System32("notepad.exe")),
            new AppRule("mspaint", Effect.Sooner(50), AppWhen.OnScreen, System32("mspaint.exe")),
        ],
    };

    // Values none of the lists has, as set by hand in the file or typed with Custom…: each shows as
    // itself, in its own group. Paint here has no path, as a rule typed into the file. Locking is off,
    // with Lock and sleep kept for when it is on again, and the shortcut is one of the person's own.
    static Config HandSet()
    {
        var c = Sample();
        c.LockWhenAway = false;
        c.WhenLocking = WhenLocking.LockSleep;
        c.LockShortcut = "Ctrl+Shift+F9";
        c.OnBattery = Effect.Cap(45);
        c.AppRules =
        [
            new AppRule("notepad", Effect.Extra(90), AppWhen.Running, System32("notepad.exe")),
            new AppRule("mspaint", Effect.Sooner(40), AppWhen.OnScreen),
        ];
        return c;
    }

    // App names longer than the sketch's 128 px column, all Windows' own: the column grows for the
    // first three, up to a third of the line, and the longest is cut short with its whole name on hover.
    static Config LongNames() => new()
    {
        IdleMinutes = 30,
        AppRules =
        [
            new AppRule("Windows Media Player", Effect.Never, AppWhen.Running),
            new AppRule("Remote Desktop Connection", Effect.Extra(60), AppWhen.OnScreen),
            new AppRule("Windows Fax and Scan", Effect.Sooner(50), AppWhen.Running),
            new AppRule("Windows PowerShell Integrated Scripting Environment", Effect.Cap(5), AppWhen.OnScreen),
        ],
    };

    static readonly string[] Whole = ["Windows Media Player", "Remote Desktop Connection", "Windows Fax and Scan"];

    // ---- Making and checking the pictures ----

    static void Each(string tag)
    {
        bool rtl = Lang.Current.RightToLeft;
        foreach (var size in new[] { Size3.Normal, Size3.Dpi150, Size3.LargeText })
        {
            Page("empty", tag, size, mirrored: rtl, Empty(), EmptyChecks);
            Page("rules", tag, size, mirrored: rtl, Sample(), RuleChecks);
            Page("handset", tag, size, mirrored: rtl, HandSet(), HandSetChecks);
            Page("long", tag, size, mirrored: rtl, LongNames(), size == Size3.Normal ? LongChecks : CutChecks);
        }
        // Windows' text size at 150%: the lists keep their words and the app names give way.
        Page("rules", tag, Size3.Text150, mirrored: rtl, Sample(), RuleChecks);
        Page("long", tag, Size3.Text150, mirrored: rtl, LongNames(), CutChecks);
        // The same page in a window that does not mirror itself: each control flips its own drawing.
        if (rtl) Page("rules-unmirrored", tag, Size3.Normal, mirrored: false, Sample(), RuleChecks);
        // A PC without a battery: the battery's row is left out and the line moves down to the next row.
        WithoutBattery(() =>
        {
            Page("nobattery", tag, Size3.Normal, mirrored: rtl, Sample(), NoBatteryChecks);
            Page("nobattery", tag, Size3.Dpi150, mirrored: rtl, Sample(), NoBatteryChecks);
        });
        Menus(tag);
        Compare(tag, "empty");
        Compare(tag, "rules");
        Compare(tag, "long");
    }

    sealed class Host
    {
        public required Form Form { get; init; }
        public required TabStrip Tabs { get; init; }
        public required LockingTab Tab { get; init; }
        public required Control Page { get; init; }
        public Config Config { get; set; } = new();
        public int Edits { get; set; }

        public void Arrange()
        {
            int side = P(Form, 20);
            int width = Form.ClientSize.Width - side * 2;
            int y = P(Form, 14);
            int tabs = Tabs.GetPreferredSize(new Size(width, 0)).Height;
            Tabs.SetBounds(side, y, width, tabs);
            Page.SetBounds(side, y + tabs, width, Page.GetPreferredSize(new Size(width, 0)).Height);
            Page.PerformLayout();
            Form.ClientSize = new Size(Form.ClientSize.Width, Page.Bottom + P(Form, 15));
        }
    }

    static int P(Control c, int logical) => c.LogicalToDeviceUnits(logical);

    // The page in a window of its own, under its tab, as the settings window has it. Each edit is
    // applied to the settings and shown again, as the settings window does it.
    static Host Make(Config config, bool mirrored, Size3 size)
    {
        var f = new Form
        {
            AutoScaleDimensions = new SizeF(96F, 96F),
            AutoScaleMode = AutoScaleMode.Dpi,
            FormBorderStyle = FormBorderStyle.None,
            Font = Theme.Body(10f),
            RightToLeft = Lang.Direction,
            RightToLeftLayout = mirrored,
            Text = "locking",
        };
        Theme.Current.Dress(f);
        Shots.Place(f);
        var v = Lang.Current.V1;
        var tabs = new TabStrip()
            .Add(Glyphs.Lock, v.LockingTab, v.LockingTabDetail)
            .Add(Glyphs.Exchange, v.AwayTab, v.AwayTabDetail)
            .Add(Glyphs.Palette, v.AppearanceTab, v.AppearanceTabDetail)
            .Add(Glyphs.Coffee, v.BreaksTab, v.BreaksTabDetail)
            .Add(Glyphs.Sliders, v.AdvancedTab, v.AdvancedTabDetail);
        tabs.Selected = 0;
        var tab = new LockingTab(new NoApp());
        var host = new Host { Form = f, Tabs = tabs, Tab = tab, Page = tab.Build(config.Copy(), Lang.Current), Config = config.Copy() };
        tab.Edited += edit =>
        {
            host.Edits++;
            host.Config = edit(host.Config.Copy());
            tab.Refill(host.Config);
        };
        bool arranging = false;
        host.Page.SizeChanged += (_, _) =>
        {
            if (arranging) return;
            arranging = true;
            host.Arrange();
            arranging = false;
        };
        f.Controls.Add(tabs);
        f.Controls.Add(host.Page);
        f.ClientSize = new Size(P(f, 780), P(f, 600));
        host.Arrange();
        f.Show();
        Pump(150);
        // Again once shown: until then every control reads as hidden.
        host.Arrange();
        Pump(50);
        if (size == Size3.Dpi150)
        {
            Rescale(f, 144);
            f.ClientSize = new Size(P(f, 780), f.ClientSize.Height);
            host.Arrange();
            Pump(150);
        }
        return host;
    }

    static void Page(string what, string tag, Size3 size, bool mirrored, Config config, Action<string, Host>? checks)
    {
        string name = $"tab-locking-{what}-{tag}{Suffix(size)}";
        if (TextScale(size) != 1f) Theme.UseForShots(Theme.Current.IsDark, TextScale(size));
        try
        {
            var host = Make(config, mirrored, size);
            var f = host.Form;
            var drawn = new List<(Control Control, string Text, Font Font, Rectangle Box, TextFormatFlags Flags)>();
            KitControl.TextDrawn = (c, text, font, box, flags) => drawn.Add((c, text, font, box, flags));
            f.Refresh();
            var window = Grab(f.Handle);
            KitControl.TextDrawn = null;
            Shots.Foreground(f, name);
            Inspect(name, f, drawn, size);
            HeadChecks(name, host, drawn);
            if (size == Size3.LargeText)
            {
                float body = Theme.Body(10f).SizeInPoints;
                Is(Math.Abs(body - 12f) < 0.01f, $"{name}: Large text makes 10 pt text 12 pt ({body:0.##})");
            }
            if (pictures && (size == Size3.Normal || what != "handset"))
            {
                window.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
                Console.WriteLine($"{name}: {window.Width}x{window.Height}");
            }
            window.Dispose();
            checks?.Invoke(name, host);
            Shots.Foreground(f, name);
            f.Close();
            f.Dispose();
            Pump(50);
        }
        finally
        {
            if (TextScale(size) != 1f) Theme.UseForShots(Theme.Current.IsDark, 1f);
        }
    }

    // ---- What each page must show and do ----

    // The text check measures words, not icons: the page head's icon is checked here, so an icon
    // drawn into a box shorter than itself, or outside the head, fails the sheet.
    static void HeadChecks(string name, Host host, List<(Control Control, string Text, Font Font, Rectangle Box, TextFormatFlags Flags)> drawn)
    {
        var head = host.Tab.Head!;
        var icon = head.GlyphSize;
        var box = head.GlyphBox;
        Is(box.Height >= icon.Height && box.Width >= icon.Width && box.Top >= 0 && box.Bottom <= head.Height,
            $"{name}: the page head's icon has room for all of it ({box.Width}x{box.Height} at {box.Top} for an icon {icon.Width}x{icon.Height}, head {head.Height} tall)");
        var painted = drawn.Where(d => d.Control == head && d.Text == head.Glyph).Select(d => d.Box).Distinct().ToList();
        Is(painted.Count == 1 && painted[0] == box, $"{name}: the icon is painted in that box ({string.Join(", ", painted)})");
        int line = Shapes.Measure(head.Title, head.TitleFont).Height;
        int off = Math.Abs(box.Top + box.Height / 2 - (head.TitleBox.Top + line / 2));
        Is(off <= 1, $"{name}: the icon is level with the name's first line ({off} px off)");
    }

    static void WithoutBattery(Action run)
    {
        var had = WelcomeForm.HasBattery;
        WelcomeForm.HasBattery = () => false;
        try
        {
            run();
        }
        finally
        {
            WelcomeForm.HasBattery = had;
        }
    }

    static void NoBatteryChecks(string name, Host host)
    {
        var v = Lang.Current.V1;
        var tab = host.Tab;
        var rows = host.Page.Controls.OfType<SettingRow>().ToList();
        Is(tab.Battery is null && rows.All(r => r.Title != v.OnBattery), $"{name}: no battery, no battery row");
        var away = rows.FirstOrDefault(r => r.Title == v.AtLockTime);
        Is(away is not null && Shown(away) && ((FlatPanel)host.Page).HasLineAbove(away), $"{name}: the line that started the battery's row starts the next one");
        Is(away is not null && tab.Head!.Bottom < away.Top, $"{name}: the next row sits under the page head ({tab.Head!.Bottom} < {away?.Top})");
        Pick(tab.AtLockTime!, 3);
        Is(host.Edits == 1 && !host.Config.LockWhenAway && Equals(host.Config.OnBattery, Effect.Sooner(50)),
            $"{name}: Don't lock is a change on the page that keeps the battery's setting as it was ({host.Config.OnBattery})");
        Pick(tab.AtLockTime!, 0);
        Is(host.Edits == 2 && host.Config.LockWhenAway, $"{name}: Lock the PC turns locking on again");
    }

    static void Pick(DropDown list, int row)
    {
        using var menu = list.BuildMenu();
        menu.Items.OfType<ToolStripMenuItem>().ElementAt(row).PerformClick();
        Pump(30);
    }

    // At the lock time comes first after the battery, and the shortcut and Why close the tab, the
    // shortcut under a line, each with its control at the end of its row.
    static void OrderChecks(string name, Host host)
    {
        var v = Lang.Current.V1;
        var panel = (FlatPanel)host.Page;
        var rows = panel.Controls.OfType<SettingRow>().ToList();
        var titles = rows.Select(r => r.Title).Where(t => t != v.OnBattery).ToList();
        Is(titles.SequenceEqual([v.AtLockTime, v.CountController, v.Shortcut, v.WhyNotLocking]),
            $"{name}: At the lock time, the controller, then the shortcut and Why ({string.Join(" | ", titles)})");
        var shortcut = rows.FirstOrDefault(r => r.Title == v.Shortcut);
        var why = rows.FirstOrDefault(r => r.Title == v.WhyNotLocking);
        var rules = host.Tab.Rules!;
        Is(shortcut is not null && why is not null && Shown(shortcut) && Shown(why) && panel.HasLineAbove(shortcut) && !panel.HasLineAbove(why),
            $"{name}: the shortcut and Why show, the shortcut under a line");
        Is(shortcut is not null && why is not null && rules.Bottom <= shortcut.Top && shortcut.Bottom <= why.Top && why.Bottom <= panel.ClientSize.Height,
            $"{name}: under the app rules, inside the page ({rules.Bottom} {shortcut?.Top} {why?.Top} {why?.Bottom} {panel.ClientSize.Height})");
    }

    static void EmptyChecks(string name, Host host)
    {
        var v = Lang.Current.V1;
        var tab = host.Tab;
        var rules = tab.Rules!;
        Is(rules.Rows.Count == 0, $"{name}: no app rules by default");
        Is(Shown(rules.EmptyNote) && rules.EmptyNote.Text == v.NoAppsAdded, $"{name}: the empty list says \"{v.NoAppsAdded}\"");
        Is(!Shown(rules.OnScreenNote), $"{name}: the on-screen line waits for a rule");
        Is(Equals(tab.Battery!.Value, Effect.None) && tab.Battery.Text == v.LockAsUsual, $"{name}: on battery it locks as usual ({tab.Battery.Text})");
        Is(Equals(tab.AtLockTime!.Value, WhenLocking.Lock) && tab.AtLockTime.Text == v.LockOnly && tab.CountController!.Checked,
            $"{name}: Lock the PC and the game controller, as new users have them ({tab.AtLockTime.Text})");
        Is(tab.ShortcutList!.Text == v.Off, $"{name}: no shortcut until one is picked ({tab.ShortcutList.Text})");
        OrderChecks(name, host);
        Is(host.Edits == 0, $"{name}: showing the page made no edits");

        // Add app, with Windows' own Notepad picked: a rule that keeps the PC from locking, with
        // focus on its list, and the on-screen line under it.
        rules.Picker = (_, already, picked) => picked(new PickedApp("notepad", System32("notepad.exe"), null));
        rules.AddButton.Focus();
        Pump(50);
        rules.AddButton.PerformClick();
        Pump(150);
        Is(host.Config.AppRules is [{ App: "notepad", Effect.Kind: EffectKind.Never, When: AppWhen.Running }] && host.Edits == 1,
            $"{name}: Add app adds one rule, Never lock while open ({string.Join(", ", host.Config.AppRules)})");
        Is(rules.Rows.Count == 1 && rules.Rows[0].RuleList.Focused, $"{name}: the new rule's list has focus");
        Is(!Shown(rules.EmptyNote) && Shown(rules.OnScreenNote), $"{name}: the empty line goes and the on-screen line shows");
        Is(host.Page.Bottom + P(host.Form, 15) == host.Form.ClientSize.Height && host.Page.Height == host.Page.GetPreferredSize(new Size(host.Page.Width, 0)).Height,
            $"{name}: the page grew to hold the rule ({host.Page.Height})");
        // The × from the keyboard: the rule goes and focus goes back to Add app.
        var remove = (RuleRemove)rules.Rows[0].RemoveButton;
        remove.Focus();
        Pump(50);
        remove.Press();
        Pump(150);
        Is(host.Config.AppRules.Count == 0 && rules.Rows.Count == 0 && rules.AddButton.Focused, $"{name}: the × removes the rule and focus goes to Add app");
        Is(Shown(rules.EmptyNote), $"{name}: and the empty line is back");
    }

    static void RuleChecks(string name, Host host)
    {
        var t = Lang.Current;
        var v = t.V1;
        var tab = host.Tab;
        var rules = tab.Rules!;
        bool rtl = t.RightToLeft;
        Is(rules.Rows.Count == 2, $"{name}: two rules");
        Is(tab.Battery!.Text == v.Sooner50 + " " + t.PercentNote(15), $"{name}: the battery list says how soon 50% is ({tab.Battery.Text})");
        Is(rules.Rows[1].RuleList.Text == v.Sooner50 + " " + t.PercentNote(15), $"{name}: so does an app's ({rules.Rows[1].RuleList.Text})");
        Is(rules.Rows.Select(r => r.WhenList.Text).SequenceEqual([v.OpenEvenInBackground, v.OnlyWhenOnScreen]), $"{name}: each rule says when it applies");
        Is(Shown(rules.OnScreenNote) && rules.OnScreenNote.Text == v.OnScreenExplained, $"{name}: the line under the rules says what on screen means");
        // In each line the name comes first, then Locking, Applies when and the ×: from the left, or
        // from the right in Arabic. The name has at least 72 px, the least it gives up to the lists.
        var wrong = new List<string>();
        foreach (var row in rules.Rows)
        {
            var parts = new[] { row.RuleList, row.WhenList, row.RemoveButton }.Select(Seen).ToList();
            var line = Seen(row);
            bool ordered = rtl
                ? parts[0].Right < line.Right - P(row, 72) && parts[1].Right <= parts[0].Left && parts[2].Right <= parts[1].Left
                : parts[0].Left > line.Left + P(row, 72) && parts[1].Left >= parts[0].Right && parts[2].Left >= parts[1].Right;
            if (!ordered) wrong.Add(row.AppName);
        }
        Is(wrong.Count == 0, $"{name}: in each line the app, its rule, when and the × follow each other{(rtl ? " from the right" : "")}{Few(wrong)}");
        Is(!File.Exists(System32("notepad.exe")) || rules.Rows[0].AppName != "notepad", $"{name}: the app shows the name it gives itself ({rules.Rows[0].AppName})");

        // The lock time changes elsewhere: the notes follow and nothing is edited.
        var longer = host.Config.Copy();
        longer.IdleMinutes = 60;
        host.Edits = 0;
        tab.Refill(longer);
        Pump(50);
        Is(host.Edits == 0, $"{name}: Refill raises no edits");
        Is(tab.Battery.Text == v.Sooner50 + " " + t.PercentNote(30) && rules.Rows[1].RuleList.Text == v.Sooner50 + " " + t.PercentNote(30),
            $"{name}: with a 60-minute lock time the notes say 30 min ({tab.Battery.Text})");
        var outside = Inside(host.Form).Where(c => c.Visible && c.Parent is { } p && !p.ClientRectangle.Contains(c.Bounds)).ToList();
        Is(outside.Count == 0, $"{name}: after the notes change nothing sticks out{Few(outside.Select(Describe))}");
    }

    static void HandSetChecks(string name, Host host)
    {
        var t = Lang.Current;
        var v = t.V1;
        var tab = host.Tab;
        var rules = tab.Rules!;
        Is(Equals(tab.Battery!.Value, Effect.Cap(45)) && tab.Battery.Text == t.AlwaysAfterLabel(45), $"{name}: \"always 45\" on battery shows as itself ({tab.Battery.Text})");
        Is(BeforeCustom(tab.Battery, v.FixedTimeHeading, t.AlwaysAfterLabel(45)), $"{name}: under Fixed time, before Custom…");
        Is(rules.Rows[0].RuleList.Text == t.ExtraLabel(90) && BeforeCustom(rules.Rows[0].RuleList, v.WaitLongerHeading, t.ExtraLabel(90)),
            $"{name}: \"later 90\" shows as itself under Wait longer ({rules.Rows[0].RuleList.Text})");
        Is(rules.Rows[1].RuleList.Text.StartsWith(t.SoonerLabel(40)) && Group(rules.Rows[1].RuleList, rules.Rows[1].RuleList.Text) == v.LockSoonerHeading,
            $"{name}: \"sooner 40\" shows as itself under Lock sooner ({rules.Rows[1].RuleList.Text})");
        Is(rules.Rows[1].AppName == "mspaint", $"{name}: an app with no path shows its own name ({rules.Rows[1].AppName})");
        Is(ReferenceEquals(tab.AtLockTime!.Value, LockingTab.DontLock) && tab.AtLockTime.Text == v.DontLock,
            $"{name}: locking off shows Don't lock, whatever comes after the lock ({tab.AtLockTime.Text})");
        Is(tab.ShortcutList!.Text == SteppedOut.Shortcut.Parse("Ctrl+Shift+F9")!.Value.Display, $"{name}: a shortcut of one's own shows as itself ({tab.ShortcutList.Text})");
        OrderChecks(name, host);
        Is(host.Edits == 0, $"{name}: showing values set by hand made no edits");

        // Another value set by hand while the page is open: the list is made again with it.
        var next = host.Config.Copy();
        next.OnBattery = Effect.Sooner(40);
        tab.Refill(next);
        Pump(50);
        Is(host.Edits == 0 && Equals(tab.Battery!.Value, Effect.Sooner(40)) && Group(tab.Battery, tab.Battery.Text) == v.LockSoonerHeading,
            $"{name}: a refill with \"sooner 40\" shows it under Lock sooner and edits nothing ({tab.Battery.Text})");
    }

    static void LongChecks(string name, Host host)
    {
        var rules = host.Tab.Rules!;
        var row = rules.Rows[0];
        int column = rules.Grid(row, row.Width).NameW;
        Is(column > P(row, 128) && column <= row.Width / 3, $"{name}: the name column grows past 128 px, up to a third of the line ({column} of {row.Width})");
        var cutShort = rules.Rows.Where(r => Whole.Contains(r.AppName) && r.NameCut).Select(r => r.AppName).ToList();
        Is(cutShort.Count == 0, $"{name}: {string.Join(", ", Whole)} show whole{Few(cutShort)}");
        CutChecks(name, host);
    }

    // Each name cut short with "…" shows whole when the pointer rests on its line; no other line
    // has a hint.
    static void CutChecks(string name, Host host)
    {
        var rules = host.Tab.Rules!;
        var wrong = rules.Rows.Where(r => r.NameTip != (r.NameCut ? r.AppName : "")).Select(r => r.AppName).ToList();
        Is(wrong.Count == 0, $"{name}: the {rules.Rows.Count(r => r.NameCut)} names cut short show whole on hover, and only those{Few(wrong)}");
        Is(rules.Rows[^1].NameCut, $"{name}: the longest name is cut short ({rules.Rows[^1].AppName})");
    }

    // Whether the list holds this choice in that group, just before the group's Custom… row.
    static bool BeforeCustom(DropDown list, string heading, string label)
    {
        var rows = list.Rows.ToList();
        int at = rows.IndexOf(label);
        return at > 0 && Group(list, label) == heading && at + 1 < rows.Count && rows[at + 1] == Lang.Current.V1.Custom;
    }

    // The heading over a row of a list.
    static string? Group(DropDown list, string label)
    {
        var rows = list.Rows.ToList();
        int at = rows.IndexOf(label);
        var v = Lang.Current.V1;
        var headings = new[] { v.WaitLongerHeading, v.LockSoonerHeading, v.FixedTimeHeading };
        for (int i = at - 1; i >= 0; i--) if (headings.Contains(rows[i])) return rows[i];
        return null;
    }

    static bool Shown(Control c)
    {
        for (var at = c; at is not null; at = at.Parent) if (!at.Visible) return false;
        return c.Parent is not null && c.Width > 0 && c.Height > 0;
    }

    // ---- The menus ----

    // The battery's menu, an app's rule menu and its Applies when menu, drawn beside each other. A
    // menu is drawn without being shown, so its rounded corners, which Windows draws on screen,
    // are drawn here the same way.
    static void Menus(string tag)
    {
        var host = Make(Sample(), mirrored: Lang.Current.RightToLeft, Size3.Normal);
        var f = host.Form;
        var tab = host.Tab;
        var row = tab.Rules!.Rows[1];
        var menus = new[] { tab.AtLockTime!.BuildMenu(), tab.Battery!.BuildMenu(), row.RuleList.BuildMenu(), row.WhenList.BuildMenu() };
        var parts = menus.Select(m => DrawMenu(m, f)).ToList();
        foreach (var menu in menus) menu.Dispose();
        var v = Lang.Current.V1;
        string name = $"tab-locking-menus-{tag}";
        Is(tab.AtLockTime.Rows.SequenceEqual([v.LockOnly, v.LockScreenOff, v.LockSleep, v.DontLock]),
            $"{name}: At the lock time: Lock the PC, Lock and turn off the screen, Lock and sleep, Don't lock");
        Is(tab.Battery.Rows.SequenceEqual(new[] { v.LockAsUsual, v.LockSoonerHeading }.Concat(Sooners()).Append(v.FixedTimeHeading).Concat(Caps()).Append(v.Custom)),
            $"{name}: the battery menu: Lock as usual, Lock sooner with the times, Fixed time with Custom…");
        Is(row.RuleList.Rows.SequenceEqual(new[] { v.NeverLock, v.WaitLongerHeading, v.Extra15, v.Extra30, v.ExtraHour1, v.ExtraHours2, v.Custom, v.LockSoonerHeading }
            .Concat(Sooners()).Append(v.FixedTimeHeading).Concat(Caps()).Append(v.Custom)),
            $"{name}: an app's menu: Never lock, Wait longer with Custom…, Lock sooner, Fixed time with Custom…");
        Is(!row.RuleList.Rows.Contains(v.LockAsUsual) && !row.RuleList.Rows.Contains(v.NoExtraTime), $"{name}: an app's menu has no choice that does nothing");
        if (pictures)
        {
            using var sheet = Beside(parts, Theme.Current.Window, P(f, 16), Lang.Current.RightToLeft);
            sheet.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
            Console.WriteLine($"{name}: {sheet.Width}x{sheet.Height}");
        }
        foreach (var part in parts) part.Dispose();
        f.Close();
        f.Dispose();
        Pump(50);
    }

    static IEnumerable<string> Sooners()
    {
        var t = Lang.Current;
        return new[] { 25, 50, 75 }.Select(p => t.SoonerLabel(p) + " " + t.PercentNote(LockTime.Compute(30, [new ActiveEffect(Effect.Sooner(p), EffectSource.Battery)]).Total!.Value));
    }

    static IEnumerable<string> Caps()
    {
        var v = Lang.Current.V1;
        return [v.AlwaysAfter2, v.AlwaysAfter5, v.AlwaysAfter10, v.AlwaysAfter15, v.AlwaysAfter30, v.AlwaysAfterHour1];
    }

    internal static Bitmap DrawMenu(ContextMenuStrip menu, Form owner)
    {
        menu.RightToLeft = owner.RightToLeft;
        menu.PerformLayout();
        var preferred = menu.GetPreferredSize(Size.Empty);
        menu.Size = new Size(Math.Max(preferred.Width, menu.MinimumSize.Width), preferred.Height);
        menu.PerformLayout();
        using var flat = new Bitmap(menu.Width, menu.Height);
        menu.DrawToBitmap(flat, new Rectangle(0, 0, menu.Width, menu.Height));
        var shaped = new Bitmap(menu.Width, menu.Height);
        using (var g = Graphics.FromImage(shaped))
        {
            g.Clear(Theme.Current.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float radius = menu.LogicalToDeviceUnits(8);
            using var path = Shapes.Rounded(new RectangleF(0.5f, 0.5f, menu.Width - 1, menu.Height - 1), radius);
            using (var brush = new TextureBrush(flat)) g.FillPath(brush, path);
            using var pen = new Pen(Theme.Current.ControlEdge);
            g.DrawPath(pen, path);
        }
        return shaped;
    }

    internal static Bitmap Beside(List<Bitmap> parts, Color back, int gap, bool rtl)
    {
        int width = parts.Sum(p => p.Width) + gap * (parts.Count + 1);
        int height = parts.Max(p => p.Height) + gap * 2;
        var sheet = new Bitmap(width, height);
        using var g = Graphics.FromImage(sheet);
        g.Clear(back);
        int x = gap;
        foreach (var part in rtl ? Enumerable.Reverse(parts) : parts)
        {
            g.DrawImage(part, x, gap, part.Width, part.Height);
            x += part.Width + gap;
        }
        return sheet;
    }

    // ---- Beside the sketch ----

    // The sketch's Locking tab, cut from its settings window from just above the tabs to just below
    // the panel, beside the picture. KIT_REF names the folder of the sketch's renders.
    static void Compare(string tag, string what)
    {
        string? folder = Environment.GetEnvironmentVariable("KIT_REF");
        if (!pictures || string.IsNullOrEmpty(folder)) return;
        string sketch = Path.Combine(folder, $"settings-tab0-none-{tag}.png");
        string mine = Path.Combine(outDir, $"tab-locking-{what}-{tag}.png");
        if (!File.Exists(sketch) || !File.Exists(mine))
        {
            Console.WriteLine($"compare {what} {tag}: no sketch render to compare with");
            return;
        }
        Directory.CreateDirectory(Path.Combine(outDir, "compare"));
        using var whole = new Bitmap(sketch);
        // In the sketch's 1307 px window the tabs start at 696 and the panel ends at 1261.
        var cut = Rectangle.Intersect(new Rectangle(1, 682, whole.Width - 2, 1276 - 682), new Rectangle(0, 0, whole.Width, whole.Height));
        using var a = whole.Clone(cut, whole.PixelFormat);
        using var b = new Bitmap(mine);
        int gap = 24;
        using var both = new Bitmap(a.Width + b.Width + gap * 3, Math.Max(a.Height, b.Height) + gap * 2);
        using (var g = Graphics.FromImage(both))
        {
            g.Clear(Color.FromArgb(0x80, 0x80, 0x80));
            g.DrawImage(a, gap, gap, a.Width, a.Height);
            g.DrawImage(b, gap * 2 + a.Width, gap, b.Width, b.Height);
        }
        string file = $"tab-locking-{what}-vs-sketch-{tag}.png";
        both.Save(Path.Combine(outDir, "compare", file), ImageFormat.Png);
        Console.WriteLine($"compare/{file}");
    }

    // ---- The checks every page gets ----

    static void Inspect(string name, Form f, List<(Control Control, string Text, Font Font, Rectangle Box, TextFormatFlags Flags)> drawn, Size3 size)
    {
        var all = Inside(f).Where(c => c.Visible).ToList();
        bool rtl = f.RightToLeft == RightToLeft.Yes;

        // Nothing sticks out of what holds it, and nothing lies on top of something else.
        var outside = all.Where(c => c.Parent is { } p && !p.ClientRectangle.Contains(c.Bounds)).ToList();
        Is(outside.Count == 0, $"{name}: every control is inside what holds it{Few(outside.Select(c => $"{Describe(c)} {c.Bounds} in {c.Parent!.ClientSize}"))}");
        var overlapping = new List<string>();
        foreach (var parent in all.Append(f))
        {
            var kids = parent.Controls.Cast<Control>().Where(c => c.Visible && c.Width > 0 && c.Height > 0).ToList();
            for (int i = 0; i < kids.Count; i++)
                for (int j = i + 1; j < kids.Count; j++)
                    if (kids[i].Bounds.IntersectsWith(kids[j].Bounds)) overlapping.Add($"{Describe(kids[i])} and {Describe(kids[j])}");
        }
        Is(overlapping.Count == 0, $"{name}: no two controls overlap{Few(overlapping)}");

        // Text drawn by hand fits its box, and labels show their words whole.
        var cut = drawn.Distinct().Where(d => !Fits(d.Control, d.Text, d.Font, d.Box, d.Flags)).ToList();
        Is(cut.Count == 0, $"{name}: the {drawn.Distinct().Count()} pieces of hand-drawn text fit their boxes{Few(cut.Select(d => $"\"{d.Text}\" {Measure(d.Text, d.Font, d.Box, d.Flags)} in {d.Box.Size}"))}");
        var labels = all.OfType<Label>().Where(l => l.Text.Length > 0 && l.Width > 0).ToList();
        var shortLabels = labels.Where(l => Wrapped(l) > l.Height).ToList();
        Is(shortLabels.Count == 0, $"{name}: the {labels.Count} labels are tall enough for their words{Few(shortLabels.Select(l => $"\"{l.Text}\" needs {Wrapped(l)} has {l.Height}"))}");
        var sized = all.Where(c => c is KitButton or Toggle or DropDown).ToList();
        var squeezed = sized.Where(c => c.GetPreferredSize(Size.Empty) is var want && (want.Width > c.Width + 1 || want.Height > c.Height)).ToList();
        Is(squeezed.Count == 0, $"{name}: the {sized.Count} buttons, lists and switches have the room they ask for{Few(squeezed.Select(c => $"{Describe(c)} wants {c.GetPreferredSize(Size.Empty)} has {c.Size}"))}");

        // Every place Tab reaches has a name, and Tab goes in reading order.
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
        Is(backwards.Count == 0, $"{name}: Tab visits the {stops.Count} stops in reading order{Few(backwards)}");

        // Each row's control is at its end: the right, or the left in Arabic.
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

        if (size == Size3.Dpi150)
        {
            var other = all.Where(c => c.DeviceDpi != 144).ToList();
            Is(f.DeviceDpi == 144 && other.Count == 0, $"{name}: the window and its {all.Count} controls are at 150% (144 DPI){Few(other.Select(c => $"{Describe(c)} at {c.DeviceDpi}"))}");
        }
    }

    // A control where it shows on the screen, which in a mirrored window is not where its bounds say.
    static Rectangle Seen(Control c) => c.RectangleToScreen(c.ClientRectangle);

    static List<Control> TabStops(Form f)
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

    static IEnumerable<Control> Inside(Control parent, bool post = false)
    {
        foreach (Control child in parent.Controls)
        {
            if (!post) yield return child;
            foreach (var deeper in Inside(child, post)) yield return deeper;
            if (post) yield return child;
        }
    }

    static Size Measure(string text, Font font, Rectangle box, TextFormatFlags flags) =>
        flags.HasFlag(TextFormatFlags.WordBreak)
            ? TextRenderer.MeasureText(text, font, new Size(Math.Max(1, box.Width), int.MaxValue), flags & ~(TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter))
            : TextRenderer.MeasureText(text, font, Size.Empty, (flags | TextFormatFlags.SingleLine) & ~(TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak));

    static bool Fits(Control c, string text, Font font, Rectangle box, TextFormatFlags flags)
    {
        var size = Measure(text, font, box, flags);
        if (size.Height > box.Height + 1) return false;
        if (flags.HasFlag(TextFormatFlags.WordBreak) || size.Width <= box.Width + 1) return true;
        // Only an app's name may end in "…", and only when its whole name shows on hover.
        return c is RuleRow row && text == row.AppName && row.NameCut && row.NameTip == text;
    }

    // As a label breaks its words: the way the rows measure them, and the way Windows' own text box
    // style breaks them, whichever is taller.
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

    // ---- Drawing a window that is off the screen ----

    // The window as it shows, drawn by itself: PrintWindow, flipped back for a mirrored window.
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

    // A move to a monitor at another scaling, as WinForms does it: every control is told the new DPI
    // from the inside out, then the window gets WM_DPICHANGED. Sending the message alone leaves the
    // controls inside at the old DPI.
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
}
