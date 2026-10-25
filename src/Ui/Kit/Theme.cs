using System.Reflection;
using Microsoft.Win32;

namespace SteppedOut;

// The colors and fonts every window shares. The settings choose Light, Dark or the same as Windows
// (Settings > Personalization > Colors > "Choose your app mode") and a text size. The accent is
// always the green of the logo.
// High contrast uses the system's own colors instead, whatever was chosen, so nothing here fights it.
sealed class Theme
{
    public required bool IsDark { get; init; }

    // Made of the system's high contrast colors.
    public bool IsHighContrast { get; init; }

    public required Color Window { get; init; }
    public required Color Card { get; init; }
    public required Color Field { get; init; }

    // A button or list under the pointer. Not AccentSoft, which only means chosen or on.
    public required Color FieldHover { get; init; }
    public required Color Line { get; init; }

    // The edge of something you can click or type in: dark enough to see without its text, at least
    // 3:1 against the field, the card and the window. A box you type in keeps this edge.
    public required Color FieldLine { get; init; }
    public required Color Text { get; init; }
    public required Color Subtle { get; init; }
    public required Color Accent { get; init; }
    public required Color OnAccent { get; init; }
    public required Color AccentSoft { get; init; }
    public required Color Warning { get; init; }
    public required Color WarningSoft { get; init; }

    // The settings window's status box: its fill, its edge (also the ring's track) and the band inside it.
    public required Color Hero { get; init; }
    public required Color HeroEdge { get; init; }
    public required Color HeroIn { get; init; }

    // A chosen tab, a hovered row, the circle behind a row's icon.
    public required Color Soft { get; init; }

    // A switch: its track when off, its knob when on and when off. The off track stands out 3:1 from
    // the card and the window, and the knob 3:1 from the track, so the switch and its state show
    // without color.
    public required Color Off { get; init; }
    public required Color Knob { get; init; }
    public required Color KnobOff { get; init; }

    // The edge of a card or a panel.
    public required Color Edge { get; init; }

    // The edge of a button, a list's face, a few-choices box, a chip and a menu: soft, since the
    // words or the arrow inside already say what it is.
    public required Color ControlEdge { get; init; }

    // ---- The palettes ----

    // The accent and the colors tinted from it, for one palette.
    internal readonly record struct AccentSet(Color Accent, Color OnAccent, Color AccentSoft, Color Hero, Color HeroEdge, Color HeroIn, Color Soft, Color Knob);

    static Color Rgb(int rgb) => Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    // The green of the logo and the approved drawing.
    static readonly AccentSet GreenLight = new(Rgb(0x0F6B54), Color.White, Rgb(0xE6F1ED), Rgb(0xE7F2EE), Rgb(0xB5D6CA), Color.White, Rgb(0xEEF5F2), Color.White);
    static readonly AccentSet GreenDark = new(Rgb(0x52C7A5), Rgb(0x08251D), Rgb(0x1E3730), Rgb(0x1E3A33), Rgb(0x2F5A4E), Rgb(0x192F29), Rgb(0x23423A), Rgb(0x0B2A22));

    static Theme Build(bool dark, AccentSet a) => dark
        ? new()
        {
            IsDark = true,
            Window = Rgb(0x202020),
            Card = Rgb(0x2B2B2B),
            Field = Rgb(0x353535),
            FieldHover = Rgb(0x3C3C3C),
            Line = Rgb(0x404040),
            FieldLine = Rgb(0x8C8C8C),
            Text = Color.White,
            Subtle = Rgb(0xC9C9C9),
            Accent = a.Accent,
            OnAccent = a.OnAccent,
            AccentSoft = a.AccentSoft,
            Warning = Rgb(0xF2B33D),
            WarningSoft = Rgb(0x3A2F1B),
            Hero = a.Hero,
            HeroEdge = a.HeroEdge,
            HeroIn = a.HeroIn,
            Soft = a.Soft,
            // The drawing's 4A4A4A stands out only 1.6:1 from the card; this reaches 3:1, and the knob
            // is a little lighter than the drawing's CFCFCF to stay 3:1 from it.
            Off = Rgb(0x747474),
            Knob = a.Knob,
            KnobOff = Rgb(0xD4D4D4),
            Edge = Rgb(0x383838),
            ControlEdge = Rgb(0x5A5A5A),
        }
        : new()
        {
            IsDark = false,
            Window = Rgb(0xF3F3F3),
            Card = Color.White,
            Field = Color.White,
            FieldHover = Rgb(0xF0F0F0),
            Line = Rgb(0xDCDCDC),
            FieldLine = Rgb(0x858585),
            Text = Rgb(0x1A1A1A),
            Subtle = Rgb(0x5B5B5B),
            Accent = a.Accent,
            OnAccent = a.OnAccent,
            AccentSoft = a.AccentSoft,
            Warning = Rgb(0xA35F00),
            WarningSoft = Rgb(0xFBEEDB),
            Hero = a.Hero,
            HeroEdge = a.HeroEdge,
            HeroIn = a.HeroIn,
            Soft = a.Soft,
            // The drawing's BDBDBD stands out only 1.9:1 from the card and from its white knob; this
            // reaches 3:1 against both, and against the window.
            Off = Rgb(0x8C8C8C),
            Knob = a.Knob,
            KnobOff = Color.White,
            Edge = Rgb(0xD4D4D4),
            ControlEdge = Rgb(0xC4C4C4),
        };

