using System.Drawing.Drawing2D;

namespace SteppedOut;

// A box with rounded corners on the window background: one settings row, the status at the top,
// or a group. A card joined to the one above it drops its top corners and sits flush against it.
class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw, true);
        // Read by what is in it. Left unnamed, WinForms names a card after the label before it in the
        // Tab order, such as the heading above or the hint at the end of the row before.
        AccessibleName = "";
    }

    public Color? Fill { get; set; }
    public bool JoinedAbove { get; set; }

    // How round the corners are, in pixels at 100%.
    public int Radius { get; set; } = 8;

    // The color of the edge, from the theme in use when it is drawn; the light line when unset.
    public Func<Theme, Color>? Outline { get; set; }

    // Drawn without a fill or an edge of its own, on whatever it sits on: a row inside a panel.
    public bool Flat { get; set; }

    // The color behind the card: the card it sits on, or the window.
    protected Color Behind => Parent is Card under ? under.Fill ?? Theme.Current.Card : Parent?.BackColor ?? Theme.Current.Window;

    // Positions inside the card that come out mirrored in Arabic when the window does not mirror them.
    protected bool Flip => RightToLeft == RightToLeft.Yes && !IsMirrored;

    protected Rectangle Box(int x, int y, int width, int height) => new(Flip ? Width - x - width : x, y, width, height);

    protected int Px(int logical) => LogicalToDeviceUnits(logical);

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        // Whatever is behind the rounded corners: the card this one sits on, or the window.
        g.Clear(Behind);
        if (Flat) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // As in KitButton, so the border sits on the edge pixels.
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var box = Shapes.Inside(ClientRectangle);
        float radius = Px(Radius);
        using var path = JoinedAbove ? SquareTop(box, radius) : Shapes.Rounded(box, radius);
        using var brush = new SolidBrush(Fill ?? t.Card);
        g.FillPath(brush, path);
        using var pen = new Pen(Outline?.Invoke(t) ?? t.Line);
        g.DrawPath(pen, path);
    }

    static GraphicsPath SquareTop(RectangleF box, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        // The card above already draws the line between the two, so this one's top edge sits a pixel
        // higher, where it is cut off, instead of doubling it.
        path.AddLine(box.X, box.Y - 1, box.Right, box.Y - 1);
        path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
        path.AddArc(box.X, box.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

// A label that wraps at whatever width its column gives it: section headings and hints.
sealed class TextBlock : Label
{
    Automation.Provider? provider;

    public TextBlock(string text, Font font, Color color)
    {
        Text = text;
        Font = font;
        ForeColor = color;
        BackColor = Color.Transparent;
        AutoSize = false;
        UseMnemonic = false;
    }

    // Screen readers can jump from heading to heading. MSAA has no headings, and WinForms' own UI
    // Automation answer for a label never says it is one, so a heading gives its own.
    public int Heading { get; init; }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        return new Size(width, Shapes.WrappedHeight(Text, Font, width - Padding.Horizontal) + Padding.Vertical);
    }

    internal Automation.Provider? AutomationProvider => Heading == 0 ? null : provider ??= new(this, Automation.TextType) { HeadingLevel = Heading };

    protected override void WndProc(ref Message m)
    {
        if (Automation.IsAsking(m) && AutomationProvider is { } answer) m.Result = Automation.Answer(m, answer);
        else base.WndProc(ref m);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (provider is not null)
        {
            Automation.Release(Handle, provider);
            provider = null;
        }
        base.OnHandleDestroyed(e);
    }
}

// Stacks its children top to bottom at its full width and scrolls when they do not fit. A child's
// Margin.Top is the gap above it.
sealed class Column : Panel
{
    public Column()
    {
        AutoScroll = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        // As for a card: otherwise named after the last label on the page shown before it.
        AccessibleName = "";
    }

    public int ContentHeight { get; private set; }

    int held;
    bool redrawOff;

    // Holds the painting while rows come, go and move, then paints the column once. Otherwise each
    // row is drawn where it lands while the ones below it have not moved yet, and for a moment a row
    // shows twice or half drawn. Holds nest; the last one to end paints.
    public IDisposable HoldPainting()
    {
        // Turning painting off also hides the window to Windows, without telling WinForms, so it is
        // only done to a column on show, and turned back on only by the hold that turned it off.
        if (held++ == 0 && IsHandleCreated && (Native.GetWindowLong(Handle, Native.GwlStyle) & Native.WsVisible) != 0)
        {
            Native.SendMessage(Handle, Native.WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
            redrawOff = true;
        }
        return new Hold(this);
    }

    void Release()
    {
        if (--held > 0 || !redrawOff) return;
        redrawOff = false;
        if (!IsHandleCreated) return;
        Native.SendMessage(Handle, Native.WmSetRedraw, 1, IntPtr.Zero);
        Native.RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, Native.RdwRepaintAll);
    }

    sealed class Hold(Column column) : IDisposable
    {
        bool done;

        public void Dispose()
        {
            if (done) return;
            done = true;
            column.Release();
        }
    }

    public int Measure(int width)
    {
        int y = Padding.Top;
        foreach (Control child in Controls)
        {
            if (!child.Visible) continue;
            y += child.Margin.Top + child.GetPreferredSize(new Size(width - Padding.Horizontal, 0)).Height + child.Margin.Bottom;
        }
        return y + Padding.Bottom;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int width = ClientSize.Width - Padding.Horizontal;
        // A hidden column's children all read as hidden too, so a layout now would take its scroll
        // range away. It is laid out again when it shows.
        if (width <= 0 || !Visible) return;
        using var hold = HoldPainting();
        int y = Padding.Top + AutoScrollPosition.Y;
        foreach (Control child in Controls)
        {
            if (!child.Visible) continue;
            y += child.Margin.Top;
            int height = child.GetPreferredSize(new Size(width, 0)).Height;
            child.SetBounds(Padding.Left, y, width, height);
            y += height + child.Margin.Bottom;
        }
        ContentHeight = y - AutoScrollPosition.Y + Padding.Bottom;
        if (AutoScrollMinSize.Height != ContentHeight) AutoScrollMinSize = new Size(0, ContentHeight);
    }
}
