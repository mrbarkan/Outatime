import SwiftUI

/// What a Log form records: time off and holidays, Travel and Out of Office blocks, or hours carried in.
enum LogKind: String, CaseIterable, Identifiable {
    case timeOff, holiday, travel, outOfOffice, carried
    var id: String { rawValue }

    var label: LocalizedStringKey {
        switch self {
        case .timeOff: "Time Off"
        case .holiday: "Holiday"
        case .travel: "Travel"
        case .outOfOffice: "Out of Office"
        case .carried: "Carried Hours"
        }
    }

    var symbol: String {
        switch self {
        case .timeOff: "clock.arrow.circlepath"
        case .holiday: "beach.umbrella"
        case .travel: Activity.travel.symbol
        case .outOfOffice: Activity.outOfOffice.symbol
        case .carried: "tray.and.arrow.down"
        }
    }

    var color: Color {
        switch self {
        case .timeOff: .cyan
        case .holiday: .secondary
        case .travel: Activity.travel.color
        case .outOfOffice: Activity.outOfOffice.color
        case .carried: .green
        }
    }
}

/// A Log form's subject: something new, or a bank record being edited.
enum LogItem: Identifiable, Hashable {
    case new(LogKind)
    case timeOff(TimeOff)
    case holiday(ClosedRange<Date>)
    case carried(Carried)

    var id: String {
        switch self {
        case .new(let k): "new-\(k.rawValue)"
        case .timeOff(let t): t.id.uuidString
        case .holiday(let r): "holiday-\(r.lowerBound.dayKey)"
        case .carried(let c): c.id.uuidString
        }
    }

    var kind: LogKind {
        switch self {
        case .new(let k): k
        case .timeOff: .timeOff
        case .holiday: .holiday
        case .carried: .carried
        }
    }
}

/// The settings the hours bank depends on, read the same way by the menu, the Logbook, Objectives and the Log forms.
struct BankContext: DynamicProperty {
    @Environment(Store.self) var store
    @AppStorage("targetHours") var targetHours = 8.0
    @AppStorage("excludedFromTarget") private var excluded = Activity.defaultExcluded
    @AppStorage("bankSince") private var bankSince = 0.0

    var target: Target { store.target(hours: targetHours, excluded: excluded) }
    /// Where the bank starts: the day picked in Settings → Target, or when tracking began.
    var since: Date { bankSince > 0 ? Date(timeIntervalSinceReferenceDate: bankSince) : target.since }
    var bank: HoursBank { store.bank(target, since: since) }
}

/// The Logbook's Hours Bank sheet: the balance, and every deposit and withdrawal that isn't a day's work.
struct HoursBankView: View {
    @Environment(\.dismiss) private var dismiss
    @Environment(\.locale) private var locale
    private var context = BankContext()
    @State private var editing: LogItem?

    var body: some View {
        let bank = context.bank
        VStack(spacing: 0) {
            VStack(spacing: 4) {
                Text(verbatim: signed(bank.balance))
                    .font(.system(size: 34, weight: .semibold)).monospacedDigit()
                    .foregroundStyle(bank.balance >= 0 ? .green : .red)
                caption(bank)
            }
            .padding(.top, 20).padding(.bottom, 4)
            Form {
                let rows = self.rows
                if rows.isEmpty {
                    Text("Nothing logged yet. Log time off, holidays, or overtime carried in from before.").foregroundStyle(.secondary)
                }
                ForEach(rows) { row($0) }
            }
            .formStyle(.grouped)
            HStack {
                Menu("Log", systemImage: "plus") {
                    ForEach(LogKind.allCases) { k in Button(k.label, systemImage: k.symbol) { editing = .new(k) } }
                }
                .fixedSize()
                Spacer()
                Button("Done") { dismiss() }.keyboardShortcut(.defaultAction)
            }
            .padding(14)
        }
        .frame(width: 460, height: 540)
        .sheet(item: $editing) { LogForm(item: $0) }
    }

    /// "2.5 days off · 24h 00m booked ahead", each part only when there's something to say.
    private func caption(_ bank: HoursBank) -> some View {
        let day = context.target.seconds
        return HStack(spacing: 6) {
            if day > 0, bank.balance > 0 {
                Text("\((bank.balance / day).formatted(.number.precision(.fractionLength(0...1)).locale(locale))) days off")
                if bank.bookedAhead > 0 { Text(verbatim: "·") }
            }
            if bank.bookedAhead > 0 { Text("\(bank.bookedAhead.hm) booked ahead") }
        }
        .font(.callout).foregroundStyle(.secondary).monospacedDigit()
    }

