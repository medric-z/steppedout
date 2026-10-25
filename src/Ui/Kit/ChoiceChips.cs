namespace SteppedOut;

// A few choices drawn as pills in a row that wraps onto more lines, each picked with one click:
// "5 minutes", "15 minutes", "Custom…". One is chosen at a time and filled in the accent color. A
// Custom… pill asks for a value of its own (CustomClicked) and, while the value is one no other pill
// has, is the chosen one and shows it. With Multiple, each pill is a check box of its own, with a
// box before its words, as the welcome's preview toggles are.
// One stop for Tab; the arrow keys move between the pills and Space picks one. Enter is left to the
// window, so it goes on to the next page.
sealed class ChoiceChips : PartGroup
{
    object? value;
    readonly bool multiple;

    // A pill chosen by a click, Space or a screen reader; not when Value is set. With Multiple, a
    // pill turned on or off.
    public event EventHandler? SelectedChanged;

    // A Custom… pill pressed, with the pill.
    public event EventHandler? CustomClicked;

    public ChoiceChips(bool multiple = false)
    {
        this.multiple = multiple;
        AccessibleRole = AccessibleRole.Grouping;
    }

    public bool Multiple => multiple;

    // The room between the pills, across and down, at 100%.
    public int Gap { get; set; } = 6;

    // A smaller pill, for a tile.
    public bool Small { get; init; }

    public ChoiceChips Add(string text, object choice, string? glyph = null)
    {
        AddPart(new Chip(text, choice, custom: false, glyph));
        return this;
    }

    // The pill that asks for a value: chosen while the value is not one of the others.
    public ChoiceChips AddCustom(string text)
    {
        AddPart(new Chip(text, null, custom: true, null));
        return this;
    }

    internal IEnumerable<Chip> Chips => Parts.Cast<Chip>();

    internal Chip? CustomChip => Chips.FirstOrDefault(chip => chip.Custom);

    // The chosen value. Setting it chooses the pill that has it, or the Custom… pill for any other,
    // without raising SelectedChanged. Null chooses none.
    public object? Value
    {
        get => value;
        set
        {
            var was = Chosen;
            this.value = value;
            var now = Chosen;
            if (now is not null) SetCurrent(IndexOf(now));
            if (was != now)
            {
                was?.ChosenChanged();
                now?.ChosenChanged();
            }
        }
    }

    Chip? Chosen => multiple || value is null ? null
        : Chips.FirstOrDefault(chip => !chip.Custom && Equals(chip.Choice, value)) ?? CustomChip;

    // The words of the Custom… pill: its own while no value of its own is chosen, otherwise that
    // value, such as "+45 minutes".
    public void ShowCustom(string? words)
    {
        if (CustomChip is not { } chip) return;
        string text = words ?? chip.Plain;
        if (chip.Text == text) return;
        chip.Text = text;
        chip.AccessibleName = text;
        PerformLayout();
        Parent?.PerformLayout();
    }

    // The words a pill shows, changed in place: the battery choices with their real times.
    public void Relabel(object choice, string text)
    {
        var chip = Chips.FirstOrDefault(c => !c.Custom && Equals(c.Choice, choice));
        if (chip is null || chip.Text == text) return;
        chip.Text = text;
        chip.AccessibleName = text;
        PerformLayout();
        Parent?.PerformLayout();
    }

    // With Multiple: whether the pill with this choice is on.
    public bool IsChecked(object choice) => Chips.FirstOrDefault(c => Equals(c.Choice, choice))?.Checked ?? false;

    public void SetChecked(object choice, bool on)
    {
        if (Chips.FirstOrDefault(c => Equals(c.Choice, choice)) is not { } chip || chip.Checked == on) return;
        chip.Checked = on;
        chip.CheckedChanged();
    }

    // Shown or hidden, such as the battery toggle on a PC without a battery.
    public void ShowChoice(object choice, bool show)
    {
        if (Chips.FirstOrDefault(c => Equals(c.Choice, choice)) is not { } chip || chip.Shown == show) return;
        chip.Shown = show;
        chip.Visible = show;
        if (!show && Current == IndexOf(chip)) SetCurrent(IndexOf(Chips.First(c => c.Shown)));
        PerformLayout();
    }

