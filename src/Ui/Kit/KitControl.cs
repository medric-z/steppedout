using System.Drawing.Drawing2D;

namespace SteppedOut;

// What the hand-drawn controls share: flicker-free painting, sizes that follow the screen's
// scaling, drawing the right way round in Arabic, and answering UI Automation.
abstract class KitControl : Control
{
    Automation.Provider? provider;

    protected KitControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    // What a control that takes focus tells UI Automation; see Automation.
    protected virtual Automation.Provider? CreateProvider() => null;

    internal Automation.Provider? AutomationProvider => provider ??= CreateProvider();

    // The provider, when there is anyone to hear a change.
    protected Automation.Provider? Listened => Automation.Heard(this) ? AutomationProvider : null;

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

    protected bool Hovered { get; private set; }
    protected bool Pressed { get; private set; }

    // A window laid out right to left already mirrors what is drawn in it. A control that reads right
    // to left without that has to flip its own drawing.
    protected bool Flip => RightToLeft == RightToLeft.Yes && !IsMirrored;

    protected Rectangle Box(int x, int y, int width, int height) => new(Flip ? Width - x - width : x, y, width, height);

    protected int Px(int logical) => LogicalToDeviceUnits(logical);

    // Fonts for the text drawn here, sized for the monitor the control is on and its window's text
    // size.
    protected Font Body(float points) => Theme.Body(points, this);

    protected Font Strong(float points) => Theme.Strong(points, this);

    protected Font Icons(float points) => Theme.Icons(points, this);

    protected void DrawText(Graphics g, string text, Font font, Rectangle box, Color color, TextFormatFlags flags)
    {
        if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
        if (Flip && (flags & (TextFormatFlags.HorizontalCenter | TextFormatFlags.Right)) == 0) flags |= TextFormatFlags.Right;
        TextRenderer.DrawText(g, text, font, box, color, flags | TextFormatFlags.NoPrefix);
    }

    // A box that stays where it is in Arabic too, for what reads left to right in every language,
    // such as the order of the tabs. A window laid out right to left mirrors it; this undoes that.
    protected Rectangle LtrBox(int x, int y, int width, int height) => new(IsMirrored ? Width - x - width : x, y, width, height);

    // Hears every piece of text the newer controls draw, with the box it has to fit in, so the render
    // tool can check that none of it is cut off. Null outside that tool.
    internal static Action<Control, string, Font, Rectangle, TextFormatFlags>? TextDrawn;

    // DrawText, and told to TextDrawn.
    protected void Write(Graphics g, string text, Font font, Rectangle box, Color color, TextFormatFlags flags)
    {
        TextDrawn?.Invoke(this, text, font, box, flags);
        DrawText(g, text, font, box, color, flags);
    }

    protected static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    protected void FocusRing(Graphics g, RectangleF box, float radius)
    {
        if (!Focused || !ShowFocusCues) return;
        using var pen = new Pen(Theme.Current.Text, Px(2));
        var ring = RectangleF.Inflate(box, -Px(1), -Px(1));
        using var path = Shapes.Rounded(ring, radius);
        g.DrawPath(pen, path);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        Hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        Hovered = false;
        Pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Pressed = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        Pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }
}

