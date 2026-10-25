namespace SteppedOut.Tests;

[TestClass]
public class BatteryRuleTests
{
    static List<WhyLine> Check(Effect effect, PowerLineStatus power)
    {
        var world = new FakeWorld { Power = power };
        var lines = new List<WhyLine>();
        new BatteryRule { Effect = effect }.Check(world.Look(TimeSpan.FromSeconds(1)), lines);
        return lines;
    }

    [TestMethod]
    public void OnlyOfflineIsOnBattery()
    {
        Assert.IsTrue(BatteryRule.OnBattery(PowerLineStatus.Offline));
        Assert.IsFalse(BatteryRule.OnBattery(PowerLineStatus.Online));
        Assert.IsFalse(BatteryRule.OnBattery(PowerLineStatus.Unknown), "a desktop PC, or Windows cannot tell: lock as usual");
    }

    [TestMethod]
    public void OnBatteryItsEffectApplies()
    {
        CollectionAssert.AreEqual(new[] { new WhyLine(Signal.Battery, Effect.Sooner(50)) }, Check(Effect.Sooner(50), PowerLineStatus.Offline));
        CollectionAssert.AreEqual(new[] { new WhyLine(Signal.Battery, Effect.Cap(10)) }, Check(Effect.Cap(10), PowerLineStatus.Offline));
        Assert.IsEmpty(Check(Effect.Sooner(50), PowerLineStatus.Online));
        Assert.IsEmpty(Check(Effect.Sooner(50), PowerLineStatus.Unknown));
    }

    [TestMethod]
    public void ItIsAskedEverySecond()
    {
        Assert.IsTrue(new BatteryRule().EveryTick);
        Assert.IsFalse(new CallRule().EveryTick);
    }

    [TestMethod]
    public void OnlyACutOrACapMakesABatteryRule()
    {
        Assert.IsTrue(Rules.Build(new Config { OnBattery = Effect.Sooner(25) }).Any(r => r is BatteryRule));
        Assert.IsTrue(Rules.Build(new Config { OnBattery = Effect.Cap(5) }).Any(r => r is BatteryRule));
        Assert.IsFalse(Rules.Build(new Config()).Any(r => r is BatteryRule), "lock as usual by default");
        Assert.IsFalse(Rules.Build(new Config { OnBattery = Effect.Never }).Any(r => r is BatteryRule));
    }

    [TestMethod]
    public void ReadingTheRealPowerLineDoesNotThrow()
    {
        Assert.IsTrue(Enum.IsDefined(Native.PowerLine()));
    }
}
