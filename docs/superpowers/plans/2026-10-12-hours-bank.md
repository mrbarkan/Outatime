# Hours Bank Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every deposit into and withdrawal from the hours bank explicit (time off, holidays, carried hours), and
log Travel and Out of Office for whole days instead of timing them, on the Mac and Windows apps alike.

**Architecture:** Two new lists in `data.json` (`timeOff`, `carried`); holidays stay in `daysOff`. `Target` learns
how much of each day is booked off (`booked`), which lowers what the day owes; one `bank` function subtracts booked time
off and adds carried hours on top of the day balances. A new Hours Bank sheet/window and Log forms edit the records;
logging Travel/Out of Office makes ordinary blocks. The Windows Core mirrors the Mac logic line for line, and both read
and write the same file.

**Tech Stack:** Swift 6 / SwiftUI / Swift Testing (Mac, `Outatime/`); C# / .NET 10 / Avalonia / xUnit (Windows,
`windows/`).

**Spec:** `docs/superpowers/specs/2026-10-12-hours-bank-design.md`

## Global Constraints

- Day keys are `"yyyy-MM-dd"` in the local time zone (`Date.dayKey` / `DayKey()`), exactly like `daysOff`.
- `data.json`: keys sorted, upper-case UUIDs, nil optionals omitted, `timeOff` and `carried` always written (possibly
  empty), every record's `note` always written. `TimeOff.hours` omitted when nil.
- Mac tests: `xcodebuild -project Outatime.xcodeproj -scheme Outatime test` (includes `catalogIsComplete`: every key in
  `Localizable.xcstrings` needs es, pt-BR, fr, de, it, ja, zh-Hans).
- Windows tests: `~/.dotnet/dotnet test windows/tests/Outatime.Tests` (includes `EveryStringIsTranslated`: es, pt-BR).
- Never launch the Mac app against real data to "check" things: the dev build shares the real logbook. Tests only.
- Exported column names and sheet names are English and never translated.
- Match surrounding style: terse doc comments, `ponytail:` for known shortcuts, no new dependencies.

## Review Focus

1. **Carried hours dated before tracking began** must count while "Hours bank since" is unset; that's the starting
   balance. → test `bankCountsTimeOffAndCarriedHours` (Task 1 / Task 5) case A.
2. **A time-off range spanning a weekend or a holiday** must not charge those days. → `timeOffLowersWhatADayOwes`.
3. **Logging Travel onto a day that already has blocks, or past midnight**, must not overlap or spill into the next day.
   → `loggingTravelFillsWorkdays`.
4. **Editing a holiday range** must remove the old days, not leave strays. → `holidaysGroupIntoRanges`.
5. **Windows reading/writing a Mac file with the new keys** must round-trip byte-for-byte. → `WritesWhatTheMacWrites`
   with the extended fixture (Task 5).

---

## File Map

Mac
- Modify `Outatime/Models.swift`: `TimeOff`, `Carried`, `HoursBank`; `Target.booked`, `isWorkday`, `book`, `cost(of:)`;
  `Date(dayKey:)`, `Date.days(from:to:)`.
- Modify `Outatime/Store.swift`: `timeOff`, `carried`, `editingBank`, file keys; `target()` books time off; `bank`;
  `holidays`, `setHoliday`, `removeHoliday`; `log`; `masterWorkbook` passes records.
- Modify `Outatime/Stats.swift`: `carried:` parameter, bank via `Store.bank`.
- Modify `Outatime/Export.swift`: `DaySummary.timeOff`, Time Off column, CSV trailing column, master Hours Bank sheet and
  card.
- Create `Outatime/HoursBank.swift`: `LogKind`, `LogItem`, `BankContext`, `HoursBankView`, `LogForm`.
- Modify `Outatime/EditorView.swift`: Hours Bank toolbar button and sheet, day-row icons, Stats gets carried, `signed`
  made internal.
- Modify `Outatime/MenuPanel.swift`: 2×2 tiles, Stop for leftover Travel/OoO, Log menu, clickable bank row.
- Modify `Outatime/Objectives.swift`: Vacation progress reads `store.bank`.
- Modify `Outatime/OutatimeApp.swift`: `log` window.
- Modify `Outatime/Localizable.xcstrings`, `Outatime/WhatsNew.swift`.
- Modify `OutatimeTests/OutatimeTests.swift`.

Windows
- Modify `windows/src/Outatime.Core/Models.cs`, `Cal.cs`, `Store.cs`, `Json.cs`, `Stats.cs`, `Export.cs`,
  `WhatsNew.cs`, `Strings.json` (generated), `Strings.Windows.json`.
- Create `windows/src/Outatime/HoursBankWindow.cs` (`LogKind`, `LogItem`, the window), `windows/src/Outatime/LogWindow.cs`.
- Modify `windows/src/Outatime/Shell.cs`, `MenuPanel.cs`, `Logbook/LogbookWindow.cs`, `DevSupport.cs`, `Ui.cs`.
- Modify `windows/tests/Outatime.Tests/CoreTests.cs`, `CompatibilityTests.cs`, `UiTests.cs`, `fixtures/mac-data.json`.
- Modify `.github/workflows/windows.yml` (snapshot the new windows).

Docs: `docs/manual/**/index.html` (8 languages), `README.md`, `windows/README.md`.

---

### Task 1: Mac core — records, booked time off, the bank

**Files:**
- Modify: `Outatime/Models.swift`, `Outatime/Store.swift`, `Outatime/Stats.swift`
- Test: `OutatimeTests/OutatimeTests.swift`

**Interfaces:**
- Produces: `TimeOff(id:start:end:hours:note:)`, `Carried(id:date:hours:note:)`, `HoursBank { balance, worked, bookedAhead }`,
  `Target.booked: [String: TimeInterval]`, `Target.isWorkday(_:)`, `Target.book(_:)` (mutating), `Target.cost(of:)`,
  `Date(dayKey:)`, `Date.days(from:to:) -> [Date]`, `Store.timeOff`, `Store.carried`, `Store.editingBank`,
  `Store.bank(_ target:since:now:) -> HoursBank`, `static Store.bank(_:carried:target:since:now:)`,
  `Stats(_:target:bankSince:carried:now:)`.

- [ ] **Step 1: Write the failing tests** — add to `OutatimeTests` (after `periodBalance`):

```swift
    func d(_ day: Int, _ month: Int = 9) -> Date { cal.date(from: DateComponents(year: 2026, month: month, day: day))! }

    /// Thu 3 Sep a full day off; Fri 4 – Mon 7 4h a day, the weekend free and Mon a holiday; 10h asks for more than a day.
    @Test func timeOffLowersWhatADayOwes() {
        var t = Target(seconds: 8 * 3600, daysOff: ["2026-09-07"], since: day)
        t.book([TimeOff(start: "2026-09-03", end: "2026-09-03"), TimeOff(start: "2026-09-04", end: "2026-09-07", hours: 4),
                TimeOff(start: "2026-09-08", end: "2026-09-08", hours: 10)])
        #expect(t.booked == ["2026-09-03": 8 * 3600, "2026-09-04": 4 * 3600, "2026-09-08": 8 * 3600])
        #expect(t.scheduled(on: day) == 0)
        #expect(t.scheduled(on: d(4)) == 4 * 3600)
        #expect(t.scheduled(on: d(9)) == 8 * 3600)
        #expect(t.cost(of: TimeOff(start: "2026-09-04", end: "2026-09-07", hours: 4)) == 4 * 3600)
        // Overlapping records never take more than a day.
        t.book([TimeOff(start: "2026-09-04", end: "2026-09-04", hours: 6)])
        #expect(t.booked["2026-09-04"] == 8 * 3600)
        // Before tracking began costs nothing.
        #expect(t.cost(of: TimeOff(start: "2026-09-01", end: "2026-09-01")) == 0)
    }

    /// Thu 3 worked 9h (+1), Fri 4 off from the bank, Mon 7 worked 8h, now Tue 8 14:00 with nothing yet; Thu 10 booked
    /// ahead. Carried: +32h dated 31 Aug, before tracking began, and +10h on Mon 7.
    @Test func bankCountsTimeOffAndCarriedHours() {
        let entries = [Entry(activity: .work, start: at(9), end: at(18)),
                       Entry(activity: .work, start: d(7) + 9 * 3600, end: d(7) + 17 * 3600)]
        var target = Target(seconds: 8 * 3600, since: day)
        target.book([TimeOff(start: "2026-09-04", end: "2026-09-04"), TimeOff(start: "2026-09-10", end: "2026-09-10")])
        let carried = [Carried(date: "2026-08-31", hours: 32), Carried(date: "2026-09-07", hours: 10)]
        let now = d(8) + 14 * 3600
        // A: the bank starts when tracking began, so every carried record counts: +1 − 16 + 42.
        let a = Store.bank(entries, carried: carried, target: target, since: day, now: now)
        #expect(a.balance == 27 * 3600 && a.bookedAhead == 8 * 3600 && a.worked == 17 * 3600)
        // B: moved later, to Fri 4: Thu's surplus and the August hours drop out: 0 − 16 + 10.
        #expect(Store.bank(entries, carried: carried, target: target, since: d(4), now: now).balance == -6 * 3600)
    }

    @Test @MainActor func bankRecordsRoundTrip() throws {
        let url = FileManager.default.temporaryDirectory.appending(path: "outatime-test-\(UUID().uuidString)/data.json")
        defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }
        let store = Store(url: url)
        #expect(store.timeOff.isEmpty && store.carried.isEmpty)
        store.timeOff = [TimeOff(start: "2026-12-24", end: "2027-01-01", note: "Holidays"), TimeOff(start: "2026-09-04", end: "2026-09-04", hours: 4)]
        store.carried = [Carried(date: "2026-08-31", hours: -8.5, note: "Paid out")]
        let reloaded = Store(url: url)
        #expect(reloaded.timeOff == store.timeOff && reloaded.carried == store.carried)
        let json = try String(contentsOf: url, encoding: .utf8)
        #expect(json.contains("\"carried\" : [") && json.contains("\"timeOff\" : ["))
        #expect(json.components(separatedBy: "\"hours\"").count == 3)  // the full-day record leaves hours out
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `xcodebuild -project Outatime.xcodeproj -scheme Outatime test -only-testing:OutatimeTests/OutatimeTests 2>&1 | tail -20`
Expected: build fails — `cannot find 'TimeOff' in scope`.

- [ ] **Step 3: Implement — `Models.swift`**

After `DayTemplate`, add:

```swift
/// Time taken off workdays and paid from the hours bank: `start`…`end` (day keys, both included), each workday costing
/// `hours`, or with none a whole day's target, so a new target re-prices full days.
nonisolated struct TimeOff: Codable, Identifiable, Hashable {
    var id = UUID()
    var start: String
    var end: String
    var hours: Double?
    var note = ""
}

/// Hours put into the bank from outside the Logbook, like overtime from before tracking began, or taken out (negative)
/// when they're paid.
nonisolated struct Carried: Codable, Identifiable, Hashable {
    var id = UUID()
    var date: String  // day key
    var hours: Double
    var note = ""
}

/// What the hours bank holds, the time worked behind it, and how much of it is already booked as time off ahead.
nonisolated struct HoursBank {
    var balance: TimeInterval
    var worked: TimeInterval
    var bookedAhead: TimeInterval
}
```

Replace `struct Target` with:

```swift
/// The daily target and which days owe it.
nonisolated struct Target {
    var seconds: TimeInterval
    var excluded: String = Activity.defaultExcluded
    var daysOff: Set<String> = []
    var since: Date  // tracking began; nothing is owed before it
    /// Seconds of each day taken off from the bank, by day key; see `book`.
    var booked: [String: TimeInterval] = [:]

    /// Weekends, days off, days before tracking began and days still ahead owe nothing.
    // ponytail: workdays are the locale's weekdays; a per-weekday schedule when someone works Saturdays.
    func owed(on day: Date, now: Date = .now) -> TimeInterval {
        Calendar.current.startOfDay(for: day) <= now ? scheduled(on: day) : 0
    }

    /// What a day will owe once it comes, so a week's goal counts the days still ahead. Time off lowers it.
    func scheduled(on day: Date) -> TimeInterval {
        let d = Calendar.current.startOfDay(for: day)
        guard isWorkday(d), d >= Calendar.current.startOfDay(for: since) else { return 0 }
        return max(0, seconds - (booked[d.dayKey] ?? 0))
    }

    /// Not a weekend or a holiday.
    func isWorkday(_ day: Date) -> Bool {
        !Calendar.current.isDateInWeekend(day) && !daysOff.contains(day.dayKey)
    }

    /// What a day adds to a week/month balance. Today is still in progress: it can add a surplus, not a shortfall yet.
    func balance(worked: TimeInterval, on day: Date, now: Date = .now) -> TimeInterval {
        let b = worked - owed(on: day, now: now)
        return Calendar.current.isDate(day, inSameDayAs: now) ? max(0, b) : b
    }

    /// Spreads time off over the workdays it covers, never more than a day's target on one day.
    mutating func book(_ records: [TimeOff]) {
        for r in records {
            for d in days(of: r) { booked[d.dayKey] = min(seconds, (booked[d.dayKey] ?? 0) + perDay(r)) }
        }
    }

    /// What one record takes from the bank, overlaps with other records aside.
    func cost(of r: TimeOff) -> TimeInterval { Double(days(of: r).count) * perDay(r) }

    private func perDay(_ r: TimeOff) -> TimeInterval { min(seconds, r.hours.map { $0 * 3600 } ?? seconds) }

    /// Workdays a record covers since tracking began: weekends, holidays and earlier days cost nothing.
    private func days(of r: TimeOff) -> [Date] {
        let start = Calendar.current.startOfDay(for: since)
        return Date.days(from: r.start, to: r.end).filter { isWorkday($0) && $0 >= start }
    }
}
```

In the `nonisolated extension Date`, add:

```swift
    /// The day a day key names, at local midnight; nil for a malformed key.
    init?(dayKey: String) {
        let parts = dayKey.split(separator: "-").compactMap { Int($0) }
        guard parts.count == 3,
              let d = Calendar.current.date(from: DateComponents(year: parts[0], month: parts[1], day: parts[2])) else { return nil }
        self = d
    }

    /// Every day from one key to the other, both included; none when either is malformed or they're reversed.
    static func days(from start: String, to end: String) -> [Date] {
        guard let a = Date(dayKey: start), let b = Date(dayKey: end) else { return [] }
        let cal = Calendar.current
        return Array(sequence(first: a) { cal.date(byAdding: .day, value: 1, to: $0) }.prefix { $0 <= b })
    }
