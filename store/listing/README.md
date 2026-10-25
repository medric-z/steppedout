# Store listing kit

| File | What it holds |
|---|---|
| `en.md` | The English listing: name, short description, description, features, keywords, screenshot captions |
| `submission.md` | Pricing, properties, age-rating answers, the runFullTrust justification and the notes for certification |

Images:

- **App tile icon (300 x 300):** `listing/logo-300.png` in the build's icons folder, drawn by
  `tools/store/build.ps1` from the app's logo code. The larger `logo-1080.png` and `logo-2160.png`
  are there too, for the optional 1:1 box art.
- **Screenshots:** PNG, 1366 x 768, in `screenshots/en/`, numbered
  in the order of the captions in `en.md`: `1-settings.png`, `2-countdown.png`,
  `3-tray-menu.png`, `4-away-and-back.png` and `5-why.png`. They are the app's real windows on a
  plain desktop, light theme, drawn by `tools/shots` (its `store` part, which writes
  `store-<scene>-<language>-<theme>.png`; the light ones are copied here under these names). Draw
  them again after any change to a window they show.

Limits Partner Center applies (checked in Microsoft's documentation, August 2026): description up to
10,000 characters; short description up to 1,000, with the first 270 shown in some views; up to 20
features of up to 200 characters each; screenshot captions up to 200 characters; at least one
screenshot and up to 10 for desktop. The description is plain text: no HTML and no links.

The listing names the app's features as the app's own text does. If a button or setting is renamed
before release (for example "Try it"), change it here and in `submission.md` too.
