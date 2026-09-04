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
        var lines = [row(["Date", "Activity", "Tag", "Start", "End", "Hours"])]
        for e in entries {
            lines.append(row([e.start.formatted(day), e.activity.label, e.tag,
                              e.start.formatted(time), e.end?.formatted(time) ?? "", hours(e.duration)]))
        }
        return lines.joined(separator: "\n") + "\n"
    }

    static func daily(_ entries: [Entry], targetHours: Double) -> String {
        let day = Date.ISO8601FormatStyle(timeZone: .current).year().month().day()
        let byDay = Dictionary(grouping: entries) { Calendar.current.startOfDay(for: $0.start) }
        var lines = [row(["Date", "Work", "Break", "Lunch", "Extra", "Balance", "Tags"])]
        for (date, es) in byDay.sorted(by: { $0.key < $1.key }) {
            let t = Store.totals(es)
            let work = t[.work, default: 0], extra = t[.extra, default: 0]
            let balance = (work + extra) / 3600 - targetHours
            let tags = Set(es.map(\.tag).filter { !$0.isEmpty }).sorted().joined(separator: "; ")
            lines.append(row([date.formatted(day), hours(work), hours(t[.break, default: 0]),
                              hours(t[.lunch, default: 0]), hours(extra), String(format: "%+.2f", balance), tags]))
        }
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
