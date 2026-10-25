using System.Runtime.InteropServices;
using System.Text;

namespace SteppedOut;

// Every piece of text the windows, the tray and the corner box show. English.cs and Arabic.cs each
// fill in one of these; the compiler refuses to build when a language is missing a line.
// The log stays in English whatever the language.
sealed partial class Strings
{
    public required bool RightToLeft { get; init; }

    // The settings text of version 1, generated from tools/text; StringsV1.cs puts numbers in it.
    public required V1Text V1 { get; init; }

    // Counted words, since "1 minute" / "2 minutes" works differently in every language.
    public required Func<int, string> SecondsCount { get; init; }
    public required Func<int, string> MinutesCount { get; init; }
    public required Func<int, string> HoursCount { get; init; }

    // The word after a number typed into a box, which shows the number itself: "minutes".
    public required Func<int, string> MinutesWord { get; init; }

    // Joins hours and minutes: "1 hour 5 minutes".
    public required string And { get; init; }

    // First-run window
    public required string WelcomeTitle { get; init; }
    public required string WelcomeWhere { get; init; }
    public required string Done { get; init; }
    public required string OpenSettingsButton { get; init; }

    // Started again while a copy already runs
    public required string AlreadyRunning { get; init; }

    // config.json was edited by hand and did not load, or holds a name that is not a setting
    public required string ConfigNotUsed { get; init; }
    public required string ConfigDefaultsUntilFixed { get; init; }
    public required string ConfigEarlierUntilFixed { get; init; }
    public required string ConfigSkipped { get; init; }
    public required string ConfigSkippedDetail { get; init; }

    // A time of your own, typed in minutes, in the settings window
    public required string MinutesFieldName { get; init; }
    public required string MinutesRange { get; init; }
    public required string MinutesThatIs { get; init; }
    public required string MinutesClamped { get; init; }
    public required string MinutesNotANumber { get; init; }

    // What a screen reader says about the hand-drawn controls
    public required string OpenAction { get; init; }
    public required string SelectAction { get; init; }
    public required string TurnOn { get; init; }
    public required string TurnOff { get; init; }

    // "42 minutes", "1 hour 5 minutes"
    public string Duration(TimeSpan time)
    {
        if (time.TotalMinutes < 1) return SecondsCount(Math.Max(1, (int)time.TotalSeconds));
        int hours = (int)time.TotalHours;
        int minutes = time.Minutes;
        if (hours == 0) return MinutesCount(minutes);
        return minutes == 0 ? HoursCount(hours) : HoursCount(hours) + And + MinutesCount(minutes);
    }

    // A time from the settings, as a list item says it. Duration counts the whole minutes gone,
    // which suits time away, but would call a hand-set 1.5 minutes "1 minute"; a setting reads to
    // the nearest minute instead, the same in every line that names it. The lines it goes into put
    // it after a colon, where Arabic takes the standalone ساعتان and دقيقتان.
    public string SettingDuration(double minutes) => minutes < 1
        ? SecondsOption((int)Math.Max(1, Math.Round(minutes * 60, MidpointRounding.AwayFromZero)))
        : OptionDuration((int)Math.Min(int.MaxValue, Math.Round(minutes, MidpointRounding.AwayFromZero)));

    // A time of day inside a line of this language, 12 or 24 hour as the taskbar clock shows it.
    // English takes Windows' own format. Arabic writes the 12-hour clock itself, "5:12 م", with
    // Western digits as its other lines have them: Windows' format can be English ("5:12 PM", Latin
    // letters inside an Arabic line) or put a leading zero and the mark first. A 24-hour clock is
    // digits alone, which read the same in both languages, so it is kept as Windows writes it.
    public string ClockTime(DateTime time) => RightToLeft ? ArabicTime(time, Lang.ClockTime(time)) : Lang.ClockTime(time);

    // windows is the time as Windows formats it; a letter in it (AM, PM, ص, م) means a 12-hour clock.
    internal static string ArabicTime(DateTime time, string windows)
    {
        if (!windows.Any(char.IsLetter)) return windows;
        int hour = time.Hour % 12 == 0 ? 12 : time.Hour % 12;
        string minute = time.Minute.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        return $"{hour.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{minute} {(time.Hour < 12 ? "ص" : "م")}";
    }

    // For a second start while a copy runs. That copy opens its settings when it can be asked to, so
    // where to find it is only said when it cannot; options given to the second start never reach it.
    public string AlreadyRunningMessage(bool settingsOpened, string options)
    {
        string message = settingsOpened ? AlreadyRunning : AlreadyRunning + " " + WelcomeWhere;
        return options == "" ? message : message + Environment.NewLine + Environment.NewLine + string.Format(V1.OptionsNotUsed, options);
    }
}

enum Tone
{
    On,
    Busy,
    Off,
}

static class Lang
{
    static Strings? current;

    public static Strings Current => current ??= For("auto");

    public static void Use(string setting) => current = For(setting);

    // Which way windows are laid out in the language shown now.
    public static RightToLeft Direction => Current.RightToLeft ? RightToLeft.Yes : RightToLeft.No;

    // The language Windows itself is shown in, as a Windows language id. The tests set it, so they
    // run the same on an Arabic Windows as on any other.
    internal static Func<ushort> WindowsLanguage { get; set; } = Native.GetUserDefaultUILanguage;

    // "auto" follows the language Windows itself is shown in.
    public static Strings For(string setting) => setting switch
    {
        "en" => English.Text,
        "ar" => Arabic.Text,
        _ => IsArabic(WindowsLanguage()) ? Arabic.Text : English.Text,
    };

    // Whether Windows itself is laid out right to left, which puts the clock and the tray on the left.
    public static bool ShellIsRightToLeft()
    {
        int primary = WindowsLanguage() & PrimaryLanguageMask;
        return primary is LangArabic or LangHebrew or LangUrdu or LangPersian;
    }

    static bool IsArabic(ushort languageId) => (languageId & PrimaryLanguageMask) == LangArabic;

    // A Windows language id has the language in its low ten bits and the regional variant above.
    const int PrimaryLanguageMask = 0x3FF;
    const int LangArabic = 0x01, LangHebrew = 0x0D, LangUrdu = 0x20, LangPersian = 0x29;

    // A time of day as the taskbar clock shows it, 12 or 24 hour as set in Windows. The app is built
    // with invariant globalization, so .NET's own formats would always give the 24-hour clock.
    public static string ClockTime(DateTime time)
    {
        var at = new SystemTime { Year = (ushort)time.Year, Month = (ushort)time.Month, Day = (ushort)time.Day, Hour = (ushort)time.Hour, Minute = (ushort)time.Minute };
        var buffer = new StringBuilder(64);
        return GetTimeFormatEx(null, TimeNoSeconds, ref at, null, buffer, buffer.Capacity) > 0 ? buffer.ToString() : time.ToString("HH:mm");
    }

    const uint TimeNoSeconds = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct SystemTime
    {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    // A null locale is the person's own, with any changes they made to its time format.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetTimeFormatEx(string? locale, uint flags, ref SystemTime time, string? format, StringBuilder buffer, int size);
}
