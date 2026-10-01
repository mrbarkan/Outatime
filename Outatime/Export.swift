import AppKit
import UniformTypeIdentifiers

/// One row of a report: a day with entries or owed hours. Summed, the balances match the app's balances.
nonisolated struct DaySummary {
    var date: Date
    var hours: [Activity: TimeInterval]
    /// The hours that count toward the target, and what the day owed.
    var worked, owed, balance: TimeInterval
    var notes: String

    static func days(_ dates: [Date], _ entries: [Entry], target: Target, now: Date = .now) -> [DaySummary] {
        let byDay = Dictionary(grouping: entries) { Calendar.current.startOfDay(for: $0.start) }
        return dates.compactMap { date in
            let es = byDay[date] ?? []
            let owed = target.owed(on: date, now: now)
            guard !es.isEmpty || owed > 0 else { return nil }
            let t = Store.totals(es), worked = t.worked(excluding: target.excluded)
            return DaySummary(date: date, hours: t, worked: worked, owed: owed, balance: target.balance(worked: worked, on: date, now: now),
                              notes: es.flatMap(\.notes).joined(separator: "; "))
        }
    }
}

nonisolated extension Activity {
    /// Column and row name in exported files.
    var title: String { self == .outOfOffice ? "Out of Office" : rawValue.capitalized }
}

nonisolated enum CSV {
    private static func escape(_ s: String) -> String {
        s.contains(where: { $0 == "," || $0 == "\"" || $0.isNewline }) ? "\"\(s.replacingOccurrences(of: "\"", with: "\"\""))\"" : s
    }

    private static func row(_ fields: [String]) -> String { fields.map(escape).joined(separator: ",") }
    private static func hours(_ t: TimeInterval) -> String { String(format: "%.2f", t / 3600) }
    private static func signed(_ t: TimeInterval) -> String { String(format: "%+.2f", t / 3600) }

    static func entries(_ entries: [Entry]) -> String {
        let time = Date.FormatStyle(date: .omitted, time: .shortened)
        var lines = [row(["Date", "Activity", "Notes", "Start", "End", "Hours"])]
        for e in entries {
            lines.append(row([e.start.dayKey, e.activity.title, e.notes.joined(separator: "; "),
                              e.start.formatted(time), e.end?.formatted(time) ?? "", hours(e.duration)]))
        }
        return lines.joined(separator: "\n") + "\n"
    }

    /// Every day of `month` that has entries or owes hours, then a total row that matches the app's month balance.
    static func daily(_ entries: [Entry], month: Date, target: Target, now: Date = .now) -> String {
        let days = DaySummary.days(month.daysInMonth, entries, target: target, now: now)
        var lines = [row(["Date"] + Activity.allCases.map(\.title) + ["Balance", "Notes"])]
        for d in days {
            lines.append(row([d.date.dayKey] + Activity.allCases.map { hours(d.hours[$0, default: 0]) } + [signed(d.balance), d.notes]))
        }
        let sum = { (f: (DaySummary) -> TimeInterval) in days.map(f).reduce(0, +) }
        lines.append(row(["Total"] + Activity.allCases.map { a in hours(sum { $0.hours[a, default: 0] }) } + [signed(sum(\.balance)), ""]))
        return lines.joined(separator: "\n") + "\n"
    }
}

