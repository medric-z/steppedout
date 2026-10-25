namespace SteppedOut;

// Numbers and the lock status put into the version 1 lines. Fixed list items, such as "1 hour"
// (ساعة واحدة), always come from the approved lines; the counting rules only make the values a
// list doesn't hold, such as a time typed with Custom… or set in the settings file.
sealed partial class Strings
{
    V1Words Words => V1.Words;

    // "28 min", "1 hr 20 min": a lock time or a setting, to the nearest whole minute, and under a
    // minute in seconds, so a test run with --idle 0.5 reads "30 sec". standalone: Arabic ساعتان
    // and دقيقتان, for a time after "+ " in the why line.
    public string ShortDuration(decimal minutes, bool standalone = false)
    {
        if (minutes < 1) return Words.Seconds((int)Math.Max(1, Math.Round(minutes * 60, MidpointRounding.AwayFromZero)), true, standalone);
        int whole = (int)Math.Min(int.MaxValue, Math.Round(minutes, MidpointRounding.AwayFromZero));
        return Words.Time(whole / 60, whole % 60, true, standalone);
    }

    // Time away, in the whole minutes gone: "42 min", and under a minute in seconds.
    public string ShortDuration(TimeSpan time)
    {
        if (time.TotalMinutes < 1) return Words.Seconds(Math.Max(1, (int)time.TotalSeconds), true, false);
        int whole = (int)Math.Min(int.MaxValue, time.TotalMinutes);
        return Words.Time(whole / 60, whole % 60, true, false);
    }

    // Time left, rounded up, so a full 15 minutes reads 15 rather than 14: "28 min", "24 sec".
    public string ShortLeft(double seconds)
    {
        int whole = (int)Math.Clamp(Math.Ceiling(seconds), 1, int.MaxValue);
        if (whole < 60) return Words.Seconds(whole, true, false);
        int minutes = (int)Math.Ceiling(whole / 60.0);
        return Words.Time(minutes / 60, minutes % 60, true, false);
    }

    // "Away today: 1 hr 20 min", starting from "0 min". The time stands alone after the label's
    // colon, so Arabic takes ساعتان and دقيقتان there.
    public string AwayToday(TimeSpan away)
    {
        int whole = (int)Math.Clamp(away.TotalMinutes, 0, int.MaxValue);
        return Words.Time(whole / 60, whole % 60, true, standalone: true);
    }

    // "Locked today: 3 times"; Arabic shows the number alone.
    public string Times(int count) => count == 1 ? V1.OneTime : string.Format(V1.TimesCount, count);

    // A length of time as a list item: "45 minutes", "1 hour 30 minutes".
    public string OptionDuration(int minutes) => minutes switch
    {
        1 => V1.Minute1,
        5 => V1.Minutes5,
        10 => V1.Minutes10,
        15 => V1.Minutes15,
        30 => V1.Minutes30,
        50 => V1.Minutes50,
        60 => V1.Hour1,
        90 => V1.Minutes90,
        120 => V1.Hours2,
        _ => Long(minutes, standalone: true),
    };

    // The countdown before the lock as a list item: "15 seconds", "1 minute".
    public string SecondsOption(int seconds) => seconds switch
    {
        10 => V1.Seconds10,
        15 => V1.Seconds15,
        30 => V1.Seconds30,
        60 => V1.Minute1,
        _ when seconds > 0 && seconds % 60 == 0 => OptionDuration(seconds / 60),
        _ => Words.Seconds(seconds, false, true),
    };

    // The pause choices the tray menu and the status box share: 15 minutes, 30 minutes, 1 hour,
    // 2 hours, 1 day, and null for until turned back on.
    public string PauseChoice(int? minutes) => minutes is int m ? PauseLength(m) : V1.UntilTurnedBackOn;

    // A pause length: whole days as days ("1 day", "7 days"; Arabic يومان، 3 أيام), anything else
    // as a list item says it.
    public string PauseLength(int minutes)
    {
        const int day = 24 * 60;
        if (minutes <= 0 || minutes % day != 0) return OptionDuration(minutes);
        int days = minutes / day;
        return days == 1 ? V1.Day1 : Words.Days(days, standalone: true);
    }

