using System.Runtime.InteropServices;

namespace SteppedOut;

// "App rules" on the Locking tab: its title with Add app, then one line per app with what the app
// does to the lock time, when that applies, and a × that removes it, under a line of column names.
// Empty until someone adds an app. Each change shows at once and goes out through the change it
// was given, as a change to the list of rules, so it lands on whatever list the settings hold then.
sealed class AppRulesList : Card
{
    readonly Strings text;
    readonly Action<Func<List<AppRule>, List<AppRule>>> change;
    readonly FlatRow head;
    readonly KitButton add;
    readonly TextBlock empty;
    readonly TextBlock onScreen;
    // The whole name of an app whose name is cut short in its line, shown when the pointer rests on it.
    readonly ToolTip tip = new() { ShowAlways = true };
    List<RuleRow> rows = new();
    List<AppRule> shown = new();
    decimal normal = LockTime.NormalMinutes(Config.DefaultIdleMinutes);

    // The list needs another height: a rule came or went.
    public event Action? HeightChanged;

    public AppRulesList(Strings text, Action<Func<List<AppRule>, List<AppRule>>> change)
    {
        this.text = text;
        this.change = change;
        var v = text.V1;
        Flat = true;
        add = new KitButton(v.AddApp) { Small = true, Glyph = Glyphs.Plus };
        add.Click += (_, _) => OpenPicker();
        empty = Note(v.NoAppsAdded, italic: true);
        onScreen = Note(v.OnScreenExplained, italic: false);
        head = new FlatRow(v.AppRules, v.AppRulesDetail, add) { TabIndex = 0 };
        Controls.Add(head);
        Controls.Add(onScreen);
        onScreen.TabIndex = int.MaxValue;
        Show([], normal);
    }

    // How Add app lists the apps to choose from; the app picker, or a stand-in in tests.
    internal Action<Control, IEnumerable<string>, Action<PickedApp>>? Picker { get; set; }

    internal KitButton AddButton => add;

    // Where the lines read the names programs give themselves; a stand-in in tests.
    internal ProgramNames Names { get; set; } = ProgramNames.Shared;

    // Where the lines read their programs' icons; a stand-in in tests.
    internal ProgramIcons Icons { get; set; } = ProgramIcons.Shared;

    // The size a line draws its program's icon at (RuleRow.OnPaint), for reading the icons ahead.
    int IconSize => Px(16);

    internal IReadOnlyList<RuleRow> Rows => rows;

    // "No apps added", shown while the list is empty, and the line that says what "Only when on
    // screen" means, shown while it is not.
    internal TextBlock EmptyNote => empty;

    internal TextBlock OnScreenNote => onScreen;