```

- [ ] **Step 4: Implement — `Store.swift`**

Properties, after `objectives`:

```swift
    var timeOff: [TimeOff] = [] { didSet { if loaded { save() } } }
    var carried: [Carried] = [] { didSet { if loaded { save() } } }
    /// The menu asks the Logbook to open its Hours Bank sheet.
    var editingBank = false
```

`File` gains `var timeOff: [TimeOff]?  // added in 1.3` and `var carried: [Carried]?  // added in 1.3`. In `init`, after
`objectives = …`: `timeOff = file.timeOff ?? []` and `carried = file.carried ?? []`. In `save()`, pass
`timeOff: timeOff, carried: carried` to `File(...)` (memberwise order: entries, templates, daysOff, profiles, current,
objectives, timeOff, carried).

Replace `target(hours:excluded:)` and add the bank after `balance(...)`:

```swift
    func target(hours: Double, excluded: String) -> Target {
        var t = Target(seconds: hours * 3600, excluded: excluded, daysOff: daysOff, since: entries.map(\.start).min() ?? .now)
        t.book(timeOff)
        return t
    }

    func bank(_ target: Target, since: Date, now: Date = .now) -> HoursBank {
        Self.bank(entries, carried: carried, target: target, since: since, now: now)
    }

    /// The day balances since `since`, less the time off booked from then on (days ahead too: booked is spent), plus the
    /// hours carried in. Carried hours are usually dated before tracking began, so they only drop out once the bank is
    /// made to start later than that.
    nonisolated static func bank(_ entries: [Entry], carried: [Carried], target: Target, since: Date, now: Date = .now) -> HoursBank {
        let cal = Calendar.current
        let start = cal.startOfDay(for: since), today = cal.startOfDay(for: now)
        let days = balance(entries, in: DateInterval(start: start, end: max(start, now)), target: target, now: now)
        var off = 0.0, ahead = 0.0
        for (key, seconds) in target.booked {
            guard let d = Date(dayKey: key), d >= start else { continue }
            off += seconds
            if d > today { ahead += seconds }
        }
        let all = start <= cal.startOfDay(for: target.since)
        let brought = carried.filter { c in all || Date(dayKey: c.date).map { $0 >= start } == true }.map { $0.hours * 3600 }.reduce(0, +)
        return HoursBank(balance: days.balance - off + brought, worked: days.worked, bookedAhead: ahead)
    }
```

- [ ] **Step 5: Implement — `Stats.swift`**

Signature: `init(_ entries: [Entry], target: Target, bankSince: Date, carried: [Carried] = [], now: Date = .now)` and
the bank line becomes:

```swift
        bank = Store.bank(entries, carried: carried, target: target, since: bankSince, now: now).balance
```

- [ ] **Step 6: Run the tests**

Run: `xcodebuild -project Outatime.xcodeproj -scheme Outatime test 2>&1 | grep -E "Test run|failed|error:" | head -20`
Expected: all pass (including the existing `periodBalance`, `stats`, `breakAndDaysOffAtTarget`).

- [ ] **Step 7: Commit**

```bash
git add Outatime/Models.swift Outatime/Store.swift Outatime/Stats.swift OutatimeTests/OutatimeTests.swift
git commit -m "Hours bank: time off and carried hours in the data file; booked time off lowers a day's target"
```

---

### Task 2: Mac Store — holidays as ranges, logging Travel and Out of Office

**Files:**
- Modify: `Outatime/Store.swift`
- Test: `OutatimeTests/OutatimeTests.swift`

**Interfaces:**
- Consumes: `Target.isWorkday`, `Target.booked`, `Date.days(from:to:)`, `Date(dayKey:)` (Task 1).
- Produces: `Store.holidays: [ClosedRange<Date>]` (newest first), `Store.setHoliday(from:to:replacing:)`,
  `Store.removeHoliday(_:)`, `Store.log(_:from:to:hours:profile:target:usualStart:now:)`.

- [ ] **Step 1: Write the failing tests**

```swift
    @Test @MainActor func holidaysGroupIntoRanges() {
        let url = FileManager.default.temporaryDirectory.appending(path: "outatime-test-\(UUID().uuidString)/data.json")
        defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }
        let store = Store(url: url)
        store.daysOff = ["2026-09-07"]
        store.setHoliday(from: d(24), to: d(26))
        #expect(store.holidays == [d(24)...d(26), d(7)...d(7)])
        store.setHoliday(from: d(24), to: d(25), replacing: d(24)...d(26))
        #expect(store.daysOff == ["2026-09-07", "2026-09-24", "2026-09-25"])
        store.removeHoliday(d(7)...d(7))
        #expect(store.daysOff == ["2026-09-24", "2026-09-25"])
    }

    /// Thu 3 – Wed 9 Sep: Thu has a block until 11:00, Mon 7 is a holiday, Tue 8 a full day off.
    @Test @MainActor func loggingTravelFillsWorkdays() throws {
        let url = FileManager.default.temporaryDirectory.appending(path: "outatime-test-\(UUID().uuidString)/data.json")
        defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }
        let store = Store(url: url)
        let acme = try #require(store.addProfile("Acme"))
        store.entries = [Entry(activity: .work, start: at(9), end: at(11))]
        store.daysOff = ["2026-09-07"]
        store.timeOff = [TimeOff(start: "2026-09-08", end: "2026-09-08")]
        let target = store.target(hours: 8, excluded: Activity.defaultExcluded)
        store.log(.travel, from: day, to: d(9), hours: 6, profile: acme, target: target, usualStart: 8 * 60 + 30)
        let travel = store.entries.filter { $0.activity == .travel }.sorted { $0.start < $1.start }
        #expect(travel.map(\.start.dayKey) == ["2026-09-03", "2026-09-04", "2026-09-09"])
        #expect(travel[0].start == at(11) && travel[0].duration == 6 * 3600)  // after the day's last block
        #expect(travel[1].start == d(4) + 8.5 * 3600)  // usual start on an empty day
        #expect(travel.allSatisfy { $0.profile == acme })
        // Out of Office never has a client; a block that would run past midnight stops there.
        store.log(.outOfOffice, from: d(10), to: d(10), hours: 8, profile: acme, target: target, usualStart: 20 * 60)
        let ooo = try #require(store.entries.first { $0.activity == .outOfOffice })
        #expect(ooo.profile == nil && ooo.duration == 4 * 3600)
        // No usual start yet: 9:00.
        store.log(.outOfOffice, from: d(11), to: d(11), hours: 1, profile: nil, target: target, usualStart: nil)
        #expect(store.entries.contains { $0.activity == .outOfOffice && $0.start == d(11) + 9 * 3600 })
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `xcodebuild -project Outatime.xcodeproj -scheme Outatime test -only-testing:OutatimeTests/OutatimeTests 2>&1 | tail -5`
Expected: build fails — `value of type 'Store' has no member 'setHoliday'`.

- [ ] **Step 3: Implement** — in `Store.swift`, a new `// MARK: Hours bank` section before `// MARK: Exports`:

```swift
    // MARK: Hours bank

    /// Days off as runs of consecutive days, newest first: how the Hours Bank lists holidays.
    var holidays: [ClosedRange<Date>] {
        let cal = Calendar.current
        var runs: [ClosedRange<Date>] = []
        for d in daysOff.compactMap({ Date(dayKey: $0) }).sorted() {
            if let last = runs.last, cal.date(byAdding: .day, value: 1, to: last.upperBound) == d {
                runs[runs.count - 1] = last.lowerBound...d
            } else {
                runs.append(d...d)
            }
        }
        return runs.reversed()
    }

    /// Marks `from`…`to` as holidays, in place of `old` when one is being edited.
    func setHoliday(from: Date, to: Date, replacing old: ClosedRange<Date>? = nil) {
        var days = daysOff
        if let old { days.subtract(Date.days(from: old.lowerBound.dayKey, to: old.upperBound.dayKey).map(\.dayKey)) }
        days.formUnion(Date.days(from: from.dayKey, to: to.dayKey).map(\.dayKey))
        daysOff = days
    }

    func removeHoliday(_ run: ClosedRange<Date>) {
        daysOff.subtract(Date.days(from: run.lowerBound.dayKey, to: run.upperBound.dayKey).map(\.dayKey))
    }

    /// Travel or Out of Office logged rather than timed: a block on each workday from `from` to `to` that isn't taken
    /// off in full, after the day's last block, or else at the usual start (minutes after midnight; 9:00 without one),
    /// `hours` long and cut at midnight. Only Travel keeps the client.
    func log(_ activity: Activity, from: Date, to: Date, hours: Double, profile: Profile.ID?, target: Target, usualStart: Int?,
             now: Date = .now) {
        let cal = Calendar.current
        let minute = usualStart ?? 9 * 60
        var added: [Entry] = []
        for d in Date.days(from: from.dayKey, to: to.dayKey) where target.isWorkday(d) {
            if target.seconds > 0, (target.booked[d.dayKey] ?? 0) >= target.seconds { continue }
            let midnight = cal.date(byAdding: .day, value: 1, to: d)!
            let start = entries(on: d).map { $0.end ?? now }.max()
                ?? cal.date(bySettingHour: minute / 60, minute: minute % 60, second: 0, of: d)!
            let end = min(start + hours * 3600, midnight)
            guard start < end else { continue }
            added.append(Entry(activity: activity, start: start, end: end, profile: activity.billable ? profile : nil))
        }
        entries += added  // one save
    }
```

- [ ] **Step 4: Run the tests** — same command as Task 1 Step 6. Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add Outatime/Store.swift OutatimeTests/OutatimeTests.swift
git commit -m "Store: holidays as ranges, Travel and Out of Office logged over a stretch of days"
```

---

### Task 3: Mac exports — Time Off column, Hours Bank sheet

**Files:**
- Modify: `Outatime/Export.swift`, `Outatime/Store.swift` (`masterWorkbook`)
- Test: `OutatimeTests/OutatimeTests.swift`

**Interfaces:**
- Consumes: `Target.booked`, `TimeOff`, `Carried`, `Store.bank` (Task 1).
- Produces: `DaySummary.timeOff`, `Report.master(_:target:names:timeOff:carried:now:)`.

- [ ] **Step 1: Update and add tests**

In `dailyCSV`, the expected lines gain a trailing Time Off column:

```swift
        #expect(lines[1] == "2026-09-03,8.00,0.00,0.00,1.50,0.00,0.00,+1.50,\"a, \"\"b\"\"\",0.00")
        #expect(lines[2] == "Total,8.00,0.00,0.00,1.50,0.00,0.00,+1.50,,0.00")
```

In `monthReportWorkbook`, Balance moves from J to K and the table gains a column:

```swift
        #expect(text.contains(#"<c r="K2" s="5"><v>1.0</v></c>"#))  // balance: 9h worked, 8h owed
        #expect(text.contains(#"name="tblDays" displayName="tblDays" ref="A1:L3" totalsRowCount="1""#))
```

In `masterWorkbook`: `ref="A1:L3" totalsRowShown="0"` and the card formula becomes
`"<f>SUM(tblDays[Balance])+SUM(tblBank[Hours])</f><v>-7.0</v>"`.

Add:

```swift
    /// Thu 3 +1h, Fri 4 off (today, 20:00), 32h carried in: the workbook's bank is the app's.
    @Test func masterWorkbookCountsTheBank() {
        let off = [TimeOff(start: "2026-09-04", end: "2026-09-04", note: "Dentist")]
        var target = Target(seconds: 8 * 3600, since: day)
        target.book(off)
        let entries = [Entry(activity: .work, start: at(9), end: at(18))]
        let carried = [Carried(date: "2026-08-31", hours: 32, note: "Jan–Aug")]
        let now = d(4) + 20 * 3600
        let text = String(decoding: Report.master(entries, target: target, timeOff: off, carried: carried, now: now), as: UTF8.self)
        #expect(text.contains("<f>SUM(tblDays[Balance])+SUM(tblBank[Hours])</f><v>25.0</v>"))
        #expect(text.contains(#"name="tblBank""#) && text.contains(">Dentist<") && text.contains(">Carried<"))
        #expect(Store.bank(entries, carried: carried, target: target, since: day, now: now).balance == 25 * 3600)
        // A day off with nothing logged still gets a Summary row, with its Time Off.
        let month = String(decoding: Report.month(entries, month: day, target: target, now: now), as: UTF8.self)
        #expect(month.contains(#"ref="A1:L4""#))
    }
