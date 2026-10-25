namespace SteppedOut;

// The person's own rules for apps: while an app runs, or only while one of its windows can be seen
// (D6), lock later, never, sooner, or after a fixed time at the latest. Each app counts once however
// many processes it runs, so a browser with a dozen of them adds its extra time once.
sealed class AppRules : Rule
{
    IReadOnlyList<(string App, AppRule Rule)> rules = Array.Empty<(string, AppRule)>();
    IReadOnlySet<string> names = new HashSet<string>();

    public override string Name => "apps";

    public bool Any => rules.Count > 0;

    // One rule per app, by its normalized name, the first one winning (D3), as the settings file's
    // own clean-up keeps them. A rule that does nothing, or names no app, is left out.
    public void Set(IEnumerable<AppRule> list)
    {
        var kept = new List<(string, AppRule)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in list)
        {
            if (rule is null || rule.Effect.Kind == EffectKind.None) continue;
            string app = ProcessRule.Normalize(rule.App ?? "");
            if (app.Length == 0 || !seen.Add(app)) continue;
            kept.Add((app, rule));
        }
        rules = kept;
        names = seen;
    }

    public override void Check(Scene scene, List<WhyLine> lines)
    {
        if (rules.Count == 0) return;
        var mine = scene.Processes.Where(p => p.Session == ProcessRule.MySession).ToList();
        var running = ProcessRule.Running(mine, names);
        HashSet<string>? onScreen = null;
        foreach (var (app, rule) in rules)
        {
            if (!running.Contains(app)) continue;
            if (rule.When == AppWhen.OnScreen && !(onScreen ??= OnScreen(scene.Windows, mine)).Contains(app)) continue;
            lines.Add(new WhyLine(Signal.App, rule.Effect, app));
        }
    }

    // The names of this session's apps with a window that can be seen.
    internal static HashSet<string> OnScreen(IEnumerable<TopWindow> windows, IEnumerable<RunningProcess> mine)
    {
        var byId = new Dictionary<int, string>();
        foreach (var process in mine) byId[process.Id] = process.Name;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in windows)
        {
            if (window.OnScreen && byId.TryGetValue(window.ProcessId, out string? name)) seen.Add(name);
        }
        return seen;
    }
}
