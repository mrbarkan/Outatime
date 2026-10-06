# Work Profiles (Clients) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let freelancers pick a client in the menu panel so Work, Extra and Travel blocks are stamped with it, switch
clients mid-block, and export per-client month reports.

**Architecture:** A client is a `Profile` stored in `data.json`; `Entry.profile` holds its id. `Store` owns the list,
the selected client and the switching logic. Exports take a `[Profile.ID: String]` name map so they stay pure functions.

**Tech Stack:** Swift 6 / SwiftUI (macOS 26), Swift Testing, the in-repo XLSX writer.

**Spec:** `docs/superpowers/specs/2026-10-06-work-profiles-design.md`

## Global Constraints

- Billable activities: Work, Extra, Travel. Only these ever carry a client.
- Old `data.json` files must load unchanged; `profile`, `profiles`, `current` keys are optional.
- Existing export columns keep their positions; Client is appended last.
- Every new UI string goes in `Outatime/Localizable.xcstrings` with `es` and `pt-BR` (enforced by `catalogIsComplete`).
- Test command: `xcodebuild -project Outatime.xcodeproj -scheme Outatime -destination 'platform=macOS' test 2>&1 | grep -E "✘|error:|Test run"`

## Review Focus

- Picking the wrong client and correcting it within a minute: should relabel the running block, not leave a sliver. → Task 2 test `switchingClientSplitsRunningBlock`.
- Picking a client while on Break/Lunch: nothing splits; the next Work picks it up. → Task 2 test.
- Removing the client you're currently working for: picker falls back to No client, the running block keeps its client and its name in exports. → Task 2 test `removingClientArchives`.
- Client name with "/" or ":" in the export file name: must be file-safe. → Task 3 test `clientExportNameIsFileSafe`.
- Changing a block from Work to Break in the editor: the client is dropped. → Task 4 (`onChange` in `EntryForm`), covered by `Entry` invariant in exports (Task 3 filters billable).

---

### Task 1: Model and persistence

**Files:** Modify `Outatime/Models.swift`, `Outatime/Store.swift`; Test `OutatimeTests/OutatimeTests.swift`

**Produces:** `Activity.billable: Bool`; `Profile { id: UUID, name: String, archived: Bool }`; `Entry.profile: Profile.ID?`
(`Entry.init(..., profile: Profile.ID? = nil)`); `DayTemplate.Slot.profile`; `Store.profiles`, `Store.currentProfile`,
`Store.activeProfiles`, `Store.profileNames: [Profile.ID: String]`, `Store.addProfile(_:) -> Profile.ID?`,
`Store.rename(_:to:)`.

- [ ] **Step 1: Failing test**

```swift
/// Files from before clients load as before; clients, the selection and each block's client survive a relaunch.
@Test @MainActor func clientsRoundTrip() throws {
    let url = FileManager.default.temporaryDirectory.appending(path: "outatime-test-\(UUID().uuidString)/data.json")
    defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }
    try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
    try Data(#"{"entries":[{"id":"\#(UUID())","activity":"work","start":"2026-09-03T09:00:00Z","notes":[]}],"templates":[]}"#.utf8).write(to: url)
    let store = Store(url: url)
    #expect(store.entries.first?.profile == nil && store.profiles.isEmpty)
    let acme = try #require(store.addProfile("  Acme "))
    #expect(store.addProfile("  ") == nil)
    store.currentProfile = acme
    store.entries[0].profile = acme
    let reloaded = Store(url: url)
    #expect(reloaded.profiles.map(\.name) == ["Acme"])
    #expect(reloaded.currentProfile == acme && reloaded.entries[0].profile == acme)
    #expect(DayTemplate(name: "t", entries: reloaded.entries).entries(on: day)[0].profile == acme)
}
```

- [ ] **Step 2: Run, expect compile failure** (`profile`, `addProfile` missing).

- [ ] **Step 3: Implement**

`Models.swift` — in `Activity`: `/// Time billed to a client.  var billable: Bool { self == .work || self == .extra || self == .travel }`.
New type after `Activity`:

