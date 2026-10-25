using System.Runtime.InteropServices;

namespace SteppedOut;

// The Away and back tab: what happens when you leave the PC and when you come back. When you're
// away: mute, turn off the screen, a sound, apps to open and apps to close. When you're back: the
// note that says how long you were away (with a preview of it), a sound, apps to open and apps to
// close. Then a script for each side. Everything is off until it is turned on.
//
// The page never changes the settings itself. Each change goes out through Edited, and the page
// shows it at once from its own copy, so a list it adds to never waits for the save. Refill shows
// the settings as they are without raising anything.
sealed class AwayTab : ISettingsPage
{
    Config current = new();
    Strings text = Lang.Current;
    bool refilling;
    int builds;

    Toggle? mute;
    Toggle? screensOff;
    SoundPicker? soundAway;
    ChipList? openAway;
    ChipList? closeAway;
    Toggle? showTimeAway;
    SoundPicker? soundBack;
    ChipList? openBack;
    ChipList? closeBack;
    ScriptRow? scriptAway;
    ScriptRow? scriptBack;

    public event Action<Func<Config, Config>>? Edited;

    // Where the running apps come from; Windows unless a test gives its own.
    internal IProcessSource? Processes { get; init; }

    // "Choose another app…": the program file someone picked, or null. The file dialog unless a test
    // gives its own.
    internal Func<string?>? ChooseAppFile { get; init; }

    // Opens the list of apps under an Add app button: the button, the names already in its list, and
    // what to do with the one picked. AppPicker's menu unless a test gives its own.
    internal Action<Control, IReadOnlyList<string>, Action<PickedApp>>? PickApp { get; init; }

    // Choose… for a script: the file someone picked, or null. The file dialog unless a test gives its own.
    internal Func<Control, string?>? ChooseScript { get; init; }

    // Your own sound…: the .wav file someone picked, or null. The file dialog unless a test gives its own.
    internal Func<Control, string?>? ChooseSoundFile { get; init; }

    // Plays a sound picked in a sound list once (ISettingsHost.PreviewSound). Nothing when it is not
    // set, as in the tests.
    internal Action<Sound>? Preview { get; init; }

    // The Start menu's name for a Store app's id ("Media Player"), or null when Windows doesn't know
    // it. Windows' own answer unless a test or a picture gives its own.
    internal Func<string, string?>? StartMenuName { get; init; }

    // Where the chips read the names programs give themselves; a stand-in in tests.
    internal ProgramNames Names { get; init; } = ProgramNames.Shared;

    // What Windows says is behind a drive letter (LocalPaths); a stand-in in tests.
    internal Func<char, DriveType> Drives { get; init; } = LocalPaths.DriveTypeOf;

    // Where the app running under each Close apps name is, looked for away from the window, once
    // while the page is open.
    readonly Dictionary<string, Task<string?>> running = new(StringComparer.OrdinalIgnoreCase);

