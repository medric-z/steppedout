using System.Drawing.Drawing2D;

namespace SteppedOut;

// Sizes for the tray menu at one scaling, in device pixels: the sketch's numbers (option 4), which
// are logical pixels at 100%, and the fonts at that scaling with the text size applied.
sealed class TrayMetrics
{
    public TrayMetrics(int dpi) => Dpi = dpi;

    public int Dpi { get; }

    public int Px(int logical) => (int)Math.Round(logical * Dpi / 96.0, MidpointRounding.AwayFromZero);

    // The menu: 252 wide, 4 around its rows inside its 1 px edge.
    public int MenuWidth => Px(252);
    public int Inset => Px(4);

    // A row: 32 high, 10 at each end, an 18 px glyph column, 10 between the glyph and the words.
    public int RowPad => Px(10);
    public int GlyphBox => Px(18);
    public int Gap => Px(10);
    public int Corner => Px(5);

    // The line between groups: 4 above and below, 8 in from the sides.
    public int LineHeight => Px(9);
    public int LineInset => Px(8);

    // The status box: 10 above and below, 12 at the sides, a 36 px ring, 12 between the ring and
    // the words, and 4 under the box.
    public int StatusPadY => Px(10);
    public int StatusPadX => Px(12);
    public int Ring => Px(SteppedOut.Ring.Small);
    public int StatusCorner => Px(6);

    // The pause list beside it: 188 wide, 5 away from the menu's edge.
    public int PauseWidth => Px(188);
    public int PauseGap => Px(5);

    public Font Text => Theme.Body(10f, Dpi);
    public Font Glyph => Theme.Icons(12f, Dpi);
    public Font Arrow => Theme.Icons(10.5f, Dpi);
    public Font Shortcut => Theme.Body(9f, Dpi);
    public Font Headline => Theme.Strong(9.75f, Dpi);
    public Font Small => Theme.Body(9f, Dpi);
    public Font SmallStrong => Theme.Strong(9f, Dpi);

    // As tall as the sketch's rows, or taller when the words are (Large text).
    public int RowHeight => Math.Max(Px(32), Text.Height + Px(10));

    // Where a row's parts go, inside a row as wide as the menu less its right edge. The menu's own
    // layout puts each row at its left edge, so the 1 px edge is left of the row's fill.
    public RowParts Row(int rowWidth, int rowHeight, bool rightToLeft, int trailWidth)
    {
        var fill = Rectangle.FromLTRB(1 + Inset, 0, Math.Max(1 + Inset, rowWidth - Inset), rowHeight);
        int x = fill.Left + RowPad;
        var glyph = new Rectangle(x, 0, GlyphBox, rowHeight);
        int end = fill.Right - RowPad;
        var trail = new Rectangle(end - trailWidth, 0, trailWidth, rowHeight);
        int textLeft = glyph.Right + Gap;
        int textRight = trailWidth > 0 ? trail.Left - Gap : end;
        var text = new Rectangle(textLeft, 0, Math.Max(0, textRight - textLeft), rowHeight);
        if (rightToLeft)
        {
            glyph = Mirror(glyph, fill);
            text = Mirror(text, fill);
            trail = Mirror(trail, fill);
        }
        return new RowParts(fill, glyph, text, trail);
    }

    // How wide the menu must be for a row's words and its arrow or shortcut.
    public int WidthFor(int textWidth, int trailWidth) =>
        1 + Inset + RowPad + GlyphBox + Gap + textWidth + (trailWidth > 0 ? Gap + trailWidth : 0) + RowPad + Inset + 1;

    // The status box's parts inside its item, top being how far down the box starts in the item.
    public StatusParts Status(int itemWidth, int top, bool rightToLeft, int textHeight)
    {
        int inner = Math.Max(Ring, textHeight);
        var box = new Rectangle(1 + Inset, top, Math.Max(0, itemWidth - (1 + Inset) - Inset), inner + 2 * StatusPadY + 2);
        var ring = new Rectangle(box.Left + 1 + StatusPadX, box.Top + (box.Height - Ring) / 2, Ring, Ring);
        int textLeft = ring.Right + StatusPadX;
        var text = new Rectangle(textLeft, box.Top + (box.Height - textHeight) / 2, Math.Max(0, box.Right - 1 - StatusPadX - textLeft), textHeight);
        if (rightToLeft)
        {
            ring = Mirror(ring, box);
            text = Mirror(text, box);
        }
        return new StatusParts(box, ring, text);
    }

    static Rectangle Mirror(Rectangle r, Rectangle within) => new(within.Left + within.Right - r.Right, r.Y, r.Width, r.Height);
}

readonly record struct RowParts(Rectangle Fill, Rectangle Glyph, Rectangle Text, Rectangle Trail);

readonly record struct StatusParts(Rectangle Box, Rectangle Ring, Rectangle Text);

