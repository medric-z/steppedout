namespace SteppedOut;

// Shown the first time SteppedOut starts, so it never just vanishes into the tray, and again from
// "Show the welcome again". Five pages: what SteppedOut does; when it locks; the time it adds while
// the PC is in use without the mouse or keyboard; what happens while you're away; and where to find
// it. On the second and third a drawn PC acts out the moment picked on a timeline under it. Every
// choice is one click, and nothing changes in the settings until the window closes: Done, Open
// settings and closing the window all keep what is chosen.
sealed class WelcomeForm : Form
{
    public const int PageCount = 5;

    readonly Strings text = Lang.Current;
    readonly Config start;
    readonly ISettingsHost? host;
    readonly WelcomeChoices choices;
    readonly bool hasBattery;

    readonly WelcomeBody body = new();
    readonly WelcomeHead head = new();
    readonly WelcomeFrame frame;
    readonly WelcomeFooter footer;
    readonly StepDots dots = new();
    readonly WelcomeStage stage;
    readonly WelcomeTimeline timeline;
    readonly FirstPage first;
    readonly PageStack locking = new();
    readonly PageStack stillUsing = new();
    readonly PageStack awayPage = new() { CanCloseUp = true };
    readonly PageStack setPage = new();

    readonly KitButton startButton;
    readonly KitButton back;
    readonly KitButton skip;
    readonly KitButton next;
    readonly KitButton openSettings;
    readonly KitButton done;

    readonly ChoiceChips lockAfter;
    readonly ChoiceChips warning;
    readonly KitButton tryIt;
    readonly ChoiceChips shortcut;
    readonly ChoiceChips music;
    readonly ChoiceChips call;
    readonly ChoiceChips fullScreen;
    readonly ChoiceChips battery;
    readonly WelcomeQuestion batteryQuestion;
    readonly ChoiceChips media;
    readonly Toggle mute = new();
    readonly Toggle screenOff = new();
    readonly Toggle timeAway = new();
    readonly ChoiceChips breaks;
    readonly Toggle startWithWindows = new();
    readonly PickedList picked;

    readonly System.Windows.Forms.Timer tryTimer = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer playTimer = new() { Interval = PlayHold };
    readonly System.Windows.Forms.Timer glideTimer = new() { Interval = 15 };

    int page = 1;
    // What the third page previews going on, from the toggles on its stage.
    Situation on = Situation.Music;
    Moment moment = Moment.Warning;
    // Seconds left on Try it's countdown note; 0 while it is not showing.
    int tryLeft;
    // The moments Play walks through, and the one it is on; null while it is not playing.
    IReadOnlyList<Moment>? playOrder;
    int playIndex;
    float glideFrom;
    float glideTo;
    long glideStart;
    // A shortcut "Choose another shortcut…" registered with the app while the window is open.
    string? held;
    // The countdown's length when the countdown is chosen: the settings' own, or the welcome's.
    readonly int countdown;
    bool loaded;
    bool finished;

    // How long Play holds each moment, and how long its dot takes to move to the next, in ms.
    public const int PlayHold = 2000;
    public const int GlideTime = 600;

    // The settings with the choices in them, whether start with Windows is on, and whether to open
    // the settings window next.
    public event Action<Config, bool, bool>? Finished;

    // The time on the clocks in the pictures. The render tool fixes it, so its pictures are the same
    // from one run to the next.
    internal static Func<DateTime> Clock
    {
        get => WelcomeStage.Clock;
        set
        {
            WelcomeStage.Clock = value;
            TaskbarPicture.Clock = value;
        }
    }

    // Whether the PC has a battery: without one the battery question and its toggle are left out,
    // here and in the settings' Locking tab. Tests and the render tool set it.
    internal static Func<bool> HasBattery { get; set; } = Native.HasBattery;

    // Whether Windows shows animations: without them Play steps from moment to moment and nothing
    // glides. Tests and the render tool set it.
    internal static Func<bool> Motion { get; set; } = Native.AnimationsOn;

    // How a Custom… box is shown. Null shows it under its pill with the keyboard in it; tests and the
    // render tool put their own here.
    internal Action<CustomBox>? Presenter { get; set; }

    // "Choose another shortcut…": the shortcut picked, from the one chosen now, or null for none. Null
    // opens the app's own window, which needs the app.
    internal Func<string, string?>? ChooseShortcut { get; set; }

