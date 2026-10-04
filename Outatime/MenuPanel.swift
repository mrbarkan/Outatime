import SwiftUI

struct MenuPanel: View {
    @Environment(Store.self) private var store
    @Environment(\.openWindow) private var openWindow
    @Environment(\.dismiss) private var dismiss
    @Environment(\.openSettings) private var openSettings
    @AppStorage("targetHours") private var targetHours = 8.0
    @AppStorage("excludedFromTarget") private var excluded = Activity.defaultExcluded
    @AppStorage("bankSince") private var bankSince = 0.0
    @State private var note = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            status
            TextField(store.running != nil ? "What are you working on?" : "Add a note to the last entry", text: $note)
                .textFieldStyle(.roundedBorder)
                .disabled(store.noteTarget == nil)
                .onSubmit { store.addNote(note); note = "" }
            if let notes = store.noteTarget?.notes, !notes.isEmpty {
                VStack(alignment: .leading, spacing: 2) {
                    ForEach(Array(notes.enumerated()), id: \.offset) { Text("· \($0.element)") }
                }
                .font(.caption).foregroundStyle(.secondary).lineLimit(1)
            }

            GlassEffectContainer(spacing: 10) {
                Grid(horizontalSpacing: 10, verticalSpacing: 10) {
                    GridRow { ActivityButton(.work); ActivityButton(.break) }
                    GridRow { ActivityButton(.lunch); ActivityButton(.extra) }
                    GridRow { ActivityButton(.travel); ActivityButton(.outOfOffice) }
                }
            }

            totals
            Divider()
            footer
        }
        .padding(14)
        .frame(width: 300)
    }

    private var status: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                if let running = store.running {
                    Image(systemName: running.activity.symbol).foregroundStyle(running.activity.color)
                    Text(running.activity.label).fontWeight(.semibold)
                    Spacer()
                    Text(store.runningSince ?? running.start, style: .timer).monospacedDigit().foregroundStyle(.secondary)
                } else {
                    Image(systemName: "clock").foregroundStyle(.secondary)
                    Text("Not tracking").foregroundStyle(.secondary)
                    Spacer()
                }
                Button("Tomato timer", systemImage: "timer", action: store.toggleTomato)
                    .labelStyle(.iconOnly).buttonStyle(.borderless)
                    .foregroundStyle(store.tomatoSince != nil ? AnyShapeStyle(.red) : AnyShapeStyle(.secondary))
                    .help("Tomato timer")
            }
            .font(.title3)
            if store.tomatoSince != nil { TomatoLine(round: store.tomatoRound) }
            // Left running overnight: it was cut at midnight, and the Logbook shows where.
            if let since = store.runningSince, !Calendar.current.isDateInToday(since) {
                Label("Running since \(since, format: .dateTime.weekday().hour().minute())", systemImage: "exclamationmark.triangle.fill")
                    .font(.caption).foregroundStyle(.orange)
            }
        }
    }

    private var totals: some View {
        let t = store.totals(on: .now)
        let target = store.target(hours: targetHours, excluded: excluded)
        let cal = Calendar.current
        let week = store.balance(cal.dateInterval(of: .weekOfYear, for: .now)!, target)
        let month = store.balance(cal.dateInterval(of: .month, for: .now)!, target)
        let since = bankSince > 0 ? Date(timeIntervalSinceReferenceDate: bankSince) : target.since
        let bank = store.balance(DateInterval(start: cal.startOfDay(for: since), end: .now), target)
        return VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 12) {
                ForEach(Activity.allCases.filter { $0 == .work || t[$0, default: 0] > 0 }) { a in
                    HStack(spacing: 4) {
                        Image(systemName: a.symbol).foregroundStyle(a.color)
                        Text(t[a, default: 0].hm).monospacedDigit()
                    }
                }
            }
            .font(.caption)
            Grid(alignment: .trailing, horizontalSpacing: 12, verticalSpacing: 4) {
                // Today is still in progress: a shortfall isn't alarming yet.
                let worked = t.worked(excluding: excluded)
                row("Today", worked: worked, balance: worked - target.owed(on: .now), shortfall: .secondary)
                row("This Week", worked: week.worked, balance: week.balance, shortfall: .red)
                row("This Month", worked: month.worked, balance: month.balance, shortfall: .red)
                row("Since \(since, format: .dateTime.day().month(.abbreviated))", worked: bank.worked, balance: bank.balance, shortfall: .red)
            }
            .font(.caption).monospacedDigit()
        }
    }

    private func row(_ label: LocalizedStringKey, worked: TimeInterval, balance: TimeInterval, shortfall: Color) -> some View {
        GridRow {
            Text(label).foregroundStyle(.secondary).gridColumnAlignment(.leading)
            Spacer()
            Text(worked.hm)
            Text((balance >= 0 ? "+" : "−") + abs(balance).hm).foregroundStyle(balance >= 0 ? .green : shortfall)
        }
    }

    private var footer: some View {
        HStack(spacing: 2) {
            Button("Logbook", systemImage: "calendar", action: showLogbook)
            Menu("Export", systemImage: "square.and.arrow.up") {
                let month = Date.now.startOfMonth
                let target = store.target(hours: targetHours, excluded: excluded)
                Button("This Month — Report (Excel)…") {
                    save(Report.month(store.entries(inMonth: month), month: month, target: target), as: .xlsx, suggestedName: exportName(month, ".xlsx"))
                }
                Button("Master Workbook (Excel)…") {
                    save(Report.master(store.entries, target: target), as: .xlsx, suggestedName: "Outatime Master.xlsx")
                }
                Divider()
                Button("This Month — Daily Summary…") {
                    save(Data(CSV.daily(store.entries(inMonth: month), month: month, target: target).utf8), as: .commaSeparatedText, suggestedName: exportName(month, " daily.csv"))
                }
                Button("This Month — Entries…") {
                    save(Data(CSV.entries(store.entries(inMonth: month)).utf8), as: .commaSeparatedText, suggestedName: exportName(month, " entries.csv"))
                }
            }
            Spacer()
            Menu {
                Button("About", action: showAbout)
                Button("What's New") {
                    dismiss()
                    WhatsNew.showing = Array(WhatsNew.releases.prefix(1))
                    show(window: "whats-new", openWindow)
                }
                Button("Settings…") {
                    dismiss()
                    NSApp.activate()
                    openSettings()
                }
                .keyboardShortcut(",")
                Divider()
                Button("Quit") { NSApp.terminate(nil) }.keyboardShortcut("q")
            } label: {
                Image(systemName: "ellipsis.circle")
            }
            .menuIndicator(.hidden)
        }
        .buttonStyle(.accessoryBar)
        .menuStyle(.button)
        .fixedSize(horizontal: false, vertical: true)
    }

    private func showLogbook() {
        dismiss()
        show(window: "editor", openWindow)
    }
}