```

- [ ] **Step 2: Run to verify they fail** — expected: `extra argument 'timeOff' in call` and the changed expectations fail.

- [ ] **Step 3: Implement — `Export.swift`**

`DaySummary`: add `var timeOff: TimeInterval` after `owed, balance`, and in `days(...)`:

```swift
            let owed = target.owed(on: date, now: now), off = target.booked[date.dayKey] ?? 0
            guard !es.isEmpty || owed > 0 || off > 0 else { return nil }
            let t = Store.totals(es), worked = t.worked(excluding: target.excluded)
            return DaySummary(date: date, hours: t, worked: worked, owed: owed, balance: target.balance(worked: worked, on: date, now: now),
                              timeOff: off, notes: es.flatMap(\.notes).joined(separator: "; "))
```

(Update the doc comment: "a day with entries, owed hours or time off".) Memberwise order: `date, hours, worked, owed,
balance, timeOff, notes` — declare `var worked, owed, balance: TimeInterval` then `var timeOff: TimeInterval` then
`var notes: String`.

`CSV.daily`: header `… + ["Balance", "Notes", "Time Off"]`, rows `… + [signed(d.balance), d.notes, hours(d.timeOff)]`,
total `… + [signed(sum(\.balance)), "", hours(sum(\.timeOff))]`. Doc comment: "Time Off last, so existing columns
keep their places."

`Report.values` totals: `[("Worked", …), ("Target", …), ("Time Off", { $0.timeOff / 3600 }), ("Balance", …)]`
(Balance stays last, so `balance = values.count - 1` holds).

`Report.master`:

```swift
    /// Everything since tracking began, opening on a dashboard.
    static func master(_ entries: [Entry], target: Target, names: [Profile.ID: String] = [:], timeOff: [TimeOff] = [],
                       carried: [Carried] = [], now: Date = .now) -> Data {
        let cal = Calendar.current
        let dates = sequence(first: cal.startOfDay(for: target.since)) { cal.date(byAdding: .day, value: 1, to: $0) }.prefix { $0 <= now }
        let days = DaySummary.days(Array(dates), entries, target: target, now: now)
        let bank = bank(target, timeOff: timeOff, carried: carried)
        return XLSX.workbook([dashboard(days, bank: bank.total, target: target, now: now), summary(days, totals: false),
                              list(entries.sorted { $0.start < $1.start }, names: names), bank.sheet])
    }

    /// Carried hours and each day taken off, signed as they move the bank. The dashboard's bank card adds them to the
    /// day balances.
    private static func bank(_ target: Target, timeOff: [TimeOff], carried: [Carried]) -> (sheet: XLSX.Sheet, total: Double) {
        let note = { (key: String) in timeOff.first { $0.start <= key && key <= $0.end }?.note ?? "" }
        let rows = (carried.map { (date: $0.date, kind: "Carried", hours: $0.hours, note: $0.note) }
            + target.booked.map { (date: $0.key, kind: "Time Off", hours: -$0.value / 3600, note: note($0.key)) })
            .sorted { $0.date < $1.date }
        let cells = rows.map { r -> [Cell] in
            [Date(dayKey: r.date).map { .number(XLSX.serial($0), .date) } ?? .text(r.date), .text(r.kind), .number(r.hours, .balance), .text(r.note)]
        }
        return (XLSX.tableSheet("Hours Bank", table: "tblBank", columns: ["Date", "Kind", "Hours", "Note"], rows: cells, widths: [16, 12, 10, 40]),
                rows.map(\.hours).reduce(0, +))
    }
```

`dashboard(_:bank:target:now:)` — new `bank: Double` parameter; the HOURS BANK card:

```swift
            ("HOURS BANK", .formula("SUM(tblDays[Balance])+SUM(tblBank[Hours])", cached: sum(\.balance, days) + bank, .cardBank),
             "worked minus target, plus carried, less time off"),
```

- [ ] **Step 4: Implement — `Store.masterWorkbook`**

```swift
        Report.master(entries, target: target, names: profileNames, timeOff: timeOff, carried: carried, now: now)
```

- [ ] **Step 5: Run the tests** — expected: all pass. If the cached-number format differs from `25.0`, read it from the
  failing output and match the existing `-7.0` style.

- [ ] **Step 6: Commit**

```bash
git add Outatime/Export.swift Outatime/Store.swift OutatimeTests/OutatimeTests.swift
git commit -m "Exports: a Time Off column, and an Hours Bank sheet the master workbook's bank card adds in"
```

---

### Task 4: Mac UI — Hours Bank sheet, Log forms, menu and Logbook changes, translations

**Files:**
- Create: `Outatime/HoursBank.swift`
- Modify: `Outatime/EditorView.swift`, `Outatime/MenuPanel.swift`, `Outatime/Objectives.swift`, `Outatime/OutatimeApp.swift`,
  `Outatime/Localizable.xcstrings`, `Outatime/WhatsNew.swift`

**Interfaces:**
- Consumes: everything from Tasks 1–3.
- Produces: `BankContext` (`store`, `targetHours`, `target`, `since`, `bank`), `HoursBankView()`, `LogForm(item:)`,
  `LogKind` (rawValues `timeOff`, `holiday`, `travel`, `outOfOffice`, `carried`), window id `"log"` taking a `LogKind`
  rawValue.

- [ ] **Step 1: Create `Outatime/HoursBank.swift`**

```swift
import SwiftUI

/// What a Log form records: time off and holidays, Travel and Out of Office blocks, or hours carried in.
enum LogKind: String, CaseIterable, Identifiable {
    case timeOff, holiday, travel, outOfOffice, carried
    var id: String { rawValue }

    var label: LocalizedStringKey {
        switch self {
        case .timeOff: "Time Off"
        case .holiday: "Holiday"
        case .travel: "Travel"
        case .outOfOffice: "Out of Office"
        case .carried: "Carried Hours"
        }
    }

    var symbol: String {
        switch self {
        case .timeOff: "clock.arrow.circlepath"
        case .holiday: "beach.umbrella"
        case .travel: Activity.travel.symbol
        case .outOfOffice: Activity.outOfOffice.symbol
        case .carried: "tray.and.arrow.down"
        }
    }

    var color: Color {
        switch self {
        case .timeOff: .cyan
        case .holiday: .secondary
        case .travel: Activity.travel.color
        case .outOfOffice: Activity.outOfOffice.color
        case .carried: .green
        }
    }
}

/// A Log form's subject: something new, or a bank record being edited.
enum LogItem: Identifiable, Hashable {
    case new(LogKind)
    case timeOff(TimeOff)
    case holiday(ClosedRange<Date>)
    case carried(Carried)

    var id: String {
        switch self {
        case .new(let k): "new-\(k.rawValue)"
        case .timeOff(let t): t.id.uuidString
        case .holiday(let r): "holiday-\(r.lowerBound.dayKey)"
        case .carried(let c): c.id.uuidString
        }
    }

    var kind: LogKind {
        switch self {
        case .new(let k): k
        case .timeOff: .timeOff
        case .holiday: .holiday
        case .carried: .carried
        }
    }
}

/// The settings the hours bank depends on, read the same way by the menu, the Logbook, Objectives and the Log forms.
struct BankContext: DynamicProperty {
    @Environment(Store.self) var store
    @AppStorage("targetHours") var targetHours = 8.0
    @AppStorage("excludedFromTarget") private var excluded = Activity.defaultExcluded
    @AppStorage("bankSince") private var bankSince = 0.0

    var target: Target { store.target(hours: targetHours, excluded: excluded) }
    /// Where the bank starts: the day picked in Settings → Target, or when tracking began.
    var since: Date { bankSince > 0 ? Date(timeIntervalSinceReferenceDate: bankSince) : target.since }
    var bank: HoursBank { store.bank(target, since: since) }
}

/// The Logbook's Hours Bank sheet: the balance, and every deposit and withdrawal that isn't a day's work.
struct HoursBankView: View {
    @Environment(\.dismiss) private var dismiss
    @Environment(\.locale) private var locale
    private var context = BankContext()
    @State private var editing: LogItem?

    var body: some View {
        let bank = context.bank
        VStack(spacing: 0) {
            VStack(spacing: 4) {
                Text(verbatim: signed(bank.balance))
                    .font(.system(size: 34, weight: .semibold)).monospacedDigit()
                    .foregroundStyle(bank.balance >= 0 ? .green : .red)
                caption(bank)
            }
            .padding(.top, 20).padding(.bottom, 4)
            Form {
                let rows = self.rows
                if rows.isEmpty {
                    Text("Nothing logged yet. Log time off, holidays, or overtime carried in from before.").foregroundStyle(.secondary)
                }
                ForEach(rows) { row($0) }
            }
            .formStyle(.grouped)
            HStack {
                Menu("Log", systemImage: "plus") {
                    ForEach(LogKind.allCases) { k in Button(k.label, systemImage: k.symbol) { editing = .new(k) } }
                }
                .fixedSize()
                Spacer()
                Button("Done") { dismiss() }.keyboardShortcut(.defaultAction)
            }
            .padding(14)
        }
        .frame(width: 460, height: 540)
        .sheet(item: $editing) { LogForm(item: $0) }
    }

    /// "2.5 days off · 24h 00m booked ahead", each part only when there's something to say.
    private func caption(_ bank: HoursBank) -> some View {
        let day = context.target.seconds
        return HStack(spacing: 6) {
            if day > 0, bank.balance > 0 {
                Text("\((bank.balance / day).formatted(.number.precision(.fractionLength(0...1)).locale(locale))) days off")
                if bank.bookedAhead > 0 { Text(verbatim: "·") }
            }
            if bank.bookedAhead > 0 { Text("\(bank.bookedAhead.hm) booked ahead") }
        }
        .font(.callout).foregroundStyle(.secondary).monospacedDigit()
    }

    private struct Row: Identifiable {
        var item: LogItem
        var start: Date, end: Date
        var hours: TimeInterval?  // signed as it moves the bank; a holiday doesn't
        var note: String
        var id: String { item.id }
    }

    /// Time off, carried hours and holidays, newest first.
    private var rows: [Row] {
        let store = context.store, target = context.target
        let off = store.timeOff.compactMap { t -> Row? in
            guard let s = Date(dayKey: t.start), let e = Date(dayKey: t.end) else { return nil }
            return Row(item: .timeOff(t), start: s, end: e, hours: -target.cost(of: t), note: t.note)
        }
        let carried = store.carried.compactMap { c -> Row? in
            Date(dayKey: c.date).map { Row(item: .carried(c), start: $0, end: $0, hours: c.hours * 3600, note: c.note) }
        }
        let holidays = store.holidays.map { Row(item: .holiday($0), start: $0.lowerBound, end: $0.upperBound, hours: nil, note: "") }
        return (off + carried + holidays).sorted { $0.start > $1.start }
    }

    private func row(_ r: Row) -> some View {
        HStack(spacing: 10) {
            Image(systemName: r.item.kind.symbol).foregroundStyle(r.item.kind.color).frame(width: 20)
            VStack(alignment: .leading, spacing: 2) {
                Text(verbatim: span(r.start, r.end))
                Group { if r.note.isEmpty { Text(r.item.kind.label) } else { Text(verbatim: r.note) } }
                    .font(.caption).foregroundStyle(.secondary).lineLimit(1)
            }
            Spacer()
            Text(verbatim: r.hours.map(signed) ?? "—").monospacedDigit()
                .foregroundStyle(r.hours.map { $0 > 0 ? Color.green : Color.primary } ?? Color.secondary)
            Button("Delete", systemImage: "trash", role: .destructive) { delete(r.item) }
                .labelStyle(.iconOnly).buttonStyle(.borderless).help("Delete")
        }
        .contentShape(.rect)
        .onTapGesture { editing = r.item }
    }

    /// "24 Dec – 2 Jan", or one day; the year only when it isn't this one.
    private func span(_ start: Date, _ end: Date) -> String {
        let cal = Calendar.current
        let text = { (d: Date) in
            cal.isDate(d, equalTo: .now, toGranularity: .year)
                ? d.formatted(.dateTime.day().month(.abbreviated).locale(locale))
                : d.formatted(.dateTime.day().month(.abbreviated).year().locale(locale))
        }
        return cal.isDate(start, inSameDayAs: end) ? text(start) : "\(text(start)) – \(text(end))"
    }

    private func delete(_ item: LogItem) {
        let store = context.store
        switch item {
        case .timeOff(let t): store.timeOff.removeAll { $0.id == t.id }
        case .carried(let c): store.carried.removeAll { $0.id == c.id }
        case .holiday(let r): store.removeHoliday(r)
        case .new: break
        }
    }
}

/// One Log form: time off, a holiday, Travel or Out of Office blocks, or carried hours; new, or a record being edited.
struct LogForm: View {
    @Environment(\.dismiss) private var dismiss
    private var context = BankContext()
    let item: LogItem
    @State private var from: Date
    @State private var to: Date
    @State private var hours: Double?
    @State private var note: String
    @State private var client: Profile.ID?

    init(item: LogItem) {
        self.item = item
        let today = Calendar.current.startOfDay(for: .now)
        var from = today, to = today, hours: Double?, note = ""
        switch item {
        case .new: break
        case .timeOff(let t): from = Date(dayKey: t.start) ?? today; to = Date(dayKey: t.end) ?? from; hours = t.hours; note = t.note
        case .holiday(let r): from = r.lowerBound; to = r.upperBound
        case .carried(let c): from = Date(dayKey: c.date) ?? today; to = from; hours = c.hours; note = c.note
        }
        _from = State(initialValue: from); _to = State(initialValue: to)
        _hours = State(initialValue: hours); _note = State(initialValue: note)
        _client = State(initialValue: nil)
    }

