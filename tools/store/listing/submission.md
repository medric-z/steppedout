# Store submission: everything outside the listing text

What to choose or paste on each Partner Center page of the submission. The listing text itself is
in `en.md`. The package is the `.msixupload` that `tools/store/build.ps1` makes.

## Pricing and availability

- Markets: all markets (the default).
- Audience: public.
- Discoverability: make this product available and discoverable in the Microsoft Store.
- Schedule: release as soon as possible (the default).
- Base price: Free.
- Free trial, sale pricing: none.

## Properties

- Category: Utilities & tools. No subcategory or secondary category.
- Does the app access, collect or transmit personal information? Answer Yes if the question can
  be answered; it may already be set to Yes and locked. Microsoft's Store Policies (10.5.1) count
  every Desktop Bridge and Win32 product as having access to personal information, and runFullTrust
  makes this package one, so a privacy policy URL is always required. The privacy section it points
  to says what is true: SteppedOut makes no network connections and collects or sends nothing.
- Privacy policy URL: https://github.com/medric-z/steppedout#privacy
- Website: https://github.com/medric-z/steppedout
- Support contact info: https://github.com/medric-z/steppedout/issues
- Display mode, game settings, system requirements: leave empty.
- Product declarations: leave as they are.

## Age ratings

The questionnaire's first question asks for the category that best describes the app. Choose the
utility or productivity kind of category: not a game, social or messaging, web browser, or
streaming. The questions after that depend on the category chosen; answer each one truthfully.
For SteppedOut every true answer is no:

- No violence, fear, sexual content, nudity, crude humour, bad language, drugs, alcohol, tobacco or
  gambling, and no references to any of them.
- Users can't talk to or share content with other users.
- It doesn't share the user's location or any personal information.
- It has no purchases of any kind.
- It doesn't give unrestricted access to the internet, such as a web browser or a search engine. It
  makes no network connections at all.

The expected result is the lowest rating in every region (for example 3+ or Everyone). IARC sets
the actual rating from the answers. The page also offers to use an existing IARC rating ID instead
of the questionnaire; that only applies to an app already rated in another store, so skip it.

## Packages

- Upload `SteppedOut_<version>_x64_arm64.msixupload` from the build's output folder.
- Device family availability: Windows 10/11 Desktop only (it is the only family the package names).
- Leave "Let Microsoft decide whether to make this app available to any future device families"
  checked.

## Submission options

### Restricted capabilities: runFullTrust

Paste this into the box that asks why the app uses runFullTrust:

SteppedOut is a desktop app that locks the Windows session after the user has been away from the PC for the time they choose. It needs runFullTrust because it uses desktop Windows features that a packaged app can't reach otherwise: it locks the session (the same lock as Win+L), reads the time of the last mouse and keyboard input, reads audio levels to tell when music, video or a call is playing, checks whether any app is using the microphone so that a call keeps the PC unlocked, lists running apps and windows to apply the user's app rules and to notice full-screen apps, and, if the user turns one on, registers a keyboard shortcut for locking right away. By default it also pauses music and video while the user is away and plays them again when they return, which the user can turn off. Only when the user turns them on, it also mutes the sound, turns off the screen, opens or closes apps the user picked, runs scripts the user chose, and puts the PC to sleep after it locks. It makes no network connections and collects no data.

### Notes for certification

Paste this, with the date filled in:

Date: <the day you submit>
No account or sign-in is needed, and the app has no purchases and no network features.
SteppedOut runs in the notification area: a padlock icon next to the clock (it may be under the ^ arrow). On first start a welcome window opens: click Start, then Next on each page (or Skip), and Done on the last.
Left-click the icon to open the settings window. Right-click it for the menu: Pause, Lock now, Settings and Quit.
The PC locks after the chosen time without mouse or keyboard input. Music or video, calls and full-screen apps can delay the lock, as set in the settings.
To see the countdown without locking the PC, open the settings and click "Try it" next to "Warn me before it locks".
"Lock now" in the menu and the button in the settings lock the session right away, the same as Win+L. A keyboard shortcut, Ctrl+Alt+L or another, does the same once it is turned on in the welcome window or on the Extras tab; it is off by default.
"Start with Windows" uses the package's startup task and can be turned off in the settings or in Windows Settings, Apps, Startup.
The source code is public: https://github.com/medric-z/steppedout

### Publishing hold

Leave the default: publish as soon as it passes certification.
