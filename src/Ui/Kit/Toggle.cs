namespace SteppedOut;

// How a switch looks. Classic is the welcome window's: an outline when off. LookC is the settings
// window's: 40 by 20, a filled grey track with a light knob when off. Compact is look C at 34 by 18.
enum ToggleLook
{
    Classic,
    LookC,
    Compact,
}

// The on/off switch from Windows 11 settings. WinForms has none, so it is drawn by hand.
sealed class Toggle : KitControl
{
    // Room around the track for the focus ring, and the space between the word and the track.
    const int Room = 4;
    const int LabelGap = 8;
    // Room above and below the word, where it is taller than the track.
    const int WordRoom = 2;
    internal const float LabelPoints = 9.75f;
    const TextFormatFlags LabelFlags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;

    bool on;
    ToggleLook look;
    bool showLabel;

    public event EventHandler? CheckedChanged;

    public Toggle()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.CheckButton;
        Size = new Size(48, 28);
    }

    public bool Checked
    {
        get => on;
        set
        {
            if (on == value) return;
            on = value;
            Invalidate();
            Automation.Notify(this, AccessibleEvents.StateChange);
            Automation.ToggleChanged(Listened, value);
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ToggleLook Look
    {
        get => look;
        set
        {
            if (look == value) return;
            look = value;
            Resized();
        }
    }

    // "On" or "Off" before the switch, in a column as wide as the longer of the two, so the switch
    // stays put when it flips. Screen readers already hear the state, so the word is drawn rather
    // than a control of its own, and they never meet it.
    public bool ShowLabel
    {
        get => showLabel;
        set
        {
            if (showLabel == value) return;
            showLabel = value;
            Resized();
        }
    }

    void Resized()
    {
        Size = GetPreferredSize(Size.Empty);
        Invalidate();
    }

    Size TrackSize => look == ToggleLook.Compact ? new Size(34, 18) : new Size(40, 20);

    // The label's column, in pixels: as wide as the wider of the two words, and a line of them high.
    Size LabelColumn
    {
        get
        {
            if (!showLabel) return Size.Empty;
            var font = Body(LabelPoints);
            var text = Lang.Current.V1;
            Size on = Measure(text.On, font), off = Measure(text.Off, font);
            return new(Math.Max(on.Width, off.Width), Math.Max(on.Height, off.Height));
        }
    }

    // Measured without VerticalCenter, which leaves the height short of the line.
    static Size Measure(string word, Font font) => TextRenderer.MeasureText(word, font, Size.Empty, (LabelFlags & ~TextFormatFlags.VerticalCenter) | TextFormatFlags.NoPrefix);

    // At a big text size the word is taller than the track and its room around it; the switch is
    // then as tall as a line of the word and WordRoom above and below it, with the track in the
    // middle. The dots under an Arabic word («غير مفعّل») reach the very bottom of its line, so
    // without that room they would be cut.
    public override Size GetPreferredSize(Size proposedSize)
    {
        var track = TrackSize;
        var column = LabelColumn;
        int width = Px(track.Width + 2 * Room) + (column.Width > 0 ? column.Width + Px(LabelGap - Room) : 0);
        int words = column.Height > 0 ? column.Height + Px(2 * WordRoom) : 0;
        return new(width, Math.Max(Px(track.Height + 2 * Room), words));
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new ToggleAccessible(this);

    // A check box to UI Automation, as it is to MSAA.
    protected override Automation.Provider CreateProvider() => new(this, Automation.CheckBoxType)
    {
        Press = () =>
        {
            if (Enabled) Checked = !Checked;
        },
        IsOn = () => on,
    };

    // A switch that cannot be changed now, such as Start with Windows while Windows holds it off,
    // looks it: track and knob halfway to what is behind them, or the contrast theme's grey.
    Color Dimmed(Color color, Theme t)
    {
        if (Enabled) return color;
        if (t.IsHighContrast) return SystemColors.GrayText;
        return Mix(color, Under(t), 0.5f);
    }

    // A knob on a filled track: in a contrast theme the window's color on the grey track.
    Color DimmedKnob(Color color, Theme t) => Enabled || !t.IsHighContrast ? Dimmed(color, t) : t.Window;

    Color Under(Theme t)
    {
        for (Control? c = this; c is not null; c = c.Parent)
        {
            if (c is Card { Flat: false } card) return card.Fill ?? t.Card;
            if (c.BackColor.A == 255) return c.BackColor;
        }
        return t.Window;
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        // No hand over a switch that cannot be changed now.
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        Focus();
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space)
        {
            Checked = !Checked;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        Smooth(g);
        if (look == ToggleLook.Classic && !showLabel)
        {
            // Inset, so the focus ring around the track still lands inside the control.
            Classic(g, t, Shapes.Inside(new Rectangle(Px(Room), Px(Room), Width - Px(2 * Room), Height - Px(2 * Room))));
            return;
        }

        // The track at the far end, in the middle of the height, so its edge lines up with the other
        // controls at the end of a row; the word, if any, just before it.
        var size = TrackSize;
        int w = Px(size.Width), h = Px(size.Height);
        int left = Width - Px(Room) - w;
        if (showLabel)
        {
            var font = Body(LabelPoints);
            string word = on ? Lang.Current.V1.On : Lang.Current.V1.Off;
            int width = Measure(word, font).Width;
            int end = left - Px(LabelGap);
            DrawText(g, word, font, Box(end - width, 0, width, Height), Enabled ? t.Text : t.IsHighContrast ? SystemColors.GrayText : t.Subtle,
                LabelFlags | TextFormatFlags.NoClipping);
        }
        var track = Box(left, (Height - h) / 2, w, h);
        if (look == ToggleLook.Classic) Classic(g, t, Shapes.Inside(track));
        else Filled(g, t, track);
    }

    // Classic: filled with the accent when on; an outline with a small knob when off.
    void Classic(Graphics g, Theme t, RectangleF track)
    {
        float radius = track.Height / 2;
        using (var path = Shapes.Rounded(track, radius))
        {
            if (on)
            {
                using var fill = new SolidBrush(Dimmed(Hovered && Enabled ? ControlPaint.Light(t.Accent, 0.1f) : t.Accent, t));
                g.FillPath(fill, path);
            }
            else
            {
                using var pen = new Pen(Dimmed(t.Subtle, t), Px(1));
                g.DrawPath(pen, path);
            }
        }

        float knob = on ? track.Height * 0.6f : track.Height * 0.5f;
        if (Pressed) knob = track.Height * 0.7f;
        float center = on != Flip ? track.Right - radius : track.Left + radius;
        using (var brush = new SolidBrush(on ? DimmedKnob(t.OnAccent, t) : Dimmed(t.Subtle, t)))
            g.FillEllipse(brush, center - knob / 2, track.Top + (track.Height - knob) / 2, knob, knob);

        FocusRing(g, RectangleF.Inflate(track, Px(3), Px(3)), radius + Px(3));
    }

    // Look C: filled with the accent when on and with grey when off, a 12 px knob either way.
    void Filled(Graphics g, Theme t, Rectangle box)
    {
        RectangleF track = box;
        float radius = track.Height / 2;
        var fill = on ? t.Accent : t.Off;
        if (Hovered && Enabled && !t.IsHighContrast) fill = on ? ControlPaint.Light(fill, 0.1f) : Mix(fill, t.Text, 0.12f);
        using (var path = Shapes.Rounded(track, radius))
        using (var brush = new SolidBrush(Dimmed(fill, t)))
            g.FillPath(brush, path);

        float inset = Px(look == ToggleLook.Compact ? 3 : 4);
        float knob = Px(Pressed ? 14 : 12);
        float center = on != Flip ? track.Right - inset - Px(12) / 2f : track.Left + inset + Px(12) / 2f;
        float middle = track.Top + track.Height / 2;
        using (var brush = new SolidBrush(DimmedKnob(on ? t.Knob : t.KnobOff, t)))
            g.FillEllipse(brush, center - knob / 2, middle - knob / 2, knob, knob);

        FocusRing(g, RectangleF.Inflate(track, Px(3), Px(3)), radius + Px(3));
    }

    static Color Mix(Color from, Color to, float amount) => Color.FromArgb(
        (int)Math.Round(from.R + (to.R - from.R) * amount),
        (int)Math.Round(from.G + (to.G - from.G) * amount),
        (int)Math.Round(from.B + (to.B - from.B) * amount));

    sealed class ToggleAccessible(Toggle owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleStates State =>
            base.State | (owner.Checked ? AccessibleStates.Checked : AccessibleStates.None);

        public override string DefaultAction => owner.Checked ? Lang.Current.TurnOff : Lang.Current.TurnOn;

        // A screen reader's press, as the UI Automation one: a switch that cannot be changed now
        // stays as it is.
        public override void DoDefaultAction()
        {
            if (owner.Enabled) owner.Checked = !owner.Checked;
        }
    }
}