    var body: some View {
        let kind = item.kind
        VStack(spacing: 0) {
            Form {
                Section {
                    if kind == .carried {
                        DatePicker("Date", selection: $from, displayedComponents: .date)
                    } else {
                        DatePicker("From", selection: $from, displayedComponents: .date)
                        DatePicker("To", selection: $to, in: from..., displayedComponents: .date)
                    }
                    switch kind {
                    case .timeOff where oneDay:
                        TextField("Hours", value: $hours, format: .number, prompt: Text("Full day"))
                    case .travel, .outOfOffice:
                        TextField("Hours per day", value: $hours, format: .number, prompt: Text(verbatim: context.targetHours.formatted()))
                    case .carried:
                        TextField("Hours", value: $hours, format: .number, prompt: Text(verbatim: "32"))
                    default:
                        EmptyView()
                    }
                    if kind == .travel, !context.store.activeProfiles.isEmpty {
                        Picker("Client", selection: $client) {
                            Text("No client").tag(Profile.ID?.none)
                            ForEach(context.store.activeProfiles) { Text($0.name).tag(Optional($0.id)) }
                        }
                    }
                    if kind == .timeOff || kind == .carried { TextField("Note", text: $note) }
                } header: {
                    Label(kind.label, systemImage: kind.symbol)
                } footer: {
                    footer(kind).font(.caption).foregroundStyle(.secondary)
                }
            }
            .formStyle(.grouped)
            HStack {
                Spacer()
                Button("Cancel") { dismiss() }.keyboardShortcut(.cancelAction)
                Button("Save") { save(); dismiss() }.keyboardShortcut(.defaultAction).disabled(!valid)
            }
            .padding(14)
        }
        .frame(width: 400)
        .fixedSize(horizontal: false, vertical: true)
        .onChange(of: from) { if to < from { to = from } }
        .onAppear { if kind == .travel { client = context.store.currentProfile } }
    }

    private func footer(_ kind: LogKind) -> Text {
        switch kind {
        case .timeOff: Text("Each workday costs a day's target, or the hours you enter. Weekends and holidays cost nothing. It comes off the bank as soon as you save.")
        case .holiday: Text("Holidays owe nothing and leave the bank alone.")
        case .travel: Text("Adds a Travel block on each workday, after the day's last block or at your usual start.")
        case .outOfOffice: Text("Adds an Out of Office block on each workday, after the day's last block or at your usual start.")
        case .carried: Text("Overtime from before you tracked it here, or a negative amount for hours paid out.")
        }
    }

    private var oneDay: Bool { Calendar.current.isDate(from, inSameDayAs: to) }

    private var valid: Bool {
        switch item.kind {
        case .carried: (hours ?? 0) != 0
        case .holiday: true
        case .timeOff, .travel, .outOfOffice: hours.map { $0 > 0 } ?? true
        }
    }

    /// The record being edited, if any.
    private var editingID: UUID? {
        switch item {
        case .timeOff(let t): t.id
        case .carried(let c): c.id
        default: nil
        }
    }

    private func save() {
        let store = context.store
        switch item.kind {
        case .timeOff:
            let record = TimeOff(id: editingID ?? UUID(), start: from.dayKey, end: to.dayKey, hours: oneDay ? hours : nil, note: note)
            if let i = store.timeOff.firstIndex(where: { $0.id == record.id }) { store.timeOff[i] = record } else { store.timeOff.append(record) }
        case .holiday:
            if case .holiday(let old) = item { store.setHoliday(from: from, to: to, replacing: old) } else { store.setHoliday(from: from, to: to) }
        case .travel, .outOfOffice:
            let target = context.target
            store.log(item.kind == .travel ? .travel : .outOfOffice, from: from, to: to, hours: hours ?? context.targetHours,
                      profile: client, target: target, usualStart: Stats(store.entries, target: target, bankSince: context.since).usualStart)
        case .carried:
            let record = Carried(id: editingID ?? UUID(), date: from.dayKey, hours: hours ?? 0, note: note)
            if let i = store.carried.firstIndex(where: { $0.id == record.id }) { store.carried[i] = record } else { store.carried.append(record) }
        }
    }
}
```

- [ ] **Step 2: `EditorView.swift`**

- Make `signed(_:)` internal: `func signed(_ t: TimeInterval) -> String` (drop `private`), so `HoursBank.swift` uses it.
- Toolbar: replace the `Toggle("Day Off", …)` with
  `Button("Hours Bank", systemImage: "building.columns") { store.editingBank = true }`.
- After the Objectives `.sheet`, add
  `.sheet(isPresented: Binding(get: { store.editingBank }, set: { store.editingBank = $0 })) { HoursBankView() }`.
- Stats popover: `Stats(store.entries, target: target, bankSince: …, carried: store.carried)`.
- Day rows: replace `dayOff: target.daysOff.contains(d.dayKey)` with
  `off: target.daysOff.contains(d.dayKey) ? .holiday : (target.booked[d.dayKey] ?? 0) > 0 ? .timeOff : nil`.
  In `DayRow`, replace `let dayOff: Bool` with `let off: LogKind?` and the icon line with:

```swift
            if let off { Image(systemName: off.symbol).foregroundStyle(selected ? Color.white : Color.secondary) }
```

- [ ] **Step 3: `MenuPanel.swift`**

- Tiles: keep only `GridRow { ActivityButton(.work); ActivityButton(.break) }` and
  `GridRow { ActivityButton(.lunch); ActivityButton(.extra) }`.
- Status, after the timer `Text` inside `if let running`:

```swift
                    // Travel and Out of Office are logged now; one still running from before has no tile to stop it.
                    if running.activity == .travel || running.activity == .outOfOffice {
                        Button("Stop", systemImage: "stop.circle", action: store.stop)
                            .labelStyle(.iconOnly).buttonStyle(.borderless).help("Stop")
                    }
```

- Totals: replace `let bank = store.balance(DateInterval(...), target)` with `let bank = store.bank(target, since: since)`,
  the Stats line with `Stats(store.entries, target: target, bankSince: since, carried: store.carried)`, and the bank row
  with `row("Since \(since, format: .dateTime.day().month(.abbreviated))", worked: bank.worked, balance: bank.balance,
  shortfall: .red) { store.editingBank = true; showLogbook() }`. `row` gains `action: (() -> Void)? = nil`; its first
  cell becomes:

```swift
            Group {
                if let action { Button(action: action) { Text(label) }.buttonStyle(.plain).help("Hours Bank") } else { Text(label) }
            }
            .foregroundStyle(.secondary).gridColumnAlignment(.leading)
```

- Footer, after the Export menu:

```swift
            Menu("Log", systemImage: "square.and.pencil") {
                ForEach(LogKind.allCases) { k in
                    Button(k.label, systemImage: k.symbol) { dismiss(); show(window: "log", value: k.rawValue, openWindow) }
                }
            }
```

- [ ] **Step 4: `OutatimeApp.swift`** — after the What's New `WindowGroup`:

```swift
        // The menu's Log: one form, its kind picked from the menu.
        WindowGroup("Log", id: "log", for: String.self) { $kind in
            LogForm(item: .new(LogKind(rawValue: kind ?? "") ?? .timeOff)).environment(store).environment(\.locale, language.locale)
        }
        .windowResizability(.contentSize)
        .defaultPosition(.center)
        .restorationBehavior(.disabled)
        .commandsRemoved()
```

- [ ] **Step 5: `Objectives.swift`** — in `ObjectiveContext.progress`:

```swift
        let bank = o.kind == .daysOff ? store.bank(target, since: since).balance : 0
```

- [ ] **Step 6: `WhatsNew.swift`** — in the `1.3` release's `new:` list, after Objectives:

```swift
            Item(symbol: "building.columns", color: .green, title: "Hours Bank",
                 detail: "Log time off, holidays and overtime carried over in the Logbook's Hours Bank. Travel and Out of Office are logged by the day instead of timed in the menu."),
```

- [ ] **Step 7: Translations** — build once so Xcode extracts the new keys, then merge with this script (run from the
  repo root; it fills every new key in all seven languages and fails on a key it doesn't know):

```bash
python3 - <<'EOF'
import json
p = "Outatime/Localizable.xcstrings"
cat = json.load(open(p))
L = ["es", "pt-BR", "fr", "de", "it", "ja", "zh-Hans"]
T = {
 "Time Off": ["Tiempo libre", "Folga", "Congé", "Freizeit", "Permesso", "休暇", "调休"],
 "Holiday": ["Festivo", "Feriado", "Jour férié", "Feiertag", "Festività", "祝日", "节假日"],
 "Carried Hours": ["Horas traspasadas", "Horas transferidas", "Heures reportées", "Übertragene Stunden", "Ore riportate", "繰越時間", "结转工时"],
 "Hours Bank": ["Banco de horas", "Banco de horas", "Banque d'heures", "Stundenkonto", "Banca ore", "時間バンク", "工时银行"],
 "Log": ["Registrar", "Registrar", "Saisir", "Eintragen", "Registra", "記録", "记录"],
 "From": ["Desde", "De", "Du", "Von", "Dal", "開始日", "开始"],
 "To": ["Hasta", "Até", "Au", "Bis", "Al", "終了日", "结束"],
 "Date": ["Fecha", "Data", "Date", "Datum", "Data", "日付", "日期"],
 "Note": ["Nota", "Nota", "Note", "Notiz", "Nota", "メモ", "备注"],
 "Hours per day": ["Horas por día", "Horas por dia", "Heures par jour", "Stunden pro Tag", "Ore al giorno", "1日あたりの時間", "每天小时数"],
 "Full day": ["Día completo", "Dia inteiro", "Journée entière", "Ganzer Tag", "Giornata intera", "終日", "全天"],
 "Stop": ["Detener", "Parar", "Arrêter", "Stoppen", "Ferma", "停止", "停止"],
 "Nothing logged yet. Log time off, holidays, or overtime carried in from before.": [
  "Aún no hay nada registrado. Registra tiempo libre, festivos u horas extra que traes de antes.",
  "Nada registrado ainda. Registre folgas, feriados ou horas extras trazidas de antes.",
  "Rien de saisi pour l'instant. Saisissez des congés, des jours fériés ou des heures sup' reportées d'avant.",
  "Noch nichts eingetragen. Trage Freizeit, Feiertage oder Überstunden von früher ein.",
  "Ancora niente di registrato. Registra permessi, festività o straordinari riportati da prima.",
  "まだ記録はありません。休暇、祝日、以前からの残業時間を記録しましょう。",
  "还没有记录。记录调休、节假日或之前结转的加班时间。"],
 "%@ days off": ["%@ días libres", "%@ dias de folga", "%@ jours de congé", "%@ freie Tage", "%@ giorni liberi", "休暇 %@ 日分", "%@ 天假"],
 "%@ booked ahead": ["%@ reservadas", "%@ já reservadas", "%@ déjà posées", "%@ schon verplant", "%@ già prenotate", "予約済み %@", "已预订 %@"],
 "Each workday costs a day's target, or the hours you enter. Weekends and holidays cost nothing. It comes off the bank as soon as you save.": [
  "Cada día laborable cuesta la meta diaria, o las horas que indiques. Fines de semana y festivos no cuestan nada. Se descuenta del banco en cuanto guardas.",
  "Cada dia útil custa a meta diária, ou as horas que você informar. Fins de semana e feriados não custam nada. Sai do banco assim que você salvar.",
  "Chaque jour ouvré coûte l'objectif d'une journée, ou les heures saisies. Les week-ends et jours fériés ne coûtent rien. C'est déduit de la banque dès l'enregistrement.",
  "Jeder Arbeitstag kostet ein Tagesziel oder die eingegebenen Stunden. Wochenenden und Feiertage kosten nichts. Es wird beim Sichern sofort vom Konto abgezogen.",
  "Ogni giorno lavorativo costa l'obiettivo di una giornata, o le ore indicate. Weekend e festività non costano nulla. Viene scalato dalla banca appena salvi.",
  "平日1日ごとに1日の目標時間、または入力した時間を使います。週末と祝日は対象外です。保存するとすぐにバンクから差し引かれます。",
  "每个工作日扣除一天的目标时长，或你填写的小时数。周末和节假日不扣。保存后立即从工时银行扣除。"],
 "Holidays owe nothing and leave the bank alone.": [
  "Los festivos no deben horas y no tocan el banco.", "Feriados não devem horas e não mexem no banco.",
  "Les jours fériés ne doivent rien et ne touchent pas à la banque.", "Feiertage schulden nichts und lassen das Konto unberührt.",
  "Le festività non devono ore e non toccano la banca.", "祝日は目標時間がなく、バンクにも影響しません。", "节假日不计目标时长，也不影响工时银行。"],
 "Adds a Travel block on each workday, after the day's last block or at your usual start.": [
  "Añade un bloque de Viaje en cada día laborable, tras el último bloque del día o a tu hora de inicio habitual.",
  "Adiciona um bloco de Viagem em cada dia útil, depois do último bloco do dia ou no seu horário de início habitual.",
  "Ajoute un bloc Déplacement chaque jour ouvré, après le dernier bloc du jour ou à votre heure de début habituelle.",
  "Fügt an jedem Arbeitstag einen Reise-Block hinzu, nach dem letzten Block des Tages oder zu deiner üblichen Startzeit.",
  "Aggiunge un blocco Viaggio in ogni giorno lavorativo, dopo l'ultimo blocco del giorno o al tuo orario d'inizio abituale.",
  "平日ごとに移動のブロックを、その日の最後のブロックの後、またはいつもの開始時刻に追加します。",
  "在每个工作日添加一个出差时段，接在当天最后一个时段之后，或从你通常的开始时间开始。"],
 "Adds an Out of Office block on each workday, after the day's last block or at your usual start.": [
  "Añade un bloque de Fuera de la oficina en cada día laborable, tras el último bloque del día o a tu hora de inicio habitual.",
  "Adiciona um bloco de Fora do escritório em cada dia útil, depois do último bloco do dia ou no seu horário de início habitual.",
  "Ajoute un bloc Absent du bureau chaque jour ouvré, après le dernier bloc du jour ou à votre heure de début habituelle.",
  "Fügt an jedem Arbeitstag einen Außer-Haus-Block hinzu, nach dem letzten Block des Tages oder zu deiner üblichen Startzeit.",
  "Aggiunge un blocco Fuori ufficio in ogni giorno lavorativo, dopo l'ultimo blocco del giorno o al tuo orario d'inizio abituale.",
  "平日ごとに外出のブロックを、その日の最後のブロックの後、またはいつもの開始時刻に追加します。",
  "在每个工作日添加一个外出时段，接在当天最后一个时段之后，或从你通常的开始时间开始。"],
 "Overtime from before you tracked it here, or a negative amount for hours paid out.": [
  "Horas extra de antes de registrarlas aquí, o una cantidad negativa por horas pagadas.",
  "Horas extras de antes de registrá-las aqui, ou um valor negativo para horas pagas.",
  "Des heures sup' d'avant leur suivi ici, ou un montant négatif pour des heures payées.",
  "Überstunden von vor der Erfassung hier, oder ein negativer Wert für ausbezahlte Stunden.",
  "Straordinari di prima che li registrassi qui, o un valore negativo per ore pagate.",
  "ここで記録する前の残業時間。支払われた時間はマイナスで入力します。",
  "在这里记录之前的加班时间；已结算的小时数填负数。"],
 "Log time off, holidays and overtime carried over in the Logbook's Hours Bank. Travel and Out of Office are logged by the day instead of timed in the menu.": [
  "Registra tiempo libre, festivos y horas extra traspasadas en el Banco de horas del Registro. Viaje y Fuera de la oficina se registran por días en lugar de cronometrarse en el menú.",
  "Registre folgas, feriados e horas extras transferidas no Banco de horas do Diário de bordo. Viagem e Fora do escritório são registrados por dia em vez de cronometrados no menu.",
  "Saisissez congés, jours fériés et heures sup' reportées dans la Banque d'heures du Journal. Déplacement et Absent du bureau se saisissent à la journée au lieu d'être chronométrés dans le menu.",
  "Trage Freizeit, Feiertage und übertragene Überstunden im Stundenkonto des Logbuchs ein. Reise und Außer Haus werden tageweise eingetragen statt im Menü gestoppt.",
  "Registra permessi, festività e straordinari riportati nella Banca ore del Registro. Viaggio e Fuori ufficio si registrano a giornate invece di cronometrarli nel menu.",
  "ログブックの時間バンクで休暇、祝日、繰越の残業時間を記録できます。移動と外出はメニューで計測せず、日単位で記録します。",
  "在日志的工时银行中记录调休、节假日和结转的加班。出差和外出改为按天记录，不再在菜单中计时。"],
}
strings = cat["strings"]
for key, values in T.items():
    entry = strings.setdefault(key, {})
    locs = entry.setdefault("localizations", {})
    for code, value in zip(L, values):
        locs[code] = {"stringUnit": {"state": "translated", "value": value}}
