using System.Globalization;

namespace SteppedOut;

sealed record Options
{
    public bool DryRun { get; init; }
    public double? IdleMinutes { get; init; }
    public bool Once { get; init; }
    public bool ShowHelp { get; init; }
    public bool ShowVersion { get; init; }
    public string? Error { get; init; }
}

static class CommandLine
{
    public const string Usage = """
        usage: SteppedOut [--dry-run] [--idle <minutes>] [--once]

          --dry-run         log "WOULD LOCK" instead of locking the session
          --idle <minutes>  idle threshold for this run, overriding the config file
          --once            evaluate every rule once, print the verdict, exit
          --version         print the version
          --help            print this text
        """;

    public static Options Parse(string[] args)
    {
        bool dryRun = false, once = false, help = false, version = false;
        double? idle = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string? value = null;
            int eq = arg.IndexOf('=');
            if (eq > 0)
            {
                value = arg[(eq + 1)..];
                arg = arg[..eq];
            }

            switch (arg)
            {
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--once":
                    once = true;
                    break;
                case "--help" or "-h" or "/?":
                    help = true;
                    break;
                case "--version":
                    version = true;
                    break;
                case "--idle":
                    value ??= i + 1 < args.Length ? args[++i] : null;
                    if (value is null
                        || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes)
                        || minutes <= 0)
                    {
                        return new Options { Error = "--idle needs a number of minutes greater than zero" };
                    }
                    idle = minutes;
                    break;
                default:
                    return new Options { Error = "unknown option: " + arg };
            }
        }

        return new Options { DryRun = dryRun, IdleMinutes = idle, Once = once, ShowHelp = help, ShowVersion = version };
    }
}
