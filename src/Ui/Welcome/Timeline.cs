using System.Drawing.Drawing2D;

namespace SteppedOut;

// Where the parts of the welcome's timeline go along a line this wide, in pixels at 100%, from its
// start: the stretch before the lock (the lock time, then each extra that applies), the warning,
// Locks, You're away and You're back. The stretch grows with the time on a square-root scale, so 15
// minutes and 2 hours both read, and stops at a cap that leaves the stops after it room; past the
// cap the stretch shows a break. A time that never ends runs off the edge instead.
sealed class TimelineLayout
{
    // From the start of the line to the first stop's middle, and from the last stop's to the end.
    public const float Edge = 24;

    // From the warning to Locks.
    public const float WarnGap = 76;

    // From Locks to You're away, and from there to You're back, at the least.
    public const float MinAfter = 112;

    // Pixels for the square root of a minute: 15 minutes come to about 132.
    public const float PerRoot = 34;

    // The shortest stretch, which still has room for "5 min" inside it, and the narrowest extra.
    public const float ShortestStretch = 76;
    public const float ShortestExtra = 44;

    // One part of the stretch: the lock time (Situation.None) or an extra.
    public readonly record struct Segment(Situation Source, float Start, float Width, decimal Minutes);

    TimelineLayout(float width, LockTime time)
    {
        Width = width;
        Time = time;
    }

    public float Width { get; }

    public LockTime Time { get; }

    public IReadOnlyList<Segment> Segments { get; private set; } = [];

    // Where the stretch ends: the warning stop, or where never starts.
    public float StretchEnd { get; private set; }

    public bool Never => Time.NeverLocks;

    // The stops after the stretch; NaN while it never locks.
    public float Locked { get; private set; } = float.NaN;
    public float Away { get; private set; } = float.NaN;
    public float Back { get; private set; } = float.NaN;

    // The longest stretch there is room for.
    public float Cap { get; private set; }

    // The time is longer than the cap shows: a break is drawn here in the stretch.
    public float? BreakAt { get; private set; }

    // Where Locks would be without the battery's cut, when the cut moved it.
    public float? Ghost { get; private set; }

    public float Start => Edge;

    public float X(Moment moment) => moment switch
    {
        Moment.Idle => Edge,
        Moment.Warning => StretchEnd,
        Moment.Locked => Locked,
        Moment.Away => Away,
        _ => Back,
    };

    // The stretch for a time, on the square-root scale, between the shortest and the cap.
    public static float Length(decimal minutes, float cap) =>
        Math.Clamp(PerRoot * (float)Math.Sqrt((double)Math.Max(0, minutes)), ShortestStretch, Math.Max(ShortestStretch, cap));

    public static TimelineLayout Compute(WelcomeChoices choices, Situation on, float width)
    {
        var time = choices.Preview(on);
        var line = new TimelineLayout(width, time);
        float back = width - Edge;
        line.Cap = Math.Max(ShortestStretch + 3 * ShortestExtra, back - 2 * MinAfter - WarnGap - Edge);
        if (time.NeverLocks)
        {
            float length = Length(time.Normal, line.Cap);
            line.Segments = [new Segment(Situation.None, Edge, length, time.Normal)];
            line.StretchEnd = Edge + length;
            return line;
        }
        var parts = new List<(Situation Source, decimal Minutes)> { (Situation.None, time.Normal) };
        foreach (var extra in time.Extras)
        {
            if (extra.Effect.Amount <= 0) continue;
            parts.Add((SituationOf(extra.Source), extra.Effect.Amount));
        }
        decimal total = time.Total ?? time.Sum;
        float stretch = Math.Max(Length(total, line.Cap), ShortestStretch + (parts.Count - 1) * ShortestExtra);
        stretch = Math.Min(stretch, Math.Max(line.Cap, ShortestStretch + (parts.Count - 1) * ShortestExtra));
        var widths = Split(stretch, parts.Select(p => (float)p.Minutes).ToList(), parts.Select((_, i) => i == 0 ? ShortestStretch : ShortestExtra).ToList());
        var segments = new List<Segment>();
        float x = Edge;
        for (int i = 0; i < parts.Count; i++)
        {
            segments.Add(new Segment(parts[i].Source, x, widths[i], parts[i].Minutes));
            x += widths[i];
        }
        line.Segments = segments;
        line.StretchEnd = x;
        line.Locked = x + WarnGap;
        line.Back = back;
        line.Away = (line.Locked + back) / 2;
        if (PerRoot * (float)Math.Sqrt((double)total) > line.Cap)
        {
            var last = segments[^1];
            line.BreakAt = last.Start + last.Width * 0.62f;
        }
        if (time.CutApplied)
        {
            float whole = Math.Min(Length(time.Sum, line.Cap), Math.Max(line.Cap, stretch));
            float ghost = Edge + whole + WarnGap;
            if (ghost - line.Locked > 3) line.Ghost = ghost;
        }
        return line;
    }