missing = [k for k, e in strings.items() if e.get("shouldTranslate") is not False and any(c not in e.get("localizations", {}) for c in L)]
json.dump(cat, open(p, "w"), ensure_ascii=False, indent=2, separators=(",", " : "))
open(p, "a").write("\n")
print("still missing:", missing)
EOF
```

  Before running it, check the activity names each language already uses for Travel/Out of Office and the Logbook
  (`grep -A30 '"Out of Office"' Outatime/Localizable.xcstrings`) and adjust the three sentences that name them so they
  match. Check the catalog's existing JSON formatting (`head -5`) and make the dump match it (indent and separators) so
  the diff only shows new keys. Expected output: `still missing: []`.

- [ ] **Step 8: Build and run the whole suite**

Run: `xcodebuild -project Outatime.xcodeproj -scheme Outatime test 2>&1 | grep -E "Test run|failed|error:|warning: .*HoursBank" | head -20`
Expected: build succeeds, all tests pass including `catalogIsComplete` and `everyReleaseHasNotes`.

- [ ] **Step 9: Commit**

```bash
git add Outatime/ OutatimeTests/
git commit -m "Hours Bank in the Logbook and a Log menu; Travel and Out of Office leave the menu's tiles"
```

---

### Task 5: Windows Core — records, booked time off, the bank, holidays, logging, the data file

**Files:**
- Modify: `windows/src/Outatime.Core/Models.cs`, `Cal.cs`, `Store.cs`, `Json.cs`, `Stats.cs`
- Test: `windows/tests/Outatime.Tests/CoreTests.cs`, `CompatibilityTests.cs`, `fixtures/mac-data.json`

**Interfaces:**
- Produces: `TimeOff(Guid Id, string Start, string End, double? Hours = null, string Note = "")` (+ ctor without Id),
  `Carried(Guid Id, string Date, double Hours, string Note = "")` (+ ctor without Id),
  `HoursBank(double Balance, double Worked, double BookedAhead)`, `Target.Booked`, `Target.IsWorkday`,
  `Target.Book(records) -> Target`, `Target.Cost(record)`, `Cal.FromKey`, `Cal.DaysBetween`,
  `Store.TimeOff`, `Store.Carried`, `Store.Bank(target, since, now?)`, `Store.BankOf(...)` (static),
  `Store.Holidays()`, `Store.SetHoliday(from, to, replacing?)`, `Store.RemoveHoliday(run)`,
  `Store.Upsert(TimeOff)`, `Store.Upsert(Carried)`, `Store.DeleteTimeOff(Guid)`, `Store.DeleteCarried(Guid)`,
  `Store.Log(...)`, `DataFile.Contents(..., List<TimeOff>? TimeOff = null, List<Carried>? Carried = null)`,
  `new Stats(entries, target, bankSince, now?, carried?)`.

- [ ] **Step 1: Write the failing tests** — port Task 1/2's tests to `CoreTests` (same scenarios, same numbers):

```csharp
    static DateTimeOffset D(int day, int month = 9) => Cal.At(2026, month, day);

    [Fact]
    public void TimeOffLowersWhatADayOwes()
    {
        var t = new Target(8 * 3600, Day, DaysOff: new HashSet<string> { "2026-09-07" })
            .Book([new TimeOff("2026-09-03", "2026-09-03"), new TimeOff("2026-09-04", "2026-09-07", 4), new TimeOff("2026-09-08", "2026-09-08", 10)]);
        Assert.Equal(new Dictionary<string, double> { ["2026-09-03"] = 8 * 3600, ["2026-09-04"] = 4 * 3600, ["2026-09-08"] = 8 * 3600 }, t.Booked);
        Assert.Equal(0, t.Scheduled(Day));
        Assert.Equal(4 * 3600, t.Scheduled(D(4)));
        Assert.Equal(8 * 3600, t.Scheduled(D(9)));
        Assert.Equal(4 * 3600, t.Cost(new TimeOff("2026-09-04", "2026-09-07", 4)));
        Assert.Equal(8 * 3600, t.Book([new TimeOff("2026-09-04", "2026-09-04", 6)]).Booked!["2026-09-04"]);
        Assert.Equal(0, t.Cost(new TimeOff("2026-09-01", "2026-09-01")));
    }

    [Fact]
    public void BankCountsTimeOffAndCarriedHours()
    {
        var entries = new[] { E(A.Work, At(9), At(18)), E(A.Work, Cal.Setting(D(7), 9), Cal.Setting(D(7), 17)) };
        var target = new Target(8 * 3600, Day).Book([new TimeOff("2026-09-04", "2026-09-04"), new TimeOff("2026-09-10", "2026-09-10")]);
        var carried = new[] { new Carried("2026-08-31", 32), new Carried("2026-09-07", 10) };
        var now = Cal.Setting(D(8), 14);
        var a = Store.BankOf(entries, carried, target, Day, now);
        Assert.Equal((27 * 3600.0, 8 * 3600.0, 17 * 3600.0), (a.Balance, a.BookedAhead, a.Worked));
        Assert.Equal(-6 * 3600, Store.BankOf(entries, carried, target, D(4), now).Balance);
    }

    [Fact]
    public void BankRecordsRoundTrip()
    {
        var store = NewStore();
        store.Upsert(new TimeOff("2026-12-24", "2027-01-01", note: "Holidays"));
        store.Upsert(new TimeOff("2026-09-04", "2026-09-04", 4));
        store.Upsert(new Carried("2026-08-31", -8.5, "Paid out"));
        var reloaded = new Store(store.Path);
        Assert.Equal(store.TimeOff, reloaded.TimeOff);
        Assert.Equal(store.Carried, reloaded.Carried);
        Assert.Equal(2, File.ReadAllText(store.Path).Split("\"hours\"").Length - 1);
        store.DeleteCarried(store.Carried[0].Id);
        Assert.Empty(new Store(store.Path).Carried);
    }

    [Fact]
    public void HolidaysGroupIntoRanges()
    {
        var store = NewStore();
        store.DaysOff = ["2026-09-07"];
        store.SetHoliday(D(24), D(26));
        Assert.Equal([(D(24), D(26)), (D(7), D(7))], store.Holidays());
        store.SetHoliday(D(24), D(25), (D(24), D(26)));
        Assert.Equal(["2026-09-07", "2026-09-24", "2026-09-25"], store.DaysOff.Order());
        store.RemoveHoliday((D(7), D(7)));
        Assert.Equal(["2026-09-24", "2026-09-25"], store.DaysOff.Order());
    }

    [Fact]
    public void LoggingTravelFillsWorkdays()
    {
        var store = NewStore();
        var acme = store.AddProfile("Acme")!.Value;
        store.Entries = [E(A.Work, At(9), At(11))];
        store.DaysOff = ["2026-09-07"];
        store.Upsert(new TimeOff("2026-09-08", "2026-09-08"));
        var target = store.Target(8, Activities.DefaultExcluded);
        store.Log(A.Travel, Day, D(9), 6, acme, target, 8 * 60 + 30);
        var travel = store.Entries.Where(e => e.Activity == A.Travel).OrderBy(e => e.Start).ToList();
        Assert.Equal(["2026-09-03", "2026-09-04", "2026-09-09"], travel.Select(e => e.Start.DayKey()));
        Assert.Equal((At(11), 6 * 3600.0), (travel[0].Start, travel[0].Duration));
        Assert.Equal(Cal.Setting(D(4), 8, 30), travel[1].Start);
        Assert.All(travel, e => Assert.Equal(acme, e.Profile));
        store.Log(A.OutOfOffice, D(10), D(10), 8, acme, target, 20 * 60);
        var ooo = store.Entries.First(e => e.Activity == A.OutOfOffice);
        Assert.Equal((null, 4 * 3600.0), (ooo.Profile, ooo.Duration));
        store.Log(A.OutOfOffice, D(11), D(11), 1, null, target, null);
        Assert.Contains(store.Entries, e => e.Activity == A.OutOfOffice && e.Start == Cal.Setting(D(11), 9));
    }
```

Extend `fixtures/mac-data.json` (keys sorted, so `carried` first and `timeOff` last):

```json
  "carried" : [
    {
      "date" : "2026-08-31",
      "hours" : 32,
      "id" : "5D0B6A1C-2E3F-4A5B-8C7D-9E0F1A2B3C4D",
      "note" : "Jan–Aug"
    }
  ],
```

and, after `templates`:

```json
  "timeOff" : [
    {
      "end" : "2026-09-11",
      "id" : "7A1B2C3D-4E5F-4061-8273-94A5B6C7D8E9",
      "note" : "",
      "start" : "2026-09-10"
    },
    {
      "end" : "2026-09-04",
      "hours" : 4,
      "id" : "8B2C3D4E-5F60-4172-8384-A5B6C7D8E9FA",
      "note" : "Dentist",
      "start" : "2026-09-04"
    }
  ]
```

Then confirm the Mac writes exactly this: add a Mac test `fixtureMatchesWhatTheMacWrites` that decodes
`windows/tests/Outatime.Tests/fixtures/mac-data.json` through `Store(url:)` (copy it to a temp folder first), triggers a
save (`store.daysOff = store.daysOff`), and compares the file to the fixture text. In `ReadsTheMacFile` add:

```csharp
        Assert.Equal([new Carried(Guid.Parse("5D0B6A1C-2E3F-4A5B-8C7D-9E0F1A2B3C4D"), "2026-08-31", 32, "Jan–Aug")], c.Carried);
        Assert.Equal(2, c.TimeOff!.Count);
        Assert.Null(c.TimeOff[0].Hours);
        Assert.Equal((4.0, "Dentist"), (c.TimeOff[1].Hours, c.TimeOff[1].Note));
```

and in `ReadsOldTags`: `Assert.Empty(c.TimeOff!); Assert.Empty(c.Carried!);`.

- [ ] **Step 2: Run to verify they fail**

Run: `cd windows && ~/.dotnet/dotnet test tests/Outatime.Tests 2>&1 | tail -15`
Expected: build errors — `The type or namespace name 'TimeOff' could not be found`.

- [ ] **Step 3: Implement — `Cal.cs`**

```csharp
    /// The day a day key names, at local midnight; null for a malformed key.
    public static DateTimeOffset? FromKey(string key) =>
        DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? At(d) : null;

    /// Every day from one key to the other, both included; none when either is malformed or they're reversed.
    public static List<DateTimeOffset> DaysBetween(string start, string end)
    {
        var days = new List<DateTimeOffset>();
        if (FromKey(start) is { } a && FromKey(end) is { } b)
            for (var d = a; d <= b; d = AddDays(d, 1)) days.Add(d);
        return days;
    }
