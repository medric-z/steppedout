using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace SteppedOut;

// The fifteen sounds built into the exe, made by tools/sounds/gen.cs: five for leaving the PC, five
// for coming back that answer them (the same notes rising), and five for break reminders. Each is
// 0.5 to 1.5 seconds, so the away sound can be waited for before the mute.
enum SoundClip
{
    AwaySteppedOut,
    AwayDrift,
    AwayPebble,
    AwayGlow,
    AwayRipple,
    BackSteppedOut,
    BackDrift,
    BackPebble,
    BackGlow,
    BackRipple,
    BreakSteppedOut,
    BreakDawn,
    BreakMeadow,
    BreakLantern,
    BreakTide,
}

// A sound to play: one built into the exe, or a .wav file of the person's own.
sealed record Sound
{
    public SoundClip? Clip { get; private init; }

    public string? File { get; private init; }

    public static Sound Of(SoundClip clip) => new() { Clip = clip };

    public static Sound Own(string file) => new() { File = file };

    // For the log: the built-in sound's name, or the file's name.
    public override string ToString() => File is { } file ? Path.GetFileName(file) : Clip.ToString()!;
}

interface ISoundPlayer
{
    // Starts the sound and returns at once.
    void Play(Sound sound);

    // Returns once the sound has been heard to its end.
    void PlayAndWait(Sound sound);
}

static class SoundClips
{
    // The built-in sound for a choice, for leaving (away) or coming back; null for Off and Own.
    public static SoundClip? For(ActionSound choice, bool away) => choice switch
    {
        ActionSound.SteppedOut => away ? SoundClip.AwaySteppedOut : SoundClip.BackSteppedOut,
        ActionSound.Drift => away ? SoundClip.AwayDrift : SoundClip.BackDrift,
        ActionSound.Pebble => away ? SoundClip.AwayPebble : SoundClip.BackPebble,
        ActionSound.Glow => away ? SoundClip.AwayGlow : SoundClip.BackGlow,
        ActionSound.Ripple => away ? SoundClip.AwayRipple : SoundClip.BackRipple,
        _ => null,
    };

    public static SoundClip? For(BreakSound choice) => choice switch
    {
        BreakSound.SteppedOut => SoundClip.BreakSteppedOut,
        BreakSound.Dawn => SoundClip.BreakDawn,
        BreakSound.Meadow => SoundClip.BreakMeadow,
        BreakSound.Lantern => SoundClip.BreakLantern,
        BreakSound.Tide => SoundClip.BreakTide,
        _ => null,
    };

    // What a choice plays: nothing for Off, the file for Own. An own file that can no longer be
    // played (gone, on another PC now, replaced by one too long) gives SteppedOut's own sound of
    // that set instead, and the log says so once for that file: told keeps the files already said.
    public static Sound? Choose(ActionSound choice, string file, bool away, Action<string> log, ISet<string> told, Func<char, DriveType>? drive = null) =>
        choice == ActionSound.Own
            ? OwnOr(file, away ? SoundClip.AwaySteppedOut : SoundClip.BackSteppedOut, away ? "the away sound" : "the sound for coming back", log, told, drive)
            : For(choice, away) is SoundClip clip ? Sound.Of(clip) : null;

    public static Sound? Choose(BreakSound choice, string file, Action<string> log, ISet<string> told, Func<char, DriveType>? drive = null) =>
        choice == BreakSound.Own
            ? OwnOr(file, SoundClip.BreakSteppedOut, "the reminder sound", log, told, drive)
            : For(choice) is SoundClip clip ? Sound.Of(clip) : null;

    static Sound OwnOr(string file, SoundClip fallback, string what, Action<string> log, ISet<string> told, Func<char, DriveType>? drive)
    {
        string path = OwnSound.Tidy(file);
        var problem = path.Length == 0 ? OwnSoundProblem.CantPlay : OwnSound.Problem(path, drive ?? LocalPaths.DriveTypeOf);
        if (problem is null) return Sound.Own(path);
        bool first;
        lock (told) first = told.Add(path);
        if (first) log($"{what}, {(path.Length == 0 ? "with no file" : path)}, can't be played: {OwnSound.Why(problem.Value)}; SteppedOut's own sound plays instead");
        return Sound.Of(fallback);
    }

    public static string ResourceName(SoundClip clip)
    {
        string name = clip.ToString();
        int split = name.StartsWith("Break", StringComparison.Ordinal) ? 5 : 4;
        return $"Sounds.{name[..split].ToLowerInvariant()}-{name[split..].ToLowerInvariant()}.wav";
    }

    // The file's bytes as built into the exe.
    public static byte[] Bytes(SoundClip clip)
    {
        using var stream = typeof(SoundClips).Assembly.GetManifestResourceStream(ResourceName(clip))
            ?? throw new InvalidOperationException($"the {clip} sound is missing from the exe");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}

// Why a .wav file can't be your own sound.
enum OwnSoundProblem
{
    // On another PC, by a network path or on a network drive (LocalPaths).
    AnotherPC,
    TooBig,
    TooLong,
    // Not there, not a full path on this PC, not readable, or not a sound Windows plays.
    CantPlay,
}

// A .wav file of the person's own, picked with "Your own sound…". It is checked as a script is: a
// file on this PC by its full path (LocalPaths), never one on another PC. It is at most 5 MB and 10
// seconds long, so the away sound, which is waited for before the mute, never holds that up for
// long. Only the file's start is read: its size comes from Windows and its length from its header.
static class OwnSound
{
    public const long LargestBytes = 5 * 1024 * 1024;
    public const int LongestSeconds = 10;
    const int HeaderBytes = 64 * 1024;

