namespace SteppedOut;

// The row of pages under the settings window's main list: tabs of equal width, each an icon, a name
// and a short line under it. When a name's longest word does not fit beside its icon (a large text
// size), every tab puts its icon above its name instead, and a word too long even for the whole tab
// ends in an ellipsis rather than being cut (I15), with the tab's whole words in a tip. The chosen
// tab has a soft fill and an accent bar along its top. The
// tabs keep their English order in Arabic, left to right, while each tab's own words read right to
// left. The arrow keys, Home and End move between the tabs; Enter, Space or a click chooses one.
sealed class TabStrip : PartGroup
{
    int selected = -1;
    ToolTip? tip;

    // A tab chosen by a click, a key or a screen reader; not when Selected is set.
    public event EventHandler? SelectedChanged;

    public TabStrip()
    {
        AccessibleRole = AccessibleRole.PageTabList;
        AccessibleName = "";
    }

    protected override bool AlwaysLeftToRight => true;

    public TabStrip Add(string glyph, string name, string detail)
    {
        AddPart(new Tab(glyph, name, detail));
        if (selected < 0) Selected = 0;
        return this;
    }

    public int Selected
    {
        get => selected;
        set
        {
            if (value < 0 || value >= Parts.Count || value == selected) return;
            var was = selected >= 0 ? Parts[selected] : null;
            selected = value;
            SetCurrent(value);
            was?.ChosenChanged();
            Parts[value].ChosenChanged();
        }
    }

    internal override void Choose(Part part)
    {
        int index = IndexOf(part);
        if (index < 0 || index == selected) return;
        Selected = index;
        SelectedChanged?.Invoke(this, EventArgs.Empty);
    }

    // Lose the chosen tab's place as the stop for Tab when the arrows move away, but come back to it.
    protected override void OnLeave(EventArgs e)
    {
        if (selected >= 0) SetCurrent(selected);
        base.OnLeave(e);
    }

    // Each tab as wide as the rest; the last takes what is left over.
    int ColumnWidth(int width) => Parts.Count == 0 ? 0 : (width - 2) / Parts.Count;

