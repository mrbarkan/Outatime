import Foundation
import Testing
@testable import Outatime

nonisolated struct OutatimeTests {
    let cal = Calendar.current
    let day = Calendar.current.date(from: DateComponents(year: 2026, month: 9, day: 3))!

    func at(_ h: Int, _ m: Int = 0) -> Date { cal.date(bySettingHour: h, minute: m, second: 0, of: day)! }

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

    @Test func totals() {
        #expect(1.5 * 3600 == TimeInterval(5400))
        #expect(TimeInterval(5400).hm == "1h 30m")
        #expect(Store.totals([Entry(activity: .work, start: at(9), end: at(10)),
                              Entry(activity: .work, start: at(11), end: at(11, 5))])[.work] == 3900)
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
