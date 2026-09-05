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
                    Button("Today") {
                        month = Date.now.startOfMonth
                        selectedDay = Calendar.current.startOfDay(for: .now)
                    }
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
        let cal = Calendar.current
        let today = cal.isDateInToday(day)
        HStack {
            Text(day, format: .dateTime.weekday(.abbreviated).day())
                .fontWeight(today ? .bold : .regular)
                .foregroundStyle(today ? Color.accentColor : cal.isDateInWeekend(day) ? Color.secondary : Color.primary)
            Spacer()
            HStack(spacing: 8) {
                ForEach(Activity.allCases.filter { totals[$0, default: 0] > 0 }) { a in
                    Label(totals[a]!.hm, systemImage: a.symbol).foregroundStyle(a.color)
                }
            }
            .font(.caption).monospacedDigit()
        }
        .padding(.vertical, 2)
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
        let dayEntries = store.entries(on: day)
        DayTimeline(day: day, entries: dayEntries)
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
        let balance = t.worked - targetHours * 3600
        return HStack(spacing: 14) {
            ForEach(Activity.allCases) { a in
                Label(t[a, default: 0].hm, systemImage: a.symbol).foregroundStyle(a.color)
            }
            Spacer()
            Text("Balance \(balance >= 0 ? "+" : "−")\(abs(balance).hm)")
                .fontWeight(.semibold)
                .foregroundStyle(balance >= 0 ? .green : .secondary)
            Stepper("Target \(targetHours.formatted())h", value: $targetHours, in: 0...16, step: 0.5)
                .controlSize(.small)
        }
        .font(.callout).monospacedDigit()
        .padding(.horizontal, 16).padding(.vertical, 10)
        .glassEffect(.regular, in: .rect)
    }
}

/// Calendar-style day view: drag a block to move it, drag its top/bottom edge to resize, click to edit, double-click empty space to add.
private struct DayTimeline: View {
    @Environment(Store.self) private var store
    let day: Date
    let entries: [Entry]
    private let hourHeight: CGFloat = 44
    private let gutter: CGFloat = 48

    var body: some View {
        @Bindable var store = store
        let dayStart = Calendar.current.startOfDay(for: day)
        ScrollViewReader { proxy in
            ScrollView {
                ZStack(alignment: .topLeading) {
                    VStack(spacing: 0) {
                        ForEach(0..<24, id: \.self) { h in
                            HStack(alignment: .top, spacing: 6) {
                                Text(dayStart.addingTimeInterval(TimeInterval(h) * 3600), format: .dateTime.hour())
                                    .font(.caption2).foregroundStyle(.secondary)
                                    .frame(width: gutter - 6, alignment: .trailing).offset(y: -7)
                                Rectangle().fill(.separator).frame(height: 1)
                            }
                            .frame(height: hourHeight, alignment: .top)
                            .id(h)
                        }
                    }
                    ForEach(entries) { entry in
                        if let i = store.entries.firstIndex(where: { $0.id == entry.id }) {
                            TimelineBlock(entry: $store.entries[i], dayStart: dayStart, hourHeight: hourHeight) { store.delete(entry.id) }
                                .padding(.leading, gutter).padding(.trailing, 12)
                        }
                    }
                }
                .padding(.vertical, 10)
                .contentShape(Rectangle())
                .onTapGesture(count: 2) { p in
                    let minutes = (((p.y - 10) / hourHeight * 4).rounded(.down) * 15).clamped(to: 0...(23 * 60))
                    store.addEntry(on: day, at: dayStart.addingTimeInterval(minutes * 60))
                }
            }
            .overlay {
                if entries.isEmpty {
                    Text("Double-click to add an entry").foregroundStyle(.secondary).allowsHitTesting(false)
                }
            }
            .onAppear { proxy.scrollTo(entries.first.map { Calendar.current.component(.hour, from: $0.start) } ?? 8, anchor: .top) }
        }
    }
}

