namespace SteppedOut.Tests;

[TestClass]
public class LockTimeTests
{
    static readonly EffectSource Media = EffectSource.MusicOrVideo;
    static readonly EffectSource Full = EffectSource.FullScreen;
    static readonly EffectSource Call = EffectSource.Call;
    static readonly EffectSource Battery = EffectSource.Battery;
    static readonly EffectSource Notepad = EffectSource.ForApp("notepad");

    static ActiveEffect Extra(int minutes, EffectSource from) => new(Effect.Extra(minutes), from);

    static ActiveEffect Sooner(int percent, EffectSource from) => new(Effect.Sooner(percent), from);

    static ActiveEffect Cap(int minutes, EffectSource from) => new(Effect.Cap(minutes), from);

    static ActiveEffect Never(EffectSource from) => new(Effect.Never, from);

    static decimal? Total(decimal normal, params ActiveEffect[] effects) => LockTime.Compute(normal, effects).Total;

    [TestMethod]
    public void ExtrasAddUp()
    {
        Assert.AreEqual(210m, Total(30, Extra(60, Media), Extra(120, Full)));
    }

    [TestMethod]
    public void ACutAppliesToTheExtrasToo()
    {
        Assert.AreEqual(75m, Total(30, Extra(120, Full), Sooner(50, Battery)), "1 h 15 min");
        Assert.AreEqual(45m, Total(30, Extra(60, Media), Sooner(50, Battery)));
    }

    [TestMethod]
    public void NeverMeansItNeverLocks()
    {
        var time = LockTime.Compute(30, new[] { Never(Call) });
        Assert.IsNull(time.Total);
        Assert.IsTrue(time.NeverLocks);
        Assert.AreEqual(Call, time.NeverCause);
    }

    [TestMethod]
    public void ACapBeatsNever()
    {
        var time = LockTime.Compute(30, new[] { Never(Call), Cap(10, Battery) });
        Assert.AreEqual(10m, time.Total);
        Assert.IsTrue(time.CapApplied);
        Assert.AreEqual(Call, time.NeverCause, "what says never is still reported");
    }

    [TestMethod]
    public void ACapIsALatestTimeNeverALongerOne()
    {
        var time = LockTime.Compute(15, new[] { Cap(30, Battery) });
        Assert.AreEqual(15m, time.Total);
        Assert.IsFalse(time.CapApplied);
        Assert.IsFalse(time.Adjusted);
    }

    [TestMethod]
    public void ACapCutsTheExtrasShort()
    {
        Assert.AreEqual(45m, Total(30, Extra(60, Media), Cap(45, Battery)));
    }

    [TestMethod]
    public void TheShortestCapWins()
    {
        var time = LockTime.Compute(30, new[] { Cap(10, Battery), Cap(5, Notepad) });
        Assert.AreEqual(5m, time.Total);
        Assert.AreEqual(Notepad, time.Cap?.Source);
    }

    [TestMethod]
    public void TheBiggestCutWins()
    {
        var time = LockTime.Compute(30, new[] { Sooner(25, Notepad), Sooner(50, Battery) });
        Assert.AreEqual(15m, time.Total);
        Assert.AreEqual(Battery, time.Cut?.Source);
    }

    [TestMethod]
    public void ACutRoundsUpToWholeMinutes()
    {
        Assert.AreEqual(2m, Total(5, Sooner(75, Battery)), "1.25 rounded up");
    }

    [TestMethod]
    public void ACutNeverGoesBelowOneMinute()
    {
        Assert.AreEqual(1m, Total(1, Sooner(75, Battery)));
        Assert.AreEqual(1m, Total(3, Sooner(95, Battery)), "0.15 is floored to a minute");
    }

    [TestMethod]
    public void MusicOrVideoCountsOnceWhenSoundAndVideoBothFire()
    {
        var time = LockTime.Compute(30, new[] { Extra(60, Media), Extra(60, Media) });
        Assert.AreEqual(90m, time.Total, "not 150");
        Assert.HasCount(1, time.Extras);
    }

