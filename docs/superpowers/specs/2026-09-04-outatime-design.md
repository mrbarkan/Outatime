# Outatime — design (2026-09-04)

Lightweight menu bar time tracker for macOS 26 (Tahoe). Personal use first, notarizable, App Store-ready structure.

## Scope
- Activities: work, break, lunch, extra. Optional free-text tag per entry.
- Menu bar panel: start/switch activity, stop, tag, today's totals, balance vs. daily target.
- Editor window: month sidebar → day → editable entries (activity, start, end, tag). Add/delete entries.
- Templates: save a day as a template; apply a template to any day (replaces that day's entries).
- Export: CSV for a month — entries (one row per entry) or daily summary (one row per day with hours per activity and balance). Opens in Excel, imports into Notion.
- Open at login toggle.

## Architecture
- SwiftUI app, `MenuBarExtra(.window)` + one `Window` scene. LSUIElement (no Dock icon).
- `Store` (@Observable): `[Entry]`, `[DayTemplate]`, persisted as one JSON file in Application Support. Save on every mutation via didSet.
- Running entry = entry with `end == nil`. Only one at a time.
- Liquid Glass: `GlassEffectContainer` + `.buttonStyle(.glass/.glassProminent)` for the activity switcher; system toolbars in the editor.
- Sandboxed, hardened runtime, Developer ID signing. `scripts/release.sh` archives, exports, notarizes, staples.

## Not in v1
- Control Center / WidgetKit control (needs extension target + app group).
- Native .xlsx writing (CSV covers Excel).
- iCloud sync.
