namespace SteppedOut;

// One line of settings: an icon, a title with a short explanation under it, and the control that
// changes it at the far end. Clicking anywhere on a row that ends in a switch flips the switch.
class SettingRow : Card
{
    // Drawn rather than held in a Label: screen readers would meet a blank item before every title.
    string? glyph;
    readonly RowLabel title;
    readonly RowLabel detail;
    readonly Control? control;
    RowLook look;
    Control? below;
    bool detailWarns;

    public SettingRow(string? glyph, string titleText, string? detailText = null, Control? control = null)
    {
        var t = Theme.Current;
        this.glyph = glyph;
        title = new RowLabel { Text = titleText, Font = Theme.Body(10.5f), ForeColor = t.Text, BackColor = Color.Transparent, UseMnemonic = false };
        detail = new RowLabel { Text = detailText ?? "", Font = Theme.Body(9.5f), ForeColor = t.Subtle, BackColor = Color.Transparent, UseMnemonic = false };
        detail.Visible = !string.IsNullOrEmpty(detailText);
        Controls.Add(title);
        Controls.Add(detail);
        this.control = control;
        if (control is not null)
        {
            Controls.Add(control);
            // A button keeps its own words after the row's, so a spoken "click Open" still finds it.
            control.AccessibleName ??= control is ButtonBase ? titleText + ". " + control.Text : titleText;
            // Tabbing to the control reads the explanation under the title too.
            control.AccessibleDescription ??= detailText;
            Automation.HelpFromDescription(control);
        }
        if (control is Toggle toggle)
        {
            // The hand only while the switch can be changed, as the clicks below.
            Cursor = toggle.Enabled ? Cursors.Hand : Cursors.Default;
            toggle.EnabledChanged += (_, _) => Cursor = toggle.Enabled ? Cursors.Hand : Cursors.Default;
            foreach (Control part in new Control[] { this, title, detail })
            {
                part.Click += (_, _) =>
                {
                    // A switch that cannot be changed now stays as it is, whatever part is clicked.
                    if (!toggle.Enabled) return;
                    toggle.Focus();
                    toggle.Checked = !toggle.Checked;
                };
            }
        }
    }

    // For rows that open or reveal something rather than end in a switch.
    public SettingRow WhenClicked(Action action)
    {
        Cursor = Cursors.Hand;
        foreach (Control part in new Control[] { this, title, detail }) part.Click += (_, _) => action();
        return this;
    }

    public string Title => title.Text;

    // For a row that is sometimes joined under another, without an icon, and sometimes stands alone.
    public string? Glyph
    {
        get => glyph;
        set
        {
            glyph = value;
            Parent?.PerformLayout();
            PerformLayout();
            Invalidate();
        }
    }

    // Rows that belong to the one above, like "Wait up to" under "If a video is playing", are
    // indented and joined to it.
    public int Indent { get; set; }

    public string? Detail
    {
        get => detail.Text;
        set
        {
            detail.Text = value ?? "";
            detail.Visible = !string.IsNullOrEmpty(value);
            if (control is not null) control.AccessibleDescription = value;
            Parent?.PerformLayout();
            PerformLayout();
        }
    }

    // The words under the title say something went wrong, in the warning color, rather than explain
    // the row.
    public bool DetailWarns
    {
        get => detailWarns;
        set
        {
            if (detailWarns == value) return;
            detailWarns = value;
            detail.ForeColor = value ? Theme.Current.Warning : Theme.Current.Subtle;
            detail.Invalidate();
        }
    }

    // Says a note that just came under the title to a screen reader, as the focus stays where it is.
    protected void SayNote(string note)
    {
        var target = control ?? this;
        if (!target.IsHandleCreated) return;
        target.AccessibilityObject.RaiseAutomationNotification(
            System.Windows.Forms.Automation.AutomationNotificationKind.Other,
            System.Windows.Forms.Automation.AutomationNotificationProcessing.ImportantMostRecent,
            note);
    }

    // A note in the warning color in place of the row's explanation, which comes back with null.
    public void Note(string? note, string? explanation)
    {
        Detail = note ?? explanation;
        DetailWarns = note is not null;
        if (note is not null) SayNote(note);
    }