    [TestMethod]
    public void ACutNeverMakesItLater()
    {
        var half = LockTime.Compute(0.5m, new[] { Sooner(50, Battery) });
        Assert.AreEqual(0.5m, half.Total);
        Assert.IsFalse(half.CutApplied, "rounding 0.25 up to a minute would be later than 0.5");
        Assert.AreEqual(1.5m, Total(1.5m, Sooner(25, Battery)));
    }

    [TestMethod]
    public void TheMathIsExact()
    {
        Assert.AreEqual(9m, Total(30, Sooner(70, Battery)), "doubles give 10");
        Assert.AreEqual(70m, Total(100, Sooner(30, Battery)));
    }

    [TestMethod]
    public void ACapThatDoesNotBindRoundsNothing()
    {
        var time = LockTime.Compute(0.5m, new[] { Cap(5, Battery) });
        Assert.AreEqual(0.5m, time.Total);
        Assert.IsFalse(time.Adjusted);
    }

    [TestMethod]
    public void APlainLockTimeStaysAsSet()
    {
        Assert.AreEqual(0.05m, Total(0.05m), "--idle 0.05 still locks after 3 seconds");
        var extras = LockTime.Compute(0.05m, new[] { Extra(2, Media), Extra(0, Full) });
        Assert.AreEqual(2.05m, extras.Total, "extras alone are not rounded");
        Assert.IsFalse(extras.Adjusted);
    }

    [TestMethod]
    public void TheCountdownEndsAtTheLockTime()
    {
        Assert.AreEqual(60, LockTime.CountdownSeconds(30, 60));
        Assert.AreEqual(29 * 60m, LockTime.CountdownStart(30, 60), "starts at 29:00");
        Assert.AreEqual(30 * 60m, LockTime.CountdownStart(30, 60) + LockTime.CountdownSeconds(30, 60), "locks at 30:00");
    }

    [TestMethod]
    public void TheCountdownIsAtMostHalfTheLockTime()
    {
        decimal total = LockTime.Compute(2, new[] { Sooner(50, Battery) }).Total!.Value;
        Assert.AreEqual(1m, total);
        Assert.AreEqual(30, LockTime.CountdownSeconds(total, 60), "30 s, not 60");
        Assert.AreEqual(30m, LockTime.CountdownStart(total, 60), "starts at 0:30");
    }

    [TestMethod]
    public void TheCountdownRoundsUpToWholeSeconds()
    {
        Assert.AreEqual(2, LockTime.CountdownSeconds(0.05m, 15), "half of 3 seconds is 1.5");
        Assert.AreEqual(1m, LockTime.CountdownStart(0.05m, 15));
    }

    [TestMethod]
    public void NoWarningMeansNoCountdown()
    {
        Assert.AreEqual(0, LockTime.CountdownSeconds(15, 0));
        Assert.AreEqual(15 * 60m, LockTime.CountdownStart(15, 0));
    }

    [TestMethod]
    public void EveryCutMatchesIntegerMath()
    {
        for (int sum = 1; sum <= 3000; sum++)
        {
            for (int percent = 1; percent <= 95; percent++)
            {
                // Integer ceiling of sum × (100 − percent) ÷ 100, floored at 1 and never past the sum.
                int x = (sum * (100 - percent) + 99) / 100;
                int expected = Math.Min(sum, Math.Max(1, x));
                var time = LockTime.Compute(sum, new[] { Sooner(percent, Battery) });
                if (time.Total != expected) Assert.Fail($"{sum} min {percent}% sooner gave {time.Total}, not {expected}");
                Assert.AreEqual(expected < sum, time.CutApplied, $"{sum} min {percent}% sooner");
            }
        }
    }