    // The rules as the settings hold them now. Lines for apps still listed stay, so focus stays where
    // it was; nothing here tells anyone of a change.
    public void Show(IReadOnlyList<AppRule> rules, decimal normal)
    {
        this.normal = normal;
        shown = (rules ?? []).Where(rule => rule is not null).ToList();
        // The names and the icons are read together, and waited for a moment in all.
        var started = System.Diagnostics.Stopwatch.StartNew();
        var programs = shown.Where(rule => rule.Aumid is null).Select(rule => rule.Path).ToList();
        Icons.Wait(programs, IconSize, TimeSpan.Zero);
        Names.Wait(shown.Select(rule => rule.Path), ProgramNames.Moment);
        Icons.Wait(programs, IconSize, ProgramNames.Moment - started.Elapsed);
        var keep = new Dictionary<string, RuleRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows) keep.TryAdd(row.Key, row);
        var next = new List<RuleRow>();
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in shown)
        {
            string key = Key(rule.App);
            if (key.Length == 0 || !listed.Add(key)) continue;
            if (keep.Remove(key, out var row)) row.Show(rule, normal);
            else
            {
                row = new RuleRow(this, rule, normal);
                Controls.Add(row);
            }
            next.Add(row);
        }
        foreach (var gone in keep.Values)
        {
            tip.SetToolTip(gone, null);
            Controls.Remove(gone);
            gone.Dispose();
        }
        rows = next;
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].TabIndex = i + 1;
            Controls.SetChildIndex(rows[i], i + 1);
        }
        head.Below = rows.Count == 0 ? empty : null;
        onScreen.Visible = rows.Count > 0;
        Fit();
        // Longer words chosen in one line can move the columns of every line.
        foreach (var row in rows) row.PerformLayout();
        Invalidate();
    }

    void OpenPicker()
    {
        var already = shown.Select(rule => rule.App).ToList();
        (Picker ?? ShowPicker)(add, already, Add);
    }

    void ShowPicker(Control button, IEnumerable<string> already, Action<PickedApp> picked) =>
        AppPicker.Show(button, already, picked, new PickerText(text.V1.ChooseAnotherApp, text.V1.AppPickerFileFilter));

    // An app picked from Add app gets a rule that keeps the PC from locking while it is open, the
    // first choice of its list; someone adding an app most often wants that, and can pick another.
    // An app already listed is not added twice: its line takes focus.
    internal void Add(PickedApp app)
    {
        string key = Key(app.Name);
        if (key.Length == 0) return;
        if (rows.FirstOrDefault(row => Same(row.Key, key)) is { } listed)
        {
            listed.FocusRule();
            return;
        }
        bool focused = ContainsFocus;
        var rule = new AppRule(app.Name, Effect.Never, AppWhen.Running, app.Path, app.Aumid);
        Apply(list => list.Any(r => Same(Key(r.App), key)) ? list : [.. list, rule]);
        if (focused) rows.FirstOrDefault(row => Same(row.Key, key))?.FocusRule();
    }

    internal void Remove(RuleRow row)
    {
        int at = rows.IndexOf(row);
        bool focused = row.ContainsFocus;
        string key = row.Key;
        Apply(list => list.Where(r => !Same(Key(r.App), key)).ToList());
        if (!focused) return;
        if (rows.Count == 0) add.Focus();
        else rows[Math.Clamp(at, 0, rows.Count - 1)].FocusRule();
    }

    internal void Changed(RuleRow row, Effect effect)
    {
        string key = row.Key;
        Apply(list => list.Select(r => Same(Key(r.App), key) ? r with { Effect = effect } : r).ToList());
    }

    internal void Changed(RuleRow row, AppWhen when)
    {
        string key = row.Key;
        Apply(list => list.Select(r => Same(Key(r.App), key) ? r with { When = when } : r).ToList());
    }

    // Shown here first, then handed on.
    void Apply(Func<List<AppRule>, List<AppRule>> edit)
    {
        Show(edit(shown.ToList()), normal);
        change(list => edit(list ?? []));
    }

    internal Strings Words => text;

    // Rules match apps by name, whatever the case, with or without ".exe" or a folder.
    internal static string Key(string? app) => ProcessRule.Normalize(app ?? "");

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // ---- Layout ----

    // Where the four columns are inside a line of this width, from its start: the app, Locking,
    // Applies when and the ×, as the sketch's grid of 128 px, 1.6 and 1.15 shares, and 28 px, 8 px
    // apart and 8 px in from the line's edge. A longer app name widens the first column, up to a
    // third of the line, with room the two lists can spare beyond the words chosen in them. When
    // the words chosen in one of the two lists need more than its share, as with Large text, it
    // takes room the other list can spare. When the two together need more than both shares, the
    // app column gives up room, down to 72 px: a name cut short shows whole on hover, words cut in
    // a list do not.
    internal static (int NameX, int NameW, int RuleX, int RuleW, int WhenX, int WhenW, int RemoveX, int RemoveW) Columns(Control c, int width, int ruleNeed = 0, int whenNeed = 0, int nameNeed = 0)
    {
        int pad = c.LogicalToDeviceUnits(8) + 1;
        int gap = c.LogicalToDeviceUnits(8);
        int name = c.LogicalToDeviceUnits(128);
        int remove = c.LogicalToDeviceUnits(28);
        int most = Math.Max(name, (width - 2 * pad) / 3);
        if (nameNeed > name)
        {
            int spare = Math.Max(0, width - 2 * pad - name - remove - 3 * gap - ruleNeed - whenNeed);
            name += Math.Min(Math.Min(nameNeed, most) - name, spare);
        }
        int lists = width - 2 * pad - name - remove - 3 * gap;
        if (ruleNeed + whenNeed > lists)
        {
            int given = Math.Min(ruleNeed + whenNeed - lists, Math.Max(0, name - c.LogicalToDeviceUnits(72)));
            name -= given;
        }
        int rest = Math.Max(0, width - 2 * pad - name - remove - 3 * gap);
        int rule = rest * 160 / 275;
        int when = rest - rule;
        if (when < whenNeed)
        {
            int moved = Math.Min(whenNeed - when, Math.Max(0, rule - ruleNeed));
            rule -= moved;
            when += moved;
        }
        else if (rule < ruleNeed)
        {
            int moved = Math.Min(ruleNeed - rule, Math.Max(0, when - whenNeed));
            rule += moved;
            when -= moved;
        }
        int ruleX = pad + name + gap;
        int whenX = ruleX + rule + gap;
        return (pad, name, ruleX, rule, whenX, when, whenX + when + gap, remove);
    }

    // The columns every line uses, so the lists line up from one line to the next.
    internal (int NameX, int NameW, int RuleX, int RuleW, int WhenX, int WhenW, int RemoveX, int RemoveW) Grid(Control c, int width) =>
        Columns(c, width, rows.Count == 0 ? 0 : rows.Max(row => row.RuleNeed), rows.Count == 0 ? 0 : rows.Max(row => row.WhenNeed),
            rows.Count == 0 ? 0 : rows.Max(row => row.NameNeed));

    // Shows the whole name over a line whose name is cut short, or nothing.
    internal void NameTip(RuleRow row, string? whole)
    {
        if (tip.GetToolTip(row) != (whole ?? "")) tip.SetToolTip(row, whole);
    }

    internal string NameTipOf(RuleRow row) => tip.GetToolTip(row) ?? "";

    // The name a line shows: the name the program gives itself, such as "Paint" for mspaint, once its
    // file is read; otherwise the name the rule matches on. A name still being read goes into its
    // line when it comes.
    internal string NameOf(RuleRow row, AppRule rule)
    {
        if (Names.Name(rule.Path, row, _ => row.Renamed()) is { } own) return own;
        string key = Key(rule.App);
        return key.Length > 0 ? key : rule.App;
    }

    // A line's name came: a longer name can move the columns of every line.
    internal void NamesChanged()
    {
        foreach (var row in rows) row.PerformLayout();
        Invalidate();
    }

    // How many times the lines have measured their words, for the tests.
    internal int Measures { get; private set; }

    internal void Measured() => Measures++;

    Font HeaderFont => Theme.Body(9f, DeviceDpi);

    int HeaderHeight => TextRenderer.MeasureText("Ag", HeaderFont).Height;

    List<(Control Control, int Top, int Height)> Stack(int width)
    {
        var places = new List<(Control, int, int)>();
        int y = 0;
        int height = head.GetPreferredSize(new Size(width, 0)).Height;
        places.Add((head, y, height));
        y += height;
        if (rows.Count == 0) return places;
        y += Px(10) + HeaderHeight + Px(6);
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0) y += Px(6);
            height = rows[i].GetPreferredSize(new Size(width, 0)).Height;
            places.Add((rows[i], y, height));
            y += height;
        }
        y += Px(10);
        places.Add((onScreen, y, onScreen.GetPreferredSize(new Size(width, 0)).Height));
        return places;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        var last = Stack(width)[^1];
        return new Size(width, last.Top + last.Height);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        foreach (var (control, top, height) in Stack(Width)) control.Bounds = Box(0, top, Width, height);
        Invalidate();
    }

    void Fit()
    {
        PerformLayout();
        int height = GetPreferredSize(new Size(Width, 0)).Height;
        if (Width > 0 && height != Height) HeightChanged?.Invoke();
    }

    // The column names over the lines, drawn: a screen reader hears each list's own name instead.
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (rows.Count == 0) return;
        var v = text.V1;
        var c = Grid(this, Width);
        int top = head.Bottom + Px(10);
        var font = HeaderFont;
        int height = HeaderHeight;
        foreach (var (words, x, w) in new[] { (v.AppColumn, c.NameX, c.NameW), (v.LockingTab, c.RuleX, c.RuleW), (v.AppliesWhenColumn, c.WhenX, c.WhenW) })
            Draw(this, e.Graphics, words, font, Box(x, top, w, height), Theme.Current.Subtle, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }

    // Text drawn by hand, the right way round in Arabic, and told to the render tool's checks.
    internal static void Draw(Control c, Graphics g, string words, Font font, Rectangle box, Color color, TextFormatFlags flags)
    {
        KitControl.TextDrawn?.Invoke(c, words, font, box, flags);
        if (c.RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
        if (c.RightToLeft == RightToLeft.Yes && !c.IsMirrored && (flags & (TextFormatFlags.HorizontalCenter | TextFormatFlags.Right)) == 0) flags |= TextFormatFlags.Right;
        TextRenderer.DrawText(g, words, font, box, color, flags | TextFormatFlags.NoPrefix);
    }

    // A muted line of words that wraps: italic in English for an empty list, as the app lists on the
    // Away and back tab show it. Arabic has no slanted letters.
    static TextBlock Note(string words, bool italic)
    {
        var font = Theme.Body(9f);
        if (italic && !Lang.Current.RightToLeft) font = Italic(font);
        return new TextBlock(words, font, Theme.Current.Subtle) { AccessibleName = words };
    }

    static readonly Dictionary<Font, Font> Italics = new();

    static Font Italic(Font body)
    {
        if (!Italics.TryGetValue(body, out var font)) Italics[body] = font = new Font(body, FontStyle.Italic);
        return font;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (empty.Parent is null) empty.Dispose();
            tip.Dispose();
        }
        base.Dispose(disposing);
    }
}

