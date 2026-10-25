using System.Drawing.Drawing2D;

namespace SteppedOut;

// The parts the welcome's pages are made of, laid out by hand so they follow the screen's scaling,
// the text size and the reading direction.

// Everything above the buttons: the stage and the timeline at the top on the pages that have them,
// and the page under them. When the window leaves the page enough room under the stage and the
// timeline, they stay put and only the page scrolls, so a choice far down still shows on them;
// otherwise all of it scrolls together.
sealed class WelcomeFrame : Panel
{
    readonly WelcomeHead head;
    readonly WelcomeBody page;

    public WelcomeFrame(WelcomeHead head, WelcomeBody page)
    {
        this.head = head;
        this.page = page;
        AutoScroll = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        AccessibleName = "";
        Controls.Add(head);
        Controls.Add(page);
    }

    bool staged;

    // The stage and the timeline are on show. Kept here: Visible reads false for every control of a
    // window not on show yet.
    public bool Staged
    {
        get => staged;
        set
        {
            staged = value;
            head.Visible = value;
            AutoScrollPosition = Point.Empty;
            PerformLayout();
        }
    }

    // The least of the page that shows under the stage and the timeline held at the top, at 100%.
    public const int LeastPage = 260;

    public bool Pinned { get; private set; }

    int HeadHeight(int width) => Staged ? head.GetPreferredSize(new Size(width, 0)).Height : 0;

    // How tall the page and, when shown, the stage and the timeline are at a width.
    public int Measure(int width) => HeadHeight(width) + page.GetPreferredSize(new Size(width, 0)).Height;

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int width = ClientSize.Width;
        int height = ClientSize.Height;
        if (width <= 0) return;
        int top = HeadHeight(width);
        bool pinned = !Staged || top + LogicalToDeviceUnits(LeastPage) <= height;
        Pinned = Staged && pinned;
        if (pinned)
        {
            if (AutoScrollMinSize != Size.Empty) AutoScrollMinSize = Size.Empty;
            page.AutoScroll = true;
            if (Staged) head.SetBounds(0, 0, width, top);
            page.SetBounds(0, top, width, Math.Max(1, height - top));
            return;
        }
        // All of it scrolls here, the page at its full height.
        page.AutoScroll = false;
        int content = page.GetPreferredSize(new Size(width, 0)).Height;
        int y = AutoScrollPosition.Y;
        head.SetBounds(0, y, width, top);
        page.SetBounds(0, y + top, width, Math.Max(content, height - top));
        var wanted = new Size(0, top + content);
        if (AutoScrollMinSize != wanted) AutoScrollMinSize = wanted;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        PerformLayout();
    }
}

// The page under the stage: the parts on show, top to bottom, scrolling when the room under the
// stage is too short for them. The last part takes any room left.
sealed class WelcomeBody : Panel
{
    readonly List<Control> parts = new();

    public WelcomeBody()
    {
        AutoScroll = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        AccessibleName = "";
    }

    // The parts on show now, top to bottom.
    public void Use(params Control[] shown)
    {
        parts.Clear();
        parts.AddRange(shown);
        foreach (Control child in Controls) child.Visible = parts.Contains(child);
        AutoScrollPosition = Point.Empty;
        PerformLayout();
    }

    public static int Measure(int width, IEnumerable<Control> shown) =>
        shown.Sum(c => c.GetPreferredSize(new Size(width, 0)).Height);

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        return new Size(width, Measure(width, parts));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int width = ClientSize.Width;
        if (width <= 0) return;
        int y = AutoScrollPosition.Y;
        int total = 0;
        for (int i = 0; i < parts.Count; i++)
        {
            int height = parts[i].GetPreferredSize(new Size(width, 0)).Height;
            if (i == parts.Count - 1) height = Math.Max(height, ClientSize.Height - total);
            parts[i].SetBounds(0, y, width, height);
            y += height;
            total += height;
        }
        var wanted = AutoScroll ? new Size(0, total) : Size.Empty;
        if (AutoScrollMinSize != wanted) AutoScrollMinSize = wanted;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        PerformLayout();
    }
}

// The stage and the timeline at the top of the window, with a line under them.
sealed class WelcomeHead : Panel
{
    public WelcomeHead()
    {
        BackColor = Theme.Current.Window;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        AccessibleName = "";
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        return new Size(width, WelcomeBody.Measure(width, Controls.Cast<Control>()));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int y = 0;
        foreach (Control part in Controls)
        {
            int height = part.GetPreferredSize(new Size(Width, 0)).Height;
            part.SetBounds(0, y, Width, height);
            y += height;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Current.Line);
        e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}

// The first page: the logo, the window's name, what SteppedOut does, and Start, in the middle.
sealed class FirstPage : Panel
{
    readonly TextBlock title;
    readonly TextBlock intro;
    readonly KitButton start;
    Bitmap? logo;
    bool logoTurned;

    public FirstPage(Strings text, KitButton start)
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        var t = Theme.Current;
        BackColor = t.Window;
        AccessibleName = "";
        title = new TextBlock(text.WelcomeTitle, Theme.Strong(20f), t.Text) { Heading = 1, TextAlign = ContentAlignment.TopCenter };
        intro = new TextBlock(text.V1.WelcomeIntro, Theme.Body(11f), t.Subtle) { TextAlign = ContentAlignment.TopCenter };
        this.start = start;
        Controls.AddRange([title, intro, start]);
    }

    int Px(int logical) => LogicalToDeviceUnits(logical);

    int TextWidth(int width) => Math.Min(Px(540), Math.Max(1, width - Px(48)));

    int LogoSize => Px(72);

    int ContentHeight(int width) => LogoSize + Px(20) + title.GetPreferredSize(new Size(TextWidth(width), 0)).Height + Px(10)
        + intro.GetPreferredSize(new Size(TextWidth(width), 0)).Height + Px(30) + StartSize.Height;

