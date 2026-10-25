namespace SteppedOut;

// The top of the settings window: the ring with the logo, what the app is doing right now and why,
// the time away and the locks today, and Pause and Lock now. It reads the status from the app and
// shows it again each time the app says it changed. The box is green while the app is on, as the
// sketch draws it with and without a call; amber while the countdown runs; grey while paused, away
// or locked (D20).
sealed class StatusBox : Card
{
    readonly ISettingsHost host;
    readonly Strings text;
    readonly Ring ring = new(Ring.Large);
    readonly TextLine headline = new(13.5f, strong: true, t => t.Text) { OneLine = true };
    readonly TextLine sentence = new(9f, strong: false, t => t.Subtle);
    readonly TextLine why = new(9f, strong: false, t => t.Accent);
    readonly Stat away;
    readonly Stat locked;
    readonly KitButton pause;
    readonly KitButton lockNow;
    bool lockingOn = true;
    bool paused;
    // Whether the line under the sentence shows: only when something changes the lock time.
    bool whyShown;
    BoxTone tone;

    public StatusBox(ISettingsHost host, Strings text)
    {
        this.host = host;
        this.text = text;
        var v = text.V1;
        away = new Stat(Glyphs.Clock, () => Look(tone));
        locked = new Stat(Glyphs.Lock, () => Look(tone));
        pause = new KitButton(v.Pause) { Chevron = true };
        lockNow = new KitButton(v.LockNow, primary: true);
        pause.Click += (_, _) => PauseClicked();
        lockNow.Click += (_, _) => host.LockNowChosen();
        Controls.Add(ring);
        Controls.Add(headline);
        Controls.Add(sentence);
        Controls.Add(why);
        Controls.Add(away);
        Controls.Add(locked);
        Controls.Add(pause);
        Controls.Add(lockNow);
        AccessibleRole = AccessibleRole.StatusBar;
        UseTone(BoxTone.Green);
        host.StatusChanged += OnStatusChanged;
        ShowStatus();
    }

    // Whether locking is on (At the lock time is not "Don't lock"): while it is off, the time counts
    // down to the away actions instead.
    public bool LockingOn
    {
        get => lockingOn;
        set
        {
            if (lockingOn == value) return;
            lockingOn = value;
            ShowStatus();
        }
    }

    // For tests and the render tool.
    internal string Headline => headline.Text;
    internal string Sentence => sentence.Text;
    internal string Why => why.Text;
    internal bool WhyShown => whyShown;
    internal string AwayText => away.Value;
    internal string LockedText => locked.Value;
    internal KitButton PauseButton => pause;
    internal KitButton LockNowButton => lockNow;
    internal Ring Ring => ring;
    internal BoxTone Tone => tone;

    // The choices under Pause: the one list the tray menu also shows (MenuLook.PauseChoices).
    internal ToolStripItem[] PauseChoices() => MenuLook.PauseChoices(text, length => host.PauseChosen(length), PauseFor);

    // Custom…: the Pause for window, in front of the settings window.
    void PauseFor()
    {
        if (IsDisposed) return;
        PauseForWindow.Open(FindForm(), text, length => host.PauseChosen(length));
    }

    void PauseClicked()
    {
        if (paused)
        {
            host.ResumeChosen();
            return;
        }
        var menu = MenuLook.NewMenu(RightToLeft);
        menu.Items.AddRange(PauseChoices());
        MenuLook.ShowBelow(menu, pause);
    }

