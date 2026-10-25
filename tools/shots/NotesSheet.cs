using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using SteppedOut;

// Sheets of the corner notes, drawn the way the note window shows them: each pill (the countdown,
// Try it, time away with and without "music or video is playing again", a break and an eye break
// with and without Snooze, the notes about the settings file, including the one that says unsaved
// changes were replaced, and the one that says locking might not work, which stays until it is taken
// back; their detail runs to the lines under the title) and each place (bottom right, top right, bottom left, center) on a
// pretend work area with its taskbar. Nothing is shown on the screen: the notes are made headless
// and drawn into pictures. Pictures are named notes-<what>-<language>-<theme>.png (with -200 for a
// monitor at 200%) and the checks go to notes-check.txt. With NOTES_REF set to a folder of the
// sketch's pills (pill-<what>-<language>-<theme>.png), compare/ gets each pill beside the sketch's.
static class NotesSheet
{
    static readonly Rectangle Area = new(0, 0, 960, 520);
    const int Taskbar = 40;
    const int Pad = 16;

    static string outDir = "";
    static bool pictures;
    static bool firstRun = true;
    static int failures;
    static TextWriter? report;

    public static void Run(string outDir, string lang, string theme)
    {
        NotesSheet.outDir = outDir;
        pictures = true;
        string file = Path.Combine(outDir, "notes-check.txt");
        if (firstRun)
        {
            File.Delete(file);
            firstRun = false;
        }
        using (report = File.AppendText(file))
        {
            Each($"{lang}-{theme}");
        }
        report = null;
        Console.WriteLine(failures == 0 ? "notes: all checks passed" : $"notes: {failures} checks failed");
    }

    // The same checks without pictures, for the window checks: both languages, light and dark.
    // Returns how many failed, and leaves the light palette in use.
    public static int Check()
    {
        pictures = false;
        failures = 0;
        foreach (var lang in new[] { "en", "ar" })
        {
            foreach (var dark in new[] { false, true })
            {
                Lang.Use(lang);
                Theme.UseForShots(dark);
                Each($"{lang}-{(dark ? "dark" : "light")}");
            }
        }
        Theme.UseForShots(false);
        Console.WriteLine(failures == 0 ? "notes: all checks passed" : $"notes: {failures} checks failed");
        return failures;
    }

    static void Is(bool ok, string what)
    {
        string line = (ok ? "ok   " : "FAIL ") + what;
        if (report is not null) report.WriteLine(line);
        else if (!ok) Console.WriteLine(line);
        if (!ok) failures++;
    }

    // The scenes, each a note made and set up the way the app does it.
    static readonly (string Name, Action<Toast> Show)[] Scenes =
    [
        ("countdown", t => t.ShowCountdown(15, locking: true)),
        ("countdown-9", t => { t.ShowCountdown(15, locking: true); t.SetSeconds(9); }),
        ("countdown-away", t => t.ShowCountdown(15, locking: false)),
        ("tryit", t => t.ShowPreview()),
        ("away", t => t.ShowAway(TimeSpan.FromMinutes(42), playingAgain: false)),
        ("away-playing", t => t.ShowAway(TimeSpan.FromMinutes(42), playingAgain: true)),
        ("break", t => t.ShowReminder(new Reminder(ReminderKind.Break, 3600))),
        ("break-snooze", t => t.ShowReminder(new Reminder(ReminderKind.Break, 3600))),
        ("eye", t => t.ShowReminder(new Reminder(ReminderKind.EyeBreak, 1200))),
        ("eye-snooze", t => t.ShowReminder(new Reminder(ReminderKind.EyeBreak, 1200))),
        ("settings-unread", t => t.ShowNote(Lang.Current.ConfigNotUsed, Lang.Current.ConfigDefaultsUntilFixed, 15)),
        ("settings-skipped", t => t.ShowNote(Lang.Current.ConfigSkipped, Lang.Current.ConfigSkippedDetail, 15)),
        ("settings-not-saved", t => t.ShowNote(Lang.Current.V1.SaveFailedTitle, Lang.Current.V1.SaveFailedDetail, 15)),
        ("settings-replaced", t => t.ShowNote(Lang.Current.V1.UnsavedReplacedTitle, Lang.Current.V1.UnsavedReplacedDetail, 15)),
        ("locking-failing", t => t.ShowLastingNote(Lang.Current.V1.TicksFailingTitle, Lang.Current.V1.TicksFailingDetail)),
    ];