```swift
/// A client billable blocks are tracked for. Removing one archives it, so old blocks keep its name.
nonisolated struct Profile: Codable, Identifiable, Hashable {
    var id = UUID()
    var name: String
    var archived = false
}
```

`Entry`: add `var profile: Profile.ID?`, `profile` in `CodingKeys`, `profile: Profile.ID? = nil` last init param,
`profile = try c.decodeIfPresent(UUID.self, forKey: .profile)`, `try c.encodeIfPresent(profile, forKey: .profile)`.
`Slot`: `var profile: Profile.ID?`, init param `profile: Profile.ID? = nil`, decodeIfPresent; pass through in
`DayTemplate.init(name:entries:)` and `entries(on:)`.

`Store.swift`:

```swift
var profiles: [Profile] = [] { didSet { if loaded { save() } } }
/// The client picked in the menu; new billable blocks get it.
var currentProfile: Profile.ID? { didSet { if loaded { save() } } }
// File: var profiles: [Profile]?; var current: UUID?  // added in 1.2
var activeProfiles: [Profile] { profiles.filter { !$0.archived } }
var profileNames: [Profile.ID: String] { Dictionary(uniqueKeysWithValues: profiles.map { ($0.id, $0.name) }) }

@discardableResult
func addProfile(_ name: String) -> Profile.ID? {
    let name = name.trimmingCharacters(in: .whitespaces)
    guard !name.isEmpty else { return nil }
    let p = Profile(name: name)
    profiles.append(p)
    return p.id
}

func rename(_ id: Profile.ID, to name: String) {
    if let i = profiles.firstIndex(where: { $0.id == id }) { profiles[i].name = name }
}
```

Load `profiles = file.profiles ?? []; currentProfile = file.current`; save them.

- [ ] **Step 4: Run tests, expect PASS.**
- [ ] **Step 5: Commit** `Clients: model and persistence`.

### Task 2: Tracking

**Files:** Modify `Outatime/Store.swift`; Test `OutatimeTests/OutatimeTests.swift`

**Consumes:** Task 1. **Produces:** `Store.select(_ profile: Profile.ID?, now: Date = .now)`, `Store.removeProfile(_:)`;
`start(_:)` stamps the client.

- [ ] **Step 1: Failing tests**

```swift
@Test @MainActor func switchingClientSplitsRunningBlock() throws {
    let store = Store(url: FileManager.default.temporaryDirectory.appending(path: "outatime-test-\(UUID().uuidString)/data.json"))
    let acme = try #require(store.addProfile("Acme")), globex = try #require(store.addProfile("Globex"))
    store.select(acme)
    store.start(.break)
    #expect(store.running?.profile == nil)  // breaks are never billed
    store.entries = [Entry(activity: .work, start: at(9), notes: ["x"], profile: acme)]
    store.select(globex, now: at(9, 0, 40))  // a quick correction relabels
    #expect(store.entries.count == 1 && store.running?.profile == globex)
    store.select(acme, now: at(10))
    #expect(store.entries.count == 2 && store.entries[0].end == at(10) && store.entries[0].profile == globex)
    #expect(store.running?.start == at(10) && store.running?.profile == acme && store.running?.notes == [])
    store.select(acme, now: at(11))
    #expect(store.entries.count == 2)
}

@Test @MainActor func clientCarriesOverMidnightAndIntoExtra() throws {
    let store = Store(url: FileManager.default.temporaryDirectory.appending(path: "outatime-test-\(UUID().uuidString)/data.json"))
    let acme = try #require(store.addProfile("Acme")), globex = try #require(store.addProfile("Globex"))
    store.entries = [Entry(activity: .work, start: at(17), profile: acme)]
    let next = cal.date(byAdding: .hour, value: 16, to: at(17))!
    store.rollOver(now: next)
    #expect(store.running?.profile == acme && store.runningSince == at(17))
    store.entries = [Entry(activity: .work, start: at(9), end: at(10), profile: globex), Entry(activity: .work, start: at(10), profile: acme)]
    #expect(store.runningSince == at(10))  // another client's block isn't the same stretch
    store.shiftToExtra(Target(seconds: 8 * 3600, since: at(0)), now: at(17, 30))
    #expect(store.running?.activity == .extra && store.running?.profile == acme)
    store.stop()
    store.currentProfile = globex
    store.insert(.travel, at: at(18), length: 600)
    #expect(store.entries.last?.profile == globex)
}

@Test @MainActor func removingClientArchives() throws {
    let store = Store(url: FileManager.default.temporaryDirectory.appending(path: "outatime-test-\(UUID().uuidString)/data.json"))
    let acme = try #require(store.addProfile("Acme"))
    store.select(acme)
    store.entries = [Entry(activity: .work, start: at(9), profile: acme)]
    store.removeProfile(acme)
    #expect(store.activeProfiles.isEmpty && store.profileNames[acme] == "Acme")
    #expect(store.currentProfile == nil && store.running?.profile == acme && store.entries.count == 1)
}
```

