using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using SteppedOut;

// Draws the app's real windows off the screen and saves each as a PNG, for comparing against the
// design sketches and against earlier renders. The windows are built in this process; the tray
// app is never started, no tray icon is made, the lock shortcut is never registered, and a lock
// request throws.
//
//   dotnet run --project tools/shots -- <folder> [parts] [languages] [themes]
//
// parts: welcome (the default: its pages, states and checks), settings (the settings window), toast, demo,
// look (logo, ring and toggle sheets), kit (control sheets), locking, away, pages (settings tabs
// and dialogs), notes (corner notes), tray (the tray menu and icons), store (the five 1366 x 768 Store
// screenshots, named store-<scene>-<language>-<theme>.png), or all; several with commas. check runs the behaviour checks instead and sets the exit code;
// "check welcome" runs only the welcome window's.
// languages: en,ar by default. themes: light,dark by default.
// Pictures are named <scene>-<language>-<theme>.png.
static class Shots
{
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT r, int size);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

    const int ExStyle = -20;
    const int NoActivateStyle = 0x08000000;
    const int LayoutRtlStyle = 0x00400000;
    const uint RenderFullContent = 2;

    // Off-screen windows must never take the keyboard from whoever is using the PC.
    public static void NoActivate(Form f)
    {
        var h = f.Handle;
        SetWindowLong(h, ExStyle, GetWindowLong(h, ExStyle) | NoActivateStyle);
    }

    public static void Foreground(Form f, string what)
    {
        if (GetForegroundWindow() == f.Handle) Console.WriteLine($"WARNING: {what} became the foreground window");
    }

    static string outDir = "";
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    [STAThread]
    static int Main(string[] args)
    {
        // Before anything else, so nothing this process builds can lock the session.
        SessionLock.Blocked = true;
        // The clocks in the welcome's pictures, so a picture only differs from the one before it
        // when the window does.
        WelcomeForm.Clock = () => new DateTime(2026, 10, 20, 11, 58, 0);
        // A PC with a battery, so the pictures show the battery's choices on any PC. The sheets that
        // show a PC without one set it themselves.
        WelcomeForm.HasBattery = () => true;
        if (args.Length == 0)
        {
            Console.WriteLine("usage: dotnet run --project tools/shots -- <folder> [parts] [languages] [themes]");
            return 2;
        }
        outDir = Path.GetFullPath(args[0]);
        string only = args.Length > 1 ? args[1] : "welcome";
        var langs = (args.Length > 2 ? args[2] : "en,ar").Split(',');
        var themes = (args.Length > 3 ? args[3] : "light,dark").Split(',');
        Directory.CreateDirectory(outDir);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Log.Open(Path.Combine(outDir, "shots.log"), 1 << 20);
        // Every window that places itself from the work area, such as the corner notes, lands just
        // as far off the screen instead, at the same size.
        WorkArea.Of = screen => OffScreen(screen.WorkingArea);
        if (only == "check")
        {
            // check welcome runs only the welcome window's checks, which are quick.
            return Check.Run(welcomeOnly: args.Length > 2 && args[2] == "welcome");
        }
        var parts = only.Split(',');
        bool want(string p) => only == "all" || parts.Contains(p);
        foreach (var theme in themes)
        {
            Theme.UseForShots(theme == "dark");
            foreach (var lang in langs)
            {
                Lang.Use(lang);
                string tag = $"{lang}-{theme}";
                if (want("welcome")) WelcomeSheet.Run(outDir, lang, theme);
                if (want("settings")) SettingsSheet.Run(outDir, lang, theme);
                if (want("toast")) ToastShots(tag);
                if (want("demo")) Demo.Run(outDir, lang, theme);
                if (want("look")) LookSheet.Run(outDir, lang, theme);
                if (want("kit")) KitSheet.Run(outDir, lang, theme);
                if (want("locking")) LockingSheet.Run(outDir, lang, theme);
                if (want("away")) AwaySheet.Run(outDir, lang, theme);
                if (want("pages")) PagesSheet.Run(outDir, lang, theme);
                if (want("notes")) NotesSheet.Run(outDir, lang, theme);
                if (want("tray")) TraySheet.Run(outDir, lang, theme);
                if (want("store")) StoreShots.Run(outDir, lang, theme);
            }
        }
        Console.WriteLine("done");
        return 0;
    }

    // Left of every screen, with room for the widest window, so nothing is ever drawn where the
    // person using the PC can see it. PrintWindow has each window draw itself there.
    public static Point Spot()
    {
        var all = SystemInformation.VirtualScreen;
        return new Point(all.Left - 6000, all.Top + 20);
    }

    static Rectangle OffScreen(Rectangle area) => new(Spot().X - 200, area.Top, area.Width, area.Height);

    // Shown without the taskbar and without taking the keyboard, off the screen, and kept there:
    // a window that centers or moves itself on the screen is put straight back.
    public static void Place(Form f)
    {
        f.StartPosition = FormStartPosition.Manual;
        f.ShowInTaskbar = false;
        f.TopMost = false;
        f.Location = Spot();
        NoActivate(f);
        // A window that changes direction makes its handle again; it stays one that never activates.
        f.HandleCreated += (_, _) => NoActivate(f);
        bool moving = false;
        f.LocationChanged += (_, _) =>
        {
            if (moving || !SystemInformation.VirtualScreen.IntersectsWith(f.Bounds)) return;
            moving = true;
            f.Location = Spot();
            moving = false;
        };
    }

    // A note is a see-through window, which PrintWindow draws blank, so each is saved as the
    // picture the note itself puts on the screen: at 100%, and at 200% with -200 on the name.
    static void ToastShots(string tag)
    {
        foreach (int dpi in new[] { 96, 192 })
        {
            string at = dpi == 96 ? "" : "-200";
            using var t = new Toast(headless: true) { Dpi = _ => dpi };
            t.Use(new Config());
            t.ShowCountdown(15, locking: true);
            t.SetSeconds(9);
            SaveNote(t, $"toast-countdown-{tag}{at}");
            t.EndCountdown();
            t.ShowAway(TimeSpan.FromMinutes(42), playingAgain: false);
            SaveNote(t, $"toast-back-{tag}{at}");
            t.ShowReminder(new Reminder(ReminderKind.Break, 65 * 60));
            SaveNote(t, $"toast-break-{tag}{at}");
        }
    }

    static void SaveNote(Toast t, string name)
    {
        using var picture = t.Picture();
        picture.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
        Console.WriteLine($"{name}: {picture.Width}x{picture.Height}");
    }

    static void Pump(int ms)
    {
        var until = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < until)
        {
            Application.DoEvents();
            Thread.Sleep(15);
        }
    }

    // The window draws itself with PrintWindow, so this works with it off the screen, covered, or
    // with the screen locked. Cropped to what DWM shows of it, without the invisible resize border.
    static void Capture(IntPtr handle, string name)
    {
        Pump(500);
        GetWindowRect(handle, out var w);
        if (DwmGetWindowAttribute(handle, 9, out var r, Marshal.SizeOf<RECT>()) != 0) r = w;
        var crop = new Rectangle(r.L, r.T, r.R - r.L, r.B - r.T);
        using var bmp = new Bitmap(crop.Width, crop.Height);
        using var whole = new Bitmap(w.R - w.L, w.B - w.T);
        using (var g = Graphics.FromImage(whole))
        {
            // Twice: the first pass can catch a window that has not finished painting.
            for (int i = 0; i < 2; i++)
            {
                IntPtr dc = g.GetHdc();
                PrintWindow(handle, dc, RenderFullContent);
                g.ReleaseHdc(dc);
                if (i == 0) Pump(300);
            }
        }
        // A right-to-left window draws itself mirrored into a plain bitmap; flipped back here.
        bool mirrored = (GetWindowLong(handle, ExStyle) & LayoutRtlStyle) != 0;
        if (mirrored) whole.RotateFlip(RotateFlipType.RotateNoneFlipX);
        int left = mirrored ? w.R - r.R : r.L - w.L;
        using (var g2 = Graphics.FromImage(bmp))
            g2.DrawImage(whole, new Rectangle(0, 0, crop.Width, crop.Height), new Rectangle(left, r.T - w.T, crop.Width, crop.Height), GraphicsUnit.Pixel);
        bmp.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
        Console.WriteLine($"{name}: {crop.Width}x{crop.Height}");
    }
}
