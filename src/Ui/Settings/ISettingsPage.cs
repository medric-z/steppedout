namespace SteppedOut;

// One tab of the settings window. The page never changes the settings itself: it asks for each
// change through Edited, and the window applies the change, saves it shortly after, and calls
// Refill on every page.
interface ISettingsPage
{
    // The page's content for these settings in this language. Called again, for a new control,
    // when the language, the theme or the text size changes; the window owns the control it gets
    // back and disposes it when it asks for a new one.
    Control Build(Config config, Strings text);

    // The settings changed: from the file, the tray, the main list, another page, or this page's
    // own edit once applied. The page shows them as they are now. Refill never raises Edited, so a
    // refill cannot start a save that changes the file that starts another refill.
    void Refill(Config config);

    // A change the person made. The window calls it with a copy of the settings it may change, keeps
    // what it returns, and skips the save when the result is SameAs the settings it had.
    event Action<Func<Config, Config>>? Edited;
}