    static Situation SituationOf(EffectSource source) => source.Kind switch
    {
        SourceKind.MusicOrVideo => Situation.Music,
        SourceKind.Call => Situation.Call,
        SourceKind.FullScreen => Situation.FullScreen,
        _ => Situation.Battery,
    };

    // The span shared out by value, each part at least its shortest; what the short ones take is
    // shared by the others.
    internal static List<float> Split(float span, List<float> values, List<float> shortest)
    {
        var fixedParts = new bool[values.Count];
        for (int round = 0; round < values.Count; round++)
        {
            float freeValue = 0, taken = 0;
            for (int i = 0; i < values.Count; i++)
            {
                if (fixedParts[i]) taken += shortest[i];
                else freeValue += values[i];
            }
            float left = Math.Max(0, span - taken);
            bool changed = false;
            for (int i = 0; i < values.Count; i++)
            {
                if (fixedParts[i]) continue;
                float share = freeValue > 0 ? left * values[i] / freeValue : 0;
                if (share < shortest[i])
                {
                    fixedParts[i] = true;
                    changed = true;
                }
            }
            if (!changed) break;
        }
        float fixedSum = 0, free = 0;
        for (int i = 0; i < values.Count; i++)
        {
            if (fixedParts[i]) fixedSum += shortest[i];
            else free += values[i];
        }
        float rest = Math.Max(0, span - fixedSum);
        return values.Select((v, i) => fixedParts[i] ? shortest[i] : free > 0 ? rest * v / free : 0).ToList();
    }
}

// The welcome's timeline, under the drawn PC: five stops with their names, You stop using the PC,
// the warning, Locks, You're away and You're back, along a line where the stretch before the lock
// shows the lock time and grows with the extra time that applies. Clicking a stop, or its name,
// shows that moment on the PC. The stops are one stop for Tab, and the arrow keys move between
// them, showing each moment as they go.
sealed class WelcomeTimeline : PartGroup
{
    readonly Strings text;
    readonly Stop[] stops;
    WelcomeChoices choices = new();
    Situation on;
    int page = 2;
    Moment moment = Moment.Idle;
    TimelineLayout line = TimelineLayout.Compute(new WelcomeChoices(), Situation.None, 752);
    float? playhead;
    readonly List<(Moment Moment, Rectangle Box)> names = new();

    // The lock time written in its bar. It stands alone there, so Arabic says ساعتان, not ساعتين.
    internal static string BarWords(Strings text, decimal normal) => text.ShortDuration(normal, standalone: true);

    // A stop, or its name, chosen by a click, a key or a screen reader.
    public event Action<Moment>? MomentChosen;

    public WelcomeTimeline(Strings text)
    {
        this.text = text;
        AccessibleName = text.V1.Timeline;
        AccessibleRole = AccessibleRole.Grouping;
        stops = Enum.GetValues<Moment>().Select(m => new Stop(m)).ToArray();
        foreach (var stop in stops) AddPart(stop);
        Cursor = Cursors.Default;
    }

    protected override bool ArrowsChoose => true;

    internal TimelineLayout Line => line;

    internal Moment Moment => moment;

    internal IReadOnlyList<Part> Stops => stops;

    // The names drawn under the stops, where they are.
    internal IReadOnlyList<(Moment Moment, Rectangle Box)> Names => names;

