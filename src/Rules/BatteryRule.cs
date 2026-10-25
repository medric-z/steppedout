namespace SteppedOut;

// On battery power, lock sooner or after a fixed time at the latest. Read every second: it is one
// cheap call, and unplugging can bring the lock forward at any moment.
sealed class BatteryRule : Rule
{
    public override string Name => "battery";

    public override bool EveryTick => true;

    // Sooner or Cap; anything else leaves the rule out.
    public Effect Effect { get; set; }

    public override void Check(Scene scene, List<WhyLine> lines)
    {
        if (OnBattery(scene.Read.PowerLine())) lines.Add(new WhyLine(Signal.Battery, Effect));
    }

    // Only Offline is on battery. Online is plugged in; Unknown, as on a desktop PC with no battery
    // or when Windows cannot tell, locks as usual.
    internal static bool OnBattery(PowerLineStatus status) => status == PowerLineStatus.Offline;
}
