using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Reflection;
using SteppedOut;

// The Microsoft Store screenshots: 1366 x 768 PNGs, the app's real windows drawn by PrintWindow and
// placed on a plain desktop with a taskbar, as they open on a screen that size (the windows take
// their height from a 1366 x 720 work area). Five scenes, in the order of the screenshot captions
// in store/listing: settings, countdown, tray menu, away and back, and the Why window. A scene is
// store-<scene>-<language>-<theme>.png. Nothing is shown on the screen: the windows are off it, as
// in every other part.
static class StoreShots
{
    const int W = 1366, H = 768, Bar = 48;

    public static void Run(string outDir, string lang, string theme)
    {
        bool dark = theme == "dark";
        bool ar = lang == "ar";
        string tag = $"{lang}-{theme}";
        var before = WorkArea.Of;
        // A laptop's screen at 100%: the taskbar takes its bottom 48 px.
        WorkArea.Of = _ => new Rectangle(Shots.Spot().X - 200, 0, W, H - Bar);
        try
        {
            var look = new Look(dark, ar);
            using var on = TrayIcons.Art(TrayIconState.On, 20, dark);
            using var counting = TrayIcons.Art(TrayIconState.CountingDown, 20, dark);

            // 1. Settings: the window as it opens.
            using (var window = OpenWindow(Settings(), Sim(), null, null))
            {
                using var shot = look.Desktop(on);
                look.Centered(shot, window);
                Save(shot, outDir, "settings", tag);
            }

            // 2. Countdown: the note in the corner, with nothing else on the desktop.
            using (var note = Countdown())
            {
                using var shot = look.Desktop(counting);
                look.Corner(shot, note, noteShadow: true);
                Save(shot, outDir, "countdown", tag);
            }

            // 3. Tray menu: the menu and its pause list above the icon.
            var (menu, list, listAt) = TraySheet.StoreMenu();
            using (menu)
            using (list)
            {
                using var shot = look.Desktop(on);
                look.TrayMenu(shot, menu, list, listAt);
                Save(shot, outDir, "tray", tag);
            }

            // 4. Away and back: the Away and back tab, scrolled so the rows above it that matter
            // (the music and video setting) show too.
            using (var window = OpenWindow(Away(), Sim(), form =>
            {
                Choose(form.Tabs, 1);
                Demo.Pump(300);
                form.Scroller.AutoScrollPosition = new Point(0, form.Tabs.Top - form.LogicalToDeviceUnits(150));
            }, null))
            {
                using var shot = look.Desktop(on);
                look.Centered(shot, window);
                Save(shot, outDir, "away", tag);
            }

            // 5. Why isn't the PC locking?: the window from the status box, over the settings.
            var lines = WhyLines();
            var never = Sim(new ActiveEffect(Effect.Extra(60), EffectSource.MusicOrVideo), new ActiveEffect(Effect.Never, EffectSource.ForApp("Notepad")));
            using (var window = OpenWindow(Settings(), never, null, lines))
            using (var why = Why(never, lines))
            {
                using var shot = look.Desktop(on);
                var at = look.Centered(shot, window);
                look.Over(shot, why, at);
                Save(shot, outDir, "why", tag);
            }
        }
        finally
        {
            WorkArea.Of = before;
        }
    }

