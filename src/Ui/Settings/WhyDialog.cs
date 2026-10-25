namespace SteppedOut;

// "Why isn't the PC locking?": what is delaying or stopping the lock right now, each with what it
// does to the lock time, under the lock time it comes to, asked again every second (D33). While
// paused, away, locked or with locking off, the top line says that instead, since then it is the
// reason. Close or Esc closes it. The rows scroll inside their card when they would not fit the
// screen, and Tab then reaches them so the arrow and page keys scroll them.
sealed class WhyDialog : Form
{
    readonly ISettingsHost host;
    readonly Func<bool> lockingOn;
    readonly Strings text;
    readonly Label heading;
    readonly Card list;
    readonly WhyRows rows;
    readonly KitButton close;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    IReadOnlyList<WhyLine>? shown;
    bool emptyShown;

    // Where the lines between rows go, inside the rows.
    readonly List<int> lines = new();

    public WhyDialog(ISettingsHost host, Func<bool> lockingOn, Strings text)
    {
        this.host = host;
        this.lockingOn = lockingOn;
        this.text = text;
        var v = text.V1;
        var t = Theme.Current;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        RightToLeft = text.RightToLeft ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = text.RightToLeft;
        Font = Theme.Body(9.75f);
        Text = v.WhyNotLocking;
        t.Dress(this);

        heading = new Label { AutoSize = false, UseMnemonic = false, Font = Theme.Strong(10.5f), ForeColor = t.Text, BackColor = Color.Transparent };
        list = new Card { Outline = theme => theme.Edge, TabStop = false };
        rows = new WhyRows { AccessibleName = v.WhyNotLocking };
        t.DressScrollBars(rows);
        list.Controls.Add(rows);
        close = new KitButton(v.Close) { DialogResult = DialogResult.Cancel, Name = "close" };
        Controls.AddRange([heading, list, close]);
        AcceptButton = close;
        CancelButton = close;
        ActiveControl = close;
        rows.Paint += (_, pe) =>
        {
            using var pen = new Pen(Theme.Current.Line);
            int scroll = rows.AutoScrollPosition.Y;
            foreach (int y in lines) pe.Graphics.DrawLine(pen, Px(12) - Px(1), y + scroll, rows.ClientSize.Width - Px(12) + Px(1) - 1, y + scroll);
        };
        // The card's edge shows where the keys go while the rows have them.
        list.Paint += (_, pe) =>
        {
            if (!rows.Focused || !rows.ShowCues) return;
            pe.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(Theme.Current.Text, Px(2));
            using var path = Shapes.Rounded(new RectangleF(Px(1), Px(1), list.Width - Px(2) - 1, list.Height - Px(2) - 1), Px(8));
            pe.Graphics.DrawPath(pen, path);
        };
        rows.GotFocus += (_, _) => list.Invalidate();
        rows.LostFocus += (_, _) => list.Invalidate();
        timer.Tick += (_, _) => Tick();
        Tick();
    }

    // How many times the rows were made, for tests: only when what they say changes.
    internal int Builds { get; private set; }

    internal string Heading => heading.Text;

    internal bool Ticking => timer.Enabled;

    internal Card List => list;

    // What scrolls inside the card: the rows.
    internal ScrollableControl Scroller => rows;

    // The rows' words in order: what is going on, then what it does; or the one line saying nothing is.
    internal IReadOnlyList<string> Lines => rows.Controls.OfType<Label>().Select(l => l.Text).ToList();

    // Asks the app again: what is going on, and the lock time it comes to.
    internal void Tick()
    {
        IReadOnlyList<WhyLine> lines;
        LockStatus status;
        try
        {
            lines = host.Explain();
            status = host.Status;
        }
        catch (Exception e)
        {
            Log.Write("why window: " + e.Message);
            return;
        }
        var (top, nothing) = TopLine(status, lines, lockingOn(), text);
        bool changed = heading.Text != top;
        heading.Text = top;
        heading.AccessibleName = top;
        bool empty = nothing && lines.Count == 0;
        if (shown is null || !shown.SequenceEqual(lines) || empty != emptyShown)
        {
            Fill(lines, empty);
            shown = lines.ToList();
            emptyShown = empty;
            changed = true;
        }
        if (changed) Arrange();
    }

