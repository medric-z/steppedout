# SteppedOut for advanced users

[Back to the README](../README.md)

In the app, the detailed options are on the Extras tab.

## Command-line options

| Flag | Effect |
| --- | --- |
| `--dry-run` | Logs `WOULD LOCK`, and what your away settings would have done, instead of doing it. An app or script the live run would refuse or could not start, such as a file that is not there, is logged as `WOULD NOT open` or `WOULD NOT run` with the reason. It never changes Start with Windows or takes over its entry: it logs what it would do. The tooltip and the settings window say "(dry run)". |
| `--idle <minutes>` | The lock time for this run only, in place of `idleMinutes`, with the countdown inside it. Decimals work (`--idle 0.5`), from 0.05 to 1440. Extra time and the other rules still apply on top. |
| `--once` | Prints what would happen if you stepped away now, and why, then exits. Works while SteppedOut is running. |
| `--version`, `--help` | Prints the version, or the list of options, then exits. |

To see why it is or is not locking, run `--once` from PowerShell, using the name your copy has.
The `| Out-Host` makes PowerShell wait for the answer.

```
& "$env:LOCALAPPDATA\SteppedOut\SteppedOut.exe" --once | Out-Host
.\SteppedOut-win-x64.exe --once | Out-Host
```

The first line is for a copy installed with the PowerShell line. The second is for one downloaded by
hand, run from its folder. The first line is the version, with "(dry run)" when `--dry-run` is
given. A mistake in config.json, or a setting name it does not know, is listed next, on a line that
starts with `config:`. Then come the idle time ("idle: 5:33 of 15:00"), whether the session is
locked, what it would do when away (`when away: lock`, or `do not lock` when At the lock time is
Don't lock), one line for each rule, the lock time with what makes it up, and a `verdict:` line
saying what would happen. `--once` writes no file at all: it never rewrites an existing config.json,
never makes one when there is none, keeps no copy of it and writes no log. With a file from a
version before 1.0 it reports on the default settings and says so.

- A mistyped option, or an `--idle` value outside 0.05 to 1440 (use a dot for decimals), stops it
  from starting and says what was wrong, in the terminal or in a message box. Unlike config.json, the
  flag is not clamped.
- `--dry-run` and `--idle` only apply when it starts. If a copy is already running, a message says
  the options were not used; quit it from the menu first.
- `--once`, `--version` and `--help` work while it runs.

## The settings file

Settings live in `%APPDATA%\SteppedOut\config.json`, which is made with the defaults the first time
it runs. To open its folder, go to the Extras tab, Settings file and log, Open folder. The
settings window offers a short list of choices for each time and level, plus Custom… for typing a
number; the file takes any value in range, decimals included where a setting allows them. Changes
apply within a couple of seconds, without a restart.

When you edit it by hand:

- Comments and trailing commas are fine, but the settings window drops them the next time it saves.
- Names can be in any case. A name that is not a setting, usually a typo, is skipped: the log and
  `--once` name it, a note in the corner says so, and the rest of the file still applies.
- A value of the wrong kind in a setting added in 1.0, such as a word where a number belongs or an
  unknown choice, is skipped on its own: that setting keeps its default, the log names it, and the
  rest of the file still applies.
- A number outside its range (in brackets below) is moved to the nearest end of the range, and a
  decimal in a whole-number setting is rounded.
- In paths, double each backslash (`C:\\Tools\\away.ps1`) or use forward slashes.
- If the file does not parse, it is left as it is and not used. The log gives the line, a note says
  so, and the defaults apply until you fix it. If it broke while SteppedOut was running, the settings
  you had stay instead.

**Lock time.** A setting that changes the lock time while something is going on takes one of these
words. They are the file's spelling of the choices in the window.

| Word | Meaning |
| --- | --- |
| `"none"` | No change. |
| `"never"` | The PC does not lock while it lasts. |
| `"later 60"` | Add 60 minutes (1 to 1440) to the lock time while it lasts. |
| `"sooner 50"` | Lock 50% sooner (1 to 95). |
| `"always 10"` | Lock after 10 minutes (1 to 1440) at the latest, even while something else says never. |

| Key | Default | Meaning |
| --- | --- | --- |
| `configVersion` | 1 | Which version of the file this is. Leave it alone. |
| `language` | `"auto"` | Interface language: `auto` follows Windows, or `en` / `ar`. Picking a language in the window saves `en` or `ar`. |
| `idleMinutes` | 15 | The normal lock time, in minutes without real input, countdown included (0.05 to 1440) |
| `graceSeconds` | 15 | Length of the countdown; 0 locks with no countdown (0 to 600). It is never longer than half the lock time. |
| `lockWhenAway` | true | Lock the session when you leave. Off runs the away actions but never locks. |
| `whenLocking` | `"lock"` | What follows the lock SteppedOut makes by itself at the lock time: `lock`, `lockScreenOff` (the screens go off too) or `lockSleep` (the PC sleeps too). Lock now and the shortcut only lock. |
| `musicVideo` | `"never"` | While sound or video plays or an app keeps the display on: `none`, `never` or `later N` |
| `fullScreen` | `"never"` | While a window truly fills its screen: `none`, `never` or `later N` |
| `onCall` | `"never"` | While the microphone is in use: `none`, `never` or `later N` |
| `onBattery` | `"none"` | While the laptop is on battery: `none`, `sooner P` or `always N` |
| `appRules` | [] | One rule per app, see below |
| `countController` | true | Count a controller (Xbox-compatible) as using the PC |
| `mediaOnLock` | `"pauseUntilBack"` | What happens to what is playing when you leave: `pauseUntilBack` (Play again when you're back), `pauseStayPaused` (Keep it paused) or `keepPlaying` (Don't pause) |
| `muteWhenAway` | false | Mute the speakers while you are away |
| `screensOffWhenAway` | false | Turn the screens off when you leave |
| `soundWhenAway` | `"off"` | A short sound when the PC locks: `off`, `steppedOut`, `drift`, `pebble`, `glow`, `ripple` or `own`. `click` and `chime`, from earlier builds, read as `steppedOut` |
| `soundWhenAwayFile` | `""` | The .wav file for `own`, by its full path on this PC; kept when another sound is chosen |
| `openWhenAway` | [] | Apps to open when you leave: the full path of a `.exe` on this PC, or `shell:AppsFolder\\<app id>` for a Store app. Nothing on another PC opens. |
| `closeWhenAway` | [] | Program names (with or without `.exe`) to close when you leave |
| `runWhenAway` | `""` | A program, script or shortcut on this PC, by its full path, to start when you leave. A web address, a bare command or a path on another PC is refused and logged. |
| `showTimeAway` | true | Show a short "You were away …" note on return |
| `soundWhenBack` | `"off"` | A short sound when you are back after a lock, the same choices as `soundWhenAway` |
| `soundWhenBackFile` | `""` | The .wav file for `own` when you are back |
| `openWhenBack` | [] | Apps to open when you are back |
| `closeWhenBack` | [] | Program names to close when you are back |
| `runWhenBack` | `""` | A program, script or shortcut on this PC, by its full path, to start when you are back, with the same refusals |
| `breakReminders` | false (true on a new install) | Remind you to take a break. A file without it reads as false |
| `breakMinutes` | 60 (120 on a new install) | Time at the PC before a break reminder (1 to 720). The window offers 30, 45, 60, 90, 120 and 240, and Custom… from 5. |
| `breakSound` | `"off"` | A sound with break and eye break reminders: `off`, `steppedOut`, `dawn`, `meadow`, `lantern`, `tide` or `own`. `softChime`, from earlier builds, reads as `steppedOut` |
| `breakSoundFile` | `""` | The .wav file for `own` with reminders |
| `eyeBreaks` | false | Every 20 minutes of use, remind you to look into the distance |
| `snoozeMinutes` | 10 | Snooze time on a reminder, in minutes: 0 for no button (0 to 240). The window offers 5, 10, 15, 30 and 60, and Custom…. |
| `lockShortcut` | `""` | The lock shortcut, written like `"Ctrl+Alt+L"`; `""` turns it off. A value that is not a valid shortcut counts as off. |
| `theme` | `"system"` | `system`, `light` or `dark` |
| `textSize` | `"system"` | `system`, `small`, `medium` or `large` |
| `minutesOnTrayIcon` | false | Draw the minutes left on the tray icon |
| `notificationPlace` | not written | `bottomRight`, `bottomLeft`, `topRight`, `topLeft` or `center`. Missing means bottom left when Windows is laid out right to left and bottom right otherwise; it is only written once you pick a place. |
| `settingsTab` | 0 | The tab the settings window opens on, the one used last: 0 Locking, 1 Away and back, 2 Appearance, 3 Breaks, 4 Extras (0 to 4) |
| `jitterFilter` | true | Ignore mouse movements too small to be a hand |
| `jitterPixels` | 5 | A movement below this many pixels within one second does not count as input (1 to 100). The window offers 3, 5 and 10, and Custom… up to 50. |
| `audioPeakThreshold` | 0.01 | Peak level, 0 to 1, that counts as sound (0.001 to 1). The window offers 1%, 3% and 10% (0.01, 0.03 and 0.1), and Custom… from 1% to 100%. |
| `audioSilenceSeconds` | 30 | Quiet time before audio counts as stopped, so a quiet moment does not end a video (0 to 600). The window offers 10, 30 and 60, and Custom… from 5 to 600. |
| `logMaxKilobytes` | 512 | Log size before it rolls over (64 to 65536). The window offers 256, 512, 1024 and 4096, and Custom…. |

The file also carries a `welcomed` flag, which records that the first-run window has been shown;
leave it alone. `buttonColor`, which earlier builds wrote, is no longer a setting: a file that still
has it loads with no note about it, and the next save leaves it out.

An `appRules` entry looks like this. `app` is the program name; `obs64`, `obs64.exe` and a full path
all name the same program, and a second rule for the same program is dropped. `effect` is `never`,
`later N`, `sooner P` or `always N`. `when` is `running` (the app is open at all) or `onScreen` (one
of its windows can be seen on a screen and is not minimized). `path` and `aumid` are optional; the
app picker fills them in so the window can show the app's icon, and they do not change what matches.

```
"appRules": [
  { "app": "vlc", "effect": "later 30", "when": "onScreen" }
]
```

### Files from before 1.0

A file with no `configVersion`, or one below 1, is from a version before 1.0, and nothing in it is
read. The copy that runs in the tray keeps it as `config.json.before-1.0`, with the date and time
added when that name is taken by a different file or a folder (see [Privacy](../README.md#privacy)), and writes a new file
with the defaults and `welcomed` false, so the welcome window opens. `--once`, `--help` and a second
start leave it byte for byte as it is and use the defaults for that run. A copy or a write that fails
is written to the log and the defaults still load; a file that could not be copied is never written
over. Any later save, from the settings window or the welcome, keeps such a file the same way first.

A file from before 1.0 put back while SteppedOut runs, by an older copy for example, is kept the same
way, and the settings in use are written back over it: they never start over part way through a
session, and they are applied once. The names that only earlier versions used (`whilePlaying`,
`whileFullscreen`, `whileOnCall`, `moreTimeMinutes`, `neverLockWhileRunning`, `pauseMedia`,
`breakReminderMinutes` and 0.1's `suppressWhile…` switches) mean nothing in a 1.0 file: like any name
it does not know, each is listed in the log as unknown and left out at the next save. A
`configVersion` that is not a number is a mistake in a 1.0 file, not a sign of an old one: it is
skipped with a warning and the rest of the file loads.

Going back to an older version needs `config.json.before-1.0` copied over `config.json` first: an
older version reading a 1.0 file falls back to its defaults for its own settings and, if it saves,
drops every new one.

## The log

Everything it does is written to `%APPDATA%\SteppedOut\SteppedOut.log`, capped at 512 KB by default
(Log size on the Extras tab, or `logMaxKilobytes`) with one older generation kept. If another program holds the log open so it
cannot roll over, lines keep being added past the cap until it can. Each time it starts, it first
writes a line with the config version or, when it finds a file from before 1.0, one saying under
which name the old file was kept and that the settings start fresh. When it starts
in the tray, a line follows with the version and the commit the exe was built from, the lock time
and warning, the Windows build, the app's and the processor's architecture, the display language and
the path of the settings file, so a log sent from another PC explains itself. Whenever the lock time
or what makes it up changes, it writes one line with the idle time, the lock time and what is behind
it: `idle 0:12: lock time 90 min (30 + 60 music or video); sound from spotify (later 60)`. Times in
the log read minutes:seconds, or hours:minutes:seconds from one hour on, so `0:12` is 12 seconds.

An error that comes back again and again, such as one in the once-a-second check, is written in full
the first time and then only counted: one short line an hour while it keeps coming, and a line with
the count once it has stayed away for a minute. When that check has failed 10 times without 10 good
checks in a row between the failures, a note in the corner says "Locking might not work right now",
with "SteppedOut keeps running into an error. The log in the settings folder says what it is.
Quitting SteppedOut and starting it again may help." under it. It stays until the check has worked 10
times in a row, or you click it away. A countdown or another note may take its place for a while; it
comes back when that is over.

## Why the file is about 100 MB

The file is about 100 MB because it carries its own copy of the .NET runtime, so nothing else needs
installing. It is stored uncompressed on purpose: a compressed exe is half the size, but it unpacks
itself into memory at every start and keeps it there, about 40 MB more for as long as it runs.
