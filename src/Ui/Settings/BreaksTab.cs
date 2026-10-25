namespace SteppedOut;

// The Breaks tab: a reminder to get up after a stretch at the PC (with a picture of the note it
// shows), how long that stretch is, the reminder's sound, eye breaks, and the Snooze button. None
// of these ever lock the PC. The reminder time and the snooze time each end with Custom…; eye
// breaks are a switch, with no list.
sealed class BreaksTab : FlatTab
{
    Toggle? reminders;
    NotePreview? preview;
    DropDown? after;
    SoundPicker? sound;
    Toggle? eyes;
    DropDown? snooze;

    protected override void Fill(FlatPanel page, Action<Func<Config, Config>> edit)
    {
        var v = Text.V1;
        var reminders = this.reminders = Switch("breakReminders");
        var preview = this.preview = new NotePreview(v.TimeForABreak, Text.AtThePcFor(TimeSpan.FromMinutes(Current.BreakMinutes)));
        var after = this.after = new DropDown { LookC = true, Name = "breakMinutes", Unlisted = value => value is int m ? Text.OptionDuration(m) : null };
        foreach (int m in TimeChoices.Breaks) after.Add(Text.OptionDuration(m), m);
        after.AddCustom(v.Custom, new CustomEntry
        {
            Name = v.ReminderAfter,
            Shortest = TimeChoices.ShortestBreak,
            Longest = TimeChoices.LongestBreak,
            Start = 75,
            Value = minutes => minutes,
            Label = Text.OptionDuration,
            Number = value => value is int minutes && minutes >= TimeChoices.ShortestBreak ? minutes : null,
        });
        (string, object)[] sounds =
        [
            (v.Off, BreakSound.Off),
            (v.SoundSteppedOut, BreakSound.SteppedOut),
            (v.SoundDawn, BreakSound.Dawn),
            (v.SoundMeadow, BreakSound.Meadow),
            (v.SoundLantern, BreakSound.Lantern),
            (v.SoundTide, BreakSound.Tide),
        ];
        var sound = this.sound = new SoundPicker(Text, v.ReminderSound, sounds, BreakSound.Own, (value, file) => Plays((BreakSound)value, file))
        {
            Preview = Preview,
            ChooseFile = ChooseSoundFile,
            Drives = Drives,
        };
        sound.List.ButtonWidth = 1;
        sound.List.Name = "breakSound";
        var soundRow = new FlatRow(v.ReminderSound, v.ReminderSoundDetail, sound.List);
        sound.Place = (soundRow, v.ReminderSoundDetail);
        var eyes = this.eyes = Switch("eyeBreaks");
        var snooze = this.snooze = new DropDown { Small = true, ButtonWidth = 1, Name = "snooze", Unlisted = value => value is int m ? SnoozeLabel(m) : null };
        // No snooze is Off, not "0 minutes later".
        snooze.Add(v.Off, 0);
        foreach (int m in TimeChoices.Snooze) snooze.Add(SnoozeLabel(m), m);
        snooze.AddCustom(v.Custom, new CustomEntry
        {
            Name = v.Snooze,
            Shortest = 1,
            Longest = TimeChoices.LongestSnooze,
            Start = 20,
            Value = minutes => minutes,
            Label = SnoozeLabel,
            Number = value => value is int minutes && minutes > 0 ? minutes : null,
        });

        page.Add(new PageHeading(Glyphs.Coffee, v.BreaksTab, v.BreaksIntro, part: false))
            .Add(new MiddleRow(v.BreakReminders, v.BreakRemindersDetail, reminders) { Below = preview }, lineAbove: true)
            .Add(new FlatRow(v.ReminderAfter, v.ReminderAfterDetail, after))
            .Add(soundRow)
            .Add(new FlatRow(v.EyeBreaks, v.EyeBreaksDetail, eyes))
            .Add(new FlatRow(v.Snooze, v.SnoozeDetail, snooze));

        reminders.CheckedChanged += (_, _) =>
        {
            bool on = reminders.Checked;
            edit(c => { c.BreakReminders = on; return c; });
        };
        after.SelectedChanged += (_, _) =>
        {
            if (after.Value is int minutes) edit(c => { c.BreakMinutes = minutes; return c; });
        };
        sound.Chosen += (value, file) =>
        {
            if (value is BreakSound chosen) edit(c => { c.BreakSound = chosen; c.BreakSoundFile = file; return c; });
        };
        eyes.CheckedChanged += (_, _) =>
        {
            bool on = eyes.Checked;
            edit(c => { c.EyeBreaks = on; return c; });
        };
        snooze.SelectedChanged += (_, _) =>
        {
            if (snooze.Value is int minutes) edit(c => { c.SnoozeMinutes = minutes; return c; });
        };
    }

    // Your own sound…: the .wav file someone picked, or null. The file dialog unless a test gives its own.
    internal Func<Control, string?>? ChooseSoundFile { get; init; }

    // Plays a sound picked in the list once (ISettingsHost.PreviewSound). Nothing when it is not set,
    // as in the tests.
    internal Action<Sound>? Preview { get; init; }