    static Toast Make(string scene, NotePosition place, Rectangle area, int dpi = 96)
    {
        long clock = 1_000_000;
        var toast = new Toast(headless: true)
        {
            Clock = () => clock,
            // The pointer is away from every place, so no note is held.
            Pointer = () => new Point(area.X + area.Width / 4, area.Y + area.Height / 4),
            Area = _ => area,
            Dpi = _ => dpi,
        };
        toast.Use(new Config { NotificationPlace = place, SnoozeMinutes = scene.EndsWith("-snooze", StringComparison.Ordinal) ? 10 : 0 });
        Scenes.First(s => s.Name == scene).Show(toast);
        // Notes that go away by themselves are drawn a little way into their time, the countdown on
        // the second it is in, and Try it two seconds in.
        clock += scene == "tryit" ? 2000 : toast.IsCounting ? 0 : 3000;
        toast.Step();
        return toast;
    }

    static void Each(string tag)
    {
        Say($"--- {tag}");
        bool rtl = Lang.Current.RightToLeft;
        var t = Theme.Current;
        var pills = new Dictionary<string, Bitmap>();

        foreach (var (name, _) in Scenes)
        {
            using var toast = Make(name, NotePosition.BottomRight, Area);
            var drawn = toast.Drawn;
            var picture = toast.Picture();
            pills[name] = picture;

            Is(drawn.TitleNeeds.Width == drawn.Title.Width && drawn.DetailNeeds.Width == drawn.Detail.Width, $"{name}: nothing cut short");
            Is(drawn.Title.Top >= 0 && drawn.Title.Bottom <= drawn.Size.Height && drawn.Detail.Bottom <= drawn.Size.Height, $"{name}: the words fit the pill's height");
            Is(rtl == drawn.RightToLeft, $"{name}: reads the language's way");
            // The notes about a settings file that could not be read, the one about unsaved changes
            // replaced and the one about the locking always run to more lines; the one about a save
            // that failed and the one about a settings file that could not be read fit on one line in
            // Arabic and wrap in English.
            bool settings = name.StartsWith("settings", StringComparison.Ordinal) || name == "locking-failing";
            if (name != "settings-not-saved" && !(rtl && name == "settings-unread")) Is(drawn.Wrapped == settings, $"{name}: {(settings ? "detail on the lines under the title" : "one line")}");
            if (drawn.Wrapped)
            {
                Is(drawn.Detail.Top >= drawn.Title.Bottom && (rtl ? drawn.Detail.Right == drawn.Title.Right : drawn.Detail.Left == drawn.Title.Left), $"{name}: detail under the title, from the same start");
                Is(toast.DetailLines(drawn.Detail.Width).Height <= drawn.Detail.Height && drawn.Detail.Bottom <= drawn.Size.Height - 4, $"{name}: every line of the detail has room, clear of the line");
                Is(rtl ? drawn.Logo.Left >= drawn.Title.Right : drawn.Logo.Right <= drawn.Title.Left, $"{name}: logo before the words");
            }
            else if (rtl) Is(drawn.Logo.Left >= drawn.Title.Right && (drawn.Detail.Width == 0 || drawn.Title.Left >= drawn.Detail.Right), $"{name}: logo, title, detail from the right");
            else Is(drawn.Logo.Right <= drawn.Title.Left && (drawn.Detail.Width == 0 || drawn.Title.Right <= drawn.Detail.Left), $"{name}: logo, title, detail from the left");
            if (rtl && name == "countdown-9") Is(InkBelow(picture, drawn.Title) > 0, $"{name}: the mark below the line in its last word is drawn, not cut off");
            if (rtl && !drawn.Wrapped) Is(LowestInk(picture, drawn) < picture.Height - 4, $"{name}: a clear row between the words and the line");
            Is(picture.GetPixel(0, 0).A == 0 && picture.GetPixel(picture.Width - 1, 0).A == 0, $"{name}: round ends");

            // Every note in the one calm style: the card's thin edge, the line in the accent, the time
            // in the title (the seconds left, the time away) in the accent and the rest of it in the
            // text color, and nothing in the warning color.
            bool counting = name.StartsWith("countdown", StringComparison.Ordinal) || name == "tryit";
            Is(picture.GetPixel(picture.Width / 2, 0).ToArgb() == t.Edge.ToArgb(), $"{name}: edge in the card's edge color");
            int lineY = picture.Height - 2;
            int startX = rtl ? picture.Width - picture.Height / 2 - 2 : picture.Height / 2 + 2;
            Is(picture.GetPixel(startX, lineY).ToArgb() == t.Accent.ToArgb(), $"{name}: the line starts at the reading start in the accent");
            // A note that stays until it is taken back has nothing that runs down: its line is full,
            // to the reading end. The others are drawn a little way into their time, short of it.
            int endX = rtl ? picture.Height / 2 + 2 : picture.Width - picture.Height / 2 - 2;
            bool lasting = name == "locking-failing";
            if (!counting) Is((picture.GetPixel(endX, lineY).ToArgb() == t.Accent.ToArgb()) == lasting, $"{name}: the line {(lasting ? "full, to the reading end" : "already short of the reading end, on its soft track")}");
            bool timed = counting || name.StartsWith("away", StringComparison.Ordinal);
            Is(drawn.Time.IsEmpty != timed, $"{name}: {(timed ? "the time in the title is in the accent" : "no time in the title, all in the text color")}");
            if (timed) Is(rtl ? drawn.Time.Left == drawn.Title.Left : drawn.Time.Right == drawn.Title.Right, $"{name}: the time where it is in the words, at the title's {(rtl ? "left" : "right")} end");
            Is(Inked(picture, Rectangle.FromLTRB(drawn.Time.Left, drawn.Title.Top, drawn.Time.Right, drawn.Title.Bottom), t.Accent) > 9 == timed, $"{name}: {(timed ? "the time drawn in the accent" : "no accent in the title")}");
            Is(Inked(picture, new Rectangle(Point.Empty, picture.Size), t.Warning, 12) == 0, $"{name}: nothing in the warning color");

            bool snooze = name.EndsWith("-snooze", StringComparison.Ordinal);
            Is(toast.SnoozeShown == snooze, $"{name}: Snooze {(snooze ? "shown" : "not shown")}");
            if (snooze)
            {
                var inside = new Rectangle(Point.Empty, drawn.Size);
                Is(inside.Contains(drawn.Snooze), $"{name}: Snooze inside the pill");
                Is(rtl ? drawn.Snooze.Right <= drawn.Detail.Left : drawn.Snooze.Left >= drawn.Detail.Right, $"{name}: Snooze at the reading end");
            }
            Is(toast.AccessibilityObject.Name == toast.Spoken && toast.Spoken.StartsWith(toast.Title, StringComparison.Ordinal), $"{name}: a screen reader hears the title and the detail");
            Is(!AppNames.Any(a => toast.Spoken.Contains(a, StringComparison.OrdinalIgnoreCase)), $"{name}: no example app names");

            if (pictures)
            {
                using var card = OnCard(picture);
                Save(card, $"notes-{name}-{tag}");
            }
        }

        // The note that the locking might not work is still there an hour on with its line full, and
        // goes once taken back; the note about replaced settings goes after its 15 seconds, as the
        // ones about the settings file do.
        foreach (var (name, after, stays) in new[] { ("locking-failing", TimeSpan.FromHours(1), true), ("settings-replaced", TimeSpan.FromSeconds(16), false) })
        {
            using var toast = Make(name, NotePosition.BottomRight, Area);
            long later = 1_000_000 + (long)after.TotalMilliseconds;
            toast.Clock = () => later;
            toast.Step();
            Is(toast.IsShowing == stays && (!stays || toast.LineLeft() == 1), $"{name}: {(stays ? "still there an hour on, its line full" : "gone after its 15 seconds")}");
            if (!stays) continue;
            toast.HideNote(Lang.Current.V1.TicksFailingTitle);
            Is(!toast.IsShowing, $"{name}: gone once taken back");
        }

        // On a monitor at 150% and 200%, and with larger text, every note is whole and inside its
        // pill; a one-line note is about 30 px tall at 100%, so 45 and 60. The ones at 200% are drawn.
        foreach (var (dpi, scale) in new[] { (144, 1f), (192, 1f), (96, 1.5f), (192, 1.5f) })
        {
            Theme.UseForShots(t.IsDark, scale);
            var area = new Rectangle(0, 0, Area.Width * dpi / 96, Area.Height * dpi / 96);
            foreach (var (name, _) in Scenes)
            {
                using var toast = Make(name, NotePosition.BottomRight, area, dpi);
                var drawn = toast.Drawn;
                string at = $"{name} at {dpi * 100 / 96}%{(scale > 1f ? " with larger text" : "")}";
                var inside = new Rectangle(Point.Empty, drawn.Size);
                Is(drawn.TitleNeeds.Width == drawn.Title.Width && (drawn.Wrapped || drawn.DetailNeeds.Width == drawn.Detail.Width), $"{at}: nothing cut short");
                Is(inside.Contains(drawn.Title) && inside.Contains(drawn.Logo) && (drawn.Detail.IsEmpty || inside.Contains(drawn.Detail)) && (drawn.Snooze.IsEmpty || inside.Contains(drawn.Snooze)), $"{at}: everything inside the pill");
                if (!drawn.Wrapped && scale == 1f) Is(Math.Abs(drawn.Size.Height - 30 * dpi / 96) <= dpi / 96, $"{at}: about {30 * dpi / 96} px tall ({drawn.Size.Height})");
                if (!drawn.Wrapped && scale > 1f) Is(drawn.Size.Height > 30 * dpi / 96, $"{at}: taller for the larger text ({drawn.Size.Height})");
                if (!pictures || dpi != 192 || scale != 1f) continue;
                using var picture = toast.Picture();
                using var card = OnCard(picture);
                Save(card, $"notes-{name}-{tag}-200");
            }
        }
        Theme.UseForShots(t.IsDark, 1f);

        // At 150% text the kasratan of ثوانٍ reaches further below the words; the pill grows for it.
        if (rtl)
        {
            Theme.UseForShots(t.IsDark, 1.5f);
            foreach (var name in new[] { "tryit", "countdown-9" })
            {
                using var toast = Make(name, NotePosition.BottomRight, Area);
                var drawn = toast.Drawn;
                using var picture = toast.Picture();
                Is(LowestInk(picture, drawn) < picture.Height - 4, $"{name} at 150% text: a clear row between the words and the line");
                Is(drawn.TitleNeeds.Width == drawn.Title.Width && drawn.DetailNeeds.Width == drawn.Detail.Width, $"{name} at 150% text: nothing cut short");
                if (pictures)
                {
                    using var card = OnCard(picture);
                    Save(card, $"notes-{name}-{tag}-150");
                }
            }
            Theme.UseForShots(t.IsDark, 1f);
        }

        Places(tag, rtl);
        if (pictures) Compare(tag, pills);
        foreach (var picture in pills.Values) picture.Dispose();
    }

