namespace SteppedOut;

// The lock status, and what changes the lock time, in words: the status box, the tray, the notes
// and the Why window.
sealed partial class Strings
{
    // What a setting does to the lock time, as its list shows it. noneText is the list's own
    // first item: "No extra time", or "Lock as usual" on battery.
    public string EffectLabel(Effect effect, string noneText) => effect.Kind switch
    {
        EffectKind.Never => V1.NeverLock,
        EffectKind.Extra => ExtraLabel(effect.Amount),
        EffectKind.Sooner => SoonerLabel(effect.Amount),
        EffectKind.Cap => AlwaysAfterLabel(effect.Amount),
        _ => noneText,
    };

    // "during music or video", "while obs64 is open": what an effect comes from, after a verb.
    public string SourcePhrase(EffectSource source) => source.Kind switch
    {
        SourceKind.MusicOrVideo => V1.DuringMusicVideo,
        SourceKind.FullScreen => V1.InFullScreen,
        SourceKind.Call => V1.DuringCall,
        SourceKind.App => string.Format(V1.WhileAppOpen, source.App),
        _ => V1.OnBatteryPower,
    };

    // "music or video", "obs64": what an extra comes from, in brackets.
    public string SourceNoun(EffectSource source) => source.Kind switch
    {
        SourceKind.MusicOrVideo => V1.MusicVideoNoun,
        SourceKind.FullScreen => V1.FullScreenNoun,
        SourceKind.Call => V1.CallNoun,
        SourceKind.App => source.App,
        _ => V1.BatteryNoun,
    };

    // The status box headline: "On · locks in 28 min". lockingOn is false while "Lock the PC when
    // you're away" is off, when the time counts down to the away actions instead.
    public string Headline(LockStatus status, bool lockingOn = true)
    {
        bool locking = lockingOn && status.Kind != LockState.LockingOff;
        string line = status.Kind switch
        {
            LockState.Starting => V1.Starting,
            LockState.On or LockState.Countdown or LockState.LockingOff when !status.Time.NeverLocks =>
                string.Format(locking ? V1.OnLocksIn : V1.LockingOffAwayIn, ShortLeft(status.SecondsLeft)),
            LockState.On or LockState.Never or LockState.Countdown or LockState.LockingOff => WontLock(status.Time, locking),
            LockState.Paused => status.PausedUntil is DateTime until ? PausedLine(until, LocalNow()) : V1.PausedUntilTurnedBackOn,
            LockState.Away => V1.YoureAway,
            _ => V1.PcIsLocked,
        };
        return status.DryRun ? string.Format(V1.DryRun, line) : line;
    }

    // The local time now, for saying which day a pause ends; the tests set their own.
    internal static Func<DateTime> LocalNow = () => DateTime.Now;

    // "Paused · back on at 5:12 PM" when the pause ends today, "… back on tomorrow at 5:12 PM",
    // and later "… back on Saturday at 5:12 PM". A pause can be a week long, which would end on
    // today's own day name, so from a week on it names the date instead.
    public string PausedLine(DateTime until, DateTime now)
    {
        int days = (until.Date - now.Date).Days;
        string time = ClockTime(until);
        if (days <= 0) return string.Format(V1.PausedUntil, time);
        if (days == 1) return string.Format(V1.PausedUntilTomorrow, time);
        var culture = DayCulture();
        string day = days < 7 ? culture.DateTimeFormat.GetDayName(until.DayOfWeek) : until.ToString(culture.DateTimeFormat.MonthDayPattern, culture);
        return string.Format(V1.PausedUntilDay, day, time);
    }

    // The culture that names days and dates in this language: Windows' own when it is in the same
    // language, otherwise a plain one of it, always on the Gregorian calendar the clock time uses.
    System.Globalization.CultureInfo DayCulture()
    {
        string language = RightToLeft ? "ar" : "en";
        var windows = System.Globalization.CultureInfo.CurrentCulture;
        var culture = windows.TwoLetterISOLanguageName == language ? windows : System.Globalization.CultureInfo.GetCultureInfo(RightToLeft ? "ar-EG" : "en-US");
        if (culture.DateTimeFormat.Calendar is System.Globalization.GregorianCalendar) return culture;
        var copy = (System.Globalization.CultureInfo)culture.Clone();
        var gregorian = copy.OptionalCalendars.OfType<System.Globalization.GregorianCalendar>().FirstOrDefault();
        if (gregorian is null) return System.Globalization.CultureInfo.GetCultureInfo(RightToLeft ? "ar-EG" : "en-US");
        copy.DateTimeFormat.Calendar = gregorian;
        return copy;
    }

    string WontLock(LockTime time, bool locking)
    {
        if (time.NeverCause is not EffectSource cause) return V1.WontLockNow;
        return string.Format(locking ? V1.WontLock : V1.LockingOffWontStart, SourcePhrase(cause));
    }

