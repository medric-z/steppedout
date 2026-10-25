// Makes the fifteen sounds SteppedOut can play, from nothing but numbers, so they are its own and
// every run gives the same bytes: five for leaving the PC, five for coming back and five for break
// reminders. Each away sound has a back sound that answers it: the same notes, falling when you
// leave and rising when you come back. The first of each set is SteppedOut's own, and the three
// share one short motif, a major chord played as three notes.
//
//   dotnet run tools/sounds/gen.cs                   write src/Sounds/*.wav
//   dotnet run tools/sounds/gen.cs -- --check        only say whether those files match (exit 1 if not)
//   dotnet run tools/sounds/gen.cs -- --page <dir>   also write a listening page and the sounds there
//
// Each is 16-bit mono at 44.1 kHz. Every note starts softly and fades out, the tones are simple
// (sines and soft FM with a few gentle overtones, in consonant intervals), and every sound is set
// to the same loudness, well below full scale. SoundCheck measures each and the run fails when one
// is too loud, too sudden, too bright, too short or too long.
//
// Only adding, multiplying, dividing, rounding, Math.Floor, Math.Sqrt and the sine and exponential
// written out below make the samples, never the platform's Math.Sin or Math.Exp, whose last digit
// may differ between machines, so the check holds anywhere.

using System.Text;

const int Rate = SoundCheck.Rate;

// The loudest 300 ms of every sound, as RMS: -23 dBFS.
const double Loudness = 0.0707945784384138;

var check = args.Contains("--check");
int pageAt = Array.IndexOf(args, "--page");
string? page = pageAt >= 0 && pageAt + 1 < args.Length ? args[pageAt + 1] : null;
var root = FindRoot();
var outDir = Path.Combine(root, "src", "Sounds");

var sounds = new (string Set, string Name, string Title, string About, double[] Samples)[]
{
    ("away", "steppedout", "SteppedOut", "SteppedOut's own: three soft bell notes falling (G, E, C)", Signature(falling: true)),
    ("away", "drift", "Drift", "a warm pad, two notes falling a fourth", Drift(falling: true)),
    ("away", "pebble", "Pebble", "a soft marimba, three notes falling", Pebble(falling: true)),
    ("away", "glow", "Glow", "a soft bell, two notes falling a fifth", Glow(falling: true)),
    ("away", "ripple", "Ripple", "four quick soft notes falling", Ripple(falling: true)),
    ("back", "steppedout", "SteppedOut", "SteppedOut's own: the same three notes rising (C, E, G)", Signature(falling: false)),
    ("back", "drift", "Drift", "the warm pad, rising a fourth", Drift(falling: false)),
    ("back", "pebble", "Pebble", "the soft marimba, rising", Pebble(falling: false)),
    ("back", "glow", "Glow", "the soft bell, rising a fifth", Glow(falling: false)),
    ("back", "ripple", "Ripple", "the four soft notes, rising", Ripple(falling: false)),
    ("break", "steppedout", "SteppedOut", "SteppedOut's own: the three notes rising slowly, then held", BreakSignature()),
    ("break", "dawn", "Dawn", "a soft chord that swells and fades", Dawn()),
    ("break", "meadow", "Meadow", "a soft marimba, up and back", Meadow()),
    ("break", "lantern", "Lantern", "one soft bell note and its fifth", Lantern()),
    ("break", "tide", "Tide", "a warm pad, three notes rising and settling", Tide()),
};

var made = sounds.Select(s => (s.Set, s.Name, s.Title, s.About, Wav: Wav(Level(s.Samples)))).ToList();
var measures = made.Select(s => SoundCheck.Of(Samples(s.Wav))).ToList();
double setLoudness = measures.Average(m => m.LoudnessDb);