    internal override void Choose(Part part)
    {
        var chip = (Chip)part;
        if (multiple)
        {
            chip.Checked = !chip.Checked;
            chip.CheckedChanged();
            SelectedChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (chip.Custom)
        {
            CustomClicked?.Invoke(chip, EventArgs.Empty);
            return;
        }
        if (Equals(chip.Choice, value)) return;
        Value = chip.Choice;
        SelectedChanged?.Invoke(this, EventArgs.Empty);
    }

    // Enter goes to the window.
    internal override bool Key(Part from, Keys key) => key != Keys.Enter && base.Key(from, key);

    // Not a pill that is left out.
    protected override bool Offers(Part part) => part is not Chip { Shown: false };

    // Tab comes back to the chosen pill, wherever the arrow keys were.
    protected override void OnLeave(EventArgs e)
    {
        if (Chosen is { } chosen) SetCurrent(IndexOf(chosen));
        base.OnLeave(e);
    }

    // The pills in rows at a width: where each goes, from the start of the row.
    List<(Chip Chip, Rectangle Box)> Rows(int width)
    {
        var places = new List<(Chip, Rectangle)>();
        int gap = Px(Gap);
        int x = 0, y = 0, row = 0;
        foreach (var chip in Chips)
        {
            if (!chip.Shown) continue;
            var size = chip.Wanted;
            if (x > 0 && x + size.Width > width)
            {
                x = 0;
                y += row + gap;
                row = 0;
            }
            places.Add((chip, new Rectangle(x, y, Math.Min(size.Width, Math.Max(1, width)), size.Height)));
            x += size.Width + gap;
            row = Math.Max(row, size.Height);
        }
        return places;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : int.MaxValue / 4;
        var rows = Rows(width);
        if (rows.Count == 0) return new Size(0, 0);
        int right = rows.Max(r => r.Box.Right);
        return new Size(proposedSize.Width > 0 ? proposedSize.Width : right, rows.Max(r => r.Box.Bottom));
    }

    // The width of the widest row the pills make on one line.
    public int OneLineWidth => Rows(int.MaxValue / 4) is { Count: > 0 } rows ? rows.Max(r => r.Box.Right) : 0;

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        foreach (var (chip, box) in Rows(Width)) chip.Bounds = Place(box.X, box.Y, box.Width, box.Height);
    }

    protected override Automation.Provider CreateProvider() => new(this, Automation.GroupType)
    {
        Selection = multiple ? null : ChosenProviders,
    };

    internal sealed class Chip : Part
    {
        readonly string? glyph;

        public Chip(string text, object? choice, bool custom, string? glyph)
        {
            Text = text;
            Plain = text;
            Choice = choice;
            Custom = custom;
            this.glyph = glyph;
            AccessibleName = text;
        }

        public object? Choice { get; }

        public bool Custom { get; }

        // The words it was made with: Custom… itself.
        public string Plain { get; }

        public bool Checked { get; set; }

        // Hidden pills are left out of the rows. Kept apart from Visible, which reads false for every
        // control of a window not on show yet.
        public bool Shown { get; set; } = true;

        ChoiceChips? Owner => Parent as ChoiceChips;

        bool Multiple => Owner?.Multiple == true;

        internal override bool IsChosen => Multiple ? Checked : Owner is { } owner && owner.Chosen == this;

        bool Small => Owner?.Small == true;

        Font Face => Body(Small ? 9f : 9.5f);

        Font GlyphFace => Icons(Small ? 9f : 10f);

        const TextFormatFlags Tight = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        int BoxSide => Px(14);

        int GlyphWidth => glyph is null ? 0 : TextRenderer.MeasureText(glyph, GlyphFace, Size.Empty, Tight).Width + Px(6);

        int Lead => Multiple ? BoxSide + Px(7) : 0;

