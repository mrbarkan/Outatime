import Foundation
import Testing
@testable import Outatime

nonisolated struct OutatimeTests {
    let cal = Calendar.current
    let day = Calendar.current.date(from: DateComponents(year: 2026, month: 9, day: 3))!

    func at(_ h: Int, _ m: Int = 0) -> Date { cal.date(bySettingHour: h, minute: m, second: 0, of: day)! }

    @Test func templateRoundTrip() {
        let entries = [Entry(activity: .work, start: at(9), end: at(12, 30), tag: "acme"),
                       Entry(activity: .lunch, start: at(12, 30), end: at(13, 15))]
        let t = DayTemplate(name: "Normal", entries: entries)
        let other = cal.date(byAdding: .day, value: -10, to: day)!
        let applied = t.entries(on: other)
        #expect(applied.count == 2)
        #expect(applied[0].tag == "acme")
        #expect(applied[0].duration == 3.5 * 3600)
        #expect(cal.isDate(applied[1].start, inSameDayAs: other))
        #expect(cal.component(.hour, from: applied[1].start) == 12)
    }

    @Test func dailyCSV() {
        let entries = [Entry(activity: .work, start: at(9), end: at(17), tag: "a, \"b\""),
                       Entry(activity: .extra, start: at(20), end: at(21, 30))]
        let csv = CSV.daily(entries, targetHours: 8)
        let lines = csv.split(separator: "\n")
        #expect(lines.count == 2)
        #expect(lines[1] == "2026-09-03,8.00,0.00,0.00,1.50,+1.50,\"a, \"\"b\"\"\"")
    }

    @Test func totals() {
        #expect(1.5 * 3600 == TimeInterval(5400))
        #expect(TimeInterval(5400).hm == "1h 30m")
        #expect(Store.totals([Entry(activity: .work, start: at(9), end: at(10)),
                              Entry(activity: .work, start: at(11), end: at(11, 5))])[.work] == 3900)
    }
}