bool failed = false;
Console.WriteLine($"{"sound",-18} {"length",7} {"peak",8} {"loudness",9} {"attack",8} {"centroid",9} {"> 4 kHz",8} {"end",7}  result");
for (int i = 0; i < made.Count; i++)
{
    var m = measures[i];
    var problems = SoundCheck.Problems(m, setLoudness);
    failed |= problems.Count > 0;
    Console.WriteLine($"{made[i].Set + "-" + made[i].Name,-18} {m.Seconds,6:0.00}s {m.PeakDb,6:0.0}dB {m.LoudnessDb,7:0.0}dB {m.AttackMs,6:0.0}ms {m.CentroidHz,7:0}Hz {m.HighShare,8:P2} {m.EndDb,5:0}dB  {(problems.Count == 0 ? "ok" : string.Join("; ", problems))}");
}
if (failed)
{
    Console.Error.WriteLine("a sound failed its check; nothing was written");
    return 1;
}

bool stale = false;
foreach (var s in made)
{
    string file = $"{s.Set}-{s.Name}.wav";
    string path = Path.Combine(outDir, file);
    bool same = File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(s.Wav);
    if (same) continue;
    stale = true;
    if (check)
    {
        Console.Error.WriteLine($"src/Sounds/{file} differs; run: dotnet run tools/sounds/gen.cs");
        continue;
    }
    Directory.CreateDirectory(outDir);
    File.WriteAllBytes(path, s.Wav);
    Console.WriteLine($"wrote src/Sounds/{file} ({s.Wav.Length} bytes)");
}
if (check && !stale) Console.WriteLine("src/Sounds is up to date");

if (page is not null)
{
    Directory.CreateDirectory(page);
    var html = new StringBuilder();
    html.Append("""
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>SteppedOut sounds</title>
        <style>
        :root { color-scheme: light dark; --bg: #f7f7f5; --card: #fff; --text: #1b1b1b; --muted: #666; --line: #ddd; --accent: #2f7d4f; }
        @media (prefers-color-scheme: dark) { :root { --bg: #1c1c1c; --card: #262626; --text: #eee; --muted: #aaa; --line: #3a3a3a; --accent: #6cc28f; } }
        body { margin: 0; padding: 24px 16px; background: var(--bg); color: var(--text); font: 15px/1.45 "Segoe UI", system-ui, sans-serif; }
        main { max-width: 980px; margin: 0 auto; }
        h1 { font-size: 22px; margin: 0 0 4px; } h2 { font-size: 17px; margin: 28px 0 8px; }
        p { color: var(--muted); margin: 0 0 12px; }
        .card { background: var(--card); border: 1px solid var(--line); border-radius: 8px; padding: 12px 14px; margin: 8px 0; display: grid; grid-template-columns: 1fr auto; gap: 6px 16px; align-items: center; }
        .name { font-weight: 600; } .own { color: var(--accent); font-weight: 600; font-size: 13px; margin-left: 6px; }
        .about { color: var(--muted); font-size: 13px; } audio { width: 260px; max-width: 100%; }
        .nums { grid-column: 1 / -1; color: var(--muted); font-size: 12px; font-variant-numeric: tabular-nums; }
        @media (max-width: 560px) { .card { grid-template-columns: 1fr; } }
        </style></head><body><main>
        <h1>SteppedOut sounds</h1>
        <p>Fifteen sounds made by tools/sounds/gen.cs. The away and back sounds are pairs: the same notes, falling when you leave and rising when you come back. The first of each set is SteppedOut's own, and the three share one motif. Each card shows the checks: peak (limit −12 dBFS), loudness of the loudest 300 ms, attack (at least 10 ms), brightness (centroid up to 2000 Hz, under 1% of the energy above 4 kHz).</p>

        """);
    string? set = null;
    for (int i = 0; i < made.Count; i++)
    {
        var s = made[i];
        var m = measures[i];
        if (s.Set != set)
        {
            set = s.Set;
            html.Append($"<h2>{set switch { "away" => "When you're away", "back" => "When you're back", _ => "Break reminders" }}</h2>\n");
        }
        string file = $"{s.Set}-{s.Name}.wav";
        File.WriteAllBytes(Path.Combine(page, file), s.Wav);
        html.Append($"""
            <div class="card"><div><span class="name">{s.Title}</span>{(s.Name == "steppedout" ? "<span class=\"own\">default</span>" : "")}<div class="about">{s.About} · {file}</div></div>
            <audio controls preload="auto" src="data:audio/wav;base64,{Convert.ToBase64String(s.Wav)}"></audio>
            <div class="nums">{m.Seconds:0.00} s · peak {m.PeakDb:0.0} dBFS · loudness {m.LoudnessDb:0.0} dBFS · attack {m.AttackMs:0} ms · centroid {m.CentroidHz:0} Hz · above 4 kHz {m.HighShare:P2}</div></div>

            """);
    }
    html.Append("</main></body></html>\n");
    File.WriteAllText(Path.Combine(page, "index.html"), html.ToString(), new UTF8Encoding(false));
    Console.WriteLine($"wrote the listening page and {made.Count} sounds to {page}");
}
return check && stale ? 1 : 0;