```

- [ ] **Step 4: Implement — `Models.cs`**

```csharp
/// Time taken off workdays and paid from the hours bank: `Start`…`End` (day keys, both included), each workday costing
/// `Hours`, or with none a whole day's target, so a new target re-prices full days.
public record struct TimeOff(Guid Id, string Start, string End, double? Hours = null, string Note = "")
{
    public TimeOff(string start, string end, double? hours = null, string note = "") : this(Guid.NewGuid(), start, end, hours, note) { }
}

/// Hours put into the bank from outside the Logbook, like overtime from before tracking began, or taken out (negative)
/// when they're paid.
public record struct Carried(Guid Id, string Date, double Hours, string Note = "")
{
    public Carried(string date, double hours, string note = "") : this(Guid.NewGuid(), date, hours, note) { }
}

/// What the hours bank holds, the time worked behind it, and how much of it is already booked as time off ahead.
public readonly record struct HoursBank(double Balance, double Worked, double BookedAhead);
```

`Target` becomes:

```csharp
/// The daily target and which days owe it. `Booked`: seconds of each day taken off from the bank, by day key; see Book.
public record struct Target(double Seconds, DateTimeOffset Since, string Excluded = Activities.DefaultExcluded, IReadOnlySet<string>? DaysOff = null,
                            IReadOnlyDictionary<string, double>? Booked = null)
{
    readonly IReadOnlySet<string> Off => DaysOff ?? new HashSet<string>();

    public readonly double Owed(DateTimeOffset day, DateTimeOffset? now = null) =>
        Cal.StartOfDay(day) <= (now ?? Clock.Now) ? Scheduled(day) : 0;

    /// What a day will owe once it comes, so a week's goal counts the days still ahead. Time off lowers it.
    public readonly double Scheduled(DateTimeOffset day)
    {
        var d = Cal.StartOfDay(day);
        if (!IsWorkday(d) || d < Cal.StartOfDay(Since)) return 0;
        return Math.Max(0, Seconds - (Booked?.GetValueOrDefault(d.DayKey()) ?? 0));
    }

    /// Not a weekend or a holiday.
    public readonly bool IsWorkday(DateTimeOffset day) => !Cal.IsWeekend(day) && !Off.Contains(day.DayKey());

    public readonly double Balance(double worked, DateTimeOffset day, DateTimeOffset? now = null) { /* unchanged */ }

    /// Spreads time off over the workdays it covers, never more than a day's target on one day.
    public readonly Target Book(IEnumerable<TimeOff> records)
    {
        var booked = new Dictionary<string, double>(Booked ?? new Dictionary<string, double>());
        foreach (var r in records)
            foreach (var d in DaysOf(r)) booked[d.DayKey()] = Math.Min(Seconds, booked.GetValueOrDefault(d.DayKey()) + PerDay(r));
        return this with { Booked = booked };
    }

    /// What one record takes from the bank, overlaps with other records aside.
    public readonly double Cost(TimeOff r) => DaysOf(r).Count * PerDay(r);

    readonly double PerDay(TimeOff r) => Math.Min(Seconds, r.Hours * 3600 ?? Seconds);

    /// Workdays a record covers since tracking began: weekends, holidays and earlier days cost nothing.
    readonly List<DateTimeOffset> DaysOf(TimeOff r)
    {
        var start = Cal.StartOfDay(Since);
        var self = this;
        return Cal.DaysBetween(r.Start, r.End).Where(d => self.IsWorkday(d) && d >= start).ToList();
    }
}
```

(Keep the existing doc comments on `Owed`/`Balance`.)

- [ ] **Step 5: Implement — `Json.cs`**

`Contents` gains `List<TimeOff>? TimeOff = null, List<Carried>? Carried = null` at the end. `Read` passes
`Array(root, "timeOff", false).Select(ReadTimeOff).ToList()` and `Array(root, "carried", false).Select(ReadCarried).ToList()`
(both `// added in 1.3`), with:

```csharp
    static TimeOff ReadTimeOff(JsonNode o) =>
        new(ReqGuid(o, "id"), Str(Req(o, "start")), Str(Req(o, "end")), o["hours"] is { } h ? Num(h) : null, Str(Req(o, "note")));

    static Carried ReadCarried(JsonNode o) => new(ReqGuid(o, "id"), Str(Req(o, "date")), Num(Req(o, "hours")), Str(Req(o, "note")));
```

`Write` adds:

```csharp
            ["timeOff"] = (c.TimeOff ?? []).Select(t => Obj(("id", G(t.Id)), ("start", t.Start), ("end", t.End), ("hours", t.Hours),
                ("note", t.Note))).ToList(),
            ["carried"] = (c.Carried ?? []).Select(x => Obj(("id", G(x.Id)), ("date", x.Date), ("hours", x.Hours), ("note", x.Note))).ToList(),
```

(`Obj` drops the null `hours`; `Emit` writes `32.0` as `32`, as Swift does — `ToString("R")` already does.)

- [ ] **Step 6: Implement — `Store.cs`**

Fields/properties next to `objectives`:

```csharp
    List<TimeOff> timeOff = [];
    List<Carried> carried = [];
    public List<TimeOff> TimeOff { get => timeOff; set { timeOff = value; Commit(); } }
    public List<Carried> Carried { get => carried; set { carried = value; Commit(); } }
```

`Load`: `timeOff = c.TimeOff ?? []; carried = c.Carried ?? [];`. `Contents`: `new(entries, templates, daysOff, profiles,
currentProfile, objectives, timeOff, carried)`. `Target(...)`: append `.Book(timeOff)`.

After `BalanceOf`:

```csharp
    public HoursBank Bank(Target target, DateTimeOffset since, DateTimeOffset? now = null) => BankOf(entries, carried, target, since, now);

    /// The day balances since `since`, less the time off booked from then on (days ahead too: booked is spent), plus the
    /// hours carried in. Carried hours are usually dated before tracking began, so they only drop out once the bank is
    /// made to start later than that.
    public static HoursBank BankOf(IEnumerable<Entry> entries, IEnumerable<Carried> carried, Target target, DateTimeOffset since, DateTimeOffset? now = null)
    {
        var n = now ?? Clock.Now;
        var start = Cal.StartOfDay(since);
        var today = Cal.StartOfDay(n);
        var days = BalanceOf(entries, new Interval(start, n > start ? n : start), target, n);
        double off = 0, ahead = 0;
        foreach (var (key, seconds) in target.Booked ?? new Dictionary<string, double>())
        {
            if (Cal.FromKey(key) is not { } d || d < start) continue;
            off += seconds;
            if (d > today) ahead += seconds;
        }
        var all = start <= Cal.StartOfDay(target.Since);
        var brought = carried.Where(c => all || Cal.FromKey(c.Date) >= start).Sum(c => c.Hours * 3600);
        return new(days.Balance - off + brought, days.Worked, ahead);
    }
```

A `// MARK: Hours bank` section before `SetDayOff`:

```csharp
    /// Days off as runs of consecutive days, newest first: how the Hours Bank lists holidays.
    public List<(DateTimeOffset Start, DateTimeOffset End)> Holidays()
    {
        var runs = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var d in daysOff.Select(Cal.FromKey).OfType<DateTimeOffset>().Order())
        {
            if (runs.Count > 0 && Cal.AddDays(runs[^1].End, 1) == d) runs[^1] = (runs[^1].Start, d);
            else runs.Add((d, d));
        }
        runs.Reverse();
        return runs;
    }

    /// Marks `from`…`to` as holidays, in place of `replacing` when one is being edited.
    public void SetHoliday(DateTimeOffset from, DateTimeOffset to, (DateTimeOffset Start, DateTimeOffset End)? replacing = null)
    {
        if (replacing is { } old) daysOff.ExceptWith(Cal.DaysBetween(old.Start.DayKey(), old.End.DayKey()).Select(d => d.DayKey()));
        daysOff.UnionWith(Cal.DaysBetween(from.DayKey(), to.DayKey()).Select(d => d.DayKey()));
        Commit();
    }

    public void RemoveHoliday((DateTimeOffset Start, DateTimeOffset End) run)
    {
        daysOff.ExceptWith(Cal.DaysBetween(run.Start.DayKey(), run.End.DayKey()).Select(d => d.DayKey()));
        Commit();
    }

    public void Upsert(TimeOff record)
    {
        var i = timeOff.FindIndex(t => t.Id == record.Id);
        if (i >= 0) timeOff[i] = record; else timeOff.Add(record);
        Commit();
    }

    public void Upsert(Carried record)
    {
        var i = carried.FindIndex(c => c.Id == record.Id);
        if (i >= 0) carried[i] = record; else carried.Add(record);
        Commit();
    }

    public void DeleteTimeOff(Guid id) { timeOff.RemoveAll(t => t.Id == id); Commit(); }
    public void DeleteCarried(Guid id) { carried.RemoveAll(c => c.Id == id); Commit(); }

    /// Travel or Out of Office logged rather than timed: a block on each workday from `from` to `to` that isn't taken
    /// off in full, after the day's last block, or else at the usual start (minutes after midnight; 9:00 without one),
    /// `hours` long and cut at midnight. Only Travel keeps the client.
    public void Log(Activity activity, DateTimeOffset from, DateTimeOffset to, double hours, Guid? profile, Target target, int? usualStart,
                    DateTimeOffset? now = null)
    {
        var n = now ?? Clock.Now;
        var minute = usualStart ?? 9 * 60;
        foreach (var d in Cal.DaysBetween(from.DayKey(), to.DayKey()).Where(target.IsWorkday))
        {
            if (target.Seconds > 0 && (target.Booked?.GetValueOrDefault(d.DayKey()) ?? 0) >= target.Seconds) continue;
            var day = EntriesOn(d);
            var start = day.Count > 0 ? day.Max(e => e.End ?? n) : Cal.Setting(d, minute / 60, minute % 60);
            var midnight = Cal.AddDays(d, 1);
            var end = start.Plus(hours * 3600);
            if (end > midnight) end = midnight;
            if (start < end) entries.Add(new Entry(activity, start, end, profile: activity.Billable() ? profile : null));
        }
        Commit();
    }
```

(Class doc comment: "Entries, templates, days off, clients, objectives, time off and carried hours".)

- [ ] **Step 7: Implement — `Stats.cs`** — constructor gains `IEnumerable<Carried>? carried = null` as its last
  parameter; `Bank = Store.BankOf(entries, carried ?? [], target, bankSince, n).Balance;`.

- [ ] **Step 8: Run the tests**

Run: `cd windows && ~/.dotnet/dotnet test tests/Outatime.Tests 2>&1 | tail -5`
Expected: `Passed!` with no failures. Then run the Mac suite for `fixtureMatchesWhatTheMacWrites`; if the Mac writes the
fixture differently (key order, number format), fix the fixture to what the Mac writes and rerun both suites.

- [ ] **Step 9: Commit**

```bash
git add windows/src/Outatime.Core windows/tests OutatimeTests/OutatimeTests.swift
git commit -m "Windows: time off, carried hours, holidays and logged Travel/Out of Office in the Core, read and written like the Mac"
```

---

### Task 6: Windows exports

**Files:**
- Modify: `windows/src/Outatime.Core/Export.cs`, `windows/src/Outatime.Core/Store.cs` (`MasterWorkbook`)
- Test: `windows/tests/Outatime.Tests/CoreTests.cs`

**Interfaces:**
- Consumes: Task 5.
- Produces: `DaySummary(..., double Balance, double TimeOff, string Notes)`, `Report.Master(entries, target, names, now, timeOff, carried)`.

- [ ] **Step 1: Update the tests** — the Windows ports of `DailyCsv`, `MonthReportWorkbook`, `MasterWorkbook` get the
  same new expectations as Task 3 Step 1 (trailing `,0.00`, `K2`, `A1:L3`, the new card formula), and port
  `masterWorkbookCountsTheBank`:

```csharp
    [Fact]
    public void MasterWorkbookCountsTheBank()
    {
        var off = new[] { new TimeOff("2026-09-04", "2026-09-04", note: "Dentist") };
        var target = new Target(8 * 3600, Day).Book(off);
        var entries = new[] { E(A.Work, At(9), At(18)) };
        var carried = new[] { new Carried("2026-08-31", 32, "Jan–Aug") };
        var now = Cal.Setting(D(4), 20);
        var text = Text(Report.Master(entries, target, null, now, off, carried));
        Assert.Contains("<f>SUM(tblDays[Balance])+SUM(tblBank[Hours])</f><v>25.0</v>", text);
        Assert.Contains("name=\"tblBank\"", text);
        Assert.Contains(">Dentist<", text);
        Assert.Equal(25 * 3600, Store.BankOf(entries, carried, target, Day, now).Balance);
        Assert.Contains("ref=\"A1:L4\"", Text(Report.Month(entries, Day, target, null, now)));
    }
```

  (Match the cached-number format the existing Windows workbook tests use.)

- [ ] **Step 2: Run to verify they fail.**

- [ ] **Step 3: Implement** — mirror Task 3 Step 3 in C#:
  - `DaySummary` record: insert `double TimeOff` before `Notes`; in `Days`, `var off = target.Booked?.GetValueOrDefault(date.DayKey()) ?? 0;`,
    skip only when `es.Count == 0 && owed <= 0 && off <= 0`, pass `off`.
  - `Csv.Daily`: header `"Balance", "Notes", "Time Off"`; rows `…, Signed(d.Balance), d.Notes, Hours(d.TimeOff)`; total
    `…, Signed(days.Sum(d => d.Balance)), "", Hours(days.Sum(d => d.TimeOff))`.
  - `Values`: insert `("Time Off", d => d.TimeOff / 3600)` between Target and Balance.
  - `Master(entries, target, names = null, now = null, IReadOnlyList<TimeOff>? timeOff = null, IReadOnlyList<Carried>? carried = null)`:
    build `var (bankSheet, bankTotal) = Bank(target, timeOff ?? [], carried ?? []);`, pass `bankTotal` to `Dashboard`,
    append `bankSheet` after the Entries sheet. `Bank`:

```csharp
    /// Carried hours and each day taken off, signed as they move the bank. The dashboard's bank card adds them to the
    /// day balances.
    static (Xlsx.Sheet Sheet, double Total) Bank(Target target, IReadOnlyList<TimeOff> timeOff, IReadOnlyList<Carried> carried)
    {
        string Note(string key) => timeOff.FirstOrDefault(t => string.CompareOrdinal(t.Start, key) <= 0 && string.CompareOrdinal(key, t.End) <= 0).Note ?? "";
        var rows = carried.Select(c => (Date: c.Date, Kind: "Carried", Hours: c.Hours, Note: c.Note))
            .Concat((target.Booked ?? new Dictionary<string, double>()).Select(kv => (Date: kv.Key, Kind: "Time Off", Hours: -kv.Value / 3600, Note: Note(kv.Key))))
            .OrderBy(r => r.Date, StringComparer.Ordinal).ToList();
        var cells = rows.Select(r => (IReadOnlyList<Cell>)[Cal.FromKey(r.Date) is { } d ? new Cell.Number(Xlsx.Serial(d), Style.Date) : new Cell.Text(r.Date),
                                                       new Cell.Text(r.Kind), new Cell.Number(r.Hours, Style.Balance), new Cell.Text(r.Note)]).ToList();
        return (Xlsx.TableSheet("Hours Bank", "tblBank", ["Date", "Kind", "Hours", "Note"], cells, null, [16, 12, 10, 40]), rows.Sum(r => r.Hours));
    }
```

    (Adapt the cell/row collection types to what `Xlsx.TableSheet` takes — read its signature and the `List(...)` sheet
    builder right above for the exact shapes.)
  - Dashboard card: `new Cell.Formula("SUM(tblDays[Balance])+SUM(tblBank[Hours])", Sum(d => d.Balance, days) + bank, Style.CardBank)`,
    caption `"worked minus target, plus carried, less time off"`.
  - `Store.MasterWorkbook`: `Report.Master(entries, target, ProfileNames, now, timeOff, carried)`.

- [ ] **Step 4: Run the tests** — expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add windows/src/Outatime.Core windows/tests
git commit -m "Windows exports: Time Off column and the Hours Bank sheet"
```

---

### Task 7: Windows UI — Hours Bank and Log windows, panel and Logbook changes, strings

**Files:**
- Create: `windows/src/Outatime/HoursBankWindow.cs`, `windows/src/Outatime/LogWindow.cs`
- Modify: `windows/src/Outatime/Shell.cs`, `MenuPanel.cs`, `Logbook/LogbookWindow.cs`, `DevSupport.cs`,
  `windows/src/Outatime.Core/WhatsNew.cs`, `Strings.json` (via `scripts/strings.py`), `Strings.Windows.json`,
  `.github/workflows/windows.yml`
- Test: `windows/tests/Outatime.Tests/UiTests.cs`

**Interfaces:**
- Consumes: Task 5 Store API; `Ui` helpers (`Text`, `H`, `V`, `Spread`, `Button`, `IconButton`, `Icon`, `Section`,
  `Labeled`, `With`, `Green`, `Red`, `Accent`).
- Produces: `LogKind` (+ `LogKinds.All`, `Label()`, `Symbol()`, `Fill()`), `LogItem` (`New`, `TimeOffItem`,
  `HolidayItem`, `CarriedItem`), `Shell.ShowHoursBank()`, `Shell.ShowLog(LogItem)`, `HoursBankWindow.LogMenu(Shell)`.

- [ ] **Step 1: Write the failing UI test** — in `UiTests`:

```csharp
    [AvaloniaFact]
    public void HoursBankLogsTimeOff()
    {
        var (shell, store, _) = Start();
        shell.ShowPanel();
        Assert.DoesNotContain(All<Button>(Window(nameof(MenuPanel))), b => b.Classes.Contains("tile")
            && b.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == Activity.Travel.Label()));
        shell.ShowHoursBank();
        shell.ShowLog(new LogItem.New(LogKind.TimeOff));
        var log = Window(nameof(LogWindow));
        var before = store.TimeOff.Count;
        All<Button>(log).First(b => b.IsDefault).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(before + 1, store.TimeOff.Count);
        Assert.Contains(All<TextBlock>(Window(nameof(HoursBankWindow))), t => t.Text == Loc.T("Time Off"));
    }
```

  and add `shell.ShowHoursBank(); shell.ShowLog(new LogItem.New(LogKind.Travel));` plus `nameof(HoursBankWindow)`,
  `nameof(LogWindow)` to `EveryWindowOpens`. Update `PanelTilesStartAndStop` if it touches Travel (it uses Break — fine).

- [ ] **Step 2: Run to verify it fails** — `The name 'LogKind' does not exist`.

- [ ] **Step 3: Create `HoursBankWindow.cs`**

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FluentIcons.Common;
using static Outatime.App.Ui;

namespace Outatime.App;

/// What a Log form records: time off and holidays, Travel and Out of Office blocks, or hours carried in.
public enum LogKind { TimeOff, Holiday, Travel, OutOfOffice, Carried }

public static class LogKinds
{
    public static readonly LogKind[] All = Enum.GetValues<LogKind>();

    public static string Label(this LogKind k) => k switch
    {
        LogKind.TimeOff => Loc.T("Time Off"), LogKind.Holiday => Loc.T("Holiday"), LogKind.Travel => Activity.Travel.Label(),
        LogKind.OutOfOffice => Activity.OutOfOffice.Label(), _ => Loc.T("Carried Hours"),
    };

    public static Symbol Symbol(this LogKind k) => k switch
    {
        LogKind.TimeOff => FluentIcons.Common.Symbol.History, LogKind.Holiday => FluentIcons.Common.Symbol.Beach,
        LogKind.Travel => Activity.Travel.Symbol(), LogKind.OutOfOffice => Activity.OutOfOffice.Symbol(),
        _ => FluentIcons.Common.Symbol.ArrowDownload,
    };

    public static IBrush Fill(this LogKind k) => k switch
    {
        LogKind.TimeOff => Brush.Parse("#64D2FF"), LogKind.Holiday => Brushes.Gray, LogKind.Travel => Activity.Travel.Fill(),
        LogKind.OutOfOffice => Activity.OutOfOffice.Fill(), _ => Green,
    };
}

/// A Log form's subject: something new, or a bank record being edited.
public abstract record LogItem
{
    public sealed record New(LogKind What) : LogItem;
    public sealed record TimeOffItem(TimeOff Record) : LogItem;
    public sealed record HolidayItem(DateTimeOffset Start, DateTimeOffset End) : LogItem;
    public sealed record CarriedItem(Carried Record) : LogItem;

    public LogKind Kind => this switch
    {
        New n => n.What, TimeOffItem => LogKind.TimeOff, HolidayItem => LogKind.Holiday, _ => LogKind.Carried,
    };
}

/// The hours bank: its balance, and every deposit and withdrawal that isn't a day's work.
public sealed class HoursBankWindow : AppWindow
{
    readonly Shell shell;
    Store Store => shell.Store;
    Settings Settings => shell.Settings;
    readonly StackPanel body = new() { Spacing = 12 };

    public HoursBankWindow(Shell shell)
    {
        this.shell = shell;
        Title = Loc.T("Hours Bank");
        Width = 480;
        Height = 560;
        MinWidth = 420;
        MinHeight = 360;

        var log = Button(H(6, Icon(FluentIcons.Common.Symbol.AddCircle, 16), Text(Loc.T("Log"))), () => { });
        log.Flyout = LogMenu(shell);
        var done = Button(Loc.T("Done"), Close, classes: "accent");
        done.IsDefault = true;
        var footer = new Border { Padding = new Thickness(14), Child = Spread(H(0, log), done) };
        DockPanel.SetDock(footer, Dock.Bottom);
        Content = new DockPanel { Children = { footer, new ScrollViewer { Content = new Border { Padding = new Thickness(16, 16, 16, 4), Child = body } } } };

        Store.Changed += Render;
        Settings.Changed += Render;
        Closed += (_, _) => { Store.Changed -= Render; Settings.Changed -= Render; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Render();
    }

    /// The Log kinds, each opening its form; the panel's Log button shows the same menu.
    public static MenuFlyout LogMenu(Shell shell)
    {
        var menu = new MenuFlyout();
        foreach (var k in LogKinds.All)
        {
            var item = new MenuItem { Header = k.Label(), Icon = Icon(k.Symbol(), 16, k.Fill()) };
            item.Click += (_, _) => shell.ShowLog(new LogItem.New(k));
            menu.Items.Add(item);
        }
        return menu;
    }

    void Render()
    {
        body.Children.Clear();
        var target = Settings.Target(Store);
        var bank = Store.Bank(target, Settings.BankStart(target));
        var caption = new List<string>();
        if (target.Seconds > 0 && bank.Balance > 0) caption.Add(Loc.T("%@ days off", (bank.Balance / target.Seconds).ToString("0.#", Loc.Culture)));
        if (bank.BookedAhead > 0) caption.Add(Loc.T("%@ booked ahead", bank.BookedAhead.Hm()));
        body.Children.Add(V(4,
            Text(bank.Balance.Signed(), 34, FontWeight.SemiBold, bank.Balance >= 0 ? Green : Red, tabular: true).With(t => t.HorizontalAlignment = HorizontalAlignment.Center),
            caption.Count == 0 ? null : Text(string.Join(" · ", caption), 13, secondary: true, tabular: true).With(t => t.HorizontalAlignment = HorizontalAlignment.Center)));

        var rows = Rows(target);
        if (rows.Count == 0)
            body.Children.Add(Text(Loc.T("Nothing logged yet. Log time off, holidays, or overtime carried in from before."), secondary: true)
                .With(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; }));
        else
            body.Children.Add(Section(rows.Select(Row).ToArray()));
    }

    sealed record BankRow(LogItem Item, DateTimeOffset Start, DateTimeOffset End, double? Hours, string Note);

    /// Time off, carried hours and holidays, newest first.
    List<BankRow> Rows(Target target)
    {
        var rows = new List<BankRow>();
        foreach (var t in Store.TimeOff)
            if (Cal.FromKey(t.Start) is { } s && Cal.FromKey(t.End) is { } e) rows.Add(new(new LogItem.TimeOffItem(t), s, e, -target.Cost(t), t.Note));
        foreach (var c in Store.Carried)
            if (Cal.FromKey(c.Date) is { } d) rows.Add(new(new LogItem.CarriedItem(c), d, d, c.Hours * 3600, c.Note));
        foreach (var (s, e) in Store.Holidays()) rows.Add(new(new LogItem.HolidayItem(s, e), s, e, null, ""));
        return rows.OrderByDescending(r => r.Start).ToList();
    }

    Control Row(BankRow r)
    {
        var kind = r.Item.Kind;
        var label = V(2, Text(Span(r.Start, r.End)), Text(r.Note.Length > 0 ? r.Note : kind.Label(), 11, secondary: true));
        var hours = Text(r.Hours?.Signed() ?? "—", tabular: true, color: r.Hours > 0 ? Green : null, secondary: r.Hours == null);
        var delete = IconButton(FluentIcons.Common.Symbol.Delete, () => Delete(r.Item), Loc.T("Delete"), Red);
        var open = Button(H(10, Icon(kind.Symbol(), 16, kind.Fill()), label), () => shell.ShowLog(r.Item), classes: "plain");
        open.HorizontalContentAlignment = HorizontalAlignment.Left;
        return Spread(open, hours, delete);
    }

    /// "24 Dec – 2 Jan", or one day; the year only when it isn't this one.
    static string Span(DateTimeOffset start, DateTimeOffset end)
    {
        string F(DateTimeOffset d) => d.Format(Cal.Date(d).Year == Cal.Date(Clock.Now).Year ? "d MMM" : "d MMM yyyy");
        return Cal.SameDay(start, end) ? F(start) : $"{F(start)} – {F(end)}";
    }

    void Delete(LogItem item)
    {
        switch (item)
        {
            case LogItem.TimeOffItem t: Store.DeleteTimeOff(t.Record.Id); break;
            case LogItem.CarriedItem c: Store.DeleteCarried(c.Record.Id); break;
            case LogItem.HolidayItem h: Store.RemoveHoliday((h.Start, h.End)); break;
        }
    }
}
```

- [ ] **Step 4: Create `LogWindow.cs`**

