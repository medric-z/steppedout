namespace SteppedOut;

// What the PC can be doing while nobody touches the mouse or keyboard, as the welcome's stage
// previews it. Battery is being unplugged.
[Flags]
enum Situation
{
    None = 0,
    Music = 1,
    Call = 2,
    FullScreen = 4,
    Battery = 8,
}

// The five moments along the welcome's timeline, in the order they come.
enum Moment
{
    Idle,
    Warning,
    Locked,
    Away,
    Back,
}

// What the drawn PC shows at a moment: the parts of its picture.
[Flags]
enum PcPart
{
    None = 0,
    App = 1,
    Media = 2,
    Playing = 4,
    Call = 8,
    FullScreen = 16,
    Note = 32,
    Lock = 64,
    Off = 128,
    Toast = 256,
    Cursor = 512,
    Muted = 1024,
    Laptop = 2048,
    Keys = 4096,
}

// What the welcome window asks, as it shows it: read from the settings in use, and handed back into
// them as they are. Nothing else in the settings changes.
sealed class WelcomeChoices
{
    // The Custom… boxes take whole numbers in these ranges.
    public const int ShortestLock = TimeChoices.Shortest;
    public const int LongestLock = TimeChoices.Longest;

    // The welcome's one countdown.
    public const int CountdownSeconds = 15;

    public double LockAfter { get; set; } = Config.DefaultIdleMinutes;

    // Seconds of warning before the lock; 0 for none.
    public int Warning { get; set; } = CountdownSeconds;

    // In its usual form, "" for none.
    public string Shortcut { get; set; } = "";

    public Effect Music { get; set; } = Effect.Never;
    public Effect Call { get; set; } = Effect.Never;
    public Effect FullScreen { get; set; } = Effect.Never;
    public Effect Battery { get; set; } = Effect.None;
    public MediaOnLock Media { get; set; } = MediaOnLock.PauseUntilBack;
    public bool Mute { get; set; }
    public bool ScreenOff { get; set; }
    public bool ShowTimeAway { get; set; } = true;

    // Break reminders, and the time at the PC before each. Off keeps the time, as the settings do.
    public bool BreakReminders { get; set; } = true;
    public int BreakMinutes { get; set; } = Config.FirstStartBreakMinutes;

    public bool StartWithWindows { get; set; } = true;

    public static WelcomeChoices From(Config c, bool startsWithWindows) => new()
    {
        LockAfter = c.IdleMinutes,
        Warning = c.GraceSeconds,
        Shortcut = SteppedOut.Shortcut.Normalize(c.LockShortcut),
        Music = c.MusicVideo,
        Call = c.OnCall,
        FullScreen = c.FullScreen,
        Battery = c.OnBattery,
        Media = c.MediaOnLock,
        Mute = c.MuteWhenAway,
        ScreenOff = c.ScreensOffWhenAway,
        ShowTimeAway = c.ShowTimeAway,
        BreakReminders = c.BreakReminders,
        BreakMinutes = c.BreakMinutes,
        StartWithWindows = startsWithWindows,
    };

    // The settings with these choices in them, welcomed, and brought into range as any are.
    public Config ApplyTo(Config start)
    {
        var c = start.Copy();
        c.IdleMinutes = LockAfter;
        c.GraceSeconds = Warning;
        c.LockShortcut = Shortcut;
        c.MusicVideo = Music;
        c.OnCall = Call;
        c.FullScreen = FullScreen;
        c.OnBattery = Battery;
        c.MediaOnLock = Media;
        c.MuteWhenAway = Mute;
        c.ScreensOffWhenAway = ScreenOff;
        c.ShowTimeAway = ShowTimeAway;
        c.BreakReminders = BreakReminders;
        c.BreakMinutes = BreakMinutes;
        c.Welcomed = true;
        c.Clamp();
        return c;
    }

    public bool Warns => Warning > 0;

    public bool HasShortcut => Shortcut.Length > 0;

    public Effect EffectOf(Situation one) => one switch
    {
        Situation.Music => Music,
        Situation.Call => Call,
        Situation.FullScreen => FullScreen,
        Situation.Battery => Battery,
        _ => Effect.None,
    };