    public Control Build(Config config, Strings text)
    {
        this.text = text;
        current = config.Copy();
        // Controls from an earlier build may still be around until the window disposes them; they
        // no longer change anything.
        int build = ++builds;
        var v = text.V1;

        var mute = this.mute = Switch();
        var screensOff = this.screensOff = Switch();
        var soundAway = this.soundAway = Sounds(Named(v.PlaySound, v.WhenAway), away: true);
        var showTimeAway = this.showTimeAway = Switch();
        var soundBack = this.soundBack = Sounds(Named(v.PlaySound, v.WhenBack), away: false);
        var openAwayAdd = AddButton(Named(v.OpenApps, v.WhenAway));
        var closeAwayAdd = AddButton(Named(v.CloseApps, v.WhenAway));
        var openBackAdd = AddButton(Named(v.OpenApps, v.WhenBack));
        var closeBackAdd = AddButton(Named(v.CloseApps, v.WhenBack));
        var openAway = this.openAway = Chips(openAwayAdd, Named(v.OpenApps, v.WhenAway), warning: null);
        var closeAway = this.closeAway = Chips(closeAwayAdd, Named(v.CloseApps, v.WhenAway), warning: v.CloseAppsWarning);
        var openBack = this.openBack = Chips(openBackAdd, Named(v.OpenApps, v.WhenBack), warning: null);
        var closeBack = this.closeBack = Chips(closeBackAdd, Named(v.CloseApps, v.WhenBack), warning: v.CloseAppsWarning);
        var scriptAway = this.scriptAway = new ScriptRow(v.RunScriptAway, text);
        var scriptBack = this.scriptBack = new ScriptRow(v.RunScriptBack, text);

        var openAwayRow = new FlatRow(v.OpenApps, v.OpenAppsAwayDetail, openAwayAdd) { Below = openAway };
        var openBackRow = new FlatRow(v.OpenApps, v.OpenAppsBackDetail, openBackAdd) { Below = openBack };
        var soundAwayRow = new FlatRow(v.PlaySound, v.PlaySoundAwayDetail, soundAway.List);
        var soundBackRow = new FlatRow(v.PlaySound, v.PlaySoundBackDetail, soundBack.List);
        soundAway.Place = (soundAwayRow, v.PlaySoundAwayDetail);
        soundBack.Place = (soundBackRow, v.PlaySoundBackDetail);
        var page = new FlatPanel { JoinedAbove = true };
        page.Add(new PageHeading(Glyphs.Exchange, v.AwayTab, v.AwayIntro, part: false))
            .Add(new PageHeading(Glyphs.Exit, v.WhenAway, null, part: true), lineAbove: true)
            .Add(new FlatRow(v.Mute, v.MuteDetail, mute))
            .Add(new FlatRow(v.ScreenOff, v.ScreenOffDetail, screensOff))
            .Add(soundAwayRow)
            .Add(openAwayRow)
            .Add(new FlatRow(v.CloseApps, v.CloseAppsRowDetail, closeAwayAdd) { Below = new CloseList(v.CloseAppsWarning, closeAway) })
            .Add(new PageHeading(Glyphs.Home, v.WhenBack, null, part: true), lineAbove: true)
            .Add(new MiddleRow(v.ShowTimeAway, v.ShowTimeAwayDetail, showTimeAway) { Below = new AwayPreview(text) })
            .Add(soundBackRow)
            .Add(openBackRow)
            .Add(new FlatRow(v.CloseApps, v.CloseAppsRowDetail, closeBackAdd) { Below = new CloseList(v.CloseAppsWarning, closeBack) })
            .Add(new PageHeading(Glyphs.Code, v.Scripts, v.ScriptsDetail, part: true), lineAbove: true)
            .Add(scriptAway)
            .Add(scriptBack);

        void Edit(Func<Config, Config> change)
        {
            if (build == builds) this.Edit(change);
        }

        mute.CheckedChanged += (_, _) =>
        {
            bool on = mute.Checked;
            Edit(c => { c.MuteWhenAway = on; return c; });
        };
        screensOff.CheckedChanged += (_, _) =>
        {
            bool on = screensOff.Checked;
            Edit(c => { c.ScreensOffWhenAway = on; return c; });
        };
        showTimeAway.CheckedChanged += (_, _) =>
        {
            bool on = showTimeAway.Checked;
            Edit(c => { c.ShowTimeAway = on; return c; });
        };
        soundAway.Chosen += (value, file) =>
        {
            if (value is ActionSound sound) Edit(c => { c.SoundWhenAway = sound; c.SoundWhenAwayFile = file; return c; });
        };
        soundBack.Chosen += (value, file) =>
        {
            if (value is ActionSound sound) Edit(c => { c.SoundWhenBack = sound; c.SoundWhenBackFile = file; return c; });
        };

        Apps(openAwayAdd, openAway, c => c.OpenWhenAway, Edit, (openAwayRow, v.OpenAppsAwayDetail));
        Apps(closeAwayAdd, closeAway, c => c.CloseWhenAway, Edit, null);
        Apps(openBackAdd, openBack, c => c.OpenWhenBack, Edit, (openBackRow, v.OpenAppsBackDetail));
        Apps(closeBackAdd, closeBack, c => c.CloseWhenBack, Edit, null);

        scriptAway.Pressed += () => Script(scriptAway, away: true, Edit);
        scriptBack.Pressed += () => Script(scriptBack, away: false, Edit);

        Show(current);
        return page;
    }

    public void Refill(Config config)
    {
        current = config.Copy();
        Show(current);
    }

    // Puts the settings on the page without telling Edited: a switch tells CheckedChanged even when
    // it is set from here, so its handler is told to stand aside.
    void Show(Config config)
    {
        if (mute is null) return;
        refilling = true;
        try
        {
            mute.Checked = config.MuteWhenAway;
            screensOff!.Checked = config.ScreensOffWhenAway;
            soundAway!.Show(config.SoundWhenAway, config.SoundWhenAwayFile);
            WaitForNames(config);
            openAway!.SetItems(config.OpenWhenAway.Select(entry => (OpenName(entry), (object?)entry)));
            closeAway!.SetItems(config.CloseWhenAway.Select(name => (CloseName(name), (object?)name)));
            showTimeAway!.Checked = config.ShowTimeAway;
            soundBack!.Show(config.SoundWhenBack, config.SoundWhenBackFile);
            openBack!.SetItems(config.OpenWhenBack.Select(entry => (OpenName(entry), (object?)entry)));
            closeBack!.SetItems(config.CloseWhenBack.Select(name => (CloseName(name), (object?)name)));
            scriptAway!.Show(config.RunWhenAway);
            scriptBack!.Show(config.RunWhenBack);
        }
        finally
        {
            refilling = false;
        }
    }