    // How the row is drawn. Classic is the welcome's look and stays the default.
    public RowLook Look
    {
        get => look;
        set
        {
            if (look == value) return;
            look = value;
            var (titleSize, detailSize) = value == RowLook.Classic ? (10.5f, 9.5f) : (9.75f, 9f);
            title.Font = Theme.Body(titleSize, this);
            detail.Font = Theme.Body(detailSize, this);
            title.Tight = detail.Tight = value != RowLook.Classic;
            Radius = value == RowLook.LookC ? 6 : 8;
            Outline = value == RowLook.LookC ? t => t.Edge : null;
            Flat = value == RowLook.Flat;
            // Look C rows stand 4 px apart, as in the sketch's stack of rows.
            if (value == RowLook.LookC) Margin = new Padding(Margin.Left, Px(4), Margin.Right, Margin.Bottom);
            Parent?.PerformLayout();
            PerformLayout();
            Invalidate();
        }
    }

    // A control under the explanation, in the words' column, such as the apps a row opens. A screen
    // reader names it after the row's title unless it has a name of its own.
    public Control? Below
    {
        get => below;
        set
        {
            if (below == value) return;
            if (below is not null) Controls.Remove(below);
            below = value;
            if (below is not null)
            {
                below.AccessibleName ??= title.Text;
                Controls.Add(below);
            }
            Parent?.PerformLayout();
            PerformLayout();
        }
    }

    // The padding at the sides and above and below, the gap between icon, words and control, and the
    // icon's size, at 100%.
    int PadX => look switch { RowLook.LookC => 16, RowLook.Flat => 0, _ => 18 };

    int PadY => look == RowLook.LookC ? 11 : 0;

    int Gap => look switch { RowLook.LookC => 16, RowLook.Flat => 12, _ => 14 };

    int IconSize => look switch { RowLook.LookC => 32, RowLook.Flat => 24, _ => 22 };

    int LineGap => Px(look == RowLook.Classic ? 2 : 1);