// One app's line in the app rules: its icon and name, its rule, when the rule applies, and a × to
// remove it.
sealed class RuleRow : Card
{
    readonly AppRulesList owner;
    readonly DropDown when;
    readonly RuleRemove remove;
    DropDown effect;
    AppRule rule;
    string name;
    // The program's own icon, or the apps glyph while it is read and when there is none; the size it
    // was asked for, and which ask it answers, so an icon read for an earlier size or app is dropped.
    Bitmap? icon;
    int iconSize;
    int iconAsks;
    bool showing;
    bool laying;

    public RuleRow(AppRulesList owner, AppRule rule, decimal normal)
    {
        this.owner = owner;
        this.rule = rule;
        var v = owner.Words.V1;
        Radius = 6;
        Fill = Theme.Current.Field;
        Outline = t => t.Line;
        AccessibleRole = AccessibleRole.Grouping;
        name = owner.NameOf(this, rule);
        Key = AppRulesList.Key(rule.App);
        effect = MakeRuleList(rule.Effect, normal);
        when = new DropDown { Small = true }
            .Add(v.OpenEvenInBackground, AppWhen.Running)
            .Add(v.OnlyWhenOnScreen, AppWhen.OnScreen);
        when.AccessibleDescription = v.OnScreenExplained;
        when.Value = rule.When;
        when.SelectedChanged += (_, _) =>
        {
            if (!showing && when.Value is AppWhen chosen) owner.Changed(this, chosen);
        };
        when.SizeChanged += (_, _) => Relayout();
        remove = new RuleRemove(() => owner.Remove(this));
        Controls.Add(effect);
        Controls.Add(when);
        Controls.Add(remove);
        effect.TabIndex = 0;
        when.TabIndex = 1;
        remove.TabIndex = 2;
        Rename();
    }