    // The chips show apps by the names programs give themselves, as the app rules on the Locking
    // tab do: "Notepad" for notepad.exe. The page waits a moment for those names, all together; one
    // still being read shows the name it is kept by, and the chips change when it comes.
    void WaitForNames(Config config)
    {
        var finding = config.CloseWhenAway.Concat(config.CloseWhenBack).Select(Running).Where(find => !find.IsCompleted).ToArray();
        if (finding.Length > 0)
        {
            try
            {
                Task.WaitAll(finding, ProgramNames.Moment);
            }
            catch (AggregateException)
            {
                // Not found: the name as it is kept.
            }
        }
        var paths = config.OpenWhenAway.Concat(config.OpenWhenBack)
            .Concat(config.CloseWhenAway.Concat(config.CloseWhenBack).Select(Running).Where(find => find.IsCompletedSuccessfully).Select(find => find.Result));
        Names.Wait(paths, ProgramNames.Moment);
    }

    // The name an Open apps chip shows: the program's own name once its file is read, otherwise
    // AppName's.
    string OpenName(string entry) => Names.Name(entry, openAway!, _ => NamesCame()) ?? AppName(entry, StartMenuName);

    // The name a Close apps chip shows, for an entry that is the name apps run under: the program's
    // own name when an app by that name is running, otherwise the entry as it is kept.
    string CloseName(string entry)
    {
        var find = Running(entry);
        return (find.IsCompletedSuccessfully ? Names.Name(find.Result, openAway!, _ => NamesCame()) : null) ?? entry;
    }

    Task<string?> Running(string entry)
    {
        if (running.TryGetValue(entry, out var find)) return find;
        var source = Processes ?? AppPicker.Windows;
        running[entry] = find = Task.Run(() => RunningPath(source, entry));
        ProgramNames.Then(find, openAway!, path =>
        {
            if (path is not null) NamesCame();
        });
        return find;
    }

