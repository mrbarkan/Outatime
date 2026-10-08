import SwiftUI

/// Something to work toward: days off bought with overtime, or a package of a client's hours, agreed in hours or as an
/// amount at an hourly rate. The menu counts one of them down.
nonisolated struct Objective: Codable, Identifiable, Hashable {
    enum Kind: String, Codable, CaseIterable, Identifiable {
        case daysOff, hours, money
        var id: String { rawValue }
    }

    var id = UUID()
    var kind: Kind
    var name = ""
    /// Days off, hours, or money, by kind.
    var amount: Double
    /// Money per hour; turns a money package into hours.
    var rate = 0.0
    var currency = Locale.current.currency?.identifier ?? "USD"
    var profile: Profile.ID?
    /// A package counts the client's billable time from this day on.
    var since = Calendar.current.startOfDay(for: .now)

    struct Progress {
        var done: TimeInterval, goal: TimeInterval
        var left: TimeInterval { max(0, goal - done) }
        var fraction: Double { goal > 0 ? min(1, done / goal) : 0 }
        var isDone: Bool { goal > 0 && done >= goal }
    }

    /// In hours, whatever the kind: days off are bought with the hours bank, one daily target each; a package is met by
    /// the client's billable time.
    func progress(_ entries: [Entry], bank: TimeInterval, dayTarget: TimeInterval) -> Progress {
        switch kind {
        case .daysOff:
            return Progress(done: max(0, bank), goal: amount * dayTarget)
        case .hours, .money:
            let start = Calendar.current.startOfDay(for: since)
            let done = entries.filter { $0.activity.billable && $0.profile == profile && $0.start >= start }.map(\.duration).reduce(0, +)
            let hours = kind == .hours ? amount : rate > 0 ? amount / rate : 0
            return Progress(done: done, goal: hours * 3600)
        }
    }
}

extension Objective.Kind {
    var label: LocalizedStringKey {
        switch self {
        case .daysOff: "Vacation"
        case .hours: "Hours Package"
        case .money: "Salary"
        }
    }

    var symbol: String {
        switch self {
        case .daysOff: "beach.umbrella"
        case .hours: "hourglass"
        case .money: "banknote"
        }
    }

    var color: Color {
        switch self {
        case .daysOff: .cyan
        case .hours: .indigo
        case .money: .orange
        }
    }
}

/// The settings and Store an objective's progress depends on, read the same way by the menu and the Logbook.
private struct ObjectiveContext: DynamicProperty {
    @Environment(Store.self) var store
    @AppStorage("targetHours") private var targetHours = 8.0
    @AppStorage("excludedFromTarget") private var excluded = Activity.defaultExcluded
    @AppStorage("bankSince") private var bankSince = 0.0
    @AppStorage("menuObjective") var menuObjective = ""

    /// The one the menu counts down: the one picked, or the first.
    var shown: Objective? {
        store.objectives.first { $0.id.uuidString == menuObjective } ?? store.objectives.first
    }

    func progress(_ o: Objective) -> Objective.Progress {
        let target = store.target(hours: targetHours, excluded: excluded)
        let since = bankSince > 0 ? Date(timeIntervalSinceReferenceDate: bankSince) : target.since
        let bank = o.kind == .daysOff
            ? store.balance(DateInterval(start: Calendar.current.startOfDay(for: since), end: .now), target).balance : 0
        return o.progress(store.entries, bank: bank, dayTarget: target.seconds)
    }

    func title(_ o: Objective) -> Text {
        if !o.name.isEmpty { return Text(verbatim: o.name) }
        if o.kind != .daysOff, let name = o.profile.flatMap({ store.profileNames[$0] }) { return Text(verbatim: name) }
        return Text(o.kind.label)
    }

    func money(_ value: Double, _ o: Objective) -> String {
        value.formatted(.currency(code: o.currency).precision(.fractionLength(0...2)))
    }
}

/// A ring that fills as an objective is met.
struct ProgressRing: View {
    var fraction: Double
    var color: Color
    var lineWidth = 2.5

    var body: some View {
        ZStack {
            Circle().stroke(color.opacity(0.25), lineWidth: lineWidth)
            Circle().trim(from: 0, to: fraction)
                .stroke(color, style: StrokeStyle(lineWidth: lineWidth, lineCap: .round))
                .rotationEffect(.degrees(-90))
        }
        .padding(lineWidth / 2)
    }
}

/// Next to Export in the menu: the ring and what's left of one objective. Click it for all of them.
struct MenuObjective: View {
    let edit: () -> Void
    private var context = ObjectiveContext()
    @State private var open = false

    init(edit: @escaping () -> Void) { self.edit = edit }

    var body: some View {
        if let o = context.shown {
            let p = context.progress(o)
            Button { open.toggle() } label: {
                HStack(spacing: 4) {
                    ProgressRing(fraction: p.fraction, color: p.isDone ? .green : o.kind.color).frame(width: 15, height: 15)
                    if p.isDone { Text("Done") } else { Text(verbatim: p.left.short).monospacedDigit() }
                }
            }
            .help(context.title(o))
            .popover(isPresented: $open, arrowEdge: .bottom) { list }
        }
    }

    private var list: some View {
        VStack(alignment: .leading, spacing: 12) {
            ForEach(context.store.objectives) { o in
                let p = context.progress(o)
                // Picking one puts it in the menu.
                Button { context.menuObjective = o.id.uuidString } label: {
                    VStack(alignment: .leading, spacing: 5) {
                        HStack {
                            Image(systemName: o.id == context.shown?.id ? "checkmark.circle.fill" : o.kind.symbol)
                                .foregroundStyle(o.kind.color).frame(width: 18)
                            context.title(o).fontWeight(.medium).lineLimit(1)
                            Spacer()
                            if p.isDone { Text("Done").foregroundStyle(.green) } else { Text("\(p.left.hm) left") }
                        }
                        ProgressView(value: p.fraction).tint(p.isDone ? .green : o.kind.color)
                    }
                    .contentShape(.rect)
                }
                .buttonStyle(.plain)
            }
            Divider()
            Button("Edit Objectives…") { open = false; edit() }.buttonStyle(.link)
        }
        .font(.callout).monospacedDigit()
        .padding(14)
        .frame(width: 270)
    }
}

