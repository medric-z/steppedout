namespace SteppedOut;

// The apps in a list such as Open apps, each a small outlined chip with its name and an ×. The chips
// wrap onto more lines when they do not fit. Tab lands on one chip and the arrow keys move between
// them; Delete or Backspace removes the one with focus, and focus moves to the chip after it, the one
// before it, or the list itself once it is empty. The × is a button a screen reader can press. An
// empty list says so in gray italics.
sealed class ChipList : PartGroup
{
    string emptyText = Lang.Current.V1.NoAppsAdded;
    string removeName = Lang.Current.V1.RemoveApp;

    // A chip removed by a key, a click on its × or a screen reader; never by Remove or SetItems.
    public event EventHandler<ChipRemovedEventArgs>? Removed;

    public ChipList()
    {
        // So focus has somewhere to go when the last chip goes; Tab itself never stops here.
        SetStyle(ControlStyles.Selectable, true);
        TabStop = false;
        AccessibleRole = AccessibleRole.List;
    }

    // What the list says while it is empty.
    public string EmptyText
    {
        get => emptyText;
        set
        {
            emptyText = value;
            Invalidate();
        }
    }

    // What a chip's × is called, with {0} for the chip's words: "Remove {0}".
    public string RemoveName
    {
        get => removeName;
        set => removeName = value;
    }

    // Where focus goes once the last chip is removed, such as the row's Add app button. Null keeps it
    // on the list.
    public Control? FocusWhenEmpty { get; set; }

    public IReadOnlyList<string> Texts => Parts.Select(part => part.Text).ToList();

    public IReadOnlyList<object?> Values => Parts.Cast<Chip>().Select(chip => chip.Value).ToList();

    public void Add(string text, object? value = null)
    {
        AddPart(new Chip(text, value ?? text));
        Fit();
    }

    public void Remove(int index)
    {
        if (index < 0 || index >= Parts.Count) return;
        var chip = Parts[index];
        RemovePart(chip);
        chip.Dispose();
        Fit();
    }

    // The chips the list holds now, in order, from the settings. Raises nothing.
    public void SetItems(IEnumerable<(string Text, object? Value)> items)
    {
        var wanted = items.Select(item => (item.Text, Value: item.Value ?? item.Text)).ToList();
        if (wanted.SequenceEqual(Parts.Cast<Chip>().Select(chip => (chip.Text, Value: chip.Value ?? chip.Text)))) return;
        int keep = Current;
        bool hadFocus = ContainsFocus;
        while (Parts.Count > 0) Remove(Parts.Count - 1);
        foreach (var (text, value) in wanted) AddPart(new Chip(text, value));
        Fit();
        if (Parts.Count > 0) SetCurrent(Math.Clamp(keep, 0, Parts.Count - 1));
        if (hadFocus) FocusAfterRemoval();
    }

    // Removes a chip the way a person does, with its focus moving on, and tells Removed.
    internal void RemoveByUser(Part chip)
    {
        int index = IndexOf(chip);
        if (index < 0) return;
        bool hadFocus = chip.ContainsFocus;
        string text = chip.Text;
        object? value = ((Chip)chip).Value;
        // Focus moves before the chip goes, so it never falls back to the window: to the chip after
        // it, the one before it, or, when it is the last, where an empty list sends it.
        int next = index + 1 < Parts.Count ? index + 1 : index - 1;
        if (next >= 0) SetCurrent(next);
        var target = next >= 0 ? Parts[next] : EmptyFocus;
        FocusedAfterRemoving = target;
        if (hadFocus && target.CanFocus) target.Focus();
        RemovePart(chip);
        if (IsHandleCreated) BeginInvoke(chip.Dispose);
        else chip.Dispose();
        Fit();
        Removed?.Invoke(this, new ChipRemovedEventArgs(index, text, value));
    }