    // "+1 hour"; Arabic puts the time as a list item says it: +ساعة، +ساعتان.
    public string ExtraLabel(int minutes) => minutes switch
    {
        15 => V1.Extra15,
        30 => V1.Extra30,
        60 => V1.ExtraHour1,
        120 => V1.ExtraHours2,
        _ => string.Format(V1.CustomExtra, Long(minutes, standalone: true)),
    };

    public string SoonerLabel(int percent) => percent switch
    {
        25 => V1.Sooner25,
        50 => V1.Sooner50,
        75 => V1.Sooner75,
        _ => string.Format(V1.CustomSooner, percent),
    };

    public string AlwaysAfterLabel(int minutes) => minutes switch
    {
        2 => V1.AlwaysAfter2,
        5 => V1.AlwaysAfter5,
        10 => V1.AlwaysAfter10,
        15 => V1.AlwaysAfter15,
        30 => V1.AlwaysAfter30,
        60 => V1.AlwaysAfterHour1,
        _ => string.Format(V1.CustomAlwaysAfter, Long(minutes, standalone: false)),
    };

    // The real time beside a "sooner" item: "50% sooner (15 min)". It stands alone in the item, so
    // Arabic says it as the other items do: (ساعتان), not (ساعتين).
    public string PercentNote(decimal minutes) => "(" + ShortDuration(minutes, standalone: true) + ")";

    public string SilenceLabel(int seconds) => seconds switch
    {
        10 => V1.Seconds10,
        30 => V1.Seconds30,
        60 => V1.Minute1,
        _ when seconds > 0 && seconds % 60 == 0 => string.Format(V1.CustomSilence, Long(seconds / 60, standalone: true)),
        _ => string.Format(V1.CustomSilence, Words.Seconds(seconds, false, true)),
    };

    // Tiny mouse movements left out: "Under 5 pixels", and Off for none.
    public string JitterLabel(int pixels) => pixels <= 0 ? V1.Off : pixels == 1 ? V1.UnderOnePixel : string.Format(V1.UnderPixels, pixels);

    // How loud sound must be, as a percent of full volume: "3%", and "0.2%" for a level set in the file.
    public string PercentLabel(double percent) =>
        string.Format(V1.PercentCount, percent.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));

    // A log size: whole megabytes as MB ("1 MB"), anything else as KB ("256 KB").
    public string LogSizeLabel(int kilobytes) =>
        kilobytes > 0 && kilobytes % 1024 == 0 ? string.Format(V1.MegabytesCount, kilobytes / 1024) : string.Format(V1.KilobytesCount, kilobytes);

    // A number with the unit a Custom… box counts in, for the line under the box: "1 pixel", "4%",
    // "64 KB", "45 seconds", "1 hour 30 minutes".
    public string UnitCount(TimeUnit unit, int n) => unit switch
    {
        TimeUnit.Pixels => n == 1 ? V1.OnePixel : string.Format(V1.PixelsCount, n),
        TimeUnit.Percent => string.Format(V1.PercentCount, n),
        TimeUnit.Kilobytes => string.Format(V1.KilobytesCount, n),
        TimeUnit.Seconds => SecondsOption(n),
        _ => OptionDuration(n),
    };

    public string SnoozeLabel(int minutes) => minutes switch
    {
        10 => V1.Later10,
        30 => V1.Later30,
        _ => string.Format(V1.CustomLater, Long(minutes, standalone: false)),
    };

    string Long(int minutes, bool standalone) => Words.Time(minutes / 60, minutes % 60, false, standalone);

    // The welcome-back note: "You were away 42 min".
    public string YouWereAwayFor(TimeSpan away) => string.Format(V1.YouWereAway, ShortDuration(away));

    // The break note's second line: "· 1 hr at the PC".
    public string AtThePcFor(TimeSpan used) => string.Format(V1.AtThePc, ShortDuration(used));

    // "Version 1.0.0"
    public string VersionLine(string version) => string.Format(V1.Version, version);
}
