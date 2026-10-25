# How SteppedOut works

[Back to the README](../README.md)

## The idle clock

Once a second the tool asks Windows for the time of the last keyboard or mouse event
(`GetLastInputInfo`). Its timer runs eight times a second while the session is unlocked, to sample
the cursor (see [The jitter filter](#the-jitter-filter)), and once a second while it is locked. That call returns a timestamp and nothing else: not which key, not where the
mouse went. There is no keyboard hook, no raw-input registration and no clipboard access anywhere
in the code, so the tool cannot see what you type even by accident. (The lock shortcut and the game
controller are covered in [The lock shortcut](guide.md#the-lock-shortcut) and
[Game controllers](guide.md#game-controllers).)

Windows' idle time keeps counting through sleep and hibernation, so on waking it would look as if
nobody had touched the PC for the whole sleep. SteppedOut notices the wake in two ways: Windows'
resume notification, and a gap of 30 seconds or more between its once-a-second checks. A busy PC
delays a check by a few seconds at most, and Windows calls a window Not responding after 5, so 30
seconds is a gap only a sleep or a stopped PC makes; a short sleep that Windows does not report can
bring a lock at most 30 seconds sooner. From the wake, the idle time counts from zero until the next
input. A lock or an away from before the sleep is left as it is, so Away today counts a sleep only
when it falls inside one of those; a sleep from an ordinary stretch of use or a countdown is not
time away, and the away time after it starts at the wake. Windows can also lock the session itself as
the PC goes to sleep. A lock that began no more than 30 seconds before the sleep is that lock: it
counts as a lock, with its time away from the wake. The sleep is taken to begin at Windows' notice
that the PC is going to sleep when that came up to 10 minutes before the last check, since with
Modern Standby SteppedOut can run on for a while after the lock, and otherwise at the last check.

## The jitter filter

A mouse sensor that twitches resets Windows' idle clock forever, which is why the built-in locks
never fire for some people. The tool cannot ask Windows what kind of input occurred, so it reasons
from the only thing it can read without observing content, the cursor position, sampled eight times
a second:

- cursor moved 0 px, yet input was reported: keyboard, wheel or click, none of which a sensor can
  fake. Counts.
- cursor moved less than `jitterPixels` (1 to 4 px at the default of 5): sensor twitch. Ignored.
- cursor moved `jitterPixels` or more, or the foreground window changed: a hand. Counts.

This is deliberately imperfect. A key pressed in the same second as a 2 px twitch is discarded with
the twitch; the next key counts. A sensor that never moves the cursor a full pixel is
indistinguishable from typing and keeps the session unlocked. Both are accepted costs of not
reading keystrokes. Turn the filter off by setting Ignore tiny mouse movements, on the Extras tab, to
Off, or
`"jitterFilter": false`, if it gets in your way.

## The rules

The lock time is not one number. Every second SteppedOut works it out again from the normal time you
chose and whatever is going on, and the countdown sits inside the result. Each setting that applies
to something going on adds to the sum; the rest cut it or cap it.

1. **Start with the normal time**, Lock the PC after.
2. **Add the extra times.** Each thing going on that has a `later N` adds N minutes: music or video,
   a call, a fullscreen window, and each app with a rule that applies. They add up. Music or video
   counts once, even when both its sound and the keep-the-display-on request are there, and an app
   with a dozen processes adds its time once.
3. **Never.** If any `never` is going on, the PC does not lock while it lasts, unless a cap below
   applies.
4. **Lock sooner.** The biggest `sooner` percent going on (from the battery or an app rule) takes
   that share off the total, extra times included. The exact result is rounded up to a whole minute,
   at least 1, and never comes out later than without the cut. 30 minutes cut by 70% is exactly 9.
5. **Always lock after.** A `always N` from the battery or an app rule is a latest time. It beats
   never lock, the shortest one wins, and one that is longer than the lock time changes nothing.
6. **The countdown** lasts the warning or half the lock time, whichever is shorter, and starts that
   long before the lock time.

A cut or a cap is the only thing that rounds. A plain lock time, with or without extras, is left as
it is, so `--idle 0.05` still locks after 3 seconds.

| Normal | Going on | Lock time |
| --- | --- | --- |
| 30 min | music `later 60`, full screen `later 120` | 3 hr 30 min |
| 30 min | full screen `later 120`, battery `sooner 50` | 1 hr 15 min |
| 30 min | music `later 60`, battery `sooner 50` | 45 min (the cut takes the extras too) |
| 30 min | music `later 60`, sound and display both there | 90 min, not 150 |
| 30 min | a call, set to `never` | never |
| 30 min | a call (`never`), battery `always 10` | 10 min |
| 15 min | battery `always 30` | 15 min (a latest time never makes it longer) |
| 5 min | battery `sooner 75` | 2 min (1.25 rounded up) |
| 30 min | cuts of 25% and 50% | 15 min (the bigger cut) |

A countdown that starts late still runs its whole length. If something that added time ends after
the idle time is already past the new lock time, if the laptop is unplugged, or if a pause ends,
SteppedOut starts a full countdown rather than locking on the spot. If something that adds time
appears while the countdown runs, the countdown stops.

**How often the rules are asked.** Every rule every 5 seconds at any idle time, so the status, the
tooltip and the minutes on the icon are right long before the lock. The battery every second. Every
rule every second as the lock gets near (from the earliest a countdown could start with your
settings, less the sound hold below) and whenever the settings window, the tray menu or the Why
window is open. Just before a countdown shows and before the PC locks, every rule looks again from
scratch. A rule that fails keeps its last answer for 10 seconds and the log says so, and one that
fails for longer never stops the lock. Asking the audio service is the one reading that waits on
another program, so a look waits at most 2 seconds for it; no answer in that time counts as the rule
failing.

Something that adds time and was seen a moment ago is held for 3 seconds after it goes, so a
foreground window missing for a moment while windows switch never starts a countdown on its own.
Sound has its own, longer hold (see Audio below).

**App rules.** Your list, matched by program name, checked every few seconds. Open, even in
background counts an app that is running. Only when on screen counts it while one of its windows is
visible on a screen: not minimized, and not on another virtual desktop. Only programs in your own
sign-in count: the same program run by another account signed in on this PC, or by a service, does
not. A Store app is found through its own window.

**Call.** On a call nobody types, yet nobody has left. Windows keeps a per-user record of which apps
have the microphone open, the same one the privacy settings page and the taskbar's microphone icon
read; an app whose stop time is zero has it open right now. Reading it needs no administrator
rights. A power cut, a forced shutdown or a crash can leave a record that looks open for good, so
records from before this sign-in are ignored, and a record only counts while its app is running in
this sign-in and was started before the record: a desktop app by its program, a Store app by any
program from its package. The sound from the app you are on a call in is the
call, not music, and so is the keep-the-display-on request a video call makes; music from another
app during a call still counts as music. The call app is found by its process, never by its name.

**Fullscreen.** The foreground window's visible bounds, taken from DWM so the invisible resize
borders are excluded, cover its whole monitor. The desktop, the taskbar, the screen saver and the
lock screen are excluded, and so are Windows' own full-screen views, which cover the monitor with no
title bar (Task View, File Explorer in F11, the full-screen Start menu and search): left open with
nobody there, they would stop the lock. Windows' screenshot overlay (the Snipping Tool and its
capture host) covers the monitor for a moment while you take a screenshot, and is excluded too. Remote Desktop and virtual machine windows (Remote Desktop
Connection, the Windows App, Hyper-V, VMware, VirtualBox) are excluded too: those are work on another
computer, and this one still has to lock when you walk away from it. An ordinary maximized window still has its
title bar and resize border, so it does **not** count, even where the taskbar is hidden; games, video
players and browsers drop both when they go truly fullscreen, so those do.

**Audio** and **display**, both belonging to While music or video is playing. Audio: any active
session on any output device that is not muted or turned all the way down, whose peak level is above
`audioPeakThreshold`, read through the Core Audio session API (`Wasapi.cs`, declared by hand); the
Windows system-sounds session is skipped, so a notification ding does not count, and a session counts
as playing until it has been quiet for `audioSilenceSeconds`.
Display: video players, browsers playing video and presentation apps ask Windows to keep the display
on; Windows folds every such request into one system-wide flag that can be read without administrator
rights, so the tool knows someone asked but not who. The cleaner audio signal would be the power
requests that players register (the ones `powercfg /requests` lists), but reading the per-process
list needs elevation, so the tool listens for sound instead. The cost: background music counts too.
If you leave a player running when you walk away, set While music or video is playing to No extra
time.

**Battery.** On battery means Windows reports the PC is not plugged in. A desktop with no battery,
or a PC where Windows cannot tell, locks as usual. On a PC where Windows reports no battery at all,
the On battery power row is hidden in the settings and in the welcome window; the setting is kept
as it was. The battery is read every second, since unplugging
can bring the lock forward at any moment.

## While you are away

- **Pause music and videos** goes through the Windows media controls. Only what was actually playing
  is touched, and the same things play again when you are back, unless you chose Keep it paused.
  It pauses on every away, with a lock or without. It reaches only apps that show in Windows' media
  controls; an app that does not show there keeps playing. Sending the pause straight to such an
  app's window was tested and dropped: an app that ignores it passes it on to Windows, which can
  start other music or video instead.
- **Mute the PC.** Windows keeps a device muted through a restart, so while the sound is muted a
  small file, `muted-by-SteppedOut`, sits in the config folder and names the device; if the PC
  restarts, you sign out, or the tool dies while you are away, the next start reads the file and
  unmutes. The mute is also lifted if Windows signs out or shuts down while you are away. Headphones
  or a screen that are off when you come back stay named in the file and are unmuted within a few
  seconds of being switched back on. If someone else takes the PC with Switch user while you are
  away, the sound is unmuted for them; choosing Switch user on the lock screen unmutes it too, even
  if nobody signs in. If the sound was already muted when you left, it stays muted when you return.
- **Turn off the screen.** With locking off they go off a few seconds after you leave, once your
  hand is off the mouse, and if a twitch of the mouse or a bumped desk wakes them, they go off again
  after 10 quiet seconds.
- **Play a sound.** The sounds are SteppedOut's own, made by `tools/sounds/gen.cs` and built into
  the exe, each 0.5 to 1.5 seconds. Each name is a pair: the away sound falls and the back sound
  rises. Your own sound is checked as a script is: a .wav file on this PC, never one on another PC,
  up to 5 MB and 10 seconds; if it is gone later, SteppedOut's own sound plays instead and the log
  says so once. The away sound plays only when the PC actually locks, with Lock now or the shortcut as well as by
  itself. The one case with no sound is a Lock now after SteppedOut stepped out without locking and
  has already muted the sound: it would not be heard, and unmuting it would let other sound through.
- **Run a script.** Arguments go in a shortcut (`.lnk`), which also lets a script run minimized. A
  PowerShell script (`.ps1`) runs in PowerShell with no window, with the execution policy set aside
  for that run unless it is AllSigned, which is kept (one set by Group Policy applies whatever is
  asked). A shortcut to a `.ps1` would open it in Notepad, so to give a script arguments, point the
  shortcut at `powershell.exe -File <script> <arguments>`. Only a file on this PC runs, by its full
  path; see [When you leave and come back](guide.md#when-you-leave-and-come-back).

## The reminder timers

The two reminders in [Break reminders and eye breaks](guide.md#break-reminders-and-eye-breaks) are timers
that count seconds of use. Going 3 minutes without input resets the break timer, and a minute
resets the eye break timer. A lock or being away resets both, and so does a gap of more than a
minute with no tick, as after sleep. A reminder that is due while a call, a pause or a countdown is
on waits and shows afterwards.

## Locking

The lock itself is `LockWorkStation`, which locks the way Win+L does. It returns as soon as the
request is queued, not when the lock is in place, so the tool listens for the session-lock
notification instead of trusting the return value. If Windows turns the request down, SteppedOut
asks once more five seconds later while you are still away; a lock turned down twice is left at that,
with the away actions in place, until you are back. On unlock it resets its own idle clock: a face or
fingerprint unlock may produce no keyboard or mouse event, and without that reset the tool could lock
again seconds after you sign in. If it starts while the session is already locked, it waits for the
unlock. Lock now, in the menu, the status box or with the shortcut, always locks, even when automatic
locking is turned off. Asked again while it is already locking, it does nothing. Input in the first
few seconds after stepping out, such as your hand leaving the mouse, does not count as coming back.

**At the lock time**, on the Locking tab, decides what follows the lock SteppedOut makes by
itself at the lock time; Lock now, the shortcut and a lock you or Windows make only lock. Lock and
turn off the screen turns the screens off after the lock, the same way Turn off the screen does.
Lock and sleep waits until the away actions have run, the away sound and closing apps included, and
the session is locked, then asks Windows to sleep, never to hibernate (the log says "asking Windows
to sleep" just before, and the result once the PC is awake again); if you are back before then,
it does not sleep. A PC with no sleep an app can ask for, as with Modern Standby, turns its screens
off instead, which is how such a PC goes to standby. After the wake the lock time starts over and
the back actions run when you unlock, as after any lock. A dry run only logs what it would do.
