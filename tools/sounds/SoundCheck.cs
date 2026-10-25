// Measures a sound SteppedOut ships and says what is wrong with it, if anything: too loud, too
// sudden at the start, too bright, too short or too long, or not fading out. tools/sounds/gen.cs
// runs it on every sound it makes and stops when one fails; the tests compile this same file and
// run it on the sounds in the exe and on sounds made to fail.

static class SoundCheck
{
    public const int Rate = 44100;

    // Every sound lasts from half a second to a second and a half.
    public const double ShortestSeconds = 0.5;
    public const double LongestSeconds = 1.5;

    // The loudest sample stays well below full scale.
    public const double LoudestPeakDb = -12;

    // The sound reaches its first full level no sooner than this, so it never starts with a click.
    public const double ShortestAttackMs = 10;

    // Brightness: the spectral centroid, and the share of the sound's energy above 4 kHz.
    public const double BrightestCentroidHz = 2000;
    public const double HighFromHz = 4000;
    public const double MostHighShare = 0.01;

    // Loudness is the loudest 300 ms of the sound, as RMS. Each sound is within this many dB of
    // the set's average.
    public const double LoudnessWindowSeconds = 0.3;
    public const double LoudnessSpreadDb = 1.5;

    // The last 10 ms are this far below the loudest sample at least: the sound fades out.
    public const double EndBelowPeakDb = 40;

    public sealed record Measure(double Seconds, double PeakDb, double LoudnessDb, double AttackMs, double CentroidHz, double HighShare, double EndDb);

    // samples are 16-bit values at 44.1 kHz, one channel.
    public static Measure Of(IReadOnlyList<short> samples)
    {
        int n = samples.Count;
        var x = new double[n];
        for (int i = 0; i < n; i++) x[i] = samples[i] / 32768.0;
        double peak = 0;
        foreach (double v in x) peak = Math.Max(peak, Math.Abs(v));
        return new Measure(
            n / (double)Rate,
            Db(peak),
            Loudness(x),
            Attack(x, peak) * 1000,
            Centroid(x, out double highShare),
            highShare,
            End(x, peak));
    }

    // What is wrong with one sound, in plain words; empty when nothing is. setLoudness is the average
    // loudness of all the sounds, in dB.
    public static List<string> Problems(Measure m, double setLoudness)
    {
        var problems = new List<string>();
        if (m.Seconds < ShortestSeconds || m.Seconds > LongestSeconds) problems.Add($"lasts {m.Seconds:0.00} s, not {ShortestSeconds}-{LongestSeconds} s");
        if (m.PeakDb > LoudestPeakDb) problems.Add($"too loud: peak {m.PeakDb:0.0} dBFS, above {LoudestPeakDb} dBFS");
        if (m.AttackMs < ShortestAttackMs) problems.Add($"clicky: reaches full level in {m.AttackMs:0.0} ms, under {ShortestAttackMs} ms");
        if (m.CentroidHz > BrightestCentroidHz) problems.Add($"harsh: centroid {m.CentroidHz:0} Hz, above {BrightestCentroidHz} Hz");
        if (m.HighShare > MostHighShare) problems.Add($"harsh: {m.HighShare:P1} of its energy above {HighFromHz} Hz, more than {MostHighShare:P0}");
        if (Math.Abs(m.LoudnessDb - setLoudness) > LoudnessSpreadDb) problems.Add($"loudness {m.LoudnessDb:0.0} dB is more than {LoudnessSpreadDb} dB from the set's {setLoudness:0.0} dB");
        if (m.EndDb > -EndBelowPeakDb) problems.Add($"ends abruptly: the last 10 ms are only {-m.EndDb:0} dB below its peak");
        return problems;
    }

    public static double Db(double linear) => linear <= 0 ? -200 : 20 * Math.Log10(linear);

    // The loudest stretch of 300 ms, or the whole sound when shorter, in steps of 10 ms.
    static double Loudness(double[] x)
    {
        int window = Math.Min(x.Length, (int)(LoudnessWindowSeconds * Rate));
        if (window == 0) return -200;
        int step = Rate / 100;
        double loudest = 0;
        for (int start = 0; start + window <= x.Length; start += step)
        {
            double sum = 0;
            for (int i = start; i < start + window; i++) sum += x[i] * x[i];
            loudest = Math.Max(loudest, sum / window);
        }
        return Db(Math.Sqrt(loudest));
    }

    // How long the sound takes from its first audible sample to 90% of the highest level of its
    // opening 150 ms, measured in 1 ms steps.
    static double Attack(double[] x, double peak)
    {
        if (peak <= 0) return 0;
        int first = Array.FindIndex(x, v => Math.Abs(v) > peak * 0.001);
        if (first < 0) return 0;
        int block = Rate / 1000;
        int end = Math.Min(x.Length, first + (int)(0.15 * Rate));
        var levels = new List<double>();
        for (int at = first; at < end; at += block)
        {
            double level = 0;
            for (int i = at; i < Math.Min(end, at + block); i++) level = Math.Max(level, Math.Abs(x[i]));
            levels.Add(level);
        }
        double top = levels.Max();
        int reached = levels.FindIndex(level => level >= top * 0.9);
        return reached / 1000.0;
    }

    // The power spectrum of the whole sound, from Hann-windowed blocks of 2048 samples half
    // overlapping; its centroid and the share of it above 4 kHz.
    static double Centroid(double[] x, out double highShare)
    {
        const int size = 2048;
        var power = new double[size / 2 + 1];
        var re = new double[size];
        var im = new double[size];
        for (int start = 0; start < Math.Max(1, x.Length - size / 2); start += size / 2)
        {
            for (int i = 0; i < size; i++)
            {
                double v = start + i < x.Length ? x[start + i] : 0;
                re[i] = v * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (size - 1)));
                im[i] = 0;
            }
            Fft(re, im);
            for (int k = 0; k < power.Length; k++) power[k] += re[k] * re[k] + im[k] * im[k];
        }
        double total = 0, weighted = 0, high = 0;
        for (int k = 1; k < power.Length; k++)
        {
            double hz = k * (double)Rate / size;
            total += power[k];
            weighted += hz * power[k];
            if (hz >= HighFromHz) high += power[k];
        }
        highShare = total > 0 ? high / total : 0;
        return total > 0 ? weighted / total : 0;
    }

    // In place, radix 2; re.Length is a power of two.
    static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (int length = 2; length <= n; length <<= 1)
        {
            double angle = -2 * Math.PI / length;
            double wr = Math.Cos(angle), wi = Math.Sin(angle);
            for (int i = 0; i < n; i += length)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < length / 2; k++)
                {
                    int a = i + k, b = i + k + length / 2;
                    double tr = re[b] * cr - im[b] * ci;
                    double ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                    double next = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = next;
                }
            }
        }
    }

    // The loudest of the last 10 ms, in dB below the peak (negative).
    static double End(double[] x, double peak)
    {
        int tail = Math.Min(x.Length, Rate / 100);
        double level = 0;
        for (int i = x.Length - tail; i < x.Length; i++) level = Math.Max(level, Math.Abs(x[i]));
        return peak <= 0 ? -200 : Db(level / peak);
    }
}
