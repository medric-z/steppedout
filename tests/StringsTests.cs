using System.Reflection;
using System.Text.RegularExpressions;

namespace SteppedOut.Tests;

[TestClass]
public class StringsTests
{
    static readonly Strings[] Languages = { English.Text, Arabic.Text };

    static IEnumerable<string> AllText(Strings strings) =>
        typeof(Strings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => (string)p.GetValue(strings)!);

    [TestMethod]
    public void NoLineIsBlankAndEveryPlaceholderFormats()
    {
        foreach (var strings in Languages)
        {
            foreach (string line in AllText(strings))
            {
                // " " joins hours and minutes in English, so only fully empty lines are wrong.
                Assert.AreNotEqual("", line);
                _ = string.Format(line, "a", "b");
            }
        }
    }

    static string[] Holes(string text) => Regex.Matches(text, @"\{(\d+)\}").Select(m => m.Value).Distinct().Order().ToArray();

    [TestMethod]
    public void BothLanguagesUseTheSamePlaceholders()
    {
        var english = typeof(Strings).GetProperties().Where(p => p.PropertyType == typeof(string)).ToList();
        foreach (var property in english)
        {
            string en = (string)property.GetValue(English.Text)!;
            string ar = (string)property.GetValue(Arabic.Text)!;
            CollectionAssert.AreEqual(Holes(en), Holes(ar), property.Name);
        }
    }

    [TestMethod]
    public void EnglishCounts()
    {
        Assert.AreEqual("1 minute", English.Text.MinutesCount(1));
        Assert.AreEqual("15 minutes", English.Text.MinutesCount(15));
        Assert.AreEqual("1 hour 5 minutes", English.Text.Duration(TimeSpan.FromMinutes(65)));
        Assert.AreEqual("2 hours", English.Text.Duration(TimeSpan.FromHours(2)));
        Assert.AreEqual("42 minutes", English.Text.Duration(TimeSpan.FromSeconds(42 * 60 + 30)));
        Assert.AreEqual("40 seconds", English.Text.Duration(TimeSpan.FromSeconds(40)));
        // A hand-set fraction reads to the nearest minute wherever the setting is named.
        Assert.AreEqual("2 minutes", English.Text.SettingDuration(1.5));
        Assert.AreEqual("1 hour 31 minutes", English.Text.SettingDuration(90.5));
        Assert.AreEqual("30 seconds", English.Text.SettingDuration(0.5));
    }

    [TestMethod]
    public void ArabicCounts()
    {
        var ar = Arabic.Text;
        Assert.AreEqual("دقيقة", ar.MinutesCount(1));
        Assert.AreEqual("دقيقتين", ar.MinutesCount(2));
        Assert.AreEqual("5 دقائق", ar.MinutesCount(5));
        Assert.AreEqual("10 دقائق", ar.MinutesCount(10));
        Assert.AreEqual("11 دقيقة", ar.MinutesCount(11));
        Assert.AreEqual("42 دقيقة", ar.MinutesCount(42));
        Assert.AreEqual("103 دقائق", ar.MinutesCount(103));
        Assert.AreEqual("15 ثانية", ar.SecondsCount(15));
        Assert.AreEqual("3 ثوانٍ", ar.SecondsCount(3));
        Assert.AreEqual("ساعة و5 دقائق", ar.Duration(TimeSpan.FromMinutes(65)));
        Assert.AreEqual("ساعتين", ar.Duration(TimeSpan.FromHours(2)));
    }

