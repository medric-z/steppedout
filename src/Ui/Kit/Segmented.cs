namespace SteppedOut;

// A few choices drawn as one box, such as Light | Dark | System, with the chosen part filled in the
// accent color. It works as a radio group: one stop for Tab, and the arrow keys choose as they move.
// In Arabic the parts run right to left.
sealed class Segmented : PartGroup
{
    object? value;

    // A part chosen by a click, a key or a screen reader; not when Value is set.
    public event EventHandler? SelectedChanged;

    public Segmented()
    {
        AccessibleRole = AccessibleRole.Grouping;
    }

    protected override bool ArrowsChoose => true;

    public Segmented Add(string text, object choice)
    {
        AddPart(new Segment(text, choice));
        Size = GetPreferredSize(Size.Empty);
        return this;
    }

    public object? Value
    {
        get => value;
        set
        {
            int index = Parts.Cast<Segment>().ToList().FindIndex(part => Equals(part.Choice, value));
            if (index < 0 || Equals(this.value, value)) return;
            var was = Parts.Cast<Segment>().FirstOrDefault(part => part.IsChosen);
            this.value = Parts.Cast<Segment>().ElementAt(index).Choice;
            SetCurrent(index);
            was?.ChosenChanged();
            Parts[index].ChosenChanged();
        }
    }

    internal override void Choose(Part part)
    {
        var choice = ((Segment)part).Choice;
        if (Equals(choice, value)) return;
        Value = choice;
        SelectedChanged?.Invoke(this, EventArgs.Empty);
    }

    // Tab comes back to the chosen part, wherever the arrow keys were.
    protected override void OnLeave(EventArgs e)
    {
        int chosen = Parts.Cast<Segment>().ToList().FindIndex(part => part.IsChosen);
        if (chosen >= 0) SetCurrent(chosen);
        base.OnLeave(e);
    }

    internal Font Face => Body(9f);

    // The words are measured and drawn without the room Windows adds at their ends, as the sketch
    // draws them: its Light | Dark | System is 153 by 28.
    const TextFormatFlags Tight = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

    // 28 px tall with the edge, or taller when the words are.
    int PartHeight => Math.Max(Px(26), TextRenderer.MeasureText("Ag", Face, Size.Empty, Tight).Height + Px(8));

    public override Size GetPreferredSize(Size proposedSize) =>
        new(Parts.Cast<Segment>().Sum(part => part.PreferredWidth) + 2, PartHeight + 2);

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int x = 1;
        foreach (Segment part in Parts)
        {
            int width = part.PreferredWidth;
            part.Bounds = Place(x, 1, width, Height - 2);
            x += width;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        Smooth(g);
        using var path = Shapes.Rounded(Shapes.Inside(ClientRectangle), Px(6));
        using (var brush = new SolidBrush(t.Field)) g.FillPath(brush, path);
        using var pen = new Pen(t.FieldLine);
        g.DrawPath(pen, path);
    }

    protected override Automation.Provider CreateProvider() => new(this, Automation.GroupType)
    {
        Selection = ChosenProviders,
        SelectionRequired = true,
    };

    sealed class Segment : Part
    {
        public Segment(string text, object choice)
        {
            Text = text;
            Choice = choice;
            AccessibleName = text;
            AccessibleRole = AccessibleRole.RadioButton;
        }

        public object Choice { get; }

        Segmented? Owner => Parent as Segmented;

        internal override bool IsChosen => Owner is { } owner && Equals(owner.value, Choice);

        Font Face => Body(9f);

        // Its words with 10 px each side, and the line before it.
        public int PreferredWidth => TextRenderer.MeasureText(Text, Face, Size.Empty, Tight).Width + Px(20) + 1;

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            Smooth(g);
            var owner = Owner;
            int index = owner?.IndexOf(this) ?? 0;
            int count = owner?.Parts.Count ?? 1;
            bool first = index == 0, last = index == count - 1;
            float r = Px(5);
            float left = RowStartsLeft ? (first ? r : 0) : (last ? r : 0);
            float right = RowStartsLeft ? (last ? r : 0) : (first ? r : 0);
            var fill = IsChosen ? t.Accent : Hovered ? t.FieldHover : Color.Empty;
            if (!fill.IsEmpty)
            {
                using var path = Shapes.Corners(new RectangleF(0, 0, Width, Height), left, right, right, left);
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
            }
            if (!first)
            {
                using var pen = new Pen(t.Line);
                int x = RowStartsLeft ? 0 : Width - 1;
                g.DrawLine(pen, x, 0, x, Height);
            }
            int start = first ? 0 : 1;
            Write(g, Text, Face, Box(start, 0, Width - start, Height), IsChosen ? t.OnAccent : t.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | Tight);
            if (Focused && ShowFocusCues)
            {
                using var pen = new Pen(IsChosen ? t.OnAccent : t.Text, Px(2));
                using var ring = Shapes.Rounded(new RectangleF(Px(3), Px(3), Width - Px(6), Height - Px(6)), Px(3));
                g.DrawPath(pen, ring);
            }
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new SegmentAccessible(this);

        protected override Automation.Provider CreateProvider() => new(this, Automation.RadioButtonType)
        {
            Press = () => Owner?.Choose(this),
            IsSelected = () => IsChosen,
            Container = () => Owner?.AutomationProvider,
        };

        sealed class SegmentAccessible(Segment owner) : ControlAccessibleObject(owner)
        {
            public override AccessibleStates State =>
                base.State | (owner.IsChosen ? AccessibleStates.Checked : AccessibleStates.None);

            public override string DefaultAction => Lang.Current.SelectAction;

            public override void DoDefaultAction() => owner.Owner?.Choose(owner);
        }
    }
}