    // Whether the tabs put their icons above their names at this column width: all of them, so the
    // row stays even, as soon as one name has a word too wide to go beside its icon.
    bool IconsAbove(int column) => Parts.Cast<Tab>().Any(tab => !tab.NameFitsBesideIcon(column));

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        int column = ColumnWidth(width);
        bool above = IconsAbove(column);
        int tallest = Parts.Count == 0 ? Px(40) : Parts.Cast<Tab>().Max(tab => tab.HeightFor(column, above));
        return new Size(width, tallest + 2);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int column = ColumnWidth(Width);
        int height = Height - 2;
        for (int i = 0; i < Parts.Count; i++)
        {
            int x = 1 + i * column;
            int width = i == Parts.Count - 1 ? Width - 1 - x : column;
            Parts[i].Bounds = Place(x, 1, width, height);
        }
        Retip(IconsAbove(column));
    }

    // A tab whose words end in an ellipsis shows all of them, its name and the line under it, in a
    // tip; screen readers hear them whole anyway.
    void Retip(bool above)
    {
        foreach (var tab in Parts.Cast<Tab>())
        {
            string? whole = tab.Cut(tab.Width, above) ? tab.Whole : null;
            if (whole is null && tip is null) continue;
            tip ??= new ToolTip { ShowAlways = true };
            if (tip.GetToolTip(tab) != (whole ?? "")) tip.SetToolTip(tab, whole);
        }
    }

    // A tab's tip, for the tests and the render tool; null when its words show whole.
    internal string? TipOf(int index) => tip?.GetToolTip(Parts[index]) is { Length: > 0 } whole ? whole : null;

    protected override void Dispose(bool disposing)
    {
        if (disposing) tip?.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        Smooth(g);
        float radius = Px(8);
        using var path = Shapes.Corners(Shapes.Inside(ClientRectangle), radius, radius, 0, 0);
        using (var brush = new SolidBrush(t.Card)) g.FillPath(brush, path);
        using var pen = new Pen(t.Edge);
        g.DrawPath(pen, path);
    }

    protected override Automation.Provider CreateProvider() => new(this, Automation.TabType)
    {
        Selection = ChosenProviders,
        SelectionRequired = true,
    };

    sealed class Tab : Part
    {
        readonly string glyph;
        readonly string detail;

        public Tab(string glyph, string name, string detail)
        {
            this.glyph = glyph;
            this.detail = detail;
            Text = name;
            AccessibleName = name;
            AccessibleDescription = detail;
            AccessibleRole = AccessibleRole.PageTab;
            Automation.HelpFromDescription(this);
        }

        TabStrip? Strip => Parent as TabStrip;

        internal override bool IsChosen => Strip is { } strip && strip.Selected == strip.IndexOf(this);

        Font NameFont => Strong(9.75f);

        Font DetailFont => Body(9f);

        Font IconFont => Icons(12f);

        // The icon's own width, without the room TextRenderer leaves around words.
        int IconWidth => TextRenderer.MeasureText(glyph, IconFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;

        // Inside the tab: 10 px above, 12 at the sides, 9 below, as the sketch's tabs.
        int Side => Px(12);

        // The words go beside the icon, or under it when the icons are above.
        int NameWidth(int width, bool above) => Math.Max(Px(20), width - Side * 2 - (above ? 0 : IconWidth + Px(6)));

        int IconHeight => Shapes.Measure(glyph, IconFont).Height;

        int NameHeight(int width, bool above) => Tall(Text, NameFont, NameWidth(width, above));

        // The icon and the name: side by side, or the icon on a line of its own above the name.
        int HeadHeight(int width, bool above) => above
            ? IconHeight + Px(2) + NameHeight(width, above)
            : Math.Max(IconHeight, NameHeight(width, above));

        int DetailWidth(int width) => Math.Max(Px(20), width - Side * 2);

        int DetailHeight(int width) => detail.Length == 0 ? 0 : Tall(detail, DetailFont, DetailWidth(width));

        // How tall words are as drawn: a word too long for its line is drawn with an ellipsis, and
        // Windows then wraps the words otherwise than it measures them without one, a line more at
        // Windows' largest text sizes, which a box measured without it would cut off.
        int Tall(string words, Font font, int width) =>
            TextRenderer.MeasureText(words, font, new Size(Math.Max(1, width), int.MaxValue),
                Words(words, font, width) | TextFormatFlags.NoPrefix | (RightToLeft == RightToLeft.Yes ? TextFormatFlags.RightToLeft : 0)).Height;

        // A word too long for its line ends in an ellipsis; one that fits is left whole (an ellipsis
        // flag cuts a word that only reaches into the room TextRenderer leaves at the end).
        static TextFormatFlags Words(string text, Font font, int width) =>
            TextFormatFlags.WordBreak | (Fits(text, font, width) ? 0 : TextFormatFlags.WordEllipsis);

        // Every word of the name inside its line beside the icon, as drawn: TextRenderer starts the
        // words a little in from the edge, and a word longer than the line runs past its end and is
        // cut there.
        internal bool NameFitsBesideIcon(int width) => Fits(Text, NameFont, NameWidth(width, above: false));

        // A word of the name or of the line under it ends in an ellipsis at this width.
        internal bool Cut(int width, bool above) =>
            width > 0 && (!Fits(Text, NameFont, NameWidth(width, above)) || detail.Length > 0 && !Fits(detail, DetailFont, DetailWidth(width)));

        internal string Whole => detail.Length > 0 ? Text + Environment.NewLine + detail : Text;

        static bool Fits(string words, Font font, int width) =>
            words.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word => InkEnd(word, font) <= width);

        // Where a word's ink ends, from the start of its box.
        static int InkEnd(string word, Font font)
        {
            int ink = TextRenderer.MeasureText(word, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            int padded = TextRenderer.MeasureText(word, font, Size.Empty, TextFormatFlags.SingleLine).Width;
            return ink + (padded - ink) / 2;
        }

        public int HeightFor(int width, bool above) => Px(10) + HeadHeight(width, above) + Px(3) + DetailHeight(width) + Px(9);

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            Smooth(g);
            var strip = Strip;
            int index = strip?.IndexOf(this) ?? 0;
            int count = strip?.Parts.Count ?? 1;
            bool first = index == 0, last = index == count - 1;
            // The corners of the strip are rounded, so the end tabs' fills are too.
            float r = Px(7);
            float left = RowStartsLeft ? (first ? r : 0) : (last ? r : 0);
            float right = RowStartsLeft ? (last ? r : 0) : (first ? r : 0);
            var fill = IsChosen ? t.Soft : Hovered ? t.FieldHover : Color.Empty;
            if (!fill.IsEmpty)
            {
                using var path = Shapes.Corners(new RectangleF(0, 0, Width, Height), left, right, 0, 0);
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
            }
            if (IsChosen)
            {
                using var path = Shapes.Corners(new RectangleF(0, 0, Width, Px(3)), left, right, 0, 0);
                using var brush = new SolidBrush(t.Accent);
                g.FillPath(brush, path);
            }
            // The line between this tab and the one before it.
            if (!first)
            {
                using var pen = new Pen(t.Line);
                int x = RowStartsLeft ? 0 : Width - 1;
                g.DrawLine(pen, x, 0, x, Height);
            }

            bool above = strip?.IconsAbove(strip.ColumnWidth(strip.Width)) ?? false;
            int top = Px(10);
            int icon = IconWidth;
            int headHeight = HeadHeight(Width, above);
            if (above)
            {
                DrawText(g, glyph, IconFont, Box(Side, top, icon, IconHeight), t.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                int nameTop = top + IconHeight + Px(2);
                int nameWidth = NameWidth(Width, above);
                Write(g, Text, NameFont, Box(Side, nameTop, nameWidth, NameHeight(Width, above)), t.Text, Words(Text, NameFont, nameWidth));
            }
            else
            {
                DrawText(g, glyph, IconFont, Box(Side, top, icon, headHeight), t.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                int nameWidth = NameWidth(Width, above);
                Write(g, Text, NameFont, Box(Side + icon + Px(6), top, nameWidth, headHeight), t.Text,
                    TextFormatFlags.VerticalCenter | Words(Text, NameFont, nameWidth));
            }
            if (detail.Length > 0)
            {
                Write(g, detail, DetailFont, Box(Side, top + headHeight + Px(3), DetailWidth(Width), DetailHeight(Width)), t.Subtle,
                    Words(detail, DetailFont, DetailWidth(Width)));
            }
            FocusRing(g, new RectangleF(Px(2), Px(4), Width - Px(4), Height - Px(6)), Px(4));
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new TabAccessible(this);

        protected override Automation.Provider CreateProvider() => new(this, Automation.TabItemType)
        {
            Press = () => Strip?.Choose(this),
            IsSelected = () => IsChosen,
            Container = () => Strip?.AutomationProvider,
        };

        sealed class TabAccessible(Tab owner) : ControlAccessibleObject(owner)
        {
            public override AccessibleStates State =>
                base.State | AccessibleStates.Selectable | (owner.IsChosen ? AccessibleStates.Selected : AccessibleStates.None);

            public override string DefaultAction => Lang.Current.SelectAction;

            public override void DoDefaultAction() => owner.Strip?.Choose(owner);
        }
    }
}
