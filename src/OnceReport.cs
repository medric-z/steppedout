namespace SteppedOut;

static class OnceReport
{
    public static void Print(Config config, Options options, IReadOnlyList<ILockSuppressor> rules)
    {
        double threshold = config.IdleMinutes * 60;
        var sample = new InputTracker().Sample();

        bool locked = SessionLock.IsLocked();
        var lines = new List<string>
        {
            $"{AppInfo.Name} {AppInfo.Version}{(options.DryRun ? " (dry run)" : "")}",
            $"idle: {Watcher.Format(sample.RawIdleSeconds)}, threshold {Watcher.Format(threshold)}",
            $"session: {(locked ? "locked" : "unlocked")}",
        };

        string? suppressor = null;
        foreach (var rule in rules)
        {
            string? reason;
            try
            {
                reason = rule.Check();
            }
            catch (Exception e)
            {
                lines.Add($"rule {rule.Name}: failed, {e.Message}");
                continue;
            }
            lines.Add($"rule {rule.Name}: {(reason is null ? "clear" : "holding the lock, " + reason)}");
            suppressor ??= reason;
        }

        string verdict = locked ? "would not lock, session already locked"
            : sample.RawIdleSeconds < threshold ? "would not lock, idle time below threshold"
            : suppressor is null ? "would lock"
            : "would not lock, " + suppressor;
        lines.Add("verdict: " + verdict);

        foreach (string line in lines) Console.WriteLine(line);
        Log.Write("once: " + verdict);
    }
}
