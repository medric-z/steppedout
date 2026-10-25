using SteppedOut;

// Runs every sheet's checks without pictures, off the screen and without a tray app, and sets the
// exit code. "check welcome" runs only the welcome window's, which are quick.
static class Check
{
    static int failures;

    public static int Run(bool welcomeOnly = false)
    {
        failures += WelcomeSheet.Check();
        if (welcomeOnly)
        {
            Console.WriteLine(failures == 0 ? "all checks passed" : $"{failures} checks failed");
            return failures;
        }
        // The look and kit sheets run their own checks, for both languages and both themes.
        failures += LookSheet.Check();
        failures += KitSheet.Check();
        failures += SettingsSheet.Check();
        failures += LockingSheet.Check();
        failures += AwaySheet.Check();
        failures += PagesSheet.Check();
        failures += NotesSheet.Check();
        failures += TraySheet.Check();
        failures += Demo.Check();
        Console.WriteLine(failures == 0 ? "all checks passed" : $"{failures} checks failed");
        return failures;
    }
}
