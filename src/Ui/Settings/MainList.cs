namespace SteppedOut;

// The seven rows under the status box: the lock time, what music or video, a call and full screen do
// to it, the warning before the lock with Try it beside it, music and video when the PC locks, and
// Start with Windows. Like a tab, it never changes the settings itself: each choice goes out through
// Edited, and Refill shows the settings as they are now without raising anything. Start with Windows
// lives in Windows rather than in the settings, so it is read from the app and handed back to it.
sealed class MainList : ISettingsPage
{
    readonly Func<AutostartState> startup;
    Strings text = Lang.Current;
    RowStack? stack;
    DropDown? lockAfter;
    DropDown? musicVideo;
    DropDown? onCall;
    DropDown? fullScreen;
    DropDown? warning;
    DropDown? mediaOnLock;
    KitButton? tryIt;
    Toggle? startWithWindows;
    SettingRow? startRow;
    bool warns = true;

    // While the list shows settings it was given, a switch that flips is not a choice the person made.
    bool refilling;

    // Start with Windows as Windows has it now: on or off, whether the person can change it here,
    // and why not.
    public MainList(Func<AutostartState> startup)
    {
        this.startup = startup;
    }

    public event Action<Func<Config, Config>>? Edited;

    // Try it was pressed: show the countdown note for a few seconds.
    public event Action? TryItClicked;

    // The Start with Windows switch was flipped by the person, to this.
    public event Action<bool>? StartWithWindowsClicked;

    // For tests and the render tool.
    internal DropDown? LockAfter => lockAfter;
    internal DropDown? MusicVideo => musicVideo;
    internal DropDown? OnCall => onCall;
    internal DropDown? FullScreen => fullScreen;
    internal DropDown? Warning => warning;
    internal DropDown? MediaOnLock => mediaOnLock;
    internal KitButton? TryIt => tryIt;
    internal Toggle? StartWithWindows => startWithWindows;
    internal IReadOnlyList<SettingRow> Rows => stack?.Rows ?? Array.Empty<SettingRow>();

    public Control Build(Config config, Strings text)
    {
        this.text = text;
        var v = text.V1;
        refilling = true;
        try
        {
            lockAfter = LockAfterList(v.LockAfter);
            musicVideo = ExtraList(v.MusicVideo);
            onCall = ExtraList(v.OnCall);
            fullScreen = ExtraList(v.FullScreen);
            warning = WarningList(v.WarnMe);
            mediaOnLock = Choices(v.MediaOnLock)
                .Add(v.PauseUntilBack, SteppedOut.MediaOnLock.PauseUntilBack)
                .Add(v.PauseAndStayPaused, SteppedOut.MediaOnLock.PauseStayPaused)
                .Add(v.KeepPlaying, SteppedOut.MediaOnLock.KeepPlaying);
            tryIt = new KitButton(v.TryIt) { Small = true };
            startWithWindows = new Toggle { Look = ToggleLook.LookC, ShowLabel = true };

            stack = new RowStack();
            stack.Add(Row(Glyphs.Clock, v.LockAfter, v.LockAfterDetail, lockAfter));
            stack.Add(Row(Glyphs.Video, v.MusicVideo, v.MusicVideoDetail, musicVideo));
            stack.Add(Row(Glyphs.Microphone, v.OnCall, v.OnCallDetail, onCall));
            stack.Add(Row(Glyphs.Fullscreen, v.FullScreen, v.FullScreenDetail, fullScreen));
            var warn = new TryItAndList(tryIt, warning);
            stack.Add(Row(Glyphs.Bell, v.WarnMe, v.WarnMeDetail, warn));
            // The row names the box it holds; the two controls inside get names of their own.
            warning.AccessibleName = v.WarnMe;
            warning.AccessibleDescription = v.WarnMeDetail;
            Automation.HelpFromDescription(warning);
            tryIt.AccessibleName = v.WarnMe + ". " + v.TryIt;
            stack.Add(Row(Glyphs.Pause, v.MediaOnLock, v.MediaOnLockDetail, mediaOnLock));
            startRow = Row(Glyphs.Power, v.StartWithWindows, v.StartWithWindowsDetail, startWithWindows);
            stack.Add(startRow);
            Fill(config);
        }
        finally
        {
            refilling = false;
        }

        lockAfter.SelectedChanged += (_, _) =>
        {
            if (refilling || lockAfter.Value is not double minutes) return;
            Edit(c => c.IdleMinutes = minutes);
        };
        Wire(musicVideo, (c, effect) => c.MusicVideo = effect);
        Wire(onCall, (c, effect) => c.OnCall = effect);
        Wire(fullScreen, (c, effect) => c.FullScreen = effect);
        warning.SelectedChanged += (_, _) =>
        {
            if (refilling || warning.Value is not int seconds) return;
            warns = seconds > 0;
            ShowTryIt();
            Edit(c => c.GraceSeconds = seconds);
        };
        mediaOnLock.SelectedChanged += (_, _) =>
        {
            if (refilling || mediaOnLock.Value is not SteppedOut.MediaOnLock media) return;
            Edit(c => c.MediaOnLock = media);
        };
        tryIt.Click += (_, _) => TryItClicked?.Invoke();
        startWithWindows.CheckedChanged += (_, _) =>
        {
            if (refilling) return;
            StartWithWindowsClicked?.Invoke(startWithWindows.Checked);
        };
        return stack;
    }

