using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms.Automation;

namespace SteppedOut;

// The corner notes: a slim pill with the logo, a bold title, a muted detail and a thin line along
// the bottom that runs down with the time left. Every note looks the same: the card's thin neutral
// edge, the title in the text color with only the time in it (the seconds left, the time away) in
// the accent, and the line in the accent on a soft accent track. The countdown before locking has no
// button (D21); the others go away by themselves. Break and eye-break notes get Snooze when it is on. Try it shows the countdown note for 5 seconds on its own timer and
// never locks; a real countdown that starts meanwhile takes its place.
//
// The pill is a layered window drawn from one bitmap, so its round ends are smooth. It never takes
// the keyboard: it is shown without activating, refuses activation by a click, and has no control
// that can hold the focus.
sealed class Toast : Form
{
    public const int PreviewSeconds = 5;
    const int AwaySeconds = 8;
    const int ReminderSeconds = 20;
    const int WsExNoActivate = 0x08000000;
    const int WsExLayered = 0x00080000;
    const int WmMouseActivate = 0x0021;
    const int WmDpiChanged = 0x02E0;
    const int MaNoActivate = 3;

    enum Showing { None, Countdown, Preview, Note }

    readonly bool headless;
    readonly System.Windows.Forms.Timer frame = new() { Interval = 50 };
    readonly Dictionary<int, Bitmap> logos = new();

    Showing showing;
    string title = "";
    // Where the time is in the title, drawn in the accent; no time is (0, 0).
    (int At, int Length) time;
    string detail = "";
    ReminderKind? reminder;
    bool snoozeShown;

    bool locking;
    int total = 1;
    int left;
    long secondAt;
    long previewStart;
    long hideAt;
    long noteLength = 1;
    // The note stays until it is taken back or clicked away, with its line full.
    bool lasting;
    // A lasting note the countdown, Try it or another note took the place of, or that came while the
    // countdown ran: it shows again once that is over, unless it was taken back meanwhile.
    (string Title, string Detail)? held;
    long lastStep;

    NotePosition place = NotePlace.InUse(null, Lang.ShellIsRightToLeft());
    bool snoozeOn;
    Rectangle area;
    int room = 1;
    int dpi = 96;
    int widest;
    Rectangle bounds;
    Rectangle snoozeBox;
    bool snoozeHot;
    bool snoozePressed;
    Point pointerAtShow;
    bool pointerCame;

    // Snooze on a break or eye-break note, with the kind it was on. Never raised by the countdown.
    public event Action<ReminderKind>? SnoozeClicked;

    // How many times the Try it note went: its 5 seconds ran out, a real countdown or another note
    // took its place, or it was hidden. For the tests.
    internal int PreviewsEnded { get; private set; }

    public Toast() : this(false)
    {
    }

    // headless: the notes do everything except make a window, for the tests and the render sheets.
    internal Toast(bool headless)
    {
        this.headless = headless;
        Text = AppInfo.Name;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        frame.Tick += (_, _) => Step();
    }

    // Seams for the tests and the render sheets.
    internal Func<long> Clock { get; set; } = () => Environment.TickCount64;
    internal Func<Point> Pointer { get; set; } = () => Cursor.Position;
    internal Func<Point, Rectangle> Area { get; set; } = point => WorkArea.Of(Screen.FromPoint(point));

    // The monitor's dpi where the note shows. Unset, a headless note is at 96 and a real one asks
    // Windows; the render sheets set it to draw the notes at 200%.
    internal Func<Point, int>? Dpi { get; set; }

    internal bool IsShowing => showing != Showing.None;
    internal bool IsCounting => showing == Showing.Countdown;
    internal bool IsPreviewing => showing == Showing.Preview;
    internal string Title => title;
    internal string Detail => detail;
    internal bool SnoozeShown => snoozeShown;
    internal NotePosition Place => place;

    // Where the note is, in screen pixels, and where its Snooze button is inside it.
    internal Rectangle NoteBounds => bounds;
    internal Rectangle SnoozeBox => snoozeBox;

