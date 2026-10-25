namespace SteppedOut.Tests;

// The lock time the calculator takes and the one the settings keep agree on values no reader should
// let through: not a number, or infinite, is the default lock time in both, so a bad value still
// locks, and soon.
[TestClass]
public class NormalMinutesTests
{
    [TestMethod]
    public void AValueThatIsNotANumberIsTheDefaultInBoth()
    {
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var config = new Config { IdleMinutes = bad };
            config.Clamp();
            Assert.AreEqual(Config.DefaultIdleMinutes, config.IdleMinutes, $"Clamp, {bad}");
            Assert.AreEqual((decimal)Config.DefaultIdleMinutes, LockTime.NormalMinutes(bad), $"NormalMinutes, {bad}");
        }
    }

    [TestMethod]
    public void EveryOtherValueIsKeptInTheSameRangeByBoth()
    {
        foreach (double idle in new[] { -5, 0, 0.01, 0.05, 0.5, 1, 15, 30.25, 1440, 1441, 1e9, double.MaxValue, double.MinValue })
        {
            var config = new Config { IdleMinutes = idle };
            config.Clamp();
            Assert.AreEqual((decimal)config.IdleMinutes, LockTime.NormalMinutes(idle), $"{idle}");
        }
    }
}