// A row of the tray menu: a glyph, the words, and at the far end the arrow of a submenu or the
// lock shortcut. TrayMenu sizes it; TrayMenuLook draws it.
sealed class TrayRow : ToolStripMenuItem
{
    public TrayRow(string glyph) => Glyph = glyph;

    public string Glyph { get; }

    // Quit's glyph is gray rather than in the accent.
    public bool Muted { get; init; }

    internal TrayMetrics Metrics { get; set; } = new(Theme.SystemDpi);

    internal int RowWidth { get; set; }

    public override Size GetPreferredSize(Size constrainingSize) =>
        new(RowWidth > 0 ? RowWidth : base.GetPreferredSize(constrainingSize).Width, Metrics.RowHeight);

    // The width of what sits at the far end: the submenu arrow, or the shortcut.
    internal int TrailWidth
    {
        get
        {
            if (HasDropDownItems) return Metrics.Px(14);
            if (ShortcutKeyDisplayString is { Length: > 0 } keys) return Measure(keys, Metrics.Shortcut);
            return 0;
        }
    }

    internal RowParts Parts => Metrics.Row(Width, Height, RightToLeft == RightToLeft.Yes, TrailWidth);

    // Where this row's list opens, in screen pixels, when the menu places it itself. WinForms asks
    // each time the open list is placed, moved or sized, and keeps it there.
    internal Func<Point>? DropDownPlace { get; set; }

    protected override Point DropDownLocation =>
        DropDownPlace is { } place && Owner is not null && HasDropDownItems ? place() : base.DropDownLocation;

    // Right opens Pause's list in English and Left in Arabic, wherever the list then opens, as in
    // Windows' own menus. WinForms picks the key by the side the list opened on last time, so once
    // the list had opened to the left, Right would close the whole menu instead.
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (HasDropDownItems && Enabled && keyData == (RightToLeft == RightToLeft.Yes ? Keys.Left : Keys.Right))
        {
            ShowDropDown();
            DropDown.Items.Cast<ToolStripItem>().FirstOrDefault(i => i.Available && i.CanSelect)?.Select();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    internal static int Measure(string text, Font font) =>
        TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
}

// The line between Settings and Quit.
sealed class TrayLine : ToolStripSeparator
{
    internal TrayMetrics Metrics { get; set; } = new(Theme.SystemDpi);

    public override Size GetPreferredSize(Size constrainingSize) => new(1, Metrics.LineHeight);
}

// The status at the top of the tray menu: the ring, the headline ("On · locks in 28 min") and the
// time away today, in a box in the status box's colors: green as the sketch draws it, and for the
// states it does not draw today's amber and gray (D20). It is words, not a command: the arrow keys
// pass over it, clicking it does nothing and leaves the menu open, and a screen reader reads it as
// text. Drawn here rather than by hosting a control, which a menu drawn into a picture leaves out.
sealed class TrayStatusItem : ToolStripMenuItem
{
    public TrayStatusItem()
    {
        AccessibleRole = AccessibleRole.StaticText;
    }

    internal TrayMetrics Metrics { get; set; } = new(Theme.SystemDpi);

    internal int RowWidth { get; set; }

    public string Headline { get; private set; } = "";
    public string AwayLabel { get; private set; } = "";
    public string AwayValue { get; private set; } = "";
    public float Fraction { get; private set; }
    public Tone Tone { get; private set; } = Tone.On;

    public override bool CanSelect => false;

    protected override bool DismissWhenClicked => false;

    // What a screen reader reads, and what TrayMenu puts on the first row as well.
    public string Spoken => AwayLabel.Length == 0 ? Headline : $"{Headline} · {AwayLabel} {AwayValue}";

    internal void Show(string headline, string awayLabel, string awayValue, float fraction, Tone tone = Tone.On)
    {
        if (headline == Headline && awayLabel == AwayLabel && awayValue == AwayValue && fraction == Fraction && tone == Tone) return;
        Headline = headline;
        AwayLabel = awayLabel;
        AwayValue = awayValue;
        Fraction = fraction;
        Tone = tone;
        AccessibleName = headline;
        AccessibleDescription = awayLabel.Length == 0 ? null : $"{awayLabel} {awayValue}";
        Invalidate();
    }

    // The settings status box's tones: green while it counts toward the lock, and while something
    // keeps it from locking, as the sketch draws "won't lock during a call"; amber while the
    // countdown runs; gray while paused, away or locked. A dry run takes the tone of what it shows.
    internal static Tone ToneOf(LockStatus status) => status.Kind switch
    {
        LockState.Countdown => Tone.Busy,
        LockState.Paused or LockState.Away or LockState.SessionLocked => Tone.Off,
        _ => Tone.On,
    };

