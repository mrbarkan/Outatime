import Foundation

/// The numbers behind the menu's week and day-off lines and the Logbook's Stats. Hours are those that count toward
/// the target.
nonisolated struct Stats {
    /// This week: its goal (each workday's target, days off excepted) and what's done so far.
    var weekGoal: TimeInterval = 0, weekWorked: TimeInterval = 0
    /// The hours bank, and one day's target: banking that much buys a day off.
    var bank: TimeInterval = 0, dayTarget: TimeInterval = 0
    /// Over complete weeks and months since tracking began; nil until there is one.
    var averageWeek: TimeInterval?, averageMonth: TimeInterval?
    /// Per day worked.
    var averageDay: TimeInterval?
    /// Minutes after midnight, the median over days worked; today's finish isn't in yet.
    var usualStart: Int?, usualFinish: Int?
    var longestDay: (date: Date, worked: TimeInterval)?
    var extraThisMonth: TimeInterval = 0
    var daysWorkedThisMonth = 0

    /// Negative once the week's goal is passed.
    var weekLeft: TimeInterval { weekGoal - weekWorked }
    var daysOffBanked: Int { dayTarget > 0 ? max(0, Int(bank / dayTarget)) : 0 }
    /// What's still to bank for the next day off.
    var toDayOff: TimeInterval { dayTarget > 0 ? dayTarget - (bank - Double(daysOffBanked) * dayTarget) : 0 }

    init(_ entries: [Entry], target: Target, bankSince: Date, now: Date = .now) {
        let cal = Calendar.current
        let byDay = Dictionary(grouping: entries) { cal.startOfDay(for: $0.start) }
        let worked = byDay.mapValues { Store.totals($0).worked(excluding: target.excluded) }.filter { $0.value > 0 }
        let sum = { (i: DateInterval) in worked.filter { i.contains($0.key) && $0.key < i.end }.values.reduce(0, +) }

        let week = cal.dateInterval(of: .weekOfYear, for: now)!
        weekWorked = sum(week)
        weekGoal = days(in: week).map(target.scheduled).reduce(0, +)
        dayTarget = target.seconds
        bank = Store.balance(entries, in: DateInterval(start: cal.startOfDay(for: bankSince), end: now), target: target, now: now).balance

        // Complete periods only: from the first one that starts on or after tracking began, to the one before now.
        func average(_ unit: Calendar.Component) -> TimeInterval? {
            var periods: [DateInterval] = []
            var p = cal.dateInterval(of: unit, for: target.since)!
            if p.start < cal.startOfDay(for: target.since) { p = cal.dateInterval(of: unit, for: p.end)! }
            while p.end <= cal.dateInterval(of: unit, for: now)!.start {
                periods.append(p)
                p = cal.dateInterval(of: unit, for: p.end)!
            }
            return periods.isEmpty ? nil : periods.map(sum).reduce(0, +) / Double(periods.count)
        }
        averageWeek = average(.weekOfYear)
        averageMonth = average(.month)
        averageDay = worked.isEmpty ? nil : worked.values.reduce(0, +) / Double(worked.count)

        func median(_ xs: [Int]) -> Int? {
            let s = xs.sorted()
            return s.isEmpty ? nil : s.count % 2 == 1 ? s[s.count / 2] : (s[s.count / 2 - 1] + s[s.count / 2]) / 2
        }
        let minute = { (d: Date) in Int(d.timeIntervalSince(cal.startOfDay(for: d)) / 60) }
        let workedDays = worked.keys.map { day in (day: day, entries: byDay[day]!.sorted { $0.start < $1.start }) }
        usualStart = median(workedDays.compactMap { $0.entries.first.map { minute($0.start) } })
        usualFinish = median(workedDays.filter { !cal.isDate($0.day, inSameDayAs: now) }.compactMap { $0.entries.last?.end.map(minute) })

        let month = cal.dateInterval(of: .month, for: now)!
        let monthDays = worked.filter { month.contains($0.key) && $0.key < month.end }
        longestDay = monthDays.max { $0.value < $1.value || ($0.value == $1.value && $0.key > $1.key) }.map { ($0.key, $0.value) }
        extraThisMonth = entries.filter { $0.activity == .extra && month.contains($0.start) }.map(\.duration).reduce(0, +)
        daysWorkedThisMonth = monthDays.count
    }

    private func days(in interval: DateInterval) -> [Date] {
        let cal = Calendar.current
        return Array(sequence(first: cal.startOfDay(for: interval.start)) { cal.date(byAdding: .day, value: 1, to: $0) }.prefix { $0 < interval.end })
    }
}
