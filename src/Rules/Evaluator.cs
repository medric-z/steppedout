namespace SteppedOut;

// What the rules saw in one look: every line, counted or not, the rules that failed, and when.
// Fresh is true when every rule was asked and every reading taken afresh.
sealed record Evaluation(IReadOnlyList<WhyLine> Lines, IReadOnlyList<string> Failed, TimeSpan At, bool Fresh)
{
    public static Evaluation Nothing(TimeSpan at) => new(Array.Empty<WhyLine>(), Array.Empty<string>(), at, false);

    // What changes the lock time now, for LockTime.Compute.
    public IReadOnlyList<ActiveEffect> Effects => Evaluator.Effects(Lines);

    // A call is going on, whatever it does to the lock time. Reminders wait for it to end.
    public bool CallSeen => Lines.Any(l => l.Signal == Signal.Call);

    public bool Equals(Evaluation? other) =>
        other is not null && Lines.SequenceEqual(other.Lines) && Failed.SequenceEqual(other.Failed) && At == other.At && Fresh == other.Fresh;

    public override int GetHashCode() => HashCode.Combine(Lines.Count, Failed.Count, At, Fresh);
}

// Asks the rules and turns what they see into lines, the same way for the watcher and for --once.
//
// Two things keep one bad reading from moving the lock. Something that makes the lock later or
// stops it (full screen, a call, the display request, an app) and was seen a moment ago is held for
// a few seconds after it goes, so a foreground window missing while windows switch, or a display
// request let go between two videos, never starts a countdown on its own; sound has its own, longer
// hold. And a rule that throws keeps its last good lines for a little while, so a reading that fails
// on the very tick of the lock does not lock during a call.
sealed class Evaluator
{
    public static readonly TimeSpan HoldFor = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan KeepAfterFailure = TimeSpan.FromSeconds(10);

    readonly Func<IReadOnlyList<Rule>> rules;
    readonly Scans scans;
    // What each rule says now: its last lines, or after a failure its last good ones while they last.
    readonly Dictionary<Rule, IReadOnlyList<WhyLine>> current = new();
    readonly Dictionary<Rule, (IReadOnlyList<WhyLine> Lines, TimeSpan At)> lastGood = new();
    readonly Dictionary<string, (WhyLine Line, TimeSpan Seen)> held = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> failed = new();

    public Evaluator(Func<IReadOnlyList<Rule>> rules, Scans scans)
    {
        this.rules = rules;
        this.scans = scans;
    }

    // all: ask every rule, not only the ones asked every tick (the others say what they said last).
    // fresh: read everything afresh instead of the few-second-old process list, windows and calls.
    public Evaluation Evaluate(TimeSpan now, bool all, bool fresh)
    {
        var scene = new Scene(now, fresh, scans);
        var lines = new List<WhyLine>();
        var seenNow = new List<WhyLine>();
        foreach (var rule in rules())
        {
            if (!all && !rule.EveryTick)
            {
                if (current.TryGetValue(rule, out var said)) lines.AddRange(said);
                continue;
            }

            var mine = new List<WhyLine>();
            try
            {
                rule.Check(scene, mine);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                if (!failed.Contains(rule.Name))
                {
                    failed.Add(rule.Name);
                    Log.Write($"rule {rule.Name} failed, will keep trying: {e.Message}");
                }
                var kept = lastGood.TryGetValue(rule, out var good) && now >= good.At && now - good.At < KeepAfterFailure
                    ? good.Lines
                    : Array.Empty<WhyLine>();
                current[rule] = kept;
                lines.AddRange(kept);
                continue;
            }

            if (failed.Remove(rule.Name)) Log.Write($"rule {rule.Name} works again");
            current[rule] = mine;
            lastGood[rule] = (mine, now);
            lines.AddRange(mine);
            seenNow.AddRange(mine);
        }
        Hold(lines, seenNow, now);
        return new Evaluation(AfterCalls(lines), failed.ToArray(), now, all && fresh);
    }

    // After the settings change: what the rules said was said under the old ones.
    public void Reset()
    {
        current.Clear();
        lastGood.Clear();
        held.Clear();
        failed.Clear();
    }

    void Hold(List<WhyLine> lines, List<WhyLine> seenNow, TimeSpan now)
    {
        foreach (var line in seenNow)
        {
            if (Holds(line)) held[Key(line)] = (line, now);
        }
        var present = new HashSet<string>(lines.Where(Holds).Select(Key), StringComparer.OrdinalIgnoreCase);
        foreach (var (key, (line, seen)) in held.ToList())
        {
            if (present.Contains(key)) continue;
            if (now >= seen && now - seen < HoldFor) lines.Add(line);
            else held.Remove(key);
        }
    }

