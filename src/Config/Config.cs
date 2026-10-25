using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace SteppedOut;

// The choices of the settings added in 1.0. The file spells each in camelCase: "pauseUntilBack".
enum MediaOnLock
{
    PauseUntilBack,
    PauseStayPaused,
    KeepPlaying,
}

// The sounds for leaving the PC and coming back. Each is a pair under one name: the away sound
// falls and the back sound rises. SteppedOut is the app's own; Own is a .wav file of the person's
// own (SoundWhenAwayFile, SoundWhenBackFile). "click" and "chime", the sounds before these, read as
// SteppedOut.
enum ActionSound
{
    Off,
    [Formerly("click", "chime")]
    SteppedOut,
    Drift,
    Pebble,
    Glow,
    Ripple,
    Own,
}

// The sounds for break reminders: SteppedOut's own first, then four more, or a .wav file of the
// person's own (BreakSoundFile). "softChime", the sound before these, reads as SteppedOut.
enum BreakSound
{
    Off,
    [Formerly("softChime")]
    SteppedOut,
    Dawn,
    Meadow,
    Lantern,
    Tide,
    Own,
}

enum ThemeChoice
{
    System,
    Light,
    Dark,
}

enum TextSize
{
    System,
    Small,
    Medium,
    Large,
}

// What happens when SteppedOut locks the PC by itself, at the lock time: only the lock, or the lock
// and then the screen off or the PC asleep. Lock now, the shortcut and a lock Windows makes are
// only the lock. The file spells each in camelCase: "lockScreenOff".
enum WhenLocking
{
    Lock,
    LockScreenOff,
    LockSleep,
}

enum NotePosition
{
    BottomRight,
    TopRight,
    BottomLeft,
    Center,
    TopLeft,
}

// When an app rule applies: while the app runs at all, or only while one of its windows can be
// seen on a screen, not minimized.
enum AppWhen
{
    Running,
    OnScreen,
}

// One app's rule. App is the program's name; Path and Aumid, when the app picker found them, say
// more exactly which program it is. A record nobody changes, so copies of the settings can share it.
sealed record AppRule(string App, Effect Effect, AppWhen When = AppWhen.Running, string? Path = null, string? Aumid = null)
{
    // A rule can do any of the four; "none" would be no rule at all.
    public static IReadOnlyList<EffectKind> Kinds { get; } = new[] { EffectKind.Never, EffectKind.Extra, EffectKind.Sooner, EffectKind.Cap };
}

// The effects a setting can hold; its reader takes no others.
[AttributeUsage(AttributeTargets.Property)]
sealed class EffectKindsAttribute(params EffectKind[] kinds) : Attribute
{
    public IReadOnlyList<EffectKind> Kinds { get; } = kinds;
}

sealed class Config
{
    public const double MinIdleMinutes = 0.05;
    public const double MaxIdleMinutes = 24 * 60;
    public const double DefaultIdleMinutes = 15;
    public const int CurrentVersion = 1;

    // The ranges of the numbers in the settings 1.0 added: minutes of an extra or a latest time,
    // the percent of a "sooner", and the minutes before a break reminder.
    public const int MinEffectMinutes = 1;
    public const int MaxEffectMinutes = 24 * 60;
    public const int MinSoonerPercent = 1;
    public const int MaxSoonerPercent = 95;
    public const int MinBreakMinutes = 1;
    public const int MaxBreakMinutes = 12 * 60;

    // The settings window's tabs, from Locking (0) to Extras (4).
    public const int MaxSettingsTab = 4;

    // The snooze time: 0 for none, or any whole minutes up to four hours.
    public const int MaxSnoozeMinutes = 4 * 60;

    // The log's size limit, in KB.
    public const int MinLogKilobytes = 64;
    public const int MaxLogKilobytes = 65536;