    static readonly Theme LightTheme = Build(false, GreenLight);
    static readonly Theme DarkTheme = Build(true, GreenDark);

    internal static Theme HighContrast() =>
        HighContrastOf(SystemColors.Window, SystemColors.WindowText, SystemColors.Highlight, SystemColors.HighlightText);

    // The same from four colors, so a test or a render can use a contrast theme's colors while the PC
    // is not in one. A name of its own, since a test finds HighContrast() by name.
    internal static Theme HighContrastOf(Color window, Color text, Color highlight, Color highlightText) => new()
    {
        IsDark = window.GetBrightness() < 0.5f,
        IsHighContrast = true,
        Window = window,
        Card = window,
        Field = window,
        FieldHover = window,
        Line = text,
        FieldLine = text,
        Text = text,
        Subtle = text,
        Accent = highlight,
        OnAccent = highlightText,
        AccentSoft = window,
        Warning = highlight,
        WarningSoft = window,
        // Fills are the window's own color and every edge is its text color. A chosen tab shows by its
        // highlight bar, not a tint.
        Hero = window,
        HeroEdge = text,
        HeroIn = window,
        Soft = window,
        // A switch that is on is the highlight with a knob in the highlight's text color; one that is
        // off is a track in the text color with a knob in the window's.
        Off = text,
        Knob = highlightText,
        KnobOff = window,
        Edge = text,
        ControlEdge = text,
    };

    // Either palette by name, also for what is drawn in the taskbar's colors rather than the app's.
    public static Theme Light => LightTheme;

    public static Theme Dark => DarkTheme;

    public static Theme For(bool dark) => dark ? DarkTheme : LightTheme;

    // ---- What was chosen, and what Windows says ----

    // Windows' "Make text bigger" setting, in percent, read when the text size follows Windows. The
    // tests set it, so they run the same whatever the PC they run on is set to.
    internal static Func<int> WindowsTextScale { get; set; } = ReadTextScaleFactor;

    static ThemeChoice choice = ThemeChoice.System;
    static TextSize textSize = TextSize.System;

    public static Theme Current { get; private set; } = Read();

    // How much bigger than asked for every font is: 0.9, 1 or 1.2 when chosen, or Windows' text size.
    public static float TextScale { get; private set; } = ReadScale();

    // The settings' choices. True when the colors or the text size are different now, so open windows
    // need building again.
    public static bool Choose(ThemeChoice theme, TextSize size)
    {
        choice = theme;
        textSize = size;
        return Refresh();
    }

    public static bool Choose(Config config) => Choose(config.Theme, config.TextSize);

    // Called when Windows reports a settings change. A fixed choice stays as chosen; only what follows
    // Windows (the app mode, the text size, high contrast) can change. True when anything
    // is different now.
    public static bool Refresh()
    {
        var fresh = Read();
        float scale = ReadScale();
        if (fresh.SameAs(Current) && scale == TextScale) return false;
        Current = fresh;
        TextScale = scale;
        return true;
    }

    // The render tool draws the windows in either palette and text size, whatever Windows is set
    // to. The first form draws text at the size asked for.
    internal static void UseForShots(bool dark) => UseForShots(dark, 1f);

    internal static void UseForShots(bool dark, float textScale)
    {
        Current = For(dark);
        TextScale = textScale;
    }

    static Theme Read()
    {
        if (SystemInformation.HighContrast) return HighContrast();
        return For(UsesDark(choice, ReadPersonalize("AppsUseLightTheme")));
    }

    // A fixed choice beats Windows' app mode.
    internal static bool UsesDark(ThemeChoice theme, int appsUseLightTheme) => theme switch
    {
        ThemeChoice.Light => false,
        ThemeChoice.Dark => true,
        _ => appsUseLightTheme == 0,
    };