    public WelcomeForm(Config config, bool startsWithWindows, ISettingsHost? host = null)
    {
        SuspendLayout();
        start = config.Copy();
        this.host = host;
        choices = WelcomeChoices.From(start, startsWithWindows);
        countdown = choices.Warns ? choices.Warning : WelcomeChoices.CountdownSeconds;
        hasBattery = HasBattery();
        var v = text.V1;
        var t = Theme.Current;

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = text.WelcomeTitle;
        Icon = Icons.Window();
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = Theme.Body(10f);
        RightToLeft = Lang.Direction;
        RightToLeftLayout = text.RightToLeft;
        t.Dress(this);
        frame = new WelcomeFrame(head, body) { Dock = DockStyle.Fill };
        t.DressScrollBars(frame);
        t.DressScrollBars(body);

        // The buttons.
        startButton = new KitButton(v.Start, primary: true);
        back = new KitButton(v.Back);
        skip = new KitButton(v.Skip);
        next = new KitButton(v.Next, primary: true);
        openSettings = new KitButton(text.OpenSettingsButton);
        done = new KitButton(text.Done, primary: true);
        startButton.Click += (_, _) => GoTo(2);
        back.Click += (_, _) => GoTo(page - 1);
        skip.Click += (_, _) => GoTo(page + 1);
        next.Click += (_, _) => GoTo(page + 1);
        done.Click += (_, _) => End(openSettings: false);
        openSettings.Click += (_, _) => End(openSettings: true);
        footer = new WelcomeFooter(dots, back, skip, next, openSettings, done) { Dock = DockStyle.Bottom };

        // The stage and the timeline.
        stage = new WelcomeStage(text);
        stage.Situations.ShowChoice(Situation.Battery, hasBattery);
        stage.Situations.SelectedChanged += (_, _) =>
        {
            var now = Situation.None;
            foreach (var one in new[] { Situation.Music, Situation.Call, Situation.FullScreen, Situation.Battery })
                if (stage.Situations.IsChecked(one)) now |= one;
            on = now;
            StopPlaying();
            ShowMoment();
        };
        stage.Play.Click += (_, _) => TogglePlay();
        timeline = new WelcomeTimeline(text);
        timeline.MomentChosen += chosen =>
        {
            StopPlaying();
            StopTrying();
            moment = chosen;
            ShowMoment();
        };

        // 1. Welcome.
        first = new FirstPage(text, startButton);

        // 2. Locking.
        lockAfter = new ChoiceChips();
        foreach (int minutes in TimeChoices.WelcomeLockAfter) lockAfter.Add(text.OptionDuration(minutes), (double)minutes);
        lockAfter.AddCustom(v.Custom);
        lockAfter.SelectedChanged += (_, _) => LockAfterChosen((double)lockAfter.Value!);
        lockAfter.CustomClicked += (chip, _) => Custom((Control)chip!, new CustomEntry
        {
            Name = v.LockAfter,
            Shortest = WelcomeChoices.ShortestLock,
            Longest = WelcomeChoices.LongestLock,
            Value = minutes => minutes,
            Label = minutes => text.OptionDuration(minutes),
        }, (int)Math.Clamp(Math.Round(choices.LockAfter), WelcomeChoices.ShortestLock, WelcomeChoices.LongestLock), number => LockAfterChosen(number));

        tryIt = new KitButton(v.TryIt) { Small = true };
        tryIt.AccessibleDescription = v.WarnMeDetail;
        tryIt.Click += (_, _) => TryIt();
        tryTimer.Tick += (_, _) => TryTick();

        warning = new ChoiceChips()
            .Add(WelcomeText.WarningLabel(text, countdown), true)
            .Add(v.NoWarning, false);
        warning.SelectedChanged += (_, _) =>
        {
            choices.Warning = (bool)warning.Value! ? countdown : 0;
            if (!choices.Warns) StopTrying();
            tryIt.Enabled = choices.Warns;
            if (moment == Moment.Warning && !choices.Warns) moment = Moment.Locked;
            if (moment == Moment.Locked && choices.Warns && page == 2) moment = Moment.Warning;
            StopPlaying();
            ShowMoment();
        };

        shortcut = new ChoiceChips()
            .Add(v.Off, "")
            .Add(v.CtrlAltL, SteppedOut.Shortcut.Default.ToString());
        shortcut.AddCustom(v.ChooseAnotherShortcut);
        shortcut.SelectedChanged += (_, _) => ShortcutChosen((string)shortcut.Value!);
        shortcut.CustomClicked += (_, _) => AnotherShortcut();

        var lockQuestion = new WelcomeQuestion(Glyphs.Lock, v.LockAfter, v.LockAfterDetail, lockAfter);
        var warnQuestion = new WelcomeQuestion(Glyphs.Bell, v.WarnMe, v.WarnMeDetail, warning, tryIt);
        var shortcutQuestion = new WelcomeQuestion(Glyphs.Keyboard, v.Shortcut, v.ShortcutDetail, shortcut);
        locking.Add(Heading(v.LockingTab), 0)
            .Add(lockQuestion, 14)
            .Add(warnQuestion, 20)
            .Add(shortcutQuestion, 20);

        // 3. While you're still using it.
        music = Extras(Situation.Music, v.MusicVideo);
        call = Extras(Situation.Call, v.OnCall);
        fullScreen = Extras(Situation.FullScreen, v.FullScreen);
        battery = new ChoiceChips()
            .Add(v.LockAsUsual, Effect.None);
        foreach (int percent in new[] { 25, 50, 75 }) battery.Add(text.SoonerLabel(percent), Effect.Sooner(percent));
        battery.AddCustom(v.Custom);
        battery.SelectedChanged += (_, _) => BatteryChosen((Effect)battery.Value!);
        battery.CustomClicked += (chip, _) => Custom((Control)chip!, new CustomEntry
        {
            Name = v.OnBattery,
            Unit = TimeUnit.Percent,
            FieldName = v.SoonerFieldName,
            Shortest = Config.MinSoonerPercent,
            Longest = Config.MaxSoonerPercent,
            Value = percent => percent,
            Label = text.SoonerLabel,
        }, choices.Battery.Kind == EffectKind.Sooner ? choices.Battery.Amount : 50, percent => BatteryChosen(Effect.Sooner(percent)));
        batteryQuestion = new WelcomeQuestion(Glyphs.Battery, v.OnBattery, v.OnBatteryDetail, battery);
        stillUsing.Add(Heading(v.StillUsing), 0)
            .Add(Words(v.StillUsingIntro), 4)
            .Add(new WelcomeQuestion(Glyphs.Music, v.MusicVideo, v.MusicVideoDetail, music), 16)
            .Add(new WelcomeQuestion(Glyphs.Microphone, v.OnCall, v.OnCallDetail, call), 18)
            .Add(new WelcomeQuestion(Glyphs.Fullscreen, v.FullScreen, v.FullScreenDetail, fullScreen), 18)
            .Add(batteryQuestion, 18)
            .Add(new InfoBox(Glyphs.Apps, v.AppRulesBox, v.AppRulesBoxText), 20);
        if (!hasBattery) stillUsing.Hidden.Add(batteryQuestion);

        // 4. When you're away and back.
        media = new ChoiceChips { Small = true }
            .Add(v.PauseUntilBack, MediaOnLock.PauseUntilBack)
            .Add(v.PauseAndStayPaused, MediaOnLock.PauseStayPaused)
            .Add(v.KeepPlaying, MediaOnLock.KeepPlaying);
        media.SelectedChanged += (_, _) => choices.Media = (MediaOnLock)media.Value!;
        foreach (var toggle in new[] { mute, screenOff, timeAway })
        {
            toggle.Look = ToggleLook.LookC;
            toggle.ShowLabel = true;
        }
        mute.CheckedChanged += (_, _) => choices.Mute = mute.Checked;
        screenOff.CheckedChanged += (_, _) => choices.ScreenOff = screenOff.Checked;
        timeAway.CheckedChanged += (_, _) => choices.ShowTimeAway = timeAway.Checked;
        // Break reminders: Off, the Breaks tab's ready-made times, or a time of one's own.
        breaks = new ChoiceChips { Small = true }.Add(v.Off, 0);
        foreach (int minutes in TimeChoices.Breaks) breaks.Add(text.OptionDuration(minutes), minutes);
        breaks.AddCustom(v.Custom);
        breaks.SelectedChanged += (_, _) => BreaksChosen((int)breaks.Value!);
        breaks.CustomClicked += (chip, _) => Custom((Control)chip!, new CustomEntry
        {
            Name = v.ReminderAfter,
            Shortest = TimeChoices.ShortestBreak,
            Longest = TimeChoices.LongestBreak,
            Value = minutes => minutes,
            Label = text.OptionDuration,
        }, Math.Clamp(choices.BreakMinutes, TimeChoices.ShortestBreak, TimeChoices.LongestBreak), BreaksChosen);
        awayPage.Add(Heading(v.AwayAndBack), 0)
            .Add(new TileGrid(
                new ChoiceTile(Glyphs.Pause, v.MediaOnLock, v.MediaOnLockDetail, media),
                new ChoiceTile(Glyphs.Mute, v.Mute, v.MuteDetail, mute),
                new ChoiceTile(Glyphs.Screen, v.ScreenOff, v.ScreenOffDetail, screenOff),
                new ChoiceTile(Glyphs.Clock, v.ShowTimeAway, v.ShowTimeAwayDetail, timeAway)), 16)
            .Add(new ChipsCard(Glyphs.Coffee, v.BreakReminders, v.BreakRemindersDetail, breaks), 16)
            .Add(new InfoBox(Glyphs.Code, v.AppsAndScripts, v.AppsAndScriptsText), 16);

        // 5. You're set.
        startWithWindows.CheckedChanged += (_, _) => choices.StartWithWindows = startWithWindows.Checked;
        picked = new PickedList(v.WhatYouPicked);
        setPage.Add(Heading(v.YoureSet), 0)
            .Add(new StartCard(v.StartWithWindows, v.StartWithWindowsDetail, startWithWindows), 16)
            .Add(new TwoColumns(new TaskbarPicture(), new TraySteps(v.TrayStep1, v.TrayStep2, v.TrayStep3), picked), 20);

        head.Controls.AddRange([stage, timeline]);
        body.Controls.AddRange([first, locking, stillUsing, awayPage, setPage]);
        frame.TabIndex = 0;
        footer.TabIndex = 1;
        Controls.Add(frame);
        Controls.Add(footer);

        playTimer.Tick += (_, _) => PlayTick();
        glideTimer.Tick += (_, _) => GlideTick();

        ShowChoices();
        GoTo(1);
        ResumeLayout(false);
    }

