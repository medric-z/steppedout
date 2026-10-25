namespace SteppedOut;

// The current choice and a chevron; clicking lists the others. A ComboBox cannot be drawn in the
// dark colors, so this is a button that opens a menu. Besides its choices, the menu can hold gray
// headings over groups of choices, lines between groups, and Custom… rows that open a small box
// under the list to type a time into. The arrow keys on the closed list step through the choices
// only, passing over the rest.
sealed class DropDown : KitControl
{
    // Wide enough for "30 seconds of quiet", the longest choice on the expert page in either language,
    // so the lists stacked there share both edges.
    const int MinimumWidth = 184;

    enum Kind
    {
        Choice,
        Heading,
        Line,
        Custom,
    }

    // One row of the menu. A choice that is not one of the list's own, such as a time typed with
    // Custom… or one set in the file, is Extra: it shows as itself until another takes its place.
    sealed class Entry
    {
        public required Kind Kind { get; init; }
        public string Text { get; set; } = "";
        public object? Value { get; init; }
        public CustomEntry? Custom { get; init; }
        public Action? Run { get; init; }
        public bool Extra { get; init; }
    }

    readonly List<Entry> entries = new();

    // The choices alone, in the menu's order. What the arrow keys step through and what Value is
    // one of.
    readonly List<Entry> items = new();
    int selected = -1;
    ContextMenuStrip? menu;
    CustomBox? flyout;
    bool small;
    bool lookC;
    int buttonWidth;
    int buttonHeight;

    public event EventHandler? SelectedChanged;

    // Only for a choice picked from the open list, or a time typed with Custom…, not one the arrow
    // keys step onto, so picking can move focus on (as Custom does, into its box) without the arrows
    // ever doing so.
    public event EventHandler? Picked;