```csharp
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using static Outatime.App.Ui;

namespace Outatime.App;

/// One Log form: time off, a holiday, Travel or Out of Office blocks, or carried hours; new, or a record being edited.
public sealed class LogWindow : AppWindow
{
    readonly Shell shell;
    readonly LogItem item;
    Store Store => shell.Store;
    Settings Settings => shell.Settings;
    readonly CalendarDatePicker from = new() { IsTodayHighlighted = true, MinWidth = 180 };
    readonly CalendarDatePicker to = new() { IsTodayHighlighted = true, MinWidth = 180 };
    readonly TextBox hours = new() { MinWidth = 140, HorizontalContentAlignment = HorizontalAlignment.Right };
    readonly TextBox note = new() { MinWidth = 220 };
    readonly ComboBox client = new() { MinWidth = 220 };
    readonly List<Guid?> clients = [null];
    readonly Control hoursRow;
    readonly Button save;

    public LogWindow(Shell shell, LogItem item)
    {
        this.shell = shell;
        this.item = item;
        var kind = item.Kind;
        Title = kind.Label();
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;

        var today = Cal.Date(Clock.Now);
        DateTime start = today, end = today;
        double? h = null;
        var text = "";
        DateTime Day(string key, DateTime fallback) => Cal.FromKey(key) is { } d ? Cal.Date(d) : fallback;
        switch (item)
        {
            case LogItem.TimeOffItem t: start = Day(t.Record.Start, today); end = Day(t.Record.End, start); h = t.Record.Hours; text = t.Record.Note; break;
            case LogItem.HolidayItem r: start = Cal.Date(r.Start); end = Cal.Date(r.End); break;
            case LogItem.CarriedItem c: start = end = Day(c.Record.Date, today); h = c.Record.Hours; text = c.Record.Note; break;
        }
        from.SelectedDate = start;
        to.SelectedDate = end;
        hours.Text = h?.ToString("0.##", Loc.Culture) ?? "";
        hours.PlaceholderText = kind switch
        {
            LogKind.TimeOff => Loc.T("Full day"), LogKind.Carried => "32", _ => Settings.TargetHours.ToString("0.##", Loc.Culture),
        };
        note.Text = text;
        clients.AddRange(Store.ActiveProfiles.Select(p => (Guid?)p.Id));
        client.ItemsSource = new[] { Loc.T("No client") }.Concat(Store.ActiveProfiles.Select(p => p.Name)).ToList();
        client.SelectedIndex = Math.Max(0, clients.IndexOf(Store.CurrentProfile));

        hoursRow = Labeled(kind is LogKind.Travel or LogKind.OutOfOffice ? Loc.T("Hours per day") : Loc.T("Hours"), hours);
        var cancel = Button(Loc.T("Cancel"), Close);
        cancel.IsCancel = true;
        save = Button(Loc.T("Save"), Save, classes: "accent");
        save.IsDefault = true;

        var rows = new List<Control?>
        {
            H(8, Icon(kind.Symbol(), 16, kind.Fill()), Text(kind.Label(), 13, FontWeight.SemiBold)),
            Labeled(kind == LogKind.Carried ? Loc.T("Date") : Loc.T("From"), from),
            kind == LogKind.Carried ? null : Labeled(Loc.T("To"), to),
            kind == LogKind.Holiday ? null : hoursRow,
            kind == LogKind.Travel && Store.ActiveProfiles.Count > 0 ? Labeled(Loc.T("Client"), client) : null,
            kind is LogKind.TimeOff or LogKind.Carried ? Labeled(Loc.T("Note"), note) : null,
            Text(Loc.T(Footer(kind)), 11, secondary: true).With(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; }),
        };
        var buttons = H(8, cancel, save);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        Content = new Border { Padding = new Thickness(16), Child = V(14, Section(rows.ToArray()), buttons) };

        from.SelectedDateChanged += (_, _) =>
        {
            if (from.SelectedDate is { } f && (to.SelectedDate is not { } t || t < f)) to.SelectedDate = f;
            Refresh();
        };
        to.SelectedDateChanged += (_, _) => Refresh();
        hours.TextChanged += (_, _) => Refresh();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Refresh();
    }

    static string Footer(LogKind kind) => kind switch
    {
        LogKind.TimeOff => "Each workday costs a day's target, or the hours you enter. Weekends and holidays cost nothing. It comes off the bank as soon as you save.",
        LogKind.Holiday => "Holidays owe nothing and leave the bank alone.",
        LogKind.Travel => "Adds a Travel block on each workday, after the day's last block or at your usual start.",
        LogKind.OutOfOffice => "Adds an Out of Office block on each workday, after the day's last block or at your usual start.",
        _ => "Overtime from before you tracked it here, or a negative amount for hours paid out.",
    };

    DateTimeOffset From => Cal.At((from.SelectedDate ?? Cal.Date(Clock.Now)).Date);
    DateTimeOffset To { get { var t = Cal.At((to.SelectedDate ?? from.SelectedDate ?? Cal.Date(Clock.Now)).Date); return t < From ? From : t; } }
    bool OneDay => Cal.SameDay(From, To);

    /// Empty is null; anything unreadable is NaN, which keeps Save off.
    double? Hours => string.IsNullOrWhiteSpace(hours.Text) ? null
        : double.TryParse(hours.Text, NumberStyles.Number, Loc.Culture, out var v) ? v : double.NaN;

    void Refresh()
    {
        // Time off takes hours only for a single day.
        if (item.Kind == LogKind.TimeOff) hoursRow.IsVisible = OneDay;
        var h = Hours;
        save.IsEnabled = item.Kind switch
        {
            LogKind.Carried => h is { } c && !double.IsNaN(c) && c != 0,
            LogKind.Holiday => true,
            _ => h is not { } x || (!double.IsNaN(x) && x > 0),
        };
    }

    void Save()
    {
        var h = Hours;
        switch (item.Kind)
        {
            case LogKind.TimeOff:
                Store.Upsert(new TimeOff(item is LogItem.TimeOffItem t ? t.Record.Id : Guid.NewGuid(), From.DayKey(), To.DayKey(), OneDay ? h : null, note.Text ?? ""));
                break;
            case LogKind.Holiday:
                Store.SetHoliday(From, To, item is LogItem.HolidayItem r ? (r.Start, r.End) : null);
                break;
            case LogKind.Travel or LogKind.OutOfOffice:
                var target = Settings.Target(Store);
                Store.Log(item.Kind == LogKind.Travel ? Activity.Travel : Activity.OutOfOffice, From, To, h ?? Settings.TargetHours,
                          clients[Math.Max(0, client.SelectedIndex)], target, new Stats(Store.Entries, target, Settings.BankStart(target)).UsualStart);
                break;
            case LogKind.Carried:
                Store.Upsert(new Carried(item is LogItem.CarriedItem c ? c.Record.Id : Guid.NewGuid(), From.DayKey(), h ?? 0, note.Text ?? ""));
                break;
        }
        Close();
    }
}
```

- [ ] **Step 5: `Shell.cs`** — next to `ShowObjectives`:

```csharp
    public void ShowHoursBank() => Show("hours-bank", () => new HoursBankWindow(this));
    /// One Log form at a time: asking for another replaces it.
    public void ShowLog(LogItem item)
    {
        if (windows.TryGetValue("log", out var open)) open.Close();
        Show("log", () => new LogWindow(this, item));
    }
```

- [ ] **Step 6: `Logbook/LogbookWindow.cs`**
  - Toolbar: delete the `off` ToggleButton; add
    `var bank = Ui.IconButton(Symbol.BuildingBank, shell.ShowHoursBank, Loc.T("Hours Bank"));` and return
    `Ui.Spread(nav, Ui.H(2, zoomOut, zoomIn), Ui.H(2, stats, goals, bank), Ui.H(2, add, templates, export))`.
  - Stats flyout: `new Stats(store.Entries, target, settings.BankStart(target), null, store.Carried)`.
  - `DayRow`: after the holiday icon line add
    `else if (target.Booked?.GetValueOrDefault(d.DayKey()) > 0) right.Children.Add(Ui.Icon(Symbol.History, 14, White(null)).With(i => i.Opacity = selected ? 1 : 0.62));`
    (turn the existing `if (dayOff)` into the `if` of that pair).
  - Remove `using` of `ToggleButton` if it becomes unused.

- [ ] **Step 7: `MenuPanel.cs`**
  - `Tiles()`: `new[] { Activity.Work, Activity.Break, Activity.Lunch, Activity.Extra }`.
  - `Status()`: after the elapsed text in the running branch:

```csharp
            // Travel and Out of Office are logged now; one still running from before has no tile to stop it.
            if (running.Activity is Activity.Travel or Activity.OutOfOffice)
                title.Children.Add(IconButton(Symbol.RecordStop, Store.Stop, Loc.T("Stop"), Red, 18));
```

  - `Totals()`: `var bank = Store.Bank(target, since, now);`; `new Stats(Store.Entries, target, since, now, Store.Carried)`;
    `Row` takes a `Control label` overload (`void Row(string label, …) => Row(Text(label, 11, secondary: true), …)`), and
    the bank line uses
    `Button(Text(Loc.T("Since %@", since.Format("d MMM")), 11, secondary: true), shell.ShowHoursBank, Loc.T("Hours Bank"), "plain")`
    with zero padding as its label, `bank.Worked`, `bank.Balance`.
  - `Footer()`: `var log = Button(H(6, Icon(Symbol.Edit, 15), Text(Loc.T("Log"))), () => { }, classes: "plain"); log.Flyout = HoursBankWindow.LogMenu(shell);`
    padded like the others and placed between `export` and `objective`.
  - `ObjectiveInfo.Progress` (line ~385): `store.Bank(target, settings.BankStart(target)).Balance`.

- [ ] **Step 8: `DevSupport.cs`** — sample data gains a time-off day and carried hours so the windows show something:
  `store.Upsert(new TimeOff(Cal.AddDays(today, -3).DayKey(), Cal.AddDays(today, -3).DayKey(), note: "Dentist"));`
  `store.Upsert(new Carried(Cal.AddDays(today, -60).DayKey(), 24, "Overtime before Outatime"));`
  and `Open()` gains `case "hoursbank": shell.ShowHoursBank(); break;` and `case "log": shell.ShowLog(new LogItem.New(LogKind.TimeOff)); break;`.
  In `.github/workflows/windows.yml`, `OUTATIME_OPEN = 'logbook,settings,objectives,hoursbank,log,about,panel'`.

- [ ] **Step 9: `WhatsNew.cs`** — mirror the Mac's 1.3 Hours Bank item (same title and detail strings) in the Windows
  release list, in the release where Objectives' item is; if the Windows list has no 1.3/1.0 equivalent, put it with the
  newest release.

- [ ] **Step 10: Strings** — `python3 windows/scripts/strings.py` (copies es and pt-BR from the Mac catalog). None of the
  new keys are Windows-only, so `Strings.Windows.json` only changes if the translation test reports a key.

- [ ] **Step 11: Run the tests**

Run: `cd windows && ~/.dotnet/dotnet test tests/Outatime.Tests 2>&1 | tail -5`
Expected: `Passed!`. Fix any Fluent symbol name that doesn't exist (`BuildingBank`, `History`, `ArrowDownload`,
`RecordStop`, `Edit`) by picking the nearest one in `FluentIcons.Common.Symbol`.

- [ ] **Step 12: Commit**

```bash
git add windows .github/workflows/windows.yml
git commit -m "Windows: Hours Bank and Log windows; Travel and Out of Office leave the panel's tiles"
```

---

### Task 8: Manual, READMEs

**Files:**
- Modify: `docs/manual/index.html` and `docs/manual/{de,es,fr,it,ja,pt-BR,zh-Hans}/index.html`, `README.md`, `windows/README.md`

- [ ] **Step 1: English manual** (`docs/manual/index.html`):
  - The tile mock-up: remove the Travel and Out of Office tiles; "The six activities" → "The four activities" (both
    the list item and the `<h3>`), and in the activities table move Travel and Out of Office under a note: "Travel and
    Out of Office are logged from **Log** in the menu or the Logbook's Hours Bank, a day or a stretch of days at a time."
  - Replace "Mark a day off with the umbrella button in the Logbook toolbar." with "Holidays and time off go in the
    **Hours Bank** (Logbook toolbar, or click the bank's row in the menu)."
  - After the hours-bank bullet, add a subsection:

```html
      <h3>The Hours Bank</h3>
      <p>The bank adds up every day's balance. Open <strong>Hours Bank</strong> in the Logbook toolbar to see it and to log what changes it:</p>
      <ul>
        <li><strong>Time Off</strong> — a range of days, or one day with a number of hours. Each workday costs your daily target (or those hours) and comes off the bank as soon as you save, even if it's next month. Weekends and holidays in the range cost nothing.</li>
        <li><strong>Holiday</strong> — days that owe nothing and leave the bank alone.</li>
        <li><strong>Carried Hours</strong> — overtime from before you used Outatime, or a negative amount for hours paid out.</li>
        <li><strong>Travel</strong> and <strong>Out of Office</strong> — blocks on each workday of a range, after the day's last block or at your usual start.</li>
      </ul>
      <p>Click a row to change it. A day off shows as even in the Logbook, not short; the bank is where the hours go.</p>
```

- [ ] **Step 2: The seven translations** — make the same structural edits in each language's manual, translating the
  new text with the terms that language's app strings use (`Localizable.xcstrings`: "Hours Bank", "Time Off",
  "Holiday", "Carried Hours", "Log", Travel, Out of Office).

- [ ] **Step 3: READMEs** — `README.md` feature list: "six tiles" → "four tiles: Work, Break, Lunch, Extra", and a new
  bullet "**Hours Bank** — time off (days or hours), holidays and overtime carried over; Travel and Out of Office logged
  by the day." Exports section: Summary gains "Time Off"; the master workbook gains an *Hours Bank* sheet. Same edits in
  `windows/README.md` where it lists features.

- [ ] **Step 4: Check** — `grep -rn -i "six\|umbrella" docs/manual README.md windows/README.md` returns nothing stale.

- [ ] **Step 5: Commit**

```bash
git add docs/manual README.md windows/README.md
git commit -m "Manual and READMEs: Hours Bank, Log, four tiles"
```

---

### Task 9: Review, merge, ship

- [ ] **Step 1:** Both suites green: Mac `xcodebuild … test`, Windows `dotnet test`.
- [ ] **Step 2:** Whole-branch review (superpowers:requesting-code-review) against the spec; fix findings.
- [ ] **Step 3:** Merge `windows` into `main` (`git checkout main && git merge --no-ff windows`), push `main` and
  `windows`. Windows CI builds from `main`.
- [ ] **Step 4:** Mac beta: invoke the `burn-it` skill with `beta` → `1.3-beta.3` (build number +1), `scripts/release.sh --beta`,
  verify the pre-release and the feed as the skill says.
