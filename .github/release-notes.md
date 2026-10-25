SteppedOut locks your PC when you walk away from it, the same lock you get by pressing Win+L. It waits until you have really gone: by default music or video, a call or an app in full screen keeps it unlocked, each can be set to add time instead, and a countdown in the corner gives you time to cancel first. Locking is not sleep, so downloads and anything else you left running carry on, unless you choose to have it sleep after the lock. It collects nothing and makes no network connections ([Privacy](https://github.com/medric-z/steppedout#privacy)).

**Which file:** `SteppedOut-win-x64.exe` for almost every PC. Take `SteppedOut-win-arm64.exe` only if Settings, System, About says "ARM-based processor". It is one file for Windows 10 and 11, with nothing else to install and no administrator rights.

**If Windows warns you:** the file is not code-signed yet, so Windows may say "Windows protected your PC". Click More info, then Run anyway. The [README](https://github.com/medric-z/steppedout#if-windows-warns-you) explains each warning.

### Upgrading from 0.1.0

1.0 starts fresh: none of your old settings is carried over. The first time 1.0 starts, it keeps your old settings file as `config.json.before-1.0` in the same folder (`%APPDATA%\SteppedOut`), writes a new one with the defaults, and opens the welcome window so you can make your choices again. If that name is taken by a different file or a folder, the new copy has the date and time added to its name, and nothing is overwritten. `--once`, and a second start while SteppedOut is running, leave the old file as it is. The welcome window asks about Start with Windows again, switched on, whatever it was before.

What changes in how it behaves:

- **The countdown now ends at the lock time.** It sits inside the lock time instead of coming after it. With the default 15 minutes and a 15-second warning, the countdown starts after 14 minutes 45 seconds without use and the PC locks after 15 minutes, where 0.1.0 locked after 15 minutes 15 seconds. The note reads "Locking in 15 sec · move the mouse or press a key to cancel", and either one cancels it.
- **Waking from sleep starts the lock time over.** After the PC wakes from sleep or hibernation, the lock time starts over from zero and the next countdown starts a warning's length before it, and a countdown that was running when it went to sleep ends without locking. A sleep from ordinary use is not time away. A PC that was already locked when it went to sleep stays locked, and the sleep counts as time away; a lock Windows makes as the PC goes to sleep counts as a lock, with its time away counted from the wake, and runs no away actions.

**Going back to 0.1.0:** get it from its [release page](https://github.com/medric-z/steppedout/releases/tag/v0.1.0). Quit SteppedOut from its menu, copy `config.json.before-1.0` over `config.json`, then start 0.1.0. Without that copy, 0.1.0 reading the new file falls back to its defaults for the old settings, and if it saves, it drops every new one. Starting 1.0 again later starts fresh again.

### New since 0.1.0

- An Arabic interface, laid out right to left. The language is chosen at the top right of the settings window.
- A welcome window on first start, in five pages with a drawn PC and a timeline that show each choice: how long before it locks, the warning and a shortcut (off unless you pick one); extra time while music or video plays, on a call or full screen, and locking sooner on battery (asked only on a PC with a battery); what to do when you leave and come back, and break reminders; and Start with Windows.
- A list of common lock times (5, 10, 15 and 30 minutes, 1 hour), with Custom… for any whole number of minutes from 1 to 1440. The 0.1.0 box also took fractions of a minute; the window no longer does, but the settings file and `--idle` still take one.
- Calls keep the PC unlocked while an app is using the microphone.
- A new settings window: a status box (a ring, what SteppedOut is doing and why, Pause and Lock now), seven everyday choices, and five tabs: Locking, Away and back, Appearance, Breaks and Extras. Changes save as you make them, and the window opens on the tab you used last.
- Extra times add up. While music or video plays, you are on a call, or an app is full screen, each can add time (+15 minutes to +2 hours, or any number of minutes) or never lock. The status box shows the sum and what it is made of.
- On battery power the laptop can lock as usual, 25%, 50% or 75% sooner, or after a fixed time at the latest. On a PC with no battery the row is hidden.
- App rules: for each app you add, never lock, wait longer, lock sooner, or lock after a fixed time, while it is open or only while it is on screen. This replaces the list of programs that hold the lock.
- A keyboard shortcut to lock now from any app: Ctrl+Alt+L or one you choose. It is off until you turn it on.
- When SteppedOut locks the PC by itself, it can also turn off the screen or put the PC to sleep (At the lock time, on the Locking tab, which also has Don't lock).
- Pause for 15 or 30 minutes, 1 or 2 hours, a day, until turned back on, or with Custom… any time from 1 minute to 7 days. The status says when it comes back: today, tomorrow or the day.
- A controller (Xbox-compatible) counts as using the PC.
- When you are away it can pause music and video, mute the sound, turn the screens off, play a sound, open apps, close apps and run a script, and you choose what happens to music and video: play again when you're back, keep it paused, or don't pause. Pausing reaches apps that show in Windows' media controls. The sounds are fifteen calm ones of SteppedOut's own, or a .wav file of your own. When you are back after a minute or more it says how long you were gone. Close apps closes only the apps on its list: it asks each one to close, waits up to 30 seconds, then forces only the listed apps still running. Programs they started, such as a game started from a launcher, are left alone, and so is any app running as administrator, or whose rights it cannot read. The tab says plainly that SteppedOut can't save your work: whether unsaved work is kept is up to each app.
- Away today and Locked today in the status box, kept in a small file, `today.json`.
- Reminders to take a break, eye breaks (20-20-20), a Snooze button and a reminder sound. The reminder time and the snooze time each have a list and Custom…. Break reminders are on, every 2 hours, on a new install; after an upgrade from 0.1.0 they are off. The reminder sound is off.
- Light, dark or like Windows, a text size, and where the notes appear: any corner of the screen, or its center. The notes in the corner are small and calm, with only the time and a thin line in green.
- Why isn't the PC locking? lists what is delaying the lock right now.
- A different tray menu: a status box, Snooze this reminder (only while a break or eye-break note offers Snooze), Pause, Lock now, Settings and Quit SteppedOut. The menu no longer has Open config folder (Open folder on the Extras tab does that) or Start with Windows, which is now a row in the settings window and in the welcome window.
- Try it shows the countdown for 5 seconds without locking.
- On the Extras tab: what counts as using the PC (tiny mouse movements, and how loud and how long sound must be, each with Custom…), the log size, saving the settings to a file and loading them, showing the welcome again, resetting all settings, and removing SteppedOut's files.
- Scripts and apps to open must be on this PC, by their full path. One on another PC, by a network path or on a mapped network drive, is refused where you choose it and again, with the reason, in the log.
- Two failures that would otherwise be silent now say so in a note in the corner: settings that could not be saved ("Settings couldn't be saved", and the footer says the changes are used until SteppedOut quits), and a once-a-second check that keeps failing ("Locking might not work right now").

### Checking the download

Each file's SHA-256 is in `SHA256SUMS.txt`. To check that a file was built by this repository's GitHub Actions, and see the workflow and tag it was built from: `gh attestation verify SteppedOut-win-x64.exe --repo medric-z/steppedout`

[Code signing policy](https://github.com/medric-z/steppedout#code-signing-policy)
