import SwiftUI

nonisolated enum Activity: String, Codable, CaseIterable, Identifiable {
    case work, `break`, lunch, extra

    var id: String { rawValue }
    var label: LocalizedStringKey {
        switch self {
        case .work: "Work"
        case .break: "Break"
        case .lunch: "Lunch"
        case .extra: "Extra"
        }
    }

    var symbol: String {
        switch self {
        case .work: "laptopcomputer"
        case .break: "cup.and.saucer"
        case .lunch: "fork.knife"
        case .extra: "moon.stars"
        }
    }

    var color: Color {
        switch self {
        case .work: .blue
        case .break: .green
        case .lunch: .orange
        case .extra: .purple
        }
    }
}

nonisolated struct Entry: Codable, Identifiable, Hashable {
    var id = UUID()
    var activity: Activity
    var start: Date
    var end: Date?
    var tag = ""

    var isRunning: Bool { end == nil }
    // ponytail: end < start (crossed midnight while editing) clamps to 0 rather than splitting the entry.
    var duration: TimeInterval { max(0, (end ?? .now).timeIntervalSince(start)) }
}

nonisolated struct DayTemplate: Codable, Identifiable, Hashable {
    struct Slot: Codable, Hashable {
        var activity: Activity
        var startMinute: Int
        var endMinute: Int
        var tag: String
    }

    var id = UUID()
    var name: String
    var slots: [Slot]

    init(name: String, entries: [Entry], calendar: Calendar = .current) {
        self.name = name
        slots = entries.map { e in
            let day = calendar.startOfDay(for: e.start)
            let minute = { (d: Date) in Int(d.timeIntervalSince(day) / 60) }
            return Slot(activity: e.activity, startMinute: minute(e.start), endMinute: minute(e.end ?? .now), tag: e.tag)
        }
    }

    func entries(on day: Date, calendar: Calendar = .current) -> [Entry] {
        let base = calendar.startOfDay(for: day)
        return slots.map { s in
            Entry(activity: s.activity,
                  start: base.addingTimeInterval(TimeInterval(s.startMinute * 60)),
                  end: base.addingTimeInterval(TimeInterval(s.endMinute * 60)),
                  tag: s.tag)
        }
    }
}

nonisolated extension Dictionary where Key == Activity, Value == TimeInterval {
    /// Time that counts toward the daily target: everything but lunch (coffee breaks are paid).
    var worked: TimeInterval { filter { $0.key != .lunch }.values.reduce(0, +) }
}

nonisolated extension TimeInterval {
    /// "6h 12m"
    var hm: String {
        let m = Int(self / 60)
        return "\(m / 60)h \(String(format: "%02d", m % 60))m"
    }
}

nonisolated extension Date {
    var startOfMonth: Date { Calendar.current.dateInterval(of: .month, for: self)!.start }
    var daysInMonth: [Date] {
        let cal = Calendar.current
        let first = startOfMonth
        return cal.range(of: .day, in: .month, for: first)!.compactMap { cal.date(byAdding: .day, value: $0 - 1, to: first) }
    }
}