    // The box's fill and edge for a tone. The gray box is the window color, so it shows on the
    // menu's card color. High contrast keeps the system's menu colors in every tone.
    internal static (Color Fill, Color Edge) Colors(Tone tone)
    {
        if (TrayMenuLook.Hc) return (SystemColors.Menu, SystemColors.MenuText);
        var t = Theme.Current;
        return tone switch
        {
            Tone.Busy => (t.WarningSoft, t.Warning),
            Tone.Off => (t.Window, t.Edge),
            _ => (t.Hero, t.HeroEdge),
        };
    }

    // How far down the box starts: the menu keeps a little room above its first item, and the box
    // sits 4 px inside the menu's edge as the rows do.
    internal int Top => Math.Max(0, 1 + Metrics.Inset - (Owner?.Padding.Top ?? 2));

    // The headline's line: the font's height, or more where the lowest marks reach further, such as
    // the tails of g and p at some scalings and, in Arabic, the two marks under the last letter of
    // "seconds" in the countdown.
    int HeadlineHeight => Math.Max(Metrics.Headline.Height, Theme.LowestMarks(Metrics.Headline, RightToLeft == RightToLeft.Yes).Depth);

    int TextHeight => HeadlineHeight + Metrics.Px(2) + Metrics.Small.Height;

    public override Size GetPreferredSize(Size constrainingSize)
    {
        var parts = Metrics.Status(Math.Max(RowWidth, 1), Top, false, TextHeight);
        return new(RowWidth > 0 ? RowWidth : Metrics.MenuWidth - 1, parts.Box.Bottom + Metrics.Inset);
    }

    internal StatusParts Parts => Metrics.Status(Width, Top, RightToLeft == RightToLeft.Yes, TextHeight);

    // How wide the menu must be for both lines to fit beside the ring.
    internal int NeededMenuWidth
    {
        get
        {
            var m = Metrics;
            int away = AwayLabel.Length == 0 ? 0 : TrayRow.Measure(AwayLabel, m.Small) + TrayRow.Measure(" ", m.Small) + m.Px(1) + TrayRow.Measure(AwayValue, m.SmallStrong);
            int words = Math.Max(TrayRow.Measure(Headline, m.Headline), away);
            return 2 * (1 + m.Inset + 1 + m.StatusPadX) + m.Ring + m.StatusPadX + words;
        }
    }

    // Where the headline and the away line go inside the text column, and the headline as drawn:
    // cut with "…" when it does not fit, as a long app name in "won't lock while … is open" would.
    internal (Rectangle Headline, Rectangle Label, Rectangle Value) Lines()
    {
        var text = Parts.Text;
        bool rtl = RightToLeft == RightToLeft.Yes;
        // Drawn from its top, so the room for the marks is under it.
        int first = HeadlineHeight;
        var headline = new Rectangle(text.X, text.Y, text.Width, first);
        int y = text.Y + first + Metrics.Px(2);
        int h = Metrics.Small.Height;
        int labelWidth = Math.Min(text.Width, TrayRow.Measure(AwayLabel, Metrics.Small));
        int space = AwayLabel.Length == 0 ? 0 : TrayRow.Measure(" ", Metrics.Small) + Metrics.Px(1);
        int valueWidth = Math.Max(0, Math.Min(TrayRow.Measure(AwayValue, Metrics.SmallStrong), text.Width - labelWidth - space));
        var label = new Rectangle(rtl ? text.Right - labelWidth : text.X, y, labelWidth, h);
        var value = new Rectangle(rtl ? label.Left - space - valueWidth : label.Right + space, y, valueWidth, h);
        return (headline, label, value);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var parts = Parts;
        bool hc = TrayMenuLook.Hc;
        bool rtl = RightToLeft == RightToLeft.Yes;
        var theme = hc ? TrayMenuLook.HighContrastTheme : Theme.Current;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Shapes.Rounded(new RectangleF(parts.Box.X + 0.5f, parts.Box.Y + 0.5f, parts.Box.Width - 1, parts.Box.Height - 1), Metrics.StatusCorner))
        {
            var colors = Colors(Tone);
            using var fill = new SolidBrush(colors.Fill);
            g.FillPath(fill, path);
            using var edge = new Pen(colors.Edge);
            g.DrawPath(edge, path);
        }
        g.SmoothingMode = SmoothingMode.None;
        SteppedOut.Ring.Draw(g, parts.Ring, Fraction, rtl, mirrored: false, theme);
        var (headline, label, value) = Lines();
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter
            | (rtl ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left);
        var fore = TrayMenuLook.Fore;
        TextRenderer.DrawText(g, Headline, Metrics.Headline, headline, fore, (flags & ~TextFormatFlags.VerticalCenter) | TextFormatFlags.Top);
        TextRenderer.DrawText(g, AwayLabel, Metrics.Small, label, TrayMenuLook.Muted, flags);
        TextRenderer.DrawText(g, AwayValue, Metrics.SmallStrong, value, fore, flags);
    }
}