- [ ] **Step 2: Run, expect failure.**

- [ ] **Step 3: Implement**

```swift
func start(_ activity: Activity) {
    let profile = activity.billable ? currentProfile : nil
    if let running, running.activity == activity, running.profile == profile { return }
    stop()
    entries.append(Entry(activity: activity, start: .now, profile: profile))
}

/// Picks the client for billable blocks. A billable block running for another client ends here and carries on for
/// this one; within its first minute it's taken as a correction and just relabelled.
func select(_ profile: Profile.ID?, now: Date = .now) {
    currentProfile = profile
    guard let i = entries.firstIndex(where: \.isRunning), entries[i].activity.billable, entries[i].profile != profile else { return }
    if now.timeIntervalSince(entries[i].start) < 60 {
        entries[i].profile = profile
    } else {
        entries[i].end = now
        entries.append(Entry(activity: entries[i].activity, start: now, profile: profile))
    }
}

/// Archived, not deleted: its blocks keep their client. A block running for it carries on.
func removeProfile(_ id: Profile.ID) {
    guard let i = profiles.firstIndex(where: { $0.id == id }) else { return }
    profiles[i].archived = true
    if currentProfile == id { currentProfile = nil }
}
```

`rollOver` and `shiftToExtra` new entries pass `profile: entries[i].profile`; `runningSince` adds
`&& $0.profile == running.profile`; `insert`'s new entry gets `profile: activity.billable ? currentProfile : nil`.

- [ ] **Step 4: Run tests, expect PASS.**
- [ ] **Step 5: Commit** `Clients: switching, carry-over and archiving`.

### Task 3: Exports

**Files:** Modify `Outatime/Export.swift`; Test `OutatimeTests/OutatimeTests.swift`

**Consumes:** Task 1. **Produces:** `CSV.entries(_:names:)`, `Report.month(_:month:target:names:now:)`,
`Report.master(_:target:names:now:)`, `Report.client(_ entries: [Entry], name: String, month: Date) -> Data` (entries
already filtered to the client), `clientExportName(_ month: Date, _ name: String) -> String`.

- [ ] **Step 1: Failing tests**