    [TestMethod]
    public void TheWordAfterATypedNumberAgreesWithIt()
    {
        Assert.AreEqual("minute", English.Text.MinutesWord(1));
        Assert.AreEqual("minutes", English.Text.MinutesWord(0));
        Assert.AreEqual("minutes", English.Text.MinutesWord(45));
        var ar = Arabic.Text;
        Assert.AreEqual("دقيقة", ar.MinutesWord(1));
        Assert.AreEqual("دقيقتين", ar.MinutesWord(2));
        Assert.AreEqual("دقائق", ar.MinutesWord(3));
        Assert.AreEqual("دقائق", ar.MinutesWord(10));
        Assert.AreEqual("دقيقة", ar.MinutesWord(11));
        Assert.AreEqual("دقيقة", ar.MinutesWord(45));
        Assert.AreEqual("دقائق", ar.MinutesWord(105));
        Assert.AreEqual("دقيقة", ar.MinutesWord(1440));
        // The same word MinutesCount puts after the number.
        foreach (int n in new[] { 3, 7, 11, 45, 103, 1440 })
            Assert.AreEqual(ar.MinutesCount(n), $"{n} {ar.MinutesWord(n)}");
    }

    static ActiveEffect Music(int minutes) => new(Effect.Extra(minutes), EffectSource.MusicOrVideo);

    static ActiveEffect FullScreen(int minutes) => new(Effect.Extra(minutes), EffectSource.FullScreen);

    static ActiveEffect Battery(Effect effect) => new(effect, EffectSource.Battery);

    static LockStatus Status(LockState kind, double secondsLeft = 0, decimal normal = 30, params ActiveEffect[] effects) =>
        new() { Kind = kind, Time = LockTime.Compute(normal, effects), SecondsLeft = secondsLeft };

    [TestMethod]
    public void StatusLines()
    {
        var en = English.Text;
        var ar = Arabic.Text;
        Assert.AreEqual("On · locks in 13 min", en.Headline(Status(LockState.On, 750)));
        Assert.AreEqual("مفعَّل · يُقفل بعد 13 دقيقة", ar.Headline(Status(LockState.On, 750)));
        Assert.AreEqual("Locking off · away actions in 13 min", en.Headline(Status(LockState.LockingOff, 750)));
        Assert.AreEqual("Locking off · away actions in 13 min", en.Headline(Status(LockState.On, 750), lockingOn: false));
        var call = Status(LockState.Never, effects: new ActiveEffect(Effect.Never, EffectSource.Call));
        Assert.AreEqual("On · won't lock during a call", en.Headline(call));
        Assert.AreEqual("مفعَّل · لن يُقفل أثناء مكالمة", ar.Headline(call));
        Assert.AreEqual("Locking off · away actions won't start during a call", en.Headline(call, lockingOn: false));
        var app = Status(LockState.Never, effects: new ActiveEffect(Effect.Never, EffectSource.ForApp("obs64")));
        Assert.AreEqual("On · won't lock while obs64 is open", en.Headline(app));
        Assert.AreEqual("مفعَّل · لن يُقفل عندما يكون obs64 مفتوحًا", ar.Headline(app));
        Assert.AreEqual("On · won't lock during music or video", en.Headline(Status(LockState.On, 750, effects: new ActiveEffect(Effect.Never, EffectSource.MusicOrVideo))));
        var until = new DateTime(2026, 1, 1, 14, 5, 0);
        Assert.AreEqual("Paused · back on at " + Lang.ClockTime(until), en.Headline(Status(LockState.Paused) with { PausedUntil = until }));
        Assert.AreEqual("Paused · until turned back on", en.Headline(Status(LockState.Paused)));
        Assert.AreEqual("You're away", en.Headline(Status(LockState.Away)));
        Assert.AreEqual("The PC is locked", en.Headline(Status(LockState.SessionLocked)));
        Assert.AreEqual("Starting…", en.Headline(LockStatus.Starting(15)));
        Assert.AreEqual("On · locks in 13 min (dry run)", en.Headline(Status(LockState.On, 750) with { DryRun = true }));
    }

