namespace SteppedOut;

// One thing that can change the lock time: music or video, full screen, a call, the battery, the
// app rules. Each holds what its setting does and is asked with one look at Windows that every
// rule shares (a Scene).
abstract class Rule
{
    // The rule's name in the log and in --once output.
    public abstract string Name { get; }

    // Asked every second, not only every few seconds: cheap to ask, and able to bring the lock
    // forward, so it has to be seen the moment it changes.
    public virtual bool EveryTick => false;

    // Adds a line for each thing going on now that this rule watches, with what it does to the
    // lock time. May throw; the caller keeps the rule's last good lines for a while.
    public abstract void Check(Scene scene, List<WhyLine> lines);
}

// The rules for one set of settings. The same instances live as long as the watcher, so a change
// in the settings window, saved a moment after each edit, does not make the audio rule forget the
// sound it heard a second ago.
sealed class RuleSet
{
    public RuleSet(Config config)
    {
        Apply(config);
    }

    public CallRule Call { get; } = new();
    public AudioRule Audio { get; } = new(Effect.None, 0.01, 30);
    public DisplayRule Display { get; } = new();
    public FullscreenRule FullScreen { get; } = new();
    public AppRules Apps { get; } = new();
    public BatteryRule Battery { get; } = new();

    // The rules that can change anything with these settings, in the order they are asked.
    public IReadOnlyList<Rule> Active { get; private set; } = Array.Empty<Rule>();

    public void Apply(Config config)
    {
        Call.Effect = Later(config.OnCall);
        Audio.Set(Later(config.MusicVideo), config.AudioPeakThreshold, config.AudioSilenceSeconds);
        Display.Effect = Audio.Effect;
        FullScreen.Effect = Later(config.FullScreen);
        Apps.Set(config.AppRules);
        Battery.Effect = config.OnBattery.Kind is EffectKind.Sooner or EffectKind.Cap ? config.OnBattery : Effect.None;

        // The call rule is always asked, even when calls change nothing: the call app's own sound
        // and the display request a video call makes are the call, not music or video (D32), and a
        // reminder waits until the call is over.
        var active = new List<Rule> { Call };
        if (Audio.Effect.Kind != EffectKind.None)
        {
            active.Add(Audio);
            active.Add(Display);
        }
        if (FullScreen.Effect.Kind != EffectKind.None) active.Add(FullScreen);
        if (Apps.Any) active.Add(Apps);
        if (Battery.Effect.Kind != EffectKind.None) active.Add(Battery);
        Active = active;
    }

    // Music or video, full screen and calls can only make the lock later or stop it.
    static Effect Later(Effect effect) => effect.Kind is EffectKind.Never or EffectKind.Extra ? effect : Effect.None;
}

static class Rules
{
    // The rules the settings turn on, new, for one look (--once).
    public static List<Rule> Build(Config config) => new RuleSet(config).Active.ToList();
}
