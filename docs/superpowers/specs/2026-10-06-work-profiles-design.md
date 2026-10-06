# Work profiles (clients) — design

Freelancers jump between clients' work during a day. A client is a **label** on billable blocks, with per-client
reports for billing. The daily target, balance and hours bank stay shared.

## Data

- `Profile { id: UUID, name: String, archived: Bool }`, stored in `data.json` as `profiles` (optional key, added in 1.2).
- `Store.currentProfile: Profile.ID?`, stored in `data.json` as `current`.
- `Entry.profile: Profile.ID?` — encoded only when set; old files decode with `nil`.
- `DayTemplate.Slot.profile: Profile.ID?` — same treatment; applying a template keeps each block's client.
- Removing a client archives it: it leaves the pickers, its name stays on old blocks and exports. Selecting it is cleared.

## Tracking

- `Activity.billable` is true for Work, Extra and Travel. Only those get a client.
- `start(activity)` stamps `currentProfile` on billable activities. Starting the running activity again is a no-op only
  when the client matches too.
- `select(profile)` sets `currentProfile`; if a billable block is running for another client it ends now and the same
  activity starts for the new client. Notes stay on the old block.
- Midnight rollover, Switch to Extra and `insert` (cutting a block around a break) carry the client to the new pieces.
  Blocks added in the Logbook (`addEntry`) take the current client. Switching within a block's first minute just
  relabels it (a quick correction leaves no sliver).
- `runningSince` only follows back across a midnight cut when the client matches.

## UI

- Menu panel: status reads "Work · Acme". A client picker sits under the note field — segmented (None + clients) for
  up to 3 active clients, a popup menu beyond that. Hidden while there are no active clients.
- Settings: "Clients" section — add (text field + button), rename inline, remove (archive).
- Logbook: block title "Work · Acme"; the block editor has a Client picker for billable activities.

## Exports

- Entries CSV and the Entries sheet get a trailing **Client** column (existing column positions unchanged).
- Month Excel report gets a **Clients** sheet: client, Work, Extra, Travel, Total hours for the month (only clients with
  time; blocks without a client are listed as "No client").
- Export menu: one "This Month — <Client> (Excel)…" per client with billable time this month (archived ones too): a **Days** sheet (date,
  Work, Extra, Travel, Total, Notes, total row) and that client's billable entries. No target or balance.

## Out of scope

Per-client targets, rates or budgets; client in the menu bar label; client shortcuts.

## Testing

Swift Testing: old-file decode and round-trip; start stamps only billable activities; switching splits the running
block; rollover and Switch to Extra keep the client; client report rows and Clients sheet totals.