/// The Excel exports. A month report and the master workbook share the Summary and Entries sheets (tables
/// `tblDays` and `tblEntries`), so a month's rows paste straight into the master and its dashboard picks them up.
nonisolated enum Report {
    private typealias Cell = XLSX.Cell

    static func month(_ entries: [Entry], month: Date, target: Target, now: Date = .now) -> Data {
        let days = DaySummary.days(month.daysInMonth, entries, target: target, now: now)
        return XLSX.workbook([summary(days, totals: true), list(entries)])
    }

    /// Everything since tracking began, opening on a dashboard.
    static func master(_ entries: [Entry], target: Target, now: Date = .now) -> Data {
        let cal = Calendar.current
        let dates = sequence(first: cal.startOfDay(for: target.since)) { cal.date(byAdding: .day, value: 1, to: $0) }.prefix { $0 <= now }
        let days = DaySummary.days(Array(dates), entries, target: target, now: now)
        return XLSX.workbook([dashboard(days, target: target, now: now), summary(days, totals: false),
                              list(entries.sorted { $0.start < $1.start })])
    }

    private typealias Column = (title: String, value: (DaySummary) -> Double)

    private static var values: [Column] {
        let activities = Activity.allCases.map { a -> Column in (a.title, { $0.hours[a, default: 0] / 3600 }) }
        let totals: [Column] = [("Worked", { $0.worked / 3600 }), ("Target", { $0.owed / 3600 }), ("Balance", { $0.balance / 3600 })]
        return activities + totals
    }

    private static func summary(_ days: [DaySummary], totals: Bool) -> XLSX.Sheet {
        let balance = values.count - 1
        let rows = days.map { d -> [Cell] in
            [.number(XLSX.serial(d.date), .date)] + values.indices.map { .number(values[$0].value(d), $0 == balance ? .balance : .hours) } + [.text(d.notes)]
        }
        let sums = !totals ? [] : values.indices.map { i in
            (column: i + 1, total: days.map(values[i].value).reduce(0, +), style: i == balance ? XLSX.Style.totalBalance : .totalHours)
        }
        return XLSX.tableSheet("Summary", table: "tblDays", columns: ["Date"] + values.map(\.title) + ["Notes"], rows: rows, sums: sums,
                               widths: [16] + values.map { max(10, Double($0.title.count) + 2) } + [40])
    }

    private static func list(_ entries: [Entry]) -> XLSX.Sheet {
        let rows = entries.map { e -> [Cell] in
            [.number(XLSX.serial(Calendar.current.startOfDay(for: e.start)), .date), .text(e.activity.title),
             .number(XLSX.serial(e.start), .time), e.end.map { .number(XLSX.serial($0), .time) } ?? .empty,
             .number(e.duration / 3600, .hours), .text(e.notes.joined(separator: "; "))]
        }
        return XLSX.tableSheet("Entries", table: "tblEntries", columns: ["Date", "Activity", "Start", "End", "Hours", "Notes"],
                               rows: rows, widths: [16, 14, 9, 9, 9, 40])
    }

    /// Cards, a worked-vs-target chart, an activity breakdown and a month table, all formulas over `tblDays`.
    private static func dashboard(_ days: [DaySummary], target: Target, now: Date) -> XLSX.Sheet {
        var s = XLSX.Sheet(name: "Dashboard", widths: [3] + Array(repeating: 13, count: 8), gridlines: false)
        let col = XLSX.column
        let sum = { (f: (DaySummary) -> TimeInterval, ds: [DaySummary]) in ds.map(f).reduce(0, +) / 3600 }
        let first = days.first?.date ?? now
        let worked = sum(\.worked, days), daysWorked = Double(days.count { $0.worked > 0 })

        s.put(1, 1, .text("Hours Overview", .title)); s.heights[1] = 34
        s.put(2, 1, .text("Since \(first.formatted(.dateTime.month(.wide).year())) · target \(String(format: "%g", target.seconds / 3600)) h a day · updated \(now.dayKey)", .subtitle))

        // Four cards in rows 5–7, each two merged columns wide.
        let cards: [(label: String, value: Cell, caption: String)] = [
            ("HOURS WORKED", .formula("SUM(tblDays[Worked])", cached: worked, .cardHours), "toward the target"),
            ("HOURS BANK", .formula("SUM(tblDays[Balance])", cached: sum(\.balance, days), .cardBank), "worked minus target"),
            ("DAYS WORKED", .formula(#"COUNTIF(tblDays[Worked],">0")"#, cached: daysWorked, .cardDays), "with time logged"),
            ("AVERAGE DAY", .formula("IFERROR(B6/F6,0)", cached: daysWorked > 0 ? worked / daysWorked : 0, .cardHours), "per day worked"),
        ]
        for (i, card) in cards.enumerated() {
            let c = 1 + i * 2
            for (row, cell, style) in [(4, Cell.text(card.label, .cardLabel), XLSX.Style.cardLabel), (5, card.value, .cardHours),
                                       (6, .text(card.caption, .cardCaption), .cardCaption)] {
                s.put(row, c, cell); s.put(row, c + 1, .blank(style))
                s.merges.append("\(col(c))\(row + 1):\(col(c + 1))\(row + 1)")
            }
        }
        s.heights[4] = 24; s.heights[5] = 38; s.heights[6] = 22

        // Months from the first one tracked through December. Each is EDATE of the one above, so dragging the last
        // row down adds more.
        let cal = Calendar.current
        let lastMonth = cal.date(from: DateComponents(year: cal.component(.year, from: now), month: 12))!
        let months = Array(sequence(first: first.startOfMonth) { cal.date(byAdding: .month, value: 1, to: $0) }.prefix { $0 <= lastMonth })
        let monthTop = 37  // first month row, 0-based; the table header sits above it

        s.put(8, 1, .text("Worked vs target by month", .section))
        let range = { (c: Int) in "Dashboard!$\(col(c))$\(monthTop + 1):$\(col(c))$\(monthTop + months.count)" }
        let inMonth = { (m: Date) in days.filter { cal.isDate($0.date, equalTo: m, toGranularity: .month) } }
        s.chart = XLSX.Chart(from: (1, 9), to: (9, 24), categories: (range(1), months.map(XLSX.serial)),
                             series: [("Worked", range(2), months.map { sum(\.worked, inMonth($0)) }, "2563EB"),
                                      ("Target", range(3), months.map { sum(\.owed, inMonth($0)) }, "CBD5E1")])

        s.put(25, 1, .text("By activity", .section))
        for (c, title) in ["Activity", "Hours", "Share"].enumerated() { s.put(26, 1 + c, .text(title, c == 0 ? .head : .headRight)) }
        for c in 4...8 { s.put(26, c, .blank(.head)) }
        let total = sum({ $0.hours.values.reduce(0, +) }, days)
        for (i, a) in Activity.allCases.enumerated() {
            let r = 27 + i, hours = sum({ $0.hours[a, default: 0] }, days)
            let span = "$C$28:$C$\(27 + Activity.allCases.count)"
            s.put(r, 1, .text(a.title, .rowText))
            s.put(r, 2, .formula("SUM(tblDays[\(a.title)])", cached: hours, .rowHours))
            s.put(r, 3, .formula("IFERROR(C\(r + 1)/SUM(\(span)),0)", cached: total > 0 ? hours / total : 0, .rowShare))
            // The share again, merged across the rest of the row and drawn as a bar.
            s.put(r, 4, .formula("D\(r + 1)", cached: total > 0 ? hours / total : 0, .rowBar))
            for c in 5...8 { s.put(r, c, .blank(.rowText)) }
            s.merges.append("E\(r + 1):I\(r + 1)")
        }
        s.bars = ["E28:E\(27 + Activity.allCases.count)"]

        s.put(monthTop - 2, 1, .text("By month", .section))
        let columns: [(title: String, formula: (String) -> String, value: ([DaySummary]) -> Double, style: XLSX.Style)] = [
            ("Worked", { "SUMIFS(tblDays[Worked],\($0))" }, { sum(\.worked, $0) }, .rowHours),
            ("Target", { "SUMIFS(tblDays[Target],\($0))" }, { sum(\.owed, $0) }, .rowHours),
            ("Balance", { "SUMIFS(tblDays[Balance],\($0))" }, { sum(\.balance, $0) }, .rowBalance),
            ("Days", { #"COUNTIFS(\#($0),tblDays[Worked],">0")"# }, { Double($0.count { $0.worked > 0 }) }, .rowCount),
        ] + [Activity.extra, .travel, .outOfOffice].map { a in
            (a.title, { "SUMIFS(tblDays[\(a.title)],\($0))" }, { sum({ $0.hours[a, default: 0] }, $0) }, .rowHours)
        }
        s.put(monthTop - 1, 1, .text("Month", .head))
        for (c, column) in columns.enumerated() { s.put(monthTop - 1, 2 + c, .text(column.title, .headRight)) }
        for (i, m) in months.enumerated() {
            let r = monthTop + i, ds = inMonth(m)
            let criteria = #"tblDays[Date],">="&$B\#(r + 1),tblDays[Date],"<"&EDATE($B\#(r + 1),1)"#
            s.put(r, 1, i == 0 ? .number(XLSX.serial(m), .month) : .formula("EDATE(B\(r),1)", cached: XLSX.serial(m), .month))
            for (c, column) in columns.enumerated() { s.put(r, 2 + c, .formula(column.formula(criteria), cached: column.value(ds), column.style)) }
        }
        return s
    }
}

extension UTType {
    static let xlsx = UTType(filenameExtension: "xlsx") ?? .data
}

/// Export file names use "2026-09", never the locale's month format: "09/2026" puts a slash in the name, which
/// Finder stores as a colon and Excel then can't find the file.
nonisolated func exportName(_ month: Date, _ suffix: String) -> String { "Outatime \(month.dayKey.prefix(7))\(suffix)" }

func save(_ data: Data, as type: UTType, suggestedName: String) {
    let panel = NSSavePanel()
    panel.allowedContentTypes = [type]
    panel.nameFieldStringValue = suggestedName
    NSApp.activate()
    panel.begin { response in
        guard response == .OK, let url = panel.url else { return }
        try? data.write(to: url, options: .atomic)
    }
}
