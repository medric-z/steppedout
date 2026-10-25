namespace SteppedOut;

// What a setting does to the lock time while the thing it watches is going on. The settings file
// spells them "none", "never", "later 60", "sooner 50" and "always 10": no change, never lock, lock
// 60 minutes later, lock 50% sooner, and lock after 10 minutes at the latest, even while something
// else says never.
enum EffectKind
{
    None,
    Never,
    Extra,
    Sooner,
    Cap,
}

// Amount is minutes for Extra and Cap and a percent for Sooner. Never and None have none.
readonly record struct Effect(EffectKind Kind, int Amount = 0)
{
    public static Effect None => default;

    public static Effect Never => new(EffectKind.Never);

    public static Effect Extra(int minutes) => new(EffectKind.Extra, minutes);

    public static Effect Sooner(int percent) => new(EffectKind.Sooner, percent);

    public static Effect Cap(int minutes) => new(EffectKind.Cap, minutes);

    // The settings file's words, which the log uses too.
    public override string ToString() => Kind switch
    {
        EffectKind.Never => "never",
        EffectKind.Extra => $"later {Amount}",
        EffectKind.Sooner => $"sooner {Amount}",
        EffectKind.Cap => $"always {Amount}",
        _ => "none",
    };

    // Reads the file's words, ignoring case and extra spaces. The number is whole digits only; one
    // too big for an int stops at its edge, and Clamp brings it into range like any other number.
    public static bool TryParse(string? text, out Effect effect)
    {
        effect = default;
        var words = (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1)
        {
            if (words[0].Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
            if (!words[0].Equals("never", StringComparison.OrdinalIgnoreCase)) return false;
            effect = Never;
            return true;
        }
        if (words.Length != 2 || !WholeNumber(words[1], out int amount)) return false;
        EffectKind? kind = words[0].ToLowerInvariant() switch
        {
            "later" => EffectKind.Extra,
            "sooner" => EffectKind.Sooner,
            "always" => EffectKind.Cap,
            _ => null,
        };
        if (kind is not EffectKind known) return false;
        effect = new(known, amount);
        return true;
    }

    static bool WholeNumber(string digits, out int value)
    {
        long sum = 0;
        foreach (char c in digits)
        {
            if (c is < '0' or > '9')
            {
                value = 0;
                return false;
            }
            sum = Math.Min(sum * 10 + (c - '0'), int.MaxValue);
        }
        value = (int)sum;
        return true;
    }
}

enum SourceKind
{
    MusicOrVideo,
    FullScreen,
    Call,
    App,
    Battery,
}

// What an effect comes from. App names the app of an app rule and is empty otherwise. Names are
// compared ignoring case, as Windows compares program names.
readonly record struct EffectSource(SourceKind Kind, string App = "")
{
    public static EffectSource MusicOrVideo => new(SourceKind.MusicOrVideo);

    public static EffectSource FullScreen => new(SourceKind.FullScreen);

    public static EffectSource Call => new(SourceKind.Call);

    public static EffectSource Battery => new(SourceKind.Battery);

    public static EffectSource ForApp(string app) => new(SourceKind.App, app);

    public bool Equals(EffectSource other) =>
        Kind == other.Kind && string.Equals(App ?? "", other.App ?? "", StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(App ?? ""));

    // The log's words; the windows have their own in each language.
    public override string ToString() => Kind switch
    {
        SourceKind.MusicOrVideo => "music or video",
        SourceKind.FullScreen => "full screen",
        SourceKind.Call => "a call",
        SourceKind.App => "app " + App,
        _ => "battery",
    };
}

// An effect that applies right now, and what it comes from.
readonly record struct ActiveEffect(Effect Effect, EffectSource Source)
{
    public override string ToString() => $"{Effect} ({Source})";
}

// The lock time right now, in minutes: the normal time with every effect that applies, and what
// each one did, for the status, the tooltip, the log and the Why window. The sums use decimal, never
// double: 30 minutes 70% sooner is exactly 9, where doubles give a hair over 9 and round it to 10.
sealed record LockTime
{
    LockTime(decimal normal, decimal sum, decimal? total, IReadOnlyList<ActiveEffect> extras, ActiveEffect? cut, bool cutApplied,
        ActiveEffect? cap, bool capApplied, IReadOnlyList<EffectSource> neverCauses)
    {
        Normal = normal;
        Sum = sum;
        Total = total;
        Extras = extras;
        Cut = cut;
        CutApplied = cutApplied;
        Cap = cap;
        CapApplied = capApplied;
        NeverCauses = neverCauses;
    }

    // The lock time chosen in the settings (idleMinutes), or given with --idle for one run.
    public decimal Normal { get; }

    // Normal plus every extra.
    public decimal Sum { get; }

    // When it locks, in minutes of idle time. Null while something keeps it from locking at all.
    public decimal? Total { get; }

    public bool NeverLocks => Total is null;

    public decimal? TotalSeconds => Total * 60;

    // Every extra that counts, once per source, in the order they came.
    public IReadOnlyList<ActiveEffect> Extras { get; }

    // The biggest "sooner" that applies, and whether it changed the time. A cut that would make the
    // time later than the sum, as rounding up can, changes nothing.
    public ActiveEffect? Cut { get; }

    public bool CutApplied { get; }

    // The shortest "always lock after" that applies, and whether it changed the time: it does when
    // it is shorter than the time without it, or when it ends a never.
    public ActiveEffect? Cap { get; }

    public bool CapApplied { get; }

    // What says never, in the order they came. A cap still sets a time while one of them holds.
    public IReadOnlyList<EffectSource> NeverCauses { get; }

    public EffectSource? NeverCause => NeverCauses.Count > 0 ? NeverCauses[0] : null;

    // A cut or a cap changed the time, which makes it a whole number of minutes, at least one.
    public bool Adjusted => CutApplied || CapApplied;

    public static LockTime Plain(decimal normalMinutes) => Compute(normalMinutes, Array.Empty<ActiveEffect>());

    // 1. The shortest cap wins. 2. Never, when no cap is active, means it never locks. 3. The sum is
    // the normal time plus every extra, each source once: music or video counts once when both its
    // sound and its keep-the-display-on signal fire. 4. The biggest cut P gives x = sum × (100 − P)
    // ÷ 100. 5. A cap beats never. 6. The total is min(sum, max(1, x rounded up to whole minutes)),
    // so a cut never makes it later and the floor and rounding only come with a cut that changes
    // the time; a cap applies only when it is shorter, and a plain lock time with extras stays as
    // it is, so a run with --idle 0.05 still locks after 3 seconds.
    public static LockTime Compute(decimal normalMinutes, IEnumerable<ActiveEffect> effects)
    {
        var extras = new List<ActiveEffect>();
        var never = new List<EffectSource>();
        ActiveEffect? cut = null, cap = null;
        foreach (var active in effects)
        {
            var effect = active.Effect;
            switch (effect.Kind)
            {
                case EffectKind.Extra:
                    // A source that comes twice counts once, with its bigger extra.
                    int seen = extras.FindIndex(e => e.Source == active.Source);
                    var counted = active with { Effect = Effect.Extra(Math.Max(0, effect.Amount)) };
                    if (seen < 0) extras.Add(counted);
                    else if (counted.Effect.Amount > extras[seen].Effect.Amount) extras[seen] = counted;
                    break;
                case EffectKind.Never:
                    if (!never.Contains(active.Source)) never.Add(active.Source);
                    break;
                case EffectKind.Sooner:
                    if (cut is not ActiveEffect biggest || effect.Amount > biggest.Effect.Amount) cut = active;
                    break;
                case EffectKind.Cap:
                    // Caps are whole minutes from 1 up; a smaller one in a hand-edited file means 1.
                    var floored = active with { Effect = Effect.Cap(Math.Max(1, effect.Amount)) };
                    if (cap is not ActiveEffect shortest || floored.Effect.Amount < shortest.Effect.Amount) cap = floored;
                    break;
            }
        }

        decimal sum = normalMinutes + extras.Sum(e => (decimal)e.Effect.Amount);
        decimal? total = null;
        bool cutApplied = false, capApplied = false;
        if (never.Count == 0)
        {
            total = sum;
            if (cut is ActiveEffect sooner)
            {
                decimal x = sum * (100 - sooner.Effect.Amount) / 100;
                decimal rounded = Math.Max(1, Math.Ceiling(x));
                if (rounded < sum)
                {
                    total = rounded;
                    cutApplied = true;
                }
            }
        }
        if (cap is ActiveEffect latest && (total is null || latest.Effect.Amount < total))
        {
            total = latest.Effect.Amount;
            capApplied = true;
        }
        return new LockTime(normalMinutes, sum, total, extras, cut, cutApplied, cap, capApplied, never);
    }

    // The warning countdown sits inside the lock time, so the PC locks at the time shown. It runs
    // for the warning or half the lock time, whichever is shorter, rounded up to whole seconds.
    public static int CountdownSeconds(decimal totalMinutes, int warningSeconds)
    {
        if (warningSeconds <= 0 || totalMinutes <= 0) return 0;
        return (int)Math.Ceiling(Math.Min(warningSeconds, totalMinutes * 60 / 2));
    }

    // The idle time, in seconds, at which the countdown starts so that it ends at the lock time.
    public static decimal CountdownStart(decimal totalMinutes, int warningSeconds) =>
        totalMinutes * 60 - CountdownSeconds(totalMinutes, warningSeconds);

    // The lock time from the settings (idleMinutes, or --idle for one run) as the calculator takes
    // it, kept inside the range the settings allow, so a value no reader should ever let through
    // still gives a time instead of an error. A value that is not a number at all, infinity among
    // them, is the default lock time, as Config.Clamp makes it, so a bad value still locks.
    public static decimal NormalMinutes(double idleMinutes) =>
        (decimal)(double.IsFinite(idleMinutes) ? Math.Clamp(idleMinutes, Config.MinIdleMinutes, Config.MaxIdleMinutes) : Config.DefaultIdleMinutes);

    // The lists count by what they hold, so two results worked out from the same things are equal.
    public bool Equals(LockTime? other) =>
        other is not null && Normal == other.Normal && Sum == other.Sum && Total == other.Total
        && Extras.SequenceEqual(other.Extras) && Cut == other.Cut && CutApplied == other.CutApplied
        && Cap == other.Cap && CapApplied == other.CapApplied && NeverCauses.SequenceEqual(other.NeverCauses);

    public override int GetHashCode() => HashCode.Combine(Normal, Sum, Total, Extras.Count, Cut, Cap, NeverCauses.Count);

    // For the log, which stays in English: "45 min (30 + 60 music or video, 50% sooner: battery)".
    public override string ToString()
    {
        var parts = new List<string> { Minutes(Normal) + string.Concat(Extras.Select(e => $" + {e.Effect.Amount} {e.Source}")) };
        if (Cut is ActiveEffect c) parts.Add($"{c.Effect.Amount}% sooner: {c.Source}{(CutApplied ? "" : " (no change)")}");
        if (Cap is ActiveEffect k) parts.Add($"at most {k.Effect.Amount}: {k.Source}{(CapApplied ? "" : " (no change)")}");
        if (NeverCauses.Count > 0) parts.Add("never: " + string.Join(", ", NeverCauses));
        return (Total is decimal t ? Minutes(t) + " min" : "never") + " (" + string.Join(", ", parts) + ")";
    }

    static string Minutes(decimal minutes) => minutes.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
