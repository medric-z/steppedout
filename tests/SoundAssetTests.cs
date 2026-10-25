using System.Buffers.Binary;
using System.Text;

namespace SteppedOut.Tests;

// The sounds are only read here, never played.
[TestClass]
public class SoundAssetTests
{
    static readonly SoundClip[] All = Enum.GetValues<SoundClip>();

    [TestMethod]
    public void EverySoundChoiceHasItsSoundBuiltIntoTheExe()
    {
        foreach (var choice in Enum.GetValues<ActionSound>())
        {
            foreach (bool away in new[] { true, false })
            {
                var clip = SoundClips.For(choice, away);
                Assert.AreEqual(choice is ActionSound.Off or ActionSound.Own, clip is null, choice.ToString());
                if (clip is SoundClip found)
                {
                    Assert.IsNotEmpty(SoundClips.Bytes(found), choice.ToString());
                    StringAssert.StartsWith(found.ToString(), away ? "Away" : "Back");
                    StringAssert.EndsWith(found.ToString(), choice.ToString(), "an away sound and its back sound share the choice's name");
                }
            }
        }
        foreach (var choice in Enum.GetValues<BreakSound>())
        {
            var clip = SoundClips.For(choice);
            Assert.AreEqual(choice is BreakSound.Off or BreakSound.Own, clip is null, choice.ToString());
            if (clip is SoundClip found) Assert.AreEqual("Break" + choice, found.ToString());
        }
        var names = typeof(SoundClips).Assembly.GetManifestResourceNames().Where(name => name.StartsWith("Sounds.", StringComparison.Ordinal)).ToList();
        CollectionAssert.AreEquivalent(All.Select(SoundClips.ResourceName).ToList(), names);
        Assert.HasCount(15, names);
        CollectionAssert.Contains(names, "Sounds.away-steppedout.wav");
        CollectionAssert.Contains(names, "Sounds.break-tide.wav");
    }

    [TestMethod]
    public void TheOldSoundsAreGone()
    {
        var names = typeof(SoundClips).Assembly.GetManifestResourceNames();
        foreach (string old in new[] { "Sounds.click.wav", "Sounds.chime.wav", "Sounds.softchime.wav" }) CollectionAssert.DoesNotContain(names, old);
        foreach (string old in new[] { "click.wav", "chime.wav", "softchime.wav" }) Assert.IsFalse(File.Exists(Path.Combine(Root, "src", "Sounds", old)), old);
    }

    [TestMethod]
    public void EachSoundIsAShortMonoWavAt44kHz()
    {
        foreach (var clip in All)
        {
            var wav = Wav.Read(SoundClips.Bytes(clip));

            Assert.AreEqual(1, wav.Format, $"{clip}: PCM");
            Assert.AreEqual(1, wav.Channels, $"{clip}: mono");
            Assert.AreEqual(44100, wav.Rate, $"{clip}: 44.1 kHz");
            Assert.AreEqual(16, wav.Bits, $"{clip}: 16-bit");
            Assert.AreEqual(44100 * 2, wav.ByteRate, clip.ToString());
            Assert.AreEqual(2, wav.BlockAlign, clip.ToString());
            Assert.IsTrue(wav.Seconds >= 0.5 && wav.Seconds <= 1.5, $"{clip}: {wav.Seconds:0.000} s");
            Assert.AreEqual(0, wav.Samples[0], $"{clip}: starts from silence");
            Assert.IsLessThan(20, Math.Abs((int)wav.Samples[^1]), $"{clip}: fades out to silence");
        }
    }

    // The checks the generator runs, on the sounds as they are in the exe: none is loud, sudden or
    // bright, and they are all about as loud as each other.
    [TestMethod]
    public void EachSoundPassesTheGeneratorsChecks()
    {
        var measures = All.ToDictionary(clip => clip, clip => SoundCheck.Of(Wav.Read(SoundClips.Bytes(clip)).Samples));
        double set = measures.Values.Average(m => m.LoudnessDb);
        foreach (var (clip, m) in measures)
        {
            CollectionAssert.AreEqual(Array.Empty<string>(), SoundCheck.Problems(m, set), clip.ToString());
            Assert.IsLessThanOrEqualTo(-12.0, m.PeakDb, $"{clip}: peak well below full scale");
        }
        Assert.IsLessThan(1.0, measures.Values.Max(m => m.LoudnessDb) - measures.Values.Min(m => m.LoudnessDb), "every sound is set to the same loudness");
    }