private struct TimelineBlock: View {
    @Binding var entry: Entry
    let dayStart: Date
    let hourHeight: CGFloat
    let onDelete: () -> Void
    @State private var draft: Entry?  // live copy while dragging; the store is written once on release
    @State private var mode: DragMode?
    @State private var editing = false
    private enum DragMode { case move, start, end }

    var body: some View {
        let e = draft ?? entry
        let top = CGFloat(e.start.timeIntervalSince(dayStart) / 3600) * hourHeight
        let height = max(10, CGFloat(e.duration / 3600) * hourHeight)
        RoundedRectangle(cornerRadius: 6)
            .fill(e.activity.color.opacity(draft == nil ? 0.22 : 0.35))
            .overlay(alignment: .leading) { e.activity.color.frame(width: 3).clipShape(.rect(cornerRadius: 6)) }
            .overlay(alignment: .topLeading) {
                VStack(alignment: .leading, spacing: 1) {
                    HStack(spacing: 4) {
                        Image(systemName: e.activity.symbol)
                        Text(e.activity.label).fontWeight(.semibold)
                        if !e.tag.isEmpty { Text("· \(e.tag)").foregroundStyle(.secondary) }
                    }
                    (Text(e.start, style: .time) + Text(" – ") + (e.end.map { Text($0, style: .time) } ?? Text("running")))
                        .foregroundStyle(.secondary)
                }
                .font(.caption).lineLimit(1)
                .padding(.horizontal, 8).padding(.vertical, 3)
            }
            .clipped()
            .frame(height: height)
            .contentShape(Rectangle())
            .gesture(drag(blockHeight: height))
            .onTapGesture { editing = true }
            .popover(isPresented: $editing) { EntryForm(entry: $entry, onDelete: onDelete) }
            .offset(y: top)
    }

    private func drag(blockHeight: CGFloat) -> some Gesture {
        DragGesture(minimumDistance: 3)
            .onChanged { g in
                if mode == nil {
                    let y = g.startLocation.y
                    mode = entry.isRunning ? .start : y < 10 ? .start : y > blockHeight - 10 ? .end : .move
                }
                let dayEnd = dayStart.addingTimeInterval(86400)
                var delta = (g.translation.height / hourHeight * 3600 / 300).rounded() * 300  // snap to 5 min
                var d = entry
                switch mode {
                case .move:
                    delta = max(delta, dayStart.timeIntervalSince(entry.start))
                    if let end = entry.end { delta = min(delta, dayEnd.timeIntervalSince(end)) }
                    d.start += delta
                    d.end = d.end.map { $0 + delta }
                case .start:
                    d.start = min(max(entry.start + delta, dayStart), (entry.end ?? .now) - 300)
                case .end:
                    d.end = max(min(entry.end! + delta, dayEnd), entry.start + 300)
                case nil: break
                }
                draft = d
            }
            .onEnded { _ in
                if let d = draft { entry = d }
                draft = nil
                mode = nil
            }
    }
}

private struct EntryForm: View {
    @Binding var entry: Entry
    let onDelete: () -> Void

    var body: some View {
        Form {
            Picker("Activity", selection: $entry.activity) {
                ForEach(Activity.allCases) { Label($0.label, systemImage: $0.symbol).tag($0) }
            }
            DatePicker("Start", selection: $entry.start, displayedComponents: .hourAndMinute)
            if entry.isRunning {
                LabeledContent("End") { Text("running") }
            } else {
                DatePicker("End", selection: Binding(get: { entry.end ?? entry.start }, set: { entry.end = $0 }),
                           displayedComponents: .hourAndMinute)
            }
            TextField("Tag", text: $entry.tag)
            LabeledContent("Duration") { Text(entry.duration.hm).monospacedDigit() }
            Button("Delete", systemImage: "trash", role: .destructive, action: onDelete)
        }
        .formStyle(.columns)
        .padding()
        .frame(width: 280)
    }
}

private extension Comparable {
    func clamped(to r: ClosedRange<Self>) -> Self { min(max(self, r.lowerBound), r.upperBound) }
}
