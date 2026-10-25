namespace SteppedOut;

// The Locking tab: what the battery does to the lock time, what happens at the lock time (the
// lock, the lock and the screens off, the lock and sleep, or no lock), whether a controller counts
// as using the PC, the rules for single apps, and at the bottom the lock shortcut (with a line under
// it when another app has it or it types a letter) and the Why window. It never changes the
// settings itself: each change goes out through Edited, and Refill shows the settings as they are
// now without raising anything. On a PC with no battery the battery's row is left out; its setting
// stays as it was in the file and still applies if a battery shows up later.
sealed class LockingTab : FlatTab
{
    // "Don't lock" in the At the lock time list: lockWhenAway off, with whenLocking kept as it is
    // for when locking is turned on again.
    internal static readonly object DontLock = new();

    // The shortcut list's last row, "Choose another shortcut…": it opens the shortcut window and is
    // never a value of its own.
    static readonly object ChooseAnother = new();

    readonly ISettingsHost host;
    FlatPanel? panel;
    FlatRow? batteryRow;
    DropDown? battery;
    DropDown? atLockTime;
    Toggle? countController;
    AppRulesList? rules;
    Heading? head;
    DropDown? shortcut;
    ShortcutNote? note;
    SettingRow? shortcutRow;
    Action<Func<Config, Config>> edit = _ => { };

    // The lock time the "sooner" notes are worked out from, as set in the settings.
    decimal normal = LockTime.NormalMinutes(Config.DefaultIdleMinutes);

    // The rules and the lock time the rules list shows, so showing the same again does no work.
    List<AppRule>? rulesShown;
    decimal rulesNormal;

    // "Choose another shortcut…" was just chosen in the list. Picking it from the open list opens the
    // window; stepping onto it with the arrow keys only puts the list back.
    bool choosing;

    public LockingTab(ISettingsHost host)
    {
        this.host = host;
    }

    // The shortcut window: the page, the shortcut in use, and back the one saved there, or null when
    // nothing changed. The window registers the new one through the host before it closes. The real
    // window unless a test gives its own.
    internal Func<Control, string, string?>? ChooseShortcut { get; init; }

    // The Why window for the page. The real window unless a test gives its own.
    internal Action<Control>? ShowWhy { get; init; }

    // Runs something once the list is done with the click it is in. BeginInvoke on the page unless a
    // test gives its own.
    internal Action<Action>? Later { get; init; }

    // For tests and the render tool.
    internal DropDown? Battery => battery;
    internal DropDown? AtLockTime => atLockTime;
    internal Toggle? CountController => countController;
    internal AppRulesList? Rules => rules;
    internal Heading? Head => head;
    internal DropDown? ShortcutList => shortcut;

    // What the At the lock time list shows for these settings: Don't lock whenever locking is off.
    internal static object AtLockTimeOf(Config config) =>
        !config.LockWhenAway ? DontLock : Enum.IsDefined(config.WhenLocking) ? config.WhenLocking : WhenLocking.Lock;