    [TestMethod]
    public void TimeLeftIsInWordsRoundedUp()
    {
        var en = English.Text;
        Assert.AreEqual("On · locks in 15 min", en.Headline(Status(LockState.On, 900)));
        Assert.AreEqual("On · locks in 15 min", en.Headline(Status(LockState.On, 899.4)));
        Assert.AreEqual("On · locks in 45 sec", en.Headline(Status(LockState.On, 44.2)));
        Assert.AreEqual("On · locks in 1 min", en.Headline(Status(LockState.On, 59.5)));
        Assert.AreEqual("On · locks in 1 hr", en.Headline(Status(LockState.On, 3599)));
        Assert.AreEqual("On · locks in 1 hr 30 min", en.Headline(Status(LockState.On, 5400)));
        Assert.AreEqual("مفعَّل · يُقفل بعد 15 دقيقة", Arabic.Text.Headline(Status(LockState.On, 900)));
        Assert.AreEqual("مفعَّل · يُقفل بعد دقيقتين", Arabic.Text.Headline(Status(LockState.On, 120)));
    }

    // Arabic writes the time of day itself: the hour without a leading zero, two-digit minutes and
    // ص or م after them, in Western digits and no Latin letter, whatever Windows' own format (I4).
    [TestMethod]
    public void ArabicWritesTheTimeOfDayInArabic()
    {
        var at = new DateTime(2026, 1, 1, 17, 12, 0);
        Assert.AreEqual("5:12 م", Strings.ArabicTime(at, "5:12 PM"));
        Assert.AreEqual("5:12 م", Strings.ArabicTime(at, "PM 5:12"));
        Assert.AreEqual("5:12 م", Strings.ArabicTime(at, "05:12 م"));
        Assert.AreEqual("9:05 ص", Strings.ArabicTime(new DateTime(2026, 1, 1, 9, 5, 0), "9:05 AM"));
        Assert.AreEqual("12:00 ص", Strings.ArabicTime(new DateTime(2026, 1, 1, 0, 0, 0), "12:00 AM"));
        Assert.AreEqual("12:30 م", Strings.ArabicTime(new DateTime(2026, 1, 1, 12, 30, 0), "12:30 PM"));
        // A 24-hour clock is digits alone and stays as Windows writes it.
        Assert.AreEqual("17:12", Strings.ArabicTime(at, "17:12"));
        Assert.AreEqual("05:07", Strings.ArabicTime(new DateTime(2026, 1, 1, 5, 7, 0), "05:07"));

        var ar = Arabic.Text;
        string headline = ar.Headline(Status(LockState.Paused) with { PausedUntil = at });
        Assert.AreEqual(string.Format(ar.V1.PausedUntil, Strings.ArabicTime(at, Lang.ClockTime(at))), headline);
        Assert.IsFalse(headline.Any(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z'), headline);
        Assert.IsFalse(headline.Any(c => c is >= '\u202A' and <= '\u202E'), "no embedding marks: " + headline);
        Assert.AreEqual("متوقف مؤقتًا · حتى الساعة 5:12 م", string.Format(ar.V1.PausedUntil, Strings.ArabicTime(at, "5:12 PM")));
        // English keeps Windows' own format.
        Assert.AreEqual(Lang.ClockTime(at), English.Text.ClockTime(at));
    }

    [TestMethod]
    public void ClockTimeKeepsTheMinutes()
    {
        string time = Lang.ClockTime(new DateTime(2026, 1, 1, 14, 5, 0));
        Assert.Contains("05", time);
        Assert.DoesNotContain("05:00", time);
    }

    [TestMethod]
    public void CountdownSaysAwayWhenLockingIsOff()
    {
        var countdown = Status(LockState.Countdown, 9);
        Assert.AreEqual("On · locks in 9 sec", English.Text.Headline(countdown));
        Assert.AreEqual("Locking off · away actions in 9 sec", English.Text.Headline(countdown, lockingOn: false));
        Assert.AreEqual("Locking in 9 sec · move the mouse or press a key to cancel", English.Text.CountdownNote(9));
        Assert.AreEqual("Away actions in 9 sec · move the mouse or press a key to cancel", English.Text.CountdownNote(8.2, lockingOn: false));
        Assert.AreEqual("القفل بعد 9 ثوانٍ · حرّك الماوس أو اضغط على مفتاح للإلغاء", Arabic.Text.CountdownNote(9));
        Assert.AreEqual("Locking in 5 sec · preview only, the PC won't lock", English.Text.PreviewNote(5));
    }

    // The status box: a headline, the sentence with the normal time, and why the time differs,
    // as the settings sketch works them out.
    [TestMethod]
    public void StatusCardSaysWhatIsHappening()
    {
        var en = English.Text;
        var ar = Arabic.Text;
        var plain = Status(LockState.On, 28 * 60);
        Assert.AreEqual("On · locks in 28 min", en.Headline(plain));
        Assert.AreEqual("The PC locks if you don't use the mouse or keyboard for 30 min", en.Sentence(plain));
        Assert.AreEqual("", en.Why(plain));
        Assert.AreEqual("Away actions start if you don't use the mouse or keyboard for 30 min", en.Sentence(plain, lockingOn: false));

        var extras = Status(LockState.On, 208 * 60, 30, Music(60), FullScreen(120));
        Assert.AreEqual("On · locks in 3 hr 28 min", en.Headline(extras));
        Assert.AreEqual("The PC locks if you don't use the mouse or keyboard for 30 min", en.Sentence(extras));
        Assert.AreEqual("30 min + 1 hr (music or video) + 2 hr (full screen)", en.Why(extras));
        Assert.AreEqual("30 دقيقة + ساعة (الموسيقى أو الفيديو) + ساعتان (ملء الشاشة)", ar.Why(extras));
        Assert.AreEqual("30 دقيقة + دقيقتان (المكالمة)", ar.Why(Status(LockState.On, 60, 30, new ActiveEffect(Effect.Extra(2), EffectSource.Call))));

        // His example: 30 min + 2 hr full screen, 50% sooner on battery, is 1 hr 15 min.
        var battery = Status(LockState.On, 75 * 60, 30, FullScreen(120), Battery(Effect.Sooner(50)));
        Assert.AreEqual("On · locks in 1 hr 15 min", en.Headline(battery));
        Assert.AreEqual("30 min + 2 hr (full screen), 50% sooner on battery", en.Why(battery));
        Assert.AreEqual("30 دقيقة + ساعتان (ملء الشاشة)، أقصر بنسبة 50% عند استخدام طاقة البطارية", ar.Why(battery));
        Assert.AreEqual("30 min, 50% sooner on battery", en.Why(Status(LockState.On, 60, 30, Battery(Effect.Sooner(50)))));
        // A cut that changes nothing is left out.
        Assert.AreEqual("", en.Why(Status(LockState.On, 30, 0.5m, Battery(Effect.Sooner(50)))));

        // A cap beats never, and is the whole story when it applies.
        var cap = Status(LockState.On, 600, 30, new ActiveEffect(Effect.Never, EffectSource.Call), Battery(Effect.Cap(10)));
        Assert.AreEqual("On · locks in 10 min", en.Headline(cap));
        Assert.AreEqual("Always locks after 10 min on battery", en.Why(cap));
        Assert.AreEqual("يُقفل دائمًا بعد 10 دقائق عند استخدام طاقة البطارية", ar.Why(cap));
        Assert.AreEqual("Always locks after 5 min while chrome is open",
            en.Why(Status(LockState.On, 60, 30, new ActiveEffect(Effect.Cap(5), EffectSource.ForApp("chrome")))));
        // A cap that doesn't bind says nothing.
        Assert.AreEqual("", en.Why(Status(LockState.On, 60, 15, Battery(Effect.Cap(30)))));
        // Never is said by the headline alone.
        Assert.AreEqual("", en.Why(Status(LockState.Never, 0, 30, Music(60), new ActiveEffect(Effect.Never, EffectSource.Call))));
    }

    [TestMethod]
    public void TrayTooltipAndMinutesOnTheIcon()
    {
        var en = English.Text;
        Assert.AreEqual("SteppedOut · On · locks in 28 min", en.Tooltip(Status(LockState.On, 28 * 60)));
        Assert.AreEqual("SteppedOut · On · locks in 24 sec", en.Tooltip(Status(LockState.Countdown, 24)));
        var until = new DateTime(2026, 1, 1, 17, 12, 0);
        Assert.AreEqual("SteppedOut · Paused · back on at " + Lang.ClockTime(until), en.Tooltip(Status(LockState.Paused) with { PausedUntil = until }));
        string longName = new('x', 300);
        string tip = en.Tooltip(Status(LockState.Never, effects: new ActiveEffect(Effect.Never, EffectSource.ForApp(longName))));
        Assert.AreEqual(Strings.TooltipLength, tip.Length);
        Assert.EndsWith("…", tip);
        Assert.StartsWith("SteppedOut · On · won't lock while xxx", tip);

        Assert.AreEqual("28", Strings.IconMinutes(Status(LockState.On, 27 * 60 + 1)));
        Assert.AreEqual("1", Strings.IconMinutes(Status(LockState.Countdown, 12)));
        Assert.AreEqual("99", Strings.IconMinutes(Status(LockState.On, 99 * 60)));
        Assert.IsNull(Strings.IconMinutes(Status(LockState.On, 99 * 60 + 1)));
        Assert.IsNull(Strings.IconMinutes(Status(LockState.Paused, 600)));
        Assert.IsNull(Strings.IconMinutes(Status(LockState.Never, 600, effects: new ActiveEffect(Effect.Never, EffectSource.Call))));
        Assert.IsNull(Strings.IconMinutes(Status(LockState.Away)));
    }

    [TestMethod]
    public void WhyWindowRows()
    {
        var en = English.Text;
        Assert.AreEqual(("Sound is playing in Spotify", "+1 hour"), en.WhyRow(new WhyLine(Signal.Sound, Effect.Extra(60), "Spotify")));
        Assert.AreEqual(("An app is keeping the screen on", "Never lock"), en.WhyRow(new WhyLine(Signal.Display, Effect.Never)));
        Assert.AreEqual(("Sound is playing in Teams", "Counted as the call"), en.WhyRow(new WhyLine(Signal.Sound, Effect.Extra(60), "Teams", Counts: false)));
        Assert.AreEqual(("The PC is on battery power", "50% sooner"), en.WhyRow(new WhyLine(Signal.Battery, Effect.Sooner(50))));
        Assert.AreEqual(("obs64 is open", "Always lock after 5 minutes"), en.WhyRow(new WhyLine(Signal.App, Effect.Cap(5), "obs64")));
        Assert.AreEqual(("تطبيق obs64 مفتوح", "+دقيقتان"), Arabic.Text.WhyRow(new WhyLine(Signal.App, Effect.Extra(2), "obs64")));
        Assert.AreEqual("Lock time right now: 1 hr 30 min", en.WhyLockTime(LockTime.Compute(30, new[] { Music(60) })));
        Assert.AreEqual("Lock time right now: never", en.WhyLockTime(LockTime.Compute(30, new[] { new ActiveEffect(Effect.Never, EffectSource.Call) })));
        Assert.AreEqual("Lock as usual", en.EffectLabel(Effect.None, en.V1.LockAsUsual));
        Assert.AreEqual("+45 minutes", en.EffectLabel(Effect.Extra(45), en.V1.NoExtraTime));
    }

    [TestMethod]
    public void AlreadyRunningSaysWhereOnlyWhenTheSettingsCannotOpen()
    {
        var en = English.Text;
        Assert.AreEqual("SteppedOut is already running. " + en.WelcomeWhere, en.AlreadyRunningMessage(settingsOpened: false, ""));
        Assert.AreEqual("SteppedOut is already running.", en.AlreadyRunningMessage(settingsOpened: true, ""));
        string withOptions = en.AlreadyRunningMessage(settingsOpened: true, "--dry-run --idle 0.5");
        Assert.Contains("These options were not used: --dry-run --idle 0.5.", withOptions);
        Assert.DoesNotContain(en.WelcomeWhere, withOptions);
        Assert.Contains(Arabic.Text.WelcomeWhere, Arabic.Text.AlreadyRunningMessage(false, "--dry-run"));
    }

    // Options given to a second start: the message names the tray menu's quit row as the menu
    // shows it, in both languages (I14), and keeps the options left to right in Arabic.
    [TestMethod]
    public void TheSecondStartNamesTheMenusQuitRow()
    {
        foreach (var text in new[] { English.Text, Arabic.Text })
        {
            string message = text.AlreadyRunningMessage(settingsOpened: true, "--dry-run");
            Assert.Contains(text.V1.QuitSteppedOut, message, message);
        }
        Assert.Contains("\u202A--dry-run\u202C", Arabic.Text.AlreadyRunningMessage(true, "--dry-run"));
        Assert.DoesNotContain("«خروج»", Arabic.Text.AlreadyRunningMessage(true, "--dry-run"));
    }

    // The mouse is الماوس in every Arabic line, the welcome's included, as the settings, the README
    // and the Store listing write it (I7).
    [TestMethod]
    public void ArabicCallsTheMouseAlMaws()
    {
        var ar = Arabic.Text;
        var lines = typeof(Strings).GetProperties().Where(p => p.PropertyType == typeof(string)).Select(p => (p.Name, (string)p.GetValue(ar)!))
            .Concat(typeof(V1Text).GetProperties().Where(p => p.PropertyType == typeof(string)).Select(p => (p.Name, (string)p.GetValue(ar.V1)!)))
            .Append(("AlreadyRunningMessage", ar.AlreadyRunningMessage(false, "--dry-run")))
            .ToList();
        Assert.IsGreaterThan(200, lines.Count);
        foreach (var (name, line) in lines) Assert.DoesNotContain("الفأرة", line, name);
        Assert.Contains("الماوس", ar.WelcomeWhere);
    }

    // The README's demo is drawn by the render tool, with captions of its own: the mouse is الماوس
    // there too, and the caption over the cancelled countdown says a key cancels it, as the note in
    // the picture does (I7, I32).
    [TestMethod]
    public void TheDemosCaptionsSayWhatTheAppSays()
    {
        string demo = File.ReadAllText(Path.Combine(TextPipelineTests.Root, "tools", "shots", "Demo.cs"));
        Assert.DoesNotContain("الفأرة", demo);
        Assert.Contains("\"Move the mouse or press a key to cancel it\"", demo);
        Assert.Contains("\"حرّك الماوس أو اضغط أي مفتاح لإلغائه\"", demo);
    }

    // A key cancels the countdown as well as the mouse, so the note and the Warn me row say so (I32).
    [TestMethod]
    public void TheCountdownSaysAKeyCancelsItToo()
    {
        Assert.AreEqual("Locking in 15 sec · move the mouse or press a key to cancel", English.Text.CountdownNote(15));
        Assert.AreEqual("Away actions in 15 sec · move the mouse or press a key to cancel", English.Text.CountdownNote(15, lockingOn: false));
        Assert.AreEqual("القفل بعد 15 ثانية · حرّك الماوس أو اضغط على مفتاح للإلغاء", Arabic.Text.CountdownNote(15));
        Assert.AreEqual("إجراءات المغادرة بعد 15 ثانية · حرّك الماوس أو اضغط على مفتاح للإلغاء", Arabic.Text.CountdownNote(15, lockingOn: false));
        Assert.AreEqual("Move the mouse or press a key during the countdown to cancel the lock", English.Text.V1.WarnMeDetail);
        Assert.AreEqual("حرّك الماوس أو اضغط على مفتاح للإلغاء", Arabic.Text.V1.WarnMeDetail);
    }

    [TestMethod]
    public void OnlyArabicIsRightToLeft()
    {
        Assert.IsFalse(English.Text.RightToLeft);
        Assert.IsTrue(Arabic.Text.RightToLeft);
        Assert.AreSame(English.Text, Lang.For("en"));
        Assert.AreSame(Arabic.Text, Lang.For("ar"));
    }
}