    void OnStatusChanged()
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            // Told from another thread: shown on the window's own, once it has one.
            if (!IsHandleCreated) return;
            try
            {
                BeginInvoke(ShowStatus);
            }
            catch (InvalidOperationException)
            {
                // Closing at that moment.
            }
            return;
        }
        ShowStatus();
    }

    void ShowStatus()
    {
        if (IsDisposed) return;
        var v = text.V1;
        var status = host.Status;
        int before = GetPreferredSize(new Size(Width, 0)).Height;
        headline.Text = text.Headline(status, lockingOn);
        sentence.Text = text.Sentence(status, lockingOn);
        string because = text.Why(status);
        why.Text = because;
        whyShown = because.Length > 0;
        why.Visible = whyShown;
        away.Set(v.AwayToday, text.AwayToday(status.Today.Away));
        locked.Set(v.LockedToday, text.Times(status.Today.Locks));
        ring.Fraction = Ring.FractionOf(status);
        AccessibleName = headline.Text + ". " + sentence.Text + (because.Length > 0 ? ". " + because : "");

        bool nowPaused = status.Kind == LockState.Paused;
        if (nowPaused != paused)
        {
            paused = nowPaused;
            pause.Text = paused ? v.TurnBackOn : v.Pause;
            pause.Chevron = !paused;
        }
        var nowTone = ToneOf(status);
        if (nowTone != tone) UseTone(nowTone);

        PerformLayout();
        if (GetPreferredSize(new Size(Width, 0)).Height != before) Relayout();
    }

    void Relayout()
    {
        Parent?.PerformLayout();
        PerformLayout();
        Invalidate(true);
    }

    // Green while the app is on, a call or anything else that keeps it from locking too, as the
    // sketch draws "won't lock during a call"; amber while the countdown runs; grey while paused, away
    // or locked. A dry run takes the tone of what it shows.
    internal static BoxTone ToneOf(LockStatus status) => status.Kind switch
    {
        LockState.Countdown => BoxTone.Amber,
        LockState.Paused or LockState.Away or LockState.SessionLocked => BoxTone.Grey,
        _ => BoxTone.Green,
    };

    // The box's fill and edge, and the fill and edge of the two counts inside it.
    internal static (Color Fill, Color Edge, Color Inner, Color InnerEdge) Look(BoxTone tone)
    {
        var t = Theme.Current;
        return tone switch
        {
            BoxTone.Amber => (t.WarningSoft, t.Warning, t.Card, t.Line),
            BoxTone.Grey => (t.Card, t.Edge, t.Window, t.Line),
            _ => (t.Hero, t.HeroEdge, t.HeroIn, t.HeroEdge),
        };
    }

    void UseTone(BoxTone tone)
    {
        this.tone = tone;
        Fill = Look(tone).Fill;
        Outline = _ => Look(this.tone).Edge;
        // The buttons' rounded corners and the counts show the box's color, so they repaint with it.
        Invalidate(true);
    }

    // At 100%: 18 px above and below, 20 at the sides and between the ring, the words and the buttons.
    int PadX => Px(20);
    int PadY => Px(18);
    int Gap => Px(20);

    int RingSide => ring.GetPreferredSize(Size.Empty).Width;

    // The sketch's buttons: 32 px tall, 14 px each side of the words, and on Pause the chevron 6 px
    // after them. KitButton's own size is the welcome's, 34 px tall with 18 px sides, and the kit
    // has no size like the sketch's yet, so the box gives its two buttons this room itself.
    // KitButton draws its words in the middle of what its 20 px chevron room leaves, and the
    // chevron 18 to 30 px in from its edge, so with the chevron a button is 48 px wider than its
    // words. Pause and Turn back on take one width, the wider of the two, so pausing moves nothing.
    Size PauseRoom => new(Math.Max(Words(pause, text.V1.Pause) + Px(48), Words(pause, text.V1.TurnBackOn) + Px(28)), ButtonHeight);
    Size LockRoom => new(Words(lockNow, text.V1.LockNow) + Px(28), ButtonHeight);

    // What the box gives the button, for the checks.
    internal Size RoomFor(KitButton button) => button == pause ? PauseRoom : LockRoom;

    // KitButton draws a primary button's words in semibold and the others' in the regular face.
    Font ButtonFont(KitButton button) => button.Primary ? Theme.Strong(10f, DeviceDpi) : Theme.Body(10f, DeviceDpi);

    const TextFormatFlags Measuring = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

    int Words(KitButton button, string words) => TextRenderer.MeasureText(words, ButtonFont(button), Size.Empty, Measuring).Width;

    int ButtonHeight => Math.Max(Px(32), TextRenderer.MeasureText("Ag", Theme.Strong(10f, DeviceDpi)).Height + Px(12));

    int ButtonsWidth => PauseRoom.Width + Px(8) + LockRoom.Width;

    int TextLeft => PadX + RingSide + Gap;

    // The words' width: beside the buttons, or the whole width beside the ring when the buttons go
    // under the counts.
    int TextWidth(int width) => Math.Max(Px(120), Down(width) ? width - TextLeft - PadX : Beside(width));

    int Beside(int width) => width - TextLeft - Gap - ButtonsWidth - PadX;

    // At Windows' largest text sizes the buttons leave too little room beside them for a count, which
    // would be cut off: they go under the counts instead, at the start of the words, and the words
    // and the counts take the whole width beside the ring. At the sketch's sizes they stay at the end.
    bool Down(int width)
    {
        int room = CountsRoom(Beside(width));
        return away.GetPreferredSize(Size.Empty).Width > room || locked.GetPreferredSize(Size.Empty).Width > room;
    }

    // Whether the buttons are under the counts, for the tests and the render tool.
    internal bool ButtonsDown => Width > 0 && Down(Width);

    // The counts have nothing else on their line, so they may take the room before the buttons
    // too, but keep 12 px from them.
    int CountsRoom(int textWidth) => textWidth + Gap - Px(12);

    // When the counts do not fit beside the buttons, as a long day's counts in Arabic, the buttons
    // rise to the headline's line and the counts run under them, out to the box's padding.
    int CountsUnderRoom(int textWidth) => textWidth + Gap + ButtonsWidth;

    // The words and the counts under them, from the top of the column: where each starts, the
    // height of the whole, and whether the buttons rise to the headline or, down, where they go
    // under the counts, 12 px under them. The lines are 3 px apart and the counts 9 px under them,
    // as the sketch's line height spaces them.
    (int Headline, int Sentence, int Why, int StatsTop, int Second, int Height, bool ButtonsUp, int ButtonsTop) Column(int width, bool down)
    {
        int y = 0;
        int head = y;
        y += headline.HeightFor(width);
        int sent = y + Px(3);
        y = sent + sentence.HeightFor(width);
        int because = y + Px(3);
        if (whyShown) y = because + why.HeightFor(width);
        int stats = y + Px(9);
        int second = stats;
        bool up = false;
        var a = away.GetPreferredSize(Size.Empty);
        var b = locked.GetPreferredSize(Size.Empty);
        int both = a.Width + Px(8) + b.Width;
        if (down)
        {
            // Side by side when both fit the width, the second under the first otherwise.
            if (both > width) second = stats + a.Height + Px(6);
            int buttons = second + b.Height + Px(12);
            return (head, sent, because, stats, second, buttons + ButtonHeight, false, buttons);
        }
        // Side by side when both fit beside the buttons, or under the buttons when they fit there
        // and the buttons clear them; the second under the first otherwise.
        if (both > CountsRoom(width))
        {
            if (both <= CountsUnderRoom(width) && stats >= ButtonHeight + Px(6)) up = true;
            else second = stats + a.Height + Px(6);
        }
        y = second + b.Height;
        return (head, sent, because, stats, second, y, up, 0);
    }

    (int Headline, int Sentence, int Why, int StatsTop, int Second, int Height, bool ButtonsUp, int ButtonsTop) Column(int boxWidth, out int width)
    {
        bool down = Down(boxWidth);
        width = TextWidth(boxWidth);
        return Column(width, down);
    }

    // Whether the buttons sit up on the headline's line, for the tests.
    internal bool ButtonsUp => Width > 0 && Column(Width, out _).ButtonsUp;

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        int column = Column(width, out _).Height;
        int inner = Math.Max(RingSide, Math.Max(column, ButtonHeight));
        // And the box's own edge, a pixel at the top and at the bottom.
        return new Size(width, inner + PadY * 2 + 2);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (Width <= 0) return;
        int side = RingSide;
        ring.Bounds = Box(PadX, (Height - side) / 2, side, side);

        var c = Column(Width, out int width);
        bool down = c.ButtonsTop > 0;
        int top = (Height - c.Height) / 2;

        var lockSize = LockRoom;
        var pauseSize = PauseRoom;
        if (down)
        {
            pause.Bounds = Box(TextLeft, top + c.ButtonsTop, pauseSize.Width, pauseSize.Height);
            lockNow.Bounds = Box(TextLeft + pauseSize.Width + Px(8), top + c.ButtonsTop, lockSize.Width, lockSize.Height);
        }
        else
        {
            int lockX = Width - PadX - lockSize.Width;
            int pauseX = lockX - Px(8) - pauseSize.Width;
            int lockY = c.ButtonsUp ? top + c.Headline : (Height - lockSize.Height) / 2;
            int pauseY = c.ButtonsUp ? top + c.Headline : (Height - pauseSize.Height) / 2;
            lockNow.Bounds = Box(lockX, lockY, lockSize.Width, lockSize.Height);
            pause.Bounds = Box(pauseX, pauseY, pauseSize.Width, pauseSize.Height);
        }

        headline.Bounds = Box(TextLeft, top + c.Headline, width, headline.HeightFor(width));
        sentence.Bounds = Box(TextLeft, top + c.Sentence, width, sentence.HeightFor(width));
        why.Bounds = Box(TextLeft, top + c.Why, width, whyShown ? why.HeightFor(width) : 0);
        var a = away.GetPreferredSize(Size.Empty);
        var b = locked.GetPreferredSize(Size.Empty);
        int room = down ? width : c.ButtonsUp ? CountsUnderRoom(width) : CountsRoom(width);
        away.Bounds = Box(TextLeft, top + c.StatsTop, Math.Min(a.Width, room), a.Height);
        int secondX = c.Second == c.StatsTop ? TextLeft + a.Width + Px(8) : TextLeft;
        locked.Bounds = Box(secondX, top + c.Second, Math.Min(b.Width, room), b.Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) host.StatusChanged -= OnStatusChanged;
        base.Dispose(disposing);
    }

    // Words that wrap at the width they are given, or stay on one line, drawn by hand so they follow
    // the screen's scaling and the render tool can check that none of them is cut off by mistake.
    internal sealed class TextLine : KitControl
    {
        readonly float points;
        readonly bool strong;
        readonly Func<Theme, Color> color;
        ToolTip? tip;

        public TextLine(float points, bool strong, Func<Theme, Color> color)
        {
            this.points = points;
            this.strong = strong;
            this.color = color;
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            AccessibleRole = AccessibleRole.StaticText;
        }

        internal Font Face => strong ? Strong(points) : Body(points);

        // A line that never wraps, as the sketch's headline: cut with "…" when it is too long, as the
        // tray menu cuts the same words, with the whole of them in a tip and for screen readers.
        public bool OneLine { get; init; }

        // Without the room Windows adds at the ends of the words, measured and drawn alike, so the
        // lines start where the counts under them do.
        const TextFormatFlags Wrapping = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        const TextFormatFlags Single = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        TextFormatFlags Flags => OneLine ? Single | TextFormatFlags.EndEllipsis : Wrapping;

        // In Arabic, taller than Windows measures by the marks set under the letters, such as the two
        // under the last letter of "seconds" in the countdown: they hang below the line's height.
        public int HeightFor(int width) => Text.Length == 0 ? 0
            : (OneLine ? TextRenderer.MeasureText(Text, Face, Size.Empty, Single).Height
                : TextRenderer.MeasureText(Text, Face, new Size(Math.Max(1, width), int.MaxValue), Wrapping).Height)
                + (RightToLeft == RightToLeft.Yes ? Theme.MarksBelow(Face) : 0);

        // Whether a line that never wraps is too long for its width and cut.
        internal bool Cut => OneLine && Text.Length > 0 && TextRenderer.MeasureText(Text, Face, Size.Empty, Single).Width > Width;

        // The tip over a cut line, for the tests and the render tool.
        internal string? Tip => tip?.GetToolTip(this) is { Length: > 0 } whole ? whole : null;

        void Retip()
        {
            string? whole = Cut ? Text : null;
            if (whole is null && tip is null) return;
            tip ??= new ToolTip { ShowAlways = true };
            if (tip.GetToolTip(this) != (whole ?? "")) tip.SetToolTip(this, whole);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            AccessibleName = Text;
            Invalidate();
            Retip();
            base.OnTextChanged(e);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            Retip();
            base.OnSizeChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Text.Length > 0) Write(e.Graphics, Text, Face, ClientRectangle, color(Theme.Current), Flags);
            base.OnPaint(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip?.Dispose();
            base.Dispose(disposing);
        }
    }

    // "Away today: 1 hr 20 min" in a small box: an icon, the words, and the count in semibold.
    internal sealed class Stat : KitControl
    {
        const TextFormatFlags Flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter;
        // Measured without centering, which measures nothing in a box of no height.
        const TextFormatFlags Measuring = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        readonly string glyph;
        readonly Func<(Color Fill, Color Edge, Color Inner, Color InnerEdge)> look;
        string label = "";

        public Stat(string glyph, Func<(Color, Color, Color, Color)> look)
        {
            this.glyph = glyph;
            this.look = look;
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            AccessibleRole = AccessibleRole.StaticText;
        }

        public string Value { get; private set; } = "";

        public void Set(string label, string value)
        {
            if (this.label == label && Value == value) return;
            this.label = label;
            Value = value;
            AccessibleName = label + " " + value;
            Invalidate();
        }

        Font LabelFont => Body(9f);
        Font ValueFont => Strong(9f);
        Font IconFont => Icons(10.5f);

        static int Wide(string text, Font font) => TextRenderer.MeasureText(text, font, Size.Empty, Measuring).Width;

        int Line => Math.Max(TextRenderer.MeasureText("Ag", ValueFont, Size.Empty, Measuring).Height, TextRenderer.MeasureText(glyph, IconFont, Size.Empty, Measuring).Height);

        public override Size GetPreferredSize(Size proposedSize) =>
            new(Px(10) + Wide(glyph, IconFont) + Px(6) + Wide(label, LabelFont) + Px(4) + Wide(Value, ValueFont) + Px(10) + 2, Line + Px(5) * 2 + 2);

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            Smooth(g);
            var (_, _, inner, innerEdge) = look();
            var box = Shapes.Inside(ClientRectangle);
            using (var path = Shapes.Rounded(box, Px(6)))
            {
                using var brush = new SolidBrush(inner);
                g.FillPath(brush, path);
                using var pen = new Pen(innerEdge);
                g.DrawPath(pen, path);
            }
            int x = Px(10) + 1;
            int icon = Wide(glyph, IconFont);
            DrawText(g, glyph, IconFont, Box(x, 0, icon, Height), t.Accent, Flags | TextFormatFlags.HorizontalCenter);
            x += icon + Px(6);
            int words = Wide(label, LabelFont);
            Write(g, label, LabelFont, Box(x, 0, words, Height), t.Subtle, Flags);
            x += words + Px(4);
            int count = Wide(Value, ValueFont);
            Write(g, Value, ValueFont, Box(x, 0, count, Height), t.Text, Flags);
            base.OnPaint(e);
        }
    }
}

// The status box's color: counting toward the lock, held off or counting down, or stopped.
enum BoxTone
{
    Green,
    Amber,
    Grey,
}
