import Foundation
import Testing
@testable import Outatime

nonisolated struct OutatimeTests {
    let cal = Calendar.current
    let day = Calendar.current.date(from: DateComponents(year: 2026, month: 9, day: 3))!

    func at(_ h: Int, _ m: Int = 0, _ s: Int = 0) -> Date { cal.date(bySettingHour: h, minute: m, second: s, of: day)! }

    @Test func templateRoundTrip() {
        let entries = [Entry(activity: .work, start: at(9), end: at(12, 30), notes: ["acme"]),
                       Entry(activity: .lunch, start: at(12, 30), end: at(13, 15))]
        let t = DayTemplate(name: "Normal", entries: entries)
        let other = cal.date(byAdding: .day, value: -10, to: day)!
        let applied = t.entries(on: other)
        #expect(applied.count == 2)
        #expect(applied[0].notes == ["acme"])
        #expect(applied[0].duration == 3.5 * 3600)
        #expect(cal.isDate(applied[1].start, inSameDayAs: other))
        #expect(cal.component(.hour, from: applied[1].start) == 12)
    }

    @Test func dailyCSV() {
        let entries = [Entry(activity: .work, start: at(9), end: at(17), notes: ["a, \"b\""]),
                       Entry(activity: .extra, start: at(20), end: at(21, 30))]
        let csv = CSV.daily(entries, month: day, target: Target(seconds: 8 * 3600, since: day), now: at(22))
        let lines = csv.split(separator: "\n")
        #expect(lines.count == 3)
        #expect(lines[1] == "2026-09-03,8.00,0.00,0.00,1.50,0.00,0.00,+1.50,\"a, \"\"b\"\"\"")
        #expect(lines[2] == "Total,8.00,0.00,0.00,1.50,0.00,0.00,+1.50,")
    }

    /// A slash in an export name (the en_BR month "09/2026") lands on disk as a colon and Excel can't open the file.
    @Test func exportNameIsFileSafe() {
        #expect(exportName(day, ".xlsx") == "Outatime 2026-09.xlsx")
    }

    @Test func monthReportWorkbook() {
        #expect(Zip.crc32(Data("123456789".utf8)) == 0xCBF4_3926)
        #expect(XLSX.serial(at(12)) == 46268.5)
        let entries = [Entry(activity: .work, start: at(9), end: at(17), notes: ["R&D <q>"]),
                       Entry(activity: .outOfOffice, start: at(17), end: at(18))]
        let data = Report.month(entries, month: day, target: Target(seconds: 8 * 3600, since: day), now: at(22))
        #expect(data.prefix(4) == Data([0x50, 0x4B, 0x03, 0x04]))
        let text = String(decoding: data, as: UTF8.self)
        #expect(text.contains(#"<c r="J2" s="5"><v>1.0</v></c>"#))  // balance: 9h worked, 8h owed
        #expect(text.contains(#"<f>SUBTOTAL(109,tblDays[Balance])</f><v>1.0</v>"#))
        #expect(text.contains(#"name="tblDays" displayName="tblDays" ref="A1:K3" totalsRowCount="1""#))
        #expect(text.contains("R&amp;D &lt;q&gt;"))
        #expect(text.contains(">Out of Office<"))
        #expect(!text.contains("Dashboard"))
    }

    /// The master holds the same tables (no totals row, so pasted month rows extend them) behind a dashboard.
    @Test func masterWorkbook() {
        let start = cal.date(byAdding: .day, value: -1, to: day)!  // Wednesday: owes 8h, nothing logged
        let entries = [Entry(activity: .work, start: at(9), end: at(18))]
        let data = Report.master(entries, target: Target(seconds: 8 * 3600, since: start), now: at(22))
        let text = String(decoding: data, as: UTF8.self)
        #expect(text.contains(#"<sheet name="Dashboard" sheetId="1""#))
        #expect(text.contains(#"name="tblDays" displayName="tblDays" ref="A1:K3" totalsRowShown="0""#))
        #expect(text.contains("<f>SUM(tblDays[Balance])</f><v>-7.0</v>"))  // +1 today, -8 yesterday
        #expect(text.contains(#"<f>COUNTIF(tblDays[Worked],&quot;&gt;0&quot;)</f><v>1.0</v>"#))
        #expect(text.contains("<c:f>Dashboard!$C$38:"))
    }

    /// The store must read back what it wrote, or every relaunch silently starts empty and overwrites the file.
    @Test @MainActor func storeRoundTrip() throws {
        let url = FileManager.default.temporaryDirectory.appending(path: "outatime-test-\(UUID().uuidString)/data.json")
        defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }
        let store = Store(url: url)
        store.start(.work)
        store.addNote("hello")
        let reloaded = Store(url: url)
        #expect(reloaded.entries.count == 1)
        #expect(reloaded.entries.first?.notes == ["hello"])
        #expect(reloaded.running != nil)
    }

    @Test @MainActor func noteLandsOnLastEntryAfterStop() {
        let store = Store(url: FileManager.default.temporaryDirectory.appending(path: "outatime-test-\(UUID().uuidString)/data.json"))
        store.start(.work)
        store.stop()
        store.addNote("late note")
        #expect(store.entries.first?.notes == ["late note"])
    }

    @Test func blockDragSnapping() {
        let magnet = 4.0 / 56 * 3600
        let start = cal.startOfDay(for: day)
        // Tracked back to back: a few ms apart, still one shared border.
        let a = Entry(activity: .work, start: at(9), end: at(10).addingTimeInterval(-0.004))
        let b = Entry(activity: .break, start: at(10), end: at(11, 3).addingTimeInterval(27))
        #expect(BlockDrag.neighbour(of: b, .start, in: [a])?.id == a.id)

        // A shared border can be nudged one grid step; the neighbour's own edge must not pull it back.
        let nudged = BlockDrag.drag(b, .start, by: 200, others: [a], dayStart: start, magnet: magnet, drop: true)
        #expect(nudged.start == at(10, 5))
        // Resizing the top leaves an off-grid bottom alone.
        #expect(nudged.end == b.end)

        // Moving onto a block below: the end sticks to its start, and dropping keeps it there.
        let c = Entry(activity: .lunch, start: at(12), end: at(13))
        let moved = BlockDrag.drag(b, .move, by: 55 * 60, others: [c], dayStart: start, magnet: magnet, drop: true)
        #expect(moved.end == at(12))
        #expect(moved.duration == b.duration)
    }

    @Test func breakCountsAsWork() {
        #expect([Activity.work: 3600.0, .break: 600, .lunch: 1800, .extra: 300].worked() == 4500)
        #expect([Activity.work: 3600.0, .break: 600, .lunch: 1800].worked(excluding: "break") == 5400)
    }

    /// Thu 3 – Wed 9 Sep against 8h: Thu 9h (+1), Fri forgotten (−8), a day off Mon 7 (0), Sat worked 2h (+2, weekends owe
    /// nothing), and today (Tue 8) only 3h so far — in progress, so no shortfall yet.
    @Test func periodBalance() {
        func on(_ d: Int, _ h: Double) -> Entry {
            let start = cal.date(from: DateComponents(year: 2026, month: 9, day: d, hour: 9))!
            return Entry(activity: .work, start: start, end: start + h * 3600)
        }
        let entries = [on(3, 9), on(5, 2), on(8, 3), Entry(activity: .lunch, start: at(18), end: at(19))]
        let now = cal.date(from: DateComponents(year: 2026, month: 9, day: 8, hour: 14))!
        let target = Target(seconds: 8 * 3600, daysOff: ["2026-09-07"], since: day)
        let week = DateInterval(start: day, end: cal.date(byAdding: .day, value: 7, to: day)!)
        let b = Store.balance(entries, in: week, target: target, now: now)
        #expect(b.worked == 14 * 3600)
        #expect(b.balance == -5 * 3600)
        // Nothing is owed before tracking began.
        #expect(target.owed(on: cal.date(byAdding: .day, value: -1, to: day)!, now: now) == 0)
    }

    @Test @MainActor func timerRollsOverAtMidnight() {
        let store = Store(url: FileManager.default.temporaryDirectory.appending(path: "outatime-test-\(UUID().uuidString)/data.json"))
        store.entries = [Entry(activity: .work, start: at(17))]
        let nextMorning = cal.date(byAdding: .hour, value: 16, to: at(17))!  // 09:00 the next day
        store.rollOver(now: nextMorning)
        #expect(store.entries.count == 2)
        #expect(store.entries[0].end == cal.startOfDay(for: nextMorning))
        #expect(store.running?.start == cal.startOfDay(for: nextMorning))
        #expect(store.runningSince == at(17))
    }

    /// Double-clicking inside a work block cuts a break in; adding in a gap stops at the next block.
    @Test @MainActor func insertCutsAndFills() {
        let store = Store(url: FileManager.default.temporaryDirectory.appending(path: "outatime-test-\(UUID().uuidString)/data.json"))
        store.entries = [Entry(activity: .work, start: at(9), end: at(12)), Entry(activity: .lunch, start: at(13), end: at(14))]
        store.insert(.break, at: at(10), length: 900)
        let work = store.entries.filter { $0.activity == .work }.sorted { $0.start < $1.start }
        #expect(work.map(\.start) == [at(9), at(10, 15)])
        #expect(work.map(\.end) == [at(10), at(12)])
        store.insert(.work, at: at(12, 30), length: 3600)
        #expect(store.entries.last?.end == at(13))
    }

    /// Work runs a focus round, Break a short one; the break after every 4th finished focus round is long.
    @Test func tomatoRounds() throws {
        let p = Pomodoro(focus: 25 * 60, shortBreak: 5 * 60, longBreak: 15 * 60)
        let on = at(9)
        var entries = [Entry(activity: .work, start: at(8, 50))]  // already working when the tomato went on
        var r = try #require(p.round(entries, since: on))
        #expect(r.phase == .focus && r.number == 1 && r.start == on && r.end == at(9, 25))

        entries[0].end = at(9, 25)
        entries.append(Entry(activity: .break, start: at(9, 25)))
        r = try #require(p.round(entries, since: on))
        #expect(r.phase == .shortBreak && r.end == at(9, 30))

        // A focus round cut short doesn't count.
        entries[1].end = at(9, 30)
        entries += [Entry(activity: .work, start: at(9, 30), end: at(9, 40)), Entry(activity: .lunch, start: at(9, 40))]
        #expect(p.round(entries, since: on) == nil)  // paused on another activity

        entries[3].end = at(10)
        for h in [10, 11, 12] { entries.append(Entry(activity: .work, start: at(h), end: at(h, 25))) }
        entries.append(Entry(activity: .break, start: at(12, 25)))
        r = try #require(p.round(entries, since: on))
        #expect(r.phase == .longBreak && r.end == at(12, 40))

        entries[entries.count - 1].end = at(12, 40)
        entries.append(Entry(activity: .work, start: at(12, 40)))
        #expect(p.round(entries, since: on)?.number == 5)
    }

    /// The switch to Extra at the daily target doesn't restart the focus round.
    @Test func tomatoRoundSpansExtra() throws {
        let p = Pomodoro()
        let entries = [Entry(activity: .work, start: at(16, 50), end: at(17)), Entry(activity: .extra, start: at(17))]
        let r = try #require(p.round(entries, since: at(16, 50)))
        #expect(r.phase == .focus && r.number == 1 && r.start == at(16, 50))
    }

    /// Work past the daily target is cut where the target was reached and carries on as Extra; lunch doesn't count.
    @Test @MainActor func workShiftsToExtraAtTarget() {
        let store = Store(url: FileManager.default.temporaryDirectory.appending(path: "outatime-test-\(UUID().uuidString)/data.json"))
        let target = Target(seconds: 8 * 3600, since: at(0))
        store.entries = [Entry(activity: .work, start: at(9), end: at(12)), Entry(activity: .lunch, start: at(12), end: at(13)),
                         Entry(activity: .work, start: at(13), notes: ["acme"])]
        #expect(!store.shiftToExtra(target, now: at(17, 59)))
        #expect(store.shiftToExtra(target, now: at(18, 0, 30)))
        #expect(store.entries[2].end == at(18))
        #expect(store.running?.activity == .extra && store.running?.start == at(18) && store.running?.notes == ["acme"])
        #expect(!store.shiftToExtra(target, now: at(18, 1)))  // already Extra

        // Work started after the target is reached just becomes Extra.
        store.stop()
        store.entries.append(Entry(activity: .work, start: at(19)))
        #expect(store.shiftToExtra(target, now: at(19, 1)))
        #expect(store.running?.activity == .extra && store.running?.start == at(19))
    }

    @Test @MainActor func breakAndDaysOffAtTarget() {
        let store = Store(url: FileManager.default.temporaryDirectory.appending(path: "outatime-test-\(UUID().uuidString)/data.json"))
        let target = Target(seconds: 8 * 3600, since: at(0))
        store.entries = [Entry(activity: .work, start: at(9), end: at(16, 50)), Entry(activity: .break, start: at(16, 50))]
        #expect(!store.shiftToExtra(target, now: at(17, 30)))  // only Work switches
        #expect(store.running?.activity == .break)

        // A day off owes nothing: Work there is Extra from the start.
        let dayOff = Target(seconds: 8 * 3600, daysOff: [day.dayKey], since: at(0))
        store.entries = [Entry(activity: .work, start: at(10))]
        #expect(store.shiftToExtra(dayOff, now: at(10, 1)))
        #expect(store.entries.count == 1 && store.running?.activity == .extra && store.running?.start == at(10))
    }

    @Test func stretchReminders() {
        #expect(Stretch.reminders(activity: .work, since: at(9), every: 50, now: at(9, 49)) == 0)
        #expect(Stretch.reminders(activity: .work, since: at(9), every: 50, now: at(9, 50)) == 1)
        #expect(Stretch.reminders(activity: .extra, since: at(9), every: 50, now: at(10, 45)) == 2)
        #expect(Stretch.reminders(activity: .break, since: at(9), every: 50, now: at(11)) == 0)
        #expect(Stretch.reminders(activity: .work, since: at(9), every: 0, now: at(11)) == 0)
    }

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

    /// Picking another client mid-block ends the block there and carries on for the new client.
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
        store.rollOver(now: cal.date(byAdding: .hour, value: 16, to: at(17))!)
        #expect(store.running?.profile == acme && store.runningSince == at(17))
        store.entries = [Entry(activity: .work, start: at(9), end: at(10), profile: globex), Entry(activity: .work, start: at(10), profile: acme)]
        #expect(store.runningSince == at(10))  // another client's block isn't the same stretch
        store.shiftToExtra(Target(seconds: 8 * 3600, since: at(0)), now: at(17, 30))
        #expect(store.running?.activity == .extra && store.running?.profile == acme)
        store.entries[store.entries.count - 1].end = at(17, 45)
        store.currentProfile = globex
        store.insert(.travel, at: at(18), length: 600)
        #expect(store.entries.last?.profile == globex)
        store.insert(.break, at: at(18, 30), length: 600)
        #expect(store.entries.last?.profile == nil)
    }

    /// A removed client leaves the picker but keeps its blocks and its name.
    @Test @MainActor func removingClientArchives() throws {
        let store = Store(url: FileManager.default.temporaryDirectory.appending(path: "outatime-test-\(UUID().uuidString)/data.json"))
        let acme = try #require(store.addProfile("Acme"))
        store.select(acme)
        store.entries = [Entry(activity: .work, start: at(9), profile: acme)]
        store.removeProfile(acme)
        #expect(store.activeProfiles.isEmpty && store.profileNames[acme] == "Acme")
        #expect(store.currentProfile == nil && store.running?.profile == acme && store.entries.count == 1)
    }

    /// Client column in the entries, a Clients sheet in the month report, and a client's own month for invoicing.
    @Test func clientExports() {
        let acme = UUID(), names = [acme: "Acme"]
        let entries = [Entry(activity: .work, start: at(9), end: at(12), notes: ["api"], profile: acme),
                       Entry(activity: .break, start: at(12), end: at(12, 15)),
                       Entry(activity: .work, start: at(12, 15), end: at(13, 15)),
                       Entry(activity: .travel, start: at(14), end: at(14, 30), profile: acme)]
        let csv = CSV.entries(entries, names: names).split(separator: "\n")
        #expect(csv[0].hasSuffix(",Hours,Client") && csv[1].hasSuffix(",3.00,Acme") && csv[2].hasSuffix(",0.25,"))

        let target = Target(seconds: 8 * 3600, since: day)
        let month = String(decoding: Report.month(entries, month: day, target: target, names: names, now: at(22)), as: UTF8.self)
        #expect(month.contains(#"<sheet name="Clients""#))
        #expect(month.contains(#"name="tblClients" displayName="tblClients" ref="A1:E4" totalsRowCount="1""#))  // Acme, No client, total
        #expect(month.contains("<f>SUBTOTAL(109,tblClients[Total])</f><v>4.5</v>"))
        #expect(month.contains(">No client<"))
        let plain = String(decoding: Report.month([entries[1]], month: day, target: target, now: at(22)), as: UTF8.self)
        #expect(!plain.contains("Clients"))  // not using clients: no sheet

        let report = String(decoding: Report.client(entries.filter { $0.profile == acme }, name: "Acme", month: day), as: UTF8.self)
        #expect(report.contains(#"<sheet name="Days""#))
        #expect(report.contains("<f>SUBTOTAL(109,tblClientDays[Total])</f><v>3.5</v>"))
        #expect(!report.contains("Balance"))
    }

    @Test func clientExportNameIsFileSafe() {
        #expect(clientExportName(day, "A/B: C") == "Outatime 2026-09 A-B- C.xlsx")
    }

    @Test func totals() {
        #expect(1.5 * 3600 == TimeInterval(5400))
        #expect(TimeInterval(5400).hm == "1h 30m")
        #expect(Store.totals([Entry(activity: .work, start: at(9), end: at(10)),
                              Entry(activity: .work, start: at(11), end: at(11, 5))])[.work] == 3900)
    }
}

