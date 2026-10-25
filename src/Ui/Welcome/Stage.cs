using System.Drawing.Drawing2D;

namespace SteppedOut;

// The top of the welcome's Locking and While you're still using it pages: a drawn PC acting out the
// moment picked on the timeline (in use, the countdown note, locked, away, back), and beside it the
// Right now block: when the PC locks with the things going on that are ticked, what makes up that
// time, and the moment shown. On the second of those pages the four things that can be going on can
// be ticked here, and Play walks through the moments.
sealed class WelcomeStage : KitControl
{
    readonly Strings text;
    readonly Label rightNow;
    readonly TextBlock headline;
    readonly TextBlock detail;
    readonly TextBlock momentName;
    readonly TextBlock momentDetail;
    readonly ChoiceChips situations;
    readonly KitButton play;
    WelcomeChoices choices = new();
    Situation on;
    int page = 2;
    Moment moment;
    PcPart parts;
    PcPart pointed;
    int noteSeconds = WelcomeChoices.CountdownSeconds;
    // The seconds the note counts down from: the countdown's own length, or Try it's 5.
    int noteLength = WelcomeChoices.CountdownSeconds;
    Bitmap? logo;
    int logoSize;
    bool logoTurned;

    public WelcomeStage(Strings text)
    {
        this.text = text;
        var t = Theme.Current;
        var v = text.V1;
        BackColor = t.Card;
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        rightNow = new Label { Text = v.RightNow, AutoSize = false, UseMnemonic = false, BackColor = Color.Transparent, ForeColor = t.Subtle, Font = Theme.Strong(9f) };
        headline = new TextBlock("", Theme.Strong(13f), t.Text) { Heading = 2 };
        detail = new TextBlock("", Theme.Body(9.5f), t.Subtle);
        momentName = new TextBlock("", Theme.Strong(9.75f), t.Text);
        momentDetail = new TextBlock("", Theme.Body(9.5f), t.Subtle);
        situations = new ChoiceChips(multiple: true) { AccessibleName = v.Situations, Small = true }
            .Add(v.MusicOrVideoToggle, Situation.Music, Glyphs.Music)
            .Add(v.CallToggle, Situation.Call, Glyphs.Microphone)
            .Add(v.FullScreenToggle, Situation.FullScreen, Glyphs.Fullscreen)
            .Add(v.OnBatteryToggle, Situation.Battery, Glyphs.Battery);
        play = new KitButton(v.Play) { Small = true, Glyph = Glyphs.Play };
        Controls.AddRange([rightNow, play, headline, detail, situations, momentName, momentDetail]);
        AccessibleName = "";
    }

    public ChoiceChips Situations => situations;

    public KitButton Play => play;

    internal string Headline => headline.Text;

    internal string Detail => detail.Text;

    internal string MomentLine => momentName.Text + " " + momentDetail.Text;

    internal PcPart Parts => parts;

    internal PcPart Pointed => pointed;

    // How much of the line along the bottom of the countdown note is left: all of it at the start.
    internal float NoteLeft => noteLength > 0 ? Math.Clamp(noteSeconds / (float)noteLength, 0f, 1f) : 1f;

    // The time on the clocks in the picture: now, as the PC would show it. The render tool fixes it.
    internal static Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    public void Show(WelcomeChoices choices, Situation on, int page, Moment moment, bool previewing, int noteSeconds)
    {
        this.choices = choices;
        this.on = on;
        this.page = page;
        this.moment = moment;
        this.noteSeconds = noteSeconds;
        noteLength = previewing ? Toast.PreviewSeconds : noteSeconds;
        parts = choices.Scene(on, moment, page, previewing);
        pointed = previewing ? PcPart.Note : choices.Changed(on, moment, page);
        var time = choices.Preview(on);
        Set(headline, WelcomeText.Headline(text, time));
        Set(detail, WelcomeText.Detail(text, choices, on, time));
        var shown = previewing ? Moment.Warning : moment;
        Set(momentName, WelcomeText.Stop(text, shown, choices.Warns));
        Set(momentDetail, WelcomeText.MomentDetail(text, shown));
        situations.Visible = page == 3;
        foreach (var one in new[] { Situation.Music, Situation.Call, Situation.FullScreen, Situation.Battery })
            situations.SetChecked(one, on.HasFlag(one));
        PerformLayout();
        Invalidate();
    }

