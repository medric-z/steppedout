namespace SteppedOut;

// What the tray shows for one status: the icon's state and minutes, the words on hover and the
// menu's state.
sealed record TrayViewState(TrayIconState Icon, string? Minutes, string Tooltip, TrayMenuState Menu);

static class TrayView
{
    // paused is Watcher.IsPaused. shortcut is the lock shortcut while it is registered, or null.
    // snooze is a reminder note with Snooze showing now.
    // The minutes show on the icon only when chosen in the settings, and only while counting toward
    // a lock (StatusText.IconMinutes). The tooltip and the menu say "Locking off" from the settings,
    // and "(dry run)" from the status.
    public static TrayViewState From(LockStatus status, bool paused, Config config, string? shortcut, Strings text, bool snooze = false) => new(
        TrayIcons.StateOf(status, paused),
        config.MinutesOnTrayIcon ? Strings.IconMinutes(status) : null,
        TrayIcons.Tooltip(text, status, config.LockWhenAway),
        new TrayMenuState(status, paused, config.LockWhenAway, shortcut, snooze));
}
