using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SteppedOut.Tests;

// The version 1 text: generated from tools/text, every approved line in it once, and nothing that
// must not ship.
[TestClass]
public class TextPipelineTests
{
    static readonly Strings En = English.Text;
    static readonly Strings Ar = Arabic.Text;

    static readonly PropertyInfo[] Lines = typeof(V1Text).GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.PropertyType == typeof(string)).ToArray();

    static string Value(V1Text text, string name) => (string)typeof(V1Text).GetProperty(name)!.GetValue(text)!;

    internal static string Root
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "tools", "text", "approved.json"))) return dir.FullName;
            }
            throw new InvalidOperationException("tools/text/approved.json not found above the test folder");
        }
    }

    static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

    static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    // The same hash tools/text/gen.cs puts in each generated file.
    static string InputsHash(string root)
    {
        var files = new List<string> { "tools/text/gen.cs", "tools/text/approved.json", "tools/text/names.json" };
        files.AddRange(Directory.GetFiles(Path.Combine(root, "tools", "text", "new"), "*.json")
            .Select(f => Path.GetFileName(f)).OrderBy(f => f, StringComparer.Ordinal).Select(f => "tools/text/new/" + f));
        var b = new StringBuilder();
        foreach (string file in files) b.Append(file).Append('\n').Append(Read(Path.Combine(root, file))).Append('\n');
        return Sha(b.ToString());
    }

    sealed record Approved(int Id, string En, string Ar, JsonElement Entry);

    static List<Approved> ApprovedLines()
    {
        using var approved = JsonDocument.Parse(Read(Path.Combine(Root, "tools", "text", "approved.json")));
        using var names = JsonDocument.Parse(Read(Path.Combine(Root, "tools", "text", "names.json")));
        var entries = names.RootElement.EnumerateArray().ToDictionary(e => e.GetProperty("id").GetInt32(), e => e.Clone());
        return approved.RootElement.EnumerateArray()
            .Select(a => new Approved(a.GetProperty("id").GetInt32(), a.GetProperty("en").GetString()!, a.GetProperty("ar").GetString()!, entries[a.GetProperty("id").GetInt32()]))
            .ToList();
    }

    static bool Flag(JsonElement entry, string flag) => entry.TryGetProperty(flag, out var value) && value.ValueKind == JsonValueKind.True;

    [TestMethod]
    public void TheGeneratedFilesAreUpToDate()
    {
        string root = Root;
        string hash = InputsHash(root);
        Assert.AreEqual(V1Text.InputsHash, hash, "run: dotnet run tools/text/gen.cs");
        foreach (string file in new[] { "V1Text.g.cs", "EnglishV1.g.cs", "ArabicV1.g.cs" })
        {
            string text = Read(Path.Combine(root, "src", "Text", "V1", file));
            StringAssert.Contains(text, "// inputs: " + hash + "\n", file);
            // The last line is the hash of everything above it, so a hand edit shows.
            var last = Regex.Match(text, @"// output: ([0-9a-f]{64})\n$");
            Assert.IsTrue(last.Success, file);
            Assert.AreEqual(Sha(text[..last.Index]), last.Groups[1].Value, file + " was edited by hand; run: dotnet run tools/text/gen.cs");
        }
    }

    [TestMethod]
    public void EveryApprovedLineIsInTheTextExactlyOnce()
    {
        var lines = ApprovedLines();
        Assert.HasCount(179, lines);
        var shipped = lines.Where(l => !Flag(l.Entry, "excluded") && l.Entry.GetProperty("kind").GetString() != "formatted").ToList();
        foreach (var line in shipped)
        {
            string ar = Flag(line.Entry, "sameInArabic") ? line.En : line.Ar;
            string? exampleEn = null, exampleAr = null;
            if (line.Entry.TryGetProperty("template", out var example))
            {
                exampleEn = example.GetProperty("en").GetString();
                exampleAr = example.GetProperty("ar").GetString();
            }
            // Two lines may share their words in one language, such as إيقاف مؤقت for Pause and for the
            // welcome's Stop; the pair of English and Arabic is in the text exactly once.
            if (exampleEn is null)
            {
                int pairs = Lines.Count(p => (string)p.GetValue(EnglishV1.Text)! == line.En && (string)p.GetValue(ArabicV1.Text)! == ar);
                Assert.AreEqual(1, pairs, $"line {line.Id} \"{line.En}\" / \"{ar}\"");
                continue;
            }
            foreach (var (text, approved, sample) in new (object, string, string?)[] { (EnglishV1.Text, line.En, exampleEn), (ArabicV1.Text, ar, exampleAr) })
            {
                // A template is its own named line, which with its example put back is the approved line.
                string value = (string)typeof(V1Text).GetProperty(line.Entry.GetProperty("name").GetString()!)!.GetValue(text)!;
                Assert.Contains("{0}", value, $"line {line.Id}");
                Assert.AreEqual(approved, value.Replace("{0}", sample), $"line {line.Id}");
            }
        }
    }

    [TestMethod]
    public void TheLiveExamplesComeOutOfTheFormatters()
    {
        foreach (var (s, ar) in new[] { (En, false), (Ar, true) })
        {
            var lines = ApprovedLines().ToDictionary(l => l.Id, l => ar ? l.Ar : l.En);
            Assert.AreEqual(lines[4], string.Format(s.V1.OnLocksIn, s.ShortDuration(28m)));
            Assert.AreEqual(lines[5], string.Format(s.V1.LocksIfIdleFor, s.ShortDuration(30m)));
            Assert.AreEqual(lines[7], s.ShortDuration(80m));
            Assert.AreEqual(lines[7], s.AwayToday(TimeSpan.FromMinutes(80.7)));
            Assert.AreEqual(lines[9], s.Times(3));
            Assert.AreEqual(lines[81], s.YouWereAwayFor(TimeSpan.FromSeconds(42 * 60 + 50)));
            Assert.AreEqual(lines[114], s.AtThePcFor(TimeSpan.FromHours(1)));
            Assert.AreEqual(lines[146], s.VersionLine("1.0.0"));
        }
    }

    [TestMethod]
    public void TheExampleAppsAreNowhere()
    {
        foreach (var text in new[] { EnglishV1.Text, ArabicV1.Text })
        {
            foreach (var p in Lines)
            {
                string value = (string)p.GetValue(text)!;
                foreach (string name in new[] { "OBS", "Zoom", "WhatsApp", "Google Chrome" })
                    Assert.IsFalse(Regex.IsMatch(value, $@"\b{name}\b"), $"{p.Name}: {value}");
            }
        }
        var excluded = ApprovedLines().Where(l => Flag(l.Entry, "excluded")).Select(l => l.Id).Order().ToArray();
        // The Lock the PC when you're away switch and its line, which became At the lock time's Don't
        // lock, the sketch's example apps, the button color row's five lines, which went with the row,
        // the Audio heading and its line, the three sounds before 1.0's own (Click, Chime, Soft chime),
        // and the three silence lengths, which read the same as the warning's 10 seconds, 30 seconds and
        // 1 minute in both languages.
        CollectionAssert.AreEqual(new[] { 48, 49, 58, 60, 62, 63, 102, 103, 104, 137, 138, 144, 160, 161, 162, 163, 167, 172, 173 }, excluded);
        foreach (string gone in new[] { "ButtonColor", "ButtonColorDetail", "Green", "Blue", "Purple", "Click", "Chime", "SoftChime",
            "SoundDetection", "SoundDetectionIntro", "CustomLoudness", "LockWhenAway", "LockWhenAwayDetail", "WhenLocking", "WhenLockingDetail",
            "Silence10", "Silence30", "Silence60", "CustomExtraTwo", "DaysCount" })
            Assert.IsNull(typeof(V1Text).GetProperty(gone), $"{gone} no longer ships");
        Assert.IsNotNull(typeof(V1Text).GetProperty("System"), "System is still the theme's and the text size's");
    }

    [TestMethod]
    public void EveryPlaceholderMatchesBetweenTheLanguages()
    {
        foreach (var p in Lines)
        {
            string en = (string)p.GetValue(EnglishV1.Text)!;
            string ar = (string)p.GetValue(ArabicV1.Text)!;
            var holes = (string s) => Regex.Matches(s, @"\{(\d+)\}").Select(m => m.Groups[1].Value).Distinct().Order().ToArray();
            CollectionAssert.AreEqual(holes(en), holes(ar), p.Name);
            Assert.AreNotEqual("", en.Trim(), p.Name);
            Assert.AreNotEqual("", ar.Trim(), p.Name);
            _ = string.Format(en, "a", "b", "c");
            _ = string.Format(ar, "a", "b", "c");
        }
    }

    [TestMethod]
    public void EachLanguageGetsItsOwnText()
    {
        Assert.AreSame(EnglishV1.Text, En.V1);
        Assert.AreSame(ArabicV1.Text, Ar.V1);
        Assert.AreSame(V1Words.English, En.V1.Words);
        Assert.AreSame(V1Words.Arabic, Ar.V1.Words);
        // The two lines that read the same in both languages are filled in, never blank.
        Assert.AreEqual("English", Ar.V1.LanguageEnglish);
        Assert.AreEqual("Ctrl+Alt+L", Ar.V1.CtrlAltL);
        Assert.AreEqual("العربية", En.V1.LanguageArabic);
    }

    [TestMethod]
    public void ShortDurations()
    {
        Assert.AreEqual("28 min", En.ShortDuration(28m));
        Assert.AreEqual("1 hr", En.ShortDuration(60m));
        Assert.AreEqual("1 hr 20 min", En.ShortDuration(80m));
        Assert.AreEqual("2 hr", En.ShortDuration(120m));
        Assert.AreEqual("30 sec", En.ShortDuration(0.5m));
        // A hand-set fraction reads to the nearest minute.
        Assert.AreEqual("2 min", En.ShortDuration(1.5m));
        Assert.AreEqual("دقيقتين", Ar.ShortDuration(2m));
        Assert.AreEqual("دقيقتان", Ar.ShortDuration(2m, standalone: true));
        Assert.AreEqual("ساعتين", Ar.ShortDuration(120m));
        Assert.AreEqual("ساعتان", Ar.ShortDuration(120m, standalone: true));
        Assert.AreEqual("ساعة و20 دقيقة", Ar.ShortDuration(80m));
        Assert.AreEqual("ساعتين ودقيقتين", Ar.ShortDuration(122m));
        Assert.AreEqual("3 ساعات و5 دقائق", Ar.ShortDuration(185m));
        Assert.AreEqual("30 ثانية", Ar.ShortDuration(0.5m));
        Assert.AreEqual("3 ثوانٍ", Ar.ShortDuration(0.05m));
        // Arabic counts by the last two digits: 103 takes the plural, 111 the singular.
        Assert.AreEqual("103 دقائق", Ar.V1.Words.Time(0, 103, true, false));
        Assert.AreEqual("111 دقيقة", Ar.V1.Words.Time(0, 111, true, false));
        Assert.AreEqual("11 ساعة", Ar.V1.Words.Time(11, 0, true, false));
    }

    [TestMethod]
    public void TimeLeftRoundsUpAndAwayRoundsDown()
    {
        Assert.AreEqual("15 min", En.ShortLeft(900));
        Assert.AreEqual("15 min", En.ShortLeft(899.4));
        Assert.AreEqual("1 min", En.ShortLeft(59.5));
        Assert.AreEqual("2 min", En.ShortLeft(60.1));
        Assert.AreEqual("45 sec", En.ShortLeft(44.2));
        Assert.AreEqual("1 hr", En.ShortLeft(3599));
        Assert.AreEqual("1 hr 30 min", En.ShortLeft(5400));
        Assert.AreEqual("1 sec", En.ShortLeft(0));
        Assert.AreEqual("دقيقتين", Ar.ShortLeft(120));
        Assert.AreEqual("42 min", En.ShortDuration(TimeSpan.FromSeconds(42 * 60 + 59)));
        Assert.AreEqual("40 sec", En.ShortDuration(TimeSpan.FromSeconds(40)));
        Assert.AreEqual("0 min", En.AwayToday(TimeSpan.Zero));
        Assert.AreEqual("0 دقيقة", Ar.AwayToday(TimeSpan.FromSeconds(30)));
        // After the label's colon the time stands alone: ساعتان and دقيقتان (I37).
        Assert.AreEqual("ساعتان", Ar.AwayToday(TimeSpan.FromHours(2)));
        Assert.AreEqual("دقيقتان", Ar.AwayToday(TimeSpan.FromMinutes(2)));
        Assert.AreEqual("ساعتان ودقيقتان", Ar.AwayToday(TimeSpan.FromMinutes(122)));
        Assert.AreEqual("2 hr 2 min", En.AwayToday(TimeSpan.FromMinutes(122)));
    }

    [TestMethod]
    public void TimesCount()
    {
        Assert.AreEqual("0 times", En.Times(0));
        Assert.AreEqual("1 time", En.Times(1));
        Assert.AreEqual("3 times", En.Times(3));
        Assert.AreEqual("مرة واحدة", Ar.Times(1));
        Assert.AreEqual("12", Ar.Times(12));
    }

    // Values a list holds come from the approved lines; the counting rules agree with them, so a
    // typed value reads like its neighbours.
    [TestMethod]
    public void ListValuesUseTheApprovedLinesAndTheRulesAgreeWithThem()
    {
        var lines = ApprovedLines().ToDictionary(l => l.Id);
        foreach (var (s, ar) in new[] { (En, false), (Ar, true) })
        {
            string L(int id) => ar ? lines[id].Ar : lines[id].En;
            Assert.AreEqual(L(174), s.OptionDuration(5));
            Assert.AreEqual(L(117), s.OptionDuration(60));
            Assert.AreEqual(L(178), s.SecondsOption(10));
            Assert.AreEqual(L(179), s.SecondsOption(60));
            Assert.AreEqual(L(158), s.ExtraLabel(15));
            Assert.AreEqual(L(23), s.ExtraLabel(120));
            Assert.AreEqual(L(47), s.SoonerLabel(50));
            Assert.AreEqual(L(152), s.AlwaysAfterLabel(2));
            Assert.AreEqual(L(156), s.AlwaysAfterLabel(60));
            Assert.AreEqual(L(172), s.SilenceLabel(10));
            Assert.AreEqual(L(173), s.SilenceLabel(60));
            Assert.AreEqual(L(124), s.SnoozeLabel(10));
            Assert.AreEqual(L(168), s.SnoozeLabel(30));
            // The templates behind Custom… give the approved wording for the approved values.
            foreach (var (minutes, id) in new[] { (15, 158), (30, 159), (60, 17), (120, 23) })
                Assert.AreEqual(L(id), string.Format(s.V1.CustomExtra, s.V1.Words.Time(minutes / 60, minutes % 60, false, true)));
            foreach (var (percent, id) in new[] { (25, 149), (50, 47), (75, 150) })
                Assert.AreEqual(L(id), string.Format(s.V1.CustomSooner, percent));
            foreach (var (minutes, id) in new[] { (2, 152), (5, 64), (10, 153), (15, 154), (30, 155), (60, 156) })
                Assert.AreEqual(L(id), string.Format(s.V1.CustomAlwaysAfter, s.V1.Words.Time(minutes / 60, minutes % 60, false, false)));
            foreach (var (minutes, id) in new[] { (10, 124), (30, 168) })
                Assert.AreEqual(L(id), string.Format(s.V1.CustomLater, s.V1.Words.Time(0, minutes, false, false)));
            foreach (var (seconds, id) in new[] { (10, 172), (30, 144) })
                Assert.AreEqual(L(id), string.Format(s.V1.CustomSilence, s.V1.Words.Seconds(seconds, false, true)));
            foreach (var (minutes, id) in new[] { (5, 174), (10, 175), (15, 176), (30, 14) })
                Assert.AreEqual(L(id), s.V1.Words.Time(0, minutes, false, true));
        }
    }

    [TestMethod]
    public void TypedValues()
    {
        Assert.AreEqual("45 minutes", En.OptionDuration(45));
        Assert.AreEqual("1 hour 30 minutes", En.OptionDuration(90));
        Assert.AreEqual("3 hours", En.OptionDuration(180));
        Assert.AreEqual("دقيقتان", Ar.OptionDuration(2));
        Assert.AreEqual("ساعة و15 دقيقة", Ar.OptionDuration(75));
        Assert.AreEqual("+45 minutes", En.ExtraLabel(45));
        Assert.AreEqual("+45 دقيقة", Ar.ExtraLabel(45));
        Assert.AreEqual("+دقيقتان", Ar.ExtraLabel(2));
        Assert.AreEqual("+ساعة ودقيقتان", Ar.ExtraLabel(62));
        Assert.AreEqual("+5 دقائق", Ar.ExtraLabel(5));
        Assert.AreEqual("40% sooner", En.SoonerLabel(40));
        Assert.AreEqual("أقصر بنسبة 40%", Ar.SoonerLabel(40));
        Assert.AreEqual("Always lock after 45 minutes", En.AlwaysAfterLabel(45));
        Assert.AreEqual("القفل بعد ساعتين", Ar.AlwaysAfterLabel(120));
        Assert.AreEqual("45 seconds", En.SecondsOption(45));
        Assert.AreEqual("ثانيتان", Ar.SecondsOption(2));
        Assert.AreEqual("2 minutes", En.SilenceLabel(120));
        Assert.AreEqual("دقيقتان", Ar.SilenceLabel(120));
        Assert.AreEqual("بعد 5 دقائق", Ar.SnoozeLabel(5));
        Assert.AreEqual("(15 min)", En.PercentNote(15));
        Assert.AreEqual("(15 دقيقة)", Ar.PercentNote(15));
    }

    // The one pause list the tray menu and the status box share (MenuLook.PauseChoices).
    [TestMethod]
    public void PauseChoices()
    {
        Assert.AreEqual("15 minutes", En.PauseChoice(15));
        Assert.AreEqual("30 minutes", En.PauseChoice(30));
        Assert.AreEqual("1 hour", En.PauseChoice(60));
        Assert.AreEqual("Until turned back on", En.PauseChoice(null));
        Assert.AreEqual("15 دقيقة", Ar.PauseChoice(15));
        Assert.AreEqual("ساعة واحدة", Ar.PauseChoice(60));
        Assert.AreEqual(Ar.V1.UntilTurnedBackOn, Ar.PauseChoice(null));
    }

    // Whole days count the Arabic way, like minutes and hours: one and two by the word alone, 3 to 10
    // with the plural, 11 and up with the singular.
    [TestMethod]
    public void DaysCountTheArabicWay()
    {
        Assert.AreEqual("يوم", V1Words.Arabic.Days(1, standalone: false));
        Assert.AreEqual("يومان", V1Words.Arabic.Days(2, standalone: true));
        Assert.AreEqual("يومين", V1Words.Arabic.Days(2, standalone: false));
        Assert.AreEqual("3 أيام", V1Words.Arabic.Days(3, standalone: true));
        Assert.AreEqual("10 أيام", V1Words.Arabic.Days(10, standalone: true));
        Assert.AreEqual("11 يومًا", V1Words.Arabic.Days(11, standalone: true));
        Assert.AreEqual("1 day", V1Words.English.Days(1, standalone: true));
        Assert.AreEqual("11 days", V1Words.English.Days(11, standalone: true));
        const int day = 24 * 60;
        Assert.AreEqual(Ar.V1.Day1, Ar.PauseLength(day));
        Assert.AreEqual("يومان", Ar.PauseLength(2 * day));
        Assert.AreEqual("3 أيام", Ar.PauseLength(3 * day));
        Assert.AreEqual("7 أيام", Ar.PauseLength(7 * day));
        Assert.AreEqual("10 أيام", Ar.PauseLength(10 * day));
        Assert.AreEqual("11 يومًا", Ar.PauseLength(11 * day));
        Assert.AreEqual("2 days", En.PauseLength(2 * day));
    }

    // A time that stands alone, as a term in a sum, in a bar or in brackets, is said with the
    // nominative two-form like the list items beside it: ساعتان + ساعة, never ساعتين + ساعة. After a
    // word such as بعد or لمدة it keeps ساعتين.
    [TestMethod]
    public void ATimeThatStandsAloneTakesTheNominativeTwoForm()
    {
        static LockStatus Status(decimal normal, params ActiveEffect[] effects) =>
            new() { Kind = LockState.On, Time = LockTime.Compute(normal, effects), SecondsLeft = 600 };
        Assert.AreEqual("ساعتان + ساعة (الموسيقى أو الفيديو)", Ar.Why(Status(120, new ActiveEffect(Effect.Extra(60), EffectSource.MusicOrVideo))));
        Assert.AreEqual("دقيقتان + دقيقتان (المكالمة)", Ar.Why(Status(2, new ActiveEffect(Effect.Extra(2), EffectSource.Call))));
        Assert.AreEqual("ساعتان + دقيقتان (المكالمة)، أقصر بنسبة 50% عند استخدام طاقة البطارية",
            Ar.Why(Status(120, new ActiveEffect(Effect.Extra(2), EffectSource.Call), new ActiveEffect(Effect.Sooner(50), EffectSource.Battery))));
        Assert.AreEqual("ساعتان، أقصر بنسبة 50% عند استخدام طاقة البطارية", Ar.Why(Status(120, new ActiveEffect(Effect.Sooner(50), EffectSource.Battery))));
        Assert.AreEqual("2 hr + 1 hr (music or video)", En.Why(Status(120, new ActiveEffect(Effect.Extra(60), EffectSource.MusicOrVideo))));
        Assert.AreEqual("(ساعتان)", Ar.PercentNote(120));
        Assert.AreEqual("(دقيقتان)", Ar.PercentNote(2));
        Assert.AreEqual("(15 دقيقة)", Ar.PercentNote(15));
        Assert.AreEqual("ساعتان", WelcomeTimeline.BarWords(Ar, 120));
        Assert.AreEqual("15 دقيقة", WelcomeTimeline.BarWords(Ar, 15));
        Assert.AreEqual("2 hr", WelcomeTimeline.BarWords(En, 120));
        // After a preposition the same time is the other form.
        Assert.AreEqual("مفعَّل · يُقفل بعد ساعتين", Ar.Headline(Status(120, Array.Empty<ActiveEffect>()) with { SecondsLeft = 7200 }));
        Assert.AreEqual("يُقفل الكمبيوتر إذا لم تستخدم الماوس أو لوحة المفاتيح لمدة ساعتين", Ar.Sentence(Status(120)));
    }

    // The two questions that wrap to a second or third line end in words bound with no-break spaces,
    // so a single word and its full stop are never left alone on the last line.
    [TestMethod]
    public void TheEndOfAQuestionStaysTogether()
    {
        Assert.EndsWith("وتبقى اللغة كما هي.", Ar.V1.ResetBody);
        Assert.EndsWith("التراجع عن هذا الإجراء.", Ar.V1.RemoveBody);
        Assert.DoesNotContain(" ", En.V1.ResetBody);
    }

    // A name in quotes, such as "القفل بعد", is one thing to find in the settings, so it never breaks
    // across two lines: its words are bound with no-break spaces.
    [TestMethod]
    public void ANameInQuotesStaysOnOneLine()
    {
        var lines = typeof(Strings).GetProperties().Where(p => p.PropertyType == typeof(string)).Select(p => (p.Name, (string)p.GetValue(Ar)!))
            .Concat(Lines.Select(p => (p.Name, (string)p.GetValue(ArabicV1.Text)!)))
            .ToList();
        int quoted = 0;
        foreach (var (name, line) in lines)
        {
            // The message box about options names the tray menu's quit row exactly as the menu writes it,
            // and the system wraps a message box itself.
            if (name == "OptionsNotUsed") continue;
            foreach (Match name2 in Regex.Matches(line, "\"([^\"]+)\""))
            {
                quoted++;
                Assert.DoesNotContain(" ", name2.Groups[1].Value, $"{name}: {name2.Value}");
            }
        }
        Assert.IsGreaterThan(5, quoted);
    }

    // A list item for one minute is دقيقة واحدة wherever it comes from, like the hour's ساعة واحدة.
    [TestMethod]
    public void OneMinuteInAListIsOneMinute()
    {
        Assert.AreEqual("1 minute", En.OptionDuration(1));
        Assert.AreEqual("دقيقة واحدة", Ar.OptionDuration(1));
        Assert.AreEqual(Ar.V1.Minute1, Ar.SecondsOption(60));
        Assert.AreEqual(Ar.OptionDuration(1), Ar.SecondsOption(60));
        Assert.AreEqual("دقيقة واحدة", Ar.PauseLength(1));
        Assert.AreEqual("اكتب عدد الدقائق. القيمة المستخدمة الآن: دقيقة واحدة.", string.Format(Ar.MinutesNotANumber, Ar.OptionDuration(1)));
    }

    // The Arabic text is Arabic: the only Latin words in it are the app's name, Windows, keys and
    // shortcuts, file names and types, the sound names and English as the language's own name.
    [TestMethod]
    public void TheArabicHasNoLatinWordsButTheAllowedOnes()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "SteppedOut", "Windows", "Ctrl", "Alt", "Shift", "Win", "Esc", "L", "F", "config.json",
            "bat", "cmd", "ps1", "lnk", "exe", "wav", "PowerShell",
            "Drift", "Pebble", "Glow", "Ripple", "Dawn", "Meadow", "Lantern", "Tide", "English",
        };
        var lines = typeof(Strings).GetProperties().Where(p => p.PropertyType == typeof(string)).Select(p => (p.Name, (string)p.GetValue(Ar)!))
            .Concat(Lines.Select(p => (p.Name, (string)p.GetValue(ArabicV1.Text)!)))
            .ToList();
        Assert.IsGreaterThan(350, lines.Count);
        foreach (var (name, line) in lines)
        {
            foreach (Match word in Regex.Matches(Regex.Replace(line, @"\{\d+\}", ""), @"[A-Za-z][A-Za-z0-9]*(\.json)?"))
                Assert.Contains(word.Value, allowed, $"{name}: \"{word.Value}\" in {line}");
        }
    }
}