    // The names from before 1.0 that 1.0 kept. They keep their strict readers too: a value of the
    // wrong kind in one of them sets the whole file aside, as it always has. Every other setting is
    // new in 1.0 and has a lenient reader. Nothing is read from a file from before 1.0 itself (D50).
    static readonly HashSet<string> KeptKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "language", "idleMinutes", "graceSeconds", "lockWhenAway", "muteWhenAway", "screensOffWhenAway", "runWhenAway",
        "runWhenBack", "showTimeAway", "jitterFilter", "jitterPixels", "audioPeakThreshold", "audioSilenceSeconds",
        "logMaxKilobytes", "welcomed",
    };

    // Settings an earlier 1.0 build wrote and this one no longer has: buttonColor, as the buttons are
    // always the app's green now. A file that still has one loads as if it were not there, with no
    // warning, and the next save leaves it out.
    internal static readonly HashSet<string> Retired = new(StringComparer.OrdinalIgnoreCase) { "buttonColor" };

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // A hand edit that writes "IdleMinutes" means idleMinutes.
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), new WholeNumber() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { ReadersAndWriters } },
        // The file is for people to read and edit, not a web page: "Ctrl+Alt+L" stays as typed, and
        // a folder with Arabic letters in its name stays readable, instead of each of those
        // characters being written as a six-character escape code.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // The shape of the file: 1 since 1.0. A file from before has none, or one below 1, and is not
    // read at all (D50).
    public int ConfigVersion { get; set; } = CurrentVersion;
    public string Language { get; set; } = "auto";
    public double IdleMinutes { get; set; } = DefaultIdleMinutes;
    public int GraceSeconds { get; set; } = 15;
    public bool LockWhenAway { get; set; } = true;

    // What comes after SteppedOut's own lock: only the lock until another is picked.
    public WhenLocking WhenLocking { get; set; } = WhenLocking.Lock;

    [EffectKinds(EffectKind.None, EffectKind.Never, EffectKind.Extra)]
    public Effect MusicVideo { get; set; } = Effect.Never;

    [EffectKinds(EffectKind.None, EffectKind.Never, EffectKind.Extra)]
    public Effect FullScreen { get; set; } = Effect.Never;

    [EffectKinds(EffectKind.None, EffectKind.Never, EffectKind.Extra)]
    public Effect OnCall { get; set; } = Effect.Never;

    [EffectKinds(EffectKind.None, EffectKind.Sooner, EffectKind.Cap)]
    public Effect OnBattery { get; set; } = Effect.None;

    public List<AppRule> AppRules { get; set; } = new();
    public bool CountController { get; set; } = true;

    public MediaOnLock MediaOnLock { get; set; } = MediaOnLock.PauseUntilBack;
    public bool MuteWhenAway { get; set; }
    public bool ScreensOffWhenAway { get; set; }
    public ActionSound SoundWhenAway { get; set; } = ActionSound.Off;

    // The .wav file picked with "Your own sound…", kept when another sound is chosen so the list can
    // still offer it; "" until one is picked.
    public string SoundWhenAwayFile { get; set; } = "";

    // Full paths, or shell:AppsFolder\ and an app's ID for a Store app.
    public List<string> OpenWhenAway { get; set; } = new();

    // Program names.
    public List<string> CloseWhenAway { get; set; } = new();
    public string RunWhenAway { get; set; } = "";
    public bool ShowTimeAway { get; set; } = true;
    public ActionSound SoundWhenBack { get; set; } = ActionSound.Off;
    public string SoundWhenBackFile { get; set; } = "";
    public List<string> OpenWhenBack { get; set; } = new();
    public List<string> CloseWhenBack { get; set; } = new();
    public string RunWhenBack { get; set; } = "";

    // Off, and an hour, for a file that does not have them: an upgrade never turns on what nobody
    // picked. A brand-new install starts with them on, every 2 hours (FirstStart).
    public bool BreakReminders { get; set; }
    public int BreakMinutes { get; set; } = 60;
    public BreakSound BreakSound { get; set; } = BreakSound.Off;
    public string BreakSoundFile { get; set; } = "";
    public bool EyeBreaks { get; set; }
    public int SnoozeMinutes { get; set; } = 10;

    // "" turns the shortcut off, as it is until someone picks one.
    public string LockShortcut { get; set; } = "";
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
    public TextSize TextSize { get; set; } = TextSize.System;
    public bool MinutesOnTrayIcon { get; set; }

    // Where the corner notes appear. Null, and so not written, until a place is picked: until then
    // they keep to the side the Windows shell uses, as before 1.0.
    public NotePosition? NotificationPlace { get; set; }

    [JsonIgnore]
    public NotePosition NotificationPlaceInUse => NotificationPlace ?? shellSide;

    // Read with the settings, so it is decided when they load.
    NotePosition shellSide = ShellSide(Lang.ShellIsRightToLeft());

    // The tab the settings window was last on, which it opens on next time: Locking at first.
    public int SettingsTab { get; set; }

    public bool JitterFilter { get; set; } = true;
    public int JitterPixels { get; set; } = 5;
    public double AudioPeakThreshold { get; set; } = 0.01;
    public int AudioSilenceSeconds { get; set; } = 30;
    public int LogMaxKilobytes { get; set; } = 512;
    public bool Welcomed { get; set; }

    // The file from before 1.0 these settings, the defaults, stand in for: nothing in it is read
    // (D50). Null for settings read from a 1.0 file or made in code. Not a setting, so it is never
    // written and SameAs does not see it.
    internal OldFile? Old { get; private set; }

    // How long a brand-new install waits before its first break reminder.
    public const int FirstStartBreakMinutes = 120;

    // The settings of a brand-new install, with no settings file yet: the defaults, with break
    // reminders on every 2 hours. Reset all settings goes back to these too.
    public static Config FirstStart() => new() { BreakReminders = true, BreakMinutes = FirstStartBreakMinutes };

    public static Config Load(string path, out string? problem) => Load(path, out problem, out _);

    // Loads the file, or writes one with the defaults so there is something to hand-edit. A file
    // that does not parse is left untouched and reported; the defaults apply until it is fixed.
    // A name that matches no setting, often a typo, is only a warning: the rest of the file loads.
    public static Config Load(string path, out string? problem, out string? warning) => Load(path, makeIfMissing: true, out problem, out warning);

    // Without makeIfMissing, for --once and a second start, which write nothing: no file gives the
    // defaults, and none is made. No file is a brand-new install, which starts with FirstStart; a
    // file that cannot be read or used gives the plain defaults.
    public static Config Load(string path, bool makeIfMissing, out string? problem, out string? warning)
    {
        problem = null;
        warning = null;
        if (!File.Exists(path))
        {
            var fresh = FirstStart();
            if (!makeIfMissing) return fresh;
            try
            {
                fresh.Save(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                problem = $"could not write {path}: {Reason(e)}";
            }
            return fresh;
        }

        string text;
        try
        {
            text = Read(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problem = $"could not read {path}: {Reason(e)}";
            return new Config();
        }
        return Parse(text, path, out problem, out warning);
    }

    // The settings in text read from path, which only names the file in the messages.
    public static Config Parse(string text, string path, out string? problem, out string? warning) =>
        Parse(text, path, shellRightToLeft: null, out problem, out warning);

    // shellRightToLeft stands in for the direction of the Windows shell, so a test can try both.
    internal static Config Parse(string text, string path, bool? shellRightToLeft, out string? problem, out string? warning)
    {
        problem = null;
        warning = null;
        try
        {
            // Text that is not JSON is no file from before 1.0 either: the reader below fails on
            // it too and says where, with the setting's name, which the first look cannot give.
            var (keys, before) = LookOrNone(text);
            Config loaded;
            if (before is not null)
            {
                // Nothing carries over from before 1.0 (D50): the defaults, the welcome among them,
                // stand in for the file. The copy that runs keeps it next to it (FreshStart).
                loaded = new Config { Old = new OldFile(text, before) };
            }
            else
            {
                IReadOnlyList<string> skipped;
                using (var lenient = Lenient.Collect())
                {
                    loaded = JsonSerializer.Deserialize<Config>(text, JsonOptions) ?? new Config();
                    skipped = lenient.Skipped;
                }
                warning = Join(UnknownKeys(keys, path), Skipped(skipped, path));
            }
            if (shellRightToLeft is bool rightToLeft) loaded.shellSide = ShellSide(rightToLeft);
            loaded.Clamp();
            return loaded;
        }
        catch (JsonException e)
        {
            problem = $"could not read {path}: {Describe(e)}";
            return new Config();
        }
    }

    // Shared for writing and deleting too, unlike File.ReadAllText: a program or an antivirus that
    // has the file open for writing does not stop the read, and the read does not stop another
    // program writing the file, deleting it or swapping in its new copy with File.Replace. A rename
    // over the file (File.Move with overwrite) still fails while it is open, so the read is short.
    public static string Read(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(file);
        return reader.ReadToEnd();
    }

    // What went wrong, in the exception's words or, when it has none, by its name and code. A log
    // line that ends in "config.json: " says nothing.
    public static string Reason(Exception e) =>
        string.IsNullOrWhiteSpace(e.Message) ? $"{e.GetType().Name} 0x{e.HResult:X8}" : e.Message;

    // The reader counts lines from 0 and adds its position to the message in that form. People
    // count from 1, as their editor does, and look for a setting by its name.
    static string Describe(JsonException e)
    {
        if (e.LineNumber is not long line) return Reason(e);
        string message = Reason(e);
        int cut = message.IndexOf(" Path: ", StringComparison.Ordinal);
        if (cut > 0) message = message[..cut];
        string setting = e.Path is { } at && at.StartsWith("$.", StringComparison.Ordinal) ? $" ({at[2..]})" : "";
        return $"line {line + 1}{setting}: {message}";
    }

    // The reader skips a name it has no setting for without a word. The names 1.0 replaced are not
    // known any more: in a 1.0 file they are as unknown as a typo (D50). A setting 1.0 had and then
    // dropped (Retired) is skipped quietly too, since the app itself wrote it there.
    static string? UnknownKeys(IEnumerable<string> keys, string path)
    {
        var known = JsonOptions.GetTypeInfo(typeof(Config)).Properties.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = keys.Where(k => !known.Contains(k) && !Retired.Contains(k)).Select(k => $"'{k}'").ToList();
        return unknown.Count == 0 ? null
            : $"ignored {(unknown.Count == 1 ? "an unknown setting" : "unknown settings")} in {path}: {string.Join(", ", unknown)}";
    }

    // What the lenient readers skipped, each with the setting's name and what was wrong.
    static string? Skipped(IReadOnlyList<string> skipped, string path) => skipped.Count == 0 ? null
        : $"skipped {(skipped.Count == 1 ? "a value" : "values")} it could not read in {path}, keeping the defaults: {string.Join(", ", skipped)}";

    static string? Join(string? first, string? second) => first is null ? second : second is null ? first : first + "; " + second;

    // The side the corner notes keep to until a place is picked: the clock and the tray are on the
    // left when the Windows shell is laid out right to left.
    public static NotePosition ShellSide(bool shellRightToLeft) => shellRightToLeft ? NotePosition.BottomLeft : NotePosition.BottomRight;

    // Every name added in 1.0 gets its lenient reader, with the default it falls back to, and one
    // that may be left unset is written only once set.
    static void ReadersAndWriters(JsonTypeInfo info)
    {
        if (info.Type != typeof(Config)) return;
        var defaults = new Config();
        foreach (var property in info.Properties)
        {
            if (KeptKeys.Contains(property.Name)) continue;
            var kinds = property.AttributeProvider?.GetCustomAttributes(typeof(EffectKindsAttribute), inherit: false)
                .OfType<EffectKindsAttribute>().FirstOrDefault()?.Kinds;
            property.CustomConverter = Lenient.For(property.PropertyType, property.Name, property.Get?.Invoke(defaults), kinds);
            if (Nullable.GetUnderlyingType(property.PropertyType) is not null) property.ShouldSerialize = static (_, value) => value is not null;
        }
    }

    // Returns what it wrote, so the tray app knows its own save when the file changes. A file from
    // before 1.0 still on disk, never replaced or put back by an older copy, is kept next to it
    // first; one that cannot be kept is not saved over (FreshStart.KeepBeforeSave throws).
    public string Save(string path)
    {
        FreshStart.KeepBeforeSave(path);
        return Write(path);
    }

    // The file is the old settings or the new ones, never part of each: a crash or a full disk while
    // saving leaves the old file as it was.
    internal string Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string text = JsonSerializer.Serialize(this, JsonOptions) + Environment.NewLine;
        WholeFile.Replace(path, WholeFile.Bytes(text));
        return text;
    }

    // Every setting the file would hold is the same.
    public bool SameAs(Config other) =>
        JsonSerializer.Serialize(this, JsonOptions) == JsonSerializer.Serialize(other, JsonOptions);

    // A copy to change without changing this one. Every list is copied; the app rules in them are
    // records nobody changes, so they are shared.
    public Config Copy()
    {
        var copy = (Config)MemberwiseClone();
        copy.AppRules = new List<AppRule>(AppRules);
        copy.OpenWhenAway = new List<string>(OpenWhenAway);
        copy.CloseWhenAway = new List<string>(CloseWhenAway);
        copy.OpenWhenBack = new List<string>(OpenWhenBack);
        copy.CloseWhenBack = new List<string>(CloseWhenBack);
        return copy;
    }

    public void Clamp()
    {
        string language = (Language ?? "").Trim().ToLowerInvariant();
        Language = language is "en" or "ar" ? language : "auto";
        // A lock time that is not a number at all falls back to the default, never to the longest
        // time, so a bad value still locks.
        IdleMinutes = double.IsFinite(IdleMinutes) ? Math.Clamp(IdleMinutes, MinIdleMinutes, MaxIdleMinutes) : DefaultIdleMinutes;
        GraceSeconds = Math.Clamp(GraceSeconds, 0, 600);
        RunWhenAway = (RunWhenAway ?? "").Trim().Trim('"');
        RunWhenBack = (RunWhenBack ?? "").Trim().Trim('"');
        JitterPixels = Math.Clamp(JitterPixels, 1, 100);
        AudioPeakThreshold = double.IsNaN(AudioPeakThreshold) ? 0.01 : Math.Clamp(AudioPeakThreshold, 0.001, 1);
        AudioSilenceSeconds = Math.Clamp(AudioSilenceSeconds, 0, 600);
        LogMaxKilobytes = Math.Clamp(LogMaxKilobytes, MinLogKilobytes, MaxLogKilobytes);

        // The settings 1.0 added. Their readers only take values of the right kind; these are for
        // numbers out of range and for values set in code.
        MusicVideo = Within(MusicVideo, BusyKinds, Effect.Never);
        FullScreen = Within(FullScreen, BusyKinds, Effect.Never);
        OnCall = Within(OnCall, BusyKinds, Effect.Never);
        OnBattery = Within(OnBattery, BatteryKinds, Effect.None);
        AppRules = OneRulePerApp(AppRules);
        WhenLocking = Enum.IsDefined(WhenLocking) ? WhenLocking : WhenLocking.Lock;
        MediaOnLock = Enum.IsDefined(MediaOnLock) ? MediaOnLock : MediaOnLock.PauseUntilBack;
        // Your own sound with no file picked is SteppedOut's own, as a missing file would play.
        SoundWhenAwayFile = OwnSound.Tidy(SoundWhenAwayFile);
        SoundWhenBackFile = OwnSound.Tidy(SoundWhenBackFile);
        BreakSoundFile = OwnSound.Tidy(BreakSoundFile);
        SoundWhenAway = !Enum.IsDefined(SoundWhenAway) ? ActionSound.Off
            : SoundWhenAway == ActionSound.Own && SoundWhenAwayFile.Length == 0 ? ActionSound.SteppedOut : SoundWhenAway;
        SoundWhenBack = !Enum.IsDefined(SoundWhenBack) ? ActionSound.Off
            : SoundWhenBack == ActionSound.Own && SoundWhenBackFile.Length == 0 ? ActionSound.SteppedOut : SoundWhenBack;
        OpenWhenAway = Tidy(OpenWhenAway, entry => entry);
        OpenWhenBack = Tidy(OpenWhenBack, entry => entry);
        CloseWhenAway = Tidy(CloseWhenAway, ProcessRule.Normalize);
        CloseWhenBack = Tidy(CloseWhenBack, ProcessRule.Normalize);
        BreakMinutes = Math.Clamp(BreakMinutes, MinBreakMinutes, MaxBreakMinutes);
        BreakSound = !Enum.IsDefined(BreakSound) ? BreakSound.Off
            : BreakSound == BreakSound.Own && BreakSoundFile.Length == 0 ? BreakSound.SteppedOut : BreakSound;
        SnoozeMinutes = Math.Clamp(SnoozeMinutes, 0, MaxSnoozeMinutes);
        // A shortcut that cannot be one is off, rather than a typo that looks set.
        string shortcut = (LockShortcut ?? "").Trim();
        LockShortcut = IsShortcut(shortcut) ? shortcut : "";
        Theme = Enum.IsDefined(Theme) ? Theme : ThemeChoice.System;
        TextSize = Enum.IsDefined(TextSize) ? TextSize : TextSize.System;
        if (NotificationPlace is NotePosition place && !Enum.IsDefined(place)) NotificationPlace = null;
        SettingsTab = Math.Clamp(SettingsTab, 0, MaxSettingsTab);
    }

    // The effects each setting may hold, from its attribute, as its reader takes them.
    static readonly IReadOnlyList<EffectKind> BusyKinds = KindsOf(nameof(MusicVideo));
    static readonly IReadOnlyList<EffectKind> BatteryKinds = KindsOf(nameof(OnBattery));

    static IReadOnlyList<EffectKind> KindsOf(string property) =>
        typeof(Config).GetProperty(property)!.GetCustomAttributes(typeof(EffectKindsAttribute), inherit: false)
            .OfType<EffectKindsAttribute>().Single().Kinds;

    // An effect the setting may hold, with its number in range; anything else is the fallback.
    // Never and none carry no number, so two of them are always equal.
    internal static Effect Within(Effect effect, IReadOnlyList<EffectKind> kinds, Effect fallback) =>
        !kinds.Contains(effect.Kind) ? fallback : effect.Kind switch
        {
            EffectKind.Extra or EffectKind.Cap => effect with { Amount = Math.Clamp(effect.Amount, MinEffectMinutes, MaxEffectMinutes) },
            EffectKind.Sooner => effect with { Amount = Math.Clamp(effect.Amount, MinSoonerPercent, MaxSoonerPercent) },
            _ => new Effect(effect.Kind),
        };

    // One rule per app (D3): rules for the same program, by name or by its full path, are one, and
    // the first one wins. A rule that names no app or does nothing is left out.
    static List<AppRule> OneRulePerApp(List<AppRule>? rules)
    {
        var kept = new List<AppRule>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules ?? new())
        {
            if (rule is null) continue;
            string app = (rule.App ?? "").Trim().Trim('"').Trim();
            string name = ProcessRule.Normalize(app);
            var effect = Within(rule.Effect, AppRule.Kinds, Effect.None);
            if (name.Length == 0 || effect.Kind == EffectKind.None || !seen.Add(name)) continue;
            kept.Add(rule with
            {
                App = app,
                Effect = effect,
                When = Enum.IsDefined(rule.When) ? rule.When : AppWhen.Running,
                Path = LocalPath(rule.Path),
                Aumid = string.IsNullOrWhiteSpace(rule.Aumid) ? null : rule.Aumid.Trim(),
            });
        }
        return kept;
    }

    // A program's full path on this PC, or null. The rule matches by the app's name either way; the
    // path only gives the settings window the app's icon and description, and reading those from a
    // network path or a mapped network drive would reach out to another PC. One on a drive letter
    // that is not connected now, such as a USB stick that is out, is kept: the window reads nothing
    // from it until it is a drive on this PC again.
    static string? LocalPath(string? path)
    {
        string trimmed = (path ?? "").Trim().Trim('"').Trim();
        return LocalPaths.Place(trimmed) is PathPlace.ThisPC or PathPlace.NoDrive ? trimmed : null;
    }

    // A list without blanks, without spaces or quotes around its entries, and without the same
    // entry twice, ignoring case: by the whole entry for apps to open, by the program's name for
    // apps to close. The first spelling is kept.
    static List<string> Tidy(List<string>? list, Func<string, string> key)
    {
        var kept = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? entry in list ?? new())
        {
            string tidy = (entry ?? "").Trim().Trim('"').Trim();
            string same = tidy.Length == 0 ? "" : key(tidy);
            if (same.Length > 0 && seen.Add(same)) kept.Add(tidy);
        }
        return kept;
    }

    // The lock shortcut: a shortcut the shortcut reader accepts (Ctrl or Alt or both, Shift if
    // wanted, and one key, as "Ctrl+Alt+L"), or "" for off. The same reader decides what can be
    // registered, so the file and the shortcut row never disagree.
    internal static bool IsShortcut(string text) => text.Length == 0 || Shortcut.TryParse(text, out _);

    // Where text comes from when it is a settings file from before 1.0, or null. Text that is not
    // JSON at all is not known to be one: it is a file that does not load.
    internal static string? OldShape(string text) => LookOrNone(text).Before;

    static (HashSet<string> Keys, string? Before) LookOrNone(string text)
    {
        try
        {
            return Look(text);
        }
        catch (JsonException)
        {
            return (new HashSet<string>(StringComparer.OrdinalIgnoreCase), null);
        }
    }

    // The names in a file, ignoring case as the reader does, and where the file comes from when it
    // is from before 1.0 (D50), the only files without a configVersion of 1 or more: "0.1", with its
    // one switch per signal, "0.x" for any other file without a configVersion, or "configVersion N"
    // for a number below 1. A version that is there but is no number is a 1.0 file that a hand edit
    // got wrong: the reader skips it and says so, and the rest of the file loads. Throws
    // JsonException for text that is not JSON.
    static (HashSet<string> Keys, string? Before) Look(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return (keys, null);
        JsonElement? version = null;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            keys.Add(property.Name);
            // The reader takes the last of a name given twice, so this does too.
            if (property.Name.Equals("configVersion", StringComparison.OrdinalIgnoreCase)) version = property.Value;
        }
        string? before = version switch
        {
            null => keys.Any(k => k.StartsWith("suppressWhile", StringComparison.OrdinalIgnoreCase)) ? "0.1" : "0.x",
            { ValueKind: JsonValueKind.Number } number when number.TryGetDouble(out double n) && n < CurrentVersion
                => "configVersion " + n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => null,
        };
        return (keys, before);
    }
}