    // The choices, the things going on, the page and the moment shown, and where Play's dot is.
    public void Show(WelcomeChoices choices, Situation on, int page, Moment moment, float? playhead = null)
    {
        this.choices = choices;
        this.on = on;
        this.page = page;
        this.moment = moment;
        this.playhead = playhead;
        line = TimelineLayout.Compute(choices, on, LogicalWidth);
        foreach (var stop in stops)
        {
            bool shown = stop.Moment == Moment.Idle || !line.Never;
            stop.Visible = shown;
            stop.Enabled = shown && (stop.Moment != Moment.Warning || choices.Warns);
            string name = WelcomeText.Stop(text, stop.Moment, choices.Warns);
            if (stop.AccessibleName != name) stop.AccessibleName = name;
        }
        int index = Array.FindIndex(stops, s => s.Moment == moment);
        int was = Current;
        SetCurrent(index);
        if (was != index)
        {
            if (was >= 0) stops[was].ChosenChanged();
            stops[index].ChosenChanged();
        }
        PerformLayout();
        Invalidate(true);
    }

    // Play's dot, moved along the line with nothing else changing; null for none.
    internal void MovePlayhead(float? x)
    {
        if (playhead == x) return;
        playhead = x;
        Invalidate();
    }

    internal float? Playhead => playhead;

    // The timeline's own width at 100%, which the line is laid out along.
    float LogicalWidth => Math.Max(300, Width * 96f / DeviceDpi - 2 * Side);

    const float Side = 24;

    float Ratio => DeviceDpi / 96f;

    float Pos(float logical) => (Side + logical) * Ratio;

    Font NameFont => Body(9f);

    Font NameChosen => Strong(9f);

    Font NoteFont => Strong(9f);

    int NoteRow => Math.Max(Px(22), TextRenderer.MeasureText("Ag", NoteFont).Height + Px(6));

    int NameRow => TextRenderer.MeasureText("Ag", NameChosen).Height + Px(2);

    int TrackY => Px(8) + NoteRow + Px(22);

    int NamesTop => TrackY + Px(24);

    public override Size GetPreferredSize(Size proposedSize) =>
        new(proposedSize.Width > 0 ? proposedSize.Width : Width, NamesTop + 2 * NameRow + Px(12));

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var fresh = TimelineLayout.Compute(choices, on, LogicalWidth);
        line = fresh;
        foreach (var stop in stops)
        {
            if (!stop.Visible && stop.Moment != Moment.Idle && line.Never) continue;
            float x = line.X(stop.Moment);
            if (float.IsNaN(x)) continue;
            int size = Px(stop.Moment == Moment.Locked ? 42 : 34);
            stop.Bounds = Box((int)Math.Round(Pos(x)) - size / 2, TrackY - size / 2, size, size);
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        PerformLayout();
    }

    internal override void Choose(Part part)
    {
        var chosen = ((Stop)part).Moment;
        if (!part.Enabled) return;
        moment = chosen;
        MomentChosen?.Invoke(chosen);
    }

    internal override bool Key(Part from, Keys key) => key != Keys.Enter && base.Key(from, key);

    // Not a stop that is not there while it never locks, or the warning when there is none.
    protected override bool Offers(Part part) => part.Enabled;

    // A name under a stop, or the stretch, shows that moment too.
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        foreach (var (m, box) in names)
        {
            if (!box.Contains(e.Location)) continue;
            var stop = stops[(int)m];
            if (stop.Visible && stop.Enabled) Choose(stop);
            return;
        }
        int x = Flip ? Width - e.X : e.X;
        if (e.Y > TrackY - Px(16) && e.Y < TrackY + Px(16) && x >= Pos(line.Start) && x <= Pos(line.StretchEnd)) Choose(stops[0]);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        Smooth(g);
        float s = Ratio;
        float y = TrackY;
        float barHalf = 12 * s;

        // The line through the stops, grey, to the last stop or to where never starts.
        float end = line.Never ? line.StretchEnd : line.Back;
        using (var pen = new Pen(t.FieldLine, Math.Max(2, 3 * s)))
            g.DrawLine(pen, X(line.Start), y, X(end), y);

