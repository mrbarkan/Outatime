# Outatime for Windows

The Windows version of Outatime: the same tracker, the same `data.json`, in the system tray. .NET 10 and
[Avalonia](https://avaloniaui.net) (Fluent theme, Mica on Windows 11), published to the Microsoft Store as an MSIX.
English, Español and Português (Brasil).

Design notes: [`docs/superpowers/specs/2026-10-09-windows-version-design.md`](../docs/superpowers/specs/2026-10-09-windows-version-design.md).

## Layout

```
src/Outatime.Core/    the Mac app's logic, ported one-to-one (Store, Stats, Objectives, Focus, BlockDrag, reports, xlsx)
  DataFile (Json.cs)  reads and writes data.json exactly as the Mac app does
  Strings*.json       translations: Strings.json is copied from the Mac catalog (scripts/strings.py), the others are Windows-only
src/Outatime/         the app: tray icon and panel, Logbook, Settings, Objectives, About, What's New
  Platform/           toasts, global shortcuts, open at login, the drawn tray icons, single instance
package/              MSIX manifest and tile images
scripts/package.ps1   portable exes + MSIX bundle
tests/Outatime.Tests/ the Mac test suite ported, data.json compatibility, translations, headless UI
```

## Build and run

Any OS with the .NET 10 SDK:

```sh
dotnet test tests/Outatime.Tests
dotnet run --project src/Outatime
```

It runs on macOS too, which is how it's developed; there it keeps its data in
`~/Library/Application Support/Outatime for Windows (dev)/`, away from the Mac app's. Handy variables:

| Variable | Effect |
|---|---|
| `OUTATIME_HOME=<folder>` | where data.json and settings.json live |
| `OUTATIME_SAMPLE=1` | fills an empty store with a few weeks of made-up work |
| `OUTATIME_OPEN=panel\|logbook\|settings\|objectives\|about\|whatsnew` | opens that window at launch (`OUTATIME_TAB=0…3` picks the Settings tab) |
| `OUTATIME_SNAPSHOT=<folder>` | renders every open window to PNG there, then quits |

A Windows exe from any OS:

```sh
dotnet publish src/Outatime -c Release -r win-x64 --self-contained -o artifacts/single-x64 \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

## Package (on Windows)

Needs the Windows SDK for `makeappx`/`signtool`.

```powershell
.\scripts\package.ps1          # artifacts\: Outatime_<v>.msixbundle, per-arch .msix, portable zips
.\scripts\package.ps1 -Sign    # also signs with a self-signed test certificate
```

To install a signed test build by hand, trust the certificate once (admin PowerShell), then double-click the bundle:

```powershell
Import-Certificate -FilePath artifacts\Outatime-test.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

The GitHub Actions workflow (`.github/workflows/windows.yml`) runs the tests, packages, launches the portable exe
once with sample data as a smoke test, and uploads everything as the `outatime-windows` artifact.

## Microsoft Store

1. Create the developer account at [storedeveloper.microsoft.com](https://storedeveloper.microsoft.com) (free for individuals).
2. In Partner Center, reserve the name **Outatime**, then open *Product identity* and copy **Package/Identity/Name**,
   **Package/Identity/Publisher** and **PublisherDisplayName** into `package/AppxManifest.xml`.
3. Run `.\scripts\package.ps1` (unsigned: the Store signs it) and upload `Outatime_<version>.msixbundle`.
4. The listing needs: a privacy policy URL (Outatime keeps everything on the PC and sends nothing), the age rating
   questionnaire, screenshots (`OUTATIME_SNAPSHOT` makes them), and the *runFullTrust* justification: "A desktop
   tray app; it runs in the notification area and registers global shortcuts."
5. Betas: a *package flight* in Partner Center with a group of testers' Microsoft accounts.

Bump `<Version>` in `Directory.Build.props` for each release (the MSIX version is it plus `.0`), and add an entry
to `WhatsNew.Releases` in `src/Outatime.Core/WhatsNew.cs`.

## What's different from the Mac

The notification area can't show text, so the elapsed time, time left or tomato countdown is the tray icon's
tooltip; the icon keeps the activity's color. Left-click opens the panel, right-click a small menu. Shortcuts are
Ctrl+Alt+Shift+W / B. Ctrl replaces ⌘, Alt replaces ⌥. The Store updates the app, so there's no Updates tab; Settings
→ General has Export/Import Data instead, because uninstalling a Store app deletes its data.