    static void Set(Label label, string words)
    {
        if (label.Text != words) label.Text = words;
    }

    // ---- Layout ----

    const int Side = 24;

    // The picture's size at 100%, and where the words beside it start.
    const int PictureWidth = 276;
    const int PictureHeight = 196;
    const int ColumnStart = Side + PictureWidth + 20;

    int ColumnWidth(int width) => Math.Max(Px(200), width - Px(ColumnStart) - Px(Side));

    // The words beside the picture, top to bottom, at a width: each control and its top.
    List<(Control Control, int Top, int Height)> Column(int width)
    {
        int columnWidth = ColumnWidth(width);
        var places = new List<(Control, int, int)>();
        int y = Px(18);
        var playSize = play.GetPreferredSize(Size.Empty);
        int labelHeight = Math.Max(playSize.Height, Shapes.Measure(rightNow.Text, rightNow.Font).Height);
        places.Add((rightNow, y, labelHeight));
        places.Add((play, y, playSize.Height));
        y += labelHeight + Px(4);
        foreach (var (control, gap) in new (Control, int)[] { (headline, 0), (detail, 2), (situations, 10), (momentName, 12), (momentDetail, 2) })
        {
            if (control == situations && page != 3) continue;
            y += Px(gap);
            // The line under the headline keeps its room when it is empty, so the stage keeps its
            // height as the choices change.
            int height = control == detail && detail.Text.Length == 0
                ? Shapes.Measure("Ag", detail.Font).Height
                : control.GetPreferredSize(new Size(columnWidth, 0)).Height;
            places.Add((control, y, height));
            y += height;
        }
        return places;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        var column = Column(width);
        int bottom = column.Count == 0 ? 0 : column.Max(c => c.Top + c.Height);
        return new Size(width, Math.Max(Px(PictureHeight + 8), bottom + Px(18)));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int columnWidth = ColumnWidth(Width);
        int left = Px(ColumnStart);
        detail.Visible = detail.Text.Length > 0;
        foreach (var (control, top, height) in Column(Width))
        {
            if (control == play)
            {
                var size = play.GetPreferredSize(Size.Empty);
                play.Bounds = Box(left + columnWidth - size.Width, top, size.Width, size.Height);
            }
            else if (control == rightNow)
            {
                int dot = Px(14);
                int room = columnWidth - dot - play.GetPreferredSize(Size.Empty).Width - Px(8);
                rightNow.Bounds = Box(left + dot, top, Math.Max(1, room), height);
            }
            else
            {
                control.Bounds = Box(left, top, columnWidth, height);
            }
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        PerformLayout();
    }

    // ---- Drawing ----

    float S => DeviceDpi / 96f;

    // Where the picture starts: its top left at 100% within the stage.
    float OriginX => Side;

    float OriginY => (Height / S - PictureHeight) / 2f + 2;

    // A box in the picture, at 100% from its top left, as pixels in the stage. Mirrored where the
    // window does not mirror it for Arabic.
    RectangleF P(float x, float y, float w, float h)
    {
        var box = new RectangleF((OriginX + x) * S, (OriginY + y) * S, w * S, h * S);
        if (Flip) box.X = Width - box.Right;
        return box;
    }

    PointF Pt(float x, float y)
    {
        float px = (OriginX + x) * S;
        return new PointF(Flip ? Width - px : px, (OriginY + y) * S);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var t = Theme.Current;
        var g = e.Graphics;
        Smooth(g);

        // The dot before "Right now".
        var label = rightNow.Bounds;
        float d = 8 * S;
        float dx = Flip ? label.Right + 5 * S : label.Left - 13 * S;
        using (var halo = new SolidBrush(t.Soft)) g.FillEllipse(halo, dx - 3 * S, label.Top + label.Height / 2f - d / 2 - 3 * S, d + 6 * S, d + 6 * S);
        using (var dot = new SolidBrush(t.Accent)) g.FillEllipse(dot, dx, label.Top + label.Height / 2f - d / 2, d, d);

        DrawDesk(g, t);
        DrawScreen(g, t);
    }

    bool Laptop => parts.HasFlag(PcPart.Laptop);

    // The desk, the stand or the laptop's base, the speaker, the keyboard and the mouse.
    void DrawDesk(Graphics g, Theme t)
    {
        var bezel = Bezel(t);
        using (var desk = new Pen(t.Line, Math.Max(1, 2 * S)))
            g.DrawLine(desk, Pt(-8, 178), Pt(PictureWidth + 4, 178));
        if (Laptop)
        {
            using var path = new GraphicsPath();
            path.AddPolygon([Pt(16, 160), Pt(256, 160), Pt(270, 172), Pt(2, 172)]);
            using var brush = new SolidBrush(Blend(bezel, t.Card, 0.25f));
            g.FillPath(brush, path);
            // "On battery", on the desk beside it.
            var words = text.V1.OnBatteryToggle;
            var font = Body(7.5f);
            int width = TextRenderer.MeasureText(words, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            var chip = P(150, 180, width / S + 26, 15);
            using (var pill = Shapes.Rounded(chip, chip.Height / 2))
            {
                using var fill = new SolidBrush(t.Card);
                g.FillPath(fill, pill);
                using var pen = new Pen(t.Edge);
                g.DrawPath(pen, pill);
            }
            var glyphBox = Rectangle.Round(new RectangleF(Flip ? chip.Right - 18 * S : chip.X + 5 * S, chip.Y, 13 * S, chip.Height));
            DrawText(g, Glyphs.Battery, Icons(7.5f), glyphBox, t.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            var wordsBox = Rectangle.Round(new RectangleF(Flip ? chip.X + 5 * S : chip.X + 20 * S, chip.Y, width + 2, chip.Height));
            DrawText(g, words, font, wordsBox, t.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            return;
        }
        using (var stand = new SolidBrush(bezel))
        {
            g.FillRectangle(stand, P(128, 156, 16, 12));
            using var foot = Shapes.Rounded(P(96, 166, 80, 7), 3 * S);
            g.FillPath(stand, foot);
        }
        // The speaker, with sound coming out while something plays, or muted.
        using (var box = Shapes.Rounded(P(4, 136, 24, 42), 4 * S))
        using (var brush = new SolidBrush(bezel))
            g.FillPath(brush, box);
        using (var cone = new SolidBrush(Blend(bezel, Color.White, 0.25f)))
        {
            g.FillEllipse(cone, P(12, 142, 8, 8));
            g.FillEllipse(cone, P(8, 155, 16, 16));
        }
        bool muted = parts.HasFlag(PcPart.Muted);
        if (muted)
        {
            var badge = P(5, 108, 22, 22);
            using (var fill = new SolidBrush(t.Card)) g.FillEllipse(fill, badge);
            using (var pen = new Pen(pointed.HasFlag(PcPart.Muted) ? t.Accent : t.ControlEdge, pointed.HasFlag(PcPart.Muted) ? 2 * S : 1)) g.DrawEllipse(pen, badge);
            DrawText(g, Glyphs.Mute, Icons(7.5f), Rectangle.Round(badge), t.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
        else if (parts.HasFlag(PcPart.Playing))
        {
            using var pen = new Pen(t.Accent, 1.6f * S) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            for (int i = 0; i < 3; i++)
            {
                float r = 5 + 5 * i;
                var arc = P(16 - r, 132 - r, 2 * r, 2 * r);
                g.DrawArc(pen, arc, 225, 90);
            }
        }
        // The keyboard and the mouse, or on the Locking page the shortcut's keys.
        using (var key = Shapes.Rounded(P(84, 182, 126, 9), 2 * S))
        {
            using var fill = new SolidBrush(t.Card);
            g.FillPath(fill, key);
            using var pen = new Pen(t.Edge);
            g.DrawPath(pen, key);
        }
        using (var mouse = Shapes.Rounded(P(220, 180, 11, 14), 5 * S))
        {
            using var fill = new SolidBrush(t.Card);
            g.FillPath(fill, mouse);
            using var pen = new Pen(t.Edge);
            g.DrawPath(pen, mouse);
        }
        if (parts.HasFlag(PcPart.Keys)) DrawKeys(g, t);
    }

    // The shortcut's keys over the keyboard: Ctrl + Alt + L, left to right in every language.
    void DrawKeys(Graphics g, Theme t)
    {
        var keys = choices.Shortcut.Split('+');
        var font = Strong(6.5f);
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        var sizes = keys.Select(k => Math.Max(18 * S, TextRenderer.MeasureText(k, font, Size.Empty, flags).Width + 9 * S)).ToList();
        float plus = 9 * S;
        float total = sizes.Sum() + plus * (keys.Length - 1);
        float center = (OriginX + 147) * S;
        if (Flip) center = Width - center;
        float x = center - total / 2;
        float top = (OriginY + 178) * S;
        for (int i = 0; i < keys.Length; i++)
        {
            var cap = new RectangleF(x, top, sizes[i], 16 * S);
            using (var path = Shapes.Rounded(cap, 3 * S))
            {
                using var fill = new SolidBrush(t.Card);
                g.FillPath(fill, path);
                using var pen = new Pen(t.Accent, 1.2f * S);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, keys[i], font, Rectangle.Round(cap), t.Text, flags | TextFormatFlags.NoPrefix);
            x += sizes[i];
            if (i < keys.Length - 1)
            {
                TextRenderer.DrawText(g, "+", font, Rectangle.Round(new RectangleF(x, top, plus, 16 * S)), t.Subtle, flags);
                x += plus;
            }
        }
    }

    Color Bezel(Theme t) => t.IsDark ? Color.FromArgb(0x0B, 0x0D, 0x0C) : Color.FromArgb(0x26, 0x2D, 0x2B);

    static Color Blend(Color a, Color b, float toB) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * toB), (int)(a.G + (b.G - a.G) * toB), (int)(a.B + (b.B - a.B) * toB));

    // The monitor and what is on it.
    void DrawScreen(Graphics g, Theme t)
    {
        var bezel = Bezel(t);
        using (var frame = Shapes.Rounded(P(20, 8, 232, 150), 7 * S))
        using (var brush = new SolidBrush(bezel))
            g.FillPath(brush, frame);
        var screen = P(26, 14, 220, 138);
        var state = g.Save();
        using var clip = Shapes.Rounded(screen, 3 * S);
        g.SetClip(clip);

        // The desktop: a quiet wash, a window, the taskbar.
        using (var wash = new LinearGradientBrush(screen, t.IsDark ? Color.FromArgb(0x1C, 0x2A, 0x26) : Color.FromArgb(0xEC, 0xF3, 0xF0),
            t.IsDark ? Color.FromArgb(0x24, 0x3B, 0x34) : Color.FromArgb(0xD3, 0xE5, 0xDE), LinearGradientMode.ForwardDiagonal))
            g.FillRectangle(wash, screen);

        if (parts.HasFlag(PcPart.App)) DrawApp(g, t);
        if (parts.HasFlag(PcPart.Call)) DrawCall(g, t);
        if (parts.HasFlag(PcPart.Media)) DrawMedia(g, t);
        if (parts.HasFlag(PcPart.App) || parts.HasFlag(PcPart.Call)) DrawTaskbar(g, t);
        if (parts.HasFlag(PcPart.FullScreen)) DrawFullScreen(g, t);
        if (parts.HasFlag(PcPart.Lock)) DrawLock(g, t);
        if (parts.HasFlag(PcPart.Off))
        {
            using var off = new SolidBrush(Color.FromArgb(0x05, 0x06, 0x06));
            g.FillRectangle(off, screen);
        }
        if (parts.HasFlag(PcPart.Note)) DrawNote(g, t);
        if (parts.HasFlag(PcPart.Toast)) DrawToast(g, t);
        if (parts.HasFlag(PcPart.Cursor)) DrawCursor(g);
        g.Restore(state);

        // The power light, green while the screen is on.
        using (var light = new SolidBrush(parts.HasFlag(PcPart.Off) ? Color.FromArgb(0x5B, 0x66, 0x62) : t.Accent))
            g.FillRectangle(light, P(134, 154, 4, 1.5f));

        // What came new at this moment, ringed.
        if (pointed.HasFlag(PcPart.Lock) || pointed.HasFlag(PcPart.Off)) Ring(g, t, P(22, 10, 228, 146), 6);
    }

    void Ring(Graphics g, Theme t, RectangleF around, float radius)
    {
        var box = RectangleF.Inflate(around, 2 * S, 2 * S);
        using var path = Shapes.Rounded(box, radius * S);
        using var pen = new Pen(t.Accent, 2.5f * S);
        g.DrawPath(pen, path);
    }

    void DrawApp(Graphics g, Theme t)
    {
        var window = P(38, 24, 124, 84);
        using (var shadow = new SolidBrush(Color.FromArgb(28, 0, 0, 0))) g.FillRectangle(shadow, RectangleF.Inflate(window, S, S));
        using (var fill = new SolidBrush(t.Card)) g.FillRectangle(fill, window);
        using (var bar = new SolidBrush(t.Line)) g.FillRectangle(bar, P(38, 24, 124, 9));
        using var lines = new SolidBrush(t.IsDark ? Color.FromArgb(0x4A, 0x4A, 0x4A) : Color.FromArgb(0xD3, 0xDA, 0xD7));
        using var accent = new SolidBrush(Color.FromArgb(110, t.Accent));
        g.FillRectangle(accent, P(48, 42, 52, 5));
        float[] widths = { 96, 70, 96, 60, 84 };
        for (int i = 0; i < widths.Length; i++) g.FillRectangle(lines, P(48, 54 + i * 10, widths[i], 3.5f));
    }

    void DrawCall(Graphics g, Theme t)
    {
        var window = P(60, 22, 150, 92);
        using (var fill = new SolidBrush(Color.FromArgb(0x1E, 0x25, 0x22))) g.FillRectangle(fill, window);
        using var tile = new SolidBrush(Color.FromArgb(0x2E, 0x37, 0x33));
        var person = Color.FromArgb(0x8F, 0xD9, 0xC1);
        for (int i = 0; i < 2; i++)
        {
            var box = P(65 + i * 72, 27, 68, 58);
            g.FillRectangle(tile, box);
            DrawText(g, "", Icons(14f), Rectangle.Round(box), person, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
        using var button = new SolidBrush(Color.FromArgb(0x3B, 0x45, 0x41));
        using (var mic = Shapes.Rounded(P(118, 92, 18, 14), 7 * S)) g.FillPath(button, mic);
        DrawText(g, Glyphs.Microphone, Icons(6.5f), Rectangle.Round(P(118, 92, 18, 14)), person, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        using var end = new SolidBrush(Color.FromArgb(0xD6, 0xDE, 0xDA));
        using (var hang = Shapes.Rounded(P(140, 92, 24, 14), 7 * S)) g.FillPath(end, hang);
    }

    void DrawMedia(Graphics g, Theme t)
    {
        bool playing = parts.HasFlag(PcPart.Playing);
        bool locked = parts.HasFlag(PcPart.Lock) || parts.HasFlag(PcPart.Off);
        if (locked)
        {
            if (parts.HasFlag(PcPart.Off)) return;
            // On the lock screen: the media box at the bottom left, playing or paused.
            var box = P(32, 128, 44, 16);
            using (var path = Shapes.Rounded(box, 3 * S))
            using (var fill = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                g.FillPath(fill, path);
            DrawText(g, playing ? Glyphs.Pause : Glyphs.Play, Icons(7f), Rectangle.Round(box), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (pointed.HasFlag(PcPart.Media)) Ring(g, t, box, 4);
            return;
        }
        var widget = P(158, 20, 82, 28);
        using (var shadow = new SolidBrush(Color.FromArgb(30, 0, 0, 0))) g.FillRectangle(shadow, RectangleF.Inflate(widget, S, S));
        using (var path = Shapes.Rounded(widget, 4 * S))
        using (var fill = new SolidBrush(t.IsDark ? Color.FromArgb(0x2B, 0x2B, 0x2B) : Color.FromArgb(0xF7, 0xF7, 0xF7)))
            g.FillPath(fill, path);
        var art = P(163, 25, 18, 18);
        using (var fill = new SolidBrush(t.Accent)) g.FillRectangle(fill, art);
        DrawText(g, Glyphs.Music, Icons(7f), Rectangle.Round(art), t.OnAccent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        using (var bar = new SolidBrush(t.IsDark ? Color.FromArgb(0x4A, 0x4A, 0x4A) : Color.FromArgb(0xD3, 0xDA, 0xD7)))
        {
            g.FillRectangle(bar, P(186, 28, 30, 3));
            g.FillRectangle(bar, P(186, 35, 22, 3));
        }
        var button = P(220, 26, 15, 15);
        using (var fill = new SolidBrush(t.Accent)) g.FillEllipse(fill, button);
        DrawText(g, playing ? Glyphs.Pause : Glyphs.Play, Icons(6f), Rectangle.Round(button), t.OnAccent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        if (pointed.HasFlag(PcPart.Media)) Ring(g, t, widget, 5);
    }

    void DrawTaskbar(Graphics g, Theme t)
    {
        var bar = P(26, 138, 220, 14);
        using (var fill = new SolidBrush(t.IsDark ? Color.FromArgb(0x20, 0x20, 0x20) : Color.FromArgb(0xF3, 0xF3, 0xF3))) g.FillRectangle(fill, bar);
        using (var start = new SolidBrush(t.Accent)) g.FillRectangle(start, P(118, 141, 8, 8));
        using (var icons = new SolidBrush(t.IsDark ? Color.FromArgb(0x4A, 0x4A, 0x4A) : Color.FromArgb(0xC8, 0xCF, 0xCC)))
        {
            for (int i = 0; i < 3; i++) g.FillRectangle(icons, P(130 + i * 11, 141, 8, 8));
        }
        var ink = t.Text;
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        DrawText(g, parts.HasFlag(PcPart.Muted) ? Glyphs.Mute : Glyphs.Volume, Icons(5.5f), Rectangle.Round(P(196, 138, 12, 14)), ink, flags);
        if (Laptop) DrawText(g, Glyphs.Battery, Icons(5.5f), Rectangle.Round(P(184, 138, 12, 14)), ink, flags);
        DrawText(g, text.ClockTime(TimeAt(moment)), Body(5.5f), Rectangle.Round(P(208, 138, 36, 14)), ink, flags);
    }

    void DrawFullScreen(Graphics g, Theme t)
    {
        var screen = P(26, 14, 220, 138);
        using (var fill = new SolidBrush(t.Card)) g.FillRectangle(fill, screen);
        using (var head = new SolidBrush(Color.FromArgb(190, t.Text))) g.FillRectangle(head, P(40, 28, 120, 7));
        using var grey = new SolidBrush(t.IsDark ? Color.FromArgb(0x4A, 0x4A, 0x4A) : Color.FromArgb(0xD3, 0xDA, 0xD7));
        g.FillRectangle(grey, P(40, 41, 80, 4));
        float[] heights = { 38, 60, 48, 82, 70 };
        for (int i = 0; i < heights.Length; i++)
        {
            using var brush = i == 3 ? new SolidBrush(t.Accent) : new SolidBrush(grey.Color);
            g.FillRectangle(brush, P(46 + i * 38, 138 - heights[i], 26, heights[i]));
        }
    }

    // The lock screen: a padlock, the time, and the sound and media boxes at the bottom.
    void DrawLock(Graphics g, Theme t)
    {
        var screen = P(26, 14, 220, 138);
        using (var wash = new LinearGradientBrush(screen, Color.FromArgb(0x16, 0x2B, 0x26), Color.FromArgb(0x2C, 0x55, 0x4A), LinearGradientMode.ForwardDiagonal))
            g.FillRectangle(wash, screen);
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        DrawText(g, Glyphs.Lock, Icons(8f), Rectangle.Round(P(26, 26, 220, 16)), Color.White, flags);
        DrawText(g, text.ClockTime(TimeAt(moment)), Strong(17f), Rectangle.Round(P(26, 44, 220, 36)), Color.White, flags);
        DrawText(g, parts.HasFlag(PcPart.Muted) ? Glyphs.Mute : Glyphs.Volume, Icons(7f), Rectangle.Round(P(220, 128, 18, 16)), Color.White, flags);
        if (pointed.HasFlag(PcPart.Muted)) Ring(g, t, P(220, 128, 18, 16), 4);
    }

    // The countdown note in the corner, as the real one looks: the logo and the seconds left.
    void DrawNote(Graphics g, Theme t)
    {
        string words = text.ShortLeft(noteSeconds);
        var font = Strong(6.5f);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        float width = TextRenderer.MeasureText(words, font, Size.Empty, flags).Width / S + 30;
        var pill = P(240 - width, 116, width, 18);
        DrawCard(g, t, pill, pill.Height / 2);
        DrawLogo(g, P(244 - width, 119, 12, 12));
        var wordsBox = P(260 - width, 116, width - 22, 18);
        DrawText(g, words, font, Rectangle.Round(wordsBox), t.Accent, flags);
        using (var track = new SolidBrush(t.Accent)) g.FillRectangle(track, P(246 - width, 132.5f, (width - 12) * NoteLeft, 1.5f));
        if (pointed.HasFlag(PcPart.Note)) Ring(g, t, pill, 10);
    }

    // The note on the way back, as the real one says it.
    void DrawToast(Graphics g, Theme t)
    {
        string words = text.YouWereAwayFor(TimeSpan.FromMinutes(BackAfter));
        var font = Body(6f);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        float width = Math.Min(200, TextRenderer.MeasureText(words, font, Size.Empty, flags).Width / S + 28);
        var card = P(240 - width, 112, width, 22);
        DrawCard(g, t, card, 4 * S);
        DrawLogo(g, P(245 - width, 117, 12, 12));
        DrawText(g, words, font, Rectangle.Round(P(261 - width, 112, width - 24, 22)), t.Text, flags);
        if (pointed.HasFlag(PcPart.Toast)) Ring(g, t, card, 6);
    }

    void DrawCard(Graphics g, Theme t, RectangleF box, float radius)
    {
        using (var shadow = new SolidBrush(Color.FromArgb(36, 0, 0, 0))) g.FillRectangle(shadow, new RectangleF(box.X + S, box.Y + 1.5f * S, box.Width, box.Height));
        using var path = Shapes.Rounded(box, radius);
        using var fill = new SolidBrush(t.Card);
        g.FillPath(fill, path);
        using var pen = new Pen(t.Edge);
        g.DrawPath(pen, path);
    }

    // The logo, never mirrored: a window laid out right to left mirrors what is drawn in it, so the
    // picture goes in turned round already.
    void DrawLogo(Graphics g, RectangleF box)
    {
        int size = Math.Max(8, (int)Math.Round(box.Width));
        if (logo is null || logoSize != size || logoTurned != IsMirrored)
        {
            logo?.Dispose();
            logoSize = size;
            logoTurned = IsMirrored;
            logo = Logo.Draw(size);
            if (logoTurned) logo.RotateFlip(RotateFlipType.RotateNoneFlipX);
        }
        g.DrawImage(logo, Rectangle.Round(box));
    }

    void DrawCursor(Graphics g)
    {
        float x = moment == Moment.Back ? 124 : 112, y = moment == Moment.Back ? 78 : 70;
        var points = new[] { (0f, 0f), (0f, 12f), (3f, 9.5f), (5f, 13.5f), (7f, 12.6f), (5f, 8.8f), (9f, 8.8f) }
            .Select(p => Pt(x + p.Item1, y + p.Item2)).ToArray();
        using var fill = new SolidBrush(Color.White);
        g.FillPolygon(fill, points);
        using var pen = new Pen(Color.FromArgb(0x11, 0x11, 0x11), Math.Max(1, 0.9f * S)) { LineJoin = LineJoin.Round };
        g.DrawPolygon(pen, points);
    }

    // The minutes from leaving to being back that the pictures use.
    const int AwayAfter = 5;
    const int BackAfter = 12;

    // The time on the clocks at a moment: now while in use, the lock time when it locks, a little
    // later while away, and later still once back.
    DateTime TimeAt(Moment at)
    {
        var start = Clock();
        var time = choices.Preview(on);
        double lockMinutes = (double)(time.Total ?? time.Normal);
        return at switch
        {
            Moment.Locked => start.AddMinutes(lockMinutes),
            Moment.Away => start.AddMinutes(lockMinutes + AwayAfter),
            Moment.Back => start.AddMinutes(lockMinutes + BackAfter),
            _ => start,
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) logo?.Dispose();
        base.Dispose(disposing);
    }
}