    Size StartSize
    {
        get
        {
            var size = start.GetPreferredSize(Size.Empty);
            return new Size(Math.Max(Px(160), size.Width), Math.Max(Px(40), size.Height));
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        return new Size(width, ContentHeight(width) + Px(96));
    }

    int ContentTop => Math.Max(Px(32), (Height - ContentHeight(Width)) / 2);

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int textWidth = TextWidth(Width);
        int x = (Width - textWidth) / 2;
        int y = ContentTop + LogoSize + Px(20);
        int h = title.GetPreferredSize(new Size(textWidth, 0)).Height;
        title.SetBounds(x, y, textWidth, h);
        y += h + Px(10);
        h = intro.GetPreferredSize(new Size(textWidth, 0)).Height;
        intro.SetBounds(x, y, textWidth, h);
        y += h + Px(30);
        var size = StartSize;
        start.SetBounds((Width - size.Width) / 2, y, size.Width, size.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int size = LogoSize;
        bool mirrored = IsMirrored;
        if (logo?.Width != size || logoTurned != mirrored)
        {
            logo?.Dispose();
            logo = Logo.Draw(size);
            logoTurned = mirrored;
            if (mirrored) logo.RotateFlip(RotateFlipType.RotateNoneFlipX);
        }
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(logo, (Width - size) / 2, ContentTop, size, size);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) logo?.Dispose();
        base.Dispose(disposing);
    }
}

// A part of a page that can lay itself out lower when the window is short of room for the page.
interface ICloseUp
{
    // Its height at a width, closed up or as usual.
    int HeightAt(int width, bool closedUp);

    bool ClosedUp { get; set; }
}

// A page under the stage: its heading and parts stacked under it, 24 px in from the window's sides,
// each with its own room above it. With CanCloseUp, a page the window is too short for closes up
// first: the room between its parts shrinks and parts that can (ICloseUp) lay themselves out lower,
// so on a small screen or at a large scaling it still fits without scrolling. It asks for only that
// lower height, and lays out as usual whenever the window gives it room for that.
sealed class PageStack : Panel
{
    public PageStack()
    {
        BackColor = Theme.Current.Window;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        AccessibleName = "";
    }

    readonly Dictionary<Control, int> above = new();

    // A part, and the room above it at 100%.
    public PageStack Add(Control part, int room)
    {
        above[part] = room;
        Controls.Add(part);
        return this;
    }

    int Px(int logical) => LogicalToDeviceUnits(logical);

    // Parts can be left out, such as the battery on a PC without one; Visible reads false for every
    // part of a window not on show yet.
    public HashSet<Control> Hidden { get; } = new();

    IEnumerable<Control> Shown => Controls.Cast<Control>().Where(c => !Hidden.Contains(c));

    public bool CanCloseUp { get; init; }

    // The most room above a part and under the last one, at 100%, while closed up.
    const int CloseRoom = 12;

    public bool ClosedUp { get; private set; }

    int Above(Control part, bool closedUp) => Px(closedUp ? Math.Min(above[part], CloseRoom) : above[part]);

    int Under(bool closedUp) => Px(closedUp ? CloseRoom : 24);

    int HeightOf(Control part, int inner, bool closedUp) =>
        part is ICloseUp lower ? lower.HeightAt(inner, closedUp) : part.GetPreferredSize(new Size(inner, 0)).Height;

    int HeightAt(int width, bool closedUp)
    {
        int inner = Math.Max(1, width - Px(48));
        int y = Px(16);
        foreach (var part in Shown) y += Above(part, closedUp) + HeightOf(part, inner, closedUp);
        return y + Under(closedUp);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        return new Size(width, HeightAt(width, CanCloseUp));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int inner = Math.Max(1, Width - Px(48));
        bool closedUp = ClosedUp = CanCloseUp && HeightAt(Width, false) > Height;
        int y = Px(16);
        foreach (Control part in Controls)
        {
            part.Visible = !Hidden.Contains(part);
            if (Hidden.Contains(part)) continue;
            if (part is ICloseUp lower) lower.ClosedUp = closedUp;
            y += Above(part, closedUp);
            int height = HeightOf(part, inner, closedUp);
            part.SetBounds(Px(24), y, inner, height);
            part.PerformLayout();
            y += height;
        }
    }
}

// One question: its icon, title and a line under it, and the choices under those, indented to the
// title. Something can follow the choices on their line, such as Try it.
sealed class WelcomeQuestion : Panel
{
    readonly string glyph;
    readonly Label title;
    readonly Label detail;
    readonly ChoiceChips chips;
    readonly Control? after;

    public WelcomeQuestion(string glyph, string titleText, string? detailText, ChoiceChips chips, Control? after = null)
    {
        var t = Theme.Current;
        this.glyph = glyph;
        this.chips = chips;
        this.after = after;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        AccessibleName = "";
        title = new Label { Text = titleText, AutoSize = false, UseMnemonic = false, BackColor = Color.Transparent, ForeColor = t.Text, Font = Theme.Strong(10.5f) };
        detail = new Label { Text = detailText ?? "", AutoSize = false, UseMnemonic = false, BackColor = Color.Transparent, ForeColor = t.Subtle, Font = Theme.Body(9.5f), Visible = !string.IsNullOrEmpty(detailText) };
        chips.AccessibleName = titleText;
        if (!string.IsNullOrEmpty(detailText)) chips.AccessibleDescription = detailText;
        Controls.AddRange([title, detail, chips]);
        if (after is not null) Controls.Add(after);
    }

    public ChoiceChips Chips => chips;

    public string Title => title.Text;

    // The line under the title.
    public string Detail => detail.Text;

    int Px(int logical) => LogicalToDeviceUnits(logical);

    int Indent => Px(32);

    bool Flip => RightToLeft == RightToLeft.Yes && !IsMirrored;

    Rectangle Box(int x, int y, int width, int height) => new(Flip ? Width - x - width : x, y, width, height);