// A settings file from before 1.0 as it was read: its text, so start-up can tell it is still the
// file on disk, and where it comes from, for the log.
sealed record OldFile(string Text, string Shape);

// Writes a whole file or nothing. The bytes go to a file of their own next to it, which is moved
// into place only once all of them are on the disk, so a crash, a full disk or an error part way
// leaves the file there before as it was, and a new file is never left half written under its
// name. The file of their own is removed whenever the write fails. It sits in the same folder, so
// the move is a rename on the same disk.
//
// Two kinds of file are written in place instead, as every save was before 1.0, because a new file
// moved over them would not end up where they are: a file with more than one name (a hard link),
// whose other names would keep the old settings, and, in the Store package, a file that is only in
// the real %APPDATA% (Packaged.KeepsInPlace). A symbolic link is followed to its file, which is
// saved as any other; the link stays.
static class WholeFile
{
    // Called with the file being written after half the bytes, for the tests to fail a write there.
    [ThreadStatic] internal static Action<string>? Midway;

    // Stands in for Packaged.KeepsInPlace in a test, which runs outside a package.
    [ThreadStatic] internal static Func<string, bool>? KeepsInPlace;

    // As File.WriteAllText writes text: UTF-8 without a byte order mark.
    public static byte[] Bytes(string text) => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);

    // Where the bytes are written first. Its name starts with ~ so it never looks like a backup,
    // whose names start with the file's own.
    public static string Temporary(string path) => Path.Combine(Path.GetDirectoryName(path)!, "~" + Path.GetFileName(path) + ".tmp");

    // Puts bytes in path, over the file there if there is one.
    public static void Replace(string path, byte[] bytes)
    {
        path = FileLinks.FinalTarget(path);
        if (File.Exists(path) && (FileLinks.Names(path) > 1 || (KeepsInPlace ?? Packaged.KeepsInPlace)(path)))
        {
            InPlace(path, bytes);
            return;
        }
        Whole(path, bytes);
    }

    // Not whole or nothing: a crash part way leaves a file that does not load, which the next start
    // reports and keeps as config.json.broken. The new bytes go over the old ones before the file is
    // cut to their length, so a program reading at that moment never finds it empty.
    static void InPlace(string path, byte[] bytes)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read | FileShare.Delete);
        file.Write(bytes);
        file.SetLength(bytes.Length);
        file.Flush(flushToDisk: true);
    }

    static void Whole(string path, byte[] bytes)
    {
        string temporary = Temporary(path);
        bool hadFile = File.Exists(path);
        bool written = false;
        try
        {
            Write(temporary, bytes);
            written = true;
            if (!hadFile)
            {
                File.Move(temporary, path);
                return;
            }
            try
            {
                // Works while another program reads the file with sharing for deletion, as
                // Config.Read does, where a rename over it would be refused.
                File.Replace(temporary, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && File.Exists(temporary))
            {
                // A file system that has no File.Replace, or a replace that stopped before its last
                // step, which can leave the new file under its own name with the old one gone. When
                // the move fails too, the replace's error says more, such as the program holding it.
                try
                {
                    File.Move(temporary, path, overwrite: true);
                }
                catch (Exception moving) when (moving is IOException or UnauthorizedAccessException && File.Exists(path))
                {
                    ExceptionDispatchInfo.Throw(e);
                }
            }
        }
        catch
        {
            CleanUp(temporary, path, onlyCopy: written && hadFile);
            throw;
        }
    }

    // Makes path with bytes, never over a file already there: an IOException when one is.
    public static void CreateNew(string path, byte[] bytes)
    {
        string temporary = Temporary(path);
        try
        {
            Write(temporary, bytes);
            File.Move(temporary, path, overwrite: false);
        }
        catch
        {
            CleanUp(temporary, path, onlyCopy: false);
            throw;
        }
    }

    static void Write(string temporary, byte[] bytes)
    {
        using var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None);
        int half = bytes.Length / 2;
        file.Write(bytes, 0, half);
        Midway?.Invoke(temporary);
        file.Write(bytes, half, bytes.Length - half);
        // On the disk, not only in a cache, before the move makes it the file.
        file.Flush(flushToDisk: true);
    }

    // After a failed write the file of its own goes. The one exception is a replace that took the
    // old file away and could not move the whole new one in (onlyCopy, with the file gone): it is
    // moved in now, and should even that fail it is kept, as the only copy of the settings, for
    // the next save to write over and move into place.
    static void CleanUp(string temporary, string path, bool onlyCopy)
    {
        try
        {
            if (!File.Exists(temporary)) return;
            if (onlyCopy && !File.Exists(path))
            {
                File.Move(temporary, path);
                return;
            }
            File.Delete(temporary);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done here; the caller reports the first error.
        }
    }
}

