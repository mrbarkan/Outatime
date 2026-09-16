import SwiftUI

struct EditorView: View {
    @Environment(Store.self) private var store
    @AppStorage("targetHours") private var targetHours = 8.0
    @AppStorage("hourHeight") private var hourHeight = 56.0
    @State private var day = Calendar.current.startOfDay(for: .now)
    @State private var naming = false
    @State private var templateName = ""
    @State private var pendingTemplate: DayTemplate?
    private static let zoomLevels = [40.0, 56, 84, 126, 189]

    var body: some View {
        let month = day.startOfMonth
        let dayEntries = store.entries(on: day)
        NavigationSplitView {
            // ⌘-click deselection is ignored, so there is always a day to show.
            List(selection: Binding(get: { day }, set: { if let d = $0 { day = d } })) {
                Section {
                    ForEach(month.daysInMonth, id: \.self) { d in
                        DayRow(day: d, totals: store.totals(on: d))
                    }
                } header: {
                    HStack {
                        Text(month, format: .dateTime.month(.wide).year())
                        Spacer()
                        Button("Previous Month", systemImage: "chevron.left") { shiftMonth(-1) }
                        Button("Next Month", systemImage: "chevron.right") { shiftMonth(1) }
                    }
                    .buttonStyle(.borderless).labelStyle(.iconOnly)
                }
            }
            .navigationSplitViewColumnWidth(min: 230, ideal: 260)
        } detail: {
            DayTimeline(day: day, entries: dayEntries)
                .safeAreaInset(edge: .bottom) { summary(dayEntries) }
                .navigationTitle(day.formatted(.dateTime.weekday(.wide).day().month(.wide)))
        }
        // Finder-style toolbar: back/forward capsule and title on the left, grouped controls on the right.
        .toolbar {
            ToolbarItemGroup(placement: .navigation) {
                Button("Previous Day", systemImage: "chevron.left") { shiftDay(-1) }.keyboardShortcut("[")
                Button("Today") { day = Calendar.current.startOfDay(for: .now) }
                    .keyboardShortcut("t").disabled(Calendar.current.isDateInToday(day))
                Button("Next Day", systemImage: "chevron.right") { shiftDay(1) }.keyboardShortcut("]")
            }
            ToolbarItemGroup {
                Button("Zoom Out", systemImage: "minus.magnifyingglass") { zoom(-1) }
                    .keyboardShortcut("-").disabled(hourHeight <= Self.zoomLevels.first!)
                Button("Zoom In", systemImage: "plus.magnifyingglass") { zoom(1) }
                    .keyboardShortcut("=").disabled(hourHeight >= Self.zoomLevels.last!)
            }
            ToolbarSpacer(.fixed)
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
        .onAppear { NSApp.setActivationPolicy(.regular); NSApp.activate() }
        .onDisappear { NSApp.setActivationPolicy(.accessory) }
    }

    private func shiftDay(_ days: Int) {
        day = Calendar.current.date(byAdding: .day, value: days, to: day)!
    }

    private func shiftMonth(_ months: Int) {
        day = Calendar.current.date(byAdding: .month, value: months, to: day.startOfMonth)!
    }

    private func zoom(_ step: Int) {
        let levels = Self.zoomLevels
        let i = levels.lastIndex { $0 <= hourHeight } ?? 0
        hourHeight = levels[(i + step).clamped(to: 0...(levels.count - 1))]
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
        .padding(.horizontal, 16).padding(.vertical, 8)
        .glassEffect(.regular, in: .capsule)
        .padding(12)
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

/// Calendar-style day view: drag a block to move it, drag its top/bottom edge to resize, click to edit, double-click empty space to add.
private struct DayTimeline: View {
    @Environment(Store.self) private var store
    let day: Date
    let entries: [Entry]
    @AppStorage("hourHeight") private var hourHeight = 56.0
    private let gutter: CGFloat = 48

    var body: some View {
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
                        TimelineBlock(entry: store.binding(for: entry), others: entries.filter { $0.id != entry.id }, dayStart: dayStart, hourHeight: hourHeight) { store.delete(entry.id) }
                            .padding(.leading, gutter).padding(.trailing, 12)
                    }
                }
                .padding(.vertical, 10)
                .coordinateSpace(name: "timeline")
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
    @Environment(Store.self) private var store
    @Binding var entry: Entry
    let others: [Entry]  // same day, for magnetic snapping and shared borders
    let dayStart: Date
    let hourHeight: CGFloat
    let onDelete: () -> Void
    @State private var draft: Entry?  // follows the pointer; settled + written to the store on release
    @State private var dragMode: BlockDrag.Mode?
    @State private var hovering = false
    @State private var editing = false

    var body: some View {
        let e = draft ?? entry
        let dragging = draft != nil
        let top = CGFloat(e.start.timeIntervalSince(dayStart) / 3600) * hourHeight
        let natural = CGFloat(e.duration / 3600) * hourHeight
        let height = max(14, natural)
        RoundedRectangle(cornerRadius: 6)
            .fill(e.activity.color.opacity(dragging ? 0.4 : hovering ? 0.3 : 0.22))
            .overlay(alignment: .leading) { e.activity.color.frame(width: 3).clipShape(.rect(cornerRadius: 6)) }
            .overlay(alignment: .topLeading) {
                let title = HStack(spacing: 4) {
                    Image(systemName: e.activity.symbol)
                    Text(e.activity.label).fontWeight(.semibold)
                }
                let span = (Text(e.start, style: .time) + Text(" – ") + (e.end.map { Text($0, style: .time) } ?? Text("running")))
                    .foregroundStyle(.secondary)
                let duration = Text(e.duration.hm).monospacedDigit().foregroundStyle(.secondary)
                Group {
                    if height < 40 {
                        // ponytail: short block — everything on one line, notes joined.
                        HStack(spacing: 6) {
                            title; span; duration
                            if !e.notes.isEmpty { Text("· " + e.notes.joined(separator: " · ")).foregroundStyle(.secondary) }
                        }
                    } else {
                        VStack(alignment: .leading, spacing: 1) {
                            HStack { title; Spacer(); duration }
                            span
                            ForEach(Array(e.notes.enumerated()), id: \.offset) { Text("· \($0.element)").foregroundStyle(.secondary) }
                        }
                    }
                }
                .font(.caption).lineLimit(1)
                .padding(.horizontal, 8).padding(.vertical, height < 40 ? 0 : 3)
                .frame(height: height < 40 ? height : nil)
            }
            .clipped()
            .frame(height: height)
            .contentShape(Rectangle())
            .pointerStyle(dragging ? .grabActive : .grabIdle)
            .gesture(drag(entry.isRunning ? .start : .move))
            .onTapGesture { editing = true }
            .onHover { hovering = $0 }
            .overlay(alignment: .top) { handle(.start, in: height) }
            .overlay(alignment: .bottom) { if !entry.isRunning { handle(.end, in: height) } }
            .overlay(alignment: dragMode == .end ? .bottomTrailing : .topTrailing) {
                if dragging {
                    Text(dragMode == .end ? e.end ?? e.start : e.start, style: .time)
                        .font(.caption).fontWeight(.semibold).monospacedDigit()
                        .padding(.horizontal, 6).padding(.vertical, 2)
                        .background(.regularMaterial, in: Capsule())
                        .padding(4)
                }
            }
            .shadow(color: .black.opacity(dragging ? 0.25 : 0), radius: 6, y: 2)
            .popover(isPresented: $editing) { EntryForm(entry: $entry) { editing = false; onDelete() } }
            .offset(y: top)
            // A block stretched to the minimum height overhangs the next one; keep it on top so it stays clickable.
            .zIndex(dragging ? 2 : height > natural ? 1 : 0)
    }

    /// Resize grip along an edge; its own gesture wins over the block's move gesture. It shrinks on short blocks so
    /// the middle stays grabbable, and a click on it still opens the editor.
    private func handle(_ mode: BlockDrag.Mode, in height: CGFloat) -> some View {
        Color.clear.frame(height: min(8, height / 4))
            .contentShape(Rectangle())
            .overlay {
                Capsule().fill(.primary.opacity(hovering || dragMode == mode ? 0.35 : 0)).frame(width: 28, height: 3)
            }
            .pointerStyle(.frameResize(position: mode == .start ? .top : .bottom))
            .gesture(drag(mode))
            .onTapGesture { editing = true }
    }

    private func drag(_ mode: BlockDrag.Mode) -> some Gesture {
        // Measured in the timeline's space: the block (and the grip on it) moves under the pointer during the drag,
        // so a local-space translation would feed back into itself and jitter.
        DragGesture(minimumDistance: 2, coordinateSpace: .named("timeline"))
            .onChanged { g in
                dragMode = mode
                // Pointer-driven layout must not inherit an animation, or the box lags and stutters behind the mouse.
                var t = Transaction()
                t.disablesAnimations = true
                withTransaction(t) { draft = moved(g, mode, drop: false) }
            }
            .onEnded { g in
                guard draft != nil else { return }
                let d = moved(g, mode, drop: true)
                // ponytail: a shared border drags the neighbour with it, but only on release — writing the store
                // per pointer move would save the file at 60 Hz. BlockDrag keeps the neighbour ≥ 5 min long.
                if var n = BlockDrag.neighbour(of: entry, mode, in: others) {
                    if mode == .start { n.end = d.start } else { n.start = d.end! }
                    store.binding(for: n).wrappedValue = n
                }
                withAnimation(.snappy(duration: 0.2)) {
                    entry = d
                    draft = nil
                    dragMode = nil
                }
            }
    }

    private func moved(_ g: DragGesture.Value, _ mode: BlockDrag.Mode, drop: Bool) -> Entry {
        // ponytail: 4 pt magnet — under one 5-minute step at the default zoom, so a block can still sit 5 min off an edge.
        BlockDrag.drag(entry, mode, by: g.translation.height / hourHeight * 3600, others: others,
                       dayStart: dayStart, magnet: 4 / hourHeight * 3600, drop: drop)
    }
}

/// Drag math for timeline blocks, kept free of views so it can be tested.
nonisolated enum BlockDrag {
    enum Mode { case move, start, end }
    static let grid: TimeInterval = 300
    static let minLength: TimeInterval = 300

    /// The block sharing the dragged edge; it follows that edge on release. Blocks tracked back to back are
    /// milliseconds apart (and lose sub-seconds when saved), so anything within a second counts as shared.
    static func neighbour(of e: Entry, _ mode: Mode, in others: [Entry]) -> Entry? {
        let touching = { (a: Date?, b: Date?) in a.flatMap { a in b.map { abs(a.timeIntervalSince($0)) < 1 } } ?? false }
        switch mode {
        case .start: return others.first { touching($0.end, e.start) }
        case .end: return others.first { touching($0.start, e.end) }
        case .move: return nil
        }
    }

    /// `e` dragged by `delta` seconds. Edges within `magnet` seconds of another block's edge stick to it; on `drop`
    /// every other dragged edge lands on the 5-minute grid. Only the dragged edge moves — a resize never nudges the
    /// opposite edge, and a move keeps the duration.
    static func drag(_ e: Entry, _ mode: Mode, by delta: TimeInterval, others: [Entry], dayStart: Date,
                     magnet: TimeInterval, drop: Bool, now: Date = .now) -> Entry {
        // The shared neighbour moves with this edge, so its edges can't attract it — they'd pin it in place.
        let shared = neighbour(of: e, mode, in: others)
        let edges = others.filter { $0.id != shared?.id }.flatMap { [$0.start, $0.end].compactMap { $0 } }
        func pull(_ t: Date) -> TimeInterval? {
            edges.map { $0.timeIntervalSince(t) }.filter { abs($0) <= magnet }.min { abs($0) < abs($1) }
        }
        func settle(_ t: Date) -> Date {
            let offset = t.timeIntervalSince(dayStart)
            return t + (pull(t) ?? (drop ? (offset / grid).rounded() * grid - offset : 0))
        }
        let dayEnd = dayStart + 86400
        var d = e
        switch mode {
        case .move:
            var delta = max(delta, dayStart.timeIntervalSince(e.start))
            if let end = e.end { delta = min(delta, dayEnd.timeIntervalSince(end)) }
            d.start += delta
            d.end = d.end.map { $0 + delta }
            // Whichever edge is nearer a magnet wins; otherwise the start settles.
            let shift = [d.start, d.end].compactMap { $0 }.compactMap(pull).min { abs($0) < abs($1) }
                ?? settle(d.start).timeIntervalSince(d.start)
            d.start += shift
            d.end = d.end.map { $0 + shift }
        case .start:
            let floor = shared.map { $0.start + minLength } ?? dayStart
            d.start = min(max(settle(e.start + delta), floor), (e.end ?? now) - minLength)
        case .end:
            let ceiling = shared.map { ($0.end ?? now) - minLength } ?? dayEnd
            d.end = max(min(settle((e.end ?? now) + delta), ceiling), e.start + minLength)
        }
        return d
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
            LabeledContent("Notes") {
                VStack(alignment: .leading, spacing: 4) {
                    // Each line is shown with a leading bullet; the bullet is display-only and stripped on the way back.
                    TextField("Notes", text: Binding(get: { entry.notes.map { "· " + $0 }.joined(separator: "\n") },
                                                     set: { entry.notes = $0.split(separator: "\n", omittingEmptySubsequences: false)
                                                         .map { $0.hasPrefix("· ") ? String($0.dropFirst(2)) : String($0) } }),
                              axis: .vertical)
                        .lineLimit(1...6)
                    Text("Option-Return adds a line").font(.caption).foregroundStyle(.secondary)
                }
            }
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