    // Where focus goes when a chip with focus is removed: the chip that took its place as the one
    // Tab lands on, or, once the list is empty, FocusWhenEmpty or the list itself.
    internal Control FocusAfter => Current >= 0 && Current < Parts.Count ? Parts[Current] : EmptyFocus;

    Control EmptyFocus => (Control?)FocusWhenEmpty ?? this;

    // Where the last removal by a person sent focus, or would have if the chip had it.
    internal Control? FocusedAfterRemoving { get; private set; }

    void FocusAfterRemoval()
    {
        var target = FocusAfter;
        if (target.CanFocus) target.Focus();
    }

    // The fill under the pointer on a chip's × and the × on it: a gray fill under the text color.
    // High contrast gives edges and text the same color, which would hide the ×, so there it takes
    // the highlight colors, as a selected item does.
    internal static (Color Fill, Color Ink) RemoveHoverColors(Theme t) =>
        t.Line.ToArgb() == t.Text.ToArgb() ? (t.Accent, t.OnAccent) : (t.Line, t.Text);

    // The chip Tab lands on is drawn so; to a screen reader the chips are plain items of a list,
    // since nothing is chosen among them, and the one with the keyboard is the one it reads (I35).
    protected override void CurrentChanged(int was, int now)
    {
        if (was >= 0 && was < Parts.Count) Parts[was].Invalidate();
        if (now >= 0 && now < Parts.Count) Parts[now].Invalidate();
    }

    internal override bool Key(Part from, Keys key)
    {
        if (key is Keys.Delete or Keys.Back)
        {
            RemoveByUser(from);
            return true;
        }
        // Nothing to choose on a chip.
        if (key is Keys.Enter or Keys.Space) return false;
        return base.Key(from, key);
    }

    // So the row it sits in makes room when the chips need another line, or one less.
    void Fit()
    {
        if (Width > 0) Height = GetPreferredSize(new Size(Width, 0)).Height;
        Parent?.PerformLayout();
        PerformLayout();
        Invalidate();
    }

    internal Font Face => Body(9f);

    // Italic in English only: Arabic has no slanted letters, and a slant made up from the upright
    // ones smears them.
    internal Font EmptyFace => Lang.Current.RightToLeft ? Body(9f) : Italic(Body(9f));

    int Gap => Px(5);

