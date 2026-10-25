namespace SteppedOut;

// The Appearance tab: light or dark mode, the text size, minutes on the taskbar icon, and where the
// notifications appear. A choice here reaches the app as an edit, and the app
// uses it at once (Theme.Choose), which builds the open windows again in the new look.
sealed class AppearanceTab : FlatTab
{
    Segmented? mode;
    Segmented? size;
    Toggle? minutes;
    DropDown? place;

    protected override void Fill(FlatPanel page, Action<Func<Config, Config>> edit)
    {
        var v = Text.V1;
        var mode = this.mode = new Segmented { Name = "themeMode" }
            .Add(v.Light, ThemeChoice.Light)
            .Add(v.Dark, ThemeChoice.Dark)
            .Add(v.System, ThemeChoice.System);
        var size = this.size = new Segmented { Name = "textSize" }
            .Add(v.Small, TextSize.Small)
            .Add(v.Medium, TextSize.Medium)
            .Add(v.Large, TextSize.Large)
            .Add(v.System, TextSize.System);
        var minutes = this.minutes = Switch("minutesOnIcon");
        var place = this.place = new DropDown { Small = true, ButtonWidth = 1, Name = "notificationPlace" };
        foreach (var where in NotePlace.All) place.Add(NotePlace.Label(where, Text), where);

        page.Add(new PageHeading(Glyphs.Palette, v.AppearanceTab, v.AppearanceIntro, part: false))
            .Add(new FlatRow(v.ThemeMode, v.ThemeModeDetail, mode), lineAbove: true)
            .Add(new FlatRow(v.TextSize, v.TextSizeDetail, size))
            .Add(new FlatRow(v.MinutesOnIcon, v.MinutesOnIconDetail, minutes))
            .Add(new FlatRow(v.NotificationPlace, v.NotificationPlaceDetail, place));

        mode.SelectedChanged += (_, _) =>
        {
            if (mode.Value is ThemeChoice theme) edit(c => { c.Theme = theme; return c; });
        };
        size.SelectedChanged += (_, _) =>
        {
            if (size.Value is TextSize text) edit(c => { c.TextSize = text; return c; });
        };
        minutes.CheckedChanged += (_, _) =>
        {
            bool on = minutes.Checked;
            edit(c => { c.MinutesOnTrayIcon = on; return c; });
        };
        // Until someone picks a place, the notes follow the side of the Windows shell and nothing is
        // saved. Picking the place already shown saves it too, which the list only tells through
        // Picked.
        void Place()
        {
            if (place.Value is NotePosition where && Current.NotificationPlace != where)
                edit(c => { c.NotificationPlace = where; return c; });
        }
        place.SelectedChanged += (_, _) => Place();
        place.Picked += (_, _) => Place();
    }

    protected override void Show(Config config)
    {
        mode!.Value = config.Theme;
        size!.Value = config.TextSize;
        minutes!.Checked = config.MinutesOnTrayIcon;
        place!.Value = config.NotificationPlaceInUse;
    }
}

// What the three tabs of this kind share: a panel of rows under the tab strip, a copy of the
// settings that each change goes into before it is sent on, and a refill that shows settings
// without sending anything.
//
// The page never changes the settings itself. Each change goes out through Edited, after the page
// shows it from its own copy. Refill puts the settings on the page with every handler told to stand
// aside, since a switch tells CheckedChanged even when it is set from here.
abstract class FlatTab : ISettingsPage
{
    bool refilling;
    int builds;
    Control? built;

    public event Action<Func<Config, Config>>? Edited;

    // The settings as the page last showed them, with its own changes in.
    protected Config Current { get; private set; } = new();

    protected Strings Text { get; private set; } = Lang.Current;

    // The page's control from the last Build, until the window disposes it.
    protected Control? Built => built;

    public Control Build(Config config, Strings text)
    {
        Text = text;
        Current = config.Copy();
        // Controls from an earlier build may still be around until the window disposes them; they
        // no longer change anything.
        int build = ++builds;
        var page = new FlatPanel { JoinedAbove = true };
        refilling = true;
        try
        {
            Fill(page, change =>
            {
                if (build == builds) Edit(change);
            });
            built = page;
            Show(Current);
        }
        finally
        {
            refilling = false;
        }
        page.Disposed += (_, _) =>
        {
            if (built != page) return;
            built = null;
            Forget();
        };
        return page;
    }