    public void Refill(Config config)
    {
        if (stack is null) return;
        refilling = true;
        try
        {
            Fill(config);
        }
        finally
        {
            refilling = false;
        }
    }

    void Fill(Config config)
    {
        lockAfter!.Value = config.IdleMinutes;
        musicVideo!.Value = config.MusicVideo;
        onCall!.Value = config.OnCall;
        fullScreen!.Value = config.FullScreen;
        warning!.Value = config.GraceSeconds;
        mediaOnLock!.Value = config.MediaOnLock;
        ShowStartup();
        warns = config.GraceSeconds > 0;
        ShowTryIt();
    }

    // On or off as Windows has it. When Windows or a policy holds the choice the switch is greyed out
    // and the line under it says why, in place of the usual one. While Windows has not answered yet,
    // the switch keeps what it shows (off at first, or what was just clicked), greyed out.
    void ShowStartup()
    {
        var state = startup();
        if (state != AutostartState.Reading) startWithWindows!.Checked = state.IsOn();
        startWithWindows!.Enabled = state.CanChange();
        startRow!.Detail = state.Note(text.V1) ?? text.V1.StartWithWindowsDetail;
    }

    // Greyed out while the warning is Off, as there is no countdown to show (D21).
    void ShowTryIt()
    {
        if (tryIt is not null) tryIt.Enabled = warns;
    }

    void Edit(Action<Config> change) => Edited?.Invoke(c =>
    {
        change(c);
        return c;
    });

    void Wire(DropDown list, Action<Config, Effect> set) => list.SelectedChanged += (_, _) =>
    {
        if (refilling || list.Value is not Effect effect) return;
        Edit(c => set(c, effect));
    };

    SettingRow Row(string glyph, string title, string detail, Control control) =>
        new(glyph, title, detail, control) { Look = RowLook.LookC };

    // A list as the settings window draws it: look C, as wide as the sketch's.
    static DropDown Choices(string name) => new() { LookC = true, ButtonWidth = 190, AccessibleName = name };

    // 5, 10, 15 and 30 minutes, 1 hour, and Custom…. Any other time, such as the welcome's 2 minutes
    // or a fraction set in the file, shows as itself.
    DropDown LockAfterList(string name)
    {
        var list = Choices(name);
        list.Unlisted = value => value is double minutes ? MinutesLabel(minutes) : null;
        foreach (int minutes in TimeChoices.LockAfter) list.Add(text.OptionDuration(minutes), (double)minutes);
        list.AddCustom(text.V1.Custom, new CustomEntry
        {
            Name = name,
            Shortest = TimeChoices.Shortest,
            Longest = TimeChoices.Longest,
            Value = minutes => (double)minutes,
            Label = text.OptionDuration,
            Number = value => value is double minutes && minutes >= TimeChoices.Shortest && minutes == Math.Floor(minutes) ? (int)minutes : null,
        });
        return list;
    }

    // A lock time as a list item: whole minutes as the list says them, under a minute in seconds,
    // and a fraction from the file to the nearest minute, as the other lines name a setting.
    string MinutesLabel(double minutes) => minutes < 1
        ? text.SecondsOption((int)Math.Max(1, Math.Round(minutes * 60, MidpointRounding.AwayFromZero)))
        : text.OptionDuration((int)Math.Round(minutes, MidpointRounding.AwayFromZero));