/// "Round 3 · 12:04 left" under the status while the tomato is on.
private struct TomatoLine: View {
    let round: Pomodoro.Round?

    var body: some View {
        TimelineView(.periodic(from: .now, by: 1)) { context in
            HStack {
                if let round {
                    let left = round.end.timeIntervalSince(context.date)
                    let clock = Duration.seconds(Int(abs(left))).formatted(.time(pattern: .minuteSecond))
                    switch round.phase {
                    case .focus: Text("Round \(round.number)")
                    case .shortBreak: Text("Break")
                    case .longBreak: Text("Long break")
                    }
                    Spacer()
                    Text(left > 0 ? "\(clock) left" : "\(clock) over").monospacedDigit()
                } else {
                    Text("Paused")  // tracking something other than Work or Break
                }
            }
        }
        .font(.caption).foregroundStyle(.secondary)
    }
}

private struct ActivityButton: View {
    @Environment(Store.self) private var store
    let activity: Activity
    init(_ activity: Activity) { self.activity = activity }

    var body: some View {
        let active = store.running?.activity == activity
        Button { active ? store.stop() : store.start(activity) } label: {
            VStack(spacing: 4) {
                Image(systemName: activity.symbol).font(.title2)
                    .foregroundStyle(active ? AnyShapeStyle(.primary) : AnyShapeStyle(activity.color))
                Text(activity.label).font(.callout)
            }
            .frame(maxWidth: .infinity, minHeight: 56)
        }
        .modifier(GlassStyle(prominent: active, color: activity.color))
    }
}

private struct GlassStyle: ViewModifier {
    let prominent: Bool
    let color: Color
    func body(content: Content) -> some View {
        if prominent { content.buttonStyle(.glassProminent).tint(color) } else { content.buttonStyle(.glass) }
    }
}