    public void Refill(Config config)
    {
        Current = config.Copy();
        Refresh();
    }

    // Shows Current on the page, telling nothing.
    void Refresh()
    {
        if (built is null) return;
        refilling = true;
        try
        {
            Show(Current);
        }
        finally
        {
            refilling = false;
        }
    }

    // A change the person made: kept and shown here first, then sent on. Nothing on the page is
    // touched after it is sent, since the window may build the page again in answer.
    void Edit(Func<Config, Config> change)
    {
        if (refilling) return;
        Current = change(Current.Copy());
        Refresh();
        Edited?.Invoke(change);
    }

    // Builds the page's rows into the panel. edit is how a control asks for a change.
    protected abstract void Fill(FlatPanel page, Action<Func<Config, Config>> edit);

    // Puts these settings on the page's controls. Never asks for a change.
    protected abstract void Show(Config config);

    // The page's control was disposed: let go of anything that would still reach it.
    protected virtual void Forget()
    {
    }

    protected static Toggle Switch(string name) => new() { Look = ToggleLook.LookC, ShowLabel = true, Name = name };
}

// The head of a page (its icon, its name in bold and what it is for) or of a part of it, such as
// "Audio": a small icon and the name in the accent color, with what it is for under it.
// Screen readers hear a heading.
sealed class PageHeading : KitControl
{
    readonly string glyph;
    readonly string title;
    readonly string? detail;
    readonly bool part;

    public PageHeading(string glyph, string title, string? detail, bool part)
    {
        this.glyph = glyph;
        this.title = title;
        this.detail = detail;
        this.part = part;
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Text = title;
        AccessibleName = title;
        AccessibleDescription = detail;
        AccessibleRole = AccessibleRole.StaticText;
    }

    public string Title => title;

    public string? Detail => detail;

    public bool Part => part;

    Font TitleFont => Strong(9.75f);

    Font DetailFont => Body(9f);

    Font GlyphFont => Icons(part ? 9.75f : 15f);

    // The icon's own size: it grows with the text size, so its box grows with it.
    Size GlyphSize => TextRenderer.MeasureText(glyph, GlyphFont, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

    int GlyphWidth => Math.Max(Px(part ? 14 : 20), GlyphSize.Width);

    int GlyphHeight(int titleHeight) => Math.Max(part ? titleHeight : Px(20), GlyphSize.Height);

    int GlyphTop => part ? 0 : Px(1);

    int GlyphGap => Px(part ? 6 : 10);

    // The page's head has its words beside the icon; a part's explanation starts under its name, at
    // the page's edge, as the sketch's Audio line does.
    int TextLeft => GlyphWidth + GlyphGap;

    int TitleHeight(int width) => Shapes.WrappedHeight(title, TitleFont, Math.Max(1, width - TextLeft));

    int DetailLeft => part ? 0 : TextLeft;

    int DetailTop(int width) => TitleHeight(width) + Px(part ? 4 : 1);

    int DetailHeight(int width) => detail is null ? 0 : Shapes.WrappedHeight(detail, DetailFont, Math.Max(1, width - DetailLeft));

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        int height = detail is null ? TitleHeight(width) : DetailTop(width) + DetailHeight(width);
        int glyphBottom = GlyphTop + GlyphHeight(TitleHeight(width));
        return new Size(width, Math.Max(Math.Max(height, glyphBottom), Px(part ? 18 : 22)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        int titleHeight = TitleHeight(Width);
        Write(g, glyph, GlyphFont, Box(0, GlyphTop, GlyphWidth, GlyphHeight(titleHeight)), t.Accent,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        Write(g, title, TitleFont, Box(TextLeft, 0, Width - TextLeft, titleHeight), part ? t.Accent : t.Text,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        if (detail is not null)
        {
            Write(g, detail, DetailFont, Box(DetailLeft, DetailTop(Width), Width - DetailLeft, DetailHeight(Width)), t.Subtle,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
    }

    protected override Automation.Provider CreateProvider() => new(this, Automation.TextType) { HeadingLevel = part ? 3 : 2 };
}