// ---- The sounds ----

// SteppedOut's motif: C, E and G, a major chord as three soft bell notes, 0.13 s apart.
static double[] Signature(bool falling)
{
    var s = new double[Seconds(1.15)];
    double[] notes = falling ? [G5, E5, C5] : [C5, E5, G5];
    for (int i = 0; i < notes.Length; i++) Bell(s, Seconds(0.13 * i), notes[i], 1.0, decay: 0.32);
    return FadeOut(s, 0.15);
}

// The motif rising, more slowly, its notes left to ring together.
static double[] BreakSignature()
{
    var s = new double[Seconds(1.5)];
    double[] notes = [C5, E5, G5];
    for (int i = 0; i < notes.Length; i++) Bell(s, Seconds(0.2 * i), notes[i], 0.9, decay: 0.55);
    return FadeOut(s, 0.25);
}

static double[] Drift(bool falling)
{
    var s = new double[Seconds(1.4)];
    double[] notes = falling ? [A4, E4] : [E4, A4];
    Pad(s, 0, notes[0], 1.0, attack: 0.06, decay: 0.7);
    Pad(s, Seconds(0.3), notes[1], 1.0, attack: 0.06, decay: 0.7);
    return FadeOut(s, 0.25);
}

static double[] Pebble(bool falling)
{
    var s = new double[Seconds(0.9)];
    double[] notes = falling ? [D5, B4, G4] : [G4, B4, D5];
    for (int i = 0; i < notes.Length; i++) Marimba(s, Seconds(0.15 * i), notes[i], 1.0);
    return FadeOut(s, 0.15);
}

static double[] Glow(bool falling)
{
    var s = new double[Seconds(1.3)];
    double[] notes = falling ? [E5, A4] : [A4, E5];
    SoftBell(s, 0, notes[0], 1.0, decay: 0.45);
    SoftBell(s, Seconds(0.25), notes[1], 1.0, decay: 0.45);
    return FadeOut(s, 0.2);
}

static double[] Ripple(bool falling)
{
    var s = new double[Seconds(1.0)];
    double[] notes = falling ? [E5, D5, C5, A4] : [A4, C5, D5, E5];
    for (int i = 0; i < notes.Length; i++) Plain(s, Seconds(0.09 * i), notes[i], 1.0, decay: 0.2);
    return FadeOut(s, 0.15);
}

static double[] Dawn()
{
    var s = new double[Seconds(1.5)];
    Pad(s, 0, C4, 1.0, attack: 0.3, decay: 0.8);
    Pad(s, Seconds(0.05), E4, 0.8, attack: 0.3, decay: 0.8);
    Pad(s, Seconds(0.1), G4, 0.7, attack: 0.3, decay: 0.8);
    return FadeOut(s, 0.35);
}

static double[] Meadow()
{
    var s = new double[Seconds(1.0)];
    double[] notes = [G4, B4, D5, B4];
    for (int i = 0; i < notes.Length; i++) Marimba(s, Seconds(0.14 * i), notes[i], i == 3 ? 0.8 : 1.0);
    return FadeOut(s, 0.15);
}