    // Where each chip goes at a width: left to right, a new line when the next does not fit.
    List<Rectangle> Flow(int width)
    {
        var places = new List<Rectangle>();
        int x = 0, y = 0;
        foreach (Chip chip in Parts)
        {
            var size = chip.Wanted;
            if (x > 0 && x + size.Width > width)
            {
                x = 0;
                y += size.Height + Gap;
            }
            places.Add(new Rectangle(x, y, Math.Min(size.Width, Math.Max(1, width)), size.Height));
            x += size.Width + Gap;
        }
        return places;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        if (Parts.Count == 0) return new Size(width, Shapes.Measure(emptyText, EmptyFace, Math.Max(1, width)).Height);
        var places = Flow(width);
        return new Size(width, places.Max(place => place.Bottom));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var places = Flow(Width);
        for (int i = 0; i < Parts.Count; i++) Parts[i].Bounds = Place(places[i].X, places[i].Y, places[i].Width, places[i].Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Parts.Count > 0) return;
        var t = Theme.Current;
        Write(e.Graphics, emptyText, EmptyFace, ClientRectangle, t.Subtle, TextFormatFlags.WordBreak);
        if (base.Focused && ShowFocusCues)
        {
            var size = Shapes.Measure(emptyText, EmptyFace, Width);
            using var pen = new Pen(t.Text) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            e.Graphics.DrawRectangle(pen, Box(0, 0, Math.Min(Width, size.Width) - 1, size.Height - 1));
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        // With chips in it, the list hands focus on to the current one.
        if (Parts.Count > 0 && Current >= 0 && Parts[Current].CanFocus) Parts[Current].Focus();
        base.OnGotFocus(e);
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new ListAccessible(this);

    protected override Automation.Provider CreateProvider() => new(this, Automation.ListType);

    // Italic text is only used here, for an empty list: the italic of each shared body font, so it
    // follows the language's font and the text size as the body font does.
    static readonly Dictionary<Font, Font> Italics = new();

    static Font Italic(Font body)
    {
        if (!Italics.TryGetValue(body, out var font)) Italics[body] = font = new Font(body, FontStyle.Italic);
        return font;
    }

    sealed class ListAccessible(ChipList owner) : ControlAccessibleObject(owner)
    {
        // An empty list says that it is empty.
        public override string? Value => owner.Parts.Count == 0 ? owner.emptyText : null;
    }

    sealed class Chip : Part
    {
        readonly ChipRemove remove;

        public Chip(string text, object? value)
        {
            Text = text;
            Value = value;
            AccessibleName = text;
            AccessibleRole = AccessibleRole.ListItem;
            remove = new ChipRemove(this);
            Controls.Add(remove);
        }

        public object? Value { get; }

        internal ChipList? List => Parent as ChipList;

        internal override bool IsChosen => List is { } list && list.Current == list.IndexOf(this);

        Font Face => Body(9f);

        int RemoveWidth => Px(18);

        public override Size GetPreferredSize(Size proposedSize)
        {
            var text = Shapes.Measure(Text, Face);
            return new Size(Px(8) + text.Width + Px(2) + RemoveWidth + Px(2), Math.Max(Px(22), text.Height + Px(4)));
        }

        internal Size Wanted => GetPreferredSize(Size.Empty);

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            remove.Bounds = Box(Width - RemoveWidth - Px(2), Px(2), RemoveWidth, Height - Px(4));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            Smooth(g);
            using (var path = Shapes.Rounded(Shapes.Inside(ClientRectangle), Px(6)))
            {
                using var brush = new SolidBrush(Hovered ? t.FieldHover : t.Field);
                g.FillPath(brush, path);
                using var pen = new Pen(t.FieldLine);
                g.DrawPath(pen, path);
            }
            Write(g, Text, Face, Box(Px(8), 0, Width - Px(8) - RemoveWidth - Px(4), Height), t.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            FocusRing(g, new RectangleF(0, 0, Width, Height), Px(6));
        }

        protected override Automation.Provider CreateProvider() => new(this, Automation.ListItemType);
    }

    // The × on a chip: removes it. Not a stop for Tab, since Delete does the same from the chip, but
    // a button a screen reader can find and press.
    sealed class ChipRemove : KitControl
    {
        readonly Chip chip;

        public ChipRemove(Chip chip)
        {
            this.chip = chip;
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
        }

        // "Remove Notepad".
        internal string RemoveText => string.Format(chip.List?.RemoveName ?? Lang.Current.V1.RemoveApp, chip.Text);

        internal void Press() => chip.List?.RemoveByUser(chip);

        protected override void OnClick(EventArgs e)
        {
            Press();
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            Smooth(g);
            var (fill, ink) = RemoveHoverColors(t);
            if (Hovered)
            {
                using var path = Shapes.Rounded(new RectangleF(0, 0, Width, Height), Px(4));
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
            }
            DrawText(g, Glyphs.Close, Icons(7f), ClientRectangle, Hovered ? ink : t.Subtle,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new RemoveAccessible(this);

        protected override Automation.Provider CreateProvider() => new(this, Automation.ButtonType)
        {
            Press = Press,
        };

        sealed class RemoveAccessible(ChipRemove owner) : ControlAccessibleObject(owner)
        {
            public override string? Name => owner.RemoveText;

            public override string DefaultAction => Lang.Current.V1.Remove;

            public override void DoDefaultAction() => owner.Press();
        }
    }
}

// Which chip a person removed: its place in the list before it went, its words and its value.
sealed class ChipRemovedEventArgs(int index, string text, object? value) : EventArgs
{
    public int Index { get; } = index;
    public string Text { get; } = text;
    public object? Value { get; } = value;
}