static class Shapes
{
    public static GraphicsPath Rounded(RectangleF box, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(box.Width, box.Height));
        if (d <= 0)
        {
            path.AddRectangle(box);
            return path;
        }
        path.AddArc(box.X, box.Y, d, d, 180, 90);
        path.AddArc(box.Right - d, box.Y, d, d, 270, 90);
        path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
        path.AddArc(box.X, box.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // Half a pixel in, so a one-pixel border lands on whole pixels.
    public static RectangleF Inside(Rectangle box) => Stroke(box, 1);

    // Half a pen width in, so a border of that width lands on whole pixels.
    public static RectangleF Stroke(Rectangle box, float pen) =>
        new(box.X + pen / 2, box.Y + pen / 2, box.Width - pen, box.Height - pen);

    // How tall text comes out wrapped at a width, as labels and hand-drawn text break it.
    public static int WrappedHeight(string text, Font font, int width) =>
        TextRenderer.MeasureText(text, font, new Size(Math.Max(1, width), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;

    // A box with each corner rounded by its own radius, 0 for square: the end parts of a row of
    // choices, or a row of tabs rounded only at the top.
    public static GraphicsPath Corners(RectangleF box, float topLeft, float topRight, float bottomRight, float bottomLeft)
    {
        var path = new GraphicsPath();
        float most = Math.Min(box.Width, box.Height) / 2;
        float tl = Math.Min(topLeft, most), tr = Math.Min(topRight, most), br = Math.Min(bottomRight, most), bl = Math.Min(bottomLeft, most);
        if (tl > 0) path.AddArc(box.X, box.Y, tl * 2, tl * 2, 180, 90);
        else path.AddLine(box.X, box.Y, box.X, box.Y);
        if (tr > 0) path.AddArc(box.Right - tr * 2, box.Y, tr * 2, tr * 2, 270, 90);
        else path.AddLine(box.Right, box.Y, box.Right, box.Y);
        if (br > 0) path.AddArc(box.Right - br * 2, box.Bottom - br * 2, br * 2, br * 2, 0, 90);
        else path.AddLine(box.Right, box.Bottom, box.Right, box.Bottom);
        if (bl > 0) path.AddArc(box.X, box.Bottom - bl * 2, bl * 2, bl * 2, 90, 90);
        else path.AddLine(box.X, box.Bottom, box.X, box.Bottom);
        path.CloseFigure();
        return path;
    }

    // How big text comes out on one line, or wrapped at a width when one is given.
    public static Size Measure(string text, Font font, int width = 0) => width > 0
        ? TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix)
        : TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
}

// A group of parts that is a single stop for Tab, such as a row of tabs or a list of chips: Tab
// lands on the current part and the arrow keys move between the parts. Each part is a control of its
// own, added in the order they show, so screen readers meet them in that order too.
abstract class PartGroup : KitControl
{
    readonly List<Part> parts = new();
    int current = -1;

    protected PartGroup()
    {
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
    }

    internal IReadOnlyList<Part> Parts => parts;

    // The part Tab lands on: the chosen one, or the one the arrow keys last moved to.
    internal int Current => current;

    // True for parts that read left to right in every language, such as the tabs; otherwise they
    // run right to left in Arabic.
    protected virtual bool AlwaysLeftToRight => false;

    internal bool ReadsLeftToRight => AlwaysLeftToRight;

    // True when moving with the arrow keys also chooses, as in a radio group; otherwise they only
    // move and Enter or Space chooses, as in a row of tabs.
    protected virtual bool ArrowsChoose => false;

    protected void AddPart(Part part)
    {
        parts.Add(part);
        part.TabStop = false;
        Controls.Add(part);
        if (current < 0) SetCurrent(0);
        PerformLayout();
    }

    protected void RemovePart(Part part)
    {
        int index = parts.IndexOf(part);
        if (index < 0) return;
        parts.RemoveAt(index);
        Controls.Remove(part);
        SetCurrent(parts.Count == 0 ? -1 : Math.Min(current > index ? current - 1 : current, parts.Count - 1));
        PerformLayout();
    }

    internal int IndexOf(Part part) => parts.IndexOf(part);

    protected void SetCurrent(int index)
    {
        int was = current;
        current = index;
        for (int i = 0; i < parts.Count; i++) parts[i].TabStop = i == current;
        if (was != index) CurrentChanged(was, index);
    }

    // The part Tab lands on moved, from one place in the list to another; either may be -1.
    protected virtual void CurrentChanged(int was, int now)
    {
    }

    // Whether the arrow keys can go to a part: not to one that is not shown or cannot be picked,
    // which Tab could then never land on.
    protected virtual bool Offers(Part part) => true;

    // A key pressed on one of the parts. True when it was used.
    internal virtual bool Key(Part from, Keys key)
    {
        int index = parts.IndexOf(from);
        if (index < 0) return false;
        bool ltr = AlwaysLeftToRight || RightToLeft != RightToLeft.Yes;
        int? to = key switch
        {
            Keys.Left => ltr ? index - 1 : index + 1,
            Keys.Right => ltr ? index + 1 : index - 1,
            Keys.Up => index - 1,
            Keys.Down => index + 1,
            Keys.Home => 0,
            Keys.End => parts.Count - 1,
            _ => null,
        };
        if (to is int target)
        {
            // On to the next part that can be picked, in the direction of the key; none further on stays.
            int step = key == Keys.Home ? 1 : key == Keys.End ? -1 : target < index ? -1 : 1;
            int at = Math.Clamp(target, 0, parts.Count - 1);
            while (at >= 0 && at < parts.Count && !Offers(parts[at])) at += step;
            MoveTo(at >= 0 && at < parts.Count ? at : index);
            return true;
        }
        if (key is Keys.Enter or Keys.Space)
        {
            Choose(from);
            return true;
        }
        return false;
    }

    protected void MoveTo(int index)
    {
        SetCurrent(index);
        var part = parts[index];
        if (part.CanFocus) part.Focus();
        if (ArrowsChoose) Choose(part);
    }

    // Enter, Space, a click, or a screen reader choosing one of the parts.
    internal virtual void Choose(Part part)
    {
    }

    internal void PartFocused(Part part)
    {
        int index = parts.IndexOf(part);
        if (index >= 0 && index != current) SetCurrent(index);
    }

    // Where a part goes, from the start of the row: mirrored in Arabic, unless the parts always
    // read left to right.
    protected Rectangle Place(int x, int y, int width, int height) => AlwaysLeftToRight ? LtrBox(x, y, width, height) : Box(x, y, width, height);

    // The parts chosen now, for UI Automation.
    protected IEnumerable<Automation.Provider> ChosenProviders() =>
        parts.Where(part => part.IsChosen).Select(part => part.AutomationProvider).OfType<Automation.Provider>();
}

// One part of a PartGroup: a tab, one of a few choices, a language pill or a chip.
abstract class Part : KitControl
{
    protected Part()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = false;
        Cursor = Cursors.Hand;
    }

    protected PartGroup? Group => Parent as PartGroup;

    // The chosen tab or choice; for a chip, the one Tab lands on.
    internal abstract bool IsChosen { get; }

    // The keys a part takes for itself rather than leaving to the window, such as the arrows.
    internal bool WantsKey(Keys key) => IsInputKey(key);

    // A key pressed on the part, as its own key handler sees it. True when it was used.
    internal bool Key(Keys key) => Group?.Key(this, key) ?? false;

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.Enter or Keys.Space or Keys.Delete or Keys.Back
        || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Modifiers == Keys.None && Key(e.KeyCode))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnClick(EventArgs e)
    {
        Focus();
        Group?.Choose(this);
        base.OnClick(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Group?.PartFocused(this);
        base.OnGotFocus(e);
    }

    // Tells screen readers the part was chosen or let go.
    internal void ChosenChanged()
    {
        Invalidate();
        Automation.Notify(this, AccessibleEvents.StateChange);
        Automation.SelectedChanged(Listened, IsChosen);
    }

    // Whether the left of the drawing faces the start of the row the part sits in, for the lines
    // between parts and the rounded ends of a row. In Arabic a row starts at the right, unless its
    // group always reads left to right.
    protected bool RowStartsLeft => Group is { ReadsLeftToRight: true } ? !IsMirrored : !Flip;
}
