# Using SteppedOut

[Back to the README](../README.md)

## The padlock and its menu

The padlock sits by the clock, or under the ^ arrow beside it until you drag it out. Left-click it
for the settings window; right-click it for the menu. It has three looks: on, paused (grey) and
counting down to a lock (brighter). Hovering over it shows "SteppedOut · " and then the status
headline that tops the menu, such as "SteppedOut · On · locks in 28 min"; a headline too long for the
tooltip is cut with …. With Show minutes left on the taskbar icon on, the minutes (1 to 99) are drawn
on the icon too.

The menu opens with a small box showing the ring, the status ("On · locks in 28 min") and "Away
today". Below it:

- **Snooze this reminder** is there only while a break or eye-break note offers Snooze. The note
  never takes the keyboard, so this row is how the keyboard and a screen reader reach its Snooze.
- **Pause** opens 15 minutes, 30 minutes, 1 hour, 2 hours, 1 day, Until turned back on and Custom….
  Custom… opens a small Pause for window: type a number, pick minutes, hours or days, and click
  Pause. It takes 1 minute to 7 days; a number outside that is changed to the nearest end, and the
  window says so. While it is paused, Pause becomes Turn back on. A pause runs on through sleep and
  ends at the time it said; quitting, signing out or restarting ends it.
- **Lock now** locks at once, with the shortcut written beside it while a shortcut is on.
- **Settings** opens the settings window.
- **Quit SteppedOut** closes it.

There is no language entry in the menu. The language is a switch at the top right of the settings
window.

Until you close the welcome window, clicking the padlock brings it back to the front. Opening
SteppedOut again while it runs does the same, or opens the settings once the welcome window is
closed.

## The settings window

Click the padlock to open the settings window. Changes save as you make them, so there is no Save
button. At the top right, English and العربية switch the language at once. In Arabic the whole
window is mirrored to read from right to left.

**The status box** shows what SteppedOut is doing now.

- A ring that fills as the idle time passes, full at the lock, with the logo in the middle.
- A headline such as "On · locks in 28 min", "On · won't lock during a call", "Paused · back on at
  5:05 PM" (or "tomorrow at 5:05 PM", or the day, such as "Saturday at 5:05 PM", and the date for a
  pause of a whole week), "Paused · until turned back on", "Locking off · away actions in 28 min",
  "You're away" or "The PC is locked". A run started with `--dry-run` adds "(dry run)". A headline too long for one
  line, as when it names an app with a long name, is cut with … and shown whole in a tip.
- A sentence under it, "The PC locks if you don't use the mouse or keyboard for 30 min". It always
  names the normal lock time.
- When something changes the lock time, one line in green says what: "30 min + 1 hr
  (music or video) + 2 hr (full screen)", "30 min, 50% sooner on battery" or "Always locks after
  10 min on battery".
- Two small boxes, Away today and Locked today. Locked today counts every lock while SteppedOut runs,
  including Win+L, but never a dry run's "would lock". Away today adds the locked time and any time
  away with locking turned off. Both start again at midnight.
- Pause, with the same choices as the menu, which becomes Turn back on while paused, and Lock now.

**The main list** holds the seven choices most people need, in this order:

| Row | What it does |
| --- | --- |
| Lock the PC after | The normal lock time: 5, 10, 15 or 30 minutes, 1 hour, or Custom… for any whole number of minutes from 1 to 1440. |
| While music or video is playing | Time added to the lock time while sound or video plays: No extra time, +15 minutes, +30 minutes, +1 hour, +2 hours, Never lock, or Custom…. |
| While you're on a call | The same list, for as long as any app is using your microphone. |
| While an app is full screen | The same list, for as long as a window fills the whole screen. |
| Warn me before it locks | How long the countdown lasts: Off, 10 seconds, 15 seconds, 30 seconds, 1 minute or Custom…. Try it shows the countdown for 5 seconds without locking, and is greyed out when the warning is Off. |
| Pause music and video when you leave | Play again when you're back, Keep it paused, or Don't pause. It works for apps that show in Windows' media controls; see [While you are away](how-it-works.md#while-you-are-away). |
| Start with Windows | A switch. Starts SteppedOut each time you sign in. |

A value that is not in a list, such as one typed into the settings file, shows as itself.

**Five tabs** hold the rest, each with a short line under its name. The window opens on the tab you
used last, or on Locking the first time.