    public DropDown()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.ComboBox;
        Size = new Size(MinimumWidth, 34);
    }

    public DropDown Add(string text, object value)
    {
        Append(new Entry { Kind = Kind.Choice, Text = text, Value = value });
        FitWidth();
        return this;
    }

    // A small gray line over the choices after it, such as "Lock sooner".
    public DropDown AddHeading(string text)
    {
        Append(new Entry { Kind = Kind.Heading, Text = text });
        return this;
    }

    // A line between two groups of choices.
    public DropDown AddLine()
    {
        Append(new Entry { Kind = Kind.Line });
        return this;
    }

    // A Custom… row: picking it opens a box under the list for a number, which becomes a choice of
    // its own, shown just above this row.
    public DropDown AddCustom(string text, CustomEntry custom)
    {
        Append(new Entry { Kind = Kind.Custom, Text = text, Custom = custom });
        return this;
    }

    // A row that does something rather than choose, such as "Your own sound…", which opens Windows'
    // Open window. Like a Custom… row it is not one of the choices: the arrow keys pass over it, and
    // a value shown with ShowAs goes just above it.
    public DropDown AddAction(string text, Action pressed)
    {
        Append(new Entry { Kind = Kind.Custom, Text = text, Run = pressed });
        return this;
    }

    // How a value that is not one of the choices shows, such as "always 45" set by hand in the file,
    // or null to leave it unshown. With no Unlisted (the default) such a value is not chosen, and
    // Value reads as null, as it always has.
    public Func<object, string?>? Unlisted { get; set; }

    // The small list, 26 px tall, as in a row of the settings tabs.
    public bool Small
    {
        get => small;
        set
        {
            small = value;
            FitWidth();
            Invalidate();
        }
    }

    // The settings window's list, look C of the sketch: 30 px tall, words of 13 px, 10 px inside
    // each end and between the words and the chevron, and at least 128 px wide. Off keeps the
    // welcome's list. Small wins when both are on.
    public bool LookC
    {
        get => lookC;
        set
        {
            lookC = value;
            FitWidth();
            Invalidate();
        }
    }

    // The width of the closed list in logical pixels, whatever its choices, so lists in a column
    // line up; 0 fits the longest choice. It still grows when the chosen words need more room, as
    // with Large text, rather than cut them off. The open menu is as wide as its longest row, and
    // never narrower than the list.
    public int ButtonWidth
    {
        get => buttonWidth;
        set
        {
            buttonWidth = Math.Max(0, value);
            FitWidth();
        }
    }

    // The height of the closed list in logical pixels; 0 is the usual 34, or 26 for a small one.
    // Large text makes it taller when its words need the room.
    public int ButtonHeight
    {
        get => buttonHeight;
        set
        {
            buttonHeight = Math.Max(0, value);
            FitWidth();
        }
    }

    public object? Value
    {
        get => selected >= 0 ? items[selected].Value : null;
        set
        {
            int index = items.FindIndex(item => Equals(item.Value, value));
            if (index < 0 && value is not null && Unlisted?.Invoke(value) is string text) index = ShowExtra(text, value, near: null);
            Select(index, raise: false);
        }
    }

    // Shows a value that is not one of the choices as text of its own, and chooses it, without
    // telling SelectedChanged. Another such value takes its place later.
    public void ShowAs(object value, string text)
    {
        int index = items.FindIndex(item => Equals(item.Value, value));
        if (index < 0) index = ShowExtra(text, value, near: null);
        else Relabel(value, text);
        Select(index, raise: false);
    }

    // New words for a choice that stays the same, such as "50% sooner (15 min)" when the lock time
    // changes. A screen reader hears the new words when it is the chosen one.
    public void Relabel(object value, string text)
    {
        int index = items.FindIndex(item => Equals(item.Value, value));
        if (index < 0 || items[index].Text == text) return;
        items[index].Text = text;
        if (index == selected)
        {
            string was = Text;
            Text = text;
            Invalidate();
            Automation.Notify(this, AccessibleEvents.ValueChange);
            Automation.ValueChanged(Listened, was, text);
        }
        FitWidth();
    }

    // The choices' words, in order, for checks and tests.
    internal IReadOnlyList<string> Choices => items.Select(item => item.Text).ToList();

    // What the open menu lists, in order: a choice's words, a heading's words, "—" for a line, and
    // a Custom… row's words.
    internal IReadOnlyList<string> Rows => entries.Select(entry => entry.Kind == Kind.Line ? "—" : entry.Text).ToList();

    void Append(Entry entry)
    {
        entries.Add(entry);
        if (entry.Kind == Kind.Choice) items.Add(entry);
    }

    // Puts a choice that is not one of the list's own just above the Custom… row it came from, or
    // the last Custom… row, or at the end, in place of the one shown before. Returns its place among the choices.
    int ShowExtra(string text, object value, Entry? near)
    {
        var old = entries.FirstOrDefault(entry => entry.Extra);
        if (old is not null)
        {
            // Chosen or not, the old one goes; the list chooses again right after.
            if (selected >= 0 && items[selected] == old) selected = -1;
            entries.Remove(old);
        }
        var extra = new Entry { Kind = Kind.Choice, Text = text, Value = value, Extra = true };
        // A value from elsewhere, such as the config file, goes where a typed one would.
        near ??= entries.LastOrDefault(entry => entry.Kind == Kind.Custom);
        int at = near is null ? entries.Count : entries.IndexOf(near);
        entries.Insert(at < 0 ? entries.Count : at, extra);
        var chosen = selected >= 0 ? items[selected] : null;
        items.Clear();
        items.AddRange(entries.Where(entry => entry.Kind == Kind.Choice));
        selected = chosen is null ? -1 : items.IndexOf(chosen);
        FitWidth();
        return items.IndexOf(extra);
    }

    void Select(int index, bool raise)
    {
        if (index < -1 || index >= items.Count || index == selected) return;
        string was = Text;
        selected = index;
        Text = selected >= 0 ? items[selected].Text : "";
        if (buttonWidth > 0) FitWidth();
        Invalidate();
        Automation.Notify(this, AccessibleEvents.ValueChange);
        Automation.ValueChanged(Listened, was, Text);
        if (raise) SelectedChanged?.Invoke(this, EventArgs.Empty);
    }

    void FitWidth() => Size = GetPreferredSize(Size.Empty);

    bool Snug => small || lookC;

    float FaceSize => small ? 9f : lookC ? 9.75f : 10f;

    Font Face => Body(FaceSize);

    int Inset => Px(small ? 8 : lookC ? 10 : 12);

    int ChevronWidth => Px(small ? 10 : 12);

    int ChevronGap => Px(small ? 6 : lookC ? 10 : 20);

    public override Size GetPreferredSize(Size proposedSize)
    {
        int line = TextRenderer.MeasureText("Ag", Face).Height;
        int height = Math.Max(Px(buttonHeight > 0 ? buttonHeight : small ? 26 : lookC ? 30 : 34), line + Px(Snug ? 6 : 10));
        // The room around the words: both ends, the chevron and the gap before it.
        int chrome = Snug ? Inset * 2 + ChevronWidth + ChevronGap : Px(52);
        if (buttonWidth > 0) return new Size(Math.Max(Px(buttonWidth), TextRenderer.MeasureText(Text, Face).Width + chrome), height);
        int widest = 0;
        foreach (var item in items) widest = Math.Max(widest, TextRenderer.MeasureText(item.Text, Face).Width);
        if (small) return new Size(widest + chrome, height);
        if (lookC) return new Size(Math.Max(Px(128), widest + chrome), height);
        return new Size(Math.Max(Px(MinimumWidth), widest + Px(52)), height);
    }

    protected override void OnClick(EventArgs e)
    {
        Focus();
        Open();
        base.OnClick(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down or Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Up or Keys.Left when !e.Alt:
                Select(Math.Max(0, selected - 1), raise: true);
                e.Handled = true;
                break;
            case Keys.Down or Keys.Right when !e.Alt:
                Select(Math.Min(items.Count - 1, selected + 1), raise: true);
                e.Handled = true;
                break;
            case Keys.Down or Keys.F4 or Keys.Space or Keys.Enter:
                Open();
                e.Handled = true;
                break;
        }
        base.OnKeyDown(e);
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new DropDownAccessible(this);

    // A list that is open or closed, which MSAA has no way to say to Narrator.
    protected override Automation.Provider CreateProvider() => new(this, Automation.ComboBoxType)
    {
        IsOpen = () => menu is not null,
        SetOpen = want =>
        {
            if (want && menu is null) Open();
            else if (!want) menu?.Close();
        },
        Value = () => Text,
    };

    // The menu Open shows, built but not shown, so tests and the render tool can look at it and
    // press its rows.
    internal ContextMenuStrip BuildMenu()
    {
        var menu = MenuLook.NewMenu(RightToLeft);
        // The chosen row shows by its color, as in Windows 11's own lists, not by a check mark.
        menu.ShowCheckMargin = false;
        // Sized for the list's own scaling, which may not be the system's on a second monitor.
        float menuPoints = Snug ? FaceSize : 10f;
        menu.Font = Body(menuPoints);
        menu.Padding = new Padding(Px(4));
        string? group = null;
        foreach (var entry in entries)
        {
            switch (entry.Kind)
            {
                case Kind.Heading:
                    var heading = MenuLook.Heading(entry.Text);
                    heading.Font = Body(8.25f);
                    heading.Padding = new Padding(0, Px(7), 0, Px(2));
                    menu.Items.Add(heading);
                    group = entry.Text;
                    break;
                case Kind.Line:
                    menu.Items.Add(new ToolStripSeparator());
                    group = null;
                    break;
                case Kind.Custom:
                    menu.Items.Add(Row(entry.Text, group, () => BeginInvoke(entry.Run ?? (() => OpenCustom(entry)))));
                    break;
                default:
                    var item = Row(entry.Text, group, () =>
                    {
                        Select(items.IndexOf(entry), raise: true);
                        Picked?.Invoke(this, EventArgs.Empty);
                    });
                    if (selected >= 0 && items[selected] == entry) MenuLook.Choose(item, menuPoints, DeviceDpi);
                    menu.Items.Add(item);
                    break;
            }
        }
        Widen(menu, Width);
        return menu;
    }

    // A menu is as wide as its longest words. Made wider with MinimumSize alone, its rows would stay
    // that narrow, and their fill and their clicks would stop short of the far side. A row that is
    // never shown, as wide as the room left over, widens the column of words instead, so every row
    // spans the menu. It is not shown, not read to a screen reader and not reached by the keys.
    static void Widen(ContextMenuStrip menu, int width)
    {
        menu.PerformLayout();
        int natural = menu.Width;
        if (natural < width)
        {
            var spacer = new ToolStripLabel { AutoSize = false, Available = false, Size = new Size(natural, 0) };
            menu.Items.Add(spacer);
            menu.PerformLayout();
            // What a menu adds around its column of words.
            int around = menu.Width - natural;
            spacer.Width = Math.Max(0, width - around);
            menu.PerformLayout();
        }
        menu.MinimumSize = new Size(width, 0);
    }

    // A row of the menu. One under a heading also says the heading to a screen reader, which the
    // arrow keys pass over.
    ToolStripMenuItem Row(string text, string? group, Action click)
    {
        var item = MenuLook.Item(text, click);
        item.Padding = new Padding(0, Px(5), 0, Px(5));
        if (group is not null) item.AccessibleDescription = group;
        return item;
    }

    void Open()
    {
        var menu = BuildMenu();
        menu.Closed += (_, _) =>
        {
            if (this.menu == menu)
            {
                this.menu = null;
                Automation.Notify(this, AccessibleEvents.StateChange);
                Automation.OpenChanged(Listened, false);
            }
            BeginInvoke(menu.Dispose);
        };
        this.menu = menu;
        Automation.Notify(this, AccessibleEvents.StateChange);
        Automation.OpenChanged(Listened, true);
        bool rtl = RightToLeft == RightToLeft.Yes;
        var at = new Point(rtl && !IsMirrored ? Width : 0, Height + Px(2));
        menu.Show(this, at, rtl ? ToolStripDropDownDirection.BelowLeft : ToolStripDropDownDirection.BelowRight);
    }

    // How the box a Custom… row opens is shown. Null shows it under the list and gives it the
    // keyboard; tests and the render tool put their own here, so nothing is shown or takes focus.
    internal Action<CustomBox>? Presenter { get; set; }

    // The box a Custom… row opened, while it is open.
    internal CustomBox? Flyout => flyout;

    internal void OpenCustom(int row) => OpenCustom(entries.Where(entry => entry.Kind == Kind.Custom && entry.Run is null).ElementAt(row));

    // Presses an AddAction row, as a click on it in the open menu does, for tests and the render tool.
    internal void PressAction(int row) => entries.Where(entry => entry.Run is not null).ElementAt(row).Run!();

    void OpenCustom(Entry entry)
    {
        if (flyout is not null || entry.Custom is not { } custom) return;
        int start = custom.Number?.Invoke(Value) ?? custom.Start;
        var box = new CustomBox(custom, start, RightToLeft);
        box.Committed += number => Commit(entry, custom, number);
        box.FormClosed += (_, _) =>
        {
            if (!IsDisposed && IsHandleCreated && box.ReturnFocus) Focus();
        };
        box.Disposed += (_, _) => flyout = null;
        flyout = box;
        (Presenter ?? Present)(box);
    }

    // The number typed becomes the value: one of the choices when it is one, otherwise a choice of
    // its own just above the Custom… row.
    void Commit(Entry entry, CustomEntry custom, int number)
    {
        object value = custom.Value(number);
        int index = items.FindIndex(item => Equals(item.Value, value));
        if (index < 0) index = ShowExtra(custom.Label(number), value, entry);
        Select(index, raise: true);
        Picked?.Invoke(this, EventArgs.Empty);
    }

    // Under the list, lined up with its start: the left edge, or the right in Arabic. Above it when
    // the screen ends first.
    void Present(CustomBox box)
    {
        var button = RectangleToScreen(ClientRectangle);
        var area = WorkArea.Of(Screen.FromControl(this));
        bool rtl = RightToLeft == RightToLeft.Yes;
        int x = rtl ? button.Right - box.Width : button.Left;
        int y = button.Bottom + Px(2);
        if (y + box.Height > area.Bottom) y = button.Top - Px(2) - box.Height;
        box.Location = new Point(Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - box.Width)), y);
        box.Show(FindForm());
        box.StartTyping();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        Smooth(g);
        var box = Shapes.Inside(new Rectangle(0, 0, Width, Height));
        float radius = Px(6);
        using (var path = Shapes.Rounded(box, radius))
        {
            using var brush = new SolidBrush(Hovered ? t.FieldHover : t.Field);
            g.FillPath(brush, path);
            bool ring = Focused && ShowFocusCues;
            using var pen = new Pen(ring ? t.Accent : t.FieldLine, ring ? Px(2) : 1);
            g.DrawPath(pen, path);
        }
        int chevron = ChevronWidth;
        Write(g, Text, Face, Box(Inset, 0, Width - Inset - chevron - ChevronGap, Height), t.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        DrawText(g, Glyphs.Down, Icons(small ? 7f : 8f), Box(Width - Inset - chevron, 0, chevron, Height), t.Subtle,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) flyout?.Close();
        base.Dispose(disposing);
    }

    sealed class DropDownAccessible(DropDown owner) : ControlAccessibleObject(owner)
    {
        public override string? Value => owner.Text;

        public override AccessibleStates State =>
            base.State | (owner.menu is not null ? AccessibleStates.Expanded : AccessibleStates.Collapsed);

        public override string DefaultAction => Lang.Current.OpenAction;

        public override void DoDefaultAction() => owner.Open();
    }
}