    static readonly string[] AppNames = ["OBS", "Zoom", "WhatsApp", "Chrome"];

    // The lowest row of the card with the words' ink on it, above the border and the line along the
    // bottom (at 96 dpi, 1 px and 2 px).
    static int LowestInk(Bitmap picture, Toast.PillLayout drawn)
    {
        var card = Theme.Current.Card;
        var words = drawn.Detail.Width > 0 ? Rectangle.Union(drawn.Title, drawn.Detail) : drawn.Title;
        int lowest = -1;
        for (int y = Math.Max(0, words.Top); y < picture.Height - 3; y++)
        {
            for (int x = Math.Max(0, words.Left); x < Math.Min(picture.Width, words.Right); x++)
            {
                var c = picture.GetPixel(x, y);
                if (Math.Abs(c.R - card.R) + Math.Abs(c.G - card.G) + Math.Abs(c.B - card.B) > 60) lowest = y;
            }
        }
        return lowest;
    }

    // Pixels in a box near a color.
    static int Inked(Bitmap picture, Rectangle box, Color color, int within = 90)
    {
        int ink = 0;
        for (int y = Math.Max(0, box.Top); y < Math.Min(picture.Height, box.Bottom); y++)
        {
            for (int x = Math.Max(0, box.Left); x < Math.Min(picture.Width, box.Right); x++)
            {
                var c = picture.GetPixel(x, y);
                if (c.A == 255 && Math.Abs(c.R - color.R) + Math.Abs(c.G - color.G) + Math.Abs(c.B - color.B) <= within) ink++;
            }
        }
        return ink;
    }

