# Hours Bank — design

Overtime piles up across months and is spent as time off. Today the bank is only implied: an empty, unmarked workday
silently costs a day's target and shows red, the Day Off toggle makes a day free (a holiday, not a withdrawal), and
hours earned before tracking began can't be entered. The Hours Bank makes every deposit and withdrawal explicit, and
moves Travel and Out of Office from live timers to things you log. Mac and Windows ship it together.

## Data

All in `data.json`, read and written identically by both apps (day keys `"2026-10-12"`, upper-case UUIDs, nil
optionals left out, keys sorted).

- **Holidays** stay in `daysOff` (day keys). No migration; older versions keep reading them.
- **`timeOff`** (optional key, added in 1.3): `TimeOff { id: UUID, start: String, end: String, hours: Double?, note: String }`.
  `start`…`end` are inclusive day keys. `hours` is per workday; nil means a full day, which is whatever the daily
  target is, so changing the target re-prices full days. `note` is always written (Swift's synthesized decoder needs it).
- **`carried`** (optional key, added in 1.3): `Carried { id: UUID, date: String, hours: Double, note: String }`.
  `hours` is signed: positive for overtime brought in, negative for a payout or correction.
- Older versions ignore both keys but drop them on their next save. What's New says so; nothing prevents it.

## What a day owes

- `Target` gains `booked: [dayKey: seconds]`, filled by `book(_ timeOff:)` from the records and the target.
- A workday's cost is `min(hours ?? target, target)`; weekends, holidays and days before tracking began cost 0.
- `scheduled(day) = max(0, target − booked[day])`. A full day off owes nothing (neutral, not red); 4h off on an 8h
  day owes 4h. `owed` and `balance` build on `scheduled` as today, so day, week and month balances and exports
  follow automatically. Those balances measure worked against owed; they never show the withdrawal.

## The bank

One function, `Store.bank(_ target:, since:, now:)` returning `HoursBank { balance, worked, bookedAhead }`,, read by the menu's "Since …" row, Stats (Days off banked, To next day
off), the Vacation objective and the Hours Bank sheet:

    bank = Σ day balances from `since` to now
         − Σ time-off cost on workdays ≥ `since`   (future ones too: booked time off is spent at once)
         + Σ carried with date ≥ `since`             (all of them while the bank starts when tracking began)

`since` is Settings → Target → "Hours bank since" as today. Carried hours are usually dated before tracking began (that's
the point of them), so they're only filtered once "Hours bank since" is moved past the start of tracking; then older
records drop out along with older days. The bank also reports **booked ahead**: the time-off cost on days after today.

## Logging Travel and Out of Office

They leave the menu's tile grid (now 2×2: Work, Break, Lunch, Extra) and are logged instead:
`Store.log(activity, from:, to:, hoursPerDay:, profile:)` adds one block per workday in the range that still owes
something (skips weekends, holidays and full days off). The block starts after the day's last block, or at the usual
start (Stats' median) on an empty day, or 9:00 with no history; it is `hoursPerDay` long (default: the daily target),
cut at midnight. Travel carries the client; Out of Office has none. They are ordinary blocks from then on: draggable,
counted per "Counts toward the target", exported, billed.

A Travel or Out of Office block still running from before the upgrade gets a Stop button in the menu's status line,
since its tile is gone. Existing blocks of both kinds are untouched.

## UI (both apps)

- **Logbook toolbar:** the Day Off toggle is replaced by **Hours Bank**, opening a sheet (Mac) or dialog (Windows) the
  way Objectives does.
- **Hours Bank sheet:**
  - Header: the bank (`+20h 00m`, green/red), and under it "2.5 days off · 24h booked ahead" (each part only when
    non-zero).
  - **Log** menu: Time Off, Holiday, Travel, Out of Office, Carried Hours. Each opens a short form:
    - Time Off: From, To; an Hours field when From = To (empty = full day); Note.
    - Holiday: From, To.
    - Travel / Out of Office: From, To, Hours per day (default the target); Travel adds Client.
    - Carried Hours: Date, Hours (±), Note.
  - List, newest first: time off (`−24h`), carried (`+32h`), holidays (`—`), each with its date or range and note.
    Click a row to edit it in the same form; a trash button deletes it. Consecutive holiday days show as one range;
    editing or deleting a holiday range rewrites those `daysOff` keys. Travel and Out of Office aren't listed: once
    logged they're blocks.
- **Logbook sidebar:** a holiday shows the beach umbrella as today; a time-off day shows its own icon
  (`clock.arrow.circlepath` / the Windows equivalent). Both read neutral rather than red.
- **Menu panel:** **Log…** in the footer next to Export opens the same Log forms in a small window. The "Since …" bank
  row is clickable and opens the Logbook with the Hours Bank sheet.
- **Settings:** "Counts toward the target" keeps its Travel and Out of Office toggles.

## Exports

- Monthly report Summary and the Master workbook's Days table: a **Time Off** column right after Target (hours of
  time off that day). A day with time off and no blocks now gets a row.
- Daily Summary CSV: a trailing `time_off` column (existing columns keep their positions).
- Master workbook: an **Hours Bank** sheet (`tblBank`: Date, Kind, Hours signed, Note) listing carried hours and
  time-off records; the HOURS BANK card becomes `SUM(tblDays[Balance]) + SUM(tblBank[Hours])`, matching the app.

## Text and docs

Every new string in all eight languages, in `Localizable.xcstrings` and Windows' `Strings.json`. The manual (eight
languages): four tiles instead of six, Day Off replaced by Hours Bank and Log…. README, and a What's New item under
1.3 on both apps (betas don't show it).

## Out of scope

Per-weekday schedules, accrual rules (vacation days earned per month), time-off approval, holiday calendars by country,
logging Work/Break/Lunch/Extra as ranges.

## Testing

Swift Testing and xUnit, the same cases on both:
- `scheduled`/`owed` with full and partial time off; weekends and holidays in a range cost nothing; hours above the
  target are capped.
- `bank`: carried added (all of them while the bank starts with tracking; only those from `since` on once it's moved
  later), time off subtracted including future days, booked-ahead figure.
- `log`: skips weekends, holidays and full days off; starts after the last block / at usual start / at 9:00; Travel
  carries the client; cut at midnight.
- Data file: old files load with empty lists; round-trip; the shared Mac fixture gains `timeOff` and `carried` and
  Windows reads it and writes it back byte-identically.
- Exports: Time Off column; the Master workbook's bank equals `Store.bank`.
- Windows headless UI snapshots include the Hours Bank dialog and the Log window.

## Shipping

Built on `windows`, merged into `main`. Windows releases from `main` (its CI). The Mac ships as `1.3-beta.3` via
`burn it beta` for testing before 1.3.