@MainActor struct WhatsNewTests {
    let releases = ["1.0.15", "1.0.14", "1.0.13"].map { WhatsNew.Release(version: $0) }

    func shown(lastSeen: String?, current: String, hasData: Bool = true) -> [String] {
        WhatsNew.unseen(releases, lastSeen: lastSeen, current: current, hasData: hasData).map(\.version)
    }

    @Test func showsWhatsNewerThanLastSeen() {
        #expect(shown(lastSeen: "1.0.14", current: "1.0.15") == ["1.0.15"])
        #expect(shown(lastSeen: "1.0.12", current: "1.0.15") == ["1.0.15", "1.0.14", "1.0.13"])  // skipped releases too
        #expect(shown(lastSeen: "1.0.14", current: "1.0.14").isEmpty)
        #expect(shown(lastSeen: "1.0.13", current: "1.0.14") == ["1.0.14"])  // notes for an unreleased version stay hidden
        #expect(shown(lastSeen: "1.0.9", current: "1.0.13") == ["1.0.13"])  // numeric, not alphabetical
    }

    /// A fresh install has nothing to catch up on; an update from before What's New existed has no last-seen version but has data.
    @Test func firstLaunch() {
        #expect(shown(lastSeen: nil, current: "1.0.13", hasData: false).isEmpty)
        #expect(shown(lastSeen: nil, current: "1.0.13", hasData: true) == ["1.0.13"])
    }