    // ---- Parts of the pages ----

    TextBlock Heading(string words) => new(words, Theme.Strong(15f), Theme.Current.Text) { Heading = 2 };

    static TextBlock Words(string words) => new(words, Theme.Body(9.75f), Theme.Current.Subtle);

    // The choices for a thing going on: no extra time, a few amounts, never lock, or a number.
    ChoiceChips Extras(Situation one, string name)
    {
        var v = text.V1;
        var chips = new ChoiceChips().Add(v.NoExtraTime, Effect.None);
        foreach (int minutes in TimeChoices.Extras) chips.Add(text.ExtraLabel(minutes), Effect.Extra(minutes));
        chips.Add(v.NeverLock, Effect.Never);
        chips.AddCustom(v.Custom);
        chips.SelectedChanged += (_, _) => ExtraChosen(one, (Effect)chips.Value!);
        chips.CustomClicked += (chip, _) => Custom((Control)chip!, new CustomEntry
        {
            Name = name,
            Shortest = Config.MinEffectMinutes,
            Longest = Config.MaxEffectMinutes,
            Value = minutes => minutes,
            Label = text.ExtraLabel,
        }, choices.EffectOf(one).Kind == EffectKind.Extra ? choices.EffectOf(one).Amount : 45, minutes => ExtraChosen(one, Effect.Extra(minutes)));
        return chips;
    }