    // Each back sound answers its away sound: as long, and the same notes the other way round.
    [TestMethod]
    public void EachAwaySoundHasABackSoundOfTheSameLength()
    {
        foreach (var choice in Enum.GetValues<ActionSound>())
        {
            if (SoundClips.For(choice, away: true) is not SoundClip away) continue;
            var back = SoundClips.For(choice, away: false)!.Value;
            Assert.HasCount(Wav.Read(SoundClips.Bytes(away)).Samples.Length, Wav.Read(SoundClips.Bytes(back)).Samples, choice.ToString());
            CollectionAssert.AreNotEqual(SoundClips.Bytes(away), SoundClips.Bytes(back), choice.ToString());
        }
    }

    [TestMethod]
    public void EachSoundIsUnder150KB()
    {
        foreach (var clip in All) Assert.IsLessThan(150 * 1024, SoundClips.Bytes(clip).Length, clip.ToString());
    }

    [TestMethod]
    public void TheSoundsInTheExeAreTheFilesTheGeneratorWrote()
    {
        foreach (var clip in All)
        {
            string file = Path.Combine(Root, "src", "Sounds", SoundClips.ResourceName(clip)["Sounds.".Length..]);
            CollectionAssert.AreEqual(File.ReadAllBytes(file), SoundClips.Bytes(clip), clip.ToString());
        }
    }

    internal static string Root
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "tools", "sounds", "gen.cs"))) return dir.FullName;
            }
            throw new InvalidOperationException("repository not found");
        }
    }

    internal sealed record Wav(int Format, int Channels, int Rate, int ByteRate, int BlockAlign, int Bits, short[] Samples)
    {
        public double Seconds => Samples.Length / (double)Rate;

        public static Wav Read(byte[] bytes)
        {
            var span = bytes.AsSpan();
            Assert.AreEqual("RIFF", Encoding.ASCII.GetString(span[..4]));
            Assert.AreEqual(bytes.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(span[4..]));
            Assert.AreEqual("WAVE", Encoding.ASCII.GetString(span[8..12]));
            int at = 12;
            (int, int, int, int, int, int)? format = null;
            short[]? samples = null;
            while (at + 8 <= bytes.Length)
            {
                string id = Encoding.ASCII.GetString(span.Slice(at, 4));
                int size = BinaryPrimitives.ReadInt32LittleEndian(span[(at + 4)..]);
                var body = span.Slice(at + 8, size);
                if (id == "fmt ")
                {
                    Assert.AreEqual(16, size, "plain PCM format chunk");
                    format = (BinaryPrimitives.ReadInt16LittleEndian(body), BinaryPrimitives.ReadInt16LittleEndian(body[2..]),
                        BinaryPrimitives.ReadInt32LittleEndian(body[4..]), BinaryPrimitives.ReadInt32LittleEndian(body[8..]),
                        BinaryPrimitives.ReadInt16LittleEndian(body[12..]), BinaryPrimitives.ReadInt16LittleEndian(body[14..]));
                }
                else if (id == "data")
                {
                    Assert.AreEqual(0, size % 2);
                    samples = new short[size / 2];
                    for (int i = 0; i < samples.Length; i++) samples[i] = BinaryPrimitives.ReadInt16LittleEndian(body[(2 * i)..]);
                }
                at += 8 + size + (size & 1);
            }
            Assert.IsNotNull(format, "fmt chunk");
            Assert.IsNotNull(samples, "data chunk");
            var (tag, channels, rate, byteRate, blockAlign, bits) = format.Value;
            return new Wav(tag, channels, rate, byteRate, blockAlign, bits, samples);
        }
    }
}