// Draws the tray menu in the settings colors (D24): the menu itself as MenuLook draws every menu,
// and its own rows as the sketch has them: a glyph in the accent (Quit's in gray), the words, and a
// gray arrow or shortcut at the far end, which always reads left to right. MenuLook is sealed, so
// this wraps one and passes it what it draws as it is.
sealed class TrayMenuLook : ToolStripRenderer
{
    readonly MenuLook look = new();

    // The chevron pointing left, for a submenu in a right-to-left menu.
    const string LeftArrow = "\uE76B";

    internal static bool Hc => MenuLook.HighContrastForTests ?? SystemInformation.HighContrast;

    internal static Theme HighContrastTheme => Theme.HighContrastOf(SystemColors.Menu, SystemColors.MenuText, SystemColors.Highlight, SystemColors.HighlightText);

    internal static Color Back => Hc ? SystemColors.Menu : Theme.Current.Card;
    internal static Color Fore => Hc ? SystemColors.MenuText : Theme.Current.Text;
    internal static Color Muted => Hc ? SystemColors.GrayText : Theme.Current.Subtle;
    internal static Color Hover => Hc ? SystemColors.Highlight : Theme.Current.FieldHover;

    static Color Ink(ToolStripItem item, Color normal) => Hc && item is { Selected: true, Enabled: true } ? SystemColors.HighlightText : normal;

    internal static Color GlyphColor(TrayRow row) => Ink(row, row.Muted ? Muted : Hc ? Fore : Theme.Current.Accent);

    internal static Color TrailColor(TrayRow row) => Ink(row, Muted);

    // The shortcut is drawn without the right-to-left flag, so "Ctrl+Alt+L" keeps its order in Arabic.
    internal static TextFormatFlags ShortcutFlags =>
        TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.Left;

    internal static TextFormatFlags TextFlags(bool rightToLeft) =>
        TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
        | (rightToLeft ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left);

    protected override void Initialize(ToolStrip toolStrip)
    {
        base.Initialize(toolStrip);
        toolStrip.BackColor = Back;
        toolStrip.ForeColor = Fore;
    }

    protected override void InitializeItem(ToolStripItem item)
    {
        base.InitializeItem(item);
        item.ForeColor = Fore;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => look.DrawToolStripBackground(e);

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) => look.DrawToolStripBorder(e);

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) => look.DrawImageMargin(e);

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) => look.DrawItemCheck(e);

    // A row of this menu is drawn whole here: its fill under the pointer, glyph, words and arrow or
    // shortcut. Anything else goes to MenuLook.
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is TrayRow row) DrawRow(e.Graphics, row);
        else look.DrawMenuItemBackground(e);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is not TrayRow) look.DrawItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        if (e.Item is not TrayRow) look.DrawArrow(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        if (e.Item is not TrayLine line || line.Owner is not { } menu)
        {
            look.DrawSeparator(e);
            return;
        }
        var m = line.Metrics;
        int left = 1 + m.Inset + m.LineInset - line.Bounds.X;
        int right = menu.Width - 1 - m.Inset - m.LineInset - line.Bounds.X;
        int y = line.Height / 2;
        using var pen = new Pen(Hc ? SystemColors.MenuText : Theme.Current.Line);
        e.Graphics.DrawLine(pen, left, y, right - 1, y);
    }

    static void DrawRow(Graphics g, TrayRow row)
    {
        var m = row.Metrics;
        var parts = row.Parts;
        bool rtl = row.RightToLeft == RightToLeft.Yes;
        if (row.Selected && row.Enabled)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var fill = new RectangleF(parts.Fill.X, parts.Fill.Y + 0.5f, parts.Fill.Width, parts.Fill.Height - 1);
            using (var path = Shapes.Rounded(fill, m.Corner))
            using (var brush = new SolidBrush(Hover))
                g.FillPath(brush, path);
            // And the accent bar at its start, as in every list (MenuLook.Mark).
            MenuLook.Mark(g, fill, rtl, m.Dpi, Hc);
            g.SmoothingMode = SmoothingMode.None;
        }
        var center = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
        TextRenderer.DrawText(g, row.Glyph, m.Glyph, parts.Glyph, GlyphColor(row), center);
        TextRenderer.DrawText(g, row.Text ?? "", m.Text, parts.Text, MenuLook.TextColor(row), TextFlags(rtl));
        if (row.HasDropDownItems)
            TextRenderer.DrawText(g, rtl ? LeftArrow : Glyphs.Right, m.Arrow, parts.Trail, TrailColor(row), center);
        else if (row.ShortcutKeyDisplayString is { Length: > 0 } keys)
            TextRenderer.DrawText(g, keys, m.Shortcut, parts.Trail, TrailColor(row), ShortcutFlags);
    }
}