static double[] Lantern()
{
    var s = new double[Seconds(1.5)];
    SoftBell(s, 0, A4, 1.0, decay: 0.6);
    SoftBell(s, Seconds(0.12), E5, 0.6, decay: 0.5);
    return FadeOut(s, 0.3);
}

static double[] Tide()
{
    var s = new double[Seconds(1.5)];
    Pad(s, 0, F4, 1.0, attack: 0.12, decay: 0.7);
    Pad(s, Seconds(0.22), A4, 0.9, attack: 0.12, decay: 0.7);
    Pad(s, Seconds(0.44), C5, 0.8, attack: 0.12, decay: 0.6);
    return FadeOut(s, 0.35);
}

// ---- Voices ----

// A soft bell: the note, a quiet octave and a fainter twelfth that fade sooner.
static void Bell(double[] s, int start, double hz, double level, double decay)
{
    Partial(s, start, hz, level, attack: 0.018, decay: decay);
    Partial(s, start, hz * 2, level * 0.16, attack: 0.018, decay: decay * 0.5);
    Partial(s, start, hz * 3, level * 0.04, attack: 0.018, decay: decay * 0.3);
}

// A marimba-like bar: the note and the bar's own overtone near four times it, which dies fast.
static void Marimba(double[] s, int start, double hz, double level)
{
    Partial(s, start, hz, level, attack: 0.016, decay: 0.2);
    Partial(s, start, hz * 3.93, level * 0.08, attack: 0.016, decay: 0.05);
}

// A pad: two copies of a warm tone a few cents apart, which slowly beat against each other.
static void Pad(double[] s, int start, double hz, double level, double attack, double decay)
{
    foreach (var (detune, part) in new[] { (1.0, 0.6), (1.0035, 0.4) })
    {
        Partial(s, start, hz * detune, level * part, attack, decay);
        Partial(s, start, hz * 2 * detune, level * part * 0.18, attack, decay * 0.8);
        Partial(s, start, hz * 3 * detune, level * part * 0.05, attack, decay * 0.6);
    }
}

// A plain tone with a hint of its octave.
static void Plain(double[] s, int start, double hz, double level, double decay)
{
    Partial(s, start, hz, level, attack: 0.015, decay: decay);
    Partial(s, start, hz * 2, level * 0.08, attack: 0.015, decay: decay * 0.5);
}

// A soft FM bell: the note bent by a sine at twice its pitch, gently at first and less as it fades.
static void SoftBell(double[] s, int start, double hz, double level, double decay)
{
    int rise = Seconds(0.02);
    for (int n = start; n < s.Length; n++)
    {
        int i = n - start;
        double t = i / (double)Rate;
        double index = 0.9 * Exp(-t / 0.12);
        double carrier = Phase(hz, i);
        double modulator = Phase(hz * 2, i);
        s[n] += level * Envelope(i, rise, t, 0.02, decay) * Sin(Wrap(carrier + index * Sin(modulator)));
    }
}

// A sine from sample start: a raised-cosine rise over attack seconds, then an exponential fall with
// the given time constant.
static void Partial(double[] s, int start, double hz, double level, double attack, double decay)
{
    int rise = Math.Max(1, Seconds(attack));
    for (int n = start; n < s.Length; n++)
    {
        int i = n - start;
        double t = i / (double)Rate;
        s[n] += level * Envelope(i, rise, t, attack, decay) * Sin(Phase(hz, i));
    }
}

static double Envelope(int i, int rise, double t, double attack, double decay) =>
    i < rise ? 0.5 - 0.5 * Cos(Pi * i / rise) : Exp(-(t - attack) / decay);

// Where a sine at hz is after i samples, in [0, 2π).
static double Phase(double hz, int i)
{
    double cycles = hz * i / Rate;
    return (cycles - Math.Floor(cycles)) * TwoPi;
}

static double Wrap(double x) => x - TwoPi * Math.Floor(x / TwoPi);