    // The program file of an app running under this name, or null when none is or Windows won't say.
    static string? RunningPath(IProcessSource source, string name)
    {
        string key = ProcessRule.Normalize(name);
        string? found = null;
        if (key.Length == 0) return null;
        try
        {
            foreach (var process in source.Running())
            {
                try
                {
                    if (found is null && ProcessRule.Normalize(process.Name).Equals(key, StringComparison.OrdinalIgnoreCase)
                        && source.ImagePath(process.Id) is { Length: > 0 } path) found = path;
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    // Exited or refused while being looked at.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // The list of running apps could not be read.
        }
        return found;
    }

    // A name came: the chips show it, unless the page has gone.
    void NamesCame()
    {
        if (openAway is null || openAway.IsDisposed) return;
        Show(current);
    }

    // A change the person made: shown here from the page's own copy, and sent on to the window.
    void Edit(Func<Config, Config> change)
    {
        if (refilling) return;
        current = change(current.Copy());
        Show(current);
        Edited?.Invoke(change);
    }

    // An app list: Add app opens the picker, and a chip's × or Delete takes the app out. An Open apps
    // list (opens: its row and the row's explanation) does not take an app that runs from another PC:
    // its row says so in place of the explanation, until the next change to the list.
    void Apps(KitButton add, ChipList chips, Func<Config, List<string>> list, Action<Func<Config, Config>> edit, (SettingRow Row, string Explanation)? opens)
    {
        void Noted(string? note)
        {
            if (opens is { } open && (note is not null || open.Row.DetailWarns)) open.Row.Note(note, open.Explanation);
        }
        add.Click += (_, _) =>
        {
            // The picker leaves out the apps by the names they run under, which for a Store app is
            // nearer its id than its Start menu name, so an Open apps entry gives both.
            var names = list(current).SelectMany(entry => opens is not null ? new[] { AppName(entry, _ => null), AppName(entry, StartMenuName) } : [entry])
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var pick = PickApp ?? ShowPicker;
            pick(add, names, app =>
            {
                string? entry = opens is not null ? OpenEntry(app, Drives) : CloseEntry(app);
                if (entry is null)
                {
                    var place = app.Path is { Length: > 0 } path ? LocalPaths.Place(path, Drives) : PathPlace.NotAFullPath;
                    if (opens is not null && place is PathPlace.NetworkPath or PathPlace.NetworkDrive)
                    {
                        Log.Write($"cannot add {app.Name}: {LocalPaths.Elsewhere(app.Path!, place)}, and only a program on this PC or a Store app opens here");
                        Noted(text.V1.AppOnAnotherPC);
                        return;
                    }
                    Log.Write($"cannot add {app.Name}: SteppedOut could not find where its program is");
                    return;
                }
                Noted(null);
                if (list(current).Contains(entry, StringComparer.OrdinalIgnoreCase)) return;
                edit(c =>
                {
                    var apps = list(c);
                    if (!apps.Contains(entry, StringComparer.OrdinalIgnoreCase)) apps.Add(entry);
                    return c;
                });
            });
        };
        chips.Removed += (_, removed) =>
        {
            if (removed.Value is not string entry) return;
            Noted(null);
            edit(c =>
            {
                list(c).RemoveAll(app => app.Equals(entry, StringComparison.OrdinalIgnoreCase));
                return c;
            });
        };
    }

    void ShowPicker(Control button, IReadOnlyList<string> names, Action<PickedApp> picked) =>
        AppPicker.Show(button, names, picked, new PickerText(text.V1.ChooseAnotherApp, text.V1.AppPickerFileFilter), Processes, ChooseAppFile);

    // Choose… picks a script; Remove clears it.
    void Script(ScriptRow row, bool away, Action<Func<Config, Config>> edit)
    {
        if (row.Path.Length > 0)
        {
            edit(c =>
            {
                if (away) c.RunWhenAway = "";
                else c.RunWhenBack = "";
                return c;
            });
            return;
        }
        string? file = (ChooseScript ?? ScriptDialog)(row);
        if (string.IsNullOrWhiteSpace(file)) return;
        string path = file.Trim().Trim('"');
        // A file the row would never run is not kept: one on another PC, or of another kind, its
        // name typed into the dialog. The row says why, in place of "No script chosen".
        if (RunProgram.Refusal(path, Drives) is { } problem)
        {
            Log.Write($"did not keep {path} as the script to run when {(away ? "away" : "back")}: {problem}");
            var place = LocalPaths.Place(path, Drives);
            row.Refuse(place is PathPlace.NetworkPath or PathPlace.NetworkDrive ? text.V1.ScriptOnAnotherPC : text.V1.ScriptNotRunnable);
            return;
        }
        edit(c =>
        {
            if (away) c.RunWhenAway = path;
            else c.RunWhenBack = path;
            return c;
        });
    }

    string? ScriptDialog(Control owner)
    {
        try
        {
            using var dialog = new OpenFileDialog
            {
                Filter = text.V1.ScriptFileFilter,
                // A shortcut is kept as itself: that is where a script's arguments go.
                DereferenceLinks = false,
                CheckFileExists = true,
                RestoreDirectory = true,
            };
            return dialog.ShowDialog(owner.FindForm()) == DialogResult.OK ? dialog.FileName : null;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or ExternalException)
        {
            Log.Write("could not open the script dialog: " + e.Message);
            return null;
        }
    }

    // What an Open apps list holds for a picked app: the Start menu's id for a Store app, otherwise
    // the program's full path on this PC. Null when neither is known, since only those are opened
    // (OpenApps): never a program that runs from another PC.
    internal static string? OpenEntry(PickedApp app) => OpenEntry(app, LocalPaths.DriveTypeOf);

    internal static string? OpenEntry(PickedApp app, Func<char, DriveType> drive) =>
        app.Aumid is { Length: > 0 } aumid ? OpenApps.AppsFolder + aumid
        : app.Path is { Length: > 0 } path && LocalPaths.IsFullPathOnThisPC(path, drive) ? path
        : null;

    // What a Close apps list holds: the name the running apps are matched on.
    internal static string? CloseEntry(PickedApp app) => ProcessRule.Normalize(app.Name) is { Length: > 0 } name ? name : null;

    // The name a chip shows for an Open apps entry: the program's file name, or for a Store app the
    // name the Start menu gives it ("Media Player"). When Windows doesn't know the id, a guess from it:
    // the app's part ("Spotify" from "SpotifyAB.SpotifyMusic_…!Spotify"), or the package's name when
    // that part only says App ("WindowsCalculator"). Never empty. startMenu answers for Windows when
    // it is given.
    internal static string AppName(string entry, Func<string, string?>? startMenu = null)
    {
        string app = (entry ?? "").Trim().Trim('"');
        if (app.StartsWith(OpenApps.AppsFolder, StringComparison.OrdinalIgnoreCase))
        {
            string id = app[OpenApps.AppsFolder.Length..].Trim();
            if (id.Length == 0) return app;
            string? known = null;
            try
            {
                known = (startMenu ?? StartMenuNames.Find)(id)?.Trim();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Log.Write("could not read an app's name: " + e.Message);
            }
            if (!string.IsNullOrEmpty(known)) return known;
            int bang = id.IndexOf('!');
            string family = bang >= 0 ? id[..bang] : id;
            string inside = bang >= 0 ? id[(bang + 1)..] : "";
            if (inside.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) inside = inside[..^4];
            if (inside.Length > 0 && !inside.Equals("App", StringComparison.OrdinalIgnoreCase)) return inside;
            int under = family.IndexOf('_');
            string package = under >= 0 ? family[..under] : family;
            string name = package[(package.LastIndexOf('.') + 1)..];
            return name.Length > 0 ? name : id;
        }
        try
        {
            string file = Path.GetFileNameWithoutExtension(app);
            return file.Length > 0 ? file : app;
        }
        catch (ArgumentException)
        {
            return app;
        }
    }

    // What the Start menu calls an app, read from Windows' own list of apps on this PC
    // (shell:AppsFolder) and kept, so a page drawn again asks nothing. Only reads: nothing is opened.
    static class StartMenuNames
    {
        const int Kept = 200;
        const uint NormalDisplay = 0;
        static readonly Dictionary<string, string?> found = new(StringComparer.OrdinalIgnoreCase);

        public static string? Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            lock (found)
                if (found.TryGetValue(id, out string? kept)) return kept;
            string? name = Ask(id);
            lock (found)
            {
                if (found.Count >= Kept) found.Clear();
                found[id] = name;
            }
            return name;
        }

        static string? Ask(string id)
        {
            IShellItem? item = null;
            IntPtr text = IntPtr.Zero;
            try
            {
                Guid iid = typeof(IShellItem).GUID;
                if (SHCreateItemFromParsingName(OpenApps.AppsFolder + id, IntPtr.Zero, ref iid, out item) != 0 || item is null) return null;
                if (item.GetDisplayName(NormalDisplay, out text) != 0 || text == IntPtr.Zero) return null;
                string name = (Marshal.PtrToStringUni(text) ?? "").Trim();
                // Anything that is still the id or a path is no better than the guess.
                if (name.Length == 0 || name.Equals(id, StringComparison.OrdinalIgnoreCase) || name.Contains('\\')) return null;
                return name;
            }
            catch (Exception e) when (e is COMException or DllNotFoundException or EntryPointNotFoundException or InvalidCastException or NotSupportedException or ArgumentException or UnauthorizedAccessException)
            {
                return null;
            }
            finally
            {
                if (text != IntPtr.Zero) Marshal.FreeCoTaskMem(text);
                if (item is not null) Marshal.ReleaseComObject(item);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem? item);

        // The first three entries of IShellItem, in Windows' order; only GetDisplayName is called.
        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            [PreserveSig] int BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr result);
            [PreserveSig] int GetParent(out IntPtr parent);
            [PreserveSig] int GetDisplayName(uint form, out IntPtr name);
        }
    }