    // The place picked in the settings and whether Snooze is on. Until a place is picked the notes
    // keep to the shell's side, as before 1.0 (D37), and until this is called they have no Snooze.
    public void Use(Config config)
    {
        place = NotePlace.InUse(config.NotificationPlace, Lang.ShellIsRightToLeft());
        snoozeOn = config.SnoozeMinutes > 0;
        if (showing == Showing.Note) snoozeShown = reminder is not null && snoozeOn;
        if (showing != Showing.None) Present(announce: false);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            // Never take focus. Activating the note would change the foreground window, which is
            // both an interruption and a false signal to the fullscreen and jitter checks.
            var cp = base.CreateParams;
            cp.ExStyle |= WsExNoActivate | Native.WsExToolWindow | WsExLayered;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WmMouseActivate:
                // A click on the note leaves the keyboard where it was.
                m.Result = MaNoActivate;
                return;
            case WmDpiChanged:
                // Sized for the monitor it is on when it is drawn; Windows' own resize would undo that.
                m.Result = IntPtr.Zero;
                return;
        }
        base.WndProc(ref m);
    }

    // ---- The countdown ----

    public void ShowCountdown(int seconds, bool locking)
    {
        Hold();
        if (showing == Showing.Preview) EndPreview(hide: false);
        showing = Showing.Countdown;
        this.locking = locking;
        total = Math.Max(1, seconds);
        widest = 0;
        reminder = null;
        snoozeShown = false;
        Count(seconds);
        Present(announce: true);
    }

    public void SetSeconds(int seconds)
    {
        if (showing != Showing.Countdown) return;
        Count(seconds);
        Redraw();
    }

    void Count(int seconds)
    {
        left = Math.Max(0, seconds);
        secondAt = Clock();
        var text = Lang.Current;
        (title, detail, time) = Timed(locking ? text.V1.CountdownNote : text.V1.CountdownNoteAway, text.ShortLeft(Math.Max(1, left)));
    }

    public void EndCountdown()
    {
        if (showing != Showing.Countdown) return;
        Over();
    }

    // ---- Try it ----

    // Shows the countdown note for 5 seconds. It never locks and raises nothing.
    // A real countdown matters more, so while one runs this does nothing and returns false.
    public bool ShowPreview()
    {
        if (showing == Showing.Countdown) return false;
        Hold();
        showing = Showing.Preview;
        previewStart = Clock();
        widest = 0;
        reminder = null;
        snoozeShown = false;
        (title, detail, time) = Timed(Lang.Current.V1.PreviewNote, Lang.Current.ShortLeft(PreviewSeconds));
        Present(announce: true);
        return true;
    }

    void EndPreview(bool hide = true)
    {
        showing = Showing.None;
        frame.Stop();
        if (hide && !headless && Visible) Hide();
        PreviewsEnded++;
        if (hide) BringBack();
    }

    // ---- Notes that go away by themselves ----

    // "You were away 42 min", with "· music or video is playing again" when coming back played it.
    public void ShowAway(TimeSpan away, bool playingAgain)
    {
        var text = Lang.Current;
        var (awayTitle, _, awayTime) = Timed(text.V1.YouWereAway, text.ShortDuration(away));
        Note(awayTitle, playingAgain ? text.V1.MediaPlayingAgain : "", AwaySeconds, null, time: awayTime);
    }

    // A break or an eye break, with Snooze when it is on.
    public void ShowReminder(Reminder due)
    {
        var text = Lang.Current;
        if (due.Kind == ReminderKind.EyeBreak)
            Note(text.V1.EyeBreakTitle, text.V1.EyeBreakText, ReminderSeconds, due.Kind);
        else
            Note(text.V1.TimeForABreak, text.AtThePcFor(TimeSpan.FromSeconds(due.SecondsAtThePc)), ReminderSeconds, due.Kind);
    }

    // A note with a title and a line, such as the one about config.json. Like every note, it
    // carries the logo.
    public void ShowNote(string noteTitle, string noteBody, int seconds) => Note(noteTitle, noteBody, seconds, null);

    // A note that does not go by itself: it stays until it is taken back (HideNote) once what it
    // says is no longer true, or it is clicked away. Its line stays full. The countdown, Try it or
    // another note may take its place for a while; it comes back when they are over. For what stays
    // true until something changes, as when the locking may not work.
    public void ShowLastingNote(string noteTitle, string noteBody) => Note(noteTitle, noteBody, 0, null, lasting: true);

    // Takes a note away early once what it says is no longer true, also while it waits to come back.
    public void HideNote(string noteTitle)
    {
        if (held?.Title == noteTitle) held = null;
        if (showing != Showing.Note || title != noteTitle) return;
        Over();
    }

    void Note(string noteTitle, string noteDetail, int seconds, ReminderKind? kind, bool lasting = false, (int At, int Length) time = default)
    {
        // The countdown matters more, so a note never replaces it; a lasting one waits for it.
        if (showing == Showing.Countdown)
        {
            if (lasting) held = (noteTitle, Dotted(noteDetail));
            return;
        }
        if (showing == Showing.Note && title != noteTitle) Hold();
        if (lasting && held?.Title == noteTitle) held = null;
        if (showing == Showing.Preview) EndPreview(hide: false);
        showing = Showing.Note;
        title = noteTitle;
        this.time = time;
        detail = Dotted(noteDetail);
        reminder = kind;
        snoozeShown = kind is not null && snoozeOn;
        widest = 0;
        this.lasting = lasting;
        noteLength = Math.Max(1, seconds) * 1000L;
        hideAt = Clock() + noteLength;
        // Where the pointer rests when the note appears; it only holds the note once it moves.
        pointerAtShow = Pointer();
        pointerCame = false;
        Present(announce: true);
    }

    void HideNow()
    {
        showing = Showing.None;
        frame.Stop();
        snoozeHot = snoozePressed = false;
        if (!headless && Visible) Hide();
    }

    // A lasting note that something else is about to take the place of waits to come back.
    void Hold()
    {
        if (showing == Showing.Note && lasting) held = (title, detail);
    }

    // What showed is over: the note goes, and a lasting note that waited comes back.
    void Over()
    {
        HideNow();
        BringBack();
    }

    void BringBack()
    {
        if (held is not { } note) return;
        held = null;
        Note(note.Title, note.Detail, 0, null, lasting: true);
    }

    // "Locking in 15 sec · move the mouse or press a key to cancel": the bold part and the muted part.
    static (string Title, string Detail) Split(string line)
    {
        int at = line.IndexOf(" · ", StringComparison.Ordinal);
        return at < 0 ? (line, "") : (line[..at], line[(at + 1)..]);
    }

    // A line made from a template whose {0} is a time, as the bold part and the muted part, and where
    // the time is in the bold part. Found from where {0} is in the template, not by looking for
    // digits, so it is right in every language; a time that falls in the muted part is (0, 0).
    internal static (string Title, string Detail, (int At, int Length) Time) Timed(string template, string time)
    {
        var (lineTitle, lineDetail) = Split(string.Format(template, time));
        int mark = template.IndexOf("{0}", StringComparison.Ordinal);
        if (mark < 0) return (lineTitle, lineDetail, default);
        int at = template[..mark].Replace("{{", "{").Replace("}}", "}").Length;
        return (lineTitle, lineDetail, time.Length > 0 && at + time.Length <= lineTitle.Length ? (at, time.Length) : default);
    }

    static string Dotted(string detail) => detail.Length == 0 || detail.StartsWith('·') ? detail : "· " + detail;

    // ---- Time passing ----

    // One frame: the preview counts down and ends, a note goes once its time is up (held while the
    // pointer is on it, so Snooze can be reached, but only once the pointer has moved since the note
    // appeared: one that was resting where the note came up does not keep it), and the line runs down.
    // A lasting note has nothing that runs down, so it is drawn and the frames stop.
    internal void Step()
    {
        long now = Clock();
        switch (showing)
        {
            case Showing.None:
                frame.Stop();
                return;
            case Showing.Preview:
                long gone = now - previewStart;
                if (gone >= PreviewSeconds * 1000L)
                {
                    EndPreview();
                    return;
                }
                (title, detail, time) = Timed(Lang.Current.V1.PreviewNote, Lang.Current.ShortLeft((PreviewSeconds * 1000L - gone) / 1000.0));
                break;
            case Showing.Note when lasting:
                frame.Stop();
                break;
            case Showing.Note:
                var at = Pointer();
                if (at != pointerAtShow) pointerCame = true;
                if (pointerCame && bounds.Contains(at)) hideAt = Math.Max(hideAt + (now - lastStep), now + 1000);
                else if (now >= hideAt)
                {
                    Over();
                    return;
                }
                break;
        }
        lastStep = now;
        Redraw();
    }

    // How much of the line is left, 1 when the note appears and 0 when its time is up.
    internal double LineLeft()
    {
        long now = Clock();
        return showing switch
        {
            Showing.Countdown => Math.Clamp((left - Math.Clamp((now - secondAt) / 1000.0, 0, 1)) / total, 0, 1),
            Showing.Preview => Math.Clamp(1 - (now - previewStart) / (PreviewSeconds * 1000.0), 0, 1),
            Showing.Note => lasting ? 1 : Math.Clamp((hideAt - now) / (double)noteLength, 0, 1),
            _ => 0,
        };
    }

    // ---- Placing and drawing ----

    void Present(bool announce)
    {
        lastStep = Clock();
        var pointer = Pointer();
        area = Area(pointer);
        dpi = Dpi?.Invoke(pointer) ?? (headless ? 96 : MonitorDpi(pointer));
        Measure();
        if (headless) return;
        _ = Handle;
        Push();
        if (!Visible) Show();
        frame.Start();
        if (announce) Announce();
    }

    void Redraw()
    {
        if (showing == Showing.None) return;
        Measure();
        if (!headless && IsHandleCreated) Push();
    }

    // The note's size and place. A countdown never gets narrower as its seconds go down, so its
    // free end stays put.
    void Measure()
    {
        int margin = Scale(NotePlace.Margin);
        room = Math.Max(Scale(120), Math.Min(Scale(600), area.Width - 2 * margin));
        var layout = Arrange(0, room);
        int width = Math.Max(layout.Size.Width, showing == Showing.Note ? 0 : widest);
        if (showing != Showing.Note) widest = width;
        bounds = NotePlace.For(place, area, new Size(width, layout.Size.Height), margin);
        snoozeBox = Arrange(bounds.Width, room).Snooze;
    }

    int Scale(int logical) => (int)Math.Round(logical * dpi / 96.0);

    Font TitleFont => Theme.Strong(9f, dpi);

    Font DetailFont => Theme.Body(9f, dpi);

    Font ButtonFont => Theme.Strong(9f, dpi);

    const TextFormatFlags OneLine = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

    const TextFormatFlags Lines = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;

    // How the words are drawn. Not clipped to their boxes: the boxes are as tall as the font says,
    // and Arabic marks below the line (the kasratan of ثوانٍ) reach past that.
    static TextFormatFlags Drawing(TextFormatFlags flags, bool rtl) =>
        flags | TextFormatFlags.NoClipping | (rtl ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : 0);

    // Wrapped: the detail did not fit beside the title, so it is on the lines below it. Time: the
    // columns of the title the time is drawn in, in the accent, the pill's height tall; empty when
    // the title has no time.
    internal readonly record struct PillLayout(Size Size, Rectangle Logo, Rectangle Title, Rectangle Detail, Rectangle Snooze, bool RightToLeft, Size TitleNeeds, Size DetailNeeds, bool Wrapped = false, Rectangle Time = default);

    // On its own lines the detail does not need the dot that parts it from the title.
    string LinesText => detail.TrimStart('·', ' ');

    // The detail's lines at a width, as the wrapped note draws them.
    internal Size DetailLines(int width) =>
        TextRenderer.MeasureText(LinesText, DetailFont, new Size(Math.Max(1, width), int.MaxValue), Lines | (Lang.Current.RightToLeft ? TextFormatFlags.RightToLeft : 0));

    // Logo, title, detail and Snooze in reading order, mirrored for Arabic. width 0 is the width the
    // words need, up to most; a narrower width shortens the detail first, then the title. A note
    // whose detail does not fit beside its title within most puts the detail on lines below it.
    internal PillLayout Arrange(int width, int most)
    {
        bool rtl = Lang.Current.RightToLeft;
        var titleSize = TextRenderer.MeasureText(title, TitleFont, Size.Empty, OneLine);
        var detailSize = detail.Length == 0 ? Size.Empty : TextRenderer.MeasureText(detail, DetailFont, Size.Empty, OneLine);
        var snoozeText = snoozeShown ? TextRenderer.MeasureText(Lang.Current.V1.SnoozeButton, ButtonFont, Size.Empty, OneLine) : Size.Empty;
        int textHeight = Math.Max(titleSize.Height, detailSize.Height);
        int height = Math.Max(Scale(30), textHeight + Scale(14));
        // Arabic marks below the line (the kasratan of ثوانٍ) reach past the box the font reports, by
        // more as the text grows. The one-line pill gets taller at the bottom for them, so a clear row
        // of the card stays between them and the line; the words, logo and Snooze keep their places.
        int marks = rtl ? (int)Math.Ceiling(textHeight * 0.15) : 0;
        int lineRoom = Math.Max(2, Scale(2)) + 2 * Math.Max(1, Scale(1));
        int under = height - (height - textHeight) / 2 - textHeight;
        int taller = Math.Max(0, marks + lineRoom - under);
        int buttonHeight = snoozeShown ? Math.Min(height - Scale(8), Math.Max(Scale(24), snoozeText.Height + Scale(8))) : 0;
        int buttonWidth = snoozeShown ? snoozeText.Width + Scale(22) : 0;
        int start = Scale(9), logo = Scale(18), afterLogo = Scale(7), gap = detail.Length == 0 ? 0 : Scale(4);
        int end = snoozeShown ? Scale(12) + buttonWidth + Scale(6) : Scale(16);
        int words = start + logo + afterLogo + titleSize.Width + gap + detailSize.Width + end;
        Rectangle Mirrored(Rectangle r, int across) => rtl && !r.IsEmpty ? new Rectangle(across - r.Right, r.Y, r.Width, r.Height) : r;

        if (showing == Showing.Note && detail.Length > 0 && words > most)
        {
            int text = Math.Max(Scale(24), most - start - logo - afterLogo - end);
            var lines = DetailLines(text);
            if (width <= 0) width = Math.Min(most, start + logo + afterLogo + Math.Max(Math.Min(titleSize.Width, text), lines.Width) + end);
            int across = Math.Max(0, width - start - logo - afterLogo - end);
            int top = Scale(8);
            var titleRow = new Rectangle(start + logo + afterLogo, top, Math.Min(titleSize.Width, across), titleSize.Height);
            var linesBox = new Rectangle(titleRow.X, titleRow.Bottom + Scale(2), Math.Min(lines.Width, across), lines.Height);
            int tall = Math.Max(Scale(30), linesBox.Bottom + Scale(10));
            var mark = new Rectangle(start, top + (titleSize.Height - logo) / 2, logo, logo);
            var button = snoozeShown ? new Rectangle(width - Scale(6) - buttonWidth, (tall - buttonHeight) / 2, buttonWidth, buttonHeight) : Rectangle.Empty;
            var titleAt = Mirrored(titleRow, width);
            return new PillLayout(new Size(width, tall), Mirrored(mark, width), titleAt, Mirrored(linesBox, width), Mirrored(button, width), rtl, titleSize, lines, Wrapped: true, Time: TimeIn(titleAt, tall, rtl));
        }
        if (width <= 0) width = Math.Min(words, most);

        int titleWidth = Math.Max(0, Math.Min(titleSize.Width, width - start - logo - afterLogo - end));
        int detailWidth = Math.Max(0, Math.Min(detailSize.Width, width - start - logo - afterLogo - titleWidth - gap - end));
        if (detailWidth < Scale(24)) detailWidth = 0;

        // Title and detail share a baseline, whatever their fonts.
        int titleAscent = Ascent(TitleFont), detailAscent = Ascent(DetailFont);
        int baseline = (height - textHeight) / 2 + Math.Max(titleAscent, detailAscent);
        var logoBox = new Rectangle(start, (height - logo) / 2, logo, logo);
        var titleBox = new Rectangle(logoBox.Right + afterLogo, baseline - titleAscent, titleWidth, titleSize.Height);
        var detailBox = new Rectangle(titleBox.Right + gap, baseline - detailAscent, detailWidth, detailSize.Height);
        var snooze = snoozeShown ? new Rectangle(width - Scale(6) - buttonWidth, (height - buttonHeight) / 2, buttonWidth, buttonHeight) : Rectangle.Empty;
        var titleOn = Mirrored(titleBox, width);
        return new PillLayout(new Size(width, height + taller), Mirrored(logoBox, width), titleOn, Mirrored(detailBox, width), Mirrored(snooze, width), rtl, titleSize, detailSize, Time: TimeIn(titleOn, height + taller, rtl));
    }

    // The columns of the title the time takes, from the widths of the words before it and through
    // it, counted from the title's reading start: the left in English, the right in Arabic. A time
    // at either end of the title runs to that end of its box.
    Rectangle TimeIn(Rectangle box, int tall, bool rtl)
    {
        if (time.Length == 0 || box.Width <= 0 || time.At + time.Length > title.Length) return Rectangle.Empty;
        var flags = OneLine | (rtl ? TextFormatFlags.RightToLeft : 0);
        int before = time.At == 0 ? 0 : TextRenderer.MeasureText(title[..time.At], TitleFont, Size.Empty, flags).Width;
        int through = time.At + time.Length == title.Length ? box.Width : TextRenderer.MeasureText(title[..(time.At + time.Length)], TitleFont, Size.Empty, flags).Width;
        int from = Math.Min(before, box.Width), to = Math.Min(through, box.Width);
        if (to <= from) return Rectangle.Empty;
        return rtl ? new Rectangle(box.Right - to, 0, to - from, tall) : new Rectangle(box.X + from, 0, to - from, tall);
    }

    static int Ascent(Font font)
    {
        var family = font.FontFamily;
        double em = family.GetEmHeight(font.Style);
        return (int)Math.Round(family.GetCellAscent(font.Style) / em * font.SizeInPoints * Theme.SystemDpi / 72.0);
    }

    // The note's layout as it is drawn now.
    internal PillLayout Drawn => Arrange(bounds.Width, room);

    // The note as it looks now, with see-through corners: what the window shows.
    internal Bitmap Picture()
    {
        if (bounds.Width <= 0) Measure();
        return Draw(Drawn, LineLeft());
    }

    // Black-on-white ink turned into a color on a background over one area, each of red, green and
    // blue on its own so ClearType's look is kept: white is the background, black the color, and
    // nothing goes past the color.
    internal static Bitmap Tinted(Bitmap ink, Rectangle area, Color back, Color color)
    {
        var tinted = new Bitmap(ink.Width, ink.Height, PixelFormat.Format24bppRgb);
        var from = ink.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var to = tinted.LockBits(area, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[area.Width * 3];
            for (int y = 0; y < area.Height; y++)
            {
                Marshal.Copy(from.Scan0 + y * from.Stride, row, 0, row.Length);
                for (int i = 0; i < row.Length; i += 3)
                {
                    row[i] = Mix(back.B, color.B, row[i]);
                    row[i + 1] = Mix(back.G, color.G, row[i + 1]);
                    row[i + 2] = Mix(back.R, color.R, row[i + 2]);
                }
                Marshal.Copy(row, 0, to.Scan0 + y * to.Stride, row.Length);
            }
        }
        finally
        {
            ink.UnlockBits(from);
            tinted.UnlockBits(to);
        }
        return tinted;

        static byte Mix(int back, int color, int white) => (byte)((back * white + color * (255 - white) + 127) / 255);
    }

    Bitmap Draw(PillLayout layout, double lineLeft)
    {
        var t = Theme.Current;
        // The app's thin card edge; the accent is only on the line, the time and the logo.
        Color edge = t.Edge;
        int w = layout.Size.Width, h = layout.Size.Height;
        // Round ends on the one-line pill; the same corners on a note that runs to more lines.
        float radius = layout.Wrapped ? Math.Min(h / 2f, Scale(15)) : h / 2f;
        int border = Math.Max(1, Scale(1));

        using var content = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(content))
        {
            g.Clear(edge);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var inner = Shapes.Rounded(new RectangleF(border, border, w - 2 * border, h - 2 * border), radius - border);
            using (var card = new SolidBrush(t.Card))
                g.FillPath(card, inner);

            // The line runs along the flat bottom, from the reading start, inside the border.
            int lineHeight = Math.Max(2, Scale(2));
            int lineTop = h - border - lineHeight;
            double round = radius - border, dy = lineTop + lineHeight / 2.0 - h / 2.0;
            int inset = (int)Math.Ceiling(radius - Math.Sqrt(Math.Max(0, round * round - dy * dy)));
            int run = Math.Max(0, w - 2 * inset);
            int done = (int)Math.Round(run * Math.Clamp(lineLeft, 0, 1));
            g.SetClip(inner);
            g.SmoothingMode = SmoothingMode.None;
            using (var brush = new SolidBrush(t.AccentSoft))
                g.FillRectangle(brush, inset, lineTop, run, lineHeight);
            if (done > 0)
            {
                using var brush = new SolidBrush(t.Accent);
                g.FillRectangle(brush, layout.RightToLeft ? w - inset - done : inset, lineTop, done, lineHeight);
            }
            g.ResetClip();
            g.SmoothingMode = SmoothingMode.AntiAlias;

            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(LogoOf(layout.Logo.Width), layout.Logo);

            var flags = Drawing(OneLine | TextFormatFlags.EndEllipsis, layout.RightToLeft);
            // The title in the text color, then its time in the accent: the whole title is drawn again in
            // black on white, and over just the time's columns, from the top edge down to the line, each
            // pixel goes from the card to the accent by how much ink it got, so every word keeps the place
            // one run of text gives it, in both languages. Windows draws the small mark under the last
            // letter of the Arabic word for seconds with more than full ink, which in a color comes out
            // darker than that color, nearly black; as ink it only reaches the full accent. The columns
            // run below the title box because that mark hangs below it.
            TextRenderer.DrawText(g, title, TitleFont, layout.Title, t.Text, t.Card, flags);
            var columns = Rectangle.Intersect(Rectangle.FromLTRB(layout.Time.Left, border, layout.Time.Right, lineTop), new Rectangle(0, 0, w, h));
            if (!layout.Time.IsEmpty && !columns.IsEmpty)
            {
                using var ink = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                using (var r = Graphics.FromImage(ink))
                {
                    r.Clear(Color.White);
                    TextRenderer.DrawText(r, title, TitleFont, layout.Title, Color.Black, Color.White, flags);
                }
                using var tinted = Tinted(ink, columns, t.Card, t.Accent);
                g.SetClip(inner);
                g.IntersectClip(columns);
                g.DrawImageUnscaled(tinted, 0, 0);
                g.ResetClip();
            }
            if (detail.Length > 0 && layout.Detail.Width > 0)
            {
                if (layout.Wrapped) TextRenderer.DrawText(g, LinesText, DetailFont, layout.Detail, t.Subtle, t.Card, Drawing(Lines, layout.RightToLeft));
                else TextRenderer.DrawText(g, detail, DetailFont, layout.Detail, t.Subtle, t.Card, flags);
            }

            if (!layout.Snooze.IsEmpty)
            {
                Color fill = snoozePressed ? t.Line : snoozeHot ? t.FieldHover : t.Field;
                using (var path = Shapes.Rounded(Shapes.Inside(layout.Snooze), layout.Snooze.Height / 2f))
                {
                    using var brush = new SolidBrush(fill);
                    g.FillPath(brush, path);
                    using var pen = new Pen(t.FieldLine, 1);
                    g.DrawPath(pen, path);
                }
                TextRenderer.DrawText(g, Lang.Current.V1.SnoozeButton, ButtonFont, layout.Snooze, t.Text, fill,
                    OneLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | (layout.RightToLeft ? TextFormatFlags.RightToLeft : 0));
            }
        }

        // The pill's outline, smoothed, becomes the see-through part.
        using var mask = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(mask))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var outline = Shapes.Rounded(new RectangleF(0, 0, w, h), radius);
            g.FillPath(Brushes.White, outline);
        }
        var picture = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var all = new Rectangle(0, 0, w, h);
        var c = content.LockBits(all, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var m = mask.LockBits(all, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var p = picture.LockBits(all, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var colors = new int[w * h];
            var alphas = new int[w * h];
            for (int y = 0; y < h; y++)
            {
                Marshal.Copy(c.Scan0 + y * c.Stride, colors, y * w, w);
                Marshal.Copy(m.Scan0 + y * m.Stride, alphas, y * w, w);
            }
            for (int i = 0; i < colors.Length; i++)
                colors[i] = (colors[i] & 0x00FFFFFF) | (alphas[i] & unchecked((int)0xFF000000));
            for (int y = 0; y < h; y++)
                Marshal.Copy(colors, y * w, p.Scan0 + y * p.Stride, w);
        }
        finally
        {
            content.UnlockBits(c);
            mask.UnlockBits(m);
            picture.UnlockBits(p);
        }
        return picture;
    }

    Bitmap LogoOf(int size)
    {
        if (!logos.TryGetValue(size, out var logo)) logos[size] = logo = Logo.Draw(size);
        return logo;
    }

    // Puts the picture on the screen as the window's whole look, see-through corners and all.
    void Push()
    {
        using var picture = Picture();
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr memory = CreateCompatibleDC(screen);
        IntPtr bitmap = picture.GetHbitmap(Color.FromArgb(0));
        IntPtr old = SelectObject(memory, bitmap);
        try
        {
            var size = new SIZE { Cx = picture.Width, Cy = picture.Height };
            var source = new POINT();
            var at = new POINT { X = bounds.X, Y = bounds.Y };
            var blend = new BLENDFUNCTION { SourceConstantAlpha = 255, AlphaFormat = AcSrcAlpha };
            UpdateLayeredWindow(Handle, screen, ref at, ref size, memory, ref source, 0, ref blend, UlwAlpha);
        }
        finally
        {
            SelectObject(memory, old);
            DeleteObject(bitmap);
            DeleteDC(memory);
            _ = ReleaseDC(IntPtr.Zero, screen);
        }
    }

    // The note never takes focus, so a screen reader would not notice it; say it out loud instead.
    void Announce()
    {
        if (!IsHandleCreated) return;
        AccessibilityObject.RaiseAutomationNotification(
            AutomationNotificationKind.Other,
            AutomationNotificationProcessing.ImportantMostRecent,
            Spoken);
    }

    internal string Spoken => detail.Length == 0 ? title : title + " " + detail;

    // ---- The pointer ----

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool hot = snoozeBox.Contains(e.Location);
        if (hot == snoozeHot) return;
        snoozeHot = hot;
        Redraw();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!snoozeHot && !snoozePressed) return;
        snoozeHot = snoozePressed = false;
        Redraw();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !snoozeBox.Contains(e.Location)) return;
        snoozePressed = true;
        Redraw();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        bool pressed = snoozePressed;
        snoozePressed = false;
        Clicked(e.Location, pressed);
    }

    // A click inside the note at a point in its own pixels: Snooze snoozes, anywhere else on a note
    // that goes away by itself puts it away. The countdown and Try it ignore clicks.
    internal void Clicked(Point at, bool pressedOnSnooze = true)
    {
        if (pressedOnSnooze && snoozeShown && snoozeBox.Contains(at)) PressSnooze();
        else if (showing == Showing.Note && !snoozeBox.Contains(at)) Over();
        else Redraw();
    }

    internal void PressSnooze()
    {
        if (showing != Showing.Note || !snoozeShown || reminder is not { } kind) return;
        Over();
        SnoozeClicked?.Invoke(kind);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        // Hidden from outside: what it showed is over.
        if (Visible || showing == Showing.None) return;
        if (showing == Showing.Preview) EndPreview(hide: false);
        else HideNow();
    }

    // ---- Screen readers ----

    protected override AccessibleObject CreateAccessibilityInstance() => new NoteAccessible(this);

    sealed class NoteAccessible(Toast owner) : ControlAccessibleObject(owner)
    {
        readonly SnoozeAccessible snooze = new(owner);

        public override string? Name => owner.IsShowing ? owner.Spoken : owner.Text;

        public override AccessibleRole Role => AccessibleRole.Alert;

        public override int GetChildCount() => owner.snoozeShown && owner.IsShowing ? 1 : 0;

        public override AccessibleObject? GetChild(int index) => index == 0 && GetChildCount() == 1 ? snooze : null;
    }

    sealed class SnoozeAccessible(Toast owner) : AccessibleObject
    {
        public override string? Name => Lang.Current.V1.SnoozeButton;

        public override AccessibleRole Role => AccessibleRole.PushButton;

        public override string DefaultAction => Lang.Current.V1.SnoozeButton;

        public override AccessibleObject? Parent => owner.AccessibilityObject;

        public override AccessibleStates State => AccessibleStates.None;

        public override Rectangle Bounds
        {
            get
            {
                Rectangle box = owner.SnoozeBox, note = owner.NoteBounds;
                return box.IsEmpty ? Rectangle.Empty : new Rectangle(note.X + box.X, note.Y + box.Y, box.Width, box.Height);
            }
        }

        public override void DoDefaultAction() => owner.PressSnooze();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideNow();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            frame.Dispose();
            foreach (var logo in logos.Values) logo.Dispose();
            logos.Clear();
        }
        base.Dispose(disposing);
    }

    // ---- Windows ----

    int MonitorDpi(Point point)
    {
        try
        {
            IntPtr monitor = MonitorFromPoint(new POINT { X = point.X, Y = point.Y }, MonitorDefaultToNearest);
            if (GetDpiForMonitor(monitor, MdtEffectiveDpi, out uint x, out _) == 0 && x > 0) return (int)x;
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
        return DeviceDpi;
    }

    const int UlwAlpha = 2;
    const byte AcSrcAlpha = 1;
    const uint MonitorDefaultToNearest = 2;
    const int MdtEffectiveDpi = 0;

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE
    {
        public int Cx, Cy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct BLENDFUNCTION
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screenDc, ref POINT at, ref SIZE size, IntPtr sourceDc, ref POINT source, int key, ref BLENDFUNCTION blend, int flags);

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}