    // Held: what makes the lock later or stops it, from the rules with no hold of their own.
    static bool Holds(WhyLine line) =>
        line.Signal is Signal.Display or Signal.FullScreen or Signal.Call or Signal.App
        && line.Effect.Kind is not (EffectKind.Sooner or EffectKind.Cap);

    static string Key(WhyLine line) => line.Signal is Signal.Call or Signal.App ? line.Signal + "|" + line.Detail : line.Signal.ToString();

    // During a call the display request is the call's (a video call makes one), so it does not
    // count as music or video (D32). The call app's own sound is left out by the audio rule.
    public static IReadOnlyList<WhyLine> AfterCalls(IReadOnlyList<WhyLine> lines)
    {
        bool callSeen = lines.Any(l => l.Signal == Signal.Call);
        return lines.Select(l => DuringCall(l, callSeen)).ToList();
    }

    public static WhyLine DuringCall(WhyLine line, bool callSeen) =>
        callSeen && line.Signal == Signal.Display && line.Counts ? line with { Counts = false } : line;

    // A line in the log's words, which stay English: "sound from Spotify (later 60)".
    public static string LogText(WhyLine line)
    {
        string what = line.Signal switch
        {
            Signal.Sound => "sound from " + (line.Detail == "" ? "an app" : line.Detail),
            Signal.Display => "an app asked Windows to keep the display on",
            Signal.FullScreen => "full screen: " + line.Detail,
            Signal.Call => "microphone in use: " + line.Detail,
            Signal.App => "app " + line.Detail,
            _ => "on battery",
        };
        return what + (line.Counts ? $" ({line.Effect})" : " (part of the call)");
    }

    // The effects for LockTime.Compute: every line that counts and changes something.
    public static IReadOnlyList<ActiveEffect> Effects(IEnumerable<WhyLine> lines) =>
        lines.Where(l => l.Counts && l.Effect.Kind != EffectKind.None).Select(l => new ActiveEffect(l.Effect, l.Source)).ToList();
}

// When the rules are asked. The battery every second, since unplugging can bring the lock forward
// at any moment. Every rule every 5 seconds at any idle time, so the status, the tooltip and the
// minutes on the icon are right long before the lock. And every rule every second from a little
// before the earliest moment the countdown could start with these settings, and while a window
// that shows the status live is open.
//
// The earliest start comes from the lowest lock time the settings can give: the normal time with
// the biggest "sooner" and the shortest "always lock after" any of them could apply (only the
// battery and app rules have those), with no extras and no nevers. The countdown start, total − w
// with w = min(warning, total ÷ 2), only rises with the total, so no lock time the settings can
// give starts its countdown sooner. From the audio silence hold plus 2 seconds before it, the audio
// rule is asked every second, so it has heard the whole hold by then.
static class PollSchedule
{
    public const int FarTicks = 5;

    public static decimal LowestTotal(Config config) =>
        LowestTotal(LockTime.NormalMinutes(config.IdleMinutes), (config.AppRules ?? new()).Select(r => r.Effect).Append(config.OnBattery));

    public static decimal LowestTotal(decimal normalMinutes, IEnumerable<Effect> possible)
    {
        var list = possible.ToList();
        var effects = new List<ActiveEffect>();
        var cuts = list.Where(e => e.Kind == EffectKind.Sooner).ToList();
        var caps = list.Where(e => e.Kind == EffectKind.Cap).ToList();
        if (cuts.Count > 0) effects.Add(new ActiveEffect(cuts.MaxBy(e => e.Amount), EffectSource.Battery));
        if (caps.Count > 0) effects.Add(new ActiveEffect(caps.MinBy(e => e.Amount), EffectSource.Battery));
        return LockTime.Compute(normalMinutes, effects).Total ?? normalMinutes;
    }

    // The idle seconds from which every rule is asked every second.
    public static double CloseFrom(Config config) =>
        CloseFrom(LowestTotal(config), config.GraceSeconds, config.AudioSilenceSeconds);

    public static double CloseFrom(decimal lowestTotalMinutes, int warningSeconds, int silenceSeconds) =>
        Math.Max(0, (double)LockTime.CountdownStart(lowestTotalMinutes, warningSeconds) - (Math.Max(0, silenceSeconds) + 2));

    public static bool AllRules(double idleSeconds, double closeFrom, int ticksSinceAll, bool live) =>
        live || idleSeconds >= closeFrom || ticksSinceAll >= FarTicks;
}