// What a Custom… row in a DropDown makes of the number typed into its box.
sealed class CustomEntry
{
    // What the box is for, as a screen reader says it, without the unit: "Wait longer".
    public required string Name { get; init; }

    public TimeUnit Unit { get; init; } = TimeUnit.Minutes;

    // What a screen reader calls the box, with {0} for Name, when the unit's usual one does not fit:
    // a percent sooner rather than a percent of full volume.
    public string? FieldName { get; init; }

    // The numbers the box takes, from the setting's own limits; a number outside them is brought to
    // the nearest end, and the line under the box says so.
    public int Shortest { get; init; } = TimeChoices.Shortest;
    public int Longest { get; init; } = TimeChoices.Longest;

    // The number the box opens with when the list's value is not one of this row's.
    public int Start { get; init; } = 15;

    // The list's value for a number: 90 under "Wait longer" is "later 90".
    public required Func<int, object> Value { get; init; }

    // How that value reads in the list: "+1 hour 30 minutes".
    public required Func<int, string> Label { get; init; }

    // The number in one of this row's values, to open the box with ("later 90" gives 90), or null
    // for any other value.
    public Func<object?, int?>? Number { get; init; }
}

// The box a Custom… row opens under its list: the number with its unit, and a line under it that
// says what it comes to. Enter, or clicking anywhere else, uses the number; Escape leaves the list
// as it was. A small window of its own rather than a menu, so the box inside it types, pastes and
// takes Arabic input as any Windows box does.
sealed class CustomBox : Form
{
    readonly MinutesField input;
    readonly Label hint;
    bool done;

