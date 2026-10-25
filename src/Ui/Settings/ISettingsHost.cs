namespace SteppedOut;

// What the settings window and its pages ask of the app that opened them: the tray app, or a
// stand-in when a window is built on its own. Each call is the person's choice in the window.
interface ISettingsHost
{
    // Pause from the status box, for this long or, with null, until turned back on.
    void PauseChosen(TimeSpan? length);

    // Turn back on while paused.
    void ResumeChosen();

    void LockNowChosen();

    // Show the countdown note for a few seconds. It never locks.
    void TryItChosen();

    // The lock shortcut was set to this, or to "" for none. Register it now, and say how that went
    // through ShortcutResult.
    void ShortcutChanged(string shortcut);

    // "Choose another shortcut…" opened (true) or closed (false). Windows hands a registered
    // shortcut to the app that registered it, never to the window in front, so while the window is
    // open the app holds no lock shortcut: its keys reach the box instead of locking the PC, and
    // nothing takes one again meanwhile except ShortcutChanged, which the window uses to try one.
    // The window puts back or keeps a shortcut through ShortcutChanged before it says it closed.
    // A stand-in host has nothing to let go of.
    void ChoosingShortcut(bool open)
    {
    }

    // "Choose another shortcut…" registered this new one through ShortcutChanged and closes with it.
    // The page's edit brings it into the settings once the window is gone, so the app keeps holding
    // it as the window closes, rather than the one in settings applied while the window was open.
    void ShortcutKept(string shortcut)
    {
    }

    // The window switched to this language ("en", "ar" or "auto"): the tray follows at once.
    void LanguageChanged(string language);

    // How registering the lock shortcut went last, for the row that says when it is taken or types
    // a letter.
    HotKeyResult ShortcutResult { get; }

    event Action? ShortcutResultChanged;

    // What is delaying or stopping the lock right now, for the Why window, which asks once a second.
    IReadOnlyList<WhyLine> Explain();

    // The settings as edited in the window, once its short wait is over and only when they differ
    // from the ones in use (SameAs): save them and use them at once.
    void ConfigEdited(Config config);

    // The window went to this tab, for it to open on next time: save it with the settings in use,
    // and nothing else. A stand-in host keeps nothing.
    void TabChosen(int tab)
    {
    }

    // Start with Windows lives in Windows (the Run entry, or the Store app's startup task), not in
    // the settings file: whether this copy starts with Windows, and the person's choice to change it.
    // StartsWithWindows is StartWithWindowsState.IsOn(). The state also says when Windows or a
    // policy holds the choice (CanChange) and why (Note). It is Reading until Windows first answers,
    // and while a choice is not finished yet.
    bool StartsWithWindows { get; }

    AutostartState StartWithWindowsState { get; }

    // The choice may finish after StartWithWindowsChosen returns (the Store app asks Windows, which
    // can take seconds), and Windows can change it too: raised on the UI thread when the state did.
    event Action? StartWithWindowsChanged;

    void StartWithWindowsChosen(bool on);

    // What the app is doing right now, for the status box, and a signal each time it may have
    // changed: about once a second, and at once after a pause, a turn back on or a lock. Status
    // already shows a pause, a turn back on or a lock now when the call that asked for it returns.
    // StatusChanged is raised on the UI thread.
    LockStatus Status { get; }

    event Action? StatusChanged;

    // "Show the welcome again" on the Extras tab: once the click is done, the settings window closes,
    // saving its waiting change, and the welcome opens, one window at a time as ever. A stand-in host
    // has no welcome to show.
    void ShowWelcomeChosen()
    {
    }

    // "Remove SteppedOut's files" on the Extras tab, already asked and answered there: turn off start
    // with Windows, quit, and delete the files the app writes in its folder. A stand-in host removes
    // nothing.
    void RemoveFilesChosen()
    {
    }

    // A sound was picked in one of the sound lists: play it once so it can be heard. Through the away
    // actions, whose parts refuse every sound in the tests and the screenshot tool. A stand-in host
    // plays nothing.
    void PreviewSound(Sound sound)
    {
    }

    // The last save of the settings failed: Windows would not let config.json be written. The window
    // then says so where it says changes are saved, until a save works again; the host tells it
    // through SettingsForm.SaveStateChanged. A stand-in host always saves.
    bool SaveFailed => false;
}