    // The app's name as the rules match it.
    public string Key { get; }

    internal AppRule Rule => rule;

    internal string AppName => name;

    internal DropDown RuleList => effect;

    internal DropDown WhenList => when;

    internal Control RemoveButton => remove;

    public void Show(AppRule rule, decimal normal)
    {
        showing = true;
        try
        {
            if (rule.App != this.rule.App || rule.Path != this.rule.Path || rule.Aumid != this.rule.Aumid)
            {
                name = owner.NameOf(this, rule);
                ForgetIcon();
            }
            this.rule = rule;
            if (!RuleMenus.Show(effect, rule.Effect) && RuleMenus.Fits(rule.Effect, battery: false)) ReplaceEffect(rule.Effect, normal);
            RuleMenus.Notes(effect, owner.Words, normal);
            when.Value = rule.When;
            Rename();
        }
        finally
        {
            showing = false;
        }
        Relayout();
        Invalidate();
    }

    internal void FocusRule() => effect.Focus();

    DropDown MakeRuleList(Effect value, decimal normal)
    {
        var list = RuleMenus.App(owner.Words, value, normal);
        list.AccessibleDescription = owner.Words.V1.AppRulesDetail;
        list.SelectedChanged += (_, _) =>
        {
            if (!showing && list.Value is Effect chosen) owner.Changed(this, chosen);
        };
        list.SizeChanged += (_, _) => Relayout();
        return list;
    }

