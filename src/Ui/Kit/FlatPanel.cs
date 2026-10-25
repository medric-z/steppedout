namespace SteppedOut;

// One card holding a page of settings: rows stacked inside it, without boxes of their own, 16 px
// from its top and bottom and 20 from its sides, 13 px apart. A line can stand above a row to start
// a new part of the page, such as "When you're back". Under a row of tabs, set JoinedAbove so the
// panel hangs from the tabs.
sealed class FlatPanel : Card
{
    readonly HashSet<Control> lined = new();

    public FlatPanel()
    {
        Outline = t => t.Edge;
    }

    // The room around the rows and between them, at 100%.
    public Padding Inner { get; set; } = new(20, 16, 20, 16);

    public int Gap { get; set; } = 13;

    // Below a line, a row starts this far under it, at 100%.
    public int UnderLine { get; set; } = 12;

    // Adds a row, or any control that takes the panel's width, under the ones already in it.
    public FlatPanel Add(Control row, bool lineAbove = false)
    {
        if (row is SettingRow setting) setting.Look = RowLook.Flat;
        if (lineAbove) lined.Add(row);
        Controls.Add(row);
        PerformLayout();
        return this;
    }

    public bool HasLineAbove(Control row) => lined.Contains(row);

    // Where each visible row goes at a width: its top and height, and where its line is, if any.
    List<(Control Row, int Top, int Height, int Line)> Stack(int width)
    {
        var places = new List<(Control, int, int, int)>();
        int inner = Math.Max(1, width - Px(Inner.Left) - Px(Inner.Right));
        int y = Px(Inner.Top);
        bool first = true;
        foreach (Control row in Controls)
        {
            if (!row.Visible) continue;
            if (!first) y += Px(Gap);
            int line = -1;
            if (lined.Contains(row) && !first)
            {
                line = y;
                y += Px(UnderLine);
            }
            int height = row.GetPreferredSize(new Size(inner, 0)).Height;
            places.Add((row, y, height, line));
            y += height;
            first = false;
        }
        return places;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        var places = Stack(width);
        int bottom = places.Count == 0 ? Px(Inner.Top) : places[^1].Top + places[^1].Height;
        return new Size(width, bottom + Px(Inner.Bottom));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int inner = Math.Max(1, Width - Px(Inner.Left) - Px(Inner.Right));
        foreach (var (row, top, height, _) in Stack(Width)) row.Bounds = Box(Px(Inner.Left), top, inner, height);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Current.Line);
        foreach (var (_, _, _, line) in Stack(Width))
        {
            if (line >= 0) e.Graphics.DrawLine(pen, Px(Inner.Left), line, Width - Px(Inner.Right) - 1, line);
        }
    }
}

// A row inside a FlatPanel: its title, explanation and control, without a box of its own.
sealed class FlatRow : SettingRow
{
    public FlatRow(string titleText, string? detailText = null, Control? control = null, string? glyph = null)
        : base(glyph, titleText, detailText, control)
    {
        Look = RowLook.Flat;
    }
}
