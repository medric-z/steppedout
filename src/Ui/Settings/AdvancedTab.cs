using System.ComponentModel;
using System.Diagnostics;

namespace SteppedOut;

// The Extras tab, in two parts. Mouse and audio: tiny mouse movements, how loud sound must be to
// count as music or video, and how long a silence means it stopped. Settings and log: the folder
// with the settings file and the log, the log's size, saving the settings to a file and loading
// them from one, the welcome again, resetting every setting, and removing SteppedOut's files.
// Every list ends with Custom….
sealed class AdvancedTab : FlatTab
{
    // Tiny mouse movements left out, in pixels, after Off; Custom… takes 1 to MaxCustomPixels. A
    // number set in the file up to Config's limit shows as itself.
    internal static readonly int[] JitterChoices = [3, 5, 10];
    internal const int MaxCustomPixels = 50;

    // The loudness levels (D15), as a percent of full volume: even quiet sounds, normal volume or
    // louder, only loud sound. The file keeps the level as a fraction of full volume (0.01 to 1).
    internal static readonly int[] LoudnessPercents = [1, 3, 10];
    internal static readonly double[] LoudnessChoices = LoudnessPercents.Select(p => p / 100.0).ToArray();

    // How long a silence ends music or video, in seconds (D15); Custom… takes 5 to 600.
    internal static readonly int[] SilenceChoices = [10, 30, 60];
    internal const int ShortestCustomSilence = 5;
    internal const int LongestCustomSilence = 600;

    // The log's size in KB; Custom… takes Config's whole range, 64 to 65536.
    internal static readonly int[] LogSizeChoices = [256, 512, 1024, 4096];

    // A settings file to load is a few KB; one far bigger is no settings file, and is not read.
    internal const long LongestSettingsFile = 1 << 20;

    readonly ISettingsHost host;
    readonly Action<string> openFolder;

    DropDown? jitter;
    DropDown? loudness;
    DropDown? silence;
    DropDown? logSize;

    public AdvancedTab(ISettingsHost host, Action<string>? openFolder = null)
    {
        this.host = host;
        this.openFolder = openFolder ?? OpenFolder;
    }

    // Asks before Reset… and Remove…: the page, the question, what it means and the button that goes
    // ahead; true to go ahead. The app's own window unless a test gives its own.
    internal Func<Control, Question, bool>? Ask { get; init; }

    // Says why a file could not be used or written. A message box unless a test gives its own.
    internal Action<Control, string>? Tell { get; init; }

    // The file Save to file… writes and Load from file… reads, or null when the window was closed.
    // Windows' own windows unless a test gives its own.
    internal Func<Control, string?>? PickSaveFile { get; init; }

    internal Func<Control, string?>? PickLoadFile { get; init; }