        // Its words with 12 px each side, at least 30 px tall, or as tall as the words with room.
        public Size Wanted
        {
            get
            {
                var words = TextRenderer.MeasureText(Text, Face, Size.Empty, Tight);
                int side = Multiple ? Px(9) : Px(12);
                return new Size(side + Lead + GlyphWidth + words.Width + Px(12), Math.Max(Px(Small ? 28 : 30), words.Height + Px(10)));
            }
        }

        public override Size GetPreferredSize(Size proposedSize) => Wanted;

        // Enter is the window's: it goes on to the next page.
        protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) != Keys.Enter && base.IsInputKey(keyData);

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        // Tells screen readers a check box pill was turned on or off.
        internal void CheckedChanged()
        {
            Invalidate();
            Automation.Notify(this, AccessibleEvents.StateChange);
            Automation.ToggleChanged(Listened, Checked);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            Smooth(g);
            bool filled = IsChosen && !Multiple;
            var box = Shapes.Inside(ClientRectangle);
            float radius = Height / 2f;
            var fill = filled ? t.Accent : Hovered ? t.FieldHover : t.Field;
            var edge = filled || Hovered || Multiple && Checked ? t.Accent : t.ControlEdge;
            if (t.IsHighContrast && !filled) edge = t.Text;
            using (var path = Shapes.Rounded(box, radius))
            {
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
                using var pen = new Pen(edge);
                g.DrawPath(pen, path);
            }
            var ink = filled ? t.OnAccent : t.Text;
            int x = Multiple ? Px(9) : Px(12);
            if (Multiple)
            {
                var square = Box(x, (Height - BoxSide) / 2, BoxSide, BoxSide);
                using var path = Shapes.Rounded(Shapes.Inside(square), Px(3));
                if (Checked)
                {
                    using var brush = new SolidBrush(t.Accent);
                    g.FillPath(brush, path);
                    DrawText(g, Glyphs.Check, Icons(7.5f), square, t.OnAccent,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | Tight);
                }
                using var pen = new Pen(Checked ? t.Accent : t.FieldLine);
                g.DrawPath(pen, path);
                x += Lead;
            }
            if (glyph is not null)
            {
                DrawText(g, glyph, GlyphFace, Box(x, 0, GlyphWidth - Px(6), Height), Multiple && Checked ? t.Accent : filled ? t.OnAccent : t.Subtle,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | Tight);
                x += GlyphWidth;
            }
            Write(g, Text, Face, Box(x, 0, Math.Max(1, Width - x - Px(12) + 1), Height), ink, TextFormatFlags.VerticalCenter | Tight);
            if (Focused && ShowFocusCues)
            {
                using var pen = new Pen(filled ? t.OnAccent : t.Text, Px(2));
                using var ring = Shapes.Rounded(new RectangleF(Px(3), Px(3), Width - Px(6), Height - Px(6)), (Height - Px(6)) / 2f);
                g.DrawPath(pen, ring);
            }
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new ChipAccessible(this);

        protected override Automation.Provider CreateProvider() => Multiple
            ? new(this, Automation.CheckBoxType)
            {
                Press = () => Owner?.Choose(this),
                IsOn = () => Checked,
            }
            : new(this, Automation.RadioButtonType)
            {
                Press = () => Owner?.Choose(this),
                IsSelected = () => IsChosen,
                Container = () => Owner?.AutomationProvider,
            };

        sealed class ChipAccessible(Chip owner) : ControlAccessibleObject(owner)
        {
            public override AccessibleRole Role => owner.Multiple ? AccessibleRole.CheckButton : AccessibleRole.RadioButton;

            public override AccessibleStates State =>
                base.State | (owner.IsChosen ? AccessibleStates.Checked : AccessibleStates.None);

            public override string DefaultAction => owner.Multiple
                ? owner.Checked ? Lang.Current.TurnOff : Lang.Current.TurnOn
                : Lang.Current.SelectAction;

            public override void DoDefaultAction() => owner.Owner?.Choose(owner);
        }
    }
}