    public static string Tidy(string? path) => (path ?? "").Trim().Trim('"');

    public static OwnSoundProblem? Problem(string path) => Problem(path, LocalPaths.DriveTypeOf);

    // drive tells what Windows says is behind a drive letter; the tests give their own.
    public static OwnSoundProblem? Problem(string path, Func<char, DriveType> drive)
    {
        path = Tidy(path);
        switch (LocalPaths.Place(path, drive))
        {
            case PathPlace.ThisPC:
                break;
            case PathPlace.NetworkPath or PathPlace.NetworkDrive:
                return OwnSoundProblem.AnotherPC;
            default:
                return OwnSoundProblem.CantPlay;
        }
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return OwnSoundProblem.CantPlay;
            if (info.Length > LargestBytes) return OwnSoundProblem.TooBig;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var head = new byte[(int)Math.Min(info.Length, HeaderBytes)];
            stream.ReadExactly(head);
            double? seconds = Seconds(head, info.Length);
            if (seconds is null) return OwnSoundProblem.CantPlay;
            return seconds > LongestSeconds ? OwnSoundProblem.TooLong : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return OwnSoundProblem.CantPlay;
        }
    }

    // How long a .wav file plays, from its start (head) and its whole size; null when it is not a
    // wave file Windows plays: RIFF WAVE with a "fmt " part for PCM, floating point or the extended
    // form of either, then a "data" part with some sound in it.
    internal static double? Seconds(ReadOnlySpan<byte> head, long size)
    {
        if (head.Length < 12 || !head[..4].SequenceEqual("RIFF"u8) || !head[8..12].SequenceEqual("WAVE"u8)) return null;
        // The length is read both ways a header can give it, by the bytes a second and by the rate and
        // the size of one frame, and the longer counts: a header that understates its rate cannot make
        // a long file pass.
        long perSecond = 0;
        long perSecondByFrames = 0;
        long at = 12;
        while (at + 8 <= head.Length)
        {
            var id = head.Slice((int)at, 4);
            long length = BinaryPrimitives.ReadUInt32LittleEndian(head[(int)(at + 4)..]);
            long body = at + 8;
            if (id.SequenceEqual("fmt "u8))
            {
                if (length < 16 || body + 16 > head.Length) return null;
                var format = head.Slice((int)body, 16);
                int tag = BinaryPrimitives.ReadUInt16LittleEndian(format);
                int channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..]);
                int rate = BinaryPrimitives.ReadInt32LittleEndian(format[4..]);
                int byteRate = BinaryPrimitives.ReadInt32LittleEndian(format[8..]);
                int frame = BinaryPrimitives.ReadUInt16LittleEndian(format[12..]);
                if (tag is not (1 or 3 or 0xFFFE) || channels is < 1 or > 8 || rate is < 8000 or > 384000 || byteRate <= 0 || frame <= 0) return null;
                perSecond = byteRate;
                perSecondByFrames = (long)frame * rate;
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (perSecond <= 0) return null;
                // A file cut short, or one whose writer never set the length, plays what is there.
                long sound = Math.Min(length, size - body);
                return sound <= 0 ? null : sound / (double)Math.Min(perSecond, perSecondByFrames);
            }
            at = body + length + (length & 1);
        }
        return null;
    }

    // In a few plain words, for the log.
    public static string Why(OwnSoundProblem problem) => problem switch
    {
        OwnSoundProblem.AnotherPC => "it is on another PC",
        OwnSoundProblem.TooBig => $"it is over {LargestBytes / (1024 * 1024)} MB",
        OwnSoundProblem.TooLong => $"it is over {LongestSeconds} seconds long",
        _ => "it is not there, or not a .wav sound Windows can play",
    };
}

// Plays through Windows' PlaySound: a built-in sound straight from memory, an own sound from its
// file. Each built-in sound is copied once into memory the garbage collector never moves, because
// Windows keeps reading it after an async start has returned. The copies live as long as the app.
sealed class WavPlayer : ISoundPlayer
{
    const uint Sync = 0x0000;
    const uint Async = 0x0001;
    const uint NoDefault = 0x0002;
    const uint Memory = 0x0004;
    const uint FileName = 0x00020000;
    // PlaySound returns when it has handed the last of the sound to the audio engine, a moment
    // before it has been heard. A mute right then would cut off the end.
    static readonly TimeSpan Drain = TimeSpan.FromMilliseconds(150);

    static readonly object gate = new();
    static readonly Dictionary<SoundClip, IntPtr> loaded = new();

    public void Play(Sound sound) => Start(sound, Async);

    public void PlayAndWait(Sound sound)
    {
        Start(sound, Sync);
        Thread.Sleep(Drain);
    }

    static void Start(Sound sound, uint how)
    {
        // The tests and the screenshot tool run on a PC someone is using; nothing they do may be heard.
        if (SessionLock.Blocked) throw new InvalidOperationException("Playing sounds is blocked in this process.");
        bool played = sound.File is { } file
            ? PlaySoundFile(file, IntPtr.Zero, FileName | NoDefault | how)
            : PlaySound(Load(sound.Clip!.Value), IntPtr.Zero, Memory | NoDefault | how);
        if (!played) throw new InvalidOperationException("Windows could not play the sound");
    }

    static IntPtr Load(SoundClip clip)
    {
        lock (gate)
        {
            if (loaded.TryGetValue(clip, out IntPtr memory)) return memory;
            byte[] bytes = SoundClips.Bytes(clip);
            memory = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, memory, bytes.Length);
            loaded[clip] = memory;
            return memory;
        }
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool PlaySoundFile(string sound, IntPtr module, uint flags);
}