```swift
@Test func clientExports() {
    let acme = UUID(), names = [acme: "Acme"]
    let entries = [Entry(activity: .work, start: at(9), end: at(12), notes: ["api"], profile: acme),
                   Entry(activity: .break, start: at(12), end: at(12, 15)),
                   Entry(activity: .work, start: at(12, 15), end: at(13, 15)),
                   Entry(activity: .travel, start: at(14), end: at(14, 30), profile: acme)]
    let csv = CSV.entries(entries, names: names).split(separator: "\n")
    #expect(csv[0].hasSuffix(",Hours,Client") && csv[1].hasSuffix(",3.00,Acme") && csv[2].hasSuffix(",0.25,"))

    let month = String(decoding: Report.month(entries, month: day, target: Target(seconds: 8 * 3600, since: day), names: names, now: at(22)), as: UTF8.self)
    #expect(month.contains(#"<sheet name="Clients""#))
    #expect(month.contains(#"name="tblClients" displayName="tblClients" ref="A1:E4" totalsRowCount="1""#))  // Acme, No client, total
    #expect(month.contains("<f>SUBTOTAL(109,tblClients[Total])</f><v>4.5</v>"))
    let plain = String(decoding: Report.month([entries[1]], month: day, target: Target(seconds: 0, since: day), now: at(22)), as: UTF8.self)
    #expect(!plain.contains("Clients"))  // not using clients: no sheet

    let report = String(decoding: Report.client(entries.filter { $0.profile == acme }, name: "Acme", month: day), as: UTF8.self)
    #expect(report.contains(#"<sheet name="Days""#) && report.contains("<f>SUBTOTAL(109,tblClientDays[Total])</f><v>3.5</v>"))
    #expect(!report.contains("Balance"))
}

@Test func clientExportNameIsFileSafe() {
    #expect(clientExportName(day, "A/B: C") == "Outatime 2026-09 A-B- C.xlsx")
}
```

- [ ] **Step 2: Run, expect failure.**

- [ ] **Step 3: Implement**

```swift
// CSV.entries
static func entries(_ entries: [Entry], names: [Profile.ID: String] = [:]) -> String {
    ... header + ["Client"], row + [e.profile.flatMap { names[$0] } ?? ""]
}

// Report
static func month(_ entries: [Entry], month: Date, target: Target, names: [Profile.ID: String] = [:], now: Date = .now) -> Data {
    let days = DaySummary.days(month.daysInMonth, entries, target: target, now: now)
    return XLSX.workbook([summary(days, totals: true)] + [clients(entries, names: names)].compactMap { $0 } + [list(entries, names: names)])
}
// master: pass names to list.

/// A client's month: billable hours per day and the blocks behind them, to attach to an invoice. No target or balance.
static func client(_ entries: [Entry], name: String, month: Date) -> Data {
    let billed = entries.filter(\.activity.billable).sorted { $0.start < $1.start }
    let byDay = Dictionary(grouping: billed) { Calendar.current.startOfDay(for: $0.start) }
    let rows = byDay.keys.sorted().map { d -> [Cell] in
        let t = Store.totals(byDay[d]!)
        return [.number(XLSX.serial(d), .date)] + billable.map { .number(t[$0, default: 0] / 3600, .hours) }
            + [.number(t.values.reduce(0, +) / 3600, .hours), .text(byDay[d]!.flatMap(\.notes).joined(separator: "; "))]
    }
    let sums = (0...billable.count).map { i in
        (column: i + 1, total: rows.map { if case .number(let v, _) = $0[i + 1] { v } else { 0 } }.reduce(0, +), style: XLSX.Style.totalHours)
    }
    let days = XLSX.tableSheet("Days", table: "tblClientDays", columns: ["Date"] + billable.map(\.title) + ["Total", "Notes"],
                               rows: rows, sums: sums, widths: [16, 10, 10, 10, 10, 40])
    let id = UUID()
    return XLSX.workbook([days, list(billed.map { var e = $0; e.profile = id; return e }, names: [id: name])])
}

private static let billable = Activity.allCases.filter(\.billable)

/// Billable hours per client; omitted when no block has a client.
private static func clients(_ entries: [Entry], names: [Profile.ID: String]) -> XLSX.Sheet? {
    let billed = entries.filter(\.activity.billable)
    guard billed.contains(where: { $0.profile != nil }) else { return nil }
    let groups = Dictionary(grouping: billed) { $0.profile.flatMap { names[$0] } }
    let keys = groups.keys.sorted { ($0 == nil ? 1 : 0, $0 ?? "") < ($1 == nil ? 1 : 0, $1 ?? "") }  // "No client" last
    let totals = keys.map { Store.totals(groups[$0]!) }
    let rows = zip(keys, totals).map { key, t -> [Cell] in
        [.text(key ?? "No client")] + billable.map { .number(t[$0, default: 0] / 3600, .hours) } + [.number(t.values.reduce(0, +) / 3600, .hours)]
    }
    let sums = (0...billable.count).map { i in
        (column: i + 1, total: totals.map { i < billable.count ? $0[billable[i], default: 0] : $0.values.reduce(0, +) }.reduce(0, +) / 3600,
         style: XLSX.Style.totalHours)
    }
    return XLSX.tableSheet("Clients", table: "tblClients", columns: ["Client"] + billable.map(\.title) + ["Total"],
                           rows: rows, sums: sums, widths: [24, 10, 10, 10, 10])
}

// list gains `names:` and a trailing "Client" column (width 18): .text(e.profile.flatMap { names[$0] } ?? "")

/// "Outatime 2026-09 Acme.xlsx"; slashes and colons in a client's name would break the file name.
nonisolated func clientExportName(_ month: Date, _ name: String) -> String {
    exportName(month, " " + name.replacing(/[\/:]/, with: "-") + ".xlsx")
}
```