    [TestMethod]
    public void TheResultSaysWhatMadeTheTime()
    {
        var time = LockTime.Compute(30, new[] { Extra(60, Media), Never(Full), Extra(30, Notepad), Sooner(50, Battery) });
        Assert.IsTrue(time.NeverLocks);
        Assert.AreEqual(30m, time.Normal);
        Assert.AreEqual(120m, time.Sum);
        CollectionAssert.AreEqual(new[] { Extra(60, Media), Extra(30, Notepad) }, time.Extras.ToArray());
        Assert.AreEqual(Sooner(50, Battery), time.Cut);
        Assert.IsFalse(time.CutApplied, "never wins over a cut");
        CollectionAssert.AreEqual(new[] { Full }, time.NeverCauses.ToArray());

        var cut = LockTime.Compute(30, new[] { Extra(60, Media), Sooner(50, Battery) });
        Assert.IsTrue(cut.CutApplied);
        Assert.IsTrue(cut.Adjusted);
        Assert.IsNull(cut.Cap);
        Assert.AreEqual(45m * 60, cut.TotalSeconds);
        Assert.AreEqual("45 min (30 + 60 music or video, 50% sooner: battery)", cut.ToString());
    }

    [TestMethod]
    public void TheSameEffectsGiveEqualResults()
    {
        var a = LockTime.Compute(30, new[] { Extra(60, Media), Never(Call) });
        var b = LockTime.Compute(30, new[] { Extra(60, Media), Never(Call) });
        Assert.AreEqual(a, b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        Assert.AreNotEqual(a, LockTime.Plain(30));
    }

    [TestMethod]
    public void AnAppComesOnceWhateverItsCase()
    {
        var time = LockTime.Compute(30, new[] { Extra(30, EffectSource.ForApp("Notepad")), Extra(30, EffectSource.ForApp("NOTEPAD")) });
        Assert.AreEqual(60m, time.Total);
        Assert.AreEqual(EffectSource.ForApp("notepad"), EffectSource.ForApp("NotePad"));
        Assert.AreEqual(EffectSource.ForApp("notepad").GetHashCode(), EffectSource.ForApp("NotePad").GetHashCode());
        Assert.AreNotEqual(EffectSource.ForApp("notepad"), EffectSource.ForApp("mspaint"));
    }

    [TestMethod]
    public void NumbersOutOfRangeStillGiveASensibleTime()
    {
        Assert.AreEqual(30m, Total(30, Extra(-5, Media)), "a negative extra adds nothing");
        Assert.AreEqual(1m, Total(30, Cap(0, Battery)), "a cap is at least a minute");
        Assert.AreEqual(1m, Total(30, Sooner(100, Battery)));
        Assert.AreEqual(30m, Total(30, Sooner(0, Battery)));
        Assert.AreEqual(30m, Total(30, new ActiveEffect(Effect.None, Battery)));
    }

    [TestMethod]
    [DataRow("none", (int)EffectKind.None, 0)]
    [DataRow("never", (int)EffectKind.Never, 0)]
    [DataRow("later 60", (int)EffectKind.Extra, 60)]
    [DataRow("sooner 50", (int)EffectKind.Sooner, 50)]
    [DataRow("always 10", (int)EffectKind.Cap, 10)]
    public void EffectsReadAndWriteTheFilesWords(string text, int kind, int amount)
    {
        Assert.IsTrue(Effect.TryParse(text, out var effect));
        Assert.AreEqual(new Effect((EffectKind)kind, amount), effect);
        Assert.AreEqual(text, effect.ToString());
    }

    [TestMethod]
    public void EffectWordsAreReadWhateverTheirCaseAndSpacing()
    {
        Assert.IsTrue(Effect.TryParse("  Later   90 ", out var later));
        Assert.AreEqual(Effect.Extra(90), later);
        Assert.IsTrue(Effect.TryParse("NEVER", out var never));
        Assert.AreEqual(Effect.Never, never);
        Assert.IsTrue(Effect.TryParse("later 99999999999", out var huge));
        Assert.AreEqual(Effect.Extra(int.MaxValue), huge, "too big stops at the edge, for Clamp");
    }

    [TestMethod]
    [DataRow((string?)null)]
    [DataRow("")]
    [DataRow("sometimes")]
    [DataRow("later")]
    [DataRow("later x")]
    [DataRow("later -5")]
    [DataRow("later 1.5")]
    [DataRow("later 60 min")]
    [DataRow("never 5")]
    [DataRow("wait 60")]
    public void OtherWordsAreNotEffects(string? text)
    {
        Assert.IsFalse(Effect.TryParse(text, out var effect));
        Assert.AreEqual(Effect.None, effect);
    }
}