    private struct Row: Identifiable {
        var item: LogItem
        var start: Date, end: Date
        var hours: TimeInterval?  // signed as it moves the bank; a holiday doesn't
        var note: String
        var id: String { item.id }
    }

    /// Time off, carried hours and holidays, newest first.
    private var rows: [Row] {
        let store = context.store, target = context.target
        let off = store.timeOff.compactMap { t -> Row? in
            guard let s = Date(dayKey: t.start), let e = Date(dayKey: t.end) else { return nil }
            return Row(item: .timeOff(t), start: s, end: e, hours: -target.cost(of: t), note: t.note)
        }
        let carried = store.carried.compactMap { c -> Row? in
            Date(dayKey: c.date).map { Row(item: .carried(c), start: $0, end: $0, hours: c.hours * 3600, note: c.note) }
        }
        let holidays = store.holidays.map { Row(item: .holiday($0), start: $0.lowerBound, end: $0.upperBound, hours: nil, note: "") }
        return (off + carried + holidays).sorted { $0.start > $1.start }
    }

    private func row(_ r: Row) -> some View {
        HStack(spacing: 10) {
            Image(systemName: r.item.kind.symbol).foregroundStyle(r.item.kind.color).frame(width: 20)
            VStack(alignment: .leading, spacing: 2) {
                Text(verbatim: span(r.start, r.end))
                Group { if r.note.isEmpty { Text(r.item.kind.label) } else { Text(verbatim: r.note) } }
                    .font(.caption).foregroundStyle(.secondary).lineLimit(1)
            }
            Spacer()
            Text(verbatim: r.hours.map(signed) ?? "—").monospacedDigit()
                .foregroundStyle(r.hours.map { $0 > 0 ? Color.green : Color.primary } ?? Color.secondary)
            Button("Delete", systemImage: "trash", role: .destructive) { delete(r.item) }
                .labelStyle(.iconOnly).buttonStyle(.borderless).help("Delete")
        }
        .contentShape(.rect)
        .onTapGesture { editing = r.item }
    }

    /// "24 Dec – 2 Jan", or one day; the year only when it isn't this one.
    private func span(_ start: Date, _ end: Date) -> String {
        let cal = Calendar.current
        let text = { (d: Date) in
            cal.isDate(d, equalTo: .now, toGranularity: .year)
                ? d.formatted(.dateTime.day().month(.abbreviated).locale(locale))
                : d.formatted(.dateTime.day().month(.abbreviated).year().locale(locale))
        }
        return cal.isDate(start, inSameDayAs: end) ? text(start) : "\(text(start)) – \(text(end))"
    }

    private func delete(_ item: LogItem) {
        let store = context.store
        switch item {
        case .timeOff(let t): store.timeOff.removeAll { $0.id == t.id }
        case .carried(let c): store.carried.removeAll { $0.id == c.id }
        case .holiday(let r): store.removeHoliday(r)
        case .new: break
        }
    }
}

/// One Log form: time off, a holiday, Travel or Out of Office blocks, or carried hours; new, or a record being edited.
struct LogForm: View {
    @Environment(\.dismiss) private var dismiss
    private var context = BankContext()
    let item: LogItem
    @State private var from: Date
    @State private var to: Date
    @State private var hours: Double?
    @State private var note: String
    @State private var client: Profile.ID?

    init(item: LogItem) {
        self.item = item
        let today = Calendar.current.startOfDay(for: .now)
        var from = today, to = today, hours: Double?, note = ""
        switch item {
        case .new: break
        case .timeOff(let t): from = Date(dayKey: t.start) ?? today; to = Date(dayKey: t.end) ?? from; hours = t.hours; note = t.note
        case .holiday(let r): from = r.lowerBound; to = r.upperBound
        case .carried(let c): from = Date(dayKey: c.date) ?? today; to = from; hours = c.hours; note = c.note
        }
        _from = State(initialValue: from); _to = State(initialValue: to)
        _hours = State(initialValue: hours); _note = State(initialValue: note)
        _client = State(initialValue: nil)
    }