    // The top line, and whether an empty list means nothing is in the way. The lock time comes from
    // the run's own normal time (which --idle may change) and what is going on now.
    internal static (string Top, bool SaysNothing) TopLine(LockStatus status, IReadOnlyList<WhyLine> lines, bool lockingOn, Strings text)
    {
        bool reasonOfItsOwn = status.Kind is LockState.Starting or LockState.Paused or LockState.Away or LockState.SessionLocked or LockState.LockingOff
            || !lockingOn;
        if (reasonOfItsOwn) return (text.Headline(status, lockingOn), false);
        var time = LockTime.Compute(status.Time.Normal, Evaluator.Effects(lines));
        string line = text.WhyLockTime(time);
        return (status.DryRun ? string.Format(text.V1.DryRun, line) : line, true);
    }

    void Fill(IReadOnlyList<WhyLine> lines, bool empty)
    {
        Builds++;
        var t = Theme.Current;
        rows.SuspendLayout();
        foreach (Control old in rows.Controls.Cast<Control>().ToList())
        {
            rows.Controls.Remove(old);
            old.Dispose();
        }
        if (empty)
        {
            rows.Controls.Add(Words(text.V1.WhyNothing, t.Subtle, Theme.Body(9.75f)));
        }
        foreach (var line in lines)
        {
            var (what, does) = text.WhyRow(line);
            rows.Controls.Add(Words(what, t.Text, Theme.Body(9.75f)));
            rows.Controls.Add(Words(does, line.Counts && line.Effect.Kind != EffectKind.None ? t.Accent : t.Subtle, Theme.Strong(9.75f)));
        }
        rows.ResumeLayout(false);
    }

    static Label Words(string words, Color color, Font font) => new()
    {
        Text = words,
        AccessibleName = words,
        AutoSize = false,
        UseMnemonic = false,
        ForeColor = color,
        Font = font,
        BackColor = Color.Transparent,
    };

    int Px(int logical) => LogicalToDeviceUnits(logical);

