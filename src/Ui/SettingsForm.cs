using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Serialization;

namespace SteppedOut;

// The settings window: the language at the top, the status box, the seven settings most people
// need, and five tabs under them for the rest. A change goes to the app a moment after it is made,
// and only when the settings really differ (ISettingsHost.ConfigEdited); the window never writes the
// file itself. A new language, theme or text size builds the inside again in place, on the same
// tab, at the same scroll and with the focus where it was.
sealed class SettingsForm : Form
{
    // The pages under the tabs, in tab order: Locking, Away and back, Appearance, Breaks, Extras.
    // A tab without one shows its name and what it will hold.
    static readonly Func<ISettingsHost, ISettingsPage>?[] TabPages =
    [
        host => new LockingTab(host),
        host => new AwayTab { Preview = host.PreviewSound },
        _ => new AppearanceTab(),
        host => new BreaksTab { Preview = host.PreviewSound },
        host => new AdvancedTab(host),
    ];

    static readonly (string Glyph, Func<V1Text, string> Name, Func<V1Text, string> Detail, Func<V1Text, string> Intro)[] TabList =
    [
        (Glyphs.Lock, v => v.LockingTab, v => v.LockingTabDetail, v => v.LockingIntro),
        (Glyphs.Exchange, v => v.AwayTab, v => v.AwayTabDetail, v => v.AwayIntro),
        (Glyphs.Palette, v => v.AppearanceTab, v => v.AppearanceTabDetail, v => v.AppearanceIntro),
        (Glyphs.Coffee, v => v.BreaksTab, v => v.BreaksTabDetail, v => v.BreaksIntro),
        (Glyphs.Sliders, v => v.AdvancedTab, v => v.AdvancedTabDetail, v => v.AdvancedIntro),
    ];

    // How long after the last change the settings go to the app, so a few quick choices make one save.
    public const int SaveDelay = 400;

    // How many tabs there are, for the setting that keeps the last one (Config.MaxSettingsTab).
    internal static int TabCount => TabList.Length;

    readonly ISettingsHost host;
    readonly Func<ISettingsHost, ISettingsPage>?[] factories;
    readonly ISettingsPage?[] pages = new ISettingsPage?[TabList.Length];
    readonly Control?[] built = new Control?[TabList.Length];
    readonly MainList main;
    readonly System.Windows.Forms.Timer saveSoon = new() { Interval = SaveDelay };

    // The settings the app has, and the same with the changes not handed over yet.
    Config baseline;
    Config working;
    bool pending;
    // Above zero while the window fills its controls from the settings: nothing they raise then is a choice.
    int filling;

    // What the inside was built for.
    Strings text;
    // The line at the bottom: changes saved, or not, and the version.
    Footer? footer;
    Theme builtTheme;
    float builtScale;

    Content content;
    StatusBox status;
    LanguagePills pills;
    TabStrip tabs;
    int tab;
    bool rebuildQueued;
    // The language was just picked from the pills: they keep the focus through the rebuild.
    bool pillsPicked;

    public SettingsForm(Config config, ISettingsHost host, IReadOnlyList<Func<ISettingsHost, ISettingsPage>?>? tabPages = null)
    {
        this.host = host;
        factories = Enumerable.Range(0, TabList.Length).Select(i => tabPages is null ? TabPages[i] : i < tabPages.Count ? tabPages[i] : null).ToArray();
        baseline = config.Copy();
        baseline.Clamp();
        working = baseline.Copy();
        // The tab used last, Locking the first time.
        tab = baseline.SettingsTab;
        text = Lang.Current;
        builtTheme = Theme.Current;
        builtScale = Theme.TextScale;

        main = new MainList(() => this.host.StartWithWindowsState);
        main.Edited += Apply;
        main.TryItClicked += () =>
        {
            // The note shows the warning as it is now, even one picked a moment ago.
            FlushPending();
            this.host.TryItChosen();
        };
        main.StartWithWindowsClicked += on =>
        {
            this.host.StartWithWindowsChosen(on);
            // Shows what Windows has now, which a policy can keep from changing.
            Fill(main);
        };
        saveSoon.Tick += (_, _) => FlushPending();

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Icon = Icons.Window();
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        Dress();
        BuildContent();
        ResumeLayout(false);
    }

