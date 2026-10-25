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
        usage: SteppedOut [--dry-run] [--idle <minutes>] [--once] [--version] [--help]

          --dry-run         log what it would do ("WOULD LOCK" and so on) instead of doing it
          --idle <minutes>  minutes without input before it locks, countdown included, for this run only
          --once            print what would happen if you stepped away now, then exit
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
                    // The parser also takes "NaN" and "Infinity", and this value skips the config
                    // file's clamping, so it gets the same range here.
                    if (value is null
                        || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes)
                        || !double.IsFinite(minutes)
                        || minutes < Config.MinIdleMinutes
                        || minutes > Config.MaxIdleMinutes)
                    {
                        return new Options { Error = $"--idle needs a number of minutes from {Config.MinIdleMinutes} to {Config.MaxIdleMinutes}" };
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