    // Pixels of text under a box and above the line along the bottom.
    static int InkBelow(Bitmap picture, Rectangle box)
    {
        var card = Theme.Current.Card;
        int ink = 0;
        for (int y = box.Bottom; y < picture.Height - 4; y++)
        {
            for (int x = Math.Max(0, box.Left); x < Math.Min(picture.Width, box.Right); x++)
            {
                var c = picture.GetPixel(x, y);
                if (Math.Abs(c.R - card.R) + Math.Abs(c.G - card.G) + Math.Abs(c.B - card.B) > 120) ink++;
            }
        }
        return ink;
    }

    // A pill on the window color, with room around it.
    static Bitmap OnCard(Bitmap pill)
    {
        var sheet = new Bitmap(pill.Width + 2 * Pad, pill.Height + 2 * Pad);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Theme.Current.Window);
        g.DrawImage(pill, Pad, Pad, pill.Width, pill.Height);
        return sheet;
    }

    // Each place on a pretend screen: its work area above a taskbar, with the note where the app
    // puts it. The corners are the screen's own in both languages.
    static void Places(string tag, bool rtl)
    {
        var t = Theme.Current;
        var screen = new Rectangle(0, 0, Area.Width, Area.Height + Taskbar);
        foreach (var place in NotePlace.All)
        {
            using var toast = Make(place == NotePosition.Center ? "eye-snooze" : "countdown", place, Area);
            var box = toast.NoteBounds;
            int margin = NotePlace.Margin;
            Is(Area.Contains(box), $"place {place}: inside the work area");
            bool right = box.Right == Area.Right - margin, left = box.Left == Area.Left + margin;
            bool top = box.Top == Area.Top + margin, bottom = box.Bottom == Area.Bottom - margin;
            bool center = Math.Abs(box.Left + box.Width / 2 - (Area.Left + Area.Width / 2)) <= 1 && Math.Abs(box.Top + box.Height / 2 - (Area.Top + Area.Height / 2)) <= 1;
            bool where = place switch
            {
                NotePosition.BottomRight => right && bottom,
                NotePosition.TopRight => right && top,
                NotePosition.TopLeft => left && top,
                NotePosition.BottomLeft => left && bottom,
                _ => center,
            };
            Is(where, $"place {place}: at {box}{(rtl ? ", the same corner in Arabic" : "")}");
            if (!pictures) continue;

            using var sheet = new Bitmap(screen.Width, screen.Height);
            using (var g = Graphics.FromImage(sheet))
            {
                var desktop = t.IsDark ? Color.FromArgb(0x1B, 0x2A, 0x36) : Color.FromArgb(0xC9, 0xD6, 0xE3);
                var taskbar = t.IsDark ? Color.FromArgb(0x1C, 0x1C, 0x1C) : Color.FromArgb(0xEE, 0xEE, 0xEE);
                g.Clear(desktop);
                using (var bar = new SolidBrush(taskbar))
                    g.FillRectangle(bar, 0, Area.Bottom, screen.Width, Taskbar);
                // The clock, on the side the Windows shell keeps it.
                var clock = Lang.ShellIsRightToLeft() ? new Rectangle(12, Area.Bottom + 10, 60, 20) : new Rectangle(screen.Width - 72, Area.Bottom + 10, 60, 20);
                TextRenderer.DrawText(g, "12:00", Theme.Body(9f), clock, t.Text, taskbar, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                using var picture = toast.Picture();
                g.DrawImage(picture, box.X, box.Y, picture.Width, picture.Height);
                TextRenderer.DrawText(g, NotePlace.Label(place, Lang.Current), Theme.Body(9f), new Rectangle(0, Area.Height / 2 + 48, screen.Width, 24), t.Text, desktop,
                    TextFormatFlags.HorizontalCenter | (rtl ? TextFormatFlags.RightToLeft : 0));
            }
            Save(sheet, $"notes-place-{Slug(place)}-{tag}");
        }
    }

    static string Slug(NotePosition place) => place switch
    {
        NotePosition.TopRight => "topright",
        NotePosition.TopLeft => "topleft",
        NotePosition.BottomLeft => "bottomleft",
        NotePosition.Center => "center",
        _ => "bottomright",
    };

    // ---- Beside the sketch ----

    // The sketch draws three pills: the countdown (board 4, option D), and time away with the media
    // suffix and the break reminder (the previews in the settings sketch). Each goes beside the
    // app's, both at twice the size.
    static void Compare(string tag, Dictionary<string, Bitmap> pills)
    {
        string? folder = Environment.GetEnvironmentVariable("NOTES_REF");
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        var pairs = new List<(string Name, Bitmap Sketch, Bitmap App)>();
        foreach (var (what, scene) in new[] { ("countdown", "countdown"), ("away", "away-playing"), ("break", "break") })
        {
            string file = Path.Combine(folder, $"pill-{what}-{tag}.png");
            if (!File.Exists(file)) continue;
            using var stream = File.OpenRead(file);
            pairs.Add((scene, new Bitmap(stream), OnCard(pills[scene])));
        }
        if (pairs.Count == 0) return;

        const int zoom = 2;
        int gap = 24, caption = 22;
        int sketchWidth = pairs.Max(p => p.Sketch.Width) * zoom;
        int width = sketchWidth + pairs.Max(p => p.App.Width) * zoom + 3 * gap;
        int height = pairs.Sum(p => Math.Max(p.Sketch.Height, p.App.Height) * zoom + caption + gap) + gap;
        using var sheet = new Bitmap(width, height);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(0xDD, 0xE3, 0xEA));
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            int y = gap;
            foreach (var (name, sketch, app) in pairs)
            {
                TextRenderer.DrawText(g, $"{name}: sketch (left), app (right)", Theme.Body(9f), new Point(gap, y), Color.FromArgb(0x4A, 0x55, 0x63), Color.FromArgb(0xDD, 0xE3, 0xEA));
                y += caption;
                g.DrawImage(sketch, new Rectangle(gap, y, sketch.Width * zoom, sketch.Height * zoom));
                g.DrawImage(app, new Rectangle(2 * gap + sketchWidth, y, app.Width * zoom, app.Height * zoom));
                y += Math.Max(sketch.Height, app.Height) * zoom + gap;
            }
        }
        foreach (var (_, sketch, app) in pairs)
        {
            sketch.Dispose();
            app.Dispose();
        }
        Directory.CreateDirectory(Path.Combine(outDir, "compare"));
        sheet.Save(Path.Combine(outDir, "compare", $"notes-vs-sketch-{tag}.png"), ImageFormat.Png);
        Console.WriteLine($"compare/notes-vs-sketch-{tag}: {width}x{height}");
    }

    static void Save(Bitmap picture, string name)
    {
        picture.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
        Console.WriteLine($"{name}: {picture.Width}x{picture.Height}");
    }

    static void Say(string line) => report?.WriteLine(line);
}