    protected override void Fill(FlatPanel page, Action<Func<Config, Config>> edit)
    {
        var v = Text.V1;
        var jitter = this.jitter = new DropDown { LookC = true, Name = "ignoreTinyMoves", Unlisted = value => value is int p ? Text.JitterLabel(p) : null };
        jitter.Add(v.Off, 0);
        foreach (int p in JitterChoices) jitter.Add(Text.JitterLabel(p), p);
        jitter.AddCustom(v.Custom, new CustomEntry
        {
            Name = v.IgnoreTinyMoves,
            Unit = TimeUnit.Pixels,
            Shortest = 1,
            Longest = MaxCustomPixels,
            Start = 7,
            Value = pixels => pixels,
            Label = Text.JitterLabel,
            Number = value => value is int pixels && pixels is >= 1 and <= MaxCustomPixels ? pixels : null,
        });
        var loudness = this.loudness = new DropDown
        {
            LookC = true,
            Name = "loudness",
            Unlisted = value => value is double level ? Text.PercentLabel(level * 100) : null,
        }
            .Add(v.EvenQuietSounds, LoudnessChoices[0])
            .Add(v.MediumAndLoudSounds, LoudnessChoices[1])
            .Add(v.OnlyLoudSounds, LoudnessChoices[2]);
        loudness.AddCustom(v.Custom, new CustomEntry
        {
            Name = v.SoundsThatCount,
            Unit = TimeUnit.Percent,
            Shortest = 1,
            Longest = 100,
            Start = 5,
            Value = percent => Level(percent / 100.0),
            Label = percent => Text.PercentLabel(percent),
            Number = value => value is double level && WholePercent(level) is int percent ? percent : null,
        });
        var silence = this.silence = new DropDown { LookC = true, Name = "silence", Unlisted = value => value is int s ? Text.SilenceLabel(s) : null };
        foreach (int s in SilenceChoices) silence.Add(Text.SilenceLabel(s), s);
        silence.AddCustom(v.Custom, new CustomEntry
        {
            Name = v.StoppedAfter,
            Unit = TimeUnit.Seconds,
            Shortest = ShortestCustomSilence,
            Longest = LongestCustomSilence,
            Start = 45,
            Value = seconds => seconds,
            Label = Text.SilenceLabel,
            Number = value => value is int seconds && seconds is >= ShortestCustomSilence and <= LongestCustomSilence ? seconds : null,
        });

        var folder = new KitButton(v.OpenFolder) { Small = true, Name = "openFolder" };
        var logSize = this.logSize = new DropDown { LookC = true, Name = "logSize", Unlisted = value => value is int kb ? Text.LogSizeLabel(kb) : null };
        foreach (int kb in LogSizeChoices) logSize.Add(Text.LogSizeLabel(kb), kb);
        logSize.AddCustom(v.Custom, new CustomEntry
        {
            Name = v.LogSize,
            Unit = TimeUnit.Kilobytes,
            Shortest = Config.MinLogKilobytes,
            Longest = Config.MaxLogKilobytes,
            Start = 2048,
            Value = kb => kb,
            Label = Text.LogSizeLabel,
            Number = value => value is int kb && kb is >= Config.MinLogKilobytes and <= Config.MaxLogKilobytes ? kb : null,
        });
        var save = new KitButton(v.SaveToFile) { Small = true, Name = "saveSettings" };
        var load = new KitButton(v.LoadFromFile) { Small = true, Name = "loadSettings" };
        var welcome = new KitButton(v.Show) { Small = true, Name = "welcomeAgain" };
        var reset = new KitButton(v.ResetButton) { Small = true, Name = "resetAll" };
        var remove = new KitButton(v.RemoveButton) { Small = true, Name = "removeFiles" };

        page.Add(new PageHeading(Glyphs.Sliders, v.AdvancedTab, v.AdvancedIntro, part: false))
            .Add(new PageHeading(Glyphs.Mouse, v.MouseAndAudio, v.MouseAndAudioIntro, part: true), lineAbove: true)
            .Add(new FlatRow(v.IgnoreTinyMoves, v.IgnoreTinyMovesDetail, jitter))
            .Add(new FlatRow(v.SoundsThatCount, v.SoundsThatCountDetail, loudness))
            .Add(new FlatRow(v.StoppedAfter, v.StoppedAfterDetail, silence))
            .Add(new PageHeading(Glyphs.Folder, v.SettingsAndLog, null, part: true), lineAbove: true)
            .Add(new FlatRow(v.FilesAndLog, v.FilesAndLogDetail, folder))
            .Add(new FlatRow(v.LogSize, v.LogSizeDetail, logSize))
            .Add(new FlatRow(v.SaveAndLoad, v.SaveAndLoadDetail, new ButtonPair(save, load) { Name = "saveAndLoad" }))
            .Add(new FlatRow(v.ShowWelcomeAgain, v.ShowWelcomeAgainDetail, welcome))
            .Add(new FlatRow(v.ResetAll, v.ResetAllDetail, reset))
            .Add(new FlatRow(v.RemoveFiles, v.RemoveFilesDetail, remove));

        jitter.SelectedChanged += (_, _) =>
        {
            if (jitter.Value is not int pixels) return;
            edit(c =>
            {
                c.JitterFilter = pixels > 0;
                if (pixels > 0) c.JitterPixels = pixels;
                return c;
            });
        };
        loudness.SelectedChanged += (_, _) =>
        {
            if (loudness.Value is double level) edit(c => { c.AudioPeakThreshold = level; return c; });
        };
        silence.SelectedChanged += (_, _) =>
        {
            if (silence.Value is int seconds) edit(c => { c.AudioSilenceSeconds = seconds; return c; });
        };
        folder.Click += (_, _) => openFolder(Packaged.SettingsFolder());
        logSize.SelectedChanged += (_, _) =>
        {
            if (logSize.Value is int kb) edit(c => { c.LogMaxKilobytes = kb; return c; });
        };
        save.Click += (_, _) => SaveToFile(page);
        load.Click += (_, _) => LoadFromFile(page, edit);
        welcome.Click += (_, _) => host.ShowWelcomeChosen();
        reset.Click += (_, _) =>
        {
            if (!(Ask ?? AskInWindow)(page, new Question(v.ResetTitle, v.ResetBody, v.Reset))) return;
            Replace(Reset(Current), edit, "settings reset");
        };
        remove.Click += (_, _) =>
        {
            if (!(Ask ?? AskInWindow)(page, new Question(v.RemoveTitle, v.RemoveBody, v.RemoveAndQuit))) return;
            host.RemoveFilesChosen();
        };
    }

