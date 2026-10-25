namespace SteppedOut;

// The language switch that hangs from the top edge of the settings window at its right: the word
// "Language" and a pill for each language, the chosen one filled in the accent color. It reads
// "Language  English  العربية" from left to right in both languages, so each pill stays where the
// person who cannot read the other language left it. The arrow keys move between the pills; Enter,
// Space or a click chooses one.
sealed class LanguagePills : PartGroup
{
    readonly string label;
    string? value;

    // A language chosen by a click, a key or a screen reader; not when Value is set.
    public event EventHandler? SelectedChanged;

    public LanguagePills(string label)
    {
        this.label = label;
        AccessibleRole = AccessibleRole.Grouping;
        AccessibleName = label;
    }

    protected override bool AlwaysLeftToRight => true;

    public LanguagePills Add(string text, string language)
    {
        AddPart(new Pill(text, language));
        Size = GetPreferredSize(Size.Empty);
        return this;
    }

    public string? Value
    {
        get => value;
        set
        {
            int index = Parts.Cast<Pill>().ToList().FindIndex(pill => pill.Language == value);
            if (index < 0 || this.value == value) return;
            var was = Parts.Cast<Pill>().FirstOrDefault(pill => pill.IsChosen);
            this.value = value;
            SetCurrent(index);
            was?.ChosenChanged();
            Parts[index].ChosenChanged();
        }
    }

    internal override void Choose(Part part)
    {
        string language = ((Pill)part).Language;
        if (language == value) return;
        Value = language;
        SelectedChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnLeave(EventArgs e)
    {
        int chosen = Parts.Cast<Pill>().ToList().FindIndex(pill => pill.IsChosen);
        if (chosen >= 0) SetCurrent(chosen);
        base.OnLeave(e);
    }

    Font Face => Body(9f);

    int LabelWidth => Shapes.Measure(label, Face).Width;

    // 5 px above, 10 at the sides and 7 below, as the sketch's.
    int TopPad => Px(5);

    int Side => Px(10);

    int PillHeight => Shapes.Measure("Ag", Face).Height + Px(4) + 2;

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = Side + LabelWidth + Px(3);
        foreach (Pill pill in Parts) width += Px(5) + pill.PreferredWidth;
        return new Size(width + Side, TopPad + PillHeight + Px(7));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int x = Side + LabelWidth + Px(3);
        foreach (Pill pill in Parts)
        {
            x += Px(5);
            pill.Bounds = Place(x, TopPad, pill.PreferredWidth, PillHeight);
            x += pill.PreferredWidth;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        Smooth(g);
        // No edge along the top, which is the window's own; rounded at the bottom.
        float radius = Px(8);
        var box = new RectangleF(0.5f, -1, Width - 1, Height - 0.5f);
        using (var fill = Shapes.Corners(box, 0, 0, radius, radius))
        using (var brush = new SolidBrush(t.Card))
            g.FillPath(brush, fill);
        using (var edge = Shapes.Corners(box, 0, 0, radius, radius))
        using (var pen = new Pen(t.Edge))
        {
            g.DrawPath(pen, edge);
        }
        Write(g, label, Face, LtrBox(Side, TopPad, LabelWidth, PillHeight), t.Subtle,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    protected override Automation.Provider CreateProvider() => new(this, Automation.GroupType)
    {
        Selection = ChosenProviders,
        SelectionRequired = true,
    };

    sealed class Pill : Part
    {
        public Pill(string text, string language)
        {
            Text = text;
            Language = language;
            AccessibleName = text;
            AccessibleRole = AccessibleRole.RadioButton;
            // Each pill's words read in their own language's direction.
            RightToLeft = language == "ar" ? RightToLeft.Yes : RightToLeft.No;
        }

        public string Language { get; }

        LanguagePills? Owner => Parent as LanguagePills;

        internal override bool IsChosen => Owner is { } owner && owner.value == Language;

        // The words in a font that has their letters: the Arabic pill in Segoe UI even when the
        // window is in English, whose Segoe UI Variable has no Arabic, at the body font's size.
        Font Face => Language == "ar" ? Arabic(Body(9f)) : Body(9f);

        // Its words with 10 px each side, inside its edge.
        public int PreferredWidth => Shapes.Measure(Text, Face).Width + Px(20) + 2;

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            Smooth(g);
            var box = Shapes.Inside(ClientRectangle);
            using (var path = Shapes.Rounded(box, box.Height / 2))
            {
                var fill = IsChosen ? t.Accent : Hovered ? t.FieldHover : Color.Empty;
                if (!fill.IsEmpty)
                {
                    using var brush = new SolidBrush(fill);
                    g.FillPath(brush, path);
                }
                using var pen = new Pen(t.Accent);
                g.DrawPath(pen, path);
            }
            Write(g, Text, Face, ClientRectangle, IsChosen ? t.OnAccent : t.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues)
            {
                using var pen = new Pen(IsChosen ? t.OnAccent : t.Text, Px(2));
                var ring = RectangleF.Inflate(box, -Px(2), -Px(2));
                using var path = Shapes.Rounded(ring, ring.Height / 2);
                g.DrawPath(pen, path);
            }
        }

        static readonly Dictionary<Font, Font> ArabicFaces = new();

        static Font Arabic(Font body)
        {
            if (body.FontFamily.Name == "Segoe UI") return body;
            if (!ArabicFaces.TryGetValue(body, out var font)) ArabicFaces[body] = font = new Font("Segoe UI", body.SizeInPoints, body.Style);
            return font;
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new PillAccessible(this);

        protected override Automation.Provider CreateProvider() => new(this, Automation.RadioButtonType)
        {
            Press = () => Owner?.Choose(this),
            IsSelected = () => IsChosen,
            Container = () => Owner?.AutomationProvider,
        };

        sealed class PillAccessible(Pill owner) : ControlAccessibleObject(owner)
        {
            public override AccessibleStates State =>
                base.State | (owner.IsChosen ? AccessibleStates.Checked : AccessibleStates.None);

            public override string DefaultAction => Lang.Current.SelectAction;

            public override void DoDefaultAction() => owner.Owner?.Choose(owner);
        }
    }
}