        // After the stretch: the warning, dotted in the accent while there is one, then dashed.
        if (!line.Never)
        {
            using (var pen = new Pen(choices.Warns ? t.Accent : t.FieldLine, Math.Max(2, 3 * s)) { DashStyle = DashStyle.Dot })
                g.DrawLine(pen, X(line.StretchEnd), y, X(line.Locked), y);
            using (var pen = new Pen(t.Card, Math.Max(2, 3 * s)) { DashStyle = DashStyle.Dash })
                g.DrawLine(pen, X(line.Locked), y, X(line.Back), y);
        }

        // The stretch: the lock time in the accent, each extra striped.
        foreach (var segment in line.Segments)
        {
            var box = Bar(segment.Start, segment.Width, y, barHalf);
            if (segment.Source == Situation.None)
            {
                using var path = Shapes.Corners(box, Flip ? 0 : barHalf, Flip ? barHalf : 0, Flip ? barHalf : 0, Flip ? 0 : barHalf);
                using var brush = new SolidBrush(t.Accent);
                g.FillPath(brush, path);
            }
            else
            {
                Stripes(g, box, t, fade: false);
            }
        }
        if (line.Never)
        {
            // Never: stripes from the end of the lock time off the edge of the window, fading.
            float from = line.StretchEnd;
            float to = Width / s;
            var box = Bar(from, to - from - Side + 8, y, barHalf);
            Stripes(g, box, t, fade: true);
        }
        if (line.BreakAt is float at)
        {
            float bx = X(at);
            using var pen = new Pen(t.Card, 5 * s);
            g.DrawLine(pen, bx - 3 * s, y + barHalf + 3 * s, bx + 3 * s, y - barHalf - 3 * s);
        }

        // The lock time inside its bar, when it fits.
        if (line.Segments.Count > 0)
        {
            var first = line.Segments[0];
            string words = BarWords(text, line.Time.Normal);
            var box = Rectangle.Round(Bar(first.Start + 10, first.Width - 14, y, barHalf));
            var size = TextRenderer.MeasureText(words, NoteFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            if (size.Width <= box.Width) Write(g, words, NoteFont, box, t.OnAccent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        // Where Locks would be without the battery's cut.
        if (line.Ghost is float ghost)
        {
            float r = 20 * s;
            using var pen = new Pen(t.Accent, 1.5f * s) { DashStyle = DashStyle.Dash };
            g.DrawEllipse(pen, X(ghost) - r, y - r, 2 * r, 2 * r);
        }

        // Play's dot, between the stops as it moves.
        if (playhead is float head)
        {
            float r = 8 * s;
            using var halo = new SolidBrush(Color.FromArgb(70, t.Accent));
            g.FillEllipse(halo, X(head) - r - 4 * s, y - r - 4 * s, 2 * r + 8 * s, 2 * r + 8 * s);
            using var fill = new SolidBrush(t.Card);
            g.FillEllipse(fill, X(head) - r, y - r, 2 * r, 2 * r);
            using var ring = new Pen(t.Accent, 3 * s);
            g.DrawEllipse(ring, X(head) - r, y - r, 2 * r, 2 * r);
        }

        DrawNotes(g, t);
        DrawNames(g, t);
    }

    // A position along the line, in this control's pixels from its left. A control the window does
    // not mirror flips it itself.
    float X(float logical) => Flip ? Width - Pos(logical) : Pos(logical);

    RectangleF Bar(float start, float width, float y, float half)
    {
        float a = X(start), b = X(start + Math.Max(0, width));
        return new RectangleF(Math.Min(a, b), y - half, Math.Abs(b - a), 2 * half);
    }

    void Stripes(Graphics g, RectangleF box, Theme t, bool fade)
    {
        if (box.Width <= 0) return;
        var light = Blend(t.Accent, t.Card, 0.72f);
        var dark = Blend(t.Accent, t.Card, 0.4f);
        var state = g.Save();
        g.SetClip(box);
        using (var brush = new SolidBrush(light)) g.FillRectangle(brush, box);
        float step = 9 * Ratio;
        using (var pen = new Pen(dark, 4 * Ratio))
        {
            for (float x = box.Left - box.Height; x < box.Right + box.Height; x += step)
                g.DrawLine(pen, x, box.Bottom, x + box.Height, box.Top);
        }
        if (fade)
        {
            // Faded most of the way, not all of it, so the stripes still run off the edge.
            var back = Color.FromArgb(200, t.Card);
            var far = Flip ? new RectangleF(box.Left - 1, box.Top, box.Width * 0.5f, box.Height) : new RectangleF(box.Left + box.Width * 0.5f, box.Top, box.Width * 0.5f + 1, box.Height);
            using var brush = new LinearGradientBrush(far, Flip ? back : Color.FromArgb(0, back), Flip ? Color.FromArgb(0, back) : back, LinearGradientMode.Horizontal);
            g.FillRectangle(brush, far);
        }
        g.Restore(state);
    }

    static Color Blend(Color a, Color b, float toB) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * toB), (int)(a.G + (b.G - a.G) * toB), (int)(a.B + (b.B - a.B) * toB));