    // The icon's box: the look's size, or bigger when the text size makes the icon bigger, so it is
    // never cut; Look C's circle keeps 4 px around it. The welcome's look keeps one text size.
    int IconBox
    {
        get
        {
            if (glyph is null) return 0;
            if (look == RowLook.Classic) return Px(IconSize);
            var icon = TextRenderer.MeasureText(glyph, GlyphFont, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            int side = Math.Max(icon.Width, icon.Height) + (look == RowLook.LookC ? Px(8) : 0);
            return Math.Max(Px(IconSize), side);
        }
    }

    Font GlyphFont => Theme.Icons(look == RowLook.LookC ? 12.75f : 15f, this);

    // Where the icon is drawn, for the tests and the render tool's checks.
    internal Rectangle GlyphBox => glyph is null || look == RowLook.Classic ? Rectangle.Empty
        : Box(Px(PadX), HasBelow ? InsideTop : InsideTop + (InsideHeight - IconBox) / 2, IconBox, IconBox);

    internal Size GlyphSize => glyph is null ? Size.Empty
        : TextRenderer.MeasureText(glyph, GlyphFont, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

    // Where the words and the controls may go, top and height, inside the padding and the edge.
    int InsideTop => look == RowLook.LookC ? Px(PadY) + 1 : 0;

    int InsideHeight => Height - (look == RowLook.LookC ? Px(PadY) * 2 + 2 : 0);

    bool HasBelow => below is { Visible: true };

    int BelowHeight(int textWidth) => HasBelow ? Px(6) + below!.GetPreferredSize(new Size(textWidth, 0)).Height : 0;

    int TextHeight(int textWidth) =>
        title.HeightFor(textWidth)
        + (detail.Visible ? LineGap + detail.HeightFor(textWidth) : 0)
        + BelowHeight(textWidth);

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        var (_, textWidth) = TextColumn(width);
        int inner = Math.Max(TextHeight(textWidth), control is { Visible: true } ? ControlSize.Height : 0);
        int icon = IconBox;
        return look switch
        {
            // At least the sketch's 40 px inside, with 11 px above and below and the edge.
            RowLook.LookC => new Size(width, Math.Max(Px(40), Math.Max(inner, icon)) + Px(PadY) * 2 + 2),
            RowLook.Flat => new Size(width, Math.Max(inner, icon)),
            _ => new Size(width, Math.Max(Px(Indent > 0 ? 52 : 62), inner + Px(22))),
        };
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int pad = Px(PadX);
        var (left, textWidth) = TextColumn(Width);
        int titleHeight = title.HeightFor(textWidth);
        int detailHeight = detail.Visible ? detail.HeightFor(textWidth) : 0;
        int total = TextHeight(textWidth);
        // A row with something under its words lines up at the top, as the sketch's rows of apps do;
        // others are centered.
        int top = look == RowLook.Classic
            ? HasBelow ? Px(11) : (Height - total) / 2
            : HasBelow ? InsideTop : InsideTop + (InsideHeight - total) / 2;
        title.Bounds = Box(left, top, textWidth, titleHeight);
        detail.Bounds = Box(left, top + titleHeight + LineGap, textWidth, detailHeight);
        if (HasBelow)
        {
            int belowTop = top + titleHeight + (detail.Visible ? LineGap + detailHeight : 0) + Px(6);
            below!.Bounds = Box(left, belowTop, textWidth, below.GetPreferredSize(new Size(textWidth, 0)).Height);
        }
        if (control is not null)
        {
            var size = ControlSize;
            int controlTop = HasBelow ? top
                : look == RowLook.Classic ? (Height - size.Height) / 2
                : InsideTop + (InsideHeight - size.Height) / 2;
            control.Bounds = Box(Width - pad + ControlInset - size.Width, controlTop, size.Width, size.Height);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (glyph is null) return;
        var t = Theme.Current;
        if (look == RowLook.Classic)
        {
            TextRenderer.DrawText(e.Graphics, glyph, Theme.Icons(12f, this), Box(Px(18), (Height - Px(24)) / 2, Px(22), Px(24)), t.Subtle,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return;
        }
        var box = GlyphBox;
        if (look == RowLook.LookC)
        {
            // The sketch's green icon on a soft circle.
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(t.Soft);
            e.Graphics.FillEllipse(brush, box);
        }
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        KitControl.TextDrawn?.Invoke(this, glyph, GlyphFont, box, flags);
        TextRenderer.DrawText(e.Graphics, glyph, GlyphFont, box, t.Accent, flags);
    }

    // Where the title and explanation go: after the icon, before the control.
    (int Left, int Width) TextColumn(int width)
    {
        int pad = Px(PadX);
        int gap = Px(Gap);
        int left = pad + (glyph is not null ? IconBox + gap : Px(look == RowLook.Classic ? Indent : 0));
        int right = width - pad - (control is { Visible: true } ? ControlSize.Width - ControlInset + gap : 0);
        return (left, Math.Max(Px(80), right - left));
    }

    Size ControlSize => control is null ? Size.Empty : control.GetPreferredSize(Size.Empty);

    // A switch is wider than its track, to leave room for the focus ring, and the track itself
    // lines up with the lists; a row without padding keeps the whole switch inside it.
    int ControlInset => control is Toggle ? Math.Min(Px(4), Px(PadX)) : 0;
}

// The title or the explanation of a row. In the settings' looks its words are drawn without the
// room Windows adds at their ends, so they start at the row's edge, level with the headings, notes
// and chips beside them, which are drawn that way too. The welcome's look keeps a label's own.
sealed class RowLabel : Label
{
    // Measured and drawn the same way, so the words break where they were measured to.
    const TextFormatFlags Flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    public bool Tight { get; set; }

    public int HeightFor(int width) => Tight
        ? TextRenderer.MeasureText(Text, Font, new Size(Math.Max(1, width), int.MaxValue), Flags).Height
        : Shapes.WrappedHeight(Text, Font, width);

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!Tight)
        {
            base.OnPaint(e);
            return;
        }
        var flags = Flags | (RightToLeft == RightToLeft.Yes ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : 0);
        KitControl.TextDrawn?.Invoke(this, Text, Font, ClientRectangle, flags);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : SystemColors.GrayText, flags);
    }
}

// How a settings row is drawn.
enum RowLook
{
    // The welcome's rows: a card each, a gray icon, larger words.
    Classic,

    // The settings window's rows, look C of the sketch: a card each with 6 px corners and the darker
    // edge, the icon green on a soft 32 px circle, 11 px above and below and 16 at the sides.
    LookC,

    // A row inside a FlatPanel: no fill, edge or padding of its own, only the panel around it. An
    // icon, if it has one, is the green 20 px of the head of a page.
    Flat,
}