    // ---- What is chosen ----

    // Every control showing the choices as they are, without raising anything.
    void ShowChoices()
    {
        lockAfter.Value = choices.LockAfter;
        lockAfter.ShowCustom(TimeChoices.WelcomeLockAfter.Contains((int)choices.LockAfter) && choices.LockAfter == Math.Floor(choices.LockAfter)
            ? null : WelcomeText.LockLabel(text, choices.LockAfter));
        warning.Value = choices.Warns;
        tryIt.Enabled = choices.Warns;
        shortcut.Value = choices.Shortcut;
        bool usual = choices.Shortcut.Length == 0 || choices.Shortcut == SteppedOut.Shortcut.Default.ToString();
        shortcut.ShowCustom(usual ? null : choices.Shortcut);
        ShowEffect(music, choices.Music);
        ShowEffect(call, choices.Call);
        ShowEffect(fullScreen, choices.FullScreen);
        ShowBattery();
        media.Value = choices.Media;
        mute.Checked = choices.Mute;
        screenOff.Checked = choices.ScreenOff;
        timeAway.Checked = choices.ShowTimeAway;
        ShowBreaks();
        startWithWindows.Checked = choices.StartWithWindows;
    }

    // Off, or the time before each reminder: its own pill, or Custom… saying a time of one's own.
    void ShowBreaks()
    {
        int shown = choices.BreakReminders ? choices.BreakMinutes : 0;
        breaks.Value = shown;
        breaks.ShowCustom(shown == 0 || TimeChoices.Breaks.Contains(shown) ? null : text.OptionDuration(shown));
    }