    // The sentence under it, which always names the normal time.
    public string Sentence(LockStatus status, bool lockingOn = true) =>
        string.Format(lockingOn && status.Kind != LockState.LockingOff ? V1.LocksIfIdleFor : V1.AwayActionsIfIdleFor, ShortDuration(status.Time.Normal));

    public string Why(LockStatus status) => Why(status.Time);

    // What makes the lock time differ from the normal time, or "" when nothing does:
    // "30 min + 1 hr (music or video) + 2 hr (full screen)", "30 min, 50% sooner on battery",
    // "Always locks after 10 min on battery". A never is said by the headline.
    public string Why(LockTime time)
    {
        if (time.CapApplied && time.Cap is ActiveEffect cap) return string.Format(V1.WhyAlwaysAfter, ShortDuration(cap.Effect.Amount), SourcePhrase(cap.Source));
        if (time.NeverLocks) return "";
        var extras = time.Extras.Where(e => e.Effect.Amount > 0).ToList();
        // Every time here stands alone, as a term in a sum or before a comma, so Arabic says the
        // normal time as it says the extras: ساعتان + ساعة, not ساعتين + ساعة.
        string sum = extras.Count == 0 ? "" :
            ShortDuration(time.Normal, standalone: true) + string.Concat(extras.Select(e => $" + {ShortDuration(e.Effect.Amount, standalone: true)} ({SourceNoun(e.Source)})"));
        if (time.CutApplied && time.Cut is ActiveEffect cut)
            return string.Format(V1.WhySooner, sum == "" ? ShortDuration(time.Normal, standalone: true) : sum, cut.Effect.Amount, SourcePhrase(cut.Source));
        return sum;
    }

    // What hovering the tray icon shows, and the name screen readers give the icon: the app's name,
    // then the headline, "SteppedOut · On · locks in 28 min". The headline is cut with "…" so the
    // whole fits in a tooltip. Arabic is wrapped in right-to-left embedding marks, since the tooltip
    // is laid out left to right; inside them the name reads first, at the right. The room for the
    // marks is kept before the cut, so the closing mark is never the part cut off.
    public string Tooltip(LockStatus status, bool lockingOn = true)
    {
        int room = TooltipLength - (RightToLeft ? 2 : 0) - string.Format(V1.TrayTooltip, "").Length;
        string line = string.Format(V1.TrayTooltip, TrayIcons.Fit(Headline(status, lockingOn), room));
        return RightToLeft ? "\u202B" + line + "\u202C" : line;
    }

    // NotifyIcon.Text takes at most 127 characters.
    public const int TooltipLength = 127;

    // The minutes left on the tray icon, 1 to 99, or null when it shows none: paused, never
    // locking, away, or more than 99 minutes left. The same digits in both languages.
    public static string? IconMinutes(LockStatus status)
    {
        if (status.Kind is not (LockState.On or LockState.Countdown or LockState.LockingOff) || status.Time.NeverLocks) return null;
        double minutes = Math.Max(1, Math.Ceiling(status.SecondsLeft / 60));
        return minutes <= 99 ? ((int)minutes).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    // The countdown note: "Locking in 15 sec · move the mouse or press a key to cancel".
    public string CountdownNote(double secondsLeft, bool lockingOn = true) =>
        string.Format(lockingOn ? V1.CountdownNote : V1.CountdownNoteAway, ShortLeft(secondsLeft));

    // The note Try it shows, which never locks.
    public string PreviewNote(double secondsLeft) => string.Format(V1.PreviewNote, ShortLeft(secondsLeft));

    // The Why window: the lock time now.
    public string WhyLockTime(LockTime time) => time.Total is decimal total ? string.Format(V1.WhyLockTime, ShortDuration(total)) : V1.WhyNeverNow;

    // The Why window: one thing going on, and what it does to the lock time.
    public (string What, string Does) WhyRow(WhyLine line)
    {
        string what = line.Signal switch
        {
            Signal.Sound => line.Detail == "" ? V1.WhySomeSound : string.Format(V1.WhySound, line.Detail),
            Signal.Display => line.Detail == "" ? V1.WhySomeDisplay : string.Format(V1.WhyDisplay, line.Detail),
            Signal.FullScreen => line.Detail == "" ? V1.WhySomeFullScreen : string.Format(V1.WhyFullScreen, line.Detail),
            Signal.Call => line.Detail == "" ? V1.WhySomeCall : string.Format(V1.WhyCall, line.Detail),
            Signal.App => string.Format(V1.WhyApp, line.Detail),
            _ => V1.WhyBattery,
        };
        string does = !line.Counts
            ? line.Signal is Signal.Sound or Signal.Display ? V1.WhyCountedAsCall : V1.WhyNotApplied
            : EffectLabel(line.Effect, V1.WhyNoChange);
        return (what, does);
    }
}