    // What the tab strip does when a tab is clicked.
    static void Choose(PartGroup group, int part) =>
        typeof(PartGroup).GetMethod("Choose", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(group, [group.Parts[part]]);

    static void Save(Bitmap shot, string outDir, string scene, string tag)
    {
        string name = $"store-{scene}-{tag}";
        shot.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
        Console.WriteLine($"{name}: {shot.Width}x{shot.Height}");
    }

    // The settings the README's picture shows.
    static Config Settings() => new()
    {
        IdleMinutes = 30,
        MusicVideo = Effect.Extra(60),
        OnCall = Effect.Never,
        FullScreen = Effect.Extra(120),
        GraceSeconds = 30,
        MediaOnLock = MediaOnLock.PauseUntilBack,
        OnBattery = Effect.Sooner(50),
    };

    // The same with the away actions that the caption names turned on.
    static Config Away()
    {
        var c = Settings();
        c.MuteWhenAway = true;
        c.ScreensOffWhenAway = true;
        c.SoundWhenAway = ActionSound.SteppedOut;
        c.ShowTimeAway = true;
        c.SoundWhenBack = ActionSound.Drift;
        return c;
    }

    // Two minutes idle, 3 locks and 1 hr 20 min away today.
    static LockStatus Sim(params ActiveEffect[] effects)
    {
        var time = LockTime.Compute(30, effects);
        return new LockStatus
        {
            Kind = time.NeverLocks ? LockState.Never : LockState.On,
            Time = time,
            IdleSeconds = 120,
            SecondsLeft = time.TotalSeconds is decimal total ? (double)total - 120 : 0,
            Today = new TodayCounts(3, TimeSpan.FromMinutes(80)),
        };
    }

    static List<WhyLine> WhyLines() =>
    [
        new(Signal.Sound, Effect.Extra(60), "Media Player"),
        new(Signal.Display, Effect.Extra(60), "Media Player"),
        new(Signal.App, Effect.Never, "Notepad"),
        new(Signal.Battery, Effect.Sooner(50)),
        new(Signal.Call, Effect.None, "", Counts: false),
    ];

    // The settings window with its own status, drawn off the screen.
    static Bitmap OpenWindow(Config config, LockStatus status, Action<SettingsForm>? after, IReadOnlyList<WhyLine>? lines)
    {
        var host = new Demo.Host(status) { Lines = lines ?? [] };
        var form = new SettingsForm(config, host);
        Shots.Place(form);
        form.Show();
        Demo.Pump(800);
        after?.Invoke(form);
        Demo.Pump(500);
        Shots.Foreground(form, "settings");
        var picture = Demo.Grab(form.Handle);
        form.Close();
        form.Dispose();
        return picture;
    }

    static Bitmap Why(LockStatus status, IReadOnlyList<WhyLine> lines)
    {
        var host = new Demo.Host(status) { Lines = lines };
        var dialog = new WhyDialog(host, () => true, Lang.Current);
        Shots.Place(dialog);
        dialog.Show();
        Demo.Pump(600);
        Shots.Foreground(dialog, "why");
        var picture = Demo.Grab(dialog.Handle);
        dialog.Close();
        dialog.Dispose();
        return picture;
    }

    // The countdown, nine seconds from the lock: a note is a see-through window, which PrintWindow
    // draws blank, so this is the picture the note itself puts on the screen.
    static Bitmap Countdown()
    {
        using var toast = new Toast(headless: true);
        toast.Use(new Config());
        toast.ShowCountdown(15, locking: true);
        toast.SetSeconds(9);
        return toast.Picture();
    }

    // The desktop and what is placed on it.
    sealed class Look
    {
        readonly bool dark, ar;
        readonly Color top, bottom, bar, barLine, ink;

        public Look(bool dark, bool ar)
        {
            this.dark = dark;
            this.ar = ar;
            top = dark ? Color.FromArgb(0x1e, 0x2a, 0x37) : Color.FromArgb(0xdd, 0xe7, 0xf1);
            bottom = dark ? Color.FromArgb(0x15, 0x1d, 0x27) : Color.FromArgb(0xc8, 0xd6, 0xe4);
            bar = dark ? Color.FromArgb(0x1c, 0x1c, 0x1c) : Color.FromArgb(0xee, 0xf1, 0xf5);
            barLine = dark ? Color.FromArgb(0x30, 0x30, 0x30) : Color.FromArgb(0xd0, 0xd7, 0xde);
            ink = dark ? Color.White : Color.FromArgb(0x1b, 0x1b, 0x1b);
        }

        public Bitmap Desktop(Bitmap trayIcon)
        {
            var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            // The brush is a pixel larger on every side, so no edge of the picture is a blend.
            using (var sky = new LinearGradientBrush(new Rectangle(-1, -1, W + 2, H + 2), top, bottom, 90f))
                g.FillRectangle(sky, -1, -1, W + 2, H + 2);
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
            return bmp;
        }

        // A window in the middle of the work area, with its shadow; the place it took.
        public Rectangle Centered(Bitmap shot, Bitmap window)
        {
            var at = new Rectangle((W - window.Width) / 2, Math.Max(0, (H - Bar - window.Height) / 2), window.Width, window.Height);
            using var g = Graphics.FromImage(shot);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Window(g, window, at.X, at.Y, 8);
            return at;
        }

        // A second window over the first: in the middle of it across, and a little below its top.
        public void Over(Bitmap shot, Bitmap window, Rectangle under)
        {
            using var g = Graphics.FromImage(shot);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Window(g, window, under.X + (under.Width - window.Width) / 2, under.Y + 205, 8);
        }

        // In the corner the clock is in, 16 px from the taskbar and the edge, as a note or the tray's
        // menu stands there.
        public void Corner(Bitmap shot, Bitmap piece, bool noteShadow)
        {
            int margin = noteShadow ? 16 : 12;
            int x = ar ? margin : W - piece.Width - margin;
            int y = H - Bar - piece.Height - margin;
            using var g = Graphics.FromImage(shot);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (noteShadow)
            {
                // A note keeps its own see-through round ends and edge; only the shadow is added.
                Shadow(g, new Rectangle(x, y, piece.Width, piece.Height), Math.Min(piece.Height / 2, 17));
                g.DrawImage(piece, new Rectangle(x, y, piece.Width, piece.Height));
            }
            else
            {
                Window(g, piece, x, y, 8);
            }
        }

        // The tray menu and its pause list, each with its own shadow, in the corner the clock is in
        // (listAt is where the list stands against the menu's top left).
        public void TrayMenu(Bitmap shot, Bitmap menu, Bitmap list, Point listAt)
        {
            var all = Rectangle.Union(new Rectangle(Point.Empty, menu.Size), new Rectangle(listAt, list.Size));
            int margin = 12;
            int x = ar ? margin - all.X : W - margin - all.Right;
            int y = H - Bar - margin - all.Bottom;
            using var g = Graphics.FromImage(shot);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Window(g, menu, x, y, 8);
            Window(g, list, x + listAt.X, y + listAt.Y, 8);
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
}
