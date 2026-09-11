using System.Text.Json;

namespace SteppedOut;

sealed class Config
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public double IdleMinutes { get; set; } = 15;
    public int GraceSeconds { get; set; } = 15;
    public bool JitterFilter { get; set; } = true;
    public int JitterPixels { get; set; } = 5;
    public bool SuppressWhileAudioPlaying { get; set; } = true;
    public bool SuppressWhileFullscreen { get; set; } = true;
    public bool SuppressWhileDisplayRequested { get; set; } = true;
    public List<string> NeverLockWhileRunning { get; set; } = new();
    public double AudioPeakThreshold { get; set; } = 0.01;
    public int AudioSilenceSeconds { get; set; } = 30;
    public int LogMaxKilobytes { get; set; } = 512;

    // Loads the file, or writes one with the defaults so there is something to hand-edit. A file
    // that does not parse is left untouched and reported; the defaults apply until it is fixed.
    public static Config Load(string path, out string? problem)
    {
        problem = null;
        if (!File.Exists(path))
        {
            var fresh = new Config();
            try
            {
                fresh.Save(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                problem = $"could not write {path}: {e.Message}";
            }
            return fresh;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<Config>(File.ReadAllText(path), JsonOptions) ?? new Config();
            loaded.Clamp();
            return loaded;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            problem = $"could not read {path}, using defaults: {e.Message}";
            return new Config();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions) + Environment.NewLine);
    }

    public void Clamp()
    {
        IdleMinutes = Math.Clamp(IdleMinutes, 0.05, 24 * 60);
        GraceSeconds = Math.Clamp(GraceSeconds, 0, 600);
        JitterPixels = Math.Clamp(JitterPixels, 1, 100);
        AudioPeakThreshold = Math.Clamp(AudioPeakThreshold, 0.001, 1);
        AudioSilenceSeconds = Math.Clamp(AudioSilenceSeconds, 0, 600);
        LogMaxKilobytes = Math.Clamp(LogMaxKilobytes, 64, 65536);
    }
}

// Reloads the file when it changes on disk so hand edits apply without a restart.
sealed class ConfigWatcher : IDisposable
{
    readonly FileSystemWatcher files;
    readonly System.Windows.Forms.Timer debounce = new() { Interval = 500 };
    readonly SynchronizationContext ui;

    public event Action? Changed;

    public ConfigWatcher(string path)
    {
        ui = SynchronizationContext.Current ?? throw new InvalidOperationException("ConfigWatcher needs the UI thread");
        debounce.Tick += (_, _) =>
        {
            debounce.Stop();
            Changed?.Invoke();
        };
        files = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        files.Changed += Bump;
        files.Created += Bump;
        files.Renamed += Bump;
        files.EnableRaisingEvents = true;
    }

    void Bump(object sender, FileSystemEventArgs e)
    {
        // FileSystemWatcher raises on a pool thread; editors also write in several bursts.
        ui.Post(_ =>
        {
            debounce.Stop();
            debounce.Start();
        }, null);
    }

    public void Dispose()
    {
        files.Dispose();
        debounce.Dispose();
    }
}