    public event Action<int>? Committed;

    public CustomBox(CustomEntry entry, int start, RightToLeft direction)
    {
        var text = Lang.Current;
        var t = Theme.Current;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        RightToLeft = direction;
        RightToLeftLayout = direction == RightToLeft.Yes;
        Font = Theme.Body(10f);
        Text = entry.Name;
        AccessibleName = entry.Name;
        t.Dress(this);
        BackColor = t.Card;
        string name = string.Format(entry.FieldName ?? entry.Unit switch
        {
            TimeUnit.Seconds => text.V1.SecondsFieldName,
            TimeUnit.Pixels => text.V1.PixelsFieldName,
            TimeUnit.Percent => text.V1.PercentFieldName,
            TimeUnit.Kilobytes => text.V1.KilobytesFieldName,
            _ => text.MinutesFieldName,
        }, entry.Name);
        input = new MinutesField(name, entry.Unit, entry.Shortest, entry.Longest) { Minutes = start };
        hint = new Label { Font = Theme.Body(9f), ForeColor = t.Subtle, BackColor = Color.Transparent, UseMnemonic = false, AutoSize = false };
        hint.Text = input.Hint;
        input.HintChanged += (_, _) =>
        {
            hint.Text = input.Hint;
            PerformLayout();
        };
        Controls.Add(input);
        Controls.Add(hint);
        HandleCreated += (_, _) => Corners(Handle);
        PerformLayout();
    }