    protected override void Fill(FlatPanel page, Action<Func<Config, Config>> edit)
    {
        var v = Text.V1;
        var config = Current;
        this.edit = edit;
        panel = page;
        normal = LockTime.NormalMinutes(config.IdleMinutes);
        bool hasBattery = WelcomeForm.HasBattery();
        battery = hasBattery ? BatteryList(config.OnBattery) : null;
        batteryRow = battery is null ? null : new FlatRow(v.OnBattery, v.OnBatteryDetail, battery);
        var atLockTime = this.atLockTime = new DropDown { LookC = true, Name = "atLockTime" }
            .Add(v.LockOnly, WhenLocking.Lock)
            .Add(v.LockScreenOff, WhenLocking.LockScreenOff)
            .Add(v.LockSleep, WhenLocking.LockSleep)
            .Add(v.DontLock, DontLock);
        var countController = this.countController = Switch("countController");
        var rules = this.rules = new AppRulesList(Text, ChangeRules);
        rules.Show(config.AppRules, normal);
        rulesShown = config.AppRules.ToList();
        rulesNormal = normal;
        var shortcut = this.shortcut = new DropDown
        {
            Small = true,
            ButtonWidth = 1,
            Name = "shortcut",
            Unlisted = value => value is string s && Shortcut.TryParse(s, out var custom) ? custom.Display : null,
        }
            .Add(v.CtrlAltL, Shortcut.Default.ToString())
            .Add(v.Off, "")
            .Add(v.ChooseAnotherShortcut, ChooseAnother);
        var note = this.note = new ShortcutNote { Name = "shortcutNote" };
        var why = new KitButton(v.Show) { Small = true, Name = "why" };
        head = new Heading(Glyphs.Lock, v.LockingTab, v.LockingIntro);
        var shortcutRow = this.shortcutRow = new FlatRow(v.Shortcut, v.ShortcutDetail, shortcut) { Below = note };

        page.Add(head);
        if (batteryRow is not null) page.Add(batteryRow, lineAbove: true);
        page.Add(new FlatRow(v.AtLockTime, v.AtLockTimeDetail, atLockTime), lineAbove: batteryRow is null)
            .Add(new FlatRow(v.CountController, v.CountControllerDetail, countController))
            .Add(rules)
            .Add(shortcutRow, lineAbove: true)
            .Add(new FlatRow(v.WhyNotLocking, v.WhyNotLockingDetail, why));
        // The page grows and shrinks with its rules; whoever holds it lays it out again.
        rules.HeightChanged += Fit;

        // One list for two settings: Don't lock turns locking off and keeps what comes after the lock
        // for when it is on again; any other choice turns locking on with that.
        atLockTime.SelectedChanged += (_, _) =>
        {
            object? value = atLockTime.Value;
            if (value is WhenLocking chosen)
            {
                edit(c =>
                {
                    c.LockWhenAway = true;
                    c.WhenLocking = chosen;
                    return c;
                });
            }
            else if (ReferenceEquals(value, DontLock))
            {
                edit(c =>
                {
                    c.LockWhenAway = false;
                    return c;
                });
            }
        };
        countController.CheckedChanged += (_, _) =>
        {
            bool on = countController.Checked;
            edit(c =>
            {
                c.CountController = on;
                return c;
            });
        };
        why.Click += (_, _) => (ShowWhy ?? Why)(page);

        shortcut.SelectedChanged += (_, _) =>
        {
            if (ReferenceEquals(shortcut.Value, ChooseAnother))
            {
                // Never left showing: the list goes back to the shortcut in use at once, without telling
                // anyone. Picked comes next, in the same click, only when it was picked from the list.
                shortcut.Value = Shortcut.Normalize(Current.LockShortcut);
                choosing = true;
                Post(page, () => choosing = false);
                return;
            }
            if (shortcut.Value is string chosen) Use(chosen, edit);
        };
        shortcut.Picked += (_, _) =>
        {
            if (!choosing) return;
            choosing = false;
            Post(page, () =>
            {
                if (page.IsDisposed || Built != page) return;
                Another(page, edit);
            });
        };

        void Changed()
        {
            if (Built == page) ShowNote(Current);
        }
        host.ShortcutResultChanged += Changed;
        page.Disposed += (_, _) => host.ShortcutResultChanged -= Changed;
    }

    protected override void Show(Config config)
    {
        if (panel is null || atLockTime is null || countController is null || rules is null || shortcut is null) return;
        normal = LockTime.NormalMinutes(config.IdleMinutes);
        if (battery is not null && batteryRow is not null)
        {
            if (!RuleMenus.Show(battery, config.OnBattery) && RuleMenus.Fits(config.OnBattery, battery: true)) ReplaceBattery(config.OnBattery);
            RuleMenus.Notes(battery, Text, normal);
            batteryRow.PerformLayout();
        }
        atLockTime.Value = AtLockTimeOf(config);
        countController.Checked = config.CountController;
        if (rulesShown is null || normal != rulesNormal || !rulesShown.SequenceEqual(config.AppRules))
        {
            rules.Show(config.AppRules, normal);
            rulesShown = config.AppRules.ToList();
            rulesNormal = normal;
        }
        shortcut.Value = Shortcut.Normalize(config.LockShortcut);
        ShowNote(config);
    }

    DropDown BatteryList(Effect value)
    {
        var list = RuleMenus.Battery(Text, value, normal);
        list.SelectedChanged += (_, _) =>
        {
            if (list.Value is not Effect chosen) return;
            edit(c =>
            {
                c.OnBattery = chosen;
                return c;
            });
        };
        return list;
    }