    // Where each part goes at a width: the title, the line under it, the choices and what follows them.
    (Rectangle Title, Rectangle Detail, Rectangle Chips, Rectangle After, int Height) Places(int width)
    {
        int inner = Math.Max(1, width - Indent);
        int titleHeight = Shapes.WrappedHeight(title.Text, title.Font, inner);
        int y = 0;
        var titleBox = new Rectangle(Indent, y, inner, titleHeight);
        y += titleHeight;
        var detailBox = Rectangle.Empty;
        if (detail.Text.Length > 0)
        {
            int h = Shapes.WrappedHeight(detail.Text, detail.Font, inner);
            detailBox = new Rectangle(Indent, y, inner, h);
            y += h;
        }
        y += Px(8);
        var afterSize = after?.GetPreferredSize(Size.Empty) ?? Size.Empty;
        int gap = Px(14);
        int oneLine = chips.OneLineWidth;
        Rectangle chipsBox, afterBox = Rectangle.Empty;
        if (after is not null && oneLine + gap + afterSize.Width <= inner)
        {
            int h = chips.GetPreferredSize(new Size(oneLine, 0)).Height;
            chipsBox = new Rectangle(Indent, y, oneLine, h);
            afterBox = new Rectangle(Indent + oneLine + gap, y + (h - afterSize.Height) / 2, afterSize.Width, afterSize.Height);
            y += Math.Max(h, afterSize.Height);
        }
        else
        {
            int h = chips.GetPreferredSize(new Size(inner, 0)).Height;
            chipsBox = new Rectangle(Indent, y, inner, h);
            y += h;
            if (after is not null)
            {
                y += Px(6);
                afterBox = new Rectangle(Indent, y, afterSize.Width, afterSize.Height);
                y += afterSize.Height;
            }
        }
        return (titleBox, detailBox, chipsBox, afterBox, y);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        return new Size(width, Places(width).Height);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var places = Places(Width);
        title.Bounds = Box(places.Title.X, places.Title.Y, places.Title.Width, places.Title.Height);
        detail.Bounds = Box(places.Detail.X, places.Detail.Y, places.Detail.Width, places.Detail.Height);
        chips.Bounds = Box(places.Chips.X, places.Chips.Y, places.Chips.Width, places.Chips.Height);
        if (after is not null) after.Bounds = Box(places.After.X, places.After.Y, places.After.Width, places.After.Height);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var t = Theme.Current;
        int size = Px(22);
        var box = Box(0, 0, size, Math.Max(size, title.Height));
        TextRenderer.DrawText(e.Graphics, glyph, Theme.Icons(12f, this), box, t.Accent,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
}

// A box that says where to find more: an icon in a soft square, a title and a line or two.
sealed class InfoBox : Card
{
    readonly string glyph;
    readonly Label title;
    readonly Label body;

    public InfoBox(string glyph, string titleText, string bodyText)
    {
        var t = Theme.Current;
        this.glyph = glyph;
        Outline = theme => theme.Edge;
        title = new Label { Text = titleText, AutoSize = false, UseMnemonic = false, BackColor = Color.Transparent, ForeColor = t.Text, Font = Theme.Strong(10f) };
        body = new Label { Text = bodyText, AutoSize = false, UseMnemonic = false, BackColor = Color.Transparent, ForeColor = t.Subtle, Font = Theme.Body(9.5f) };
        Controls.AddRange([title, body]);
    }

    int TextLeft => Px(16 + 34 + 14);

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        int inner = Math.Max(1, width - TextLeft - Px(16));
        int height = Shapes.WrappedHeight(title.Text, title.Font, inner) + Px(2) + Shapes.WrappedHeight(body.Text, body.Font, inner);
        return new Size(width, Math.Max(Px(34), height) + Px(26));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int inner = Math.Max(1, Width - TextLeft - Px(16));
        int h = Shapes.WrappedHeight(title.Text, title.Font, inner);
        title.Bounds = Box(TextLeft, Px(13), inner, h);
        body.Bounds = Box(TextLeft, Px(13) + h + Px(2), inner, Shapes.WrappedHeight(body.Text, body.Font, inner));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var t = Theme.Current;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var square = Box(Px(16), Px(13), Px(34), Px(34));
        using (var path = Shapes.Rounded(square, Px(8)))
        using (var brush = new SolidBrush(t.Soft))
            g.FillPath(brush, path);
        TextRenderer.DrawText(g, glyph, Theme.Icons(12f, this), square, t.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
}

// One of the four tiles of the away page: an icon, a title, a line under it, a rule, and its control
// under that: a switch with On or Off, or a few choices. Clicking anywhere on a switch's tile flips it.
sealed class ChoiceTile : Card
{
    readonly string glyph;
    readonly Label title;
    readonly Label detail;
    readonly Control control;

    public ChoiceTile(string glyph, string titleText, string detailText, Control control)
    {
        var t = Theme.Current;
        this.glyph = glyph;
        this.control = control;
        Outline = theme => theme.Edge;
        title = new Label { Text = titleText, AutoSize = false, UseMnemonic = false, BackColor = Color.Transparent, ForeColor = t.Text, Font = Theme.Strong(10.5f) };
        detail = new Label { Text = detailText, AutoSize = false, UseMnemonic = false, BackColor = Color.Transparent, ForeColor = t.Subtle, Font = Theme.Body(9.5f) };
        Controls.AddRange([title, detail, control]);
        control.AccessibleName = titleText;
        control.AccessibleDescription = detailText;
        Automation.HelpFromDescription(control);
        if (control is Toggle toggle)
        {
            Cursor = Cursors.Hand;
            foreach (Control part in new Control[] { this, title, detail })
            {
                part.Click += (_, _) =>
                {
                    if (!toggle.Enabled) return;
                    toggle.Focus();
                    toggle.Checked = !toggle.Checked;
                };
            }
        }
    }

    public Control Control => control;

    public string Title => title.Text;

    // The line under the title.
    public string Detail => detail.Text;

    int Side => Px(16);

    int TitleLeft => Side + Px(30);

    // Laid out lower, as the grid asks when the page is short of room: less room under the line and
    // around the control.
    public bool ClosedUp { get; set; }

    // The top part's height at a width: the title and the line under it.
    public int TopHeight(int width) => TopHeight(width, ClosedUp);

    public int TopHeight(int width, bool closedUp)
    {
        int inner = Math.Max(1, width - TitleLeft - Side);
        return Px(14) + Math.Max(Px(22), Shapes.WrappedHeight(title.Text, title.Font, inner)) + Px(4)
            + Shapes.WrappedHeight(detail.Text, detail.Font, Math.Max(1, width - 2 * Side)) + Px(closedUp ? 10 : 12);
    }

    public int BottomHeight(int width) => BottomHeight(width, ClosedUp);

    public int BottomHeight(int width, bool closedUp)
    {
        int inner = Math.Max(1, width - 2 * Side);
        int controlHeight = control.GetPreferredSize(new Size(inner, 0)).Height;
        return Math.Max(Px(closedUp ? 52 : 64), controlHeight + Px(24));
    }

    // Set by the grid, so all four are the same shape.
    public int TopRoom { get; set; }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        return new Size(width, TopHeight(width) + BottomHeight(width));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int inner = Math.Max(1, Width - TitleLeft - Side);
        int titleHeight = Math.Max(Px(22), Shapes.WrappedHeight(title.Text, title.Font, inner));
        title.Bounds = Box(TitleLeft, Px(14), inner, titleHeight);
        detail.Bounds = Box(Side, Px(14) + titleHeight + Px(4), Width - 2 * Side, Shapes.WrappedHeight(detail.Text, detail.Font, Math.Max(1, Width - 2 * Side)));
        int line = Math.Max(TopRoom, TopHeight(Width));
        int bottom = Height - line;
        var size = control is ChoiceChips ? control.GetPreferredSize(new Size(Width - 2 * Side, 0)) : control.GetPreferredSize(Size.Empty);
        int width = control is ChoiceChips ? Width - 2 * Side : size.Width;
        control.Bounds = Box(Side - (control is Toggle ? Px(4) : 0), line + (bottom - size.Height) / 2, width, size.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var t = Theme.Current;
        var g = e.Graphics;
        TextRenderer.DrawText(g, glyph, Theme.Icons(12f, this), Box(Side, Px(14), Px(24), Math.Max(Px(22), title.Height)), t.Accent,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        int line = Math.Max(TopRoom, TopHeight(Width));
        using var pen = new Pen(t.Line);
        g.DrawLine(pen, 1, line, Width - 2, line);
    }
}

// The four tiles, two by two, all the same size. Closed up, when the page is short of room, the two
// side by side stay the same shape, their rules level, but each row is only as tall as its own
// tiles need.
sealed class TileGrid : Panel, ICloseUp
{
    readonly ChoiceTile[] tiles;

    public TileGrid(params ChoiceTile[] tiles)
    {
        this.tiles = tiles;
        BackColor = Theme.Current.Window;
        AccessibleName = "";
        Controls.AddRange(tiles);
    }

    int Px(int logical) => LogicalToDeviceUnits(logical);

    int Gap => Px(12);

    int TileWidth(int width) => Math.Max(1, (width - Gap) / 2);

    int Rows => (tiles.Length + 1) / 2;

    public bool ClosedUp { get; set; }

    // A row's room above its rule and below it: from every tile, or closed up from the tiles in it.
    (int Top, int Bottom) Room(int width, int row, bool closedUp)
    {
        int w = TileWidth(width);
        var from = closedUp ? tiles.Skip(row * 2).Take(2).ToList() : tiles.ToList();
        return (from.Max(tile => tile.TopHeight(w, closedUp)), from.Max(tile => tile.BottomHeight(w, closedUp)));
    }

    public int HeightAt(int width, bool closedUp)
    {
        int height = (Rows - 1) * Gap;
        for (int row = 0; row < Rows; row++)
        {
            var (top, bottom) = Room(width, row, closedUp);
            height += top + bottom;
        }
        return height;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        return new Size(width, HeightAt(width, ClosedUp));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int w = TileWidth(Width);
        bool flip = RightToLeft == RightToLeft.Yes && !IsMirrored;
        int y = 0;
        for (int row = 0; row < Rows; row++)
        {
            var (top, bottom) = Room(Width, row, ClosedUp);
            for (int i = row * 2; i < Math.Min(tiles.Length, row * 2 + 2); i++)
            {
                int x = i % 2 == 0 ? 0 : Width - w;
                if (flip) x = Width - x - w;
                tiles[i].ClosedUp = ClosedUp;
                tiles[i].TopRoom = top;
                tiles[i].SetBounds(x, y, w, top + bottom);
                tiles[i].PerformLayout();
            }
            y += top + bottom + Gap;
        }
    }
}

// A card as wide as the page, in the tiles' style: an icon, a title, the line about it, a rule, and a
// row of choices under that. Closed up, when the page is short of room, it has no rule, and the line
// follows the title on its row when both fit. The break reminders on the away page.
sealed class ChipsCard : Card, ICloseUp
{
    readonly string glyph;
    readonly Label title;
    readonly Label detail;
    readonly ChoiceChips chips;

    public ChipsCard(string glyph, string titleText, string detailText, ChoiceChips chips)
    {
        var t = Theme.Current;
        this.glyph = glyph;
        this.chips = chips;
        Outline = theme => theme.Edge;
        title = new Label { Text = titleText, AutoSize = false, UseMnemonic = false, BackColor = Color.Transparent, ForeColor = t.Text, Font = Theme.Strong(10.5f) };
        detail = new Label { Text = detailText, AutoSize = false, UseMnemonic = false, BackColor = Color.Transparent, ForeColor = t.Subtle, Font = Theme.Body(9.5f) };
        Controls.AddRange([title, detail, chips]);
        chips.AccessibleName = titleText;
        chips.AccessibleDescription = detailText;
        Automation.HelpFromDescription(chips);
    }

    public ChoiceChips Chips => chips;

    public string Title => title.Text;

    // The line under the title.
    public string Detail => detail.Text;

    public bool ClosedUp { get; set; }

    int Side => Px(16);

    int TitleLeft => Side + Px(30);

    // Where each part goes at a width: the title, the line about it, the rule (0 for none) and the
    // choices.
    (Rectangle Title, Rectangle Detail, int Rule, Rectangle Chips, int Height) Places(int width, bool closedUp)
    {
        int inner = Math.Max(1, width - 2 * Side);
        int room = Math.Max(1, width - TitleLeft - Side);
        int gap = Px(12);
        var titleSize = Shapes.Measure(title.Text, title.Font);
        var detailSize = Shapes.Measure(detail.Text, detail.Font);
        int y = Px(closedUp ? 12 : 14);
        Rectangle titleBox, detailBox;
        if (closedUp && titleSize.Width + Px(2) + gap + detailSize.Width + Px(2) <= room)
        {
            int row = Math.Max(Px(22), Math.Max(titleSize.Height, detailSize.Height));
            titleBox = new Rectangle(TitleLeft, y, titleSize.Width + Px(2), row);
            detailBox = new Rectangle(titleBox.Right + gap, y, room - titleBox.Width - gap, row);
            y += row;
        }
        else
        {
            int titleHeight = Math.Max(Px(22), Shapes.WrappedHeight(title.Text, title.Font, room));
            titleBox = new Rectangle(TitleLeft, y, room, titleHeight);
            y += titleHeight + Px(closedUp ? 2 : 4);
            int detailHeight = Shapes.WrappedHeight(detail.Text, detail.Font, inner);
            detailBox = new Rectangle(Side, y, inner, detailHeight);
            y += detailHeight;
        }
        int rule = 0;
        if (closedUp) y += Px(10);
        else
        {
            y += Px(12);
            rule = y;
            y += Px(12);
        }
        int chipsHeight = chips.GetPreferredSize(new Size(inner, 0)).Height;
        var chipsBox = new Rectangle(Side, y, inner, chipsHeight);
        y += chipsHeight + Px(12);
        return (titleBox, detailBox, rule, chipsBox, y);
    }

    public int HeightAt(int width, bool closedUp) => Places(width, closedUp).Height;

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        return new Size(width, HeightAt(width, ClosedUp));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var places = Places(Width, ClosedUp);
        bool inline = places.Detail.Y == places.Title.Y;
        title.TextAlign = inline ? ContentAlignment.MiddleLeft : ContentAlignment.TopLeft;
        detail.TextAlign = inline ? ContentAlignment.MiddleLeft : ContentAlignment.TopLeft;
        title.Bounds = Box(places.Title.X, places.Title.Y, places.Title.Width, places.Title.Height);
        detail.Bounds = Box(places.Detail.X, places.Detail.Y, places.Detail.Width, places.Detail.Height);
        chips.Bounds = Box(places.Chips.X, places.Chips.Y, places.Chips.Width, places.Chips.Height);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var t = Theme.Current;
        var g = e.Graphics;
        var places = Places(Width, ClosedUp);
        TextRenderer.DrawText(g, glyph, Theme.Icons(12f, this), Box(Side, places.Title.Y, Px(24), places.Title.Height), t.Accent,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        if (places.Rule <= 0) return;
        using var pen = new Pen(t.Line);
        g.DrawLine(pen, 1, places.Rule, Width - 2, places.Rule);
    }
}

// Start with Windows, big: an icon in a square, the title and its line, and the switch with On or
// Off. Clicking anywhere on it flips the switch.
sealed class StartCard : Card
{
    readonly Label title;
    readonly Label detail;
    readonly Toggle toggle;

    public StartCard(string titleText, string detailText, Toggle toggle)
    {
        var t = Theme.Current;
        this.toggle = toggle;
        Outline = theme => theme.Edge;
        Radius = 10;
        title = new Label { Text = titleText, AutoSize = false, UseMnemonic = false, BackColor = Color.Transparent, ForeColor = t.Text, Font = Theme.Strong(14f) };
        detail = new Label { Text = detailText, AutoSize = false, UseMnemonic = false, BackColor = Color.Transparent, ForeColor = t.Subtle, Font = Theme.Body(10f) };
        toggle.Look = ToggleLook.LookC;
        toggle.ShowLabel = true;
        toggle.AccessibleName = titleText;
        toggle.AccessibleDescription = detailText;
        Automation.HelpFromDescription(toggle);
        Controls.AddRange([title, detail, toggle]);
        Cursor = Cursors.Hand;
        foreach (Control part in new Control[] { this, title, detail })
        {
            part.Click += (_, _) =>
            {
                toggle.Focus();
                toggle.Checked = !toggle.Checked;
            };
        }
        toggle.CheckedChanged += (_, _) => Invalidate();
    }

    int Icon => Px(48);

    int TextLeft => Px(22) + Icon + Px(18);

    int TextWidth(int width) => Math.Max(1, width - TextLeft - toggle.GetPreferredSize(Size.Empty).Width - Px(22 + 16));

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        int inner = TextWidth(width);
        int words = Shapes.WrappedHeight(title.Text, title.Font, inner) + Px(2) + Shapes.WrappedHeight(detail.Text, detail.Font, inner);
        return new Size(width, Math.Max(Icon, words) + Px(36));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int inner = TextWidth(Width);
        int titleHeight = Shapes.WrappedHeight(title.Text, title.Font, inner);
        int detailHeight = Shapes.WrappedHeight(detail.Text, detail.Font, inner);
        int top = (Height - titleHeight - Px(2) - detailHeight) / 2;
        title.Bounds = Box(TextLeft, top, inner, titleHeight);
        detail.Bounds = Box(TextLeft, top + titleHeight + Px(2), inner, detailHeight);
        var size = toggle.GetPreferredSize(Size.Empty);
        toggle.Bounds = Box(Width - Px(22) - size.Width, (Height - size.Height) / 2, size.Width, size.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var t = Theme.Current;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var square = Box(Px(22), (Height - Icon) / 2, Icon, Icon);
        using (var path = Shapes.Rounded(square, Px(12)))
        using (var brush = new SolidBrush(toggle.Checked ? t.Accent : t.Soft))
            g.FillPath(brush, path);
        TextRenderer.DrawText(g, Glyphs.Power, Theme.Icons(16f, this), square, toggle.Checked ? t.OnAccent : t.Subtle,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
}

// A picture of the corner of the taskbar: ^ next to the clock picked out (1), the menu it opens
// with the padlock in it (2), and where to drag the padlock on the taskbar (3).
sealed class TaskbarPicture : KitControl
{
    public TaskbarPicture()
    {
        AccessibleRole = AccessibleRole.Graphic;
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
    }

    // The time on its clock. The render tool fixes it.
    internal static Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    public override Size GetPreferredSize(Size proposedSize) => new(Px(330), Px(150));

    float S => DeviceDpi / 96f;

    RectangleF P(float x, float y, float w, float h)
    {
        var box = new RectangleF(x * S, y * S, w * S, h * S);
        if (Flip) box.X = Width - box.Right;
        return box;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        Smooth(g);
        var area = P(0, 0, 330, 150);
        using (var frame = Shapes.Rounded(Shapes.Inside(Rectangle.Round(area)), 8 * S))
        {
            using var wash = new LinearGradientBrush(area, t.IsDark ? Color.FromArgb(0x1C, 0x2A, 0x26) : Color.FromArgb(0xEC, 0xF3, 0xF0),
                t.IsDark ? Color.FromArgb(0x24, 0x3B, 0x34) : Color.FromArgb(0xD3, 0xE5, 0xDE), LinearGradientMode.ForwardDiagonal);
            g.FillPath(wash, frame);
            var state = g.Save();
            g.SetClip(frame);

            // The taskbar, with a few apps, ^, the slot to drag to, the sound and the clock.
            var bar = P(0, 110, 330, 40);
            using (var fill = new SolidBrush(t.IsDark ? Color.FromArgb(0x1C, 0x1C, 0x1C) : Color.FromArgb(0xEE, 0xF1, 0xF4))) g.FillRectangle(fill, bar);
            using (var pen = new Pen(t.Edge)) g.DrawLine(pen, bar.Left, bar.Top, bar.Right, bar.Top);
            using (var apps = new SolidBrush(t.IsDark ? Color.FromArgb(0x3A, 0x3A, 0x3A) : Color.FromArgb(0xD5, 0xDA, 0xE0)))
            {
                for (int i = 0; i < 4; i++)
                {
                    using var path = Shapes.Rounded(P(14 + i * 30, 119, 22, 22), 5 * S);
                    g.FillPath(apps, path);
                }
            }
            var chevron = P(150, 116, 28, 28);
            using (var path = Shapes.Rounded(chevron, 5 * S))
            {
                using var fill = new SolidBrush(t.Soft);
                g.FillPath(fill, path);
                using var pen = new Pen(t.Accent, 1.5f * S);
                g.DrawPath(pen, path);
            }
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            DrawText(g, Glyphs.Up, Icons(9f), Rectangle.Round(chevron), t.Text, flags);
            var slot = P(183, 118, 24, 24);
            using (var path = Shapes.Rounded(slot, 5 * S))
            using (var pen = new Pen(t.Accent, 1.5f * S) { DashStyle = DashStyle.Dash })
                g.DrawPath(pen, path);
            DrawText(g, Glyphs.Volume, Icons(9f), Rectangle.Round(P(214, 116, 22, 28)), t.Text, flags);
            DrawText(g, Lang.Current.ClockTime(Clock()), Body(8f), Rectangle.Round(P(238, 110, 88, 40)), t.Text, flags);

            // The menu ^ opens, with the padlock picked out.
            var menu = P(110, 14, 120, 80);
            using (var shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillRectangle(shadow, new RectangleF(menu.X + 2 * S, menu.Y + 3 * S, menu.Width, menu.Height));
            using (var path = Shapes.Rounded(menu, 8 * S))
            {
                using var fill = new SolidBrush(t.IsDark ? Color.FromArgb(0x2C, 0x2C, 0x2C) : Color.FromArgb(0xF8, 0xF8, 0xF8));
                g.FillPath(fill, path);
                using var pen = new Pen(t.Edge);
                g.DrawPath(pen, path);
            }
            string[] others = { Glyphs.Globe, Glyphs.Folder, Glyphs.Gear, Glyphs.Document, Glyphs.Screen };
            int other = 0;
            for (int i = 0; i < 6; i++)
            {
                var cell = P(118 + (i % 3) * 36, 22 + (i / 3) * 34, 32, 30);
                if (i == 3)
                {
                    using (var path = Shapes.Rounded(cell, 5 * S))
                    {
                        using var fill = new SolidBrush(t.Soft);
                        g.FillPath(fill, path);
                        using var pen = new Pen(t.Accent, 1.5f * S);
                        g.DrawPath(pen, path);
                    }
                    int icon = (int)Math.Round(16 * S);
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    using (var padlock = TrayIcons.Art(TrayIconState.On, icon, t.IsDark))
                    {
                        if (IsMirrored) padlock.RotateFlip(RotateFlipType.RotateNoneFlipX);
                        g.DrawImage(padlock, (int)(cell.X + (cell.Width - icon) / 2), (int)(cell.Y + (cell.Height - icon) / 2), icon, icon);
                    }
                    Smooth(g);
                    continue;
                }
                DrawText(g, others[other++], Icons(9f), Rectangle.Round(cell), t.Subtle, flags);
            }

            // The way from the padlock to the slot.
            using (var pen = new Pen(t.Accent, 1.5f * S) { DashStyle = DashStyle.Dash, EndCap = LineCap.ArrowAnchor })
            {
                var from = Point(134, 84);
                var to = Point(193, 114);
                g.DrawBezier(pen, from, Point(150, 100), Point(188, 92), to);
            }
            g.Restore(state);
        }
        Badge(g, t, P(144, 128, 18, 18), "1");
        Badge(g, t, P(96, 64, 18, 18), "2");
        Badge(g, t, P(212, 88, 18, 18), "3");
    }

    PointF Point(float x, float y) => new(Flip ? Width - x * S : x * S, y * S);

    // A numbered circle, the same number as in the steps beside it.
    void Badge(Graphics g, Theme t, RectangleF box, string number)
    {
        using (var ring = new SolidBrush(t.Card)) g.FillEllipse(ring, RectangleF.Inflate(box, 2 * S, 2 * S));
        using (var fill = new SolidBrush(t.Accent)) g.FillEllipse(fill, box);
        TextRenderer.DrawText(g, number, Strong(8f), Rectangle.Round(box), t.OnAccent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
}

// The three steps under the taskbar picture, each with its number.
sealed class TraySteps : Panel
{
    readonly Label[] steps;

    public TraySteps(params string[] lines)
    {
        var t = Theme.Current;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        AccessibleName = "";
        steps = lines.Select(line => new Label { Text = line, AutoSize = false, UseMnemonic = false, BackColor = Color.Transparent, ForeColor = t.Text, Font = Theme.Body(9.75f) }).ToArray();
        Controls.AddRange(steps);
    }

    int Px(int logical) => LogicalToDeviceUnits(logical);

    int TextLeft => Px(28);

    int Gap => Px(6);

    int StepHeight(Label step, int width) => Math.Max(Px(20), Shapes.WrappedHeight(step.Text, step.Font, Math.Max(1, width - TextLeft)));

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        return new Size(width, steps.Sum(s => StepHeight(s, width)) + Gap * (steps.Length - 1));
    }

    bool Flip => RightToLeft == RightToLeft.Yes && !IsMirrored;

    Rectangle Box(int x, int y, int width, int height) => new(Flip ? Width - x - width : x, y, width, height);

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int y = 0;
        foreach (var step in steps)
        {
            int h = StepHeight(step, Width);
            step.Bounds = Box(TextLeft, y, Math.Max(1, Width - TextLeft), h);
            y += h + Gap;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var t = Theme.Current;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (int i = 0; i < steps.Length; i++)
        {
            int size = Px(18);
            var box = Box(0, steps[i].Top + (Math.Min(steps[i].Height, Px(20)) - size) / 2, size, size);
            using (var fill = new SolidBrush(t.Accent)) g.FillEllipse(fill, box);
            TextRenderer.DrawText(g, (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Theme.Strong(8f, this), box, t.OnAccent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }
}

// The picture and its steps, side by side with what was picked.
sealed class TwoColumns : Panel
{
    readonly Control left;
    readonly Control under;
    readonly Control right;

    public TwoColumns(Control left, Control under, Control right)
    {
        this.left = left;
        this.under = under;
        this.right = right;
        BackColor = Theme.Current.Window;
        AccessibleName = "";
        Controls.AddRange([left, under, right]);
    }

    int Px(int logical) => LogicalToDeviceUnits(logical);

    int LeftWidth => left.GetPreferredSize(Size.Empty).Width;

    int Gap => Px(20);

    int RightWidth(int width) => Math.Max(Px(200), width - LeftWidth - Gap);

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        int leftHeight = left.GetPreferredSize(Size.Empty).Height + Px(12) + under.GetPreferredSize(new Size(LeftWidth, 0)).Height;
        return new Size(width, Math.Max(leftHeight, right.GetPreferredSize(new Size(RightWidth(width), 0)).Height));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        bool flip = RightToLeft == RightToLeft.Yes && !IsMirrored;
        Rectangle Box(int x, int y, int w, int h) => new(flip ? Width - x - w : x, y, w, h);
        var size = left.GetPreferredSize(Size.Empty);
        left.Bounds = Box(0, 0, size.Width, size.Height);
        under.Bounds = Box(0, size.Height + Px(12), size.Width, under.GetPreferredSize(new Size(size.Width, 0)).Height);
        int rightWidth = RightWidth(Width);
        right.Bounds = Box(Width - rightWidth, 0, rightWidth, right.GetPreferredSize(new Size(rightWidth, 0)).Height);
    }
}

// "What you picked": the welcome's choices in groups, each a name and what it is set to.
sealed class PickedList : Card
{
    readonly Label heading;
    readonly List<Control> lines = new();

    public PickedList(string headingText)
    {
        var t = Theme.Current;
        Outline = theme => theme.Edge;
        heading = new Label { Text = headingText, AutoSize = false, UseMnemonic = false, BackColor = Color.Transparent, ForeColor = t.Text, Font = Theme.Strong(10f) };
        Controls.Add(heading);
    }

    // The groups and their rows: a group's name, then each setting's name and value.
    public void ShowGroups(IEnumerable<(string Group, IEnumerable<(string Name, string Value)> Rows)> groups)
    {
        SuspendLayout();
        foreach (var line in lines)
        {
            Controls.Remove(line);
            line.Dispose();
        }
        lines.Clear();
        var t = Theme.Current;
        foreach (var (group, rows) in groups)
        {
            lines.Add(new Label { Text = group, AutoSize = false, UseMnemonic = false, BackColor = Color.Transparent, ForeColor = t.Accent, Font = Theme.Strong(9f) });
            foreach (var (name, value) in rows) lines.Add(new PickedRow(name, value));
        }
        Controls.AddRange(lines.ToArray());
        ResumeLayout(false);
        PerformLayout();
    }

    internal IEnumerable<PickedRow> Rows => lines.OfType<PickedRow>();

    int Side => Px(14);

    int Measure(int width, bool place)
    {
        int inner = Math.Max(1, width - 2 * Side);
        int y = Px(12);
        int h = Shapes.WrappedHeight(heading.Text, heading.Font, inner);
        if (place) heading.Bounds = Box(Side, y, inner, h);
        y += h;
        foreach (var line in lines)
        {
            if (line is Label { } label)
            {
                y += Px(10);
                int lh = Shapes.WrappedHeight(label.Text, label.Font, inner);
                if (place) label.Bounds = Box(Side, y, inner, lh);
                y += lh + Px(2);
            }
            else
            {
                int rh = line.GetPreferredSize(new Size(inner, 0)).Height;
                if (place) line.Bounds = Box(Side, y, inner, rh);
                y += rh;
            }
        }
        return y + Px(12);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        return new Size(width, Measure(width, place: false));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        Measure(Width, place: true);
    }
}

// One row of the summary: the setting's name, and what it is set to at the end of the line, or under
// it when the two do not fit side by side.
sealed class PickedRow : KitControl
{
    public PickedRow(string name, string value)
    {
        Name_ = name;
        Value = value;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = name + ": " + value;
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
    }

    public string Name_ { get; }

    public string Value { get; }

    Font NameFont => Body(9f);

    Font ValueFont => Strong(9f);

    const TextFormatFlags Flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

    (int Name, int Value, int Line) Sizes()
    {
        var name = TextRenderer.MeasureText(Name_, NameFont, Size.Empty, Flags);
        var value = TextRenderer.MeasureText(Value, ValueFont, Size.Empty, Flags);
        return (name.Width, value.Width, Math.Max(name.Height, value.Height) + Px(4));
    }

    bool SideBySide(int width)
    {
        var (name, value, _) = Sizes();
        return name + Px(12) + value <= width;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        var (_, _, line) = Sizes();
        return new Size(width, SideBySide(width) ? line : 2 * line);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        var (name, value, line) = Sizes();
        var flags = Flags | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        Write(g, Name_, NameFont, Box(0, 0, Math.Min(name + 1, Width), line), t.Subtle, flags);
        if (SideBySide(Width)) Write(g, Value, ValueFont, Box(Width - value - 1, 0, value + 1, line), t.Text, flags);
        else Write(g, Value, ValueFont, Box(Px(12), line, Math.Min(value + 1, Width - Px(12)), line), t.Text, flags);
    }
}

// The step dots in the footer, and "Step 2 of 4" after them.
sealed class StepDots : KitControl
{
    int step = 1;
    int steps = 4;
    string words = "";

    public StepDots()
    {
        AccessibleRole = AccessibleRole.StaticText;
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
    }

    public void Show(int step, int steps, string words)
    {
        this.step = step;
        this.steps = steps;
        this.words = words;
        AccessibleName = words;
        Invalidate();
    }

    internal int Step => step;

    Font Face => Body(9.5f);

    int DotsWidth => Px(22) * (steps - 1) + Px(34) + Px(5) * (steps - 1);

    public override Size GetPreferredSize(Size proposedSize) =>
        new(DotsWidth + Px(12) + TextRenderer.MeasureText(words, Face, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + 1,
            Math.Max(Px(20), TextRenderer.MeasureText("Ag", Face).Height));

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        Smooth(g);
        int x = 0;
        int height = Px(5);
        for (int i = 1; i <= steps; i++)
        {
            int width = Px(i == step ? 34 : 22);
            var box = Box(x, (Height - height) / 2, width, height);
            using var path = Shapes.Rounded(box, height / 2f);
            using var brush = new SolidBrush(i <= step ? t.Accent : t.Line);
            g.FillPath(brush, path);
            x += width + Px(5);
        }
        Write(g, words, Face, Box(DotsWidth + Px(12), 0, Width - DotsWidth - Px(12), Height), t.Subtle,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
}

// The footer: the step dots at the start, and the buttons at the end, the main one last.
sealed class WelcomeFooter : Panel
{
    readonly StepDots dots;
    readonly KitButton[] buttons;

    public WelcomeFooter(StepDots dots, params KitButton[] buttons)
    {
        this.dots = dots;
        this.buttons = buttons;
        BackColor = Theme.Current.Window;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        AccessibleName = "";
        Controls.Add(dots);
        Controls.AddRange(buttons);
    }

    int Px(int logical) => LogicalToDeviceUnits(logical);

    // Buttons left out on this page. Visible reads false for every control of a window not on show.
    public HashSet<KitButton> Hidden { get; } = new();

    public override Size GetPreferredSize(Size proposedSize)
    {
        int height = buttons.Max(b => b.GetPreferredSize(Size.Empty).Height);
        return new Size(proposedSize.Width > 0 ? proposedSize.Width : Width, height + Px(28));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        bool flip = RightToLeft == RightToLeft.Yes && !IsMirrored;
        Rectangle Box(int x, int y, int w, int h) => new(flip ? Width - x - w : x, y, w, h);
        int right = Width - Px(24);
        for (int i = buttons.Length - 1; i >= 0; i--)
        {
            var button = buttons[i];
            button.Visible = !Hidden.Contains(button);
            if (Hidden.Contains(button)) continue;
            var size = button.GetPreferredSize(Size.Empty);
            int width = Math.Max(Px(88), size.Width);
            right -= width;
            button.Bounds = Box(right, (Height - size.Height) / 2, width, size.Height);
            right -= Px(8);
        }
        var dotsSize = dots.GetPreferredSize(Size.Empty);
        dots.Bounds = Box(Px(24), (Height - dotsSize.Height) / 2, Math.Min(dotsSize.Width, Math.Max(1, right - Px(24))), dotsSize.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Current.Line);
        e.Graphics.DrawLine(pen, 0, 0, Width, 0);
    }
}
