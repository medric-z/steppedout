namespace SteppedOut;

static class English
{
    public static readonly Strings Text = new()
    {
        V1 = EnglishV1.Text,
        RightToLeft = false,
        SecondsCount = n => n == 1 ? "1 second" : $"{n} seconds",
        MinutesCount = n => n == 1 ? "1 minute" : $"{n} minutes",
        HoursCount = n => n == 1 ? "1 hour" : $"{n} hours",
        MinutesWord = n => n == 1 ? "minute" : "minutes",
        And = " ",

        WelcomeTitle = "Welcome to SteppedOut",
        WelcomeWhere = "Look for the padlock next to the clock. If it's hidden, click the ^ arrow and drag it onto the taskbar. Click the padlock for settings, or right-click it for the menu.",
        Done = "Done",
        OpenSettingsButton = "Open settings",

        AlreadyRunning = "SteppedOut is already running.",

        ConfigNotUsed = "config.json was not used",
        ConfigDefaultsUntilFixed = "It could not be read, so the default settings apply until it is fixed. The log in its folder says why.",
        ConfigEarlierUntilFixed = "It could not be read, so your earlier settings stay until it is fixed. The log in its folder says why.",
        ConfigSkipped = "Part of config.json was skipped",
        ConfigSkippedDetail = "A name in it is not a setting, perhaps a typo. Everything else was used. The log in its folder names it.",

        MinutesFieldName = "{0}, in minutes",
        MinutesRange = "From 1 to 1440 minutes.",
        MinutesThatIs = "That's {0}.",
        MinutesClamped = "Only 1 to 1440 minutes work, so it uses {0}.",
        MinutesNotANumber = "Type a number of minutes. Until then it uses {0}.",

        OpenAction = "Open",
        SelectAction = "Select",
        TurnOn = "Turn on",
        TurnOff = "Turn off",
    };
}
