# steppedout

[![build](https://github.com/medric-z/steppedout/actions/workflows/build.yml/badge.svg)](https://github.com/medric-z/steppedout/actions/workflows/build.yml)

Locks your Windows session (the Win+L state) after a period of real inactivity. The PC stays
awake: downloads, renders and servers keep running. Only the session locks.

## What Windows already has

Windows can lock a session on its own in a few ways.

| Method | Where | What it costs |
| --- | --- | --- |
| Screen saver with "On resume, display logon screen" | Screen saver settings | Fires on input silence alone. No warning, no exceptions. |
| "Interactive logon: Machine inactivity limit" | Local Security Policy (Home editions have no policy editor; the registry value behind it can be set by hand) | Administrator rights, a restart, and a screen saver should be active. Input silence alone. |
| "Require sign-in when waking from sleep" | Settings, Accounts, Sign-in options | Only locks when the PC comes back from sleep. A machine that never sleeps never locks. |
| Dynamic lock | Settings, Accounts, Sign-in options | Needs a Bluetooth-paired phone. Reacts to the phone leaving, not to you. |

The first three measure the same thing, the time since the last keyboard or mouse event, and that is
not the same as you being away. A film with nobody typing looks like an empty chair. A mouse with a
twitchy sensor looks like an occupied one. None of them warn before locking, none can be told "not
while the render is running", and none show how much time is left.

This tool watches the same clock and adds judgment: a countdown you can cancel, rules that hold the
lock while something is actually happening, and a filter for mouse sensors that never quite sit
still. It needs no administrator rights, writes one registry value only if you ask it to start with
Windows, and makes no network connections.

## Install

1. Download `SteppedOut-win-x64.exe` (or `-win-arm64`) from the
   [latest release](https://github.com/medric-z/steppedout/releases/latest) and put it anywhere.
2. Run it. It appears as a padlock in the notification area and has no window. Right-click for the
   menu, double-click for settings.
3. Tick "Start with Windows" in the menu if you want it at every sign-in.

The exe is not code-signed, so SmartScreen shows "Windows protected your PC" on first run. Click
"More info", then "Run anyway", or verify the file first (see below). The file is about 47 MB
because it carries its own copy of the .NET runtime; nothing is installed.

Windows 10 and 11, x64 or ARM64. The ARM64 build comes out of CI and has not been tested on an ARM
machine.

To remove it: untick "Start with Windows", quit from the menu, delete the exe and the
`%APPDATA%\SteppedOut` folder.

## Using it

The tray menu: Pause / Resume, Pause for 1 or 4 hours, Lock now, Settings, Open config folder,
Start with Windows, Quit. The tooltip shows the time until the lock, or why it is being held off.

Fifteen seconds before locking (by default) a small box appears in the corner: "Locking in 15 s".
Any real input cancels it.

| Flag | Effect |
| --- | --- |
| `--dry-run` | Logs `WOULD LOCK` instead of locking. Run it this way for a day first. |
| `--idle <minutes>` | Idle threshold for this run, overriding the config file. Decimals work: `--idle 0.5`. |
| `--once` | Evaluates every rule once, prints the verdict and exits. Run it from a terminal to see why it is or is not locking; in PowerShell pipe it (`.\SteppedOut.exe --once \| Out-Host`) so the shell waits for the output. |
| `--version`, `--help` | |

Everything it does is written to `%APPDATA%\SteppedOut\SteppedOut.log`, capped at 512 KB with one
older generation kept.

## Configuration

`%APPDATA%\SteppedOut\config.json`, created with defaults on first run. Edit it by hand (comments
and trailing commas are tolerated) or through the settings window; changes apply within a couple of
seconds, no restart.

| Key | Default | Meaning |
| --- | --- | --- |
| `idleMinutes` | 15 | Minutes without real input before the countdown starts |
| `graceSeconds` | 15 | Length of the countdown; 0 locks at once |
| `jitterFilter` | true | Ignore mouse movements too small to be a hand |
| `jitterPixels` | 5 | A movement below this many pixels within one second does not count as input |
| `suppressWhileAudioPlaying` | true | Hold the lock while any program is producing sound |
| `audioPeakThreshold` | 0.01 | Peak level, 0 to 1, that counts as sound |
| `audioSilenceSeconds` | 30 | Quiet time before audio counts as stopped, so a silent scene does not end a film |
| `suppressWhileFullscreen` | true | Hold the lock while the foreground window covers its whole monitor |
| `suppressWhileDisplayRequested` | true | Hold the lock while an app has asked Windows to keep the display on |
| `neverLockWhileRunning` | [] | Process names, with or without `.exe`, that hold the lock while running |
| `logMaxKilobytes` | 512 | Log size before it rolls over |

## How it works

### The idle clock

Once a second the tool asks Windows for the time of the last keyboard or mouse event
(`GetLastInputInfo`). That call returns a timestamp and nothing else: not which key, not where the
mouse went. There is no keyboard hook, no raw-input registration and no clipboard access anywhere
in the code, so the tool cannot see what you type even by accident.

### The jitter filter

A mouse sensor that twitches resets Windows' idle clock forever, which is why the built-in locks
never fire for some people. The tool cannot ask Windows what kind of input occurred, so it reasons
from the only thing it can read without observing content, the cursor position, sampled ten times
a second:

- cursor moved 0 px, yet input was reported: keyboard, wheel or click, none of which a sensor can
  fake. Counts.
- cursor moved 1 to 4 px: sensor twitch. Ignored.
- cursor moved 5 px or more, or the foreground window changed: a hand. Counts.

This is deliberately imperfect. A key pressed in the same second as a 2 px twitch is discarded with
the twitch; the next key counts. A sensor that never moves the cursor a full pixel is
indistinguishable from typing and keeps the session unlocked. Both are accepted costs of not
reading keystrokes. Turn the filter off with `"jitterFilter": false` if it gets in your way.

### The rules

Every second, each enabled rule is asked whether locking should be held off. Once the clock passes
the threshold, the first one that says so wins, and its reason shows in the tooltip and the log.

**Audio.** Any active audio session on any output device whose peak level is above
`audioPeakThreshold`, read through the Core Audio session API (`Wasapi.cs`, declared by hand). The
Windows system-sounds session is skipped, so a notification ding does not count. A session counts
as playing until it has been quiet for `audioSilenceSeconds`. The cleaner signal
would be the power requests that players register, the ones `powercfg /requests` lists, but reading
those needs administrator rights, so the tool listens for sound instead. The cost: background music
counts too. If you leave a player running when you walk away, turn this rule off.

**Fullscreen.** The foreground window's visible bounds, taken from DWM so the invisible resize
borders are excluded, cover its whole monitor. The desktop and the taskbar are excluded. A maximized
window stops at the taskbar and does not count, unless the taskbar is set to hide, in which case the
two cannot be told apart and the tool errs on not locking.

**Display requested.** Video players, browsers playing video and presentation apps ask Windows to
keep the display on. Windows folds every such request into one system-wide flag that can be read
without administrator rights; the per-process list needs elevation, so the tool only knows that
someone asked, not who.

**Processes.** Names listed in `neverLockWhileRunning`, checked every few seconds.

### Locking

The lock itself is `LockWorkStation`, the same call Win+L makes. It returns as soon as the request
is queued, not when the lock is in place, so the tool listens for the session-lock notification
instead of trusting the return value. On unlock it resets its own idle clock: a face or fingerprint
unlock may produce no keyboard or mouse event, and without that reset the tool could lock again
seconds after you sign in. If it starts while the session is already locked, it waits for the
unlock.

### What it never does

No network connections of any kind: no telemetry, no update check. No keyboard hooks, no raw input,
no clipboard. No administrator rights; the manifest asks for none. No registry writes except the
`HKCU\...\Run` value for "Start with Windows", and only when you tick it. The log never contains
window titles or anything typed. `Native.cs` and `Wasapi.cs` hold every Windows API declaration; the
registry value, the session-lock notification, the process list and the Explorer launch go through
the .NET wrappers in `Autostart.cs`, `Watcher.cs`, `ProcessRule.cs` and `TrayApp.cs`.

## Verifying the binary

Every release is built by `.github/workflows/release.yml` and carries a build provenance
attestation. With the [GitHub CLI](https://cli.github.com/):

```
gh attestation verify SteppedOut-win-x64.exe --repo medric-z/steppedout
```

That confirms the file was built by that workflow, from this repository, at the tagged commit.
`SHA256SUMS.txt` in the release lists the hashes. Or build it yourself.

## Building from source

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet build src/SteppedOut.csproj -c Release
dotnet test tests/SteppedOut.Tests.csproj
dotnet publish src/SteppedOut.csproj -c Release -r win-x64 -o publish
```

The publish step produces one self-contained exe. Add `--no-self-contained` for a 265 KB exe that
needs the .NET Desktop Runtime installed instead.

## Licence

MIT.
