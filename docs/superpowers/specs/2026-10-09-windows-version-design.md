# Outatime for Windows — design

Date: 2026-10-09 · Branch: `windows` · Folder: `windows/`

## Goal

A public Windows release of Outatime with the Mac app's full feature set, in English, Español and
Português (Brasil), distributed through the Microsoft Store. A user can move `data.json` between the Mac and
Windows apps and both read it.

## Decisions

| Question | Decision | Why |
|---|---|---|
| Audience | Public release | User's call. |
| Scope | Full parity with Mac 1.3 | User's call. |
| Languages | en, es, pt-BR | User's call. Strings come from the Mac `Localizable.xcstrings`, so the wording matches. |
| Data | Same `data.json` schema | Copy the file between platforms and it just works. |
| Distribution | Microsoft Store (MSIX) | Free individual account; the Store signs the package, so there's no SmartScreen warning and no certificate to buy. Updates come from the Store. |
| Stack | .NET 10 + Avalonia 12 (Fluent theme) | Builds, runs and is tested on the Mac that develops it (WinUI 3 can't be compiled on macOS); it cross-compiles a Windows exe and packages as MSIX. Native Windows pieces (toasts, hot keys, startup) sit behind `#if WINDOWS`. |
| Repo | `windows/` in this repo | Schema changes land in one commit for both apps. Windows releases don't use GitHub Releases, so the Mac's Sparkle feed is unaffected. |

## What differs from the Mac app, on purpose

- **Tray instead of menu bar.** The Windows notification area can't show text next to the icon, so the
  elapsed time, time left today or tomato countdown moves to the icon's tooltip. The icon still takes the
  activity's color. The *Menu bar* style picker becomes a *Tooltip shows* picker.
- **Left-click the tray icon** opens the panel (the Mac popover) above the taskbar; it closes when it loses
  focus. **Right-click** gives a small menu: Open, Logbook, Settings, Quit.
- **No Updates tab.** The Store updates the app. Betas are Store package flights; nothing in the app.
- **Global shortcuts** are Ctrl+Alt+Shift+W (Work) and Ctrl+Alt+Shift+B (Break); Windows reserves most
  Win-key combinations.
- **Open at login** uses the MSIX `StartupTask` when packaged and the `HKCU\…\Run` key when run unpackaged.
- **Data lives in** `%LOCALAPPDATA%\Outatime\data.json` (MSIX redirects that into the package's private
  folder). Uninstalling a Store app deletes it, so Settings → General gets **Export data…** and
  **Import data…** (copy `data.json` out and back in), plus **Open data folder**.
- **Settings** has four tabs: General, Target, Focus, Clients.
- Keyboard shortcuts use Ctrl where the Mac uses ⌘; Alt where it uses ⌥ (shared-border drag); Ctrl-click and
  Shift-click select several blocks.

Everything else — six activities, notes, clients, Logbook with drag/resize/insert-a-break, month sidebar
with bars and target ticks, week totals, day off, templates, Objectives, Stats, auto-Extra after the target,
midnight roll-over, tomato timer with actionable notifications, stretch and long-timer reminders, Excel
(month, client, master with dashboard) and CSV exports, appearance, language, What's New, About — behaves as
on the Mac.

## data.json compatibility

Swift's `JSONEncoder` with `.iso8601` writes and reads dates as `2026-09-22T13:04:05Z`: UTC, whole seconds,
no fraction (a fraction makes the Mac decoder reject the whole file). UUIDs are upper-case. Optional values are
omitted when nil (`end`, `profile`, `current`). Objective and template slot fields are always written in full,
because Swift's synthesized decoder requires every non-optional key. Old shapes the Mac reads (a single `tag`
instead of `notes`, missing `daysOff`/`profiles`/`objectives`) are read the same way. An unreadable file is
moved aside to `data.json.broken-<unix time>` rather than overwritten.

A shared fixture, `windows/tests/fixtures/mac-data.json`, written by the Mac app's encoder rules, is read by
the Windows tests and written back; the result must decode to the same values.

Settings (target hours, excluded activities, bank start, focus minutes, language…) aren't in `data.json` on
the Mac either; Windows keeps them in `settings.json` next to it.