// A raised-cosine fade to silence over the last seconds.
static double[] FadeOut(double[] s, double seconds)
{
    int length = Seconds(seconds);
    for (int i = 0; i < length; i++) s[s.Length - 1 - i] *= 0.5 - 0.5 * Cos(Pi * i / length);
    return s;
}

// Sets the loudest 300 ms to Loudness, measured as SoundCheck measures it.
static double[] Level(double[] s)
{
    int window = Math.Min(s.Length, Seconds(SoundCheck.LoudnessWindowSeconds));
    int step = Rate / 100;
    double loudest = 0;
    for (int start = 0; start + window <= s.Length; start += step)
    {
        double sum = 0;
        for (int i = start; i < start + window; i++) sum += s[i] * s[i];
        loudest = Math.Max(loudest, sum / window);
    }
    double scale = Loudness / Math.Sqrt(loudest);
    var leveled = new double[s.Length];
    for (int i = 0; i < s.Length; i++) leveled[i] = s[i] * scale;
    return leveled;
}

static int Seconds(double seconds) => (int)Math.Round(seconds * Rate, MidpointRounding.ToEven);

static byte[] Wav(double[] samples)
{
    int dataBytes = samples.Length * 2;
    using var stream = new MemoryStream();
    using var w = new BinaryWriter(stream);
    w.Write("RIFF"u8);
    w.Write(36 + dataBytes);
    w.Write("WAVE"u8);
    w.Write("fmt "u8);
    w.Write(16);
    w.Write((short)1);
    w.Write((short)1);
    w.Write(Rate);
    w.Write(Rate * 2);
    w.Write((short)2);
    w.Write((short)16);
    w.Write("data"u8);
    w.Write(dataBytes);
    foreach (double v in samples) w.Write((short)Math.Clamp(Math.Round(v * 32768, MidpointRounding.ToEven), short.MinValue, short.MaxValue));
    w.Flush();
    return stream.ToArray();
}

static short[] Samples(byte[] wav)
{
    var samples = new short[(wav.Length - 44) / 2];
    for (int i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(wav, 44 + 2 * i);
    return samples;
}

static double Sin(double x)
{
    // x is in [0, 2π). Fold it into [-π/2, π/2], where the series below is accurate to 1e-15.
    if (x > Pi) x -= TwoPi;
    if (x > HalfPi) x = Pi - x;
    else if (x < -HalfPi) x = -Pi - x;
    double term = x;
    double sum = x;
    double square = x * x;
    for (int k = 1; k <= 12; k++)
    {
        term = -term * square / ((2 * k) * (2 * k + 1));
        sum += term;
    }
    return sum;
}

static double Cos(double x) => Sin(Wrap(x + HalfPi));

// For x <= 0: halve until small, sum the series, square back.
static double Exp(double x)
{
    if (x > 0) x = 0;
    if (x < -700) return 0;
    int halvings = 0;
    while (x < -0.5)
    {
        x /= 2;
        halvings++;
    }
    double term = 1;
    double sum = 1;
    for (int k = 1; k <= 20; k++)
    {
        term = term * x / k;
        sum += term;
    }
    for (int i = 0; i < halvings; i++) sum *= sum;
    return sum;
}

static string FindRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "tools", "sounds", "gen.cs"))) return dir.FullName;
    }
    throw new InvalidOperationException("Run this from inside the SteppedOut repository.");
}

partial class Program
{
    const double Pi = 3.141592653589793;
    const double HalfPi = 1.5707963267948966;
    const double TwoPi = 6.283185307179586;

    // The notes, in Hz (A4 = 440, equal temperament).
    const double C4 = 261.6255653005986;
    const double E4 = 329.6275569128699;
    const double F4 = 349.2282314330039;
    const double G4 = 391.99543598174927;
    const double A4 = 440.0;
    const double B4 = 493.8833012561241;
    const double C5 = 523.2511306011972;
    const double D5 = 587.3295358348151;
    const double E5 = 659.2551138257398;
    const double G5 = 783.9908719634985;
}