    // A value from the file that none of the battery's choices is, such as "sooner 40": the list is
    // made again with it in its own group, where a time typed with Custom… would also go.
    void ReplaceBattery(Effect value)
    {
        if (panel is null || batteryRow is null || battery is null) return;
        bool focused = battery.ContainsFocus;
        var list = BatteryList(value);
        var row = new FlatRow(Text.V1.OnBattery, Text.V1.OnBatteryDetail, list);
        int at = panel.Controls.IndexOf(batteryRow);
        panel.Add(row, lineAbove: true);
        panel.Controls.SetChildIndex(row, at);
        row.TabIndex = batteryRow.TabIndex;
        var old = batteryRow;
        panel.Controls.Remove(old);
        old.Dispose();
        battery = list;
        batteryRow = row;
        panel.PerformLayout();
        if (focused) list.Focus();
    }

    void ChangeRules(Func<List<AppRule>, List<AppRule>> change) =>
        edit(c =>
        {
            c.AppRules = change(c.AppRules);
            return c;
        });

    void Fit()
    {
        if (panel is null) return;
        panel.PerformLayout();
        if (panel.Width > 0) panel.Height = panel.GetPreferredSize(new Size(panel.Width, 0)).Height;
        panel.Parent?.PerformLayout();
    }

    protected override void Forget()
    {
        panel = null;
        rulesShown = null;
    }

    // Ctrl+Alt+L, Off, or a shortcut set before: registered with the app at once, so the line under
    // the list does not wait for the save, then kept.
    void Use(string chosen, Action<Func<Config, Config>> edit)
    {
        if (chosen == Shortcut.Normalize(Current.LockShortcut)) return;
        host.ShortcutChanged(chosen);
        edit(c => { c.LockShortcut = chosen; return c; });
    }

    // "Choose another shortcut…": the window registers the one pressed through the host, and the
    // page keeps it.
    void Another(Control page, Action<Func<Config, Config>> edit)
    {
        string now = Shortcut.Normalize(Current.LockShortcut);
        string? picked = (ChooseShortcut ?? Dialog)(page, now);
        if (picked is null) return;
        picked = Shortcut.Normalize(picked);
        if (picked.Length == 0 || picked == now) return;
        edit(c => { c.LockShortcut = picked; return c; });
    }

    string? Dialog(Control page, string now)
    {
        using var dialog = new ShortcutDialog(host, now, Text);
        return dialog.ShowDialog(page.FindForm()) == DialogResult.OK ? dialog.Chosen : null;
    }

    void Why(Control page)
    {
        using var dialog = new WhyDialog(host, () => Current.LockWhenAway, Text);
        dialog.ShowDialog(page.FindForm());
    }

    void Post(Control page, Action action)
    {
        if (Later is { } later) later(action);
        else if (page.IsHandleCreated && !page.IsDisposed) page.BeginInvoke(action);
        else action();
    }

    // The line under the shortcut list: shown when another app has the shortcut, when it types a
    // letter on one of the keyboards, or when the file holds one that cannot work.
    void ShowNote(Config config)
    {
        if (note is null) return;
        string? words = NoteFor(host.ShortcutResult, Shortcut.Normalize(config.LockShortcut), config.LockShortcut, Text);
        bool show = words is not null;
        if (show == note.Wanted && (words ?? "") == note.Text) return;
        note.Show(words);
        shortcutRow?.PerformLayout();
        shortcutRow?.Parent?.PerformLayout();
        shortcutRow?.Parent?.Parent?.PerformLayout();
    }

    internal static string? NoteFor(HotKeyResult result, string shortcut, string? saved, Strings text)
    {
        var v = text.V1;
        if (shortcut.Length == 0)
        {
            // A shortcut in the file that is not one: it is off, and the line says what works.
            return string.IsNullOrWhiteSpace(saved) ? null : v.ShortcutInvalid;
        }
        string shown = Shortcut.Parse(shortcut)?.Display ?? shortcut;
        return result switch
        {
            HotKeyResult.InUse => string.Format(v.ShortcutRowInUse, shown),
            HotKeyResult.TypesALetter => string.Format(v.ShortcutRowTypesALetter, shown),
            HotKeyResult.Invalid => v.ShortcutInvalid,
            _ => null,
        };
    }

    // The head of the page: its icon in the accent color, level with its name in semibold, and what
    // the page is for under the name. Screen readers hear a heading.
    internal sealed class Heading : KitControl
    {
        readonly string glyph;
        readonly string title;
        readonly string detail;

        public Heading(string glyph, string title, string detail)
        {
            this.glyph = glyph;
            this.title = title;
            this.detail = detail;
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Text = title;
            AccessibleName = title;
            AccessibleDescription = detail;
            AccessibleRole = AccessibleRole.StaticText;
        }