    Toggle Switch() => new() { Look = ToggleLook.LookC, ShowLabel = true };

    // What a screen reader calls a control that both parts of the page have, such as Open apps: the
    // row's title and the part it is in, "Open apps, When you're back", since someone moving with Tab
    // never meets the part's head.
    string Named(string title, string part) => title + (text.RightToLeft ? "، " : ", ") + part;

    // Off, SteppedOut's own and four more, the same names for leaving and coming back: each name is
    // a pair, falling when you leave and rising when you come back.
    SoundPicker Sounds(string name, bool away)
    {
        var v = text.V1;
        (string, object)[] choices =
        [
            (v.Off, ActionSound.Off),
            (v.SoundSteppedOut, ActionSound.SteppedOut),
            (v.SoundDrift, ActionSound.Drift),
            (v.SoundPebble, ActionSound.Pebble),
            (v.SoundGlow, ActionSound.Glow),
            (v.SoundRipple, ActionSound.Ripple),
        ];
        return new SoundPicker(text, name, choices, ActionSound.Own, (value, file) => Plays((ActionSound)value, file, away))
        {
            Preview = Preview,
            ChooseFile = ChooseSoundFile,
            Drives = Drives,
        };
    }

    // What a choice plays when picked: the sound itself, or your own sound's file.
    internal static Sound? Plays(ActionSound choice, string file, bool away) =>
        choice == ActionSound.Own ? (file.Length > 0 ? Sound.Own(file) : null)
        : SoundClips.For(choice, away) is SoundClip clip ? Sound.Of(clip) : null;