    // Small, Medium and Large are 90%, 100% and 120%. System follows Settings > Accessibility > Text
    // size, which Windows keeps as a percent from 100 to 225.
    internal static float ScaleFor(TextSize size, int textScaleFactor) => size switch
    {
        TextSize.Small => 0.9f,
        TextSize.Medium => 1f,
        TextSize.Large => 1.2f,
        _ => Math.Clamp(textScaleFactor, 100, 225) / 100f,
    };

    static float ReadScale() => ScaleFor(textSize, textSize == TextSize.System ? WindowsTextScale() : 100);

    static int ReadTextScaleFactor()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility");
            return key?.GetValue("TextScaleFactor") is int value ? value : 100;
        }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return 100;
        }
    }

    static readonly PropertyInfo[] ColorProperties =
        typeof(Theme).GetProperties().Where(p => p.PropertyType == typeof(Color)).ToArray();

    // Every color the same, as numbers, so high contrast colors compare by what they are now.
    internal bool SameAs(Theme other) =>
        IsDark == other.IsDark && IsHighContrast == other.IsHighContrast
        && ColorProperties.All(p => ((Color)p.GetValue(this)!).ToArgb() == ((Color)p.GetValue(other)!).ToArgb());

    // The taskbar has a setting of its own, and the welcome window's picture of it follows that one.
    public static bool TaskbarIsDark() => SystemInformation.HighContrast
        ? SystemColors.Window.GetBrightness() < 0.5f
        : TaskbarIsDark(Environment.OSVersion.Version.Build, ReadPersonalize("SystemUsesLightTheme"));

    // Windows 10 before version 1903 (build 18362) has no light taskbar, whatever the registry says.
    internal static bool TaskbarIsDark(int build, int systemUsesLightTheme) => build < 18362 || systemUsesLightTheme == 0;

    static int ReadPersonalize(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue(name) is int value ? value : 1;
        }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return 1;
        }
    }

    // ---- Contrast ----

    // The WCAG contrast ratio, from 1 to 21: words want 4.5, edges and shapes 3.
    public static double Contrast(Color a, Color b)
    {
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    public static double Luminance(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    static double Linear(int channel)
    {
        double v = channel / 255.0;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    // ---- Fonts ----

    // Windows 11's Segoe UI Variable where it exists. Arabic and Windows 10 get Segoe UI, which has
    // the Arabic letters the variable font lacks.
    static readonly bool HasVariableFont = Installed("Segoe UI Variable Text");
    static readonly string IconFamily = Installed("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    static readonly Dictionary<(string, float, FontStyle), Font> Fonts = new();

    static string BodyFamily => Lang.Current.RightToLeft || !HasVariableFont ? "Segoe UI" : "Segoe UI Variable Text";

    public static Font Body(float points = 10.5f) => Cached(BodyFamily, points, FontStyle.Regular, CurrentScale);

    public static Font Strong(float points = 10.5f) => Cached("Segoe UI Semibold", points, FontStyle.Regular, CurrentScale);

    public static Font Icons(float points = 12f) => Cached(IconFamily, points, FontStyle.Regular, CurrentScale);

    // A window whose text keeps one size, whatever the text size: the welcome, which had no text
    // size before 1.0 and stays as it was (I17). The screen's scaling still applies.
    internal interface IFixedTextSize
    {
    }

    [ThreadStatic] static bool buildingFixed;

    static float CurrentScale => buildingFixed ? 1f : TextScale;

    // Every font asked for on this thread until the result is disposed is at scale 1: for building
    // a window with a fixed text size, before its controls are in it.
    internal static IDisposable BuildingFixedTextSize()
    {
        bool before = buildingFixed;
        buildingFixed = true;
        return new Restore(() => buildingFixed = before);
    }

    sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    // The text size for what a control draws: 1 inside a window with a fixed text size, otherwise
    // the one in use.
    internal static float TextScaleFor(Control? control)
    {
        if (buildingFixed) return 1f;
        for (var c = control; c is not null; c = c.Parent)
        {
            if (c is IFixedTextSize) return 1f;
        }
        return TextScale;
    }

    // Fonts for what a control draws by hand, sized for its monitor and its window's text size.
    public static Font Body(float points, Control on) => Cached(BodyFamily, ForDpi(points, on.DeviceDpi), FontStyle.Regular, TextScaleFor(on));

    public static Font Strong(float points, Control on) => Cached("Segoe UI Semibold", ForDpi(points, on.DeviceDpi), FontStyle.Regular, TextScaleFor(on));

    public static Font Icons(float points, Control on) => Cached(IconFamily, ForDpi(points, on.DeviceDpi), FontStyle.Regular, TextScaleFor(on));

    // TextRenderer turns a point size into pixels at the system scaling (the main monitor's at
    // sign-in), whatever monitor the text is on. Labels get a rescaled font from WinForms; text drawn
    // by hand on a monitor scaled differently gets a point size that makes up for it here. On the
    // system scaling it is the same font as above.
    public static readonly int SystemDpi = ReadSystemDpi();

    public static Font Body(float points, int dpi) => Body(ForDpi(points, dpi));

    public static Font Strong(float points, int dpi) => Strong(ForDpi(points, dpi));

    public static Font Icons(float points, int dpi) => Icons(ForDpi(points, dpi));

    static float ForDpi(float points, int dpi) => dpi == SystemDpi ? points : points * dpi / SystemDpi;

    static int ReadSystemDpi()
    {
        using var screen = Graphics.FromHwnd(IntPtr.Zero);
        return (int)Math.Round(screen.DpiY);
    }

    // Fonts are shared and kept for the life of the tool: a handful of sizes, never one per window.
    // The text size is applied here, so every font asked for comes out that much bigger, on every
    // monitor, except in a window with a fixed text size. Not made for several threads at once.
    static Font Cached(string family, float points, FontStyle style, float scale)
    {
        var key = (family, points * scale, style);
        if (!Fonts.TryGetValue(key, out var font)) Fonts[key] = font = new Font(family, key.Item2, style);
        return font;
    }

    static readonly Dictionary<(Font, bool), (int Depth, int Line)> Marks = new();

    // How far the marks Arabic sets under its letters, such as the two under the last letter of
    // "seconds" or "running", reach below a line of Arabic words as Windows measures it, in pixels.
    // A line only as tall as Windows says cuts them off.
    internal static int MarksBelow(Font font)
    {
        var (depth, line) = LowestMarks(font, arabic: true);
        return Math.Max(0, depth - line);
    }

    // How far down from the top of a line its lowest marks reach: the tails of letters such as g
    // and p, and in Arabic the marks under the letters too. Also the line's height as Windows
    // measures such words. A line as tall as the font's height can be shorter than either at some
    // sizes. Neither keeps in step with the font's size or the scaling, so they are found once for
    // each font by drawing such words.
    internal static (int Depth, int Line) LowestMarks(Font font, bool arabic)
    {
        lock (Marks)
        {
            if (Marks.TryGetValue((font, arabic), out var known)) return known;
            string words = arabic ? "ثوانٍ جارٍ gjpqy" : "gjpqy";
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix
                | TextFormatFlags.Top | TextFormatFlags.Left | TextFormatFlags.NoClipping | (arabic ? TextFormatFlags.RightToLeft : 0);
            var size = TextRenderer.MeasureText(words, font, Size.Empty, flags);
            int lowest = 0;
            using (var picture = new Bitmap(size.Width + 8, size.Height * 2))
            {
                using (var g = Graphics.FromImage(picture))
                {
                    g.Clear(Color.White);
                    TextRenderer.DrawText(g, words, font, new Rectangle(4, 0, size.Width, picture.Height), Color.Black, flags);
                }
                for (int y = picture.Height - 1; y >= size.Height && lowest == 0; y--)
                {
                    for (int x = 0; x < picture.Width; x++)
                    {
                        if (picture.GetPixel(x, y).R < 192)
                        {
                            lowest = y + 1;
                            break;
                        }
                    }
                }
            }
            var marks = (Math.Max(lowest, size.Height), size.Height);
            Marks[(font, arabic)] = marks;
            return marks;
        }
    }

    static bool Installed(string family)
    {
        using var font = new Font(family, 10f);
        return string.Equals(font.Name, family, StringComparison.OrdinalIgnoreCase);
    }

    // Window background, text color, and a dark title bar to match in dark mode.
    public void Dress(Form form)
    {
        form.BackColor = Window;
        form.ForeColor = Text;
        if (form.IsHandleCreated) Native.UseDarkTitleBar(form.Handle, IsDark);
        else form.HandleCreated += (_, _) => Native.UseDarkTitleBar(form.Handle, IsDark);
    }

    // Dark scroll bars on a scrolling page in dark mode. High contrast keeps the system's own.
    public void DressScrollBars(Control page)
    {
        if (!IsDark || SystemInformation.HighContrast) return;
        if (page.IsHandleCreated) Native.UseDarkScrollBars(page.Handle);
        else page.HandleCreated += (_, _) => Native.UseDarkScrollBars(page.Handle);
    }
}
