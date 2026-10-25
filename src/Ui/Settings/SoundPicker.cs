using System.Runtime.InteropServices;

namespace SteppedOut;

// One of the sound lists: Off, SteppedOut's own and four more by name, and "Your own sound…", which
// opens Windows' Open window for a .wav file. A sound picked from the open list plays once so it can
// be heard (Preview); the arrow keys on the closed list only choose, quietly. A file is checked as a
// script is (OwnSound): one that can't be used is not kept, the row says why in place of its
// explanation, and the sound stays as it was. A file once picked stays in the list under its own
// name, just above "Your own sound…", even while another sound is chosen.
//
// The list's values are the setting's own choices (ActionSound or BreakSound); own is the one for
// your own sound. The picker never changes the settings itself: Chosen asks for a sound and, with
// your own sound, its file.
sealed class SoundPicker
{
    readonly object own;
    readonly Func<object, string, Sound?> play;
    readonly Strings text;
    string file = "";

    // play says what a value plays, with the own sound's file.
    public SoundPicker(Strings text, string name, IEnumerable<(string Text, object Value)> choices, object own, Func<object, string, Sound?> play)
    {
        this.text = text;
        this.own = own;
        this.play = play;
        List = new DropDown { Small = true, AccessibleName = name };
        foreach (var (words, value) in choices) List.Add(words, value);
        List.AddAction(text.V1.YourOwnSound, ChooseOwn);
        List.SelectedChanged += (_, _) =>
        {
            if (List.Value is { } value) Choose(value, file);
        };
        List.Picked += (_, _) =>
        {
            if (List.Value is { } value) Hear(value, file);
        };
    }

    public DropDown List { get; }

    // The row the list is in and its explanation, which a file that can't be used takes the place of.
    public (SettingRow Row, string Explanation)? Place { get; set; }

    // A sound chosen in the list, and the own sound's file, for the settings.
    public event Action<object, string>? Chosen;

    // Plays a sound picked in the list once. Nothing when it is not set, as in the tests.
    public Action<Sound>? Preview { get; init; }

    // Your own sound…: the file someone picked, or null. Windows' Open window unless a test gives its own.
    public Func<Control, string?>? ChooseFile { get; init; }

    // What Windows says is behind a drive letter (LocalPaths); a stand-in in tests.
    public Func<char, DriveType> Drives { get; init; } = LocalPaths.DriveTypeOf;

    // Puts the setting in the list without telling Chosen: the own sound's file by its name, then the
    // sound chosen.
    public void Show(object chosen, string ownFile)
    {
        file = OwnSound.Tidy(ownFile);
        if (file.Length > 0) List.ShowAs(own, FileName(file));
        List.Value = chosen;
    }

    // The file's name without .wav, cut short with … past 28 letters, so a long one cannot push the
    // list over the row's words. The … is followed by a left-to-right mark, so that in the Arabic
    // window it stays at the end of a Latin name, not at its start.
    internal static string FileName(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path).Trim();
        if (name.Length == 0) name = path;
        return name.Length <= LongestName ? name : name[..(LongestName - 1)].TrimEnd() + "…‎";
    }

    const int LongestName = 28;

    void Choose(object value, string ownFile)
    {
        Noted(null);
        Chosen?.Invoke(value, ownFile);
    }

    void Hear(object value, string ownFile)
    {
        if (Preview is null) return;
        var sound = play(value, ownFile);
        if (sound is not null) Preview(sound);
    }

    void ChooseOwn()
    {
        string? picked = (ChooseFile ?? Dialog)(List);
        if (string.IsNullOrWhiteSpace(picked)) return;
        string path = OwnSound.Tidy(picked);
        if (OwnSound.Problem(path, Drives) is { } problem)
        {
            Log.Write($"did not keep {path} as a sound: {OwnSound.Why(problem)}");
            Noted(problem switch
            {
                OwnSoundProblem.AnotherPC => text.V1.SoundOnAnotherPC,
                OwnSoundProblem.TooBig => text.V1.SoundTooBig,
                OwnSoundProblem.TooLong => text.V1.SoundTooLong,
                _ => text.V1.SoundCantPlay,
            });
            return;
        }
        file = path;
        List.ShowAs(own, FileName(path));
        Choose(own, path);
        Hear(own, path);
    }

    void Noted(string? note)
    {
        if (Place is { } place && (note is not null || place.Row.DetailWarns)) place.Row.Note(note, place.Explanation);
    }

    string? Dialog(Control owner)
    {
        try
        {
            using var dialog = new OpenFileDialog
            {
                Filter = text.V1.SoundFileFilter,
                CheckFileExists = true,
                RestoreDirectory = true,
            };
            return dialog.ShowDialog(owner.FindForm()) == DialogResult.OK ? dialog.FileName : null;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or ExternalException)
        {
            Log.Write("could not open the sound dialog: " + e.Message);
            return null;
        }
    }
}
