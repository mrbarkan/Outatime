import SwiftUI

/// The Logbook's Stats popover.
struct StatsView: View {
    let stats: Stats

    var body: some View {
        Form {
            Section("This Week") {
                LabeledContent("Worked", value: stats.weekWorked.hm)
                LabeledContent("Left this week") {
                    stats.weekLeft > 0 ? Text("\(stats.weekLeft.hm) of \(stats.weekGoal.hm)") : Text("Done").foregroundStyle(.green)
                }
                LabeledContent("To next day off", value: stats.toDayOff.hm)
                if stats.daysOffBanked > 0 { LabeledContent("Days off banked", value: "\(stats.daysOffBanked)") }
            }
            Section("Averages") {
                LabeledContent("Average week") { hours(stats.averageWeek) }
                LabeledContent("Average month") { hours(stats.averageMonth) }
                LabeledContent("Average workday") { hours(stats.averageDay) }
                LabeledContent("Usual start") { time(stats.usualStart) }
                LabeledContent("Usual finish") { time(stats.usualFinish) }
            }
            Section("This Month") {
                LabeledContent("Longest day") {
                    if let d = stats.longestDay {
                        Text("\(d.worked.hm), \(d.date.formatted(.dateTime.weekday(.abbreviated).day()))")
                    } else { none }
                }
                LabeledContent("Extra", value: stats.extraThisMonth.hm)
                LabeledContent("Days worked", value: "\(stats.daysWorkedThisMonth)")
            }
        }
        .formStyle(.grouped)
        .monospacedDigit()
        .frame(width: 320)
        .fixedSize(horizontal: false, vertical: true)
    }

    /// Averages need a complete week or month first.
    private var none: some View { Text(verbatim: "—").foregroundStyle(.secondary) }

    @ViewBuilder private func hours(_ t: TimeInterval?) -> some View {
        if let t { Text(t.hm) } else { none }
    }

    @ViewBuilder private func time(_ minutes: Int?) -> some View {
        if let m = minutes, let d = Calendar.current.date(bySettingHour: m / 60, minute: m % 60, second: 0, of: .now) {
            Text(d, format: .dateTime.hour().minute())
        } else { none }
    }
}