    // No extra time, +15 and +30 minutes, +1 and +2 hours, Never lock, and Custom… for any other extra.
    DropDown ExtraList(string name)
    {
        var v = text.V1;
        var list = Choices(name);
        list.Unlisted = value => value is Effect effect ? text.EffectLabel(effect, v.NoExtraTime) : null;
        list.Add(v.NoExtraTime, Effect.None);
        foreach (int minutes in TimeChoices.Extras) list.Add(text.ExtraLabel(minutes), Effect.Extra(minutes));
        list.Add(v.NeverLock, Effect.Never);
        list.AddCustom(v.Custom, new CustomEntry
        {
            Name = name,
            Shortest = Config.MinEffectMinutes,
            Longest = Config.MaxEffectMinutes,
            Start = 45,
            Value = minutes => Effect.Extra(minutes),
            Label = text.ExtraLabel,
            Number = value => value is Effect { Kind: EffectKind.Extra } effect ? effect.Amount : null,
        });
        return list;
    }

    // Off, 10, 15 and 30 seconds, 1 minute, and Custom… in seconds.
    DropDown WarningList(string name)
    {
        var v = text.V1;
        var list = Choices(name);
        list.Unlisted = value => value is int seconds ? seconds <= 0 ? v.Off : text.SecondsOption(seconds) : null;
        list.Add(v.Off, 0);
        foreach (int seconds in TimeChoices.Warning) list.Add(text.SecondsOption(seconds), seconds);
        list.AddCustom(v.Custom, new CustomEntry
        {
            Name = name,
            Unit = TimeUnit.Seconds,
            Shortest = 1,
            Longest = TimeChoices.LongestWarning,
            Start = 20,
            Value = seconds => seconds,
            Label = text.SecondsOption,
            Number = value => value is int seconds && seconds > 0 ? seconds : null,
        });
        return list;
    }

    // The rows one under another, 4 px apart, as the sketch's stack of rows.
    sealed class RowStack : Panel
    {
        const int Gap = 4;
        readonly List<SettingRow> rows = new();

        public RowStack()
        {
            // Read by the rows in it. Left unnamed, WinForms names a panel after the label before it.
            AccessibleName = "";
        }

        public IReadOnlyList<SettingRow> Rows => rows;

        public void Add(SettingRow row)
        {
            rows.Add(row);
            Controls.Add(row);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
            int height = 0;
            foreach (var row in rows)
            {
                if (!row.Visible) continue;
                if (height > 0) height += LogicalToDeviceUnits(Gap);
                height += row.GetPreferredSize(new Size(width, 0)).Height;
            }
            return new Size(width, height);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int y = 0;
            foreach (var row in rows)
            {
                if (!row.Visible) continue;
                if (y > 0) y += LogicalToDeviceUnits(Gap);
                int height = row.GetPreferredSize(new Size(Width, 0)).Height;
                row.SetBounds(0, y, Width, height);
                y += height;
            }
        }
    }

    // Try it and the warning's list side by side, 8 px apart: Try it first, at the start of the line.
    sealed class TryItAndList : Panel
    {
        const int Gap = 8;
        readonly KitButton button;
        readonly DropDown list;

        public TryItAndList(KitButton button, DropDown list)
        {
            this.button = button;
            this.list = list;
            // The row's own card shows through around the two.
            BackColor = Color.Transparent;
            Controls.Add(button);
            Controls.Add(list);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var b = button.GetPreferredSize(Size.Empty);
            var l = list.GetPreferredSize(Size.Empty);
            return new Size(b.Width + LogicalToDeviceUnits(Gap) + l.Width, Math.Max(b.Height, l.Height));
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            var b = button.GetPreferredSize(Size.Empty);
            var l = list.GetPreferredSize(Size.Empty);
            bool flip = RightToLeft == RightToLeft.Yes && !IsMirrored;
            int listX = Width - l.Width;
            int buttonX = listX - LogicalToDeviceUnits(Gap) - b.Width;
            button.Bounds = new Rectangle(flip ? Width - buttonX - b.Width : buttonX, (Height - b.Height) / 2, b.Width, b.Height);
            list.Bounds = new Rectangle(flip ? 0 : listX, (Height - l.Height) / 2, l.Width, l.Height);
        }
    }
}