    protected override void Show(Config config)
    {
        jitter!.Value = config.JitterFilter ? config.JitterPixels : 0;
        loudness!.Value = Level(config.AudioPeakThreshold);
        silence!.Value = config.AudioSilenceSeconds;
        logSize!.Value = config.LogMaxKilobytes;
    }

    // One of the list's levels when the file's is that level, give or take rounding; otherwise the
    // file's own, which then shows as itself.
    internal static double Level(double threshold)
    {
        foreach (double level in LoudnessChoices)
        {
            if (Math.Abs(level - threshold) <= level * 1e-6) return level;
        }
        return threshold;
    }

    // A level as a whole percent from 1 to 100, for the Custom… box to open with; null for a level
    // with a fraction of a percent, such as 0.2% set in the file.
    internal static int? WholePercent(double level)
    {
        double percent = level * 100;
        double whole = Math.Round(percent);
        return Math.Abs(percent - whole) <= 1e-6 && whole is >= 1 and <= 100 ? (int)whole : null;
    }

    // The settings as they were when SteppedOut first started (Config.FirstStart), keeping the
    // language, that the welcome was seen and the tab the window is on. Start with Windows is not a
    // setting in the file, so it stays as it is.
    internal static Config Reset(Config now)
    {
        var reset = Config.FirstStart();
        reset.Language = now.Language;
        reset.Welcomed = now.Welcomed;
        reset.SettingsTab = now.SettingsTab;
        return reset;
    }

    // Settings from a file, made the ones in use through the normal path: kept, shown, and handed to
    // the app after the short wait. The welcome and the tab stay as they are here.
    internal static Config Loaded(Config file, Config now)
    {
        var next = file.Copy();
        next.Welcomed = now.Welcomed;
        next.SettingsTab = now.SettingsTab;
        return next;
    }

    void Replace(Config next, Action<Func<Config, Config>> edit, string why)
    {
        string shortcutNow = Shortcut.Normalize(Current.LockShortcut);
        string shortcutNext = Shortcut.Normalize(next.LockShortcut);
        Log.Write(why);
        // As when the shortcut is picked from its list: registered at once, so its line does not wait.
        if (shortcutNext != shortcutNow) host.ShortcutChanged(shortcutNext);
        edit(_ => next.Copy());
    }

    // Writes the settings shown here, waiting changes and all, to a file of the person's choosing.
    void SaveToFile(Control page)
    {
        string? path = (PickSaveFile ?? PickSave)(page);
        if (path is null) return;
        try
        {
            Write(Current, path);
            Log.Write("settings saved to a file of the person's choosing");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Log.Write("could not save the settings to a file: " + Config.Reason(e));
            (Tell ?? TellInBox)(page, Text.V1.SaveCantWrite);
        }
    }

    // The settings file Save to file… writes: what config.json would hold, in the same shape, so
    // Load from file… checks it as config.json is checked. Start with Windows is not in it, as it
    // is not in config.json: it lives in Windows.
    internal static void Write(Config settings, string path)
    {
        var copy = settings.Copy();
        copy.Clamp();
        copy.Write(path);
    }