    // Off keeps the time, so the settings still offer it when reminders are turned on again.
    void BreaksChosen(int minutes)
    {
        choices.BreakReminders = minutes > 0;
        if (minutes > 0) choices.BreakMinutes = minutes;
        ShowBreaks();
    }

    void ShowEffect(ChoiceChips chips, Effect effect)
    {
        chips.Value = effect;
        bool preset = effect.Kind is EffectKind.None or EffectKind.Never
            || effect.Kind == EffectKind.Extra && TimeChoices.Extras.Contains(effect.Amount);
        chips.ShowCustom(preset ? null : WelcomeText.ExtraLabel(text, effect));
    }

    // The battery's choices with the real lock time each comes to, from the lock time chosen.
    void ShowBattery()
    {
        foreach (int percent in new[] { 25, 50, 75 })
            battery.Relabel(Effect.Sooner(percent), WelcomeText.BatteryLabel(text, choices, Effect.Sooner(percent)));
        battery.Value = choices.Battery;
        bool preset = choices.Battery.Kind == EffectKind.None
            || choices.Battery.Kind == EffectKind.Sooner && choices.Battery.Amount is 25 or 50 or 75;
        battery.ShowCustom(preset ? null : WelcomeText.BatteryLabel(text, choices, choices.Battery));
    }

    void LockAfterChosen(double minutes)
    {
        choices.LockAfter = minutes;
        lockAfter.Value = minutes;
        lockAfter.ShowCustom(TimeChoices.WelcomeLockAfter.Contains((int)minutes) && minutes == Math.Floor(minutes) ? null : WelcomeText.LockLabel(text, minutes));
        ShowBattery();
        StopPlaying();
        ShowMoment();
    }

    void ExtraChosen(Situation one, Effect effect)
    {
        choices.SetEffect(one, effect);
        ShowEffect(one switch { Situation.Music => music, Situation.Call => call, _ => fullScreen }, effect);
        // The stage shows that thing going on, with the battery as it was.
        on = one | (on & Situation.Battery);
        StopPlaying();
        ShowMoment();
    }

    void BatteryChosen(Effect effect)
    {
        choices.Battery = effect;
        ShowBattery();
        on |= Situation.Battery;
        StopPlaying();
        ShowMoment();
    }

    void ShortcutChosen(string keys)
    {
        choices.Shortcut = keys;
        shortcut.Value = keys;
        bool usual = keys.Length == 0 || keys == SteppedOut.Shortcut.Default.ToString();
        shortcut.ShowCustom(usual ? null : keys);
        // One the window registered and then left: the app lets go of it until the window closes,
        // when the one chosen is taken.
        if (held is not null && keys != held)
        {
            held = null;
            host?.ShortcutChanged("");
        }
        ShowMoment();
    }

    void AnotherShortcut()
    {
        string? keys = (ChooseShortcut ?? Dialog)(choices.Shortcut);
        if (keys is null) return;
        keys = SteppedOut.Shortcut.Normalize(keys);
        if (keys.Length == 0) return;
        if (ChooseShortcut is null) held = keys;
        ShortcutChosen(keys);
    }

    string? Dialog(string now)
    {
        if (host is null) return null;
        using var dialog = new ShortcutDialog(host, now, text);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Chosen : null;
    }