    // A rule from the file that none of the choices is: the list is made again with it in its group.
    void ReplaceEffect(Effect value, decimal normal)
    {
        bool focused = effect.ContainsFocus;
        var list = MakeRuleList(value, normal);
        Controls.Add(list);
        Controls.SetChildIndex(list, 0);
        list.TabIndex = 0;
        var old = effect;
        effect = list;
        Controls.Remove(old);
        old.Dispose();
        Rename();
        if (focused) list.Focus();
    }

    void Rename()
    {
        var v = owner.Words.V1;
        AccessibleName = name;
        effect.AccessibleName = string.Format(v.RuleLockingName, name);
        when.AccessibleName = string.Format(v.RuleWhenName, name);
        remove.AccessibleName = string.Format(v.RemoveApp, name);
    }

    // The program's own name has been read: the line shows it, and every line's columns follow.
    internal void Renamed()
    {
        if (IsDisposed || owner.IsDisposed) return;
        string next = owner.NameOf(this, rule);
        if (next == name) return;
        name = next;
        Rename();
        owner.NamesChanged();
    }

    // The width each closed list needs for the words chosen in it: the words, both ends, the chevron
    // and the gap before it, as a small list measures them.
    internal int RuleNeed => Measure(ref ruleWords, effect.Text, Theme.Body(9f, effect.DeviceDpi)) + effect.LogicalToDeviceUnits(32);

    internal int WhenNeed => Measure(ref whenWords, when.Text, Theme.Body(9f, when.DeviceDpi)) + when.LogicalToDeviceUnits(32);

    // The width the app's icon and whole name need.
    internal int NameNeed => Px(16) + Px(8) + Measure(ref nameWords, name, Face);

    // Every line's columns come from what every line needs, asked for in each line's layout and
    // paint: each width is measured once and kept until its words or font change, so a long list
    // does not measure every line again for each of its lines.
    (string? Words, Font? Font, int Width) ruleWords, whenWords, nameWords;

    int Measure(ref (string? Words, Font? Font, int Width) kept, string words, Font font)
    {
        if (kept.Words != words || kept.Font != font)
        {
            kept = (words, font, TextRenderer.MeasureText(words, font).Width);
            owner.Measured();
        }
        return kept.Width;
    }

    // Whether the name is cut short with "…" in the room its column has; its whole name then shows
    // when the pointer rests on the line.
    internal bool NameCut { get; private set; }

    internal string NameTip => owner.NameTipOf(this);

    int ListHeight => Math.Max(effect.GetPreferredSize(Size.Empty).Height, when.GetPreferredSize(Size.Empty).Height);