    public void SetEffect(Situation one, Effect effect)
    {
        switch (one)
        {
            case Situation.Music: Music = effect; break;
            case Situation.Call: Call = effect; break;
            case Situation.FullScreen: FullScreen = effect; break;
            case Situation.Battery: Battery = effect; break;
        }
    }

    static readonly (Situation One, EffectSource Source)[] Sources =
    {
        (Situation.Music, EffectSource.MusicOrVideo),
        (Situation.Call, EffectSource.Call),
        (Situation.FullScreen, EffectSource.FullScreen),
        (Situation.Battery, EffectSource.Battery),
    };

    // The lock time while these things go on, worked out as the app works it out.
    public LockTime Preview(Situation on)
    {
        var effects = new List<ActiveEffect>();
        foreach (var (one, source) in Sources)
        {
            if (on.HasFlag(one)) effects.Add(new ActiveEffect(EffectOf(one), source));
        }
        return LockTime.Compute(LockTime.NormalMinutes(LockAfter), effects);
    }

    // The lock time on battery alone with a cut of this many percent, for the battery choices.
    public decimal OnBatteryWith(int percent)
    {
        decimal normal = LockTime.NormalMinutes(LockAfter);
        return LockTime.Compute(normal, [new ActiveEffect(Effect.Sooner(percent), EffectSource.Battery)]).Total ?? normal;
    }

    // ---- The drawn PC ----

    // The picture at a moment, with these things going on. page is the welcome's page (2 shows the
    // shortcut's keys on the desk). previewing is a Try it countdown on show.
    public PcPart Scene(Situation on, Moment moment, int page, bool previewing = false)
    {
        var parts = PcPart.None;
        var time = Preview(on);
        if (on.HasFlag(Situation.Battery)) parts |= PcPart.Laptop;
        if (page == 2 && HasShortcut) parts |= PcPart.Keys;
        bool music = on.HasFlag(Situation.Music);
        if (previewing) moment = Moment.Warning;
        switch (moment)
        {
            case Moment.Idle or Moment.Warning:
                parts |= PcPart.App | PcPart.Cursor;
                if (on.HasFlag(Situation.Call)) parts |= PcPart.Call;
                if (on.HasFlag(Situation.FullScreen)) parts |= PcPart.FullScreen;
                if (music) parts |= PcPart.Media | PcPart.Playing;
                if (moment == Moment.Warning && (previewing || Warns && !time.NeverLocks)) parts |= PcPart.Note;
                break;
            case Moment.Locked or Moment.Away:
                parts |= moment == Moment.Away && ScreenOff ? PcPart.Off : PcPart.Lock;
                if (music) parts |= PcPart.Media | (Media == MediaOnLock.KeepPlaying ? PcPart.Playing : PcPart.None);
                if (Mute) parts |= PcPart.Muted;
                break;
            default:
                parts |= PcPart.App | PcPart.Cursor;
                if (music) parts |= PcPart.Media | (Media == MediaOnLock.PauseStayPaused ? PcPart.None : PcPart.Playing);
                if (ShowTimeAway) parts |= PcPart.Toast;
                break;
        }
        return parts;
    }

    // What came new at a moment, for the stage to point out: the parts it shows that the moment
    // before it did not. A sound that stops or comes back counts too.
    public PcPart Changed(Situation on, Moment moment, int page)
    {
        if (moment == Moment.Idle) return PcPart.None;
        var before = Before(moment);
        var now = Scene(on, moment, page);
        var was = Scene(on, before, page);
        var fresh = now & ~was;
        if ((was & PcPart.Playing) != (now & PcPart.Playing) && now.HasFlag(PcPart.Media)) fresh |= PcPart.Media;
        return fresh & Pointed;
    }

    // The parts the stage rings when they come: the countdown note, the lock screen, the screen
    // going off, the note on the way back, the music stopping or starting, and the sound muted.
    const PcPart Pointed = PcPart.Note | PcPart.Lock | PcPart.Off | PcPart.Toast | PcPart.Media | PcPart.Muted;

    // The moment before this one along the line, skipping a warning there is none of.
    public Moment Before(Moment moment) => moment switch
    {
        Moment.Locked when Warns => Moment.Warning,
        Moment.Locked or Moment.Warning => Moment.Idle,
        Moment.Away => Moment.Locked,
        Moment.Back => Moment.Away,
        _ => Moment.Idle,
    };