    /// At launch the window is opened with the version to catch up from — its content can't come from shared state,
    /// which the window reads before the launch check runs.
    @Test func launchOpensFromLastSeen() {
        let releases = [WhatsNew.Release(version: "1.1")]
        #expect(WhatsNew.catchUpFrom(releases, lastSeen: "1.0.12", current: "1.1", hasData: true) == "1.0.12")
        #expect(WhatsNew.catchUpFrom(releases, lastSeen: nil, current: "1.1", hasData: true) == "0")  // updated from before 1.1
        #expect(WhatsNew.catchUpFrom(releases, lastSeen: "1.1", current: "1.1", hasData: true) == nil)
        #expect(WhatsNew.catchUpFrom(releases, lastSeen: nil, current: "1.1", hasData: false) == nil)  // fresh install
        #expect(WhatsNew.catchUpFrom(releases, lastSeen: "1.1.1", current: "1.1.2", hasData: true) == nil)  // no notes for 1.1.2
    }

    /// 1.1 opened the window empty, so its users get 1.1's notes again with the next release.
    @Test func catchesUpAfterEmpty11() {
        let releases = ["1.1.1", "1.1"].map { WhatsNew.Release(version: $0) }
        #expect(WhatsNew.unseen(releases, lastSeen: "1.1", current: "1.1.1", hasData: true).map(\.version) == ["1.1.1", "1.1"])
        #expect(WhatsNew.unseen(releases, lastSeen: "1.1.1", current: "1.1.1", hasData: true).isEmpty)
    }

    @Test func everyReleaseHasNotes() {
        #expect(WhatsNew.releases.allSatisfy { !$0.new.isEmpty || !$0.fixed.isEmpty })
    }
}

nonisolated struct LocalizationTests {
    /// Every user-facing key must carry both translations, so a new string can't ship half-localized.
    @Test func catalogIsComplete() throws {
        let url = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
            .appending(path: "../Outatime/Localizable.xcstrings")
        let root = try #require(try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any])
        let strings = try #require(root["strings"] as? [String: [String: Any]])
        #expect(strings.count > 40)
        for (key, entry) in strings where entry["shouldTranslate"] as? Bool != false {
            let locs = entry["localizations"] as? [String: Any] ?? [:]
            #expect(locs["es"] != nil && locs["pt-BR"] != nil, "missing translation for \(key)")
        }
    }
}