    // ---- The settings ----

    // How many edits came in while the window filled its controls, which should be none: for tests
    // and the render tool.
    internal int EditsWhileFilling { get; private set; }

    // For tests and the render tool.
    internal MainList Main => main;
    internal StatusBox StatusBox => status;
    internal LanguagePills Pills => pills;
    internal TabStrip Tabs => tabs;
    internal int Tab => tab;
    internal Control? PageControl => built[tab];
    internal ScrollableControl Scroller => content;
    internal int ColumnWidth => content.ColumnWidth;
    internal int ContentHeight => content.Measure();
    internal bool Pending => pending;
    internal Config Working => working.Copy();

    // Settings that changed outside the window: from the file, the tray menu or the welcome. A
    // change made here and not handed over yet stays on top of them.
    public void Refill(Config config)
    {
        var fresh = config.Copy();
        fresh.Clamp();
        if (fresh.SameAs(baseline)) return;
        var mine = pending ? Changes(baseline, working) : [];
        var edited = working.Copy();
        baseline = fresh.Copy();
        foreach (var property in mine) property.SetValue(fresh, property.GetValue(edited));
        working = fresh;
        pending = !working.SameAs(baseline);
        if (!pending) saveSoon.Stop();
        FillAll();
        status.LockingOn = working.LockWhenAway;
        RebuildIfStale();
    }

    // Start with Windows changed in Windows, or a choice of it finished after StartWithWindowsChosen
    // returned: the switch shows StartsWithWindows as it is now.
    public void StartWithWindowsChanged() => Fill(main);

    internal Footer? FooterLine => footer;

    // The host's last save went through or failed (ISettingsHost.SaveFailed): the footer says which.
    public void SaveStateChanged()
    {
        if (footer is null) return;
        int was = footer.Height;
        footer.Say(SavedLine);
        // Words that no longer fit beside the version, or fit again, change the footer's height:
        // the column is laid out again, and once more for WinForms to apply the scroll range the
        // first layout set.
        if (footer.GetPreferredSize(new Size(footer.Width, 0)).Height == was) return;
        content.PerformLayout();
        content.PerformLayout();
    }

    string SavedLine => host.SaveFailed ? text.V1.SaveFailedFooter : text.V1.AutoSaved;

    // Hands the changes waiting for the short delay to the app now: before Try it, on closing, and
    // from tests, which have no message loop for the timer.
    public void FlushPending()
    {
        saveSoon.Stop();
        if (!pending) return;
        pending = false;
        if (working.SameAs(baseline)) return;
        bool newLanguage = working.Language != baseline.Language;
        baseline = working.Copy();
        host.ConfigEdited(working.Copy());
        if (newLanguage) host.LanguageChanged(working.Language);
        if (IsDisposed) return;
        RebuildIfStale();
    }

    void Apply(Func<Config, Config> edit)
    {
        if (filling > 0)
        {
            EditsWhileFilling++;
            return;
        }
        var next = edit(working.Copy());
        next.Clamp();
        bool newLanguage = next.Language != working.Language;
        working = next;
        // Every page shows the settings as they are now, the one that made the change too, so a value
        // the settings bring into range shows as it is kept.
        FillAll();
        status.LockingOn = working.LockWhenAway;
        pending = !working.SameAs(baseline);
        if (!pending)
        {
            saveSoon.Stop();
            return;
        }
        // A new language shows at once; anything else waits a moment for the next choice.
        if (newLanguage)
        {
            FlushPending();
            return;
        }
        saveSoon.Stop();
        if (IsHandleCreated) saveSoon.Start();
    }