    public MinutesField Field => input;

    // The number the box holds now: what was typed, brought into range, or the last good one.
    public int Number => (int)input.Minutes;

    // Whether closing hands the keyboard back to the list; not when a click elsewhere closed it.
    internal bool ReturnFocus { get; private set; } = true;

    public void StartTyping() => input.StartTyping();

    int Px(int logical) => LogicalToDeviceUnits(logical);

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (input is null || hint is null) return;
        int pad = Px(12);
        var size = input.GetPreferredSize(Size.Empty);
        int width = Math.Max(size.Width, Px(220));
        int hintHeight = Shapes.WrappedHeight(hint.Text, hint.Font, width);
        bool flip = RightToLeft == RightToLeft.Yes && !IsMirrored;
        Rectangle Box(int x, int y, int w, int h) => new(flip ? width + pad * 2 - x - w : x, y, w, h);
        input.Bounds = Box(pad, pad, size.Width, size.Height);
        hint.Bounds = Box(pad, pad + size.Height + Px(6), width, hintHeight);
        ClientSize = new Size(width + pad * 2, pad + size.Height + Px(6) + hintHeight + pad);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        using var pen = new Pen(Theme.Current.Edge);
        e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    // Enter uses the number; Escape leaves the list as it was. True when the key was one of them.
    internal bool Key(Keys key)
    {
        switch (key)
        {
            case Keys.Enter:
                Commit();
                return true;
            case Keys.Escape:
                Cancel();
                return true;
        }
        return false;
    }

    protected override bool ProcessDialogKey(Keys keyData) => Key(keyData) || base.ProcessDialogKey(keyData);

    // Clicking anywhere else uses the number too, as leaving any box in the settings does.
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        ClickedAway();
    }

    internal void ClickedAway()
    {
        ReturnFocus = false;
        Commit();
    }

    internal void Commit()
    {
        if (done) return;
        done = true;
        Committed?.Invoke(Number);
        Finish();
    }

    internal void Cancel()
    {
        if (done) return;
        done = true;
        Finish();
    }

    void Finish()
    {
        if (IsHandleCreated) BeginInvoke(Close);
        else Close();
    }

    static void Corners(IntPtr window)
    {
        int corner = 2;
        DwmSetWindowAttribute(window, 33, ref corner, sizeof(int));
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