    // What Windows says is behind a drive letter (LocalPaths); a stand-in in tests.
    internal Func<char, DriveType> Drives { get; init; } = LocalPaths.DriveTypeOf;

    // What a choice plays when picked: the sound itself, or your own sound's file.
    internal static Sound? Plays(BreakSound choice, string file) =>
        choice == BreakSound.Own ? (file.Length > 0 ? Sound.Own(file) : null)
        : SoundClips.For(choice) is SoundClip clip ? Sound.Of(clip) : null;

    string SnoozeLabel(int minutes) => minutes <= 0 ? Text.V1.Off : Text.SnoozeLabel(minutes);

    protected override void Show(Config config)
    {
        reminders!.Checked = config.BreakReminders;
        preview!.Show(Text.V1.TimeForABreak, Text.AtThePcFor(TimeSpan.FromMinutes(config.BreakMinutes)));
        after!.Value = config.BreakMinutes;
        sound!.Show(config.BreakSound, config.BreakSoundFile);
        eyes!.Checked = config.EyeBreaks;
        snooze!.Value = config.SnoozeMinutes;
    }
}

// A row whose control sits in the middle of everything beside it, the picture under its words
// included, as the sketch's Break reminders does; a plain row puts it level with the title.
sealed class MiddleRow : SettingRow
{
    // Null while the row's own constructor lays it out the first time.
    readonly Control? control;

    public MiddleRow(string title, string detail, Control control) : base(null, title, detail, control)
    {
        this.control = control;
        Look = RowLook.Flat;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (control is not null) control.Top = Math.Max(0, (Height - control.Height) / 2);
    }
}

// What a corner note looks like, such as "Time for a break · 1 hr at the PC": the logo, the title
// in bold in the text color and the rest muted, in a pill with the card's thin edge, as every corner
// note is drawn. Only a picture of the note.
sealed class NotePreview : KitControl
{
    string lead;
    string tail;
    Bitmap? logo;
    int logoSize;
    bool logoTurned;

    public NotePreview(string lead, string tail)
    {
        this.lead = lead;
        this.tail = tail;
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Text = lead + " " + tail;
        AccessibleName = Text;
        AccessibleRole = AccessibleRole.StaticText;
    }

    public string Lead => lead;

    public string Tail => tail;

    // New words, such as the time at the PC after the reminder time changes.
    public void Show(string lead, string tail)
    {
        if (lead == this.lead && tail == this.tail) return;
        this.lead = lead;
        this.tail = tail;
        Text = lead + " " + tail;
        AccessibleName = Text;
        Invalidate();
    }

    Font LeadFont => Strong(9f);

    Font TailFont => Body(9f);

    int LogoSize => Px(14);

    int Pad => Px(4);

    int Gap => Px(6);

    int EndPad => Px(11);

    // Widths as drawn, without the room TextRenderer keeps at each end, so the gaps are the sketch's.
    static int Across(string words, Font font) =>
        TextRenderer.MeasureText(words, font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;

    int Wanted => Pad + LogoSize + Gap + Across(lead, LeadFont) + Gap + Across(tail, TailFont) + EndPad;

    int PillHeight => Math.Max(Px(22), Math.Max(Shapes.Measure(lead, LeadFont).Height, Shapes.Measure(tail, TailFont).Height) + Px(6));

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        return new Size(width, PillHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        Smooth(g);
        int width = Math.Min(Width, Wanted);
        int height = Math.Min(Height, PillHeight);
        var pill = Box(0, 0, width, height);
        using (var path = Shapes.Rounded(Shapes.Inside(pill), height / 2f))
        {
            using var fill = new SolidBrush(t.Card);
            g.FillPath(fill, path);
            using var pen = new Pen(t.Edge);
            g.DrawPath(pen, path);
        }
        if (logo is null || logoSize != LogoSize || logoTurned != IsMirrored)
        {
            logo?.Dispose();
            logoSize = LogoSize;
            logoTurned = IsMirrored;
            logo = Logo.Draw(logoSize);
            // The logo is never mirrored: a window laid out right to left mirrors what is drawn in it,
            // so the picture goes in turned round already, as the ring's does.
            if (logoTurned) logo.RotateFlip(RotateFlipType.RotateNoneFlipX);
        }
        var logoBox = Box(Pad, (height - logoSize) / 2, logoSize, logoSize);
        g.DrawImage(logo, logoBox);
        int x = Pad + logoSize + Gap;
        int leadWidth = Across(lead, LeadFont);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        Write(g, lead, LeadFont, Box(x, 0, Math.Min(leadWidth, Math.Max(1, width - x - EndPad)), height), t.Text, flags | TextFormatFlags.EndEllipsis);
        x += leadWidth + Gap;
        int room = width - x - EndPad;
        if (room > 0) Write(g, tail, TailFont, Box(x, 0, room, height), t.Subtle, flags | TextFormatFlags.EndEllipsis);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            logo?.Dispose();
            logo = null;
        }
        base.Dispose(disposing);
    }
}