// Reloads the file when it changes on disk so hand edits apply without a restart.
sealed class ConfigWatcher : IDisposable
{
    readonly FileSystemWatcher files;
    readonly System.Windows.Forms.Timer debounce = new() { Interval = ConfigReload.WaitMilliseconds };
    readonly SynchronizationContext ui;
    // The wait running is for a change on disk, not for a read tried again.
    bool changedOnDisk;
    // A WinForms timer starts and ticks again even after it is disposed, so every start and every
    // tick checks this: a change posted just before Dispose never reloads a closed app.
    bool disposed;

    // True for the first read after a change on disk.
    public event Action<bool>? Changed;

    public ConfigWatcher(string path)
    {
        ui = SynchronizationContext.Current ?? throw new InvalidOperationException("ConfigWatcher needs the UI thread");
        debounce.Tick += (_, _) =>
        {
            debounce.Stop();
            if (disposed) return;
            bool first = changedOnDisk;
            changedOnDisk = false;
            Changed?.Invoke(first);
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
        if (disposed) return;
        ui.Post(_ =>
        {
            if (disposed) return;
            changedOnDisk = true;
            debounce.Stop();
            debounce.Start();
        }, null);
    }

    // For a read that may work a moment later: Changed comes again after the same wait, unless a
    // newer change on disk starts it over first.
    public void TryAgain()
    {
        if (!disposed) debounce.Start();
    }

    // Test seam: what a change on disk does, without the disk.
    internal void BumpForTest() => Bump(this, new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetTempPath(), "config.json"));

