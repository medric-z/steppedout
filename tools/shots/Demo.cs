using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using SteppedOut;

// Renders the README demo: real windows of the app (drawn by PrintWindow) placed on a simple
// desktop, one PNG per frame plus durations.txt. Pillow turns them into a GIF afterwards.
static class Demo
{
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT r, int size);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    const int W = 960, H = 560, Bar = 48;

    sealed record Scene(Bitmap Frame, int Ms);

    public static void Run(string outDir, string lang, string theme)
    {
        bool dark = theme == "dark";
        bool ar = lang == "ar";
        string dir = Path.Combine(outDir, $"demo-{lang}-{theme}");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);

        // The real windows.
        var (counts, back) = Notes();

        var config = new Config { AppRules = { new AppRule("notepad", Effect.Never), new AppRule("mspaint", Effect.Never) } };
        var host = new Host(new LockStatus { Kind = LockState.On, Time = LockTime.Plain(15), SecondsLeft = 750 });
        var settings = new SettingsForm(config, host);
        Shots.Place(settings);
        settings.Show();
        Pump(800);
        var settingsShot = Grab(settings.Handle);
        settings.Close();
        settings.Dispose();

        var on = TrayIcons.Art(TrayIconState.On, 20, dark);
        var counting = TrayIcons.Art(TrayIconState.CountingDown, 20, dark);

        string[] captions = ar
            ? new[]
            {
                "ابتعد عن جهازك",
                "يظهر عدّ تنازلي صغير في الزاوية",
                "حرّك الماوس أو اضغط أي مفتاح لإلغائه",
                "ابقَ بعيدًا فيُقفل جهازك",
                "عُد فيخبرك كم غبت",
                "الإعدادات الأساسية أولًا، وبقيتها في علامات تبويب أسفلها",
            }
            : new[]
            {
                "Walk away from your PC",
                "A small countdown appears in the corner",
                "Move the mouse or press a key to cancel it",
                "Stay away and your PC locks",
                "Come back: it says how long you were away",
                "The main settings come first, the rest in tabs below",
            };

        var p = new Painter(dark, ar, captions.Length);
        var cursorRest = new Point(470, 300);
        var cursorAway = new Point(ar ? 250 : 700, 372);
        var scenes = new List<Scene>();

        scenes.Add(new(p.Desktop(0, captions[0], on, cursorRest), 1800));

        int[] slide = { 46, 16 };
        foreach (int offset in slide)
            scenes.Add(new(p.Desktop(1, captions[1], counting, cursorRest, counts[15], offset), 60));
        foreach (int n in new[] { 15, 14, 13, 12, 11, 10 })
            scenes.Add(new(p.Desktop(1, captions[1], counting, cursorRest, counts[n]), 650));

        for (int i = 1; i <= 4; i++)
        {
            var at = new Point(cursorRest.X + (cursorAway.X - cursorRest.X) * i / 4, cursorRest.Y + (cursorAway.Y - cursorRest.Y) * i / 4);
            scenes.Add(new(p.Desktop(2, captions[2], counting, at, counts[10]), 90));
        }
        scenes.Add(new(p.Desktop(2, captions[2], on, cursorAway), 1600));

        foreach (int n in new[] { 3, 2, 1 })
            scenes.Add(new(p.Desktop(3, captions[3], counting, cursorRest, counts[n]), 550));
        scenes.Add(new(p.LockScreen(3, captions[3]), 2300));

        scenes.Add(new(p.Desktop(4, captions[4], on, new Point(ar ? 360 : 600, 330), back), 2800));
        scenes.Add(new(p.Settings(5, captions[5], on, settingsShot), 3400));

        using var durations = new StreamWriter(Path.Combine(dir, "durations.txt"));
        for (int i = 0; i < scenes.Count; i++)
        {
            scenes[i].Frame.Save(Path.Combine(dir, $"{i:000}.png"), ImageFormat.Png);
            durations.WriteLine(scenes[i].Ms);
        }
        Console.WriteLine($"demo {lang} {theme}: {scenes.Count} frames");
    }

    // The app behind the settings window, which only says what it is doing.
    internal sealed class Host(LockStatus status) : ISettingsHost
    {
        public IReadOnlyList<WhyLine> Lines { get; set; } = [];

        public void PauseChosen(TimeSpan? length)
        {
        }

        public void ResumeChosen()
        {
        }

        public void LockNowChosen()
        {
        }

        public void TryItChosen()
        {
        }

        public void ShortcutChanged(string shortcut)
        {
        }

        public void LanguageChanged(string language)
        {
        }

        public HotKeyResult ShortcutResult => HotKeyResult.Ok;

        public event Action? ShortcutResultChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<WhyLine> Explain() => Lines;

        public void ConfigEdited(Config config)
        {
        }

        public bool StartsWithWindows => true;

        public AutostartState StartWithWindowsState => AutostartState.On;

        public event Action? StartWithWindowsChanged
        {
            add { }
            remove { }
        }

        public void StartWithWindowsChosen(bool on)
        {
        }

        public LockStatus Status => status;

        public event Action? StatusChanged
        {
            add { }
            remove { }
        }
    }

    sealed class Painter
    {
        readonly bool dark, ar;
        readonly int steps;
        readonly Color top, bottom, bar, barLine, ink, pill, pillInk, dot, dotOn;

        public Painter(bool dark, bool ar, int steps)
        {
            this.dark = dark;
            this.ar = ar;
            this.steps = steps;
            top = dark ? Color.FromArgb(0x1e, 0x2a, 0x37) : Color.FromArgb(0xdd, 0xe7, 0xf1);
            bottom = dark ? Color.FromArgb(0x15, 0x1d, 0x27) : Color.FromArgb(0xc8, 0xd6, 0xe4);
            bar = dark ? Color.FromArgb(0x1c, 0x1c, 0x1c) : Color.FromArgb(0xee, 0xf1, 0xf5);
            barLine = dark ? Color.FromArgb(0x30, 0x30, 0x30) : Color.FromArgb(0xd0, 0xd7, 0xde);
            ink = dark ? Color.White : Color.FromArgb(0x1b, 0x1b, 0x1b);
            pill = dark ? Color.FromArgb(0x2b, 0x2b, 0x2b) : Color.White;
            pillInk = dark ? Color.White : Color.FromArgb(0x1b, 0x1b, 0x1b);
            dot = dark ? Color.FromArgb(0x55, 0x5f, 0x6a) : Color.FromArgb(0xa9, 0xb6, 0xc4);
            dotOn = Color.FromArgb(0x0f, 0x7b, 0x5f);
        }

        Bitmap Start(out Graphics g)
        {
            var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using var sky = new LinearGradientBrush(new Rectangle(0, 0, W, H), top, bottom, 90f);
            g.FillRectangle(sky, 0, 0, W, H);
            return bmp;
        }

        public Bitmap Desktop(int step, string caption, Bitmap trayIcon, Point cursor, Bitmap? box = null, int slide = 0)
        {
            var bmp = Start(out var g);
            using (g)
            {
                Taskbar(g, trayIcon);
                if (box is not null)
                {
                    int x = ar ? 16 : W - box.Width - 16;
                    int y = H - Bar - box.Height - 16 + slide;
                    var clip = new Rectangle(0, 0, W, H - Bar);
                    g.SetClip(clip);
                    Note(g, box, x, y);
                    g.ResetClip();
                }
                Caption(g, step, caption);
                Cursor(g, cursor);
            }
            return bmp;
        }

        public Bitmap LockScreen(int step, string caption)
        {
            var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                using (var night = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(0x10, 0x1b, 0x2b), Color.FromArgb(0x08, 0x0d, 0x16), 90f))
                    g.FillRectangle(night, 0, 0, W, H);
                using var time = new Font("Segoe UI Light", 60f, GraphicsUnit.Pixel);
                using var date = new Font("Segoe UI", 18f, GraphicsUnit.Pixel);
                using var glyph = new Font("Segoe Fluent Icons", 26f, GraphicsUnit.Pixel);
                Centered(g, "21:41", time, Color.White, 170);
                Centered(g, ar ? "الأحد، 18 أكتوبر" : "Sunday, 18 October", date, Color.FromArgb(0xd8, 0xe0, 0xea), 252);
                Centered(g, "", glyph, Color.FromArgb(0xd8, 0xe0, 0xea), H - 90);
                Caption(g, step, caption);
            }
            return bmp;
        }

        public Bitmap Settings(int step, string caption, Bitmap trayIcon, Bitmap window)
        {
            var bmp = Start(out var g);
            using (g)
            {
                Taskbar(g, trayIcon);
                int shown = Math.Min(window.Height, 400);
                using var part = window.Clone(new Rectangle(0, 0, window.Width, shown), PixelFormat.Format32bppArgb);
                int x = (W - part.Width) / 2, y = 96;
                g.SetClip(new Rectangle(0, 0, W, H - Bar - 6));
                Window(g, part, x, y, 8);
                // The window runs on below the taskbar; it fades into the desktop instead of ending in a cut.
                var fade = new Rectangle(x - 20, y + shown - 70, part.Width + 40, 70);
                using (var brush = new LinearGradientBrush(fade, Color.FromArgb(0, Blend(y + shown - 70)), Color.FromArgb(255, Blend(y + shown)), 90f))
                    g.FillRectangle(brush, fade);
                g.ResetClip();
                Caption(g, step, caption);
            }
            return bmp;
        }

        Color Blend(int y)
        {
            double t = Math.Clamp(y / (double)H, 0, 1);
            return Color.FromArgb((int)(top.R + (bottom.R - top.R) * t), (int)(top.G + (bottom.G - top.G) * t), (int)(top.B + (bottom.B - top.B) * t));
        }

        void Taskbar(Graphics g, Bitmap trayIcon)
        {
            using (var b = new SolidBrush(bar)) g.FillRectangle(b, 0, H - Bar, W, Bar);
            using (var line = new Pen(barLine)) g.DrawLine(line, 0, H - Bar, W, H - Bar);
            using var glyphs = new Font("Segoe Fluent Icons", 12f, GraphicsUnit.Pixel);
            using var small = new Font("Segoe UI", 12f, GraphicsUnit.Pixel);
            int mid = H - Bar / 2;
            // Laid out from the clock inwards, mirrored on a right-to-left Windows.
            int Edge(int fromEdge, int width) => ar ? fromEdge : W - fromEdge - width;
            var clock = new Rectangle(Edge(12, 74), H - Bar + 5, 74, Bar - 10);
            TextRenderer.DrawText(g, "21:41\n2026/10/18", small, clock, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "", glyphs, new Rectangle(Edge(92, 22), mid - 11, 22, 22), ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            g.DrawImage(trayIcon, Edge(122, 20), mid - 10, 20, 20);
            TextRenderer.DrawText(g, "", glyphs, new Rectangle(Edge(150, 22), mid - 11, 22, 22), ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        void Caption(Graphics g, int step, string caption)
        {
            using var font = new Font("Segoe UI Semibold", 19f, GraphicsUnit.Pixel);
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | (ar ? TextFormatFlags.RightToLeft : 0);
            var size = TextRenderer.MeasureText(g, caption, font, Size.Empty, flags);
            var box = new Rectangle((W - size.Width) / 2 - 22, 22, size.Width + 44, size.Height + 18);
            Shadow(g, box, box.Height / 2);
            using (var path = Round(box, box.Height / 2))
            using (var brush = new SolidBrush(pill))
                g.FillPath(brush, path);
            TextRenderer.DrawText(g, caption, font, new Rectangle(box.X + 22, box.Y + 9, size.Width, size.Height), pillInk, flags);
            // Where in the story this frame is.
            int spacing = 14, width = (steps - 1) * spacing;
            for (int i = 0; i < steps; i++)
            {
                int index = ar ? steps - 1 - i : i;
                using var b = new SolidBrush(index == step ? dotOn : dot);
                int cx = (W - width) / 2 + i * spacing;
                int r = index == step ? 4 : 3;
                g.FillEllipse(b, cx - r, box.Bottom + 14 - r, r * 2, r * 2);
            }
        }

        static void Centered(Graphics g, string words, Font font, Color color, int y)
        {
            var size = TextRenderer.MeasureText(g, words, font, Size.Empty, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, words, font, new Point((W - size.Width) / 2, y - size.Height / 2), color, TextFormatFlags.NoPadding);
        }

        void Window(Graphics g, Bitmap window, int x, int y, int radius)
        {
            var rect = new Rectangle(x, y, window.Width, window.Height);
            Shadow(g, rect, radius);
            using var path = Round(rect, radius);
            using var texture = new TextureBrush(window) { WrapMode = WrapMode.Clamp };
            texture.TranslateTransform(x, y);
            g.FillPath(texture, path);
            using var edge = new Pen(dark ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0));
            g.DrawPath(edge, path);
        }

        // A note keeps its own see-through round ends and edge; only the shadow is added.
        void Note(Graphics g, Bitmap note, int x, int y)
        {
            Shadow(g, new Rectangle(x, y, note.Width, note.Height), Math.Min(note.Height / 2, 17));
            g.DrawImage(note, new Rectangle(x, y, note.Width, note.Height));
        }

        void Shadow(Graphics g, Rectangle rect, int radius)
        {
            for (int i = 6; i >= 1; i--)
            {
                var r = new Rectangle(rect.X - i, rect.Y - i + 4, rect.Width + 2 * i, rect.Height + 2 * i);
                using var path = Round(r, radius + i);
                using var b = new SolidBrush(Color.FromArgb(dark ? 22 : 10, 0, 0, 0));
                g.FillPath(b, path);
            }
        }

        static void Cursor(Graphics g, Point at)
        {
            Point[] arrow = { new(0, 0), new(0, 17), new(4, 13), new(7, 20), new(10, 19), new(7, 12), new(12, 12) };
            var points = arrow.Select(p => new Point(at.X + p.X, at.Y + p.Y)).ToArray();
            g.FillPolygon(Brushes.White, points);
            using var pen = new Pen(Color.Black, 1.2f) { LineJoin = LineJoin.Round };
            g.DrawPolygon(pen, points);
        }

        static GraphicsPath Round(Rectangle r, int radius)
        {
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    static readonly int[] Seconds = { 15, 14, 13, 12, 11, 10, 3, 2, 1 };

    // The countdown at each second the demo shows, and the note on coming back. A note is a
    // see-through window, which PrintWindow draws blank, so these are the pictures the note itself
    // puts on the screen.
    internal static (Dictionary<int, Bitmap> Counts, Bitmap Back) Notes()
    {
        long clock = 1_000_000;
        var area = new Rectangle(0, 0, 1920, 1040);
        using var toast = new Toast(headless: true)
        {
            Clock = () => clock,
            // The pointer is away from the note, so nothing holds it.
            Pointer = () => new Point(area.Width / 4, area.Height / 4),
            Area = _ => area,
        };
        toast.Use(new Config());
        var counts = new Dictionary<int, Bitmap>();
        toast.ShowCountdown(15, locking: true);
        foreach (int n in Seconds)
        {
            toast.SetSeconds(n);
            counts[n] = toast.Picture();
        }
        toast.EndCountdown();
        toast.ShowAway(TimeSpan.FromMinutes(42), playingAgain: false);
        // A little way into its time, as the notes sheet draws it.
        clock += 3000;
        toast.Step();
        return (counts, toast.Picture());
    }

    // The demo's notes are drawn in the one calm style of every note: the card's thin edge, the line
    // along the bottom in the accent, round ends; and the countdown's number changes from frame to
    // frame.
    internal static int Check()
    {
        int failures = 0;
        void Is(bool ok, string what)
        {
            Console.WriteLine((ok ? "ok   " : "FAIL ") + what);
            if (!ok) failures++;
        }
        foreach (string theme in new[] { "light", "dark" })
        {
            foreach (string lang in new[] { "en", "ar" })
            {
                Theme.UseForShots(theme == "dark");
                Lang.Use(lang);
                var t = Theme.Current;
                string tag = $"demo-{lang}-{theme}";
                var (counts, back) = Notes();
                foreach (var (name, picture) in counts.Select(c => ($"countdown at {c.Key}", c.Value)).Append(("back", back)))
                {
                    int lineX = lang == "ar" ? picture.Width - picture.Height / 2 - 2 : picture.Height / 2 + 2;
                    Is(picture.GetPixel(picture.Width / 2, 0).ToArgb() == t.Edge.ToArgb() && picture.GetPixel(0, 0).A == 0
                        && picture.GetPixel(lineX, picture.Height - 2).ToArgb() == t.Accent.ToArgb(),
                        $"{tag}: the {name} note is drawn, the card's edge, its line in the accent, with round ends");
                }
                Is(!Same(counts[15], counts[14]), $"{tag}: the countdown's number changes from frame to frame");
                foreach (var picture in counts.Values.Append(back)) picture.Dispose();
            }
        }
        Theme.UseForShots(false);
        Lang.Use("en");
        Console.WriteLine(failures == 0 ? "demo: all checks passed" : $"demo: {failures} checks failed");
        return failures;
    }

    static bool Same(Bitmap a, Bitmap b)
    {
        if (a.Size != b.Size) return false;
        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
            }
        }
        return true;
    }

    internal static void Pump(int ms)
    {
        var until = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < until)
        {
            Application.DoEvents();
            Thread.Sleep(15);
        }
    }

    // The window draws itself, so it may be covered or off the screen; cropped to what DWM shows.
    internal static Bitmap Grab(IntPtr handle)
    {
        GetWindowRect(handle, out var w);
        if (DwmGetWindowAttribute(handle, 9, out var r, Marshal.SizeOf<RECT>()) != 0) r = w;
        using var whole = new Bitmap(w.R - w.L, w.B - w.T, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(whole))
        {
            for (int i = 0; i < 2; i++)
            {
                IntPtr dc = g.GetHdc();
                PrintWindow(handle, dc, 2);
                g.ReleaseHdc(dc);
                Pump(100);
            }
        }
        bool mirrored = (GetWindowLong(handle, -20) & 0x400000) != 0;
        if (mirrored) whole.RotateFlip(RotateFlipType.RotateNoneFlipX);
        int left = mirrored ? w.R - r.R : r.L - w.L;
        var crop = new Bitmap(r.R - r.L, r.B - r.T, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(crop))
            g.DrawImage(whole, new Rectangle(0, 0, crop.Width, crop.Height), new Rectangle(left, r.T - w.T, crop.Width, crop.Height), GraphicsUnit.Pixel);
        // PrintWindow leaves the alpha channel empty on some windows; the windows are opaque.
        var data = crop.LockBits(new Rectangle(0, 0, crop.Width, crop.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var bytes = new byte[data.Stride * crop.Height];
        Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
        for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
        // A rounded button leaves its corners unpainted, and they come out pure black, a color the
        // app never draws. Each such pixel takes the average of its painted neighbors, a few times
        // over so a corner of several pixels fills in from the outside.
        int stride = data.Stride, width = crop.Width, height = crop.Height;
        bool Black(int at) => bytes[at] == 0 && bytes[at + 1] == 0 && bytes[at + 2] == 0;
        for (int pass = 0; pass < 4; pass++)
        {
            var next = (byte[])bytes.Clone();
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int at = y * stride + x * 4;
                    if (!Black(at)) continue;
                    int sb = 0, sg = 0, sr = 0, n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                            int near = ny * stride + nx * 4;
                            if (Black(near)) continue;
                            sb += bytes[near]; sg += bytes[near + 1]; sr += bytes[near + 2]; n++;
                        }
                    if (n == 0) continue;
                    next[at] = (byte)(sb / n); next[at + 1] = (byte)(sg / n); next[at + 2] = (byte)(sr / n);
                }
            bytes = next;
        }
        Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        crop.UnlockBits(data);
        return crop;
    }
}
