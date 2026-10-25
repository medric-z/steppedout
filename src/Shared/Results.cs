namespace SteppedOut;

// What registering the lock shortcut gave. TypesALetter: with Ctrl and Alt held, the key types a
// character on one of the installed keyboard layouts (as AltGr does), so it is not registered and
// typing is never blocked.
enum HotKeyResult
{
    Ok,
    InUse,
    TypesALetter,
    Invalid,
}

// What a rule picked up, for the Why window.
enum Signal
{
    Sound,
    Display,
    FullScreen,
    Call,
    App,
    Battery,
}

// One line of the Why window: something going on right now and what it does to the lock time.
// Detail names the program when one is known. Counts is false for something seen that does not
// count, such as the sound of the app the call is in, which counts as the call and not also as music.
readonly record struct WhyLine(Signal Signal, Effect Effect, string Detail = "", bool Counts = true)
{
    // Sound and a keep-the-display-on request are both music or video, which counts once.
    public EffectSource Source => Signal switch
    {
        Signal.Sound or Signal.Display => EffectSource.MusicOrVideo,
        Signal.FullScreen => EffectSource.FullScreen,
        Signal.Call => EffectSource.Call,
        Signal.App => EffectSource.ForApp(Detail),
        _ => EffectSource.Battery,
    };
}

// What coming back did: the apps whose music or video was paused when the PC locked and is playing
// again now, for the "music or video is playing again" note.
sealed record ReturnResult(IReadOnlyList<string> PlayedAgain)
{
    public static ReturnResult Nothing { get; } = new(Array.Empty<string>());

    public bool AnyPlayedAgain => PlayedAgain.Count > 0;

    public bool Equals(ReturnResult? other) => other is not null && PlayedAgain.SequenceEqual(other.PlayedAgain, StringComparer.OrdinalIgnoreCase);

    public override int GetHashCode() => PlayedAgain.Count;
}