    // Named like a script's button: the row's name, then the button's own words. A screen reader
    // hears the row's explanation on it, as on the other rows' controls.
    KitButton AddButton(string row) =>
        new(text.V1.AddApp) { Small = true, Glyph = Glyphs.Plus, AccessibleName = row + ". " + text.V1.AddApp };

    // The chips of an app list. Close apps lists also say their warning to a screen reader on the
    // chips, and only there, so it is never said two or three times in a row: what happens to
    // unsaved work in those apps.
    ChipList Chips(KitButton add, string name, string? warning) => new()
    {
        EmptyText = text.V1.NoAppsAdded,
        RemoveName = text.V1.RemoveApp,
        FocusWhenEmpty = add,
        AccessibleName = name,
        AccessibleDescription = warning,
    };

    // A Close apps list with its warning above the apps (D51): SteppedOut cannot save anyone's work,
    // each app is asked to close as its X would ask it, keeping unsaved work is up to the app, and
    // what is still open after 30 seconds is closed anyway. A small icon and the words in the warning
    // color, in the row's words column; the chips follow, as in the other lists.
    internal sealed class CloseList : KitControl
    {
        public CloseList(string warning, ChipList chips)
        {
            Warning = warning;
            Chips = chips;
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            AccessibleName = chips.AccessibleName;
            AccessibleRole = AccessibleRole.Grouping;
            // The warning is the list's to say, not this group's around it too.
            Controls.Add(chips);
        }

        public string Warning { get; }

        public ChipList Chips { get; }

        Font Face => Body(9f);

        Font GlyphFont => Icons(8.25f);

        const TextFormatFlags GlyphFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

        const TextFormatFlags WordFlags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;

        // The icon's box, which grows with the text size, and the gap after it.
        int GlyphBox => Math.Max(Px(12), TextRenderer.MeasureText(Glyphs.Info, GlyphFont, Size.Empty, GlyphFlags | TextFormatFlags.NoPrefix).Width);

        int TextLeft => GlyphBox + Px(6);

        // The height of the first line, which the icon sits level with.
        int LineHeight => Math.Max(Shapes.Measure("Ag", Face).Height, TextRenderer.MeasureText(Glyphs.Info, GlyphFont, Size.Empty, GlyphFlags | TextFormatFlags.NoPrefix).Height);

        int WarningHeight(int width) => Math.Max(LineHeight, Shapes.WrappedHeight(Warning, Face, Math.Max(1, width - TextLeft)));

