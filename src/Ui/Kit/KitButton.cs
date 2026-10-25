using System.Drawing.Drawing2D;

namespace SteppedOut;

// A rounded push button. A primary one is filled with the accent color; a selected one is outlined
// in it, for a row of choices like "5 min / 10 min / 15 min".
sealed class KitButton : Button
{
    bool hovered;
    bool pressed;
    bool selected;
    bool chevron;
    bool small;
    string? glyph;
    Automation.Provider? provider;

    public KitButton(string text, bool primary = false)
    {
        Text = text;
        Primary = primary;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        // Button turns Opaque on, which skips the background, so the corners outside the rounded
        // shape kept whatever was drawn there before.
        SetStyle(ControlStyles.Opaque, false);
        BackColor = Color.Transparent;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        UseMnemonic = false;
        Size = GetPreferredSize(Size.Empty);
    }

    public bool Primary { get; set; }

    // One of a row of choices, such as the times. Screen readers hear it as a toggle button, on while
    // it is the selected one, where a plain button could not say which one is chosen.
    public bool Choice { get; init; }

    public bool Selected
    {
        get => selected;
        set
        {
            if (selected == value) return;
            selected = value;
            Invalidate();
            Automation.Notify(this, AccessibleEvents.StateChange);
            if (Automation.Heard(this)) Automation.ToggleChanged(AutomationProvider, value);
        }
    }

    // The chevron the drop-down lists have, after the text of a button that opens a menu.
    public bool Chevron
    {
        get => chevron;
        set
        {
            chevron = value;
            Size = GetPreferredSize(Size.Empty);
            Invalidate();
        }
    }

    int ChevronRoom => chevron ? LogicalToDeviceUnits(20) : 0;

    // The sketch's small button: 26 px tall, 10 px each side of its words, which are a size smaller.
    public bool Small
    {
        get => small;
        set
        {
            small = value;
            Size = GetPreferredSize(Size.Empty);
            Invalidate();
        }
    }

    // An icon before the words, such as the + of "Add app". Not read out: the words say it.
    public string? Glyph
    {
        get => glyph;
        set
        {
            glyph = value;
            Size = GetPreferredSize(Size.Empty);
            Invalidate();
        }
    }

    float TextSize => small ? 9f : 10f;

    Font GlyphFont => Theme.Icons(small ? 9f : 9.75f, this);

    int GlyphRoom => glyph is null ? 0 : TextRenderer.MeasureText(glyph, GlyphFont, Size.Empty, TextFormatFlags.NoPadding).Width + LogicalToDeviceUnits(6);

    // Tells screen readers to read the name again after it was changed in place.
    public void NameChanged() => Automation.Notify(this, AccessibleEvents.NameChange);

    protected override AccessibleObject CreateAccessibilityInstance() =>
        Choice ? new ChoiceAccessible(this) : base.CreateAccessibilityInstance();

    // WinForms' own UI Automation answer for a button has no way to say it is on, so a choice gives
    // its own, as the hand-drawn controls do.
    internal Automation.Provider? AutomationProvider => !Choice ? null : provider ??= new(this, Automation.ButtonType)
    {
        Press = PerformClick,
        IsOn = () => selected,
    };

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

    // A small button, the settings' look, is measured as the sketch draws it: its words in the face
    // they are drawn in, without the room Windows adds at their ends, and 10 px each side. The
    // others, the welcome's among them, keep their size: the words in semibold whatever the state,
    // so choosing a button never changes its width, with Windows' room at their ends.
    public override Size GetPreferredSize(Size proposedSize)
    {
        if (small)
        {
            var words = TextRenderer.MeasureText(Text, SmallFace, Size.Empty, Tight);
            return new Size(words.Width + LogicalToDeviceUnits(20) + GlyphRoom + ChevronRoom, Math.Max(LogicalToDeviceUnits(26), words.Height + LogicalToDeviceUnits(8)));
        }
        var text = TextRenderer.MeasureText(Text, Theme.Strong(TextSize, this));
        return new Size(text.Width + LogicalToDeviceUnits(36) + GlyphRoom + ChevronRoom, Math.Max(LogicalToDeviceUnits(34), text.Height + LogicalToDeviceUnits(14)));
    }

