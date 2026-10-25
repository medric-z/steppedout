# Where things live

Each feature is its own file plus one line that switches it on. To take a feature out, delete the
file, its test in `tests/` if it has one (named after it, such as `JitterFilterTests.cs`), and that
line; the compiler then points at anything else that still used it. Paths are under `src/`, and
every file under `src/` is named below with its job.

Once a second `Watching/Watcher.cs` reads the idle time and asks the rules what is going on.
`Watching/LockTime.cs` works out the lock time from the normal time and everything the rules found,
and `Watching/LockEngine.cs` runs the countdown inside that time. When it decides you have left, it
locks and raises an event, and `Ui/TrayApp.cs` hands that to the away actions and the corner note.
The same lock time, with what makes it up, goes to the tray, the settings window, the Why window,
the log and `--once`.

## Watching

| File | What it does |
| --- | --- |
| `Watching/Watcher.cs` | The main loop. Its timer runs 8 times a second (every 125 ms) while the session is unlocked, to sample the cursor, and once a second while it is locked. Once a second it reads the idle time, asks the rules (`Rules/Evaluator.cs` decides how often), works out the lock time, runs the countdown, and raises the leave, back and reminder events everything else listens to. It starts the lock time over when the PC wakes from sleep (a gap of `SleptAfter` between ticks, or Windows' resume), and `Woke` decides what the sleep was: a lock Windows made as the PC went to sleep counts as a lock, one from before stays, an ordinary sleep is not time away. It raises `TicksFailing` when 10 ticks fail without 10 good ones in a row. It also keeps today's counts and the status the windows show. |
| `Watching/LockTime.cs` | The lock time as one pure function: the normal time plus every extra, then the biggest cut, then the shortest cap, and the countdown length. Also the words of an effect in the settings file (`later 60`, `sooner 50`, `always 10`, `never`). Decimal math and no Windows calls, so `LockTimeTests.cs` checks every case. |
| `Watching/LockEngine.cs` | The countdown and the leave and back steps. It takes the lock time as a number, so it has no Windows calls and the tests drive it directly. |
| `Watching/LockStatus.cs` | What the windows are told every second: the state, the lock time and its parts, the idle time, the time left, and today's counts. |
| `Watching/InputTracker.cs` | Idle time from `GetLastInputInfo`, the cursor samples the jitter filter needs, and `NoteInput` for input Windows does not report, such as a controller. |
| `Watching/JitterFilter.cs` | Decides whether reported input was a hand or a twitching sensor. Called from `InputTracker.cs`. |
| `Watching/ControllerInput.cs` | Asks the controllers for their state and reports real use to `InputTracker`. Its reading is `Interop/XInput.cs`. |
| `Watching/SessionLock.cs` | `LockWorkStation`, and the guard that stops the tests and the screenshot tool from locking the session. |
| `Watching/BreakTimer.cs`, `Watching/EyeBreaks.cs` | The break reminder and the 20-20-20 eye break, with snooze. `Reminders` in `EyeBreaks.cs` holds both, so they never show at once. One field in `Watcher.cs`. |
| `Watching/DailyStats.cs` | Locked today and Away today. Saved in `today.json`, overwritten in place, started again at midnight. |
| `Watching/Clock.cs` | A clock that only moves forward, which the watcher and the rules time things with. `DateTime.Now` jumps when the clock is set and at daylight saving changes. |

## Rules: what changes the lock time

Each rule is a `Rule` (in `Rules/Busy.cs`) and gets one line in `RuleSet.Apply` there, which turns it
on only when its setting needs it. A rule adds a line for each thing going on, and each line says
what it does to the lock time. `Rules/Scene.cs` is one look at Windows that the rules share, and
`Rules/Evaluator.cs` asks them, holds a reading for a few seconds so a gap never starts a
countdown, and sets how often they are asked (`PollSchedule`).

| File | Watches |
| --- | --- |
| `Rules/Busy.cs` | The `Rule` base class, and `RuleSet`, which builds the rules for one set of settings and keeps them as long as the watcher runs. |
| `Rules/Scene.cs` | Everything the rules ask Windows, in one place, and one look at it that every rule shares, so the tests can answer instead of the PC. |
| `Rules/Evaluator.cs` | Asks the rules, turns what they see into lines for the watcher and for `--once`, holds a reading for a few seconds, and keeps a failed rule's last good lines. `PollSchedule` sets how often each is asked. |
| `Rules/AudioRule.cs` | Sound above `audioPeakThreshold` on any output device that is not muted or at zero volume, through `Interop/Wasapi.cs`. The sound of the app you are on a call in is the call's, not music. |
| `Rules/DisplayRule.cs` | The system-wide keep-the-display-on flag. With the sound, it is the one music or video setting. |
| `Rules/FullscreenRule.cs` | A truly fullscreen foreground window, with the desktop, taskbar, screen saver, Task View, a full-screen File Explorer, Start and search left out. |
| `Rules/CallRule.cs` | Apps with the microphone open, from the per-user privacy record. |
| `Rules/BatteryRule.cs` | Whether the laptop is on battery power. Whether the PC has a battery at all, which hides the On battery power row, is `Native.HasBattery`, read through `WelcomeForm.HasBattery`. |
| `Rules/AppRules.cs` | The apps in `appRules`, running or on screen. One effect per app, however many processes it has. |
| `Rules/ProcessRule.cs` | Finding programs by name, in this sign-in only. Also the name matching the app lists use. |

## Away actions: what happens when you leave and come back

Each is its own class and gets one line in `Build` in `Actions/AwayActions.cs`, which runs them in a
fixed order on one background thread. A new action is one class and one line there.

| File | Does |
| --- | --- |
| `Actions/AwayActions.cs` | Builds the actions, runs them in order on one background thread, and picks the starter that opens apps and runs scripts (none of that in a copy with full administrator rights). Also sign-out, shutdown and Switch user, and what At the lock time adds to SteppedOut's own lock: the screens off, or a sleep queued after the other away actions. |
| `Actions/SleepAfterLock.cs` | Lock and sleep: whether the PC has a sleep an app can ask for, asking Windows to sleep (never to hibernate), and whether the session is locked yet. Behind an interface the tests replace, and blocked like the lock while `SessionLock.Blocked` is set. |
| `Actions/PlaySound.cs` | The fifteen sounds built into the exe (`Sounds/*.wav`, made by `tools/sounds/gen.cs`): five for leaving, five for coming back and five for reminders. Which one a choice plays, the checks on your own sound (`OwnSound`: a .wav file on this PC, up to 5 MB and 10 seconds), and playing them. |
| `Actions/PauseMedia.cs` | Pauses what is playing and resumes it, or leaves it paused, through `Interop/MediaSessions.cs`. Only apps that show in Windows' media controls are reached; sending the pause straight to other apps' windows was tested and dropped, since an app that ignores it passes it on and can start other media. |
| `Actions/MuteSound.cs` | Mutes the default output and keeps the `muted-by-SteppedOut` marker. Its leftover checks are the `queue.Add(parts.UndoLeftoverMute)` line in the constructor and the `parts.RetryLeftoverMute` line in `Run`, both in `AwayActions.cs`. |
| `Actions/ScreensOff.cs` | Turns the screens off, and again if a twitch wakes them and locking is off. |
| `Actions/CloseApps.cs` | Asks every window of the apps in the list to close, waits, then forces the ones left. |
| `Actions/AppWindows.cs` | Picks which windows to ask to close for one process, including a Store app's frame window. |
| `Actions/ProcessApi.cs` | Everything closing apps asks Windows (process list, parents, windows, ending a process), behind an interface the tests replace. |
| `Actions/OpenApps.cs` | Starts the apps in the list, by full path on this PC or Store app ID. |
| `Actions/RunProgram.cs` | Starts the script chosen for leaving or for coming back, from a path on this PC. |

## Settings and start-up

| File | What it does |
| --- | --- |
| `Config/Config.cs` | Every setting, its default, its range (`Clamp`), and the reading of the file again when it changes on disk (`ConfigWatcher`, `ConfigReload`), where a read that fails is tried a few more times before the file counts as broken. A file from before 1.0 loads as the defaults; one put back while the app runs gets the settings in use written back over it. `WholeFile` in the same file writes a save through a temporary file and replaces the real one, or writes in place where a link or the Store version needs it. |
| `Config/Lenient.cs` | The readers for the settings added in 1.0. A value of the wrong kind is skipped with a note in the log, and the rest of the file still loads. |
| `Config/FreshStart.cs` | The fresh start from a file from before 1.0: the `config.json.before-1.0` copy (with the date and time when that name is taken) and the new file with the defaults. A separate step: loading only reads, and only the running copy writes. Every save over such a file keeps it first. |
| `Config/WholeNumber.cs` | Lets a hand-edited whole-number setting take `15.0`. |
| `Config/OwnFiles.cs` | The files SteppedOut writes in its settings folder, by their whole names, for Remove SteppedOut's files: it deletes only those, then the folder once it is empty. While `SessionLock.Blocked` is set, only a folder inside the temp folder is ever emptied. |
| `Config/Autostart.cs` | The `HKCU\...\Run` value, and the off mark Task Manager keeps for it. In the Store version it asks the startup task in `Platform/Packaged.cs` instead. |
| `Input/Shortcut.cs` | Reads and writes the lock shortcut (`Ctrl+Alt+L`), and says which ones are allowed. |
| `Cli/CommandLine.cs` | The flags (`--once`, `--dry-run`, `--idle`, `--help`, `--version`). |
| `Cli/OnceReport.cs` | What `--once` prints. |
| `Cli/ConsoleOutput.cs` | Lets the windowed exe print `--once`, `--help` and `--version` to the terminal it was started from. |
| `Program.cs` | Reads the flags and decides with `StartPlan` what this start may read and write, then prints the `--once` report (which writes no file) or starts the tray app, one per sign-in. Only the tray copy keeps a file from before 1.0 and starts the settings fresh. A second start asks the running copy to open its settings. `BuildInfo` holds the commit the exe was built from. |
| `AppInfo.cs` | The name, the version, and where the settings and log live. To rename the tool, change the name here and in the project file. |
| `Log.cs` | The log and its one older generation. |
| `Shared/Results.cs` | Small results the parts pass to each other: how registering the shortcut went, a line of the Why window, and which apps played again on return. |
| `Platform/Packaged.cs` | Whether this is the Microsoft Store version, which folder holds its settings (what Open folder opens), and its startup task. The normal exe never goes down this path. |
| `Platform/InstanceChannel.cs` | The one-copy-per-sign-in mutex, and the event a second start sets to ask the running copy to open its settings. |
| `Platform/ErrorRepeats.cs` | Writes an error that keeps coming back to the log in full once, then only counts it: one line an hour, and a line with the count when it stops. |
| `Platform/Elevation.cs` | Whether this copy runs with full administrator rights, and `ElevatedStarter`, which opens no app and runs no script in such a copy. |
| `Platform/LocalPaths.cs` | Whether a path from the settings is on this PC: only fixed, removable, CD-ROM and RAM drives, never a network path or a mapped network drive. |
| `Platform/FileLinks.cs` | Finds the hard and symbolic links to a file, which a save has to write through rather than replace. |
| `Platform/DllSearch.cs` | Limits every DLL the app loads to Windows' System32 folder, so a DLL dropped next to the exe is never loaded. |

## Windows

| File | What it does |
| --- | --- |
| `Ui/TrayApp.cs` | The composition root: the tray icon, and the glue between the watcher, the actions, the shortcut, the controller and the windows. |
| `Ui/TrayParts.cs` | Everything the tray app makes or reaches outside itself (icon, Run entry, windows, controller, shortcut, sound, settings file), so the tests give their own. While `SessionLock.Blocked` is set, even the real ones touch nothing of the person's. |
| `Ui/TrayNotes.cs` | The routes from the watcher to the corner notes and the away actions: the countdown, stepping out and coming back, the break and eye-break reminders with their sound, and Snooze. |
| `Ui/TrayLive.cs` | The watcher's `KeepLive` leases the tray holds, one for the open menu and one for the open settings window, so the rules are asked every second while the status shows. |
| `Ui/TrayView.cs` | What the tray shows for one status: the icon's state and minutes, the words on hover and the menu's state. |
| `Ui/TrayMenu.cs` | The tray menu: a status box, Snooze this reminder (only while a break or eye-break note offers Snooze), Pause, Lock now, Settings and Quit. |
| `Ui/TrayMenuLook.cs` | The sizes and the drawing of the tray menu's rows, line and status box. |
| `Ui/TrayIcon.cs` | The icon in the notification area: the picture, the words on hover and a left click. The real one is a `NotifyIcon`; the tests and the screenshot tool use one that only remembers. |
| `Ui/TrayIconSlot.cs` | Holds the one icon the tray shows and swaps it when the state, size, taskbar or minutes change, destroying the old handle only after the new one is in. |
| `Ui/TrayIcons.cs` | The tray icon's states (on, paused, counting down), the minutes shown on it, the size the tray wants, and the tooltip. |
| `Ui/TrayArt.cs` | The pixel art of the tray icon at 16, 20 and 24 px. |
| `Ui/Icons.cs` | The window and taskbar icon, read from `icon.ico` built into the exe. |
| `Ui/TrayAutostart.cs` | Start with Windows as the tray app reaches it: the Run entry, the Store startup task (asked off the window's thread), or a stand-in that writes nothing. |
| `Ui/Toast.cs` | The corner note: the countdown, the preview, time away, the break and eye break reminders with Snooze, and the notes about a config.json that did not load, settings that could not be saved, and a once-a-second check that keeps failing. |
| `Ui/NotePlace.cs` | Which corner of the work area a note goes in, and keeping it inside. |
| `Ui/WorkArea.cs` | The part of a screen that windows may cover, without the taskbar. |
| `Ui/SettingsForm.cs` | The settings window: the language pills, the status box, the main list and the five tabs. It opens on the tab used last (`settingsTab`) and saves a moment after each change. |
| `Ui/Settings/ISettingsPage.cs`, `ISettingsHost.cs` | What a tab promises (build a control, ask for an edit, take a refill) and what the window asks the tray app for. |
| `Ui/Settings/StatusBox.cs`, `MainList.cs` | The box at the top, and the seven everyday choices. |
| `Ui/Settings/LockingTab.cs`, `AppRulesList.cs` | The Locking tab: On battery power (left out on a PC with no battery), At the lock time (one list for `lockWhenAway` and `whenLocking`), controllers, the app rules, the lock shortcut and the Why window. |
| `Ui/Settings/AwayTab.cs` | The Away and back tab, with the warning above both Close apps lists (`CloseList`). |
| `Ui/Settings/AppearanceTab.cs`, `BreaksTab.cs` | The Appearance and Breaks tabs. |
| `Ui/Settings/AdvancedTab.cs` | The Extras tab, in two parts: Mouse and audio, and Settings and log (Open folder, Log size, Save to file… and Load from file…, Show the welcome again, Reset all settings, Remove SteppedOut's files). |
| `Ui/Settings/QuestionWindow.cs` | The question asked before Reset all settings and Remove SteppedOut's files, with Cancel as the keyboard's start. |
| `Ui/Settings/SoundPicker.cs` | A sound list of the Away and back and Breaks tabs: the sounds by name and Your own sound…, which checks the file picked; a sound picked plays once through the host. |
| `Ui/Settings/ShortcutDialog.cs`, `WhyDialog.cs` | Choosing another shortcut, and the Why isn't the PC locking? window. |
| `Ui/PauseForWindow.cs` | The Pause for window that Custom… in the pause list opens: a number and minutes, hours or days, from 1 minute to 7 days. |
| `Ui/TimeChoices.cs` | The ready-made times the settings offer, which one a setting matches, and reading a time typed in minutes. No window, so `TimeChoicesTests.cs` checks it directly. |
| `Ui/AppPicker.cs` | The list of running apps and Choose another app…, used by the app rules and the open and close lists. What it asks Windows is in `Interop/ProcessInfo.cs`. |
| `Ui/ProgramNames.cs`, `Ui/ProgramIcons.cs` | The name a program gives itself and its own icon, read away from the window and kept, for the app lists. Only a file on this PC is read. |
| `Ui/Welcome/WelcomeForm.cs` | The first-run window, also opened by Show the welcome again: five pages, the footer with the step dots, Try it and Play, and what it hands over when it closes. |
| `Ui/Welcome/WelcomeChoices.cs` | What the window asks, read from the settings and handed back into them, the lock time with the things that can be going on, the moments, what the drawn PC shows at each, and the lines put together from the choices. No window, so `WelcomeTests.cs` checks it directly. |
| `Ui/Welcome/Stage.cs`, `Timeline.cs` | The drawn PC with the Right now block beside it, and the timeline under it: five stops, the stretch before Locks that grows with the extra time, and Never lock as a striped bar. |
| `Ui/Welcome/Pages.cs` | The parts the pages are made of: the questions, the tiles, the taskbar picture, the summary, the step dots and the footer. |
| `Ui/Logo.cs` | The logo, drawn in code. |
| `Ui/Kit/Theme.cs` | Light, dark and text size, and the colors (with the app's one green) and fonts every window shares. |
| `Ui/Kit/KitControl.cs`, `Ui/Kit/KitButton.cs` | What the hand-drawn controls share (painting, scaling, mirroring, screen readers), and the rounded button. |
| `Ui/Kit/Toggle.cs`, `DropDown.cs`, `Segmented.cs`, `TabStrip.cs`, `LanguagePills.cs`, `ChoiceChips.cs` | The switch, the drop-down with its Custom… rows, the segmented choice, the tabs, the language pills, and the pills the welcome window picks with one click. |
| `Ui/Kit/Card.cs`, `FlatPanel.cs`, `SettingRow.cs`, `ChipList.cs`, `Ring.cs`, `MinutesField.cs` | The cards and panels the pages are built from, a setting row, the chip list of apps, the ring around the logo, and the minutes box. |
| `Ui/Kit/MenuLook.cs`, `Ui/Kit/Glyphs.cs` | Menus drawn the way Windows 11 does, and the icons from Segoe Fluent Icons. |

## Windows APIs

| What it touches | File |
| --- | --- |
| Most native declarations | `Interop/Native.cs` (Win32) and `Interop/Wasapi.cs` (Core Audio, COM declared by hand). `Interop/MediaSessions.cs`, `Interop/HotKey.cs`, `Interop/XInput.cs`, `Interop/ProcessInfo.cs`, `Interop/Automation.cs`, `Rules/FullscreenRule.cs`, `Actions/ProcessApi.cs`, `Actions/PlaySound.cs`, `Platform/Packaged.cs`, `Platform/Elevation.cs`, `Platform/LocalPaths.cs`, `Platform/FileLinks.cs`, `Platform/DllSearch.cs`, `Program.cs` and `Text/Strings.cs` declare the few they use themselves. |
| The lock shortcut (`RegisterHotKey`, not a keyboard hook) | `Interop/HotKey.cs` |
| Controllers (XInput polling, not raw input) | `Interop/XInput.cs`, `Watching/ControllerInput.cs` |
| The Start with Windows value and its Task Manager mark | `Config/Autostart.cs` |
| The Store version's startup task, and where its files are | `Platform/Packaged.cs` |
| Light or dark mode and the text size (read only) | `Ui/Kit/Theme.cs` |
| Session lock and unlock | `Watching/Watcher.cs` |
| Sign-out, shutdown and Switch user | `Actions/AwayActions.cs` |
| The microphone privacy record | `Rules/CallRule.cs` |
| Battery, process list and windows | `Interop/Native.cs`, read through `Rules/Scene.cs` |
| Media controls | `Interop/MediaSessions.cs` |
| Sleep after the lock | `Actions/SleepAfterLock.cs` |
| Closing, opening and running programs | `Actions/CloseApps.cs`, `Actions/OpenApps.cs`, `Actions/RunProgram.cs` |
| Whether this copy has full administrator rights | `Platform/Elevation.cs` |
| Whether a path is on this PC | `Platform/LocalPaths.cs` |
| Screen readers (UI Automation) | `Interop/Automation.cs` |
| Explorer for the settings folder, and the boxes to save and load a settings file | `Ui/Settings/AdvancedTab.cs` |

## Text

The notes the welcome window shares with the tray keep their wording in `Text/English.cs` and
`Text/Arabic.cs`, with the list of keys in `Text/Strings.cs`. The welcome window itself, the settings
window, the tray and the corner notes use the version 1 lines in `Text/V1/` (`V1Text.g.cs`,
`EnglishV1.g.cs` and `ArabicV1.g.cs`), which are generated: the approved wording and the new lines
are in `tools/text/` (`approved.json`, `names.json` and `new/*.json`), and
`dotnet run tools/text/gen.cs` writes the three files. Never edit those by hand. A line no code uses
any more is marked `"excluded": true`, with a `"why"`, in `names.json` or in its `new/*.json` file,
and is left out.
`Text/V1Words.cs` and `Text/StringsV1.cs` put numbers and times into the lines, and
`Text/StatusText.cs` writes the status, the sentence under it and the why line. A new string is one
entry in a `new/*.json` file, with its English and its Arabic; the generator stops on a blank line
in either language.

## Other files under `src/`

| File | What it is |
| --- | --- |
| `SteppedOut.csproj` | The project: the name, the version, the single-file publish settings and the files built into the exe. |
| `app.manifest` | Runs as the signed-in user (no elevation prompt), for Windows 10 and 11, with per-monitor DPI and long paths. |
| `icon.ico` | The app icon, written by `tools/brand` from `Ui/Logo.cs`. |
| `Sounds/*.wav` | The fifteen sounds (`away-*`, `back-*`, `break-*`), written by `tools/sounds/gen.cs` and built into the exe. |

## Tools

| Folder | What it is |
| --- | --- |
| `tools/text/` | The text generator, described above. |
| `tools/sounds/` | `gen.cs` makes the fifteen sounds in `src/Sounds/` and, with `--page`, a listening page. `SoundCheck.cs` measures each (peak, loudness, attack, brightness) and fails a harsh, sudden or loud one; the tests compile it too, and `Directory.Build.props` adds it to `gen.cs`. |
| `tools/brand/` | Writes `src/icon.ico` and `docs/logo.png` from the app's own logo code (`--check` says whether they are up to date), and draws sheets to check them by eye. |
| `tools/shots/` | A harness that draws the windows without showing them on a screen, and without locking, muting or playing anything. It makes the README and Store pictures and checks for clipped text. `Program.cs` takes the parts to draw; `Demo.cs` writes the frames of the demo and `make_gif.py` turns them into GIFs; the other `*Sheet.cs` files draw one group of windows each, and `Check.cs` runs the behaviour checks. |
| `tools/store/` | The scripts that build the Microsoft Store package from the exe, the program that draws the Store logos, and the script that tries the package on one PC. |
| `store/` | The Store package's manifest template, and the listing text (`store/listing/`). |
| `docs/` | This file, the user guide (`guide.md`), `advanced.md` and `how-it-works.md`, which the README links to, the README logo and the pictures (`docs/shots/`). |
