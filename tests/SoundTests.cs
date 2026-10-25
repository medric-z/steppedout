using System.Buffers.Binary;
using System.Reflection;

namespace SteppedOut.Tests;

// The sounds' checks, your own sound, and the sound lists in the settings. Nothing here is heard:
// the lists play through a stand-in Preview, and the away actions through the fake player.
[TestClass]
[DoNotParallelize]
public class SoundTests
{
    string dir = "";

    [TestInitialize]
    public void Start()
    {
        dir = Path.Combine(Path.GetTempPath(), "SteppedOut.Tests", "sounds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    [TestCleanup]
    public void End()
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // ---- The generator's checks (tools/sounds/SoundCheck.cs) ----

    // A note at hz that rises over attack seconds, falls away and fades out, its peak at peakDb.
    static short[] Note(double hz = 440, double seconds = 0.8, double attack = 0.02, double peakDb = -18, double fade = 0.1)
    {
        int n = (int)(seconds * 44100);
        var x = new double[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / 44100.0;
            double rise = attack <= 0 ? 1 : Math.Min(1, t / attack);
            double tail = fade <= 0 ? 1 : Math.Min(1, (seconds - t) / fade);
            x[i] = Math.Sin(2 * Math.PI * hz * t) * rise * Math.Exp(-t / 0.3) * Math.Max(0, tail);
        }
        double peak = x.Max(Math.Abs);
        double scale = Math.Pow(10, peakDb / 20) / peak;
        return x.Select(v => (short)Math.Round(v * scale * 32767)).ToArray();
    }

    static List<string> Problems(short[] samples, double? set = null)
    {
        var m = SoundCheck.Of(samples);
        return SoundCheck.Problems(m, set ?? m.LoudnessDb);
    }

    [TestMethod]
    public void AGentleNotePassesEveryCheck()
    {
        CollectionAssert.AreEqual(Array.Empty<string>(), Problems(Note()));
        var m = SoundCheck.Of(Note());
        Assert.AreEqual(-18, m.PeakDb, 0.1);
        Assert.IsGreaterThanOrEqualTo(10.0, m.AttackMs);
        Assert.AreEqual(440, m.CentroidHz, 60);
        Assert.IsLessThan(0.01, m.HighShare);
    }

    [TestMethod]
    public void ALoudSoundFails() => Assert.IsTrue(Problems(Note(peakDb: -3)).Any(p => p.StartsWith("too loud", StringComparison.Ordinal)));

    [TestMethod]
    public void ASoundThatStartsAtFullLevelFailsAsClicky() => Assert.IsTrue(Problems(Note(attack: 0)).Any(p => p.StartsWith("clicky", StringComparison.Ordinal)));

    [TestMethod]
    public void ABrightSoundFailsAsHarsh()
    {
        var problems = Problems(Note(hz: 6000));
        Assert.IsTrue(problems.Any(p => p.Contains("centroid", StringComparison.Ordinal)), string.Join("; ", problems));
        Assert.IsTrue(problems.Any(p => p.Contains("above 4000 Hz", StringComparison.Ordinal)), string.Join("; ", problems));
    }

    [TestMethod]
    public void ASoundTooShortOrTooLongFails()
    {
        Assert.IsTrue(Problems(Note(seconds: 0.3)).Any(p => p.StartsWith("lasts", StringComparison.Ordinal)));
        Assert.IsTrue(Problems(Note(seconds: 2.0)).Any(p => p.StartsWith("lasts", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ASoundCutOffWithoutAFadeFails() => Assert.IsTrue(Problems(Note(fade: 0)).Any(p => p.StartsWith("ends abruptly", StringComparison.Ordinal)));

    [TestMethod]
    public void ASoundMuchLouderOrQuieterThanTheSetFails()
    {
        var m = SoundCheck.Of(Note());
        Assert.IsTrue(SoundCheck.Problems(m, m.LoudnessDb + 3).Any(p => p.StartsWith("loudness", StringComparison.Ordinal)));
        Assert.IsTrue(SoundCheck.Problems(m, m.LoudnessDb - 3).Any(p => p.StartsWith("loudness", StringComparison.Ordinal)));
        Assert.IsEmpty(SoundCheck.Problems(m, m.LoudnessDb + 1));
    }

    // ---- Your own sound ----

    // A .wav file of 16-bit mono PCM at 8 kHz this many seconds long, padded to at least size bytes.
    string WavFile(string name, double seconds, long size = 0, int tag = 1)
    {
        int data = (int)(seconds * 8000) * 2;
        var bytes = new byte[Math.Max(44 + data, size)];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 36 + data);
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), (short)tag);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), 8000);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), 16000);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), 16);
        "data"u8.CopyTo(bytes.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), data);
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    static DriveType Network(char letter) => letter == 'Z' ? DriveType.Network : DriveType.Fixed;

    [TestMethod]
    public void AShortWavOnThisPCCanBeYourOwnSound()
    {
        Assert.IsNull(OwnSound.Problem(WavFile("rain.wav", 2)));
        Assert.IsNull(OwnSound.Problem(WavFile("ten.wav", 10)), "ten seconds is the longest");
        Assert.IsNull(OwnSound.Problem("  \"" + WavFile("quoted.wav", 1) + "\" "), "quotes and spaces around the path are left out");
        Assert.IsNull(OwnSound.Problem(WavFile("float.wav", 1, tag: 3)), "floating point sound plays too");
    }

    [TestMethod]
    public void AFileOnAnotherPCIsRefusedAsAScriptIs()
    {
        Assert.AreEqual(OwnSoundProblem.AnotherPC, OwnSound.Problem(@"\\server\share\rain.wav", Network));
        Assert.AreEqual(OwnSoundProblem.AnotherPC, OwnSound.Problem("//server/share/rain.wav", Network));
        Assert.AreEqual(OwnSoundProblem.AnotherPC, OwnSound.Problem(@"Z:\sounds\rain.wav", Network));
        Assert.AreEqual(OwnSoundProblem.CantPlay, OwnSound.Problem(@"\\?\C:\rain.wav", Network), "a device path");
        Assert.AreEqual(OwnSoundProblem.CantPlay, OwnSound.Problem("rain.wav", Network), "not a full path");
        Assert.AreEqual(OwnSoundProblem.CantPlay, OwnSound.Problem("", Network));
    }

    [TestMethod]
    public void AFileTooBigTooLongOrNotASoundIsRefused()
    {
        Assert.AreEqual(OwnSoundProblem.TooBig, OwnSound.Problem(WavFile("big.wav", 1, size: OwnSound.LargestBytes + 1)));
        Assert.IsNull(OwnSound.Problem(WavFile("five.wav", 1, size: OwnSound.LargestBytes)), "5 MB is the largest");
        Assert.AreEqual(OwnSoundProblem.TooLong, OwnSound.Problem(WavFile("long.wav", 10.5)));
        Assert.AreEqual(OwnSoundProblem.CantPlay, OwnSound.Problem(Path.Combine(dir, "missing.wav")));
        Assert.AreEqual(OwnSoundProblem.CantPlay, OwnSound.Problem(WavFile("odd.wav", 1, tag: 85)), "a compressed kind Windows' PlaySound may not play");
        string text = Path.Combine(dir, "notes.wav");
        File.WriteAllText(text, "not a sound at all");
        Assert.AreEqual(OwnSoundProblem.CantPlay, OwnSound.Problem(text));
        Assert.AreEqual(OwnSoundProblem.CantPlay, OwnSound.Problem(dir), "a folder");
    }

    [TestMethod]
    public void TheLengthComesFromTheHeaderAndWhatIsReallyThere()
    {
        byte[] bytes = File.ReadAllBytes(WavFile("cut.wav", 4));
        // A writer that never set the length: what is in the file plays.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), uint.MaxValue);
        Assert.AreEqual(4.0, OwnSound.Seconds(bytes, bytes.Length)!.Value, 0.001);
        Assert.AreEqual(2.0, OwnSound.Seconds(bytes.AsSpan(0, 44 + 32000), 44 + 32000)!.Value, 0.001);
        Assert.IsNull(OwnSound.Seconds(bytes.AsSpan(0, 44), 44), "no sound in it");
        Assert.IsNull(OwnSound.Seconds("RIFF\0\0\0\0WAVEdata\0\0\0\0"u8, 20), "no format before the sound");
    }

    // A header that gives a high bytes-a-second number cannot make a long file pass as a short one:
    // the rate and the size of a frame give the length too, and the longer counts.
    [TestMethod]
    public void AHeaderThatUnderstatesTheLengthDoesNotLetALongFilePass()
    {
        string path = WavFile("liar.wav", 20);
        byte[] bytes = File.ReadAllBytes(path);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), 16000 * 100);
        File.WriteAllBytes(path, bytes);
        Assert.AreEqual(20.0, OwnSound.Seconds(bytes, bytes.Length)!.Value, 0.001);
        Assert.AreEqual(OwnSoundProblem.TooLong, OwnSound.Problem(path));
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), 0);
        Assert.IsNull(OwnSound.Seconds(bytes, bytes.Length), "a frame of no bytes is not a sound");
    }

    [TestMethod]
    public void AMissingOwnSoundPlaysSteppedOutsOwnAndIsLoggedOnce()
    {
        var log = new List<string>();
        var told = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string gone = Path.Combine(dir, "gone.wav");
        Assert.AreEqual(Sound.Of(SoundClip.AwaySteppedOut), SoundClips.Choose(ActionSound.Own, gone, away: true, log.Add, told));
        Assert.AreEqual(Sound.Of(SoundClip.BackSteppedOut), SoundClips.Choose(ActionSound.Own, gone, away: false, log.Add, told));
        Assert.AreEqual(Sound.Of(SoundClip.BreakSteppedOut), SoundClips.Choose(BreakSound.Own, gone, log.Add, told));
        Assert.HasCount(1, log, "said once for the file");
        StringAssert.Contains(log[0], gone);
        StringAssert.Contains(log[0], "SteppedOut's own sound plays instead");

        string there = WavFile("there.wav", 1);
        Assert.AreEqual(Sound.Own(there), SoundClips.Choose(ActionSound.Own, there, away: true, log.Add, told));
        Assert.AreEqual(Sound.Own(there), SoundClips.Choose(BreakSound.Own, there, log.Add, told));
        Assert.IsNull(SoundClips.Choose(ActionSound.Off, there, away: true, log.Add, told), "Off plays nothing, whatever file is kept");
        Assert.AreEqual(Sound.Of(SoundClip.AwayGlow), SoundClips.Choose(ActionSound.Glow, there, away: true, log.Add, told), "the file is only for your own sound");
        Assert.HasCount(1, log);
    }

    [TestMethod]
    public void TheAwayActionsPlayYourOwnSoundOrSteppedOutsWhenItIsGone()
    {
        using var rig = new AwayFakes.Rig();
        string rain = WavFile("rain.wav", 1);
        string gone = Path.Combine(dir, "gone.wav");
        using var actions = rig.Make(new Config { SoundWhenAway = ActionSound.Own, SoundWhenAwayFile = rain, SoundWhenBack = ActionSound.Own, SoundWhenBackFile = gone, MediaOnLock = MediaOnLock.KeepPlaying });
        actions.Apply(new Config { SoundWhenAway = ActionSound.Own, SoundWhenAwayFile = rain, SoundWhenBack = ActionSound.Own, SoundWhenBackFile = gone, MediaOnLock = MediaOnLock.KeepPlaying });
        actions.Leave(locking: true);
        AwayFakes.Wait(actions.ComeBack(), "back side");
        AwayFakes.Wait(actions.Idle(), "queue");
        CollectionAssert.AreEqual(new[] { "sound rain.wav", "sound BackSteppedOut" }, rig.Rec.Events);
        Assert.HasCount(1, rig.Log.Where(line => line.Contains(gone, StringComparison.Ordinal)).ToList(), "the missing file is said once over two applies");
    }

    // ---- Hearing a sound picked in the settings ----

    [TestMethod]
    public void APreviewPlaysThroughTheAwayActionsParts()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(new Config());
        actions.PreviewSound(Sound.Of(SoundClip.BreakDawn));
        CollectionAssert.AreEqual(new[] { "sound BreakDawn" }, rig.Rec.Events);
    }

    [TestMethod]
    public void APreviewInADryRunIsOnlyLogged()
    {
        using var rig = new AwayFakes.Rig();
        using var actions = rig.Make(new Config(), dryRun: true);
        actions.PreviewSound(Sound.Of(SoundClip.AwayGlow));
        Assert.IsEmpty(rig.Rec.Events);
        CollectionAssert.Contains(rig.Log, "WOULD play AwayGlow to hear it (dry run)");
    }

    // The guard: in the tests the app's own parts refuse, and nothing is heard or thrown.
    [TestMethod]
    public void APreviewWhileTheTestsBlockSoundsIsRefusedQuietly()
    {
        Assert.IsTrue(SessionLock.Blocked);
        using var actions = new AwayActions(dryRun: false);
        actions.PreviewSound(Sound.Of(SoundClip.AwaySteppedOut));
        actions.PreviewSound(Sound.Own(WavFile("rain.wav", 1)));
        Assert.ThrowsExactly<InvalidOperationException>(() => new WavPlayer().Play(Sound.Own(WavFile("own.wav", 1))));
    }

    [TestMethod]
    public void AStandInHostPlaysNothing()
    {
        ISettingsHost host = new QuietHost();
        host.PreviewSound(Sound.Of(SoundClip.AwaySteppedOut));
    }

    // Only what PreviewSound needs; the rest is never called.
    sealed class QuietHost : ISettingsHost
    {
        public void PauseChosen(TimeSpan? length) => throw new NotSupportedException();
        public void ResumeChosen() => throw new NotSupportedException();
        public void LockNowChosen() => throw new NotSupportedException();
        public void TryItChosen() => throw new NotSupportedException();
        public void ShortcutChanged(string shortcut) => throw new NotSupportedException();
        public void LanguageChanged(string language) => throw new NotSupportedException();
        public HotKeyResult ShortcutResult => HotKeyResult.Ok;
        public event Action? ShortcutResultChanged { add { } remove { } }
        public IReadOnlyList<WhyLine> Explain() => [];
        public void ConfigEdited(Config config) => throw new NotSupportedException();
        public bool StartsWithWindows => false;
        public AutostartState StartWithWindowsState => throw new NotSupportedException();
        public event Action? StartWithWindowsChanged { add { } remove { } }
        public void StartWithWindowsChosen(bool on) => throw new NotSupportedException();
        public LockStatus Status => throw new NotSupportedException();
        public event Action? StatusChanged { add { } remove { } }
    }

    // ---- The lists ----

    sealed class Built
    {
        public required ISettingsPage Tab { get; init; }
        public required Control Page { get; init; }
        public List<Func<Config, Config>> Edits { get; } = new();
        public required List<Sound> Heard { get; init; }
        public Config Config { get; set; } = new();

        public Config Apply()
        {
            foreach (var edit in Edits) Config = edit(Config.Copy());
            Edits.Clear();
            return Config;
        }
    }

    static Built Away(Config? config = null, Func<Control, string?>? file = null, Strings? text = null)
    {
        config ??= new Config();
        var heard = new List<Sound>();
        var tab = new AwayTab
        {
            PickApp = (_, _, _) => Assert.Fail("no app list should open"),
            ChooseScript = _ => { Assert.Fail("no script dialog should open"); return null; },
            ChooseSoundFile = file ?? (_ => { Assert.Fail("no sound dialog should open"); return null; }),
            Preview = heard.Add,
            StartMenuName = _ => null,
            Drives = Network,
        };
        return Finish(tab, config, heard, text);
    }

    static Built Breaks(Config? config = null, Func<Control, string?>? file = null, Strings? text = null)
    {
        config ??= new Config();
        var heard = new List<Sound>();
        var tab = new BreaksTab
        {
            ChooseSoundFile = file ?? (_ => { Assert.Fail("no sound dialog should open"); return null; }),
            Preview = heard.Add,
            Drives = Network,
        };
        return Finish(tab, config, heard, text);
    }

    static Built Finish(ISettingsPage tab, Config config, List<Sound> heard, Strings? text)
    {
        var page = tab.Build(config, text ?? Lang.For("en"));
        var built = new Built { Tab = tab, Page = page, Config = config.Copy(), Heard = heard };
        tab.Edited += built.Edits.Add;
        page.Width = 740;
        page.PerformLayout();
        return built;
    }

    static IEnumerable<T> All<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T match) yield return match;
            foreach (var inner in All<T>(child)) yield return inner;
        }
    }

    static List<SettingRow> SoundRows(Control page) => All<SettingRow>(page).Where(row => row.Controls.OfType<DropDown>().Any() && row.Title is "Play a sound" or "Reminder sound").ToList();

    static DropDown ListOf(SettingRow row) => row.Controls.OfType<DropDown>().Single();

    static void Pick(DropDown list, int row)
    {
        using var menu = list.BuildMenu();
        menu.Items.OfType<ToolStripMenuItem>().ElementAt(row).PerformClick();
    }

    static void Press(Control control, Keys key) =>
        typeof(Control).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(control, [new KeyEventArgs(key)]);

    [TestMethod]
    public void PickingASoundFromTheListPlaysItOnceAndChoosesIt()
    {
        var b = Away();
        var rows = SoundRows(b.Page);
        Assert.HasCount(2, rows);
        var away = ListOf(rows[0]);
        var back = ListOf(rows[1]);

        Pick(away, 3);
        Assert.AreEqual(ActionSound.Pebble, b.Apply().SoundWhenAway);
        CollectionAssert.AreEqual(new[] { Sound.Of(SoundClip.AwayPebble) }, b.Heard);

        Pick(back, 1);
        Assert.AreEqual(ActionSound.SteppedOut, b.Apply().SoundWhenBack);
        CollectionAssert.AreEqual(new[] { Sound.Of(SoundClip.AwayPebble), Sound.Of(SoundClip.BackSteppedOut) }, b.Heard, "the back list plays the rising sound");

        Pick(back, 1);
        Assert.HasCount(3, b.Heard, "picking the chosen sound again plays it again");
        Assert.IsEmpty(b.Edits, "and changes nothing");

        Pick(away, 0);
        Assert.AreEqual(ActionSound.Off, b.Apply().SoundWhenAway);
        Assert.HasCount(3, b.Heard, "Off plays nothing");
    }

    [TestMethod]
    public void TheArrowKeysChooseASoundQuietly()
    {
        var b = Away();
        var away = ListOf(SoundRows(b.Page)[0]);
        Press(away, Keys.Down);
        Press(away, Keys.Down);
        Assert.AreEqual(ActionSound.Drift, b.Apply().SoundWhenAway);
        Assert.IsEmpty(b.Heard);
    }

    [TestMethod]
    public void TheBreakListPlaysItsOwnSounds()
    {
        var b = Breaks();
        var list = ListOf(SoundRows(b.Page).Single());
        Pick(list, 4);
        Assert.AreEqual(BreakSound.Lantern, b.Apply().BreakSound);
        CollectionAssert.AreEqual(new[] { Sound.Of(SoundClip.BreakLantern) }, b.Heard);
    }

    [TestMethod]
    public void YourOwnSoundIsCheckedKeptPlayedAndListedByItsName()
    {
        string rain = WavFile("rain.wav", 2);
        var b = Away(file: _ => rain);
        var v = Lang.For("en").V1;
        var row = SoundRows(b.Page)[0];
        var away = ListOf(row);

        away.PressAction(0);
        var c = b.Apply();
        Assert.AreEqual(ActionSound.Own, c.SoundWhenAway);
        Assert.AreEqual(rain, c.SoundWhenAwayFile);
        CollectionAssert.AreEqual(new[] { Sound.Own(rain) }, b.Heard, "it plays once when picked");
        Assert.AreEqual("rain", away.Text);
        CollectionAssert.AreEqual(new[] { v.Off, v.SoundSteppedOut, v.SoundDrift, v.SoundPebble, v.SoundGlow, v.SoundRipple, "rain", v.YourOwnSound }, away.Rows.ToArray());

        // Another sound keeps the file for later, still in the list by its name.
        Pick(away, 2);
        c = b.Apply();
        Assert.AreEqual(ActionSound.Drift, c.SoundWhenAway);
        Assert.AreEqual(rain, c.SoundWhenAwayFile);
        Assert.AreEqual("rain", away.Rows[6]);
        Pick(away, 6);
        Assert.AreEqual(ActionSound.Own, b.Apply().SoundWhenAway);
        Assert.AreEqual(Sound.Own(rain), b.Heard[^1]);
        Assert.IsFalse(row.DetailWarns);
    }

    [TestMethod]
    public void AFileThatCantBeYourOwnSoundSaysWhyAndKeepsTheSound()
    {
        var v = Lang.For("en").V1;
        string? next = null;
        var b = Away(new Config { SoundWhenBack = ActionSound.Glow }, file: _ => next);
        var row = SoundRows(b.Page)[1];
        var back = ListOf(row);
        foreach (var (file, note) in new (string, string)[]
        {
            (WavFile("long.wav", 12), v.SoundTooLong),
            (WavFile("big.wav", 1, size: OwnSound.LargestBytes + 10), v.SoundTooBig),
            (@"\\server\share\rain.wav", v.SoundOnAnotherPC),
            (@"Z:\rain.wav", v.SoundOnAnotherPC),
            (Path.Combine(dir, "gone.wav"), v.SoundCantPlay),
        })
        {
            next = file;
            back.PressAction(0);
            Assert.IsEmpty(b.Edits, file);
            Assert.IsEmpty(b.Heard, file);
            Assert.IsTrue(row.DetailWarns, file);
            Assert.AreEqual(note, row.Detail, file);
            Assert.AreEqual(ActionSound.Glow, back.Value, "the sound stays as it was");
        }

        // Cancelling Windows' Open window changes nothing; picking a sound brings the explanation back.
        next = null;
        back.PressAction(0);
        Assert.IsEmpty(b.Edits);
        Pick(back, 5);
        Assert.AreEqual(ActionSound.Ripple, b.Apply().SoundWhenBack);
        Assert.IsFalse(row.DetailWarns);
        Assert.AreEqual(v.PlaySoundBackDetail, row.Detail);
    }

    [TestMethod]
    public void TheBreakListTakesYourOwnSoundToo()
    {
        string bell = WavFile("bell.wav", 1);
        var b = Breaks(file: _ => bell);
        var list = ListOf(SoundRows(b.Page).Single());
        list.PressAction(0);
        var c = b.Apply();
        Assert.AreEqual(BreakSound.Own, c.BreakSound);
        Assert.AreEqual(bell, c.BreakSoundFile);
        CollectionAssert.AreEqual(new[] { Sound.Own(bell) }, b.Heard);
        Assert.AreEqual("bell", list.Text);
    }

    [TestMethod]
    public void ARefillShowsYourOwnSoundByItsNameAndAsksForNothing()
    {
        var b = Away();
        b.Tab.Refill(new Config { SoundWhenAway = ActionSound.Own, SoundWhenAwayFile = @"C:\Sounds\Soft rain.wav", SoundWhenBack = ActionSound.Ripple, SoundWhenBackFile = @"C:\Sounds\birds.wav" });
        var rows = SoundRows(b.Page);
        Assert.AreEqual("Soft rain", ListOf(rows[0]).Text);
        Assert.AreEqual(ActionSound.Own, ListOf(rows[0]).Value);
        Assert.AreEqual(ActionSound.Ripple, ListOf(rows[1]).Value);
        Assert.AreEqual("birds", ListOf(rows[1]).Rows[^2], "a file kept for later is in the list");
        Assert.IsEmpty(b.Edits);
        Assert.IsEmpty(b.Heard);

        var breaks = Breaks(new Config { BreakSound = BreakSound.Own, BreakSoundFile = @"C:\Sounds\bell.wav" });
        Assert.AreEqual("bell", ListOf(SoundRows(breaks.Page).Single()).Text);
        Assert.IsEmpty(breaks.Edits);
    }

    [TestMethod]
    public void YourOwnSoundShowsByAShortName()
    {
        Assert.AreEqual("rain", SoundPicker.FileName(@"C:\Sounds\rain.wav"));
        Assert.AreEqual("Soft rain", SoundPicker.FileName(@"C:\Sounds\Soft rain.WAV"));
        string name = SoundPicker.FileName(@"D:\Sounds\A long recording of soft rain on the window at night.wav");
        Assert.AreEqual("A long recording of soft ra…‎", name, "the … carries a left-to-right mark, so the Arabic window keeps it at the end of a Latin name");
        Assert.HasCount(29, name);
    }

    // The settings window hands the host's PreviewSound to both tabs, and the tray app hands it to the
    // away actions, so a pick is heard in the app and nowhere in the tests.
    [TestMethod]
    public void TheWindowWiresThePreviewToTheHost()
    {
        var pages = (Func<ISettingsHost, ISettingsPage>?[])typeof(SettingsForm).GetField("TabPages", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var host = new QuietHost();
        var away = (AwayTab)pages[1]!(host);
        var breaks = (BreaksTab)pages[3]!(host);
        Assert.IsNotNull(away.Preview);
        Assert.IsNotNull(breaks.Preview);
        Assert.AreSame(host, away.Preview.Target);
        Assert.AreSame(host, breaks.Preview.Target);
    }
}