        public string Title => title;

        public string Detail => detail;

        internal Font TitleFont => Strong(9.75f);

        Font DetailFont => Body(9f);

        internal Font GlyphFont => Icons(15f);

        // The icon's own size, as it is drawn: no padding around it.
        internal Size GlyphSize => TextRenderer.MeasureText(glyph, GlyphFont, Size.Empty,
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        int GlyphWidth => Math.Max(Px(20), GlyphSize.Width);

        int TextLeft => GlyphWidth + Px(10);

        int TextWidth(int width) => Math.Max(1, width - TextLeft);

        int TitleHeight(int width) => Shapes.WrappedHeight(title, TitleFont, TextWidth(width));

        // The icon's box is at least as big as the icon itself, so nothing of it is cut, and it
        // grows with Large text. The icon and the name's first line share one line, each centred on it.
        int GlyphHeight => Math.Max(Px(20), GlyphSize.Height);

        int NameLine => Shapes.Measure(title, TitleFont).Height;

        int FirstLine => Math.Max(GlyphHeight, NameLine);

        int GlyphTop => (FirstLine - GlyphHeight) / 2;

        int TitleTop => (FirstLine - NameLine) / 2;

        int DetailTop(int width) => TitleTop + TitleHeight(width) + Px(1);

        int DetailHeight(int width) => Shapes.WrappedHeight(detail, DetailFont, TextWidth(width));

        // Where the icon and the name are drawn, from the start of the line.
        internal Rectangle GlyphBox => Box(0, GlyphTop, GlyphWidth, GlyphHeight);

        internal Rectangle TitleBox => Box(TextLeft, TitleTop, TextWidth(Width), TitleHeight(Width));

        internal string Glyph => glyph;

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
            return new Size(width, Math.Max(Math.Max(DetailTop(width) + DetailHeight(width), GlyphTop + GlyphHeight), Px(22)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            Write(g, glyph, GlyphFont, GlyphBox, t.Accent,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            Write(g, title, TitleFont, TitleBox, t.Text, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            Write(g, detail, DetailFont, Box(TextLeft, DetailTop(Width), TextWidth(Width), DetailHeight(Width)), t.Subtle,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }

        protected override Automation.Provider CreateProvider() => new(this, Automation.TextType) { HeadingLevel = 2 };
    }
}

// The grouped lists the Locking tab uses for the battery and for each app's rule: Lock as usual or
// Never lock first, then Wait longer, Lock sooner with the time each comes to, and Fixed time, which
// overrides Never lock. A value outside the lists, from Custom… or from the file, shows as itself
// in its own group.
static class RuleMenus
{
    static readonly int[] Sooner = [25, 50, 75];
    static readonly int[] Always = [2, 5, 10, 15, 30, 60];

    // The battery's list: Lock as usual, Lock sooner, Fixed time.
    public static DropDown Battery(Strings text, Effect value, decimal normal)
    {
        var v = text.V1;
        var list = new DropDown { Small = true, ButtonWidth = 240 };
        var values = new List<Effect>();
        Add(list, values, v.LockAsUsual, Effect.None);
        Sooners(list, values, text, value);
        Caps(list, values, text, value);
        list.Tag = values;
        list.Value = value;
        Notes(list, text, normal);
        return list;
    }

    // An app's list: Never lock, Wait longer, Lock sooner, Fixed time. Never "none": a rule always
    // does something, and the settings drop a rule that does nothing.
    public static DropDown App(Strings text, Effect value, decimal normal)
    {
        var v = text.V1;
        var list = new DropDown { Small = true };
        var values = new List<Effect>();
        Add(list, values, v.NeverLock, Effect.Never);
        list.AddHeading(v.WaitLongerHeading);
        foreach (int minutes in Amounts(TimeChoices.Extras, value, EffectKind.Extra)) Add(list, values, text.ExtraLabel(minutes), Effect.Extra(minutes));
        list.AddCustom(v.Custom, new CustomEntry
        {
            Name = v.WaitLongerHeading,
            Shortest = Config.MinEffectMinutes,
            Longest = Config.MaxEffectMinutes,
            Value = minutes => Effect.Extra(minutes),
            Label = text.ExtraLabel,
            Number = Number(EffectKind.Extra),
        });
        Sooners(list, values, text, value);
        Caps(list, values, text, value);
        list.Tag = values;
        list.Value = value;
        Notes(list, text, normal);
        return list;
    }

    // Whether a list of this kind can show the value: one of its choices, or one it makes room for.
    public static bool Fits(Effect value, bool battery) => battery
        ? value.Kind is EffectKind.None or EffectKind.Sooner or EffectKind.Cap
        : value.Kind is EffectKind.Never or EffectKind.Extra or EffectKind.Sooner or EffectKind.Cap;

    // Chooses the value without telling anyone. False when the list has no such choice, so the
    // caller makes the list again with it.
    public static bool Show(DropDown list, Effect value)
    {
        list.Value = value;
        return Equals(list.Value, value);
    }

    // "50% sooner (15 min)": each "sooner" choice with the lock time it comes to, worked out from the
    // lock time set, for when the lock time changes.
    public static void Notes(DropDown list, Strings text, decimal normal)
    {
        if (list.Tag is not List<Effect> values) return;
        foreach (var effect in values)
        {
            if (effect.Kind != EffectKind.Sooner) continue;
            var time = LockTime.Compute(normal, [new ActiveEffect(effect, EffectSource.Battery)]);
            list.Relabel(effect, text.SoonerLabel(effect.Amount) + " " + text.PercentNote(time.Total ?? normal));
        }
    }

    static void Sooners(DropDown list, List<Effect> values, Strings text, Effect value)
    {
        list.AddHeading(text.V1.LockSoonerHeading);
        foreach (int percent in Amounts(Sooner, value, EffectKind.Sooner)) Add(list, values, text.SoonerLabel(percent), Effect.Sooner(percent));
    }

    static void Caps(DropDown list, List<Effect> values, Strings text, Effect value)
    {
        var v = text.V1;
        list.AddHeading(v.FixedTimeHeading);
        foreach (int minutes in Amounts(Always, value, EffectKind.Cap)) Add(list, values, text.AlwaysAfterLabel(minutes), Effect.Cap(minutes));
        list.AddCustom(v.Custom, new CustomEntry
        {
            Name = v.AlwaysLockAfter,
            Shortest = Config.MinEffectMinutes,
            Longest = Config.MaxEffectMinutes,
            Value = minutes => Effect.Cap(minutes),
            Label = text.AlwaysAfterLabel,
            Number = Number(EffectKind.Cap),
        });
    }

    static void Add(DropDown list, List<Effect> values, string label, Effect effect)
    {
        list.Add(label, effect);
        values.Add(effect);
    }

    // The amounts a group lists, with the value's own amount last when it is of the group's kind and
    // not one of them: just above the group's Custom…, where a time typed there goes too.
    static IEnumerable<int> Amounts(int[] listed, Effect value, EffectKind kind) =>
        value.Kind == kind && !listed.Contains(value.Amount) ? listed.Append(value.Amount) : listed;

    // The number in a value of this kind, for the Custom… box to open with.
    static Func<object?, int?> Number(EffectKind kind) => value => value is Effect effect && effect.Kind == kind ? effect.Amount : null;
}

// The line under the lock shortcut's list when the shortcut does not work: a small icon and why,
// in the warning color. Screen readers hear it with the list.
sealed class ShortcutNote : KitControl
{
    public ShortcutNote()
    {
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        AccessibleRole = AccessibleRole.StaticText;
        Visible = false;
    }

    // Whether the line is meant to show, whatever its parents.
    public bool Wanted { get; private set; }

    public void Show(string? words)
    {
        Wanted = words is not null;
        Text = words ?? "";
        AccessibleName = words;
        Visible = Wanted;
        Invalidate();
    }

    Font Face => Body(9f);

    Font GlyphFont => Icons(8.25f);

    // The icon's box, which grows with the text size, and the gap after it.
    int GlyphBox => Math.Max(Px(12), TextRenderer.MeasureText(Glyphs.Info, GlyphFont, Size.Empty,
        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width);

    int GlyphWidth => GlyphBox + Px(4);

    int TextWidth(int width) => Math.Max(1, width - GlyphWidth);

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        return new Size(width, Math.Max(Px(16), Shapes.WrappedHeight(Text, Face, TextWidth(width))));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        int line = Math.Max(Shapes.Measure("Ag", Face).Height, TextRenderer.MeasureText(Glyphs.Info, GlyphFont, Size.Empty,
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Height);
        Write(g, Glyphs.Info, GlyphFont, Box(0, 0, GlyphBox, line), t.Warning,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        Write(g, Text, Face, Box(GlyphWidth, 0, TextWidth(Width), Height), t.Warning, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
    }
}