    // A Custom… pill: a small box under it for a number, used when Enter is pressed or the box left.
    void Custom(Control chip, CustomEntry entry, int startAt, Action<int> use)
    {
        var box = new CustomBox(entry, startAt, RightToLeft);
        box.Committed += use;
        box.FormClosed += (_, _) =>
        {
            if (!IsDisposed && box.ReturnFocus && chip.Parent is { } chips) chips.Focus();
        };
        (Presenter ?? (b => Present(chip, b)))(box);
    }

    void Present(Control chip, CustomBox box)
    {
        var at = chip.RectangleToScreen(chip.ClientRectangle);
        var area = WorkArea.Of(Screen.FromControl(this));
        int x = RightToLeft == RightToLeft.Yes ? at.Right - box.Width : at.Left;
        int y = at.Bottom + LogicalToDeviceUnits(2);
        if (y + box.Height > area.Bottom) y = at.Top - LogicalToDeviceUnits(2) - box.Height;
        box.Location = new Point(Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - box.Width)), y);
        box.Show(this);
        box.StartTyping();
    }

    // ---- Pages ----

    internal int Page => page;

    internal WelcomeChoices Choices => choices;

    internal Situation Previewing => page == 3 ? on : Situation.None;

    internal Moment Moment => moment;

    internal WelcomeStage Stage => stage;

    internal WelcomeTimeline Timeline => timeline;

    internal bool HasBatteryShown => !stillUsing.Hidden.Contains(batteryQuestion);

    // The parts on show on a page, top to bottom.
    Control[] PartsOf(int which) => which switch
    {
        1 => [first],
        2 => [stage, timeline, locking],
        3 => [stage, timeline, stillUsing],
        4 => [awayPage],
        _ => [setPage],
    };

    internal void GoTo(int which)
    {
        which = Math.Clamp(which, 1, PageCount);
        StopPlaying();
        StopTrying();
        page = which;
        moment = which == 2 ? (choices.Warns ? Moment.Warning : Moment.Locked) : Moment.Idle;
        if (which == 5) FillPicked();
        ShowMoment();
        footer.Visible = which > 1;
        footer.Hidden.Clear();
        if (which == 5) footer.Hidden.UnionWith([skip, next]);
        else footer.Hidden.UnionWith([openSettings, done]);
        dots.Show(which - 1, PageCount - 1, string.Format(text.V1.StepOf, which - 1, PageCount - 1));
        footer.PerformLayout();
        Arrange();
        AcceptButton = which switch
        {
            1 => startButton,
            5 => done,
            _ => next,
        };
        if (loaded) FocusAfterMove();
    }

    // After a move the keyboard stays on the button pressed, or goes to the page's main button when
    // that one is gone.
    void FocusAfterMove()
    {
        var focused = ActiveControl;
        bool gone = focused is null || focused == startButton && page != 1
            || focused is KitButton b && footer.Hidden.Contains(b)
            || !IsShownOnPage(focused);
        if (gone && AcceptButton is Control main) main.Focus();
    }

    bool IsShownOnPage(Control control)
    {
        if (control.FindForm() != this) return false;
        for (var c = control; c is not null && c != this; c = c.Parent)
        {
            if (c == footer) return page > 1;
            if (c.Parent == body || c.Parent == head) return PartsOf(page).Contains(c);
        }
        return true;
    }

    // ---- The stage and the timeline ----

    void ShowMoment()
    {
        if (page is not (2 or 3)) return;
        var shown = Previewing;
        if (choices.Preview(shown).NeverLocks) moment = Moment.Idle;
        if (moment == Moment.Warning && !choices.Warns && tryLeft == 0) moment = Moment.Locked;
        bool previewing = tryLeft > 0;
        stage.Show(choices, shown, page, previewing ? Moment.Warning : moment, previewing, previewing ? tryLeft : choices.Warning);
        timeline.Show(choices, shown, page, previewing ? Moment.Warning : moment, playOrder is null ? null : timeline.Playhead);
        head.PerformLayout();
        frame.PerformLayout();
    }

    internal bool Pinned => frame.Pinned;

    // The page's parts under the stage, and the stage and the timeline when it has them.
    void Arrange()
    {
        var parts = PartsOf(page);
        body.Use(parts.Where(part => part != stage && part != timeline).ToArray());
        frame.Staged = parts.Contains(stage);
    }

    // How much of the third page's questions the window makes room for under them, at 100%.
    const int ThirdPageRoom = 420;

    // Try it: the app shows its countdown note for a few seconds, which never locks, and the stage
    // shows it counting down too.
    void TryIt()
    {
        if (!choices.Warns) return;
        StopPlaying();
        host?.TryItChosen();
        tryLeft = Toast.PreviewSeconds;
        tryTimer.Stop();
        tryTimer.Start();
        ShowMoment();
    }

    internal bool Trying => tryLeft > 0;

    internal void TryTick()
    {
        if (tryLeft <= 0) return;
        tryLeft--;
        if (tryLeft == 0)
        {
            tryTimer.Stop();
            moment = Moment.Warning;
        }
        ShowMoment();
    }

    void StopTrying()
    {
        tryTimer.Stop();
        if (tryLeft == 0) return;
        tryLeft = 0;
        ShowMoment();
    }

    internal bool Playing => playOrder is not null;

    void TogglePlay()
    {
        if (Playing)
        {
            StopPlaying();
            return;
        }
        StopTrying();
        playOrder = choices.PlayOrder(Previewing);
        playIndex = 0;
        moment = playOrder[0];
        stage.Play.Text = text.V1.StopPlaying;
        stage.Play.Glyph = Glyphs.Pause;
        stage.Play.NameChanged();
        ShowMoment();
        timeline.MovePlayhead(timeline.Line.X(moment));
        if (playOrder.Count > 1) playTimer.Start();
        else StopPlaying();
    }

    // The next moment, held for PlayHold; after the last one has been held as long, Play stops and
    // the last moment stays on show.
    internal void PlayTick()
    {
        if (playOrder is null) return;
        if (playIndex + 1 >= playOrder.Count)
        {
            StopPlaying();
            return;
        }
        var was = moment;
        playIndex++;
        moment = playOrder[playIndex];
        ShowMoment();
        float to = timeline.Line.X(moment);
        if (Motion())
        {
            glideFrom = timeline.Line.X(was);
            glideTo = to;
            glideStart = Environment.TickCount64;
            timeline.MovePlayhead(glideFrom);
            glideTimer.Start();
        }
        else timeline.MovePlayhead(to);
    }

    void GlideTick()
    {
        float done = Math.Clamp((Environment.TickCount64 - glideStart) / (float)GlideTime, 0, 1);
        float eased = 1 - (1 - done) * (1 - done);
        timeline.MovePlayhead(glideFrom + (glideTo - glideFrom) * eased);
        if (done >= 1) glideTimer.Stop();
    }

    void StopPlaying()
    {
        playTimer.Stop();
        glideTimer.Stop();
        if (playOrder is null) return;
        playOrder = null;
        timeline.MovePlayhead(null);
        stage.Play.Text = text.V1.Play;
        stage.Play.Glyph = Glyphs.Play;
        stage.Play.NameChanged();
        stage.PerformLayout();
    }

    // ---- The summary ----

    void FillPicked()
    {
        var v = text.V1;
        var still = new List<(string, string)>
        {
            (v.MusicVideo, WelcomeText.ExtraLabel(text, choices.Music)),
            (v.OnCall, WelcomeText.ExtraLabel(text, choices.Call)),
            (v.FullScreen, WelcomeText.ExtraLabel(text, choices.FullScreen)),
        };
        if (hasBattery) still.Add((v.OnBattery, WelcomeText.BatteryLabel(text, choices, choices.Battery)));
        picked.ShowGroups(
        [
            (v.LockingTab, new[]
            {
                (v.LockAfter, WelcomeText.LockLabel(text, choices.LockAfter)),
                (v.WarnMe, WelcomeText.WarningLabel(text, choices.Warning)),
                (v.Shortcut, WelcomeText.ShortcutLabel(text, choices.Shortcut)),
            }),
            (v.StillUsing, still),
            (v.AwayAndBack, new[]
            {
                (v.MediaOnLock, WelcomeText.MediaLabel(text, choices.Media)),
                (v.Mute, WelcomeText.OnOff(text, choices.Mute)),
                (v.ScreenOff, WelcomeText.OnOff(text, choices.ScreenOff)),
                (v.ShowTimeAway, WelcomeText.OnOff(text, choices.ShowTimeAway)),
                (v.BreakReminders, WelcomeText.BreakLabel(text, choices.BreakReminders, choices.BreakMinutes)),
            }),
        ]);
    }

    internal IEnumerable<string> PickedLines => picked.Rows.Select(row => row.AccessibleName ?? "");

    // ---- The window ----

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Fit();
        CenterToScreen();
        loaded = true;
        if (AcceptButton is Control main) main.Select();
    }

    // As wide as the pages are drawn for, and as tall as the tallest page, no taller than the screen;
    // a page that does not fit scrolls. The questions of the third page scroll under the stage and
    // the timeline held above them, so that page asks only for room to show a good part of them.
    internal void Fit()
    {
        int width = LogicalToDeviceUnits(800);
        int footerHeight = footer.GetPreferredSize(Size.Empty).Height;
        footer.Height = footerHeight;
        int tallest = 0;
        int was = page;
        var wasMoment = moment;
        for (int p = 1; p <= PageCount; p++)
        {
            page = p;
            moment = p == 2 ? Moment.Warning : Moment.Idle;
            if (p is 2 or 3)
            {
                stage.Show(choices, p == 3 ? on : Situation.None, p, moment, false, choices.Warning);
                timeline.Show(choices, p == 3 ? on : Situation.None, p, moment);
            }
            int height = p == 3
                ? WelcomeBody.Measure(width, [stage, timeline]) + LogicalToDeviceUnits(ThirdPageRoom)
                : WelcomeBody.Measure(width, PartsOf(p));
            if (p == 3) height = Math.Min(height, WelcomeBody.Measure(width, PartsOf(p)));
            tallest = Math.Max(tallest, height + (p > 1 ? footerHeight : 0));
        }
        page = was;
        moment = wasMoment;
        ShowMoment();
        var area = WorkArea.Of(Screen.FromControl(this));
        int border = Height - ClientSize.Height;
        int room = area.Height - border - LogicalToDeviceUnits(24);
        ClientSize = new Size(width, Math.Max(LogicalToDeviceUnits(360), Math.Min(tallest, room)));
        frame.PerformLayout();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (!loaded) return;
        BeginInvoke(() =>
        {
            if (IsDisposed) return;
            Fit();
            var area = WorkArea.Of(Screen.FromControl(this));
            if (Bottom > area.Bottom) Top = Math.Max(area.Top, area.Bottom - Height);
        });
    }

    // The wheel scrolls the page under the stage wherever the keyboard is: after a move to a page it
    // is on Next, in the buttons below the page, which pass the wheel on to the window and no
    // further.
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e is HandledMouseEventArgs { Handled: true }) return;
        ScrollPage(-e.Delta / (float)SystemInformation.MouseWheelScrollDelta);
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
    }

    // What scrolls: the page, or all of it when the window is too short to hold the stage up.
    internal ScrollableControl PageScroller => frame.Staged && !frame.Pinned ? frame : body;

    // Scrolls the page, down for a positive number of notches of the wheel. The stage and the timeline
    // stay put while they are held above the page; when the window is too short for that, they
    // scroll with it.
    internal void ScrollPage(float notches)
    {
        var target = PageScroller;
        var at = target.AutoScrollPosition;
        target.AutoScrollPosition = new Point(-at.X, -at.Y + (int)Math.Round(notches * LogicalToDeviceUnits(48)));
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void End(bool openSettings)
    {
        Finish(openSettings);
        Close();
    }

    // For quitting the app: the window closes without its choices, and shows again next time.
    public void CloseWithoutFinishing()
    {
        finished = true;
        StopTimers();
        Close();
    }

    // Closing the window any other way keeps what is chosen, as Done does.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!finished) Finish(openSettings: false);
        base.OnFormClosing(e);
    }

    void Finish(bool openSettings)
    {
        if (finished) return;
        finished = true;
        StopTimers();
        Finished?.Invoke(choices.ApplyTo(start), choices.StartWithWindows, openSettings);
    }

    void StopTimers()
    {
        tryTimer.Stop();
        playTimer.Stop();
        glideTimer.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tryTimer.Dispose();
            playTimer.Dispose();
            glideTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
