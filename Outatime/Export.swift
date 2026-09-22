import AppKit
import UniformTypeIdentifiers

nonisolated enum CSV {
    private static func escape(_ s: String) -> String {
        s.contains(where: { $0 == "," || $0 == "\"" || $0.isNewline }) ? "\"\(s.replacingOccurrences(of: "\"", with: "\"\""))\"" : s
    }

    private static func row(_ fields: [String]) -> String { fields.map(escape).joined(separator: ",") }
    private static func hours(_ t: TimeInterval) -> String { String(format: "%.2f", t / 3600) }

    static func entries(_ entries: [Entry]) -> String {
        let day = Date.ISO8601FormatStyle(timeZone: .current).year().month().day()
        let time = Date.FormatStyle(date: .omitted, time: .shortened)
        var lines = [row(["Date", "Activity", "Notes", "Start", "End", "Hours"])]
        for e in entries {
            lines.append(row([e.start.formatted(day), e.activity.rawValue.capitalized, e.notes.joined(separator: "; "),
                              e.start.formatted(time), e.end?.formatted(time) ?? "", hours(e.duration)]))
        }
        return lines.joined(separator: "\n") + "\n"
    }

    /// Every day of `month` that has entries or owes hours, then a total row that matches the app's month balance.
    static func daily(_ entries: [Entry], month: Date, target: Target, now: Date = .now) -> String {
        let day = Date.ISO8601FormatStyle(timeZone: .current).year().month().day()
        let byDay = Dictionary(grouping: entries) { Calendar.current.startOfDay(for: $0.start) }
        var lines = [row(["Date", "Work", "Break", "Lunch", "Extra", "Travel", "Out of Office", "Balance", "Notes"])]
        var sums: [Activity: TimeInterval] = [:], total = 0.0
        for date in month.daysInMonth {
            let es = byDay[date] ?? []
            guard !es.isEmpty || target.owed(on: date, now: now) > 0 else { continue }
            let t = Store.totals(es)
            let balance = target.balance(worked: t.worked(excluding: target.excluded), on: date, now: now)
            sums.merge(t, uniquingKeysWith: +)
            total += balance
            lines.append(row([date.formatted(day)] + Activity.allCases.map { hours(t[$0, default: 0]) }
                             + [String(format: "%+.2f", balance / 3600), es.flatMap(\.notes).joined(separator: "; ")]))
        }
        lines.append(row(["Total"] + Activity.allCases.map { hours(sums[$0, default: 0]) } + [String(format: "%+.2f", total / 3600), ""]))
        return lines.joined(separator: "\n") + "\n"
    }
}

func saveCSV(_ text: String, suggestedName: String) {
    let panel = NSSavePanel()
    panel.allowedContentTypes = [.commaSeparatedText]
    panel.nameFieldStringValue = suggestedName
    NSApp.activate()
    panel.begin { response in
        guard response == .OK, let url = panel.url else { return }
        try? text.write(to: url, atomically: true, encoding: .utf8)
    }
}