    // The top line, the rows in a card (each thing at the start, what it does at the end, a line
    // between them) and Close at the end. The rows scroll when the window would not fit the screen,
    // with the scroll bar inside the card's edge, clear of its round corners.
    void Arrange()
    {
        int width = Px(440);
        int pad = Px(20);
        int inner = width - pad * 2;
        heading.Bounds = new Rectangle(pad, pad, inner, Shapes.WrappedHeight(heading.Text, heading.Font, inner));
        var area = WorkArea.Of(Owner is { } owner ? Screen.FromControl(owner) : Screen.FromControl(this));
        var closeSize = close.GetPreferredSize(Size.Empty);
        int buttonWidth = Math.Max(Px(80), closeSize.Width);
        int chrome = Height - ClientSize.Height;
        int most = area.Height * 4 / 5 - chrome - (heading.Bottom + Px(12)) - (Px(20) + closeSize.Height + pad);
        // Laid out at the card's full width inside its edge, and again beside the scroll bar when it
        // does not fit, as wide as a scroll bar is on this monitor.
        int edge = Px(1);
        int corner = Px(6);
        int listHeight = Rows(inner - 2 * edge, Px(10));
        bool scrolls = listHeight > most;
        int beside = inner - 2 * edge - SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi);
        if (scrolls) listHeight = Rows(beside, Px(10) - corner);
        int listShown = Math.Max(Px(36), Math.Min(listHeight, most));
        rows.AutoScrollMinSize = Size.Empty;
        list.Bounds = new Rectangle(pad, heading.Bottom + Px(12), inner, listShown);
        rows.Bounds = scrolls
            ? new Rectangle(edge, corner, inner - 2 * edge, listShown - 2 * corner)
            : new Rectangle(edge, 0, inner - 2 * edge, listShown);
        if (scrolls) rows.AutoScrollMinSize = new Size(0, listHeight);
        // Beside the scroll bar as Windows draws it, once the rows have a window of their own.
        if (scrolls && rows.IsHandleCreated && rows.ClientSize.Width != beside)
        {
            listHeight = Rows(rows.ClientSize.Width, Px(10) - corner);
            rows.AutoScrollMinSize = new Size(0, listHeight);
        }
        rows.TabStop = scrolls;
        close.Bounds = new Rectangle(width - pad - buttonWidth, list.Bottom + Px(20), buttonWidth, closeSize.Height);
        ClientSize = new Size(width, close.Bottom + pad);
        list.Invalidate();
        rows.Invalidate();
    }

    // Puts the rows in a card this wide, each thing at the start and what it does at the end, and
    // gives back how tall they come to, from top. The window mirrors in Arabic but the card in it
    // does not (WinForms keeps a mirrored form's children unmirrored), so the rows are flipped here.
    // They sit 12 px in from the card's edge, which is 1 px outside them.
    int Rows(int width, int top)
    {
        bool flip = rows.RightToLeft == RightToLeft.Yes && !rows.IsMirrored;
        Rectangle Box(int x, int y, int w, int h) => new(flip ? width - x - w : x, y, w, h);
        int rowPad = Px(12) - Px(1);
        int content = width - rowPad * 2;
        var labels = rows.Controls.OfType<Label>().ToList();
        int y = top;
        int scroll = rows.AutoScrollPosition.Y;
        if (emptyShown && labels.Count > 0)
        {
            var only = labels[0];
            only.Bounds = Box(rowPad, y + scroll, content, Shapes.WrappedHeight(only.Text, only.Font, content));
            y += only.Height + Px(10);
            labels = labels.Skip(1).ToList();
        }
        lines.Clear();
        for (int i = 0; i + 1 < labels.Count; i += 2)
        {
            var what = labels[i];
            var does = labels[i + 1];
            int doesWidth = Math.Min(Shapes.Measure(does.Text, does.Font).Width + Px(2), content * 2 / 5);
            int whatWidth = content - doesWidth - Px(12);
            int whatHeight = Shapes.WrappedHeight(what.Text, what.Font, whatWidth);
            int doesHeight = Shapes.WrappedHeight(does.Text, does.Font, doesWidth);
            if (i > 0) lines.Add(y - Px(5));
            what.Bounds = Box(rowPad, y + scroll, whatWidth, whatHeight);
            does.Bounds = Box(rowPad + content - doesWidth, y + scroll, doesWidth, doesHeight);
            // The end of the row: the right in English; a label in Arabic turns this to the left.
            does.TextAlign = ContentAlignment.TopRight;
            y += Math.Max(whatHeight, doesHeight) + Px(10);
        }
        return y;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Arrange();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        timer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Stop();
        base.OnFormClosed(e);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Arrange();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Stop();
            timer.Dispose();
        }
        base.Dispose(disposing);
    }

    // The rows, inside the card's edge. A stop for Tab while they scroll: the arrow keys move them a
    // little, Page Up and Page Down a page, Home and End to either end.
    sealed class WhyRows : Panel
    {
        public WhyRows()
        {
            SetStyle(ControlStyles.Selectable | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.SupportsTransparentBackColor, true);
            AutoScroll = true;
            TabStop = false;
            BackColor = Color.Transparent;
            AccessibleRole = AccessibleRole.Pane;
        }

        internal bool ShowCues => ShowFocusCues;

        protected override bool IsInputKey(Keys keyData) =>
            keyData is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int step = LogicalToDeviceUnits(24);
            int page = Math.Max(step, ClientSize.Height - step);
            int at = -AutoScrollPosition.Y;
            int? to = e.KeyCode switch
            {
                Keys.Up => at - step,
                Keys.Down => at + step,
                Keys.PageUp => at - page,
                Keys.PageDown => at + page,
                Keys.Home => 0,
                Keys.End => DisplayRectangle.Height,
                _ => null,
            };
            if (to is not int y) return;
            AutoScrollPosition = new Point(0, Math.Max(0, y));
            e.Handled = true;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (TabStop) Focus();
            base.OnMouseDown(e);
        }
    }
}