/// The Logbook's Objectives sheet.
struct ObjectivesView: View {
    @Environment(\.dismiss) private var dismiss
    private var context = ObjectiveContext()
    private var store: Store { context.store }

    var body: some View {
        VStack(spacing: 0) {
            Form {
                if store.objectives.isEmpty {
                    Text("No objectives yet. Add one and the menu counts it down next to Export.").foregroundStyle(.secondary)
                }
                ForEach(store.objectives) { o in section(store.binding(for: o)) }
            }
            .formStyle(.grouped)
            HStack {
                Menu("Add Objective", systemImage: "plus") {
                    ForEach(Objective.Kind.allCases) { k in Button(k.label, systemImage: k.symbol) { add(k) } }
                }
                .fixedSize()
                Spacer()
                Button("Done") { dismiss() }.keyboardShortcut(.defaultAction)
            }
            .padding(14)
        }
        .frame(width: 460, height: 540)
    }

    private func section(_ o: Binding<Objective>) -> some View {
        let p = context.progress(o.wrappedValue)
        return Section {
            TextField("Name", text: o.name, prompt: context.title(Objective(kind: o.wrappedValue.kind, amount: 0, profile: o.wrappedValue.profile)))
            switch o.wrappedValue.kind {
            case .daysOff:
                Stepper(value: o.amount, in: 1...90) { Text("Days off: \(Int(o.wrappedValue.amount))") }
            case .hours:
                clientPicker(o)
                TextField("Hours", value: o.amount, format: .number)
                DatePicker("Counting since", selection: o.since, displayedComponents: .date)
            case .money:
                clientPicker(o)
                TextField("Amount", value: o.amount, format: .currency(code: o.wrappedValue.currency))
                TextField("Hourly rate", value: o.rate, format: .currency(code: o.wrappedValue.currency))
                DatePicker("Counting since", selection: o.since, displayedComponents: .date)
            }
            progressRow(o.wrappedValue, p)
        } header: {
            HStack {
                Label(o.wrappedValue.kind.label, systemImage: o.wrappedValue.kind.symbol)
                Spacer()
                let shown = context.shown?.id == o.wrappedValue.id
                Button("Show in Menu", systemImage: shown ? "menubar.arrow.up.rectangle" : "menubar.rectangle") {
                    context.menuObjective = o.wrappedValue.id.uuidString
                }
                .foregroundStyle(shown ? AnyShapeStyle(.tint) : AnyShapeStyle(.secondary))
                .help("Show in Menu")
                Button("Delete", systemImage: "trash", role: .destructive) { store.objectives.removeAll { $0.id == o.wrappedValue.id } }
                    .help("Delete")
            }
            .buttonStyle(.borderless).labelStyle(.iconOnly)
        } footer: {
            Group {
                switch o.wrappedValue.kind {
                case .daysOff: Text("Overtime in the hours bank buys days off, one daily target each. Settings → Target sets where the bank starts.")
                case .hours: Text("The client's Work, Extra and Travel from that day on count toward the package.")
                case .money: Text("The amount at your hourly rate is the hours you owe the client. Their Work, Extra and Travel from that day on count it down.")
                }
            }
            .font(.caption).foregroundStyle(.secondary)
        }
    }

    private func clientPicker(_ o: Binding<Objective>) -> some View {
        Picker("Client", selection: o.profile) {
            Text("No client").tag(Profile.ID?.none)
            // A removed client stays listed while an objective still counts its hours.
            ForEach(store.profiles.filter { !$0.archived || $0.id == o.wrappedValue.profile }) { Text($0.name).tag(Optional($0.id)) }
        }
    }

    private func progressRow(_ o: Objective, _ p: Objective.Progress) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            ProgressView(value: p.fraction).tint(p.isDone ? .green : o.kind.color)
            HStack {
                if p.goal <= 0 {
                    Text("Nothing to count yet").foregroundStyle(.secondary)
                } else if p.isDone {
                    Text("Done").foregroundStyle(.green)
                } else {
                    Text("\(p.left.hm) left of \(p.goal.hm)")
                }
                Spacer()
                switch o.kind {
                case .daysOff where p.goal > 0:
                    let days = (p.done / (p.goal / o.amount)).formatted(.number.precision(.fractionLength(0...1)))
                    HStack(spacing: 4) { Text("Days off banked"); Text(verbatim: days) }
                case .money where p.goal > 0:
                    Text("\(context.money(p.left / 3600 * o.rate, o)) left of \(context.money(o.amount, o))")
                default:
                    EmptyView()
                }
            }
            .font(.callout).foregroundStyle(.secondary).monospacedDigit()
        }
    }

    private func add(_ kind: Objective.Kind) {
        let client = kind == .daysOff ? nil : store.currentProfile ?? store.activeProfiles.first?.id
        store.objectives.append(Objective(kind: kind, amount: kind == .daysOff ? 5 : kind == .hours ? 80 : 0, profile: client))
    }
}

nonisolated extension TimeInterval {
    /// "42h" from ten hours up, "6h 12m" below that, "35m" under an hour: short enough for the menu's footer.
    var short: String {
        self >= 36000 ? "\(Int((self / 3600).rounded(.up)))h" : self >= 3600 ? hm : "\(Int((self / 60).rounded(.up)))m"
    }
}