- **Locking** (Lock time, battery, app rules, shortcut):
  - *On battery power* gives the laptop its own lock time while it is not plugged in: Lock as usual,
    Lock sooner by 25%, 50% or 75% (the menu shows the minutes it comes to, such as "50% sooner
    (15 min)"), or Fixed time, Always lock after 2, 5, 10, 15 or 30 minutes or 1 hour, or Custom….
    Always lock after overrides Never lock. On a PC with no battery the row is hidden, and the
    setting stays as it was.
  - *At the lock time*: Lock the PC (the default), Lock and turn off the screen, Lock and sleep, or
    Don't lock. The first three apply only when SteppedOut locks the PC by itself; see
    [Locking](how-it-works.md#locking). Don't lock turns locking off while leaving the rest on: the countdown still
    runs and the away actions still start, but the PC does not lock. The away actions run either way.
  - *Count a controller as using the PC* is on by default. See
    [Game controllers](#game-controllers).
  - *App rules* lock sooner, later or never while a specific app is open. Add app lists the apps
    running now, or Choose another app… opens a file box for an `.exe`. Each rule has a Locking menu
    (Never lock; Wait longer: +15 minutes, +30 minutes, +1 hour, +2 hours or Custom…; Lock sooner by
    25%, 50% or 75%; or Always lock after a time) and an Applies when menu: Open, even in background, or Only when on
    screen, meaning any window you can see, on any screen, not minimized. A window behind other
    windows still counts. The list is empty until you add an app, and each app has one rule.
  - *Keyboard shortcut to lock now*: Ctrl+Alt+L, Off (the default), or Choose another shortcut….
    See [The lock shortcut](#the-lock-shortcut).
  - *Why isn't the PC locking?* with Show. See [Why isn't the PC locking?](#why-isnt-the-pc-locking).
- **Away and back** (Pause, mute, screen, sounds, apps): the actions in
  [When you leave and come back](#when-you-leave-and-come-back). **Show how long you were away** is
  on from the start; the rest are off until you turn them on.
- **Appearance** (Theme, text size, icon, notifications):
  - *Light or dark mode*: Light, Dark or System.
  - *Text size*: Small (90%), Medium (100%), Large (120%) or System, which follows the text size set
    in Windows.
  - *Show minutes left on the taskbar icon*.
  - *Where notifications appear*: Bottom right, Bottom left, Top right, Top left or Center. Until
    you pick, the corner notes keep to the side the Windows taskbar clock is on: bottom left when
    Windows is laid out right to left, bottom right otherwise. Center is the middle of the screen the
    pointer is on.

  All of it applies at once, without a restart. Buttons, switches and the ring are always
  SteppedOut's green; there is no color to pick. In Windows high contrast mode the colors are
  Windows' own, whatever is chosen here.
- **Breaks** (Break and eye reminders, snooze): *Break reminders*, *Show a reminder after*,
  *Reminder sound*, *Eye breaks (20-20-20)* and *Snooze button*. See
  [Break reminders and eye breaks](#break-reminders-and-eye-breaks).
- **Extras** (Audio, mouse, log, reset), in two parts:
  - *Mouse and audio*, what counts as using the PC:
    - *Ignore tiny mouse movements*: Off, Under 3 pixels, Under 5 pixels (the default), Under 10
      pixels, or Custom… for 1 to 50 pixels. See [The jitter filter](how-it-works.md#the-jitter-filter).
    - *How loud sound must be to count*: Any sound, even if it's very quiet (1% of full volume, the default),
      Normal volume or louder (3%), Only loud sound (10%), or Custom… for 1% to 100%.
    - *Silence before it counts as stopped*: 10 seconds, 30 seconds (the default), 1 minute, or
      Custom… for 5 to 600 seconds.

    Change the two audio rows only if quiet background sound keeps the PC from locking on time, or
    a short silence ends the extra time too soon.
  - *Settings and log*:
    - *Settings file and log*, with Open folder.
    - *Log size*: 256 KB, 512 KB (the default), 1 MB, 4 MB, or Custom… for 64 to 65536 KB. At that
      size the log starts over, keeping one older copy.
    - *Save and load settings*: Save to file… writes the settings to a `.json` file you choose. Load
      from file… reads one, checks it the same way as `config.json`, and uses it; a file that can't
      be used is refused with the reason, and settings in it that can't be read go back to their
      defaults, with a line that says so. Start with Windows is not in the file.
    - *Show the welcome again* opens the welcome window.
    - *Reset all settings*: Reset… asks first, then puts every setting back to how it was when you
      first started SteppedOut. The language stays, and so does Start with Windows.
    - *Remove SteppedOut's files*: Remove… asks first, then turns off Start with Windows, quits, and
      deletes the files SteppedOut wrote in its settings folder (see [Privacy](../README.md#privacy)), each by
      its own name, and the folder once it is empty. It can't be undone. The exe stays.

The footer says "Changes are saved automatically." and shows the version. When the saved words no
longer fit beside the version, at larger text sizes, the version moves to its own line.

If Windows refuses a save, because another program has `config.json` open or its folder cannot be
written to, the new settings stay in use until SteppedOut quits. The footer then says "Changes
couldn't be saved. They're used until SteppedOut quits." and a note, "Settings couldn't be saved",
shows once; the log in the settings folder says why. The next save that works takes both back. If
`config.json` is changed outside SteppedOut while a save is failing, the settings in the file replace
the ones that could not be saved, and a note says "Your unsaved changes were replaced".

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="shots/settings-en-dark.png">
  <img src="shots/settings-en-light.png" width="560" alt="The settings window with example settings: the language switch at the top, the status box with the ring, the headline, Pause and Lock now, then the seven main choices from Lock the PC after to Start with Windows, then the tabs Locking, Away and back, Appearance, Breaks and Extras with the Locking tab open">
</picture>

## The countdown

The countdown is part of the lock time, so the PC locks at exactly the time the status shows. It
lasts for the warning you chose (15 seconds by default) or half the lock time, whichever is shorter,
and it starts that long before the lock. A small note in the corner reads "Locking in 15 sec · move
the mouse or press a key to cancel", with a thin line running down. Moving the mouse or pressing a
key cancels it, and it never takes the keyboard focus. Like every note in the corner, it has a thin
grey edge, with only the time, the line and the logo in green.

Four cases to know:

- If something that adds time starts while the countdown runs, such as music, the countdown stops,
  and the lock moves to the later time. (Only a warning of several minutes with a short extra can
  leave the lock time still inside the countdown; then it runs on and ends at the new lock time.)
  Pausing from the menu stops it too.
- If the lock time falls below the idle time already reached, because a call ended after a long
  stretch, the laptop was unplugged or a pause ended, the countdown still runs its whole length
  first. With a warning set, it never locks without one.
- With At the lock time set to Don't lock, it counts down "Away actions in 15 sec · move the mouse or
  press a key to cancel", then starts your away settings without locking.
- When the PC wakes from sleep or hibernation, the lock time starts over from zero, as if you had
  just moved the mouse, and the next countdown starts a warning's length before that new lock time.
  A countdown that was running when it went to sleep ends quietly, with no lock and no away actions.
  A sleep from ordinary use is not time away. A PC that was already locked, or away, when it went to
  sleep stays so, and the sleep counts as time away. Windows can also lock the session itself as the
  PC goes to sleep; a lock that began up to 30 seconds before the sleep counts as a lock, with its
  time away counted from the wake. It runs no away actions, since SteppedOut did not decide you had
  left. [The idle clock](how-it-works.md#the-idle-clock) says how the wake is noticed.

Warn me before it locks set to Off locks at the lock time with no countdown. The window's Try it
button shows the countdown for 5 seconds, "Locking in 5 sec · preview only, the PC won't lock", and
never locks.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="shots/countdown-en-dark.png">
  <img src="shots/countdown-en-light.png" alt="The countdown note in the corner: Locking in 9 sec, move the mouse or press a key to cancel, with a thin line running along its bottom edge.">
</picture>

## When you leave and come back

On the Away and back tab, **when you're away**:

- **Mute the PC.** The sound comes back when you do, and also after a restart or sign-out. If
  SteppedOut itself stopped while you were away, it unmutes the next time it starts.
- **Turn off the screen.** Any key or mouse movement turns it back on.
- **Play a sound**: Off, SteppedOut, Drift, Pebble, Glow or Ripple, soft sounds of SteppedOut's own,
  or Your own sound…, a .wav file on this PC (up to 5 MB and 10 seconds). It plays only when the PC
  actually locks; picking one in the list plays it once so you can hear it. If Mute the PC is on,
  the sound plays first and the mute starts after it.
- **Open apps** and **Close apps**: lists of apps. See [Open apps and Close apps](#open-apps-and-close-apps).

When **you're back**:

- **Show how long you were away** shows a note, "You were away 42 min", when you were away a minute
  or more; it goes away by itself. It adds "· music or video is playing again" when music or video
  that was paused is playing again.
- **Play a sound**, **Open apps** and **Close apps**, as above, for when you unlock the PC.

Scripts have their own two rows: **Run a script when you're away** and **Run a script when you're
back**. Choose… picks a `.bat`, `.cmd`, PowerShell (`.ps1`) or shortcut file; Remove clears it. An
`.exe` chosen earlier still works. Only a file on this PC runs, by its full path. Choose… does not
take one on another PC (a network path, or a mapped network drive) or of another kind: the row
keeps the script it had and says "That script is on another PC. Choose one saved on this PC." or
"SteppedOut can't run that file. Choose a script or shortcut saved on this PC." A web address, a bare
command or a path of that kind written into `config.json` is refused when the time comes, and the log
says why. A `.ps1` runs in PowerShell with no window, with the execution policy set aside for that
run unless it is AllSigned, which is kept; a policy set by Group Policy applies whatever is asked.

The main list's **Pause music and video when you leave** picks what happens to what is playing:
Play again when you're back (pauses it and plays the same things again when you are back), Keep it
paused, or Don't pause. Only what was actually playing is touched, and anything you had already
paused stays paused. It works for apps that show in Windows' media controls; an app that does not
show there keeps playing.

When you leave, the actions run in this order: the away sound, pause media, mute, turn off the screen,
close apps, open apps, run the script. When you are back: unmute, the back sound, play media again,
close apps, open apps, run the script. The back sound only plays after a lock.

These happen only when SteppedOut decides you have left, or when you use Lock now or the lock
shortcut. A lock you make yourself, with Win+L or Dynamic lock, does none of them.

### Open apps and Close apps

**Open apps** starts the apps on its list: a program (`.exe`) by its full path, or a Store app.
Nothing else opens: no web addresses, no commands with arguments, nothing on another PC (no network
paths, no programs on a mapped network drive). Add app and Choose another app… do not take such a
program either: the row says "That app is on another PC. Choose one installed on this PC." A script
goes in the script row.

A copy that was started with administrator rights, as with Run as administrator or from an admin
terminal, opens no app and runs no script, because those lists are in `config.json`, which any
program of the sign-in can write. The log says so when it starts and each time one is refused.

**Close apps** can lose work, so it works the way a person would, and it closes only the apps on its
list:

- It asks each listed app to close: each window Windows shows for any of its processes, minimized or
  on another desktop too, gets the request its close button would send, so each document gets its
  Save prompt. Then it waits up to 30 seconds. Only then does it force the processes of listed apps
  that are still running. A process with no such window to ask, such as an app that only sits in the
  notification area, is forced at the end of the wait. So is an app that only hides there when asked
  to close, as many chat apps and game launchers do, since it is still running then.
- Programs a listed app started are left alone: a game started from a launcher, an installer or a
  document opened from a browser's downloads, an attachment opened from a mail app. SteppedOut never
  asks them to close and never forces them. What the listed app does with them when it is asked to
  close is up to that app.
- If you come back within those 30 seconds, nothing is forced. It works the other way too: leaving
  again within 30 seconds of coming back calls off the force of Close apps for when you're back.
- It matches by program name, in your own sign-in only. It never closes SteppedOut (by process and by
  program file, whatever the file is called), any process of a program SteppedOut was started from
  (such as the browser it was opened from), Explorer or Windows' own processes.
- It leaves apps running as administrator open, because Windows does not let an ordinary program
  ask or force them. It does the same with an app whose rights it cannot read, because it cannot tell
  whether the app is one of those. The log says which, and which app.
- An app on both lists is closed first and opened once the close is over. A Store app is the
  exception: it is opened without waiting.
- A dry run only logs what it would close.

Above both Close apps lists the tab says what this means for your work: "SteppedOut can't save your
work. Each app is asked to close first, as if you clicked its X, and it's up to the app whether
unsaved work is kept. Apps still open after 30 seconds are closed anyway." A screen reader hears it
once, on the list.

## Break reminders and eye breaks

Break reminders are on, every 2 hours, on a new install. A settings file that is already there
keeps its own choice, and one that does not mention them has them off. Eye breaks are off by default.
Neither ever locks the PC. A small note appears in the corner and goes away by itself.

- **Break reminders**: after you have been at the PC for the time in Show a reminder after (2 hours
  on a new install), "Time for a break · 2 hr at the PC". Show a reminder after offers
  30 minutes, 45 minutes, 1 hour, 1 hour 30 minutes, 2 hours, 4 hours, or Custom… for 5 to 720
  minutes. Going 3 minutes without input
  starts the stretch again; if you carry on instead, the reminder comes again after the same time.
  **Reminder sound** is Off, SteppedOut, Dawn, Meadow, Lantern, Tide or Your own sound….
- **Eye breaks (20-20-20)**: after 20 minutes of use, a note says "Time to rest your eyes", with
  "Look at something about 6 meters away for 20 seconds" under it. A minute without input starts the
  20 minutes again.
- If both are due at once, only the break reminder shows.
- **Snooze button**: Off, 5 minutes later, 10 minutes later (the default), 15 minutes later, 30
  minutes later, 1 hour later, or Custom… for 1 to 240 minutes. When it is on, each
  note has a Snooze button that brings that reminder back after that time. While a note offers
  Snooze, the tray menu also has Snooze this reminder, which does the same.

A lock, being away, or the PC sleeping counts as a break: a sleep or hibernate leaves a gap with no
ticks, and both reminders treat it as time away. A reminder never shows during a call, while paused,
while the countdown is up, or while Windows holds notifications back, as when an app is full screen,
in a presentation, or with Do not disturb. It waits and shows afterwards.

## The lock shortcut

A keyboard shortcut can lock the PC from any app, the same as Lock now, and run your away actions,
like muting. It is off until you turn it on: Keyboard shortcut to lock now, on the Locking tab or in
the welcome window, offers Ctrl+Alt+L or Choose another shortcut…, which takes Ctrl or Alt or both,
optionally Shift, and one letter, number or F key. The Windows key is never part of it.

Choose another shortcut… opens a small window: press the keys you want, and Save turns on once they
make a shortcut that works on every keyboard layout you have installed. Alt+F4 and anything with the
Windows key are never accepted. While that window is open SteppedOut lets go of the current shortcut,
so pressing it there types it into the box instead of locking the PC; it is put back when the window
closes without a new one. If another app already has the new shortcut, the window says so and stays
open.

It uses `RegisterHotKey`, so Windows tells SteppedOut only that exactly that combination was
pressed. It is not a keyboard hook and sees no other key. The first time SteppedOut runs, a
shortcut picked in the welcome window is taken only once the window is finished, by Done, Open
settings or closing the window; quitting before that leaves it off until the next start.

- If another app already has the shortcut, the row says "Another app is using Ctrl+Alt+L, so it
  doesn't work right now".
- If the shortcut types a letter on any keyboard layout you have installed, SteppedOut does not
  register it, so typing is never blocked. Ctrl+Alt is AltGr on some layouts, and Ctrl+Alt+L types ł
  on a Polish keyboard, for one. The row says "Off because Ctrl+Alt+L types a letter on one of your
  keyboards". The layouts are checked again every second and each time the settings are applied,
  since you can add a layout at any time.

## Game controllers

Windows does not count a game controller as using the PC, so a long game with only a controller
would run the idle clock out. With Count a controller as using the PC on (the default),
SteppedOut asks Windows for the state of each connected controller four times a second (empty slots
every 3 seconds) and counts real use as activity: a button pressed or let go, a stick pushed past
its dead zone and moving, or a trigger pulled. A stick that only drifts does not count, nor does a
trigger resting part-pulled that only wobbles, as under a controller lying on it, and neither does a
controller that is merely connected.

It reads Xbox-compatible controllers (the XInput interface Windows provides). A PlayStation or other
pad shows up only through Steam or a similar program that presents it as an Xbox controller. This is
polling the state of the controller; it is not raw input and records nothing about which button.
Controller activity does not go through the mouse jitter filter.

## Why isn't the PC locking?

The Show button on the Locking tab opens a small window that lists what is delaying or stopping the lock
right now and refreshes every second. At the top it shows the lock time ("Lock time right now: 1 hr
30 min", or "Lock time right now: never"), then one row for each thing going on, with what it does
to the lock time at the right end: "Sound is playing in spotify" with "+1 hour", "An app is full
screen" with "Never lock", "The PC is on battery power" with "50% sooner". A row that changes
nothing says "No change", or "No change right now" when it is not counted at the moment. Sound from
the app you are on a call in shows as "Counted as the call". With nothing going on it says "Nothing
is delaying or stopping the lock right now." While the PC is paused, away, locked or has locking off,
the top line says that instead, since then it is the reason. Close or Esc closes it. `--once` prints
the same information in a terminal.