        int Gap => Px(8);

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
            return new Size(width, WarningHeight(width) + Gap + Chips.GetPreferredSize(new Size(width, 0)).Height);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int top = WarningHeight(Width) + Gap;
            Chips.Bounds = new Rectangle(0, top, Width, Chips.GetPreferredSize(new Size(Width, 0)).Height);
            // Chips that need another line, or one less, make the row it is in make room, as a list
            // of chips on its own does.
            if (Width > 0 && Height != top + Chips.Height) Parent?.PerformLayout();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            Write(g, Glyphs.Info, GlyphFont, Box(0, 0, GlyphBox, LineHeight), t.Warning, GlyphFlags);
            Write(g, Warning, Face, Box(TextLeft, 0, Math.Max(1, Width - TextLeft), WarningHeight(Width)), t.Warning, WordFlags);
        }
    }

    // A row whose control sits in the middle of everything beside it, the preview included, as the
    // sketch's Show how long you were away does; a plain row puts it level with the title.
    sealed class MiddleRow : SettingRow
    {
        // Null while the row's own constructor lays it out the first time.
        readonly Control? control;

        public MiddleRow(string title, string detail, Control control) : base(null, title, detail, control)
        {
            this.control = control;
            Look = RowLook.Flat;
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (control is not null) control.Top = Math.Max(0, (Height - control.Height) / 2);
        }
    }

    // A script row: its title, the script chosen or "No script chosen", and Choose… or Remove.
    internal sealed class ScriptRow : SettingRow
    {
        readonly KitButton button;
        readonly string title;
        readonly Strings text;
        bool placing;

        public ScriptRow(string title, Strings text) : this(title, text, new KitButton(text.V1.Choose) { Small = true })
        {
        }

        ScriptRow(string title, Strings text, KitButton button) : base(null, title, text.V1.NoScriptChosen, button)
        {
            this.button = button;
            this.title = title;
            this.text = text;
            Look = RowLook.Flat;
            button.Click += (_, _) => Pressed?.Invoke();
        }

        public event Action? Pressed;

        public string Path { get; private set; } = "";

        // Why the file Choose… picked last was not kept (AwayTab.Script), until a script is chosen.
        public string? Refusal { get; private set; }

        internal KitButton Button => button;

        public void Refuse(string why)
        {
            Refusal = why;
            PlacePath();
            SayNote(why);
        }

        public void Show(string? path)
        {
            path = (path ?? "").Trim();
            string words = path.Length > 0 ? text.V1.Remove : text.V1.Choose;
            bool changed = path != Path || button.Text != words;
            if (path != Path) Refusal = null;
            Path = path;
            if (!changed) return;
            button.Text = words;
            button.AccessibleName = title + ". " + words;
            button.Size = button.GetPreferredSize(Size.Empty);
            button.NameChanged();
            PlacePath();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            PlacePath();
            base.OnLayout(levent);
        }

        // The script under the title, on one line: as much of its path as fits, always with the
        // file's name, and read left to right in an Arabic window too, so a path keeps its order.
        // The button's description and the settings keep the whole path.
        void PlacePath()
        {
            // Not while the row's own constructor lays it out the first time.
            if (placing || button is null || text is null) return;
            string shown = Path.Length == 0 ? Refusal ?? text.V1.NoScriptChosen : Shown(Path);
            placing = true;
            try
            {
                if (Detail != shown) Detail = shown;
                DetailWarns = Path.Length == 0 && Refusal is not null;
                string description = Path.Length > 0 ? Path : Refusal ?? text.V1.NoScriptChosen;
                if (button.AccessibleDescription != description) button.AccessibleDescription = description;
            }
            finally
            {
                placing = false;
            }
        }

        string Shown(string path)
        {
            // The explanation's own label, which the row adds after its title.
            var label = Controls.OfType<Label>().Skip(1).FirstOrDefault();
            int width = TextWidth;
            string fitted = label is null || width <= 0 ? path
                : Compact(path, s => TextRenderer.MeasureText(s, label.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width <= width);
            return text.RightToLeft ? "‪" + fitted + "‬" : fitted;
        }

        // The width a flat row gives its words: all of it but the button and the 12 px before it,
        // the button counted once it is showing, as the row itself does.
        int TextWidth => Width <= 0 ? 0 : Math.Max(Px(80), Width - (button.Visible ? button.GetPreferredSize(Size.Empty).Width + Px(12) : 0));

        // A path cut down until it fits: the drive or the network share, "…" for the folders left
        // out from the start, then the rest down to the file's name; "…\" and the name when even one
        // folder is too many.
        internal static string Compact(string path, Func<string, bool> fits)
        {
            if (fits(path)) return path;
            bool network = path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal);
            var cuts = new List<int>();
            for (int i = network ? 2 : 0; i < path.Length; i++)
            {
                if (path[i] is '\\' or '/') cuts.Add(i);
            }
            if (cuts.Count == 0) return path;
            int head = network ? 2 : 1;
            for (int k = head; k < cuts.Count; k++)
            {
                string shorter = path[..(cuts[head - 1] + 1)] + "…" + path[cuts[k]..];
                if (fits(shorter)) return shorter;
            }
            return "…" + path[cuts[^1]..];
        }
    }

    // What the time-away note looks like: the logo, "You were away 42 min" in the text color with
    // only "42 min" in the accent, and "· music or video is playing again" muted, in a pill with the
    // card's thin edge, as the note itself is drawn. When the two parts don't fit on one line, as at
    // Windows' biggest text sizes, the muted part goes on a second line under the first, without its
    // dot. Only a picture of the note.
    internal sealed class AwayPreview : KitControl
    {
        readonly string lead;
        readonly string tail;
        readonly (int At, int Length) time;
        Bitmap? logo;
        int logoSize;
        bool logoTurned;

        public AwayPreview(Strings text)
        {
            (lead, _, time) = Toast.Timed(text.V1.YouWereAway, text.ShortDuration(TimeSpan.FromMinutes(42)));
            tail = text.V1.MediaPlayingAgain;
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Text = lead + " " + tail;
            AccessibleName = Text;
            AccessibleRole = AccessibleRole.StaticText;
        }

        public string Lead => lead;

        public string Tail => tail;

        Font LeadFont => Strong(9f);

        Font TailFont => Body(9f);

        int LogoSize => Px(14);

        int Pad => Px(4);

        int Gap => Px(6);

        int EndPad => Px(11);

        // Widths as drawn, without the room TextRenderer keeps at each end, so the gaps are the sketch's.
        static int Across(string words, Font font) =>
            TextRenderer.MeasureText(words, font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;

        int Start => Pad + LogoSize + Gap;

        // The muted part on a line of its own: the dot only parts it from the time on one line.
        public string SecondLine => tail.TrimStart('·', ' ');

        int Wanted => Start + Across(lead, LeadFont) + Gap + Across(tail, TailFont) + EndPad;

        int WantedOnTwo => Start + Math.Max(Across(lead, LeadFont), Across(SecondLine, TailFont)) + EndPad;

        public bool OnTwoLines(int width) => width > 0 && Wanted > width;

        int TwoLinePad => Px(5);

        int PillHeight(int width) => OnTwoLines(width)
            ? TwoLinePad + Shapes.Measure(lead, LeadFont).Height + Shapes.Measure(SecondLine, TailFont).Height + TwoLinePad
            : Math.Max(Px(22), Math.Max(Shapes.Measure(lead, LeadFont).Height, Shapes.Measure(tail, TailFont).Height) + Px(6));

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
            return new Size(width, PillHeight(width));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            Smooth(g);
            bool two = OnTwoLines(Width);
            int width = Math.Min(Width, two ? WantedOnTwo : Wanted);
            int height = Math.Min(Height, PillHeight(Width));
            var pill = Box(0, 0, width, height);
            using (var path = Shapes.Rounded(Shapes.Inside(pill), two ? Px(11) : height / 2f))
            {
                using var fill = new SolidBrush(t.Card);
                g.FillPath(fill, path);
                using var pen = new Pen(t.Edge);
                g.DrawPath(pen, path);
            }
            if (logo is null || logoSize != LogoSize || logoTurned != IsMirrored)
            {
                logo?.Dispose();
                logoSize = LogoSize;
                logoTurned = IsMirrored;
                logo = Logo.Draw(logoSize);
                // The logo is never mirrored: a window laid out right to left mirrors what is drawn in
                // it, so the picture goes in turned round already, as the ring's does.
                if (logoTurned) logo.RotateFlip(RotateFlipType.RotateNoneFlipX);
            }
            var logoBox = Box(Pad, (height - logoSize) / 2, logoSize, logoSize);
            g.DrawImage(logo, logoBox);
            int x = Start;
            int leadWidth = Across(lead, LeadFont);
            int top = two ? TwoLinePad : 0;
            int leadHeight = two ? Shapes.Measure(lead, LeadFont).Height : height;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            var leadBox = Box(x, top, Math.Min(leadWidth, Math.Max(1, width - x - EndPad)), leadHeight);
            Write(g, lead, LeadFont, leadBox, t.Text, flags | TextFormatFlags.EndEllipsis);
            if (time.Length > 0)
            {
                // The time drawn again in the accent over just its columns, counted from the reading
                // start, which a window laid out right to left keeps on the left of its own drawing.
                int before = time.At == 0 ? 0 : Across(lead[..time.At], LeadFont);
                int through = time.At + time.Length == lead.Length ? leadBox.Width : Across(lead[..(time.At + time.Length)], LeadFont);
                int from = Math.Min(before, leadBox.Width), to = Math.Min(through, leadBox.Width);
                if (to > from)
                {
                    int inset = two ? 0 : 1;
                    var columns = new Rectangle(Flip ? leadBox.Right - to : leadBox.Left + from, leadBox.Top + inset, to - from, leadBox.Height - 2 * inset);
                    g.SetClip(columns);
                    using (var card = new SolidBrush(t.Card))
                        g.FillRectangle(card, columns);
                    DrawText(g, lead, LeadFont, leadBox, t.Accent, flags | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping);
                    g.ResetClip();
                }
            }
            if (two)
            {
                int room = width - x - EndPad;
                if (room > 0) Write(g, SecondLine, TailFont, Box(x, top + leadHeight, room, Shapes.Measure(SecondLine, TailFont).Height), t.Subtle, flags | TextFormatFlags.EndEllipsis);
                return;
            }
            x += leadWidth + Gap;
            int oneRoom = width - x - EndPad;
            if (oneRoom > 0) Write(g, tail, TailFont, Box(x, 0, oneRoom, height), t.Subtle, flags | TextFormatFlags.EndEllipsis);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                logo?.Dispose();
                logo = null;
            }
            base.Dispose(disposing);
        }
    }
}