    const TextFormatFlags Tight = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

    // A small button is never a primary one or one of a row of choices, so its words are in the
    // regular face; if it were, they would be in semibold as drawn.
    Font SmallFace => Primary || Choice ? Theme.Strong(TextSize, this) : Theme.Body(TextSize, this);

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // Pixel centres at half pixels, so a border inset by half its width sits on whole pixels
        // rather than half outside the button.
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float border = selected ? LogicalToDeviceUnits(2) : 1;
        var box = Shapes.Stroke(new Rectangle(0, 0, Width, Height), border);
        float radius = LogicalToDeviceUnits(6);

        Color fill = Primary ? t.Accent : selected ? t.AccentSoft : t.Field;
        Color edge = Primary || selected ? t.Accent : t.FieldLine;
        if (!Enabled) fill = t.Card;
        else if (pressed) fill = Primary ? ControlPaint.Dark(t.Accent, 0.05f) : t.Line;
        else if (hovered && (Primary || !selected)) fill = Primary ? ControlPaint.Light(t.Accent, 0.15f) : t.FieldHover;

        using (var path = Shapes.Rounded(box, radius))
        {
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
            using var pen = new Pen(edge, border);
            g.DrawPath(pen, path);
        }

        var font = Primary || selected ? Theme.Strong(TextSize, this) : Theme.Body(TextSize, this);
        var color = !Enabled ? t.Subtle : Primary ? t.OnAccent : t.Text;
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
        // Child controls are not mirrored with the window, so in Arabic the button puts the chevron
        // on its left itself, where the drop-down lists have theirs.
        bool flip = RightToLeft == RightToLeft.Yes && !IsMirrored;
        if (glyph is null)
        {
            TextRenderer.DrawText(g, Text, font, new Rectangle(flip ? ChevronRoom : 0, 0, Width - ChevronRoom, Height), color, flags);
        }
        else
        {
            // The icon and the words centered together, the icon first in reading order, the words
            // measured and drawn without Windows' room at their ends, so the two sides match.
            int words = TextRenderer.MeasureText(Text, font, Size.Empty, Tight).Width;
            int room = Width - ChevronRoom;
            int start = Math.Max(0, (room - GlyphRoom - words) / 2);
            Rectangle Mirror(int x, int w) => new(flip ? Width - x - w : x, 0, w, Height);
            var glyphBox = Mirror(start, GlyphRoom - LogicalToDeviceUnits(6));
            TextRenderer.DrawText(g, glyph, GlyphFont, glyphBox, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Text, font, Mirror(start + GlyphRoom, Math.Min(words, room - start - GlyphRoom)), color, flags | TextFormatFlags.NoPadding);
        }
        if (chevron)
        {
            int chevronX = flip ? LogicalToDeviceUnits(18) : Width - LogicalToDeviceUnits(30);
            TextRenderer.DrawText(g, Glyphs.Down, Theme.Icons(8f, this), new Rectangle(chevronX, 0, LogicalToDeviceUnits(12), Height),
                Primary ? t.OnAccent : t.Subtle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        if (Focused && ShowFocusCues)
        {
            // On the accent fill of a primary button the text color is too faint to see as a ring.
            int ringWidth = LogicalToDeviceUnits(2);
            using var ring = new Pen(Primary ? t.OnAccent : t.Text, ringWidth);
            var inner = Rectangle.Inflate(new Rectangle(0, 0, Width, Height), -ringWidth, -ringWidth);
            using var path = Shapes.Rounded(Shapes.Stroke(inner, ringWidth), radius - 1);
            g.DrawPath(ring, path);
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hovered = false;
        pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        pressed = mevent.Button == MouseButtons.Left;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Size = GetPreferredSize(Size.Empty);
    }

    // To MSAA a choice is a button that is pressed while it is the selected one.
    sealed class ChoiceAccessible(KitButton owner) : ButtonBaseAccessibleObject(owner)
    {
        public override AccessibleStates State =>
            base.State | (owner.selected ? AccessibleStates.Pressed : AccessibleStates.None);
    }
}