    // The notes over the line: each extra's time over its part, the battery's cut over Locks, never
    // over its stripes, and on the Locking page the shortcut, which locks at once. A note that would
    // run into one already placed shows only its icon, or not at all.
    void DrawNotes(Graphics g, Theme t)
    {
        var notes = new List<(string? Glyph, string Words, float At, int Align)>();
        if (line.Never)
        {
            notes.Add((null, text.V1.NeverLocks, line.StretchEnd + 10, -1));
        }
        else
        {
            foreach (var segment in line.Segments.Skip(1))
                notes.Add((GlyphOf(segment.Source), string.Format(text.V1.CustomExtra, text.ShortDuration(segment.Minutes, standalone: true)), segment.Start + segment.Width / 2, 0));
            if (line.Time.CutApplied && line.Time.Cut is ActiveEffect cut)
                notes.Add((Glyphs.Battery, text.SoonerLabel(cut.Effect.Amount), line.Ghost is float ghost ? (line.Locked + ghost) / 2 : line.Locked, 0));
            if (page == 2 && choices.HasShortcut)
                notes.Add((Glyphs.Keyboard, string.Format(text.V1.LocksInstantly, choices.Shortcut), line.Locked, 0));
        }
        var placed = new List<(int Left, int Right)>();
        int top = Px(6);
        int height = NoteRow;
        int gap = Px(8);
        int glyphWidth = Px(18);
        foreach (var (glyph, words, at, align) in notes)
        {
            int wordsWidth = TextRenderer.MeasureText(words, NoteFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            foreach (bool withWords in new[] { true, false })
            {
                if (!withWords && glyph is null) break;
                int width = (glyph is null ? 0 : glyphWidth) + (withWords ? wordsWidth : 0);
                int left = align < 0 ? (int)Pos(at) : (int)(Pos(at) - width / 2f);
                left = Math.Clamp(left, 0, Math.Max(0, Width - width));
                if (placed.Any(p => left < p.Right + gap && left + width > p.Left - gap)) continue;
                placed.Add((left, left + width));
                int x = left;
                if (glyph is not null)
                {
                    DrawText(g, glyph, Icons(10f), Box(x, top, glyphWidth, height), t.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                    x += glyphWidth;
                }
                if (withWords) Write(g, words, NoteFont, Box(x, top, wordsWidth + 1, height), t.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                break;
            }
        }
    }

    static string? GlyphOf(Situation source) => source switch
    {
        Situation.Music => Glyphs.Music,
        Situation.Call => Glyphs.Microphone,
        Situation.FullScreen => Glyphs.Fullscreen,
        Situation.Battery => Glyphs.Battery,
        _ => null,
    };

    // The names under the stops, in a first row, or in a second where they would run into the one
    // before. The first name starts at the start of the line and the last ends at its end.
    void DrawNames(Graphics g, Theme t)
    {
        names.Clear();
        var rows = new List<List<(int Left, int Right)>> { new(), new() };
        int gap = Px(10);
        foreach (var stop in stops)
        {
            if (!stop.Visible && (stop.Moment != Moment.Idle && line.Never)) continue;
            float at = line.X(stop.Moment);
            if (float.IsNaN(at)) continue;
            string words = WelcomeText.Stop(text, stop.Moment, choices.Warns);
            bool chosen = stop.Moment == moment;
            var font = chosen ? NameChosen : NameFont;
            int width = TextRenderer.MeasureText(words, NameChosen, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + Px(2);
            int left = stop.Moment == Moment.Idle ? (int)Pos(at) - Px(17)
                : stop.Moment == Moment.Back ? (int)Pos(at) + Px(17) - width
                : (int)(Pos(at) - width / 2f);
            left = Math.Clamp(left, 0, Math.Max(0, Width - width));
            int row = rows[0].Any(p => left < p.Right + gap && left + width > p.Left - gap) ? 1 : 0;
            rows[row].Add((left, left + width));
            var box = Box(left, NamesTop + row * NameRow, width, NameRow);
            names.Add((stop.Moment, box));
            var color = !stop.Enabled ? t.Subtle : chosen ? t.Text : t.Subtle;
            Write(g, words, font, box, color, TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | (stop.Moment == Moment.Back ? TextFormatFlags.Right : TextFormatFlags.Left));
        }
    }

    protected override Automation.Provider CreateProvider() => new(this, Automation.GroupType)
    {
        Selection = ChosenProviders,
    };

    // One stop: a circle with its icon, filled in the accent while its moment is shown.
    internal sealed class Stop : Part
    {
        public Stop(Moment moment)
        {
            Moment = moment;
            AccessibleRole = AccessibleRole.RadioButton;
        }

        public Moment Moment { get; }

        WelcomeTimeline? Owner => Parent as WelcomeTimeline;

        internal override bool IsChosen => Owner?.moment == Moment;

        string Glyph => Moment switch
        {
            Moment.Idle => Glyphs.Mouse,
            Moment.Warning => Glyphs.Bell,
            Moment.Locked => Glyphs.Lock,
            Moment.Away => Glyphs.Walk,
            _ => Glyphs.Unlock,
        };

        protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) != Keys.Enter && base.IsInputKey(keyData);

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            Smooth(g);
            bool chosen = IsChosen;
            float ring = Px(4);
            var circle = new RectangleF(ring, ring, Width - 2 * ring - 1, Height - 2 * ring - 1);
            if (chosen)
            {
                using var halo = new SolidBrush(t.Soft);
                g.FillEllipse(halo, 0, 0, Width - 1, Height - 1);
            }
            using (var brush = new SolidBrush(chosen ? t.Accent : t.Card)) g.FillEllipse(brush, circle);
            var edge = chosen || Hovered && Enabled ? t.Accent : t.FieldLine;
            using (var pen = new Pen(edge, Math.Max(1, Px(2) * 0.75f)))
            {
                if (!Enabled) pen.DashStyle = DashStyle.Dash;
                g.DrawEllipse(pen, circle);
            }
            var color = chosen ? t.OnAccent : Hovered && Enabled ? t.Accent : t.Subtle;
            var box = Rectangle.Round(circle);
            DrawText(g, Glyph, Icons(Moment == Moment.Locked ? 12f : 10.5f), box, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (!Enabled)
            {
                using var pen = new Pen(t.Subtle, Math.Max(1, Px(1)));
                float m = Width * 0.32f;
                g.DrawLine(pen, m, m, Width - m, Height - m);
            }
            if (Focused && ShowFocusCues)
            {
                using var pen = new Pen(t.Text, Px(2));
                g.DrawEllipse(pen, Px(1), Px(1), Width - Px(2) - 1, Height - Px(2) - 1);
            }
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new StopAccessible(this);

        protected override Automation.Provider CreateProvider() => new(this, Automation.RadioButtonType)
        {
            Press = () => Owner?.Choose(this),
            IsSelected = () => IsChosen,
            Container = () => Owner?.AutomationProvider,
        };

        sealed class StopAccessible(Stop owner) : ControlAccessibleObject(owner)
        {
            public override AccessibleStates State =>
                base.State | (owner.IsChosen ? AccessibleStates.Checked : AccessibleStates.None);

            public override string DefaultAction => Lang.Current.SelectAction;

            public override void DoDefaultAction() => owner.Owner?.Choose(owner);
        }
    }
}