    // The settings that differ between two copies.
    static List<PropertyInfo> Changes(Config before, Config after) =>
        typeof(Config).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() is not { Condition: JsonIgnoreCondition.Always })
            .Where(p => !Same(p.GetValue(before), p.GetValue(after)))
            .ToList();

    static bool Same(object? a, object? b) =>
        a is IEnumerable x && b is IEnumerable y && a is not string ? x.Cast<object?>().SequenceEqual(y.Cast<object?>()) : Equals(a, b);

    void FillAll()
    {
        filling++;
        try
        {
            pills.Value = PillFor(working.Language);
            main.Refill(working.Copy());
            for (int i = 0; i < pages.Length; i++)
            {
                if (built[i] is not null) pages[i]!.Refill(working.Copy());
            }
        }
        finally
        {
            filling--;
        }
    }

    void Fill(ISettingsPage page)
    {
        filling++;
        try
        {
            page.Refill(working.Copy());
        }
        finally
        {
            filling--;
        }
    }

    void FocusPill()
    {
        if (pills.Current < 0 || !IsHandleCreated) return;
        var pill = pills.Parts[pills.Current];
        BeginInvoke(() =>
        {
            if (!pill.IsDisposed && pill.CanFocus && !pill.Focused) pill.Focus();
        });
    }

    // "Same as Windows" shows as the language Windows is in.
    static string PillFor(string language) => language is "en" or "ar" ? language : Lang.Current.RightToLeft ? "ar" : "en";

    // ---- The inside ----

    void Dress()
    {
        var t = Theme.Current;
        BackColor = t.Window;
        ForeColor = t.Text;
        Font = Theme.Body(10f);
        if (IsHandleCreated) Native.UseDarkTitleBar(Handle, t.IsDark);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Each time: a change of direction makes the window again.
        Native.UseDarkTitleBar(Handle, Theme.Current.IsDark);
    }

    [MemberNotNull(nameof(content), nameof(status), nameof(pills), nameof(tabs))]
    void BuildContent()
    {
        var v = text.V1;
        Text = v.SettingsTitle;
        RightToLeft = text.RightToLeft ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = text.RightToLeft;
        content = new Content { Dock = DockStyle.Fill };
        Theme.Current.DressScrollBars(content);

        pills = new LanguagePills(v.Language).Add(v.LanguageEnglish, "en").Add(v.LanguageArabic, "ar");
        pills.Value = PillFor(working.Language);
        pills.SelectedChanged += (_, _) =>
        {
            if (pills.Value is not string language) return;
            pillsPicked = true;
            Apply(c =>
            {
                c.Language = language;
                return c;
            });
        };
        // The group takes no focus of its own, so it goes on to the pill in use, where the arrow keys
        // work.
        pills.GotFocus += (_, _) => FocusPill();

        status = new StatusBox(host, text) { LockingOn = working.LockWhenAway };

        Control list;
        filling++;
        try
        {
            list = main.Build(working.Copy(), text);
        }
        finally
        {
            filling--;
        }

        tabs = new TabStrip { AccessibleName = v.SettingsSections };
        foreach (var (glyph, name, detail, _) in TabList) tabs.Add(glyph, name(v), detail(v));
        tabs.Selected = tab;
        tabs.SelectedChanged += (_, _) => TabChosen(tabs.Selected);

        footer = new Footer(SavedLine, text.VersionLine(AppInfo.Version));
        content.Set(pills, status, list, tabs, footer);
        ShowTab(tab);
        Controls.Add(content);
    }

    ISettingsPage PageAt(int index)
    {
        if (pages[index] is { } page) return page;
        page = factories[index]?.Invoke(host) ?? new Placeholder(index);
        page.Edited += Apply;
        pages[index] = page;
        return page;
    }

    // A tab the person chose, or one a place in the app asked for: shown, and kept for the next time
    // the window opens. Only the tab is saved, through the host, never as an edit: it is no change
    // to how the app works, so nothing is applied again and nothing waits for the short delay. Both
    // copies of the settings get it, so a later edit does not take it back.
    void TabChosen(int index)
    {
        int was = tab;
        ShowTab(index);
        if (index == was && baseline.SettingsTab == index) return;
        baseline.SettingsTab = index;
        working.SettingsTab = index;
        host.TabChosen(index);
    }

    // Opens a tab, as when the person picks it: for a place in the app that names one.
    public void OpenTab(int index)
    {
        index = Math.Clamp(index, 0, TabList.Length - 1);
        if (index == tab && baseline.SettingsTab == index) return;
        tabs.Selected = index;
        TabChosen(index);
    }

    void ShowTab(int index)
    {
        tab = index;
        var page = PageAt(index);
        if (built[index] is null)
        {
            filling++;
            try
            {
                built[index] = page.Build(working.Copy(), text);
            }
            finally
            {
                filling--;
            }
        }
        content.ShowPage(built[index]!);
    }

    // Builds the inside again when the language, the theme or the text size changed since it was
    // built, for the app to call when Windows' colors change.
    public void RebuildIfStale()
    {
        if (IsDisposed || Disposing || !Stale) return;
        if (!IsHandleCreated)
        {
            Rebuild();
            return;
        }
        // Once the click or key that brought it has finished with the controls it came from.
        if (rebuildQueued) return;
        rebuildQueued = true;
        try
        {
            BeginInvoke(Rebuild);
        }
        catch (InvalidOperationException)
        {
            rebuildQueued = false;
        }
    }

    bool Stale => Lang.Current != text || Theme.Current != builtTheme || Theme.TextScale != builtScale;

    void Rebuild()
    {
        rebuildQueued = false;
        if (IsDisposed || Disposing || !Stale) return;
        var keep = Remember();
        text = Lang.Current;
        builtTheme = Theme.Current;
        builtScale = Theme.TextScale;
        SuspendLayout();
        var old = content;
        Controls.Remove(old);
        for (int i = 0; i < built.Length; i++)
        {
            if (built[i] is { Parent: null } hidden) hidden.Dispose();
            built[i] = null;
        }
        old.Dispose();
        Dress();
        BuildContent();
        ResumeLayout(true);
        Restore(keep);
    }

    // ---- Where the person was ----

    sealed record Keep(int Tab, int Scroll, int Focus, bool OnPills);

    Keep Remember()
    {
        var active = ActiveControl;
        bool onPills = pillsPicked || (active is not null && pills.Contains(active));
        int focus = active is null ? -1 : Stops(content).IndexOf(active);
        pillsPicked = false;
        return new Keep(tab, -content.AutoScrollPosition.Y, focus, onPills);
    }

    void Restore(Keep keep)
    {
        content.PerformLayout();
        // Content sets its scroll range during a layout, and WinForms applies it on the next one.
        content.PerformLayout();
        content.AutoScrollPosition = new Point(0, keep.Scroll);
        if (keep.OnPills && pills.Current >= 0) pills.Parts[pills.Current].Select();
        else if (keep.Focus >= 0 && Stops(content) is var stops && keep.Focus < stops.Count) stops[keep.Focus].Select();
    }

    // Every control that can take the focus, in the order they were added.
    static List<Control> Stops(Control parent)
    {
        var all = new List<Control>();
        foreach (Control child in parent.Controls)
        {
            if (child.CanSelect) all.Add(child);
            all.AddRange(Stops(child));
        }
        return all;
    }

    // ---- The window ----

    // As tall as what it shows, up to most of the screen, and as wide as the sketch's 780 px plus a
    // scroll bar, so the rows keep their width whether it shows or not.
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var area = WorkArea.Of(Screen.FromControl(this));
        int height = Math.Min(Math.Max(content.Measure(), LogicalToDeviceUnits(480)), Tallest(area));
        ClientSize = new Size(Widest, height);
        MinimumSize = new Size(Width, LogicalToDeviceUnits(360));
        MaximumSize = new Size(Width, SystemInformation.VirtualScreen.Height);
        CenterToScreen();
        content.PerformLayout();
        content.PerformLayout();
        main.LockAfter?.Select();
    }

    int Widest => LogicalToDeviceUnits(780) + SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi);

    // The tallest the window's inside grows by itself, leaving some of the screen around it.
    static int Tallest(Rectangle area) => area.Height * 9 / 10;

    // Dragged onto a monitor scaled differently, the window is scaled as a whole, which can leave it
    // taller than that screen; it is kept within OnLoad's limit there, and its bottom on the screen.
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        var area = WorkArea.Of(Screen.FromControl(this));
        MinimumSize = Size.Empty;
        MaximumSize = Size.Empty;
        ClientSize = new Size(Widest, Math.Min(ClientSize.Height, Tallest(area)));
        MinimumSize = new Size(Width, LogicalToDeviceUnits(360));
        MaximumSize = new Size(Width, SystemInformation.VirtualScreen.Height);
        if (Bottom > area.Bottom) Top = Math.Max(area.Top, area.Bottom - Height);
        content.PerformLayout();
        content.PerformLayout();
        // Once every control inside has taken the new scaling, which comes after this: the inside
        // fills the window again, and the scroll range follows.
        BeginInvoke(() =>
        {
            if (IsDisposed) return;
            PerformLayout();
            content.PerformLayout();
            content.PerformLayout();
        });
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        FlushPending();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            saveSoon.Dispose();
            for (int i = 0; i < built.Length; i++)
            {
                if (built[i] is { Parent: null } hidden) hidden.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    // ---- The pieces ----

    // The inside, which scrolls: the language at the top of the window at its right, the status box,
    // the main list, the tabs with the chosen page hanging from them, and the footer, in a column as
    // wide as the sketch's (740 px) in the middle of the window.
    sealed class Content : Panel
    {
        Control? pills;
        Control? status;
        Control? list;
        Control? tabs;
        Control? page;
        Control? footer;

        public Content()
        {
            AutoScroll = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            // Read by what is in it. Left unnamed, WinForms names a panel after the label before it.
            AccessibleName = "";
        }

        public int ColumnWidth => LogicalToDeviceUnits(740);

        public void Set(Control pills, Control status, Control list, Control tabs, Control footer)
        {
            this.pills = pills;
            this.status = status;
            this.list = list;
            this.tabs = tabs;
            this.footer = footer;
            // In reading order: Tab goes from the language to the status box, the list, the tabs and the page.
            pills.TabIndex = 0;
            status.TabIndex = 1;
            list.TabIndex = 2;
            tabs.TabIndex = 3;
            footer.TabIndex = 5;
            Controls.AddRange([pills, status, list, tabs, footer]);
        }

        public void ShowPage(Control next)
        {
            if (page == next) return;
            SuspendLayout();
            if (page is not null)
            {
                page.Layout -= PageLaidOut;
                Controls.Remove(page);
            }
            page = next;
            next.TabIndex = 4;
            Controls.Add(next);
            // Before the footer among the children too, which is the order screen readers read them
            // in, so the footer is read after the page rather than between the tabs and it (I12).
            if (footer is not null) Controls.SetChildIndex(next, Controls.IndexOf(footer));
            next.Layout += PageLaidOut;
            ResumeLayout(false);
            PerformLayout();
            // Content sets its scroll range during a layout, and WinForms applies it on the next one.
            PerformLayout();
        }

        // Set while this lays out its pieces, when a page laid out at its new size is no news.
        bool placing;
        bool relayoutQueued;

        // A page that grew or shrank by itself, as when a row shows or a chip is added, moves the
        // footer and the scroll range with it: once the page has finished laying itself out, so its
        // own layout is never started again from inside.
        void PageLaidOut(object? sender, LayoutEventArgs e)
        {
            if (placing || relayoutQueued || !Resized()) return;
            if (!IsHandleCreated)
            {
                PerformLayout();
                return;
            }
            relayoutQueued = true;
            BeginInvoke(() =>
            {
                relayoutQueued = false;
                if (!IsDisposed && Resized()) PerformLayout();
            });
        }

        bool Resized() => page is not null && ClientSize.Width > 0 && page.GetPreferredSize(new Size(ColumnWidth, 0)).Height != page.Height;

        // The space at the top that the language hangs in, down to the status box.
        int TopBand => pills is null ? 0 : Math.Max(LogicalToDeviceUnits(46), pills.GetPreferredSize(Size.Empty).Height + LogicalToDeviceUnits(10));

        // Each piece's top and height at the column's width, and the height of the whole.
        (List<(Control Piece, int Top, int Height)> Places, int Height) Stack()
        {
            var places = new List<(Control, int, int)>();
            int width = ColumnWidth;
            int y = TopBand;
            void Put(Control? piece, int gap)
            {
                if (piece is null) return;
                y += LogicalToDeviceUnits(gap);
                int height = piece.GetPreferredSize(new Size(width, 0)).Height;
                places.Add((piece, y, height));
                y += height;
            }
            Put(status, 0);
            Put(list, 8);
            Put(tabs, 14);
            Put(page, 0);
            Put(footer, 12);
            return (places, y + LogicalToDeviceUnits(16));
        }

        public int Measure() => Stack().Height;

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (ClientSize.Width <= 0) return;
            int width = ColumnWidth;
            int left = Math.Max(0, (ClientSize.Width - width) / 2);
            int scroll = AutoScrollPosition.Y;
            var (places, height) = Stack();
            placing = true;
            try
            {
                if (pills is not null)
                {
                    var size = pills.GetPreferredSize(Size.Empty);
                    // At the top right in both languages: a mirrored window puts the left on the right.
                    int x = IsMirrored ? left : left + width - size.Width;
                    pills.SetBounds(x, scroll, size.Width, size.Height);
                }
                foreach (var (piece, top, pieceHeight) in places) piece.SetBounds(left, scroll + top, width, pieceHeight);
            }
            finally
            {
                placing = false;
            }
            if (AutoScrollMinSize.Height != height) AutoScrollMinSize = new Size(0, height);
        }
    }

    // "Changes are saved automatically." at the start, the version at the end, both without the room
    // Windows adds at the ends of words, so they line up with the edges of the boxes above. While
    // saving fails it says that instead, so the window never claims changes are saved when they
    // are not. When the two do not fit side by side, as that longer line at Windows' largest text
    // sizes, the saved words take the whole width, on more lines if they need them, and the version
    // goes under them at the end.
    internal sealed class Footer : KitControl
    {
        const TextFormatFlags Flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        const TextFormatFlags Wrapped = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
        string saved;
        readonly string version;

        public Footer(string saved, string version)
        {
            this.saved = saved;
            this.version = version;
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            AccessibleRole = AccessibleRole.StaticText;
            AccessibleName = saved + " " + version;
        }

        internal string Saved => saved;

        // The two lines side by side within the footer, a gap apart.
        internal bool Fits => FitsIn(Width);

        bool FitsIn(int width) => Wide(saved) + Px(16) + Wide(version) <= width;

        internal void Say(string words)
        {
            if (words == saved) return;
            saved = words;
            AccessibleName = saved + " " + version;
            Automation.Notify(this, AccessibleEvents.NameChange);
            Invalidate();
        }

        Font Face => Body(9f);

        int Wide(string words) => TextRenderer.MeasureText(words, Face, Size.Empty, Flags | TextFormatFlags.NoPrefix).Width + 1;

        int Line => TextRenderer.MeasureText("Ag", Face, Size.Empty, Flags).Height;

        // The saved words' height over the whole width, when they are not beside the version.
        int Above(int width) => TextRenderer.MeasureText(saved, Face, new Size(width, int.MaxValue), Wrapped | TextFormatFlags.NoPrefix).Height;

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
            return new(width, width <= 0 || FitsIn(width) ? Line : Above(width) + Line);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var color = Theme.Current.Subtle;
            int second = Wide(version);
            if (Fits)
            {
                int first = Wide(saved);
                Write(e.Graphics, saved, Face, Box(0, 0, first, Height), color, Flags);
                Write(e.Graphics, version, Face, Box(Width - second, 0, second, Height), color, Flags);
            }
            else
            {
                int above = Above(Width);
                Write(e.Graphics, saved, Face, Box(0, 0, Width, above), color, Wrapped);
                Write(e.Graphics, version, Face, Box(Width - second, above, second, Height - above), color, Flags);
            }
            base.OnPaint(e);
        }
    }

    // A tab whose page is still to come: its name and what it will hold.
    sealed class Placeholder(int index) : ISettingsPage
    {
        public Control Build(Config config, Strings text)
        {
            var (glyph, name, _, intro) = TabList[index];
            return new FlatPanel { JoinedAbove = true }.Add(new FlatRow(name(text.V1), intro(text.V1), null, glyph));
        }

        public void Refill(Config config)
        {
        }

        // It has nothing to change.
        public event Action<Func<Config, Config>>? Edited
        {
            add { }
            remove { }
        }
    }
}