    internal bool Waiting => debounce.Enabled;

    public void Dispose()
    {
        disposed = true;
        debounce.Stop();
        files.Dispose();
        debounce.Dispose();
    }
}

// What one try at reading config.json again found.
enum Reloaded
{
    Unchanged,
    Loaded,
    TryAgain,
    Broken,
}

// Reads config.json again after it changed on disk. Another program's save can leave it locked,
// empty or half written for a moment, and an antivirus can hold a new file open briefly, so a read
// that fails is tried again a few times before the file counts as broken: a good file taken for
// broken would be kept as config.json.broken and saved over with the settings from before it.
sealed class ConfigReload
{
    // Five reads, the last two seconds after the first. The wait is also ConfigWatcher's after a
    // change, as editors write in several bursts.
    public const int Tries = 5;
    public const int WaitMilliseconds = 500;

    readonly string path;
    // The settings in use, which a file from before 1.0 put back while the app runs never replaces.
    readonly Func<Config> running;
    // What the app last wrote, and the size and time that left on the file, so its own saves are
    // not loaded back. A flag set before saving would also swallow another program's edit that
    // lands within the same half second. Kept until the file is seen to hold something else.
    string? saved;
    long savedLength;
    DateTime savedTime;
    int tries;
    // What the file held when the app last read or wrote it.
    Held seen;
    // While the settings in use could not be saved, what the file held when that save failed. Only a
    // real change to the file replaces those settings: a write that leaves the same text, as an
    // antivirus, a backup or a sync program can make, or a change event on a file nobody wrote,
    // does not.
    Held? unsaved;