    // The moments Play walks through: up to the lock, or only the first while it never locks.
    public IReadOnlyList<Moment> PlayOrder(Situation on)
    {
        if (Preview(on).NeverLocks) return [Moment.Idle];
        var order = new List<Moment> { Moment.Idle };
        if (Warns) order.Add(Moment.Warning);
        order.AddRange([Moment.Locked, Moment.Away, Moment.Back]);
        return order;
    }
}

// The welcome's lines that are put together from the choices: the Right now block, the labels of
// chosen values, and the summary.
static class WelcomeText
{
    // A setting's time as a list item says it, "45 minutes", or a fraction from the file as the
    // status does, "30 sec".
    public static string Duration(Strings text, decimal minutes) =>
        minutes >= 1 && minutes == Math.Floor(minutes) && minutes <= int.MaxValue ? text.OptionDuration((int)minutes) : text.ShortDuration(minutes);

    // "Locks after 45 minutes", or "Never locks during a call".
    public static string Headline(Strings text, LockTime time) => time.NeverCause is EffectSource cause
        ? string.Format(text.V1.NeverLocksDuring, text.SourcePhrase(cause))
        : string.Format(text.V1.LocksAfter, Duration(text, time.Total ?? time.Normal));

    // The line under it: on battery with a cut, that every time is that much sooner; otherwise what
    // makes up the time, "15 min + 30 min (music or video)", or nothing when it is the lock time alone.
    public static string Detail(Strings text, WelcomeChoices choices, Situation on, LockTime time)
    {
        if (time.NeverLocks) return "";
        if (on.HasFlag(Situation.Battery) && choices.Battery is { Kind: EffectKind.Sooner, Amount: > 0 } cut && !time.CapApplied)
            return string.Format(text.V1.BatterySooner, cut.Amount);
        return text.Why(time);
    }

    // The lock time chosen, as its chip or the summary says it.
    public static string LockLabel(Strings text, double minutes) => Duration(text, LockTime.NormalMinutes(minutes));

    public static string WarningLabel(Strings text, int seconds) => seconds switch
    {
        <= 0 => text.V1.NoWarning,
        WelcomeChoices.CountdownSeconds => text.V1.Countdown15,
        _ => text.SecondsOption(seconds),
    };

    public static string ShortcutLabel(Strings text, string shortcut) => shortcut.Length == 0 ? text.V1.Off : shortcut;

    public static string ExtraLabel(Strings text, Effect effect) => text.EffectLabel(effect, text.V1.NoExtraTime);

    // "50% sooner (8 min)", with the real time on battery; Lock as usual has none.
    public static string BatteryLabel(Strings text, WelcomeChoices choices, Effect effect) => effect.Kind == EffectKind.Sooner
        ? text.SoonerLabel(effect.Amount) + " " + text.PercentNote(choices.OnBatteryWith(effect.Amount))
        : text.EffectLabel(effect, text.V1.LockAsUsual);

    public static string MediaLabel(Strings text, MediaOnLock media) => media switch
    {
        MediaOnLock.PauseStayPaused => text.V1.PauseAndStayPaused,
        MediaOnLock.KeepPlaying => text.V1.KeepPlaying,
        _ => text.V1.PauseUntilBack,
    };

    public static string OnOff(Strings text, bool on) => on ? text.V1.On : text.V1.Off;

    // The break reminders chosen: Off, or the time at the PC before each, "2 hours".
    public static string BreakLabel(Strings text, bool on, int minutes) => on ? text.OptionDuration(minutes) : text.V1.Off;

    // The timeline's name for a moment.
    public static string Stop(Strings text, Moment moment, bool warns = true) => moment switch
    {
        Moment.Idle => text.V1.StopIdle,
        Moment.Warning => warns ? text.V1.StopWarning : text.V1.NoWarning,
        Moment.Locked => text.V1.StopLocks,
        Moment.Away => text.V1.StopAway,
        _ => text.V1.StopBack,
    };

    // What happens at a moment, under its name beside the drawn PC.
    public static string MomentDetail(Strings text, Moment moment) => moment switch
    {
        Moment.Idle => text.V1.MomentIdle,
        Moment.Warning => text.V1.WarnMeDetail,
        Moment.Locked => text.V1.MomentLocks,
        Moment.Away => text.V1.MomentAway,
        _ => text.V1.MomentBack,
    };
}
