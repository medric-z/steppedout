using System.Collections;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using SteppedOut;

// Sheets of the kit controls (rows, drop-downs, tabs, a few choices, language pills, chips, menus
// and the Custom… box), in every state, for comparing against the design sketch, and the checks
// that nothing in them is cut off, unnamed or out of order. Each sheet is drawn at 100%, at 150%
// and with Large text. Pictures are named kit-<what>-<language>-<theme>[-150|-large].png and the
// checks go to kit-check.txt. With KIT_REF set to the folder of the sketch's renders, compare/
// gets each sheet beside the sketch.
static class KitSheet
{
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, ref RECT lParam);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT r, int size);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    const int ExStyle = -20;
    const int LayoutRtlStyle = 0x00400000;
    const int WmDpiChanged = 0x02E0;
    const int WmChangeUiState = 0x0127;
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    enum Size3 { Normal, Dpi150, LargeText }

    static string Suffix(Size3 size) => size switch { Size3.Dpi150 => "-150", Size3.LargeText => "-large", _ => "" };

    static string outDir = "";
    static bool pictures;
    static bool firstRun = true;
    static int failures;
    static TextWriter? report;

    // The height of each sheet at 100%, to see that 150% and Large text make it taller.
    static readonly Dictionary<string, int> normalHeights = new();

    public static void Run(string outDir, string lang, string theme)
    {
        KitSheet.outDir = outDir;
        pictures = true;
        string file = Path.Combine(outDir, "kit-check.txt");
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
        Console.WriteLine(failures == 0 ? "kit: all checks passed" : $"kit: {failures} checks failed");
    }

    // The same sheets and checks without pictures, for the window checks: both languages, light,
    // at 100%, 150% and Large text. Returns how many checks failed.
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
        Console.WriteLine(failures == 0 ? "kit: all checks passed" : $"kit: {failures} checks failed");
        return failures;
    }

    static void Each(string tag)
    {
        bool rtl = Lang.Current.RightToLeft;
        foreach (var size in new[] { Size3.Normal, Size3.Dpi150, Size3.LargeText })
        {
            Sheet("rows", tag, size, rtl, Rows);
            Sheet("tabs", tag, size, rtl, TabsAndPage);
            Sheet("apps", tag, size, rtl, Apps);
            Sheet("states", tag, size, rtl, States);
            Sheet("menus", tag, size, rtl, Menus);
        }
        // The same controls in a window that does not mirror itself: each control flips its own drawing.
        if (rtl)
        {
            Sheet("rows-unmirrored", tag, Size3.Normal, false, Rows);
            Sheet("tabs-unmirrored", tag, Size3.Normal, false, TabsAndPage);
            Sheet("apps-unmirrored", tag, Size3.Normal, false, Apps);
        }
        Compare(tag);
    }

    // While the sheet is drawn only to fill the font cache for Large text, nothing is checked.
    static bool warming;

    static void Is(bool ok, string what)
    {
        if (warming) return;
        string line = (ok ? "ok   " : "FAIL ") + what;
        Console.WriteLine(line);
        report?.WriteLine(line);
        if (!ok) failures++;
    }

    // ---- The sheets ----

    // What a sheet builds: its controls, how to lay them out at the window's scale, and anything
    // drawn beside the window, such as menus and the Custom… box.
    sealed class Built
    {
        public required Action Arrange { get; init; }
        public Func<Form, Bitmap, Bitmap>? Finish { get; init; }

        // Checks that use the keyboard on the sheet once it is drawn, with real focus.
        public Action<string>? Keys { get; init; }
        public List<(Control Control, Action Show)> Focus { get; } = new();
    }

    static int P(Control c, int logical) => c.LogicalToDeviceUnits(logical);

    static DropDown List(object? value, params (string Text, object Value)[] choices)
    {
        var list = new DropDown();
        foreach (var (text, choice) in choices) list.Add(text, choice);
        if (value is not null) list.Value = value;
        return list;
    }

    static (string, object)[] Options(params string[] texts) => texts.Select(text => (text, (object)text)).ToArray();

    static Segmented Choices(string chosen, params string[] texts)
    {
        var few = new Segmented();
        foreach (var text in texts) few.Add(text, text);
        few.Value = chosen;
        return few;
    }

    static KitButton AddApp() => new(Lang.Current.V1.AddApp) { Small = true, Glyph = Glyphs.Plus };

    static ChipList Chips(params string[] names)
    {
        var chips = new ChipList();
        foreach (var name in names) chips.Add(name);
        return chips;
    }

    static TabStrip Tabs(int chosen)
    {
        var v = Lang.Current.V1;
        var tabs = new TabStrip()
            .Add(Glyphs.Lock, v.LockingTab, v.LockingTabDetail)
            .Add(Glyphs.Exchange, v.AwayTab, v.AwayTabDetail)
            .Add(Glyphs.Palette, v.AppearanceTab, v.AppearanceTabDetail)
            .Add(Glyphs.Coffee, v.BreaksTab, v.BreaksTabDetail)
            .Add(Glyphs.Sliders, v.AdvancedTab, v.AdvancedTabDetail);
        tabs.Selected = chosen;
        return tabs;
    }

    static LanguagePills Pills()
    {
        var v = Lang.Current.V1;
        var pills = new LanguagePills(v.Language).Add(v.LanguageEnglish, "en").Add(v.LanguageArabic, "ar");
        pills.Value = Lang.Current.RightToLeft ? "ar" : "en";
        return pills;
    }

    // Stacks controls down the window at its full width less 20 px each side; a gap of 0 joins one
    // to the one above. Returns the bottom.
    static int Stack(Form f, int top, IEnumerable<(Control Control, int Gap)> items)
    {
        int width = f.ClientSize.Width - P(f, 40);
        int y = top;
        foreach (var (control, gap) in items)
        {
            y += P(f, gap);
            int height = control.GetPreferredSize(new Size(width, 0)).Height;
            control.SetBounds(P(f, 20), y, width, height);
            control.PerformLayout();
            y += height;
        }
        return y;
    }

    // At the top right in both languages, hanging from the window's top edge.
    static void TopRight(Form f, Control control)
    {
        var size = control.GetPreferredSize(Size.Empty);
        int x = f.RightToLeftLayout ? P(f, 20) : f.ClientSize.Width - P(f, 20) - size.Width;
        control.SetBounds(x, 0, size.Width, size.Height);
    }

    // The settings window's main list as the sketch has it, in look C, under the language pills,
    // and a plain row below it for comparison.
    static Built Rows(Form f)
    {
        var t = Lang.Current;
        var v = t.V1;
        var pills = Pills();
        DropDown Wide(DropDown list)
        {
            list.LookC = true;
            list.ButtonWidth = 190;
            return list;
        }
        var lockAfter = Wide(List(30, (t.OptionDuration(5), 5), (t.OptionDuration(10), 10), (t.OptionDuration(15), 15), (t.OptionDuration(30), 30), (t.OptionDuration(60), 60)));
        lockAfter.AddLine().AddCustom(v.Custom, new CustomEntry { Name = v.LockAfter, Value = n => n, Label = t.OptionDuration });
        var rows = new List<SettingRow>
        {
            new(Glyphs.Clock, v.LockAfter, v.LockAfterDetail, lockAfter),
            new(Glyphs.Video, v.MusicVideo, v.MusicVideoDetail, Wide(List(v.ExtraHour1, Options(v.NoExtraTime, v.Extra15, v.Extra30, v.ExtraHour1, v.ExtraHours2)))),
            new(Glyphs.Microphone, v.OnCall, v.OnCallDetail, Wide(List(v.NeverLock, Options(v.NoExtraTime, v.ExtraHour1, v.NeverLock)))),
            new(Glyphs.Fullscreen, v.FullScreen, v.FullScreenDetail, Wide(List(v.ExtraHours2, Options(v.NoExtraTime, v.ExtraHour1, v.ExtraHours2, v.NeverLock)))),
            new(Glyphs.Bell, v.WarnMe, v.WarnMeDetail, Wide(List(v.Seconds30, Options(v.Off, v.Seconds10, v.Seconds15, v.Seconds30)))),
            new(Glyphs.Pause, v.MediaOnLock, v.MediaOnLockDetail, Wide(List(v.PauseUntilBack, Options(v.PauseUntilBack, v.PauseAndStayPaused, v.KeepPlaying)))),
            new(Glyphs.Power, v.StartWithWindows, v.StartWithWindowsDetail, new Toggle { Checked = true }),
        };
        foreach (var row in rows) row.Look = RowLook.LookC;
        var welcome = new SettingRow(Glyphs.Clock, v.LockAfter, null, List(15, (t.OptionDuration(15), 15), (t.OptionDuration(30), 30)));
        f.Controls.Add(pills);
        foreach (var row in rows) f.Controls.Add(row);
        f.Controls.Add(welcome);
        return new Built
        {
            Arrange = () =>
            {
                TopRight(f, pills);
                var items = new List<(Control, int)> { (rows[0], 0) };
                foreach (var row in rows.Skip(1)) items.Add((row, 4));
                items.Add((welcome, 20));
                int bottom = Stack(f, pills.Bottom + P(f, 12), items);
                f.ClientSize = new Size(f.ClientSize.Width, bottom + P(f, 20));
            },
        };
    }

    // The tabs with the Appearance page hanging from them, as the sketch has them.
    static Built TabsAndPage(Form f)
    {
        var v = Lang.Current.V1;
        var tabs = Tabs(2);
        var page = new FlatPanel { JoinedAbove = true };
        page.Add(new FlatRow(v.AppearanceTab, v.AppearanceIntro, glyph: Glyphs.Palette))
            .Add(new FlatRow(v.ThemeMode, v.ThemeModeDetail, Choices(v.System, v.Light, v.Dark, v.System)), lineAbove: true)
            .Add(new FlatRow(v.TextSize, v.TextSizeDetail, Choices(v.System, v.Small, v.Medium, v.Large, v.System)))
            .Add(new FlatRow(v.MinutesOnIcon, v.MinutesOnIconDetail, new Toggle()))
            .Add(new FlatRow(v.NotificationPlace, v.NotificationPlaceDetail, List(v.BottomRight, Options(v.BottomRight, v.BottomLeft, v.TopRight, v.TopLeft, v.Center)).Also(l => l.Small = true)));
        f.Controls.Add(tabs);
        f.Controls.Add(page);
        return new Built
        {
            Arrange = () =>
            {
                int bottom = Stack(f, P(f, 16), [(tabs, 0), (page, 0)]);
                f.ClientSize = new Size(f.ClientSize.Width, bottom + P(f, 20));
            },
        };
    }

    // The Away page's app lists: chips under a row's words, an empty list, and a row after a line.
    static Built Apps(Form f)
    {
        var v = Lang.Current.V1;
        var away = new FlatPanel();
        away.Add(new FlatRow(v.WhenAway, null, glyph: Glyphs.Exit))
            .Add(new FlatRow(v.OpenApps, v.OpenAppsAwayDetail, AddApp()) { Below = Chips("Notepad", "Paint", "Calculator", "Snipping Tool", "File Explorer") })
            .Add(new FlatRow(v.CloseApps, v.CloseAppsRowDetail, AddApp()) { Below = Chips() })
            .Add(new FlatRow(v.PlaySound, v.PlaySoundAwayDetail, List(v.Off, Options(v.Off, v.SoundSteppedOut, v.SoundDrift, v.SoundPebble, v.SoundGlow, v.SoundRipple)).Also(l => l.Small = true)), lineAbove: true)
            .Add(new FlatRow(v.RunScriptAway, v.NoScriptChosen, new KitButton(v.Choose) { Small = true }));
        f.Controls.Add(away);
        return new Built
        {
            Arrange = () =>
            {
                int bottom = Stack(f, P(f, 16), [(away, 0)]);
                f.ClientSize = new Size(f.ClientSize.Width, bottom + P(f, 20));
            },
        };
    }

    // The states: hovered, focused, chosen, small, empty, as one picture made of several takes, since
    // only one control has focus at a time.
    static Built States(Form f)
    {
        var t = Lang.Current;
        var v = t.V1;
        var lists = new List<DropDown>
        {
            List(30, (t.OptionDuration(15), 15), (t.OptionDuration(30), 30)),
            List(30, (t.OptionDuration(15), 15), (t.OptionDuration(30), 30)),
            List(v.TopLeft, Options(v.BottomRight, v.TopLeft)).Also(l => l.Small = true),
            List(v.Sooner50, Options(v.LockAsUsual, v.Sooner25, v.Sooner50, v.Sooner75)).Also(l => l.Small = true),
        };
        // A live note after a choice, as the battery rule shows how much sooner it comes.
        lists[3].Relabel(v.Sooner50, v.Sooner50 + " " + t.PercentNote(15));
        lists[0].AccessibleName = lists[1].AccessibleName = v.LockAfter;
        lists[2].AccessibleName = v.NotificationPlace;
        lists[3].AccessibleName = v.OnBattery;
        var listRow = new FlowRow(lists.Cast<Control>().ToArray());
        var buttons = new FlowRow(
            new KitButton(v.TryIt),
            new KitButton(v.TryIt) { Small = true },
            AddApp(),
            new KitButton(v.LockNow, primary: true) { Small = true },
            new KitButton(v.Choose) { Small = true });
        var tabs = Tabs(0);
        var few = Choices(v.Dark, v.Light, v.Dark, v.System);
        few.AccessibleName = v.ThemeMode;
        var sizes = Choices(v.Large, v.Small, v.Medium, v.Large, v.System);
        sizes.AccessibleName = v.TextSize;
        var pills = Pills();
        var chips = Chips("Notepad", "Paint", "Calculator");
        chips.AccessibleName = v.OpenApps;
        var empty = Chips();
        empty.AccessibleName = v.CloseApps;
        var choiceRow = new FlowRow(few, sizes, pills);
        foreach (Control c in new Control[] { listRow, buttons, tabs, choiceRow, chips, empty }) f.Controls.Add(c);

        Hover(lists[1]);
        Hover(buttons.Controls[0]);
        Hover(tabs.Parts[3]);
        Hover(sizes.Parts[0]);
        Hover(pills.Parts[pills.Value == "en" ? 1 : 0]);
        Hover(chips.Parts[1]);
        Hover(chips.Parts[2].Controls[0]);

        var built = new Built
        {
            Arrange = () =>
            {
                int bottom = Stack(f, P(f, 16), [(listRow, 0), (buttons, 14), (tabs, 14), (choiceRow, 14), (chips, 14), (empty, 14)]);
                f.ClientSize = new Size(f.ClientSize.Width, bottom + P(f, 20));
            },
            Keys = name =>
            {
                if (sizeNow == Size3.Normal) Narrator(name, tabs, chips);
                // The arrow keys move focus itself, not only where Tab will land.
                tabs.Parts[0].Focus();
                Pump(50);
                tabs.Parts[0].Key(System.Windows.Forms.Keys.Right);
                Pump(50);
                Is(tabs.Parts[1].Focused && tabs.Current == 1 && tabs.Selected == 0, $"{name}: Right moves focus to the next tab without choosing it");
                few.Parts[0].Focus();
                Pump(50);
                // The next choice is to the left in Arabic.
                bool rtl = f.RightToLeft == RightToLeft.Yes;
                few.Parts[0].Key(rtl ? System.Windows.Forms.Keys.Left : System.Windows.Forms.Keys.Right);
                Pump(50);
                Is(few.Parts[1].Focused && Equals(few.Value, v.Dark), $"{name}: {(rtl ? "Left" : "Right")} moves focus to the next choice and chooses it");
                // Delete takes a chip away and focus stays in the list: the next chip, then the one
                // before, then the list itself.
                chips.Parts[1].Focus();
                Pump(50);
                chips.Parts[1].Key(System.Windows.Forms.Keys.Delete);
                Pump(100);
                Is(chips.Parts.Count == 2 && chips.Parts[1].Focused && chips.Parts[1].Text == "Calculator", $"{name}: Delete on Paint leaves focus on Calculator, the chip after it");
                chips.Parts[1].Key(System.Windows.Forms.Keys.Back);
                Pump(100);
                Is(chips.Parts.Count == 1 && chips.Parts[0].Focused && chips.Parts[0].Text == "Notepad", $"{name}: Backspace on the last chip leaves focus on the one before it");
                chips.Parts[0].Key(System.Windows.Forms.Keys.Delete);
                Pump(100);
                Is(chips.Parts.Count == 0 && chips.Focused, $"{name}: removing the only chip leaves focus on the empty list");
                // With a place to go once empty, such as the row's Add app button, focus goes there.
                var addApp = buttons.Controls[2];
                chips.Add("Notepad");
                chips.FocusWhenEmpty = addApp;
                chips.Parts[0].Focus();
                Pump(50);
                chips.Parts[0].Key(System.Windows.Forms.Keys.Delete);
                Pump(100);
                Is(chips.Parts.Count == 0 && addApp.Focused && !chips.ContainsFocus, $"{name}: removing the only chip moves focus to FocusWhenEmpty, the Add app button");
                chips.FocusWhenEmpty = null;
            },
        };
        // One take for each focus ring; each is pasted over the picture where its control is.
        built.Focus.Add((lists[0], () => lists[0].Focus()));
        built.Focus.Add((buttons.Controls[2], () => buttons.Controls[2].Focus()));
        built.Focus.Add((tabs, () =>
        {
            tabs.Parts[0].Focus();
            tabs.Parts[0].Key(Keys.Right);
        }));
        built.Focus.Add((few, () => few.Parts[1].Focus()));
        built.Focus.Add((pills, () => pills.Parts[pills.Current].Focus()));
        built.Focus.Add((chips, () =>
        {
            chips.Parts[0].Focus();
            chips.Parts[0].Key(Keys.Right);
        }));
        built.Focus.Add((empty, () => empty.Focus()));
        return built;
    }

    // The menus the lists open, the pause menu, and the Custom… box, drawn beside each other. A menu
    // is drawn without being shown, so its rounded corners, which Windows draws on screen, are
    // drawn here the same way.
    static Built Menus(Form f)
    {
        var t = Lang.Current;
        var v = t.V1;
        var times = List(30, (t.OptionDuration(5), 5), (t.OptionDuration(15), 15), (t.OptionDuration(30), 30), (t.OptionDuration(60), 60));
        times.AddLine().AddCustom(v.Custom, new CustomEntry { Name = v.LockAfter, Value = n => n, Label = t.OptionDuration });
        times.ButtonWidth = 190;
        var battery = new DropDown { Small = true }
            .Add(v.LockAsUsual, 0)
            .AddHeading(v.LockSoonerHeading)
            .Add(v.Sooner25, 25).Add(v.Sooner50, 50).Add(v.Sooner75, 75)
            .AddHeading(v.FixedTimeHeading)
            .Add(v.AlwaysAfter2, -2).Add(v.AlwaysAfter5, -5).Add(v.AlwaysAfter10, -10);
        battery.Value = 50;
        battery.Relabel(25, v.Sooner25 + " " + t.PercentNote(7.5m));
        battery.Relabel(50, v.Sooner50 + " " + t.PercentNote(15));
        battery.Relabel(75, v.Sooner75 + " " + t.PercentNote(22.5m));
        var seconds = List(30, (t.SecondsOption(10), 10), (t.SecondsOption(30), 30));
        seconds.AddLine().AddCustom(v.Custom, new CustomEntry { Name = v.WarnMe, Unit = TimeUnit.Seconds, Shortest = 0, Longest = 600, Start = 30, Value = n => n, Label = t.SecondsOption });
        times.AccessibleName = v.LockAfter;
        battery.AccessibleName = v.OnBattery;
        seconds.AccessibleName = v.WarnMe;
        var row = new FlowRow(times, battery, seconds);
        f.Controls.Add(row);
        var boxes = new List<CustomBox>();
        foreach (var list in new[] { times, seconds })
        {
            list.Presenter = box =>
            {
                Shots.Place(box);
                box.Show(f);
                boxes.Add(box);
            };
        }
        return new Built
        {
            Arrange = () =>
            {
                int bottom = Stack(f, P(f, 16), [(row, 0)]);
                f.ClientSize = new Size(f.ClientSize.Width, bottom + P(f, 16));
            },
            Finish = (form, window) =>
            {
                var size = Size3Of(form);
                var parts = new List<Bitmap> { window };
                var timesMenu = times.BuildMenu();
                var batteryMenu = battery.BuildMenu();
                var pause = MenuLook.NewMenu(Lang.Direction);
                pause.Items.AddRange(MenuLook.PauseChoices(t, _ => { }, () => { }));
                // The pointer over a row, to show its fill.
                timesMenu.Items[1].Select();
                // The lists build their menus for their own scaling. The pause menu is built for the
                // system's, as the tray's is, so it is only drawn at 100%.
                foreach (var menu in size == Size3.Dpi150 ? new[] { timesMenu, batteryMenu } : new[] { timesMenu, batteryMenu, pause })
                {
                    parts.Add(DrawMenu(menu, form, size));
                }
                foreach (var menu in new[] { timesMenu, batteryMenu, pause }) menu.Dispose();
                // The Custom… box as it opens, and after a number too big for it; one at a time, as
                // opening a second closes the first.
                foreach (var list in new[] { times, seconds })
                {
                    list.OpenCustom(0);
                    Pump(200);
                    var box = boxes[^1];
                    if (size == Size3.Dpi150) Rescale(box, 144);
                    box.PerformLayout();
                    Pump(150);
                    parts.Add(Grab(box.Handle));
                    box.Field.Controls.OfType<TextBox>().Single().Text = "5000";
                    Pump(150);
                    parts.Add(Grab(box.Handle));
                    string boxName = $"{sheetName}: the {list.AccessibleName} Custom box";
                    Inspect(boxName, box, new(), size);
                    // The ways out of a shown box: Enter uses its number, Escape leaves the list as
                    // it was, and clicking anywhere else uses the number too.
                    TextBox Typing(CustomBox b) => b.Field.Controls.OfType<TextBox>().Single();
                    if (list == times)
                    {
                        Typing(box).Text = "45";
                        box.Key(Keys.Enter);
                        Pump(200);
                        Is(box.IsDisposed && Equals(times.Value, 45) && times.Text == t.OptionDuration(45), $"{boxName}: Enter uses the number typed ({times.Text})");
                    }
                    else
                    {
                        box.Key(Keys.Escape);
                        Pump(200);
                        Is(box.IsDisposed && Equals(seconds.Value, 30), $"{boxName}: Escape leaves the list as it was ({seconds.Text})");
                        seconds.OpenCustom(0);
                        Pump(200);
                        var again = boxes[^1];
                        Typing(again).Text = "20";
                        again.ClickedAway();
                        Pump(200);
                        Is(again.IsDisposed && Equals(seconds.Value, 20) && seconds.Text == t.SecondsOption(20), $"{boxName}: clicking away uses the number typed ({seconds.Text})");
                    }
                }
                return Beside(parts, Theme.Current.Window, P(form, 16), Lang.Current.RightToLeft);
            },
        };
    }

    static Size3 sizeNow;
    static string sheetName = "";

    static Size3 Size3Of(Form f) => sizeNow;

    // Draws a menu that is never shown: laid out at its size, then rounded and edged as Windows 11
    // shows it.
    static Bitmap DrawMenu(ContextMenuStrip menu, Form owner, Size3 size)
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
            using var pen = new Pen(Theme.Current.Edge);
            g.DrawPath(pen, path);
        }
        return shaped;
    }

    // Pictures side by side, top-aligned, with a gap; right to left in Arabic.
    static Bitmap Beside(List<Bitmap> parts, Color back, int gap, bool rtl)
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

    // ---- Making, scaling and drawing a sheet ----

    static void Sheet(string what, string tag, Size3 size, bool mirrored, Func<Form, Built> build)
    {
        string name = $"kit-{what}-{tag}{Suffix(size)}";
        sizeNow = size;
        sheetName = name;
        if (size == Size3.LargeText && !ScaleFonts(() =>
            {
                warming = true;
                try
                {
                    Make(build, mirrored, size, warmUp: true);
                }
                finally
                {
                    warming = false;
                }
            }))
        {
            // Not a failure of the controls: the tool has no way to make Large text yet.
            string line = $"open {name}: Large text needs Theme.UseForShots(dark, scale)";
            Console.WriteLine(line);
            report?.WriteLine(line);
            return;
        }
        try
        {
            var (f, built) = Make(build, mirrored, size, warmUp: false);
            var drawn = new List<(Control Control, string Text, Font Font, Rectangle Box, TextFormatFlags Flags)>();
            KitControl.TextDrawn = (c, text, font, box, flags) => drawn.Add((c, text, font, box, flags));
            f.Refresh();
            var window = Grab(f.Handle);
            KitControl.TextDrawn = null;
            foreach (var (control, focus) in built.Focus)
            {
                focus();
                Pump(120);
                f.Refresh();
                using var take = Grab(f.Handle);
                Paste(window, take, VisualBounds(f, control, P(f, 6)));
            }
            Shots.Foreground(f, name);
            Inspect(name, f, drawn, size);
            built.Keys?.Invoke(name);
            Shots.Foreground(f, name);
            var picture = built.Finish?.Invoke(f, window) ?? window;
            if (pictures) picture.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
            if (pictures) Console.WriteLine($"{name}: {picture.Width}x{picture.Height}");
            if (picture != window) picture.Dispose();
            window.Dispose();
            f.Close();
            f.Dispose();
            Pump(50);
        }
        finally
        {
            if (size == Size3.LargeText) RestoreFonts();
        }
    }

    static (Form, Built) Make(Func<Form, Built> build, bool mirrored, Size3 size, bool warmUp)
    {
        var f = new Form
        {
            AutoScaleDimensions = new SizeF(96F, 96F),
            AutoScaleMode = AutoScaleMode.Dpi,
            FormBorderStyle = FormBorderStyle.None,
            Font = Theme.Body(10f),
            RightToLeft = Lang.Direction,
            RightToLeftLayout = mirrored,
            Text = "kit",
        };
        Theme.Current.Dress(f);
        Shots.Place(f);
        var built = build(f);
        f.ClientSize = new Size(P(f, 780), P(f, 400));
        built.Arrange();
        f.Show();
        Pump(150);
        // Again once shown: until then every control reads as hidden, and panels leave hidden rows out.
        built.Arrange();
        Pump(50);
        if (size == Size3.Dpi150)
        {
            Rescale(f, 144);
            f.ClientSize = new Size(P(f, 780), f.ClientSize.Height);
            built.Arrange();
            Pump(150);
        }
        // Focus rings show, as after a key was pressed, on the sheet of states only.
        if (built.Focus.Count > 0) SendMessage(f.Handle, WmChangeUiState, (IntPtr)((1 << 16) | 2), IntPtr.Zero);
        if (warmUp)
        {
            f.Refresh();
            built.Finish?.Invoke(f, Grab(f.Handle)).Dispose();
            f.Close();
            f.Dispose();
            Pump(50);
        }
        return (f, built);
    }

    static void Hover(Control c) => typeof(Control).GetMethod("OnMouseEnter", Any)!.Invoke(c, [EventArgs.Empty]);

    // Where a control is in the window's picture, as seen: mirrored windows already put it where
    // it shows. Grown a little so a focus ring drawn around it comes along.
    static Rectangle VisualBounds(Form f, Control c, int grow)
    {
        GetWindowRect(f.Handle, out var w);
        var screen = c.RectangleToScreen(c.ClientRectangle);
        var box = new Rectangle(screen.X - w.L, screen.Y - w.T, screen.Width, screen.Height);
        box.Inflate(grow, grow);
        return box;
    }

    static void Paste(Bitmap onto, Bitmap from, Rectangle box)
    {
        box.Intersect(new Rectangle(0, 0, Math.Min(onto.Width, from.Width), Math.Min(onto.Height, from.Height)));
        using var g = Graphics.FromImage(onto);
        g.DrawImage(from, box, box, GraphicsUnit.Pixel);
    }

    // The window as it shows, drawn by itself: PrintWindow, flipped back for a mirrored window. The
    // plain WM_PRINT way, which draws a mirrored window mirrored every time; Windows' full-content
    // capture sometimes hands these borderless windows back already the right way round.
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

    // ---- 150% ----

    // A move to a monitor at another scaling, as WinForms does it: every control is told the new DPI
    // from the inside out, then the window gets WM_DPICHANGED. Sending the message alone leaves the
    // controls inside at the old DPI. Both monitors here are at 100%, so the move is only told.
    static void Rescale(Form f, int dpi)
    {
        foreach (var c in Inside(f, post: true).ToList()) Before(c, dpi);
        var b = f.Bounds;
        int old = f.DeviceDpi;
        var r = new RECT { L = b.Left, T = b.Top, R = b.Left + b.Width * dpi / old, B = b.Top + b.Height * dpi / old };
        SendMessage(f.Handle, WmDpiChanged, (IntPtr)((dpi << 16) | dpi), ref r);
        foreach (var c in Inside(f, post: false).ToList()) After(c);
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

    // ---- Large text ----

    // Large text makes every font 120%, through Theme's text scale for the render tool.
    const float Large = 1.2f;

    static bool ScaleFonts(Action draw)
    {
        Theme.UseForShots(Theme.Current.IsDark, Large);
        return true;
    }

    static void RestoreFonts() => Theme.UseForShots(Theme.Current.IsDark, 1f);

    // ---- Checks ----

    static void Inspect(string name, Form f, List<(Control Control, string Text, Font Font, Rectangle Box, TextFormatFlags Flags)> drawn, Size3 size)
    {
        var all = Inside(f, post: false).Where(c => c.Visible).ToList();
        bool rtl = f.RightToLeft == RightToLeft.Yes;

        // Nothing sticks out of what holds it.
        var outside = all.Where(c => c.Parent is { } p && !p.ClientRectangle.Contains(c.Bounds)).ToList();
        Is(outside.Count == 0, $"{name}: every control is inside what holds it{Few(outside.Select(c => $"{Describe(c)} {c.Bounds} in {c.Parent!.ClientSize}"))}");

        // Text drawn by hand fits its box.
        var cut = drawn.Distinct().Where(d => !Fits(d.Text, d.Font, d.Box, d.Flags)).ToList();
        Is(cut.Count == 0, $"{name}: the {drawn.Distinct().Count()} pieces of hand-drawn text fit their boxes{Few(cut.Select(d => $"\"{d.Text}\" {Measure(d.Text, d.Font, d.Box, d.Flags)} in {d.Box.Size}"))}");

        // Labels show their words whole, and the controls that size to their content got that size.
        var labels = all.OfType<Label>().Where(l => l.Text.Length > 0 && l.Width > 0).ToList();
        var shortLabels = labels.Where(l => Wrapped(l) > l.Height).ToList();
        Is(shortLabels.Count == 0, $"{name}: the {labels.Count} labels are tall enough for their words{Few(shortLabels.Select(l => $"\"{l.Text}\" needs {Wrapped(l)} has {l.Height}"))}");
        var sized = all.Where(c => c is KitButton or Segmented or LanguagePills or Toggle || c is DropDown { ButtonWidth: 0 }).ToList();
        var squeezed = sized.Where(c => c.GetPreferredSize(Size.Empty) is var want && (want.Width > c.Width || want.Height > c.Height)).ToList();
        Is(squeezed.Count == 0, $"{name}: the {sized.Count} buttons, lists and choices have the room they ask for{Few(squeezed.Select(c => $"{Describe(c)} wants {c.GetPreferredSize(Size.Empty)} has {c.Size}"))}");
        var boxes = all.OfType<TextBox>().ToList();
        var flatBoxes = boxes.Where(b => b.PreferredHeight > b.Height + 1).ToList();
        Is(flatBoxes.Count == 0, $"{name}: the {boxes.Count} typing boxes are tall enough{Few(flatBoxes.Select(b => $"{b.PreferredHeight} > {b.Height}"))}");

        // Every place Tab or the arrow keys can reach has a name.
        var reachable = all.Where(c => c.CanSelect || c is Part).ToList();
        var unnamed = reachable.Where(c => string.IsNullOrWhiteSpace(c.AccessibilityObject.Name)).ToList();
        Is(unnamed.Count == 0, $"{name}: the {reachable.Count} controls a key reaches all have a name{Few(unnamed.Select(Describe))}");

        // Tab goes in reading order: down the window, and along a line from its start.
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

        // The parts of each group in their order: tabs and language pills left to right in both
        // languages, a few choices and chips from the start of the line.
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
        Is(misordered.Count == 0, $"{name}: the parts of {groups.Count} groups are in order{(rtl ? " (tabs and languages left to right, the rest right to left)" : "")}{Few(misordered)}");

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
            Is(f.LogicalToDeviceUnits(10) == 15, $"{name}: 10 px come out as 15");
        }
        string key = name.Replace("-150", "").Replace("-large", "");
        if (size == Size3.Normal) normalHeights[key] = f.ClientSize.Height;
        else if (normalHeights.TryGetValue(key, out int normal))
        {
            double ratio = (double)f.ClientSize.Height / normal;
            if (size == Size3.Dpi150) Is(ratio is > 1.35 and < 1.65, $"{name}: 1.5 times as tall as at 100% ({ratio:0.00})");
            // Sheets of rows grow with their words; a few lists of a fixed least height need not.
            else if (name.Contains("-rows-") || name.Contains("-tabs-") || name.Contains("-apps-")) Is(ratio > 1.01, $"{name}: taller with Large text ({ratio:0.00})");
            else Is(ratio >= 1, $"{name}: no shorter with Large text ({ratio:0.00})");
        }
        if (size == Size3.LargeText)
        {
            float body = Theme.Body(10f).SizeInPoints;
            Is(Math.Abs(body - 12f) < 0.01f, $"{name}: Large text makes 10 pt text 12 pt ({body:0.##})");
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

    // ---- What a screen reader gets ----

    // The same questions Narrator asks, through Windows' own UI Automation client on another
    // thread, so the hand-declared provider interfaces are proven end to end: the tab list's
    // selection, a tab's container, a chip, and the × pressed as a screen reader presses it.
    static void Narrator(string name, TabStrip tabs, ChipList chips)
    {
        chips.Add("Snipping Tool");
        chips.Parent?.PerformLayout();
        Pump(100);
        var chip = chips.Parts[^1];
        var cross = chip.Controls[0];
        IntPtr stripWindow = tabs.Handle, tabWindow = tabs.Parts[1].Handle, chipWindow = chip.Handle, crossWindow = cross.Handle;
        string chosen = tabs.Parts[tabs.Selected].Text;
        string expectRemove = string.Format(Lang.Current.V1.RemoveApp, "Snipping Tool");
        var answers = new List<(bool Ok, string What)>();
        Exception? failed = null;
        var worker = new Thread(() =>
        {
            try
            {
                var client = (IUIAutomation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("ff48dba4-60ef-4201-aa87-54103eef594e"))!)!;
                var strip = client.ElementFromHandle(stripWindow);
                answers.Add((Equals(strip.GetCurrentPropertyValue(30003), 50018), "the tabs are a tab list"));
                // Under load the tree can still be settling when the first question arrives: ask
                // again for a few seconds before taking the answer.
                string? selected = null;
                int count = -1;
                var settle = DateTime.Now.AddSeconds(5);
                do
                {
                    var selection = strip.GetCurrentPropertyValue(30059) as IUIAutomationElementArray;
                    count = selection?.Length ?? -1;
                    selected = count == 1 ? selection!.GetElement(0).GetCurrentPropertyValue(30005) as string : null;
                    if (count == 1 && selected == chosen) break;
                    Thread.Sleep(50);
                }
                while (DateTime.Now < settle);
                answers.Add((count == 1 && selected == chosen, $"its selection is the chosen tab, {chosen}{(count == 1 && selected == chosen ? "" : $" (got {count} item(s), {selected ?? "none"})")}"));
                answers.Add((Equals(strip.GetCurrentPropertyValue(30061), true), "a tab must always be chosen"));
                var tab = client.ElementFromHandle(tabWindow);
                answers.Add((Equals(tab.GetCurrentPropertyValue(30003), 50019) && Equals(tab.GetCurrentPropertyValue(30079), false), "another tab is a tab item, not selected"));
                answers.Add((tab.GetCurrentPropertyValue(30080) is IUIAutomationElement container && Equals(container.GetCurrentPropertyValue(30003), 50018), "and its container is the tab list"));
                var item = client.ElementFromHandle(chipWindow);
                answers.Add((Equals(item.GetCurrentPropertyValue(30003), 50007) && Equals(item.GetCurrentPropertyValue(30005), "Snipping Tool"), "a chip is a list item named for its app"));
                var button = client.ElementFromHandle(crossWindow);
                answers.Add((Equals(button.GetCurrentPropertyValue(30003), 50000) && Equals(button.GetCurrentPropertyValue(30005), expectRemove), $"its × is a button named \"{expectRemove}\""));
                ((IUIAutomationInvokePattern)button.GetCurrentPattern(10000)).Invoke();
            }
            catch (Exception e)
            {
                failed = e;
            }
        });
        worker.SetApartmentState(ApartmentState.MTA);
        worker.IsBackground = true;
        worker.Start();
        var until = DateTime.Now.AddSeconds(20);
        while (worker.IsAlive && DateTime.Now < until) Pump(20);
        Pump(150);
        foreach (var (ok, what) in answers) Is(ok, $"{name}: UI Automation: {what}");
        Is(failed is null && !worker.IsAlive, $"{name}: UI Automation answered{(failed is null ? "" : ": " + failed.GetType().Name + " " + failed.Message)}");
        Is(!chips.Texts.Contains("Snipping Tool"), $"{name}: UI Automation: pressing the × removes Snipping Tool");
    }

    // The start of Windows' UI Automation client interfaces, as far as these checks call them; the
    // slots before each one keep their place in the table.
    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IUIAutomation
    {
        void CompareElements();
        void CompareRuntimeIds();
        void GetRootElement();
        IUIAutomationElement ElementFromHandle(IntPtr window);
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IUIAutomationElement
    {
        void SetFocus();
        void GetRuntimeId();
        void FindFirst();
        void FindAll();
        void FindFirstBuildCache();
        void FindAllBuildCache();
        void BuildUpdatedCache();
        [return: MarshalAs(UnmanagedType.Struct)] object GetCurrentPropertyValue(int property);
        void GetCurrentPropertyValueEx();
        void GetCachedPropertyValue();
        void GetCachedPropertyValueEx();
        void GetCurrentPatternAs();
        void GetCachedPatternAs();
        [return: MarshalAs(UnmanagedType.IUnknown)] object GetCurrentPattern(int pattern);
    }

    [ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IUIAutomationElementArray
    {
        int Length { get; }
        IUIAutomationElement GetElement(int index);
    }

    [ComImport, Guid("fb377fbe-8ea6-46d5-9c73-6499642d3059"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IUIAutomationInvokePattern
    {
        void Invoke();
    }

    // ---- Beside the sketch ----

    // The controls sheet next to the sketch's Appearance page, at the same size, in the same
    // language and theme. KIT_REF names the folder of the sketch's renders.
    static void Compare(string tag)
    {
        string? folder = Environment.GetEnvironmentVariable("KIT_REF");
        if (!pictures || string.IsNullOrEmpty(folder)) return;
        string sketch = Path.Combine(folder, $"settings-tab2-none-{tag}.png");
        string rows = Path.Combine(outDir, $"kit-rows-{tag}.png");
        string tabs = Path.Combine(outDir, $"kit-tabs-{tag}.png");
        if (!File.Exists(sketch) || !File.Exists(rows) || !File.Exists(tabs))
        {
            Console.WriteLine($"compare {tag}: no sketch render to compare with");
            return;
        }
        Directory.CreateDirectory(Path.Combine(outDir, "compare"));
        using var a = new Bitmap(sketch);
        using var top = new Bitmap(rows);
        using var bottom = new Bitmap(tabs);
        using var b = new Bitmap(Math.Max(top.Width, bottom.Width), top.Height + bottom.Height);
        using (var g = Graphics.FromImage(b))
        {
            g.DrawImage(top, 0, 0, top.Width, top.Height);
            g.DrawImage(bottom, 0, top.Height, bottom.Width, bottom.Height);
        }
        int gap = 24;
        using var both = new Bitmap(a.Width + b.Width + gap * 3, Math.Max(a.Height, b.Height) + gap * 2);
        using (var g = Graphics.FromImage(both))
        {
            g.Clear(Color.FromArgb(0x80, 0x80, 0x80));
            g.DrawImage(a, gap, gap, a.Width, a.Height);
            g.DrawImage(b, gap * 2 + a.Width, gap, b.Width, b.Height);
        }
        both.Save(Path.Combine(outDir, "compare", $"kit-vs-sketch-{tag}.png"), ImageFormat.Png);
        Console.WriteLine($"compare/kit-vs-sketch-{tag}.png");
    }

    static T Also<T>(this T value, Action<T> change)
    {
        change(value);
        return value;
    }

    // A few controls in a line at their own sizes, 10 px apart, from the start of the line; a new
    // line when the next does not fit.
    sealed class FlowRow : Panel
    {
        public FlowRow(params Control[] controls)
        {
            AccessibleName = "";
            foreach (var c in controls) Controls.Add(c);
        }

        List<Rectangle> Places(int width)
        {
            var places = new List<Rectangle>();
            int gap = LogicalToDeviceUnits(10);
            int x = 0, y = 0, line = 0;
            foreach (Control c in Controls)
            {
                var size = c.GetPreferredSize(Size.Empty);
                if (c is DropDown { ButtonWidth: > 0 } fixedWidth) size.Width = fixedWidth.Width;
                if (x > 0 && x + size.Width > width)
                {
                    x = 0;
                    y += line + gap;
                    line = 0;
                }
                places.Add(new Rectangle(x, y, size.Width, size.Height));
                x += size.Width + gap;
                line = Math.Max(line, size.Height);
            }
            return places;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
            var places = Places(width);
            return new Size(width, places.Count == 0 ? 0 : places.Max(p => p.Bottom));
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            var places = Places(Width);
            bool flip = RightToLeft == RightToLeft.Yes && !IsMirrored;
            for (int i = 0; i < Controls.Count; i++)
            {
                var p = places[i];
                Controls[i].Bounds = new Rectangle(flip ? Width - p.X - p.Width : p.X, p.Y, p.Width, p.Height);
            }
        }
    }
}