## Architecture

```
windows/
  Outatime.slnx
  src/Outatime.Core/        net10.0 class library, no UI
    Models.cs               Activity, Profile, Entry, DayTemplate, Target, formatting (hm, short, dayKey)
    Cal.cs                  local-calendar helpers: start of day, weeks (culture's first weekday), months
    Store.cs                entries, templates, days off, clients, objectives; JSON persistence; tracking and editing
    Json.cs                 Mac-compatible serializer
    Stats.cs, Objectives.cs, Focus.cs (Pomodoro, Stretch, Reminder), BlockDrag.cs
    Export.cs (DaySummary, Csv, Report), Workbook.cs (Xlsx, Zip)
    Settings.cs             settings.json, same keys as the Mac's UserDefaults
    Loc.cs + Strings.json   en/es/pt-BR strings keyed by the Mac's English text, printf-style placeholders
  src/Outatime/             Avalonia app (net10.0 for development; net10.0-windows10.0.19041.0 for release)
    App.cs                  startup, tray icon, ticks (30 s: roll-over, auto-Extra, reminders; 1 s: tomato)
    MenuPanel.cs            the tray panel
    Logbook/                window, month sidebar, day timeline, entry editor, templates
    ObjectivesWindow.cs, StatsFlyout.cs, SettingsWindow.cs, AboutWindow.cs, WhatsNewWindow.cs
    Platform/               Notifications, HotKeys, StartupTask, TrayIcons (rendered per activity color)
    Package/                AppxManifest.xml and logo assets for the MSIX
  tests/Outatime.Tests/     xUnit: ports the Mac test suite, plus JSON compatibility, translations, headless UI
```

`Outatime.Core` ports the Swift logic one-to-one, keeping the names, so a fix on one side can be found and
carried over to the other. Instants are `DateTimeOffset` (absolute, like Swift's `Date`); every "which day" or
"which week" question goes through `Cal`, which uses the local time zone and current culture.

The UI is written in C#, without XAML: views rebuild from the store when it changes, the way SwiftUI does.
`Store.Changed` fires after each mutation, and the store saves on each change, as on the Mac.

## Notifications

Packaged on Windows: toast notifications. The tomato round's toast carries a *Start Break* or *Back to Work*
button that switches the tracker. Rounds end on the app's own 1-second tick, not on a scheduled toast, so a
changed round never leaves a stale toast behind. On the development Mac the notifier just logs.

## Testing

- `dotnet test` on macOS and on the Windows CI runner: the ported Mac tests (templates, CSV, workbooks, store
  round trip, roll-over, auto-Extra, client switching, insert, drag math, Pomodoro, stats, objectives),
  Mac JSON compatibility, and a translation check (every string used by the UI exists in es and pt-BR).
- Headless Avalonia tests open the panel, Logbook and Settings against a sample store and check they build.
- Manual runs on the Mac (the same app) with screenshots of every window.

## Build and release

- `dotnet publish -c Release -r win-x64` from any OS → self-contained `Outatime.exe` (zip for testers).
- GitHub Actions on `windows-latest`: test, publish win-x64 and win-arm64, pack an MSIX with `makeappx`,
  upload both as artifacts. The `.msixupload`/`.msix` goes to Partner Center by hand for the first submission;
  the Store signs it.
- Store listing needs: privacy policy URL ("Outatime keeps your data on your PC and sends nothing"), age
  rating questionnaire, screenshots, and the publisher identity from Partner Center pasted into
  `AppxManifest.xml` (`Identity Name` and `Publisher`).

## Plan

1. Scaffold the solution, Core models, calendar helpers, JSON with the Mac fixture; tests green.
2. Port Store, Stats, Objectives, Focus, BlockDrag, Export, Workbook with their tests.
3. Strings: extract en/es/pt-BR from `Localizable.xcstrings`; `Loc`; translation test.
4. App shell: tray icon, ticks, notifications, hot keys, settings file, startup.
5. In parallel: tray panel; Logbook; Settings + Objectives + Stats + About + What's New.
6. Run on the Mac, screenshot, fix. Headless UI tests.
7. Windows build: cross-compile `win-x64`, CI workflow with MSIX, README for Windows.