    var body: some View {
        let kind = item.kind
        VStack(spacing: 0) {
            Form {
                Section {
                    if kind == .carried {
                        DatePicker("Date", selection: $from, displayedComponents: .date)
                    } else {
                        DatePicker("From", selection: $from, displayedComponents: .date)
                        DatePicker("To", selection: $to, in: from..., displayedComponents: .date)
                    }
                    switch kind {
                    case .timeOff where oneDay:
                        TextField("Hours", value: $hours, format: .number, prompt: Text("Full day"))
                    case .travel, .outOfOffice:
                        TextField("Hours per day", value: $hours, format: .number, prompt: Text(verbatim: context.targetHours.formatted()))
                    case .carried:
                        TextField("Hours", value: $hours, format: .number, prompt: Text(verbatim: "32"))
                    default:
                        EmptyView()
                    }
                    if kind == .travel, !context.store.activeProfiles.isEmpty {
                        Picker("Client", selection: $client) {
                            Text("No client").tag(Profile.ID?.none)
                            ForEach(context.store.activeProfiles) { Text($0.name).tag(Optional($0.id)) }
                        }
                    }
                    if kind == .timeOff || kind == .carried { TextField("Note", text: $note) }
                } header: {
                    Label(kind.label, systemImage: kind.symbol)
                } footer: {
                    footer(kind).font(.caption).foregroundStyle(.secondary)
                }
            }
            // Sized to its rows, like the Settings tabs.
            .formStyle(.grouped).scrollDisabled(true).fixedSize(horizontal: false, vertical: true)
            HStack {
                Spacer()
                Button("Cancel") { dismiss() }.keyboardShortcut(.cancelAction)
                Button("Save") { save(); dismiss() }.keyboardShortcut(.defaultAction).disabled(!valid)
            }
            .padding(14)
        }
        .frame(width: 400)
        .onChange(of: from) { if to < from { to = from } }
        .onAppear { if kind == .travel { client = context.store.currentProfile } }
    }

    private func footer(_ kind: LogKind) -> Text {
        switch kind {
        case .timeOff: Text("Each workday costs a day's target, or the hours you enter. Weekends and holidays cost nothing. It comes off the bank as soon as you save.")
        case .holiday: Text("Holidays owe nothing and leave the bank alone.")
        case .travel: Text("Adds a Travel block on each workday, after the day's last block or at your usual start.")
        case .outOfOffice: Text("Adds an Out of Office block on each workday, after the day's last block or at your usual start.")
        case .carried: Text("Overtime from before you tracked it here, or a negative amount for hours paid out.")
        }
    }

    private var oneDay: Bool { Calendar.current.isDate(from, inSameDayAs: to) }

    private var valid: Bool {
        switch item.kind {
        case .carried: (hours ?? 0) != 0
        case .holiday: true
        case .timeOff, .travel, .outOfOffice: hours.map { $0 > 0 } ?? true
        }
    }

    /// The record being edited, if any.
    private var editingID: UUID? {
        switch item {
        case .timeOff(let t): t.id
        case .carried(let c): c.id
        default: nil
        }
    }

    private func save() {
        let store = context.store
        switch item.kind {
        case .timeOff:
            let record = TimeOff(id: editingID ?? UUID(), start: from.dayKey, end: to.dayKey, hours: oneDay ? hours : nil, note: note)
            if let i = store.timeOff.firstIndex(where: { $0.id == record.id }) { store.timeOff[i] = record } else { store.timeOff.append(record) }
        case .holiday:
            if case .holiday(let old) = item { store.setHoliday(from: from, to: to, replacing: old) } else { store.setHoliday(from: from, to: to) }
        case .travel, .outOfOffice:
            let target = context.target
            store.log(item.kind == .travel ? .travel : .outOfOffice, from: from, to: to, hours: hours ?? context.targetHours,
                      profile: client, target: target, usualStart: Stats(store.entries, target: target, bankSince: context.since).usualStart)
        case .carried:
            let record = Carried(id: editingID ?? UUID(), date: from.dayKey, hours: hours ?? 0, note: note)
            if let i = store.carried.firstIndex(where: { $0.id == record.id }) { store.carried[i] = record } else { store.carried.append(record) }
        }
    }
}