    Font Face => Theme.Body(9.75f, DeviceDpi);

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        int inner = Math.Max(ListHeight, TextRenderer.MeasureText("Ag", Face).Height);
        return new Size(width, inner + Px(6) * 2 + 2);
    }

    void Relayout()
    {
        if (!laying) PerformLayout();
    }

    int Logical(int px) => (int)Math.Floor(px * 96.0 / DeviceDpi);

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (laying) return;
        laying = true;
        try
        {
            var c = owner.Grid(this, Width);
            effect.ButtonWidth = Logical(c.RuleW);
            when.ButtonWidth = Logical(c.WhenW);
            int height = ListHeight;
            int top = (Height - height) / 2;
            effect.Bounds = Box(c.RuleX, top, c.RuleW, height);
            when.Bounds = Box(c.WhenX, top, c.WhenW, height);
            remove.Bounds = Box(c.RemoveX, top, c.RemoveW, height);
            NameCut = NameNeed > c.NameW;
            owner.NameTip(this, NameCut ? name : null);
        }
        finally
        {
            laying = false;
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var t = Theme.Current;
        var c = owner.Grid(this, Width);
        int size = Px(16);
        if (icon is null || iconSize != size) AskIcon(size, t.Accent);
        if (icon is not null) e.Graphics.DrawImage(icon, Box(c.NameX, (Height - size) / 2, size, size));
        int textX = c.NameX + size + Px(8);
        AppRulesList.Draw(this, e.Graphics, name, Face, Box(textX, 0, Math.Max(1, c.NameX + c.NameW - textX), Height), t.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }

    // The program's own icon once its file is read (ProgramIcons, away from the window's thread); the
    // apps glyph until then, and for a packaged app or one with no program file on this PC.
    void AskIcon(int size, Color glyph)
    {
        ForgetIcon();
        iconSize = size;
        int ask = iconAsks;
        try
        {
            icon = owner.Icons.Icon(rule.Aumid is null ? rule.Path : null, size, this, image =>
            {
                if (ask != iconAsks)
                {
                    image.Dispose();
                    return;
                }
                icon?.Dispose();
                icon = image;
                OwnIcon = true;
                Invalidate();
            });
            OwnIcon = icon is not null;
            icon ??= AppPicker.AppsGlyph(size, glyph);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or ExternalException or IOException)
        {
            icon = null;
        }
    }

    void ForgetIcon()
    {
        iconAsks++;
        icon?.Dispose();
        icon = null;
        OwnIcon = false;
    }

    // The line shows the program's own icon, not the glyph standing in for it.
    internal bool OwnIcon { get; private set; }

    internal Bitmap? IconShown => icon;

    protected override void Dispose(bool disposing)
    {
        if (disposing) ForgetIcon();
        base.Dispose(disposing);
    }
}

// The × at the end of an app's rule: removes the rule. A stop for Tab, as the only way to it from
// the keyboard; Enter or Space presses it.
sealed class RuleRemove : KitControl
{
    readonly Action press;

    public RuleRemove(Action press)
    {
        this.press = press;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
    }

    internal void Press() => press();

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Press();
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            e.Handled = true;
            Press();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        Smooth(g);
        var (fill, ink) = ChipList.RemoveHoverColors(t);
        int side = Math.Min(Width, Height);
        var box = new RectangleF((Width - side) / 2f, (Height - side) / 2f, side, side);
        if (Hovered)
        {
            using var path = Shapes.Rounded(box, Px(4));
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
        }
        DrawText(g, Glyphs.Close, Icons(8f), Rectangle.Round(box), Hovered ? ink : t.Subtle,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        FocusRing(g, box, Px(4));
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new RemoveAccessible(this);

    protected override Automation.Provider CreateProvider() => new(this, Automation.ButtonType)
    {
        Press = Press,
    };

    sealed class RemoveAccessible(RuleRemove owner) : ControlAccessibleObject(owner)
    {
        public override string DefaultAction => Lang.Current.V1.Remove;

        public override void DoDefaultAction() => owner.Press();
    }
}