- [ ] **Step 4: Run tests, expect PASS** (existing `monthReportWorkbook`/`masterWorkbook` still pass; their entries have no client).
- [ ] **Step 5: Commit** `Clients: Client column, Clients sheet and per-client month report`.

### Task 4: UI and localization

**Files:** Modify `Outatime/MenuPanel.swift`, `Outatime/Settings.swift`, `Outatime/EditorView.swift`, `Outatime/Localizable.xcstrings`

**Consumes:** Tasks 1–3.

- [ ] **Step 1: Menu panel.** Status: after the activity label,
  `if let name = running.profile.flatMap({ store.profileNames[$0] }) { Text("· \(name)").foregroundStyle(.secondary) }`.
  Under the notes block: `if !store.activeProfiles.isEmpty { ClientPicker() }`:

```swift
/// The client new Work, Extra and Travel blocks are tracked for. Segmented while it fits, a menu beyond that.
private struct ClientPicker: View {
    @Environment(Store.self) private var store

    var body: some View {
        let clients = store.activeProfiles
        let picker = Picker("Client", selection: Binding(get: { store.currentProfile }, set: { store.select($0) })) {
            Text("No client").tag(Profile.ID?.none)
            ForEach(clients) { Text($0.name).tag(Optional($0.id)) }
        }
        .labelsHidden()
        if clients.count <= 3 { picker.pickerStyle(.segmented) } else { picker.pickerStyle(.menu) }
    }
}
```

  Export menu, after the month report:
  `ForEach(clients with billable time this month incl. archived) { p in Button("This Month — \(p.name) (Excel)…") { save(Report.client(monthEntries.filter { $0.profile == p.id }, name: p.name, month: month), as: .xlsx, suggestedName: clientExportName(month, p.name)) } }`;
  pass `names: store.profileNames` to `Report.month`, `Report.master`, `CSV.entries`.

- [ ] **Step 2: Settings.** Section "Clients" between "Daily target" and "Tracking": one `TextField` per active client
  bound through `store.rename`, a borderless `minus.circle` "Remove" button calling `store.removeProfile`; a "New client"
  field + "Add" button calling `store.addProfile` and clearing the field; caption
  "Pick the client in the menu. Work, Extra and Travel are tracked for it."

- [ ] **Step 3: Logbook.** `TimelineBlock` title appends `Text("· \(name)")` for a block with a client.
  `EntryForm` reads `@Environment(Store.self)`; when `entry.activity.billable` and there are clients (active, plus the
  entry's own if archived) it shows `Picker("Client", selection: $entry.profile)` with "No client"; `.onChange(of:
  entry.activity)` clears `profile` for non-billable activities.

- [ ] **Step 4: Localization.** Add `es` and `pt-BR` for: "Client", "No client", "Clients", "New client", "Add",
  "Remove", "This Month — %@ (Excel)…", "Pick the client in the menu. Work, Extra and Travel are tracked for it.".

- [ ] **Step 5: Build, run the full suite, launch the app and check:** picker appears after adding a client, switching
  splits a running Work block in the Logbook, editor picker works, client export opens.
- [ ] **Step 6: Commit** `Clients: menu picker, Settings section, Logbook labels`.
