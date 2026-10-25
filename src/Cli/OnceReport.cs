namespace SteppedOut;

static class OnceReport
{
    const string VerdictLabel = "verdict: ";

    // configProblem and configWarning are what Config.Load reported. Only printed: --once writes no
    // file, the log included (StartPlan).
    public static void Print(Config config, Options options, IReadOnlyList<Rule> rules, string? configProblem = null, string? configWarning = null)
    {
        var sample = new InputTracker().Sample();
        bool locked = SessionLock.IsLocked();
        using var scans = new Scans(Sources.Real);
        var scene = new Scene(Clock.Now, fresh: true, scans);
        foreach (string line in Report(config, options, rules, scene, sample.RawIdleSeconds, locked, configProblem, configWarning)) Console.WriteLine(line);
    }

    // Every line Print writes, the verdict last. Each rule is asked once, the way the watcher asks it,
    // and a rule that fails gets its own line instead of stopping the report.
    internal static List<string> Report(Config config, Options options, IReadOnlyList<Rule> rules, Scene scene, double idleSeconds, bool locked,
        string? configProblem = null, string? configWarning = null)
    {
        decimal normal = LockTime.NormalMinutes(config.IdleMinutes);
        var lines = new List<string> { $"{AppInfo.Name} {AppInfo.Version}{(options.DryRun ? " (dry run)" : "")}" };
        // A mistake in the file is why settings look ignored, so it comes before them.
        if (configProblem is not null) lines.Add("config: " + configProblem);
        if (configWarning is not null) lines.Add("config: " + configWarning);
        lines.Add($"idle: {Watcher.Format(idleSeconds)} of {Watcher.Format((double)(normal * 60))}");
        lines.Add($"session: {(locked ? "locked" : "unlocked")}");
        lines.Add($"when away: {(config.LockWhenAway ? "lock" : "do not lock")}");

        var asked = new List<(Rule Rule, List<WhyLine> Lines, string? Failure)>();
        foreach (var rule in rules)
        {
            var found = new List<WhyLine>();
            try
            {
                rule.Check(scene, found);
                asked.Add((rule, found, null));
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                asked.Add((rule, found, e.Message));
            }
        }

        bool callSeen = asked.Any(a => a.Failure is null && a.Lines.Any(l => l.Signal == Signal.Call));
        var seen = new List<WhyLine>();
        foreach (var (rule, found, failure) in asked)
        {
            if (failure is not null)
            {
                lines.Add($"rule {rule.Name}: failed, {failure}");
                continue;
            }
            var shown = found.Select(l => Evaluator.DuringCall(l, callSeen)).ToList();
            seen.AddRange(shown);
            lines.Add(shown.Count == 0 ? $"rule {rule.Name}: clear" : $"rule {rule.Name}: {string.Join("; ", shown.Select(Evaluator.LogText))}");
        }
        // The settings leave these out, so without a line they would just be missing.
        if (!rules.Any(r => r is AudioRule)) lines.Add("rule audio, display: off, musicVideo is none");
        if (!rules.Any(r => r is FullscreenRule)) lines.Add("rule fullscreen: off, fullScreen is none");
        if (!rules.Any(r => r is BatteryRule)) lines.Add("rule battery: off, onBattery is none");
        if (!rules.Any(r => r is AppRules)) lines.Add("rule apps: none set");

        var time = LockTime.Compute(normal, Evaluator.Effects(seen));
        lines.Add("lock time: " + time);
        lines.Add(VerdictLabel + Verdict(locked, time, idleSeconds, config));
        return lines;
    }

    // What would happen if the user stepped away now. Whoever runs this from a terminal has just
    // pressed Enter, so the idle time alone would always answer "not yet"; it has its own line. The
    // countdown sits inside the lock time, so the time named is when it locks.
    internal static string Verdict(bool locked, LockTime time, double idleSeconds, Config config)
    {
        if (locked) return "session already locked";
        if (time.Total is not decimal total) return $"would not lock while this lasts (never: {string.Join(", ", time.NeverCauses)})";
        string action = config.LockWhenAway ? "lock" : "step out without locking";
        int w = LockTime.CountdownSeconds(total, config.GraceSeconds);
        double lockAt = (double)(total * 60);
        double start = lockAt - w;
        if (idleSeconds >= start) return w > 0 ? $"would {action} now, after a {w} s countdown" : $"would {action} now";
        return w > 0
            ? $"would {action} once idle reaches {Watcher.Format(lockAt)}, after a {w} s countdown from {Watcher.Format(start)}"
            : $"would {action} once idle reaches {Watcher.Format(lockAt)}";
    }
}
