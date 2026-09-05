# Outatime

Menu bar time tracker for macOS 26. Work / Break / Lunch / Extra, optional tag, edit any past day, day templates, monthly CSV export.

## Build & run
```sh
xcodegen generate          # project.yml → Outatime.xcodeproj
open Outatime.xcodeproj    # ⌘R
```
Or from the terminal:
```sh
xcodebuild -project Outatime.xcodeproj -scheme Outatime -configuration Debug -derivedDataPath build/dd build
open build/dd/Build/Products/Debug/Outatime.app
```
Tests: `xcodebuild -project Outatime.xcodeproj -scheme Outatime test`

## Data
One JSON file: `~/Library/Containers/com.dbarkan.Outatime/Data/Library/Application Support/Outatime/data.json`.

## Export
Menu bar → Export, or the Logbook toolbar. Two CSVs per month:
- **Daily summary** — Date, Work, Break, Lunch, Extra, Balance (work + break + extra − target; lunch is unpaid), Tags. Import this into Notion.
- **Entries** — one row per entry with start, end, hours.

Daily target hours: stepper in the Logbook's bottom bar (default 8).

## Settings & About
Menu bar → Settings… (⌘,): appearance (system/light/dark), language (system/English/Español/Português BR), what the menu bar shows while tracking (icon + time, icon only, time only), open at login. Translations live in `Outatime/Localizable.xcstrings`; a test fails if any key is missing `es` or `pt-BR`. About uses the standard panel (version/copyright from Info.plist).

## App icon
`swift scripts/make-icon.swift` (from the repo root) regenerates `Outatime/Assets.xcassets/AppIcon.appiconset` from AppKit drawing code.

## Release (Developer ID + notarization)
One-time: store notarization credentials (app-specific password from appleid.apple.com):
```sh
xcrun notarytool store-credentials outatime-notary --apple-id <apple-id> --team-id L26TPPMPF3
```
Then:
```sh
scripts/release.sh     # archive → export → DMG → notarize → staple → build/Outatime.dmg (skips notarization if no profile)
```
For the App Store, change `method` in `scripts/ExportOptions.plist` to `app-store-connect`.