    // Reads a file of the person's choosing, checks it as config.json is checked, and uses it, or
    // says plainly why it can't.
    void LoadFromFile(Control page, Action<Func<Config, Config>> edit)
    {
        string? path = (PickLoadFile ?? PickLoad)(page);
        if (path is null) return;
        var (loaded, problem) = Read(path, Text);
        if (loaded is null)
        {
            Log.Write("a settings file was not used: " + problem);
            (Tell ?? TellInBox)(page, problem!);
            return;
        }
        Replace(Loaded(loaded, Current), edit, "settings loaded from a file");
        if (problem is not null) (Tell ?? TellInBox)(page, problem);
    }

    // The settings in a file and, when they can't be used, null and why, in the person's words.
    // Settings that load with some values skipped come with a note that says so.
    internal static (Config? Settings, string? Problem) Read(string path, Strings text)
    {
        var v = text.V1;
        string contents;
        try
        {
            if (new FileInfo(path).Length > LongestSettingsFile) return (null, v.LoadCantOpen);
            contents = Config.Read(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Log.Write("could not read a settings file: " + Config.Reason(e));
            return (null, v.LoadCantOpen);
        }
        var loaded = Config.Parse(contents, Path.GetFileName(path), out string? problem, out string? warning);
        if (problem is not null) return (null, v.LoadCantUse);
        if (loaded.Old is not null) return (null, v.LoadTooOld);
        if (warning is not null) Log.Write(warning);
        return (loaded, warning is null ? null : v.LoadPartly);
    }

    string? PickSave(Control page)
    {
        var v = Text.V1;
        using var dialog = new SaveFileDialog
        {
            FileName = v.SettingsTitle + ".json",
            DefaultExt = "json",
            AddExtension = true,
            Filter = v.SettingsFiles + " (*.json)|*.json",
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(page.FindForm()) == DialogResult.OK ? dialog.FileName : null;
    }

    string? PickLoad(Control page)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = Text.V1.SettingsFiles + " (*.json)|*.json",
            CheckFileExists = true,
            Multiselect = false,
            DereferenceLinks = true,
        };
        return dialog.ShowDialog(page.FindForm()) == DialogResult.OK ? dialog.FileName : null;
    }

    bool AskInWindow(Control page, Question question)
    {
        using var dialog = new QuestionWindow(question, Text);
        return dialog.ShowDialog(page.FindForm()) == DialogResult.OK;
    }

    void TellInBox(Control page, string words) =>
        MessageBox.Show(page.FindForm(), words, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1,
            Text.RightToLeft ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0);

    // Opens the folder with the settings file and the log in File Explorer, making it first if it is
    // not there yet. A failure is only logged.
    static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            new ProcessStarter().Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            Log.Write("could not open the settings folder: " + e.Message);
        }
    }
}

// Two buttons side by side at the end of a row, 8 px apart: Save to file… then Load from file….
sealed class ButtonPair : Panel
{
    const int Gap = 8;

    public ButtonPair(KitButton first, KitButton second)
    {
        First = first;
        Second = second;
        // The row's card shows through around the two.
        BackColor = Color.Transparent;
        TabStop = false;
        Controls.Add(first);
        Controls.Add(second);
    }

    public KitButton First { get; }

    public KitButton Second { get; }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var a = First.GetPreferredSize(Size.Empty);
        var b = Second.GetPreferredSize(Size.Empty);
        return new Size(a.Width + LogicalToDeviceUnits(Gap) + b.Width, Math.Max(a.Height, b.Height));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var a = First.GetPreferredSize(Size.Empty);
        var b = Second.GetPreferredSize(Size.Empty);
        // In a window that does not mirror itself, Arabic still reads from the right: the first
        // button on the right.
        bool flip = RightToLeft == RightToLeft.Yes && !IsMirrored;
        int secondX = Width - b.Width;
        int firstX = secondX - LogicalToDeviceUnits(Gap) - a.Width;
        First.Bounds = new Rectangle(flip ? Width - firstX - a.Width : firstX, (Height - a.Height) / 2, a.Width, a.Height);
        Second.Bounds = new Rectangle(flip ? 0 : secondX, (Height - b.Height) / 2, b.Width, b.Height);
    }
}