    public ConfigReload(string path, Func<Config> running)
    {
        this.path = path;
        this.running = running;
        // The file the settings in use were just loaded from.
        seen = Held.Now(path, ReadOrNull(path));
    }

    // A text the file held, null when it could not be read, with the size and time the file had then.
    readonly record struct Held(string? Text, long Length, DateTime Time)
    {
        public static Held Now(string path, string? text)
        {
            var info = new FileInfo(path);
            return info.Exists ? new Held(text, info.Length, info.LastWriteTimeUtc) : new Held(text, -1, default);
        }

        public bool SameStamp(Held other) => Length == other.Length && Time == other.Time;
    }

    static string? ReadOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? Config.Read(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // True while a change on disk is being read again. The file then holds something the app has
    // neither loaded nor written, so a save keeps it as config.json.broken first, as it does a file
    // that did not load.
    public bool Waiting { get; private set; }

    // At start-up nothing is shown yet, so the tries are waited for here. firstProblem is why the
    // first read failed when it was tried again, for the log, which would otherwise say nothing
    // about a read that worked on a later try.
    public static Config AtStart(string path, out string? problem, out string? warning, out string? firstProblem) =>
        AtStart(path, Thread.Sleep, out problem, out warning, out firstProblem);

    // wait is given WaitMilliseconds before each try after the first; a test's own wait does what
    // the other program would do meanwhile.
    internal static Config AtStart(string path, Action<int> wait, out string? problem, out string? warning, out string? firstProblem)
    {
        var config = Config.Load(path, out problem, out warning);
        firstProblem = problem is not null && File.Exists(path) ? problem : null;
        for (int tried = 1; problem is not null && File.Exists(path) && tried < Tries; tried++)
        {
            wait(WaitMilliseconds);
            config = Config.Load(path, out problem, out warning);
        }
        return config;
    }

    // The text the app has just written, or null when its save failed.
    public void Saved(string? text)
    {
        var now = Held.Now(path, text);
        if (text is null)
        {
            // The file holds what it held before the save: what an earlier refused save found there,
            // or else the text the app last read or wrote. Either is kept when its text is known or
            // the file still has its size and time; only when neither is, what can be read of it
            // now. A change another program made since is not taken as that, even before its change
            // event is read, so the event still loads it. A change on disk still being read again is
            // not known yet, so it still loads.
            unsaved = Waiting ? null
                : Known(unsaved, now) ?? Known(seen, now) ?? now with { Text = ReadOrNull(path) };
        }
        else
        {
            seen = now;
            unsaved = null;
            // The file now holds the app's own settings, so a second save does not keep them as broken.
            Waiting = false;
        }
        saved = text;
        savedLength = now.Length;
        savedTime = now.Time;
    }

    // A view of the file to compare with later: one whose text is known, or whose size and time the
    // file still has.
    static Held? Known(Held? view, Held now) => view is { } held && (held.Text is not null || held.SameStamp(now)) ? held : null;

    // The file still holds what it held when the settings in use could not be saved. Its text is
    // compared when it was known then; otherwise its size and time.
    bool StillAsUnsaved(string? text) =>
        unsaved is { } was && (was.Text is { } held ? text == held : was.SameStamp(Held.Now(path, null)));

    // first is the first try after a change on disk, which starts the tries over. TryAgain asks for
    // another one after WaitMilliseconds, and problem then says why this one did not work.
    public Reloaded Try(bool first, out Config? fresh, out string? problem, out string? warning)
    {
        if (first) tries = 0;
        tries++;
        fresh = null;
        problem = null;
        warning = null;
        Waiting = false;
        // An editor that saves by deleting the old file before renaming its new copy, or by moving
        // the old one aside first, leaves no file for a moment, and Load would write the defaults
        // there, in the editor's way. A file still gone after the tries is made again with the defaults.
        if (!File.Exists(path))
        {
            if (tries < Tries)
            {
                problem = $"could not find {path}";
                Waiting = true;
                return Reloaded.TryAgain;
            }
            saved = null;
            var made = Config.Load(path, out problem, out warning);
            if (problem is not null) return Reloaded.Broken;
            seen = Held.Now(path, null);
            unsaved = null;
            fresh = made;
            return Reloaded.Loaded;
        }
        string text;
        try
        {
            text = Config.Read(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problem = $"could not read {path}: {Config.Reason(e)}";
            if (tries < Tries)
            {
                Waiting = true;
                return Reloaded.TryAgain;
            }
            // Still held, with the size and time the app's own save left, or that it had when a save
            // was refused: nothing else wrote it.
            var info = new FileInfo(path);
            bool ownSave = saved is not null && info.Exists && info.Length == savedLength && info.LastWriteTimeUtc == savedTime;
            if (ownSave || StillAsUnsaved(null))
            {
                problem = null;
                return Reloaded.Unchanged;
            }
            saved = null;
            return Reloaded.Broken;
        }
        if (text == saved || StillAsUnsaved(text)) return Reloaded.Unchanged;
        var loaded = Config.Parse(text, path, out problem, out warning);
        if (problem is not null && tries < Tries)
        {
            warning = null;
            Waiting = true;
            return Reloaded.TryAgain;
        }
        saved = null;
        if (problem is not null) return Reloaded.Broken;
        if (loaded.Old is { } old) return KeepRunning(old, out fresh);
        seen = Held.Now(path, text);
        unsaved = null;
        fresh = loaded;
        return Reloaded.Loaded;
    }

    // A file from before 1.0 put back while the app runs, by an older copy or by hand (D50). Nothing
    // in it is read, and the settings in use do not start over part way through a session: the file
    // is kept next to it, as at start-up, and the settings in use are written back as the app's own
    // save, so the change on disk that makes is not loaded again. They are loaded once, as after any
    // change on disk. A file that cannot be kept or saved over stays, and so do the settings in use.
    Reloaded KeepRunning(OldFile old, out Config? fresh)
    {
        fresh = null;
        var keep = running().Copy();
        try
        {
            Saved(keep.Save(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"config.json was replaced by a file from before 1.0 ({old.Shape}) that could not be saved over: {Config.Reason(e)}; the settings in use stay");
            return Reloaded.Unchanged;
        }
        Log.Write($"config.json was replaced by a file from before 1.0 ({old.Shape}); the settings in use were written back over it");
        fresh = keep;
        return Reloaded.Loaded;
    }
}
