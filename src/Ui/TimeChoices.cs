using System.Globalization;

namespace SteppedOut;

// The times the windows offer ready-made, and reading a time typed in minutes instead. config.json
// takes any time in range; the welcome shows one that is not ready-made as Custom, the settings
// window as itself.
static class TimeChoices
{
    // A typed time is whole minutes from one minute to a day, the range both settings share.
    public const int Shortest = 1;
    public const int Longest = 24 * 60;

    // "Lock the PC after" in the welcome window. Each is a clear step from the last rather than three
    // neighbours: 5 minutes for an office, 15 for most people (the default, and the usual limit in
    // security guidance), 30 at home, and an hour for someone who only wants it locked once they have
    // really gone. 2 minutes is no longer offered; a 2 saved earlier still loads and shows as itself.
    public static readonly int[] WelcomeLockAfter = { 5, 15, 30, 60 };

    // The settings window's lists, each followed by Custom…. A time that is not one of them, such as
    // 2 minutes saved earlier or one set in the file, shows in the list as itself, never as Custom.
    // "Lock the PC after", in minutes.
    public static readonly int[] LockAfter = { 5, 10, 15, 30, 60 };

    // The extra time for music or video, full screen and calls, in minutes, between "No extra time"
    // and "Never lock", and an app's Wait longer times.
    public static readonly int[] Extras = { 15, 30, 60, 120 };

    // "Warn me before it locks", in seconds, after Off. 15 is the default.
    public static readonly int[] Warning = { 10, 15, 30, 60 };

    // "Show a reminder after" on the Breaks tab, in minutes, then Custom… from ShortestBreak to
    // LongestBreak. Another time from the file, such as 50 from before, shows as itself.
    public static readonly int[] Breaks = { 30, 45, 60, 90, 120, 240 };
    public const int ShortestBreak = 5;
    public const int LongestBreak = Config.MaxBreakMinutes;

    // The snooze button's time on the Breaks tab, in minutes, after Off, then Custom… from 1 to
    // LongestSnooze. The list and Config take the same range.
    public static readonly int[] Snooze = { 5, 10, 15, 30, 60 };
    public const int LongestSnooze = Config.MaxSnoozeMinutes;

    // How long to pause for, in minutes, before "Until turned back on" and Custom…: 15 and 30
    // minutes, 1 and 2 hours, and a day. Custom… takes from a minute to LongestPause.
    public static readonly int[] Pause = { 15, 30, 60, 120, 24 * 60 };
    public const int LongestPause = 7 * 24 * 60;

    // The longest warning the settings take, as Config.Clamp keeps it.
    public const int LongestWarning = 600;

    // The ready-made time a setting is, or null for one shown as Custom: typed earlier, or set in the file.
    public static int? Match(double minutes, IReadOnlyList<int> choices)
    {
        foreach (int choice in choices)
        {
            if (choice == minutes) return choice;
        }
        return null;
    }

    // Reads what is in a minutes box. Only digits make a number, in whichever script they were typed
    // (an Arabic keyboard can give ٤٥ for 45); anything else, or nothing, is not a time. A number
    // outside the range is brought to its nearest end.
    public static TypedTime Read(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0) return default;
        long number = 0;
        foreach (char c in trimmed)
        {
            if (!char.IsDigit(c)) return default;
            // Ten digits are far past the range already; stopping there keeps a long paste from overflowing.
            if (number < 10_000_000_000) number = number * 10 + (long)char.GetNumericValue(c);
        }
        return new TypedTime(true, number, (int)Math.Clamp(number, Shortest, Longest));
    }

    // The line beside a minutes box while typing: what the time comes to, or, when it is not what was
    // typed, the time that is used instead.
    public static string Hint(Strings text, TypedTime typed, double used)
    {
        if (!typed.IsNumber) return string.Format(text.MinutesNotANumber, Words(text, used));
        if (typed.Clamped) return string.Format(text.MinutesClamped, Words(text, typed.Minutes));
        return Describe(text, typed.Minutes);
    }

    // The same line for a time that was not typed: an hour or more, or a fraction from the file, in
    // words; otherwise the range, so it is known before typing.
    public static string Describe(Strings text, double minutes) =>
        minutes >= 60 || minutes != Math.Floor(minutes) ? string.Format(text.MinutesThatIs, Words(text, minutes)) : text.MinutesRange;

    // A time as the box shows it: whole minutes as they are, a fraction from the file as a decimal.
    public static string Show(double minutes) => minutes.ToString("0.###", CultureInfo.InvariantCulture);

    // Where Up or Down in a minutes box goes: a minute on from the number typed, or from the time in
    // use while the box holds none. A fraction from the file goes to the whole minute beside it on
    // that side, so Down from 2.5 is 2 and Up is 3. Null when there is no whole minute that way:
    // under a minute, Down would make the time longer.
    public static int? Step(TypedTime typed, double used, bool up)
    {
        int from = typed.IsNumber ? typed.Minutes : (int)(up ? Math.Floor(used) : Math.Ceiling(used));
        int to = Math.Clamp(from + (up ? 1 : -1), Shortest, Longest);
        return (up ? to < used : to > used) ? null : to;
    }

    static string Words(Strings text, double minutes) => text.SettingDuration(minutes);
}

// What was typed into a minutes box: whether it is a number, the number, and the whole minutes from
// 1 to 1440 it comes to.
readonly record struct TypedTime(bool IsNumber, long Number, int Minutes)
{
    public bool Clamped => IsNumber && Number != Minutes;
}
