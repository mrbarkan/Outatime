import SwiftUI

struct EditorView: View {
    @Environment(Store.self) private var store
    @AppStorage("targetHours") private var targetHours = 8.0
    @State private var month = Date.now.startOfMonth
    @State private var selectedDay: Date? = Calendar.current.startOfDay(for: .now)

    var body: some View {
        NavigationSplitView {
            List(month.daysInMonth, id: \.self, selection: $selectedDay) { day in
                DayRow(day: day, totals: store.totals(on: day))
            }
            .navigationSplitViewColumnWidth(min: 230, ideal: 260)
            .navigationTitle(month.formatted(.dateTime.month(.wide).year()))
            .toolbar {
                ToolbarItemGroup {
                    Button("Previous Month", systemImage: "chevron.left") { shift(-1) }
                    Button("Next Month", systemImage: "chevron.right") { shift(1) }
                }
            }
        } detail: {
            if let day = selectedDay {
                DayEditor(day: day)
            } else {
                ContentUnavailableView("Select a day", systemImage: "calendar")
            }
        }
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Menu("Export", systemImage: "square.and.arrow.up") {
                    let name = month.formatted(.dateTime.year().month(.twoDigits))
                    Button("Daily Summary CSV…") {
                        saveCSV(CSV.daily(store.entries(inMonth: month), targetHours: targetHours), suggestedName: "Outatime \(name) daily.csv")
                    }
                    Button("Entries CSV…") {
                        saveCSV(CSV.entries(store.entries(inMonth: month)), suggestedName: "Outatime \(name) entries.csv")
                    }
                }
            }
        }
    }

    private func shift(_ months: Int) {
        month = Calendar.current.date(byAdding: .month, value: months, to: month)!.startOfMonth
        selectedDay = month
    }
}

private struct DayRow: View {
    let day: Date
    let totals: [Activity: TimeInterval]

    var body: some View {
        HStack {
            Text(day, format: .dateTime.weekday(.abbreviated).day())
                .foregroundStyle(Calendar.current.isDateInWeekend(day) ? .secondary : .primary)
            Spacer()
            HStack(spacing: 8) {
                ForEach(Activity.allCases.filter { totals[$0, default: 0] > 0 }) { a in
                    Label(totals[a]!.hm, systemImage: a.symbol).foregroundStyle(a.color)
                }
            }
            .font(.caption).monospacedDigit()
        }
    }
}

struct DayEditor: View {
    @Environment(Store.self) private var store
    @AppStorage("targetHours") private var targetHours = 8.0
    let day: Date
    @State private var naming = false
    @State private var templateName = ""
    @State private var pendingTemplate: DayTemplate?

    var body: some View {
        @Bindable var store = store
        let dayEntries = store.entries(on: day)
        List {
            ForEach(dayEntries) { entry in
                if let i = store.entries.firstIndex(where: { $0.id == entry.id }) {
                    EntryRow(entry: $store.entries[i]) { store.delete(entry.id) }
                }
            }
            if dayEntries.isEmpty {
                Text("No entries. Add one or apply a template.").foregroundStyle(.secondary)
            }
        }
        .safeAreaInset(edge: .bottom) { summary(dayEntries) }
        .navigationTitle(day.formatted(date: .complete, time: .omitted))
        .toolbar {
            ToolbarItemGroup {
                Button("Add Entry", systemImage: "plus") { store.addEntry(on: day) }
                Menu("Templates", systemImage: "doc.on.doc") {
                    Button("Save Day as Template…") { templateName = ""; naming = true }
                        .disabled(dayEntries.isEmpty)
                    if !store.templates.isEmpty { Divider() }
                    ForEach(store.templates) { t in
                        Menu(t.name) {
                            Button("Apply to This Day") {
                                if dayEntries.isEmpty { store.apply(t, to: day) } else { pendingTemplate = t }
                            }
                            Button("Delete Template", role: .destructive) { store.templates.removeAll { $0.id == t.id } }
                        }
                    }
                }
            }
        }
        .alert("Save Day as Template", isPresented: $naming) {
            TextField("Name", text: $templateName)
            Button("Save") { store.templates.append(DayTemplate(name: templateName, entries: dayEntries)) }
                .disabled(templateName.isEmpty)
            Button("Cancel", role: .cancel) {}
        }
        .confirmationDialog("Replace this day's entries with “\(pendingTemplate?.name ?? "")”?",
                            isPresented: Binding(get: { pendingTemplate != nil }, set: { if !$0 { pendingTemplate = nil } })) {
            Button("Replace", role: .destructive) { if let t = pendingTemplate { store.apply(t, to: day) } }
        }
    }

    private func summary(_ entries: [Entry]) -> some View {
        let t = Store.totals(entries)
        let balance = t[.work, default: 0] + t[.extra, default: 0] - targetHours * 3600
        return HStack(spacing: 14) {
            ForEach(Activity.allCases) { a in
                Label(t[a, default: 0].hm, systemImage: a.symbol).foregroundStyle(a.color)
            }
            Spacer()
            Text("Balance " + (balance >= 0 ? "+" : "−") + abs(balance).hm).fontWeight(.medium)
            Stepper("Target \(targetHours.formatted())h", value: $targetHours, in: 0...16, step: 0.5)
                .controlSize(.small)
        }
        .font(.callout).monospacedDigit()
        .padding(.horizontal, 16).padding(.vertical, 10)
        .glassEffect(.regular, in: .rect)
    }
}

private struct EntryRow: View {
    @Binding var entry: Entry
    let onDelete: () -> Void

    var body: some View {
        HStack(spacing: 10) {
            Picker("Activity", selection: $entry.activity) {
                ForEach(Activity.allCases) { Label($0.label, systemImage: $0.symbol).tag($0) }
            }
            .labelsHidden().frame(width: 110)
            DatePicker("Start", selection: $entry.start, displayedComponents: .hourAndMinute).labelsHidden()
            Text("–").foregroundStyle(.secondary)
            if entry.isRunning {
                Text("running").foregroundStyle(.secondary)
            } else {
                DatePicker("End", selection: Binding(get: { entry.end ?? entry.start }, set: { entry.end = $0 }),
                           displayedComponents: .hourAndMinute).labelsHidden()
            }
            TextField("Tag", text: $entry.tag).textFieldStyle(.roundedBorder)
            Text(entry.duration.hm).monospacedDigit().foregroundStyle(.secondary).frame(width: 64, alignment: .trailing)
            Button("Delete", systemImage: "trash", role: .destructive, action: onDelete)
                .labelStyle(.iconOnly).buttonStyle(.borderless)
        }
        .padding(.vertical, 2)
    }
}
