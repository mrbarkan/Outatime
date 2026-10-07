import Foundation
import Observation
import SwiftUI

@Observable
final class Store {
    var entries: [Entry] = [] { didSet { if loaded { save() } } }
    var templates: [DayTemplate] = [] { didSet { if loaded { save() } } }
    var daysOff: Set<String> = [] { didSet { if loaded { save() } } }
    var profiles: [Profile] = [] { didSet { if loaded { save() } } }
    /// The client picked in the menu; new billable blocks get it.
    var currentProfile: Profile.ID? { didSet { if loaded { save() } } }
    /// A block added in the Logbook opens its editor once it appears.
    var justAdded: Entry.ID?
    /// When the tomato timer was turned on; nil while it's off. Not saved: quitting turns it off.
    var tomatoSince: Date?

    private let url: URL
    private var loaded = false

    nonisolated private struct File: Codable {
        var entries: [Entry]
        var templates: [DayTemplate]
        var daysOff: [String]?  // added in 1.0.11
        var profiles: [Profile]?  // added in 1.2
        var current: UUID?
    }

    static let defaultURL = FileManager.default
        .urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        .appending(path: "Outatime/data.json")

    init(url: URL = Store.defaultURL) {
        self.url = url
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        if let data = try? Data(contentsOf: url) {
            if let file = try? decoder.decode(File.self, from: data) {
                entries = file.entries
                templates = file.templates
                daysOff = Set(file.daysOff ?? [])
                profiles = file.profiles ?? []
                currentProfile = file.current
            } else {
                // Unreadable file: keep it aside so the next save can't silently destroy it.
                try? FileManager.default.moveItem(at: url, to: url.appendingPathExtension("broken-\(Int(Date.now.timeIntervalSince1970))"))
            }
        }
        loaded = true
    }

    private func save() {
        // ponytail: whole-file rewrite on every change; fine for years of entries (a few hundred KB).
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        encoder.dateEncodingStrategy = .iso8601
        guard let data = try? encoder.encode(File(entries: entries, templates: templates, daysOff: daysOff.sorted(),
                                                profiles: profiles, current: currentProfile)) else { return }
        try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try? data.write(to: url, options: .atomic)
    }

    // MARK: Tracking

    var running: Entry? { entries.first(where: \.isRunning) }

    func start(_ activity: Activity) {
        let profile = activity.billable ? currentProfile : nil
        if let running, running.activity == activity, running.profile == profile { return }
        stop()
        entries.append(Entry(activity: activity, start: .now, profile: profile))
    }

    /// Picks the client for billable blocks. A billable block running for another client ends here and carries on for
    /// this one; within its first minute it's taken as a correction and just relabelled.
    func select(_ profile: Profile.ID?, now: Date = .now) {
        currentProfile = profile
        guard let i = entries.firstIndex(where: \.isRunning), entries[i].activity.billable, entries[i].profile != profile else { return }
        if now.timeIntervalSince(entries[i].start) < 60 {
            entries[i].profile = profile
        } else {
            entries[i].end = now
            entries.append(Entry(activity: entries[i].activity, start: now, profile: profile))
        }
    }

    /// The running entry, or today's latest one so a note can still land on a block after it was stopped.
    var noteTarget: Entry? { running ?? entries(on: .now).last }

    func addNote(_ text: String) {
        let text = text.trimmingCharacters(in: .whitespaces)
        guard !text.isEmpty, let id = noteTarget?.id, let i = entries.firstIndex(where: { $0.id == id }) else { return }
        entries[i].notes.append(text)
    }

    func stop() {
        if let i = entries.firstIndex(where: \.isRunning) { entries[i].end = .now }
    }

    /// A timer left running past midnight is cut there and carries on as a new entry, so each day keeps its own hours.
    func rollOver(now: Date = .now) {
        let cal = Calendar.current
        while let i = entries.firstIndex(where: \.isRunning), cal.startOfDay(for: entries[i].start) < cal.startOfDay(for: now) {
            let midnight = cal.date(byAdding: .day, value: 1, to: cal.startOfDay(for: entries[i].start))!
            entries[i].end = midnight
            entries.append(Entry(activity: entries[i].activity, start: midnight, notes: entries[i].notes, profile: entries[i].profile))
        }
    }

    /// Work past the daily target carries on as Extra, cut where the target was reached rather than when this runs.
    /// Days that owe nothing (weekends, days off) are all Extra.
    @discardableResult
    func shiftToExtra(_ target: Target, now: Date = .now) -> Bool {
        guard target.seconds > 0, let i = entries.firstIndex(where: \.isRunning), entries[i].activity == .work else { return false }
        let worked = entries(on: now)
            .reduce(into: [Activity: TimeInterval]()) { $0[$1.activity, default: 0] += max(0, ($1.end ?? now).timeIntervalSince($1.start)) }
            .worked(excluding: target.excluded)
        let over = worked - target.owed(on: now, now: now)
        guard over >= 0 else { return false }
        let cut = now - over
        if cut <= entries[i].start {
            entries[i].activity = .extra
        } else {
            entries[i].end = cut
            entries.append(Entry(activity: .extra, start: cut, notes: entries[i].notes, profile: entries[i].profile))
        }
        return true
    }

    /// Start of the running stretch, followed back across midnight cuts.
    var runningSince: Date? { since(sameClient: true) }
    /// The same, across client switches too: switching clients doesn't get you out of the chair.
    var seatedSince: Date? { since(sameClient: false) }

    private func since(sameClient: Bool) -> Date? {
        guard let running else { return nil }
        var since = running.start
        while let prev = entries.first(where: { $0.activity == running.activity && (!sameClient || $0.profile == running.profile)
                                                && $0.start < since && $0.end.map { abs($0.timeIntervalSince(since)) < 1 } == true }) {
            since = prev.start
        }
        return since
    }

    // MARK: Clients

    var activeProfiles: [Profile] { profiles.filter { !$0.archived } }
    var profileNames: [Profile.ID: String] { Dictionary(profiles.map { ($0.id, $0.name) }) { a, _ in a } }

    @discardableResult
    func addProfile(_ name: String) -> Profile.ID? {
        let name = name.trimmingCharacters(in: .whitespaces)
        guard !name.isEmpty else { return nil }
        let p = Profile(name: name)
        profiles.append(p)
        return p.id
    }

    func rename(_ id: Profile.ID, to name: String) {
        if let i = profiles.firstIndex(where: { $0.id == id }) { profiles[i].name = name }
    }

    /// Archived, not deleted: its blocks keep their client. A block running for it carries on.
    func removeProfile(_ id: Profile.ID) {
        guard let i = profiles.firstIndex(where: { $0.id == id }) else { return }
        profiles[i].archived = true
        if currentProfile == id { currentProfile = nil }
    }

    // MARK: Exports

    func monthReport(_ month: Date, target: Target, now: Date = .now) -> Data {
        Report.month(entries(inMonth: month), month: month, target: target, names: profileNames, now: now)
    }

    func masterWorkbook(target: Target, now: Date = .now) -> Data {
        Report.master(entries, target: target, names: profileNames, now: now)
    }

    func entriesCSV(_ month: Date) -> String { CSV.entries(entries(inMonth: month), names: profileNames) }

    /// Clients with billable time in `month`, removed ones too: there may still be a month to bill them for.
    func billedClients(inMonth month: Date) -> [Profile] {
        let billed = Set(entries(inMonth: month).filter(\.activity.billable).compactMap(\.profile))
        return profiles.filter { billed.contains($0.id) }
    }

    func clientReport(_ client: Profile, month: Date) -> Data {
        Report.client(entries(inMonth: month).filter { $0.profile == client.id }, name: client.name, month: month)
    }

    // MARK: Queries

    func entries(on day: Date) -> [Entry] {
        entries.filter { Calendar.current.isDate($0.start, inSameDayAs: day) }.sorted { $0.start < $1.start }
    }

    func entries(inMonth month: Date) -> [Entry] {
        entries.filter { Calendar.current.isDate($0.start, equalTo: month, toGranularity: .month) }.sorted { $0.start < $1.start }
    }

    func totals(on day: Date) -> [Activity: TimeInterval] {
        Self.totals(entries(on: day))
    }

    nonisolated static func totals(_ entries: [Entry]) -> [Activity: TimeInterval] {
        entries.reduce(into: [:]) { $0[$1.activity, default: 0] += $1.duration }
    }

    func target(hours: Double, excluded: String) -> Target {
        Target(seconds: hours * 3600, excluded: excluded, daysOff: daysOff, since: entries.map(\.start).min() ?? .now)
    }

    func balance(_ interval: DateInterval, _ target: Target, now: Date = .now) -> (worked: TimeInterval, balance: TimeInterval) {
        Self.balance(entries, in: interval, target: target, now: now)
    }

    /// Worked time over the days in `interval`, and their balance against what each day owes.
    nonisolated static func balance(_ entries: [Entry], in interval: DateInterval, target: Target, now: Date = .now)
        -> (worked: TimeInterval, balance: TimeInterval) {
        let cal = Calendar.current
        let byDay = Dictionary(grouping: entries.filter { $0.start >= interval.start && $0.start < interval.end }) { cal.startOfDay(for: $0.start) }
        var worked = 0.0, balance = 0.0
        var d = cal.startOfDay(for: interval.start)
        while d < interval.end {
            let w = byDay[d].map { totals($0).worked(excluding: target.excluded) } ?? 0
            worked += w
            balance += target.balance(worked: w, on: d, now: now)
            d = cal.date(byAdding: .day, value: 1, to: d)!
        }
        return (worked, balance)
    }

    // MARK: Editing

    func addEntry(on day: Date, at start: Date? = nil) {
        let start = start ?? entries(on: day).last?.end ?? Calendar.current.date(bySettingHour: 9, minute: 0, second: 0, of: day)!
        insert(.work, at: start, length: 3600)
    }

    /// Adds an entry at `start`, at most `length` long. Inside a block it cuts that block around itself (a break in the
    /// middle of work); in a gap it stops at the next block instead of overlapping it.
    func insert(_ activity: Activity, at start: Date, length: TimeInterval, now: Date = .now) {
        var end = start + length
        if let i = entries.firstIndex(where: { $0.start <= start && start < ($0.end ?? now) }) {
            let host = entries[i]
            end = min(end, host.end ?? now)
            if end < (host.end ?? now) {
                var tail = host
                tail.id = UUID()
                tail.start = end
                entries.append(tail)  // a running host keeps running in its tail
            }
            if host.start == start { entries.remove(at: i) } else { entries[i].end = start }
        } else if let next = entries.map(\.start).filter({ $0 > start }).min() {
            end = min(end, next)
        }
        let e = Entry(activity: activity, start: start, end: end, profile: activity.billable ? currentProfile : nil)
        entries.append(e)
        justAdded = e.id
    }

    /// Id-based so a control that fires after the entry is deleted (e.g. a date picker on dismiss) can't index out of range.
    func binding(for entry: Entry) -> Binding<Entry> {
        Binding(get: { self.entries.first { $0.id == entry.id } ?? entry },
                set: { new in if let i = self.entries.firstIndex(where: { $0.id == entry.id }) { self.entries[i] = new } })
    }

    func delete(_ id: Entry.ID) {
        entries.removeAll { $0.id == id }
    }

    // MARK: Several blocks at once (one save each)

    func delete(_ ids: Set<Entry.ID>) {
        entries.removeAll { ids.contains($0.id) }
    }

    /// Only billable blocks take a client; the rest of the selection is left alone.
    func assign(_ ids: Set<Entry.ID>, to profile: Profile.ID?) {
        entries = entries.map { e in
            guard ids.contains(e.id), e.activity.billable else { return e }
            var e = e; e.profile = profile; return e
        }
    }

    /// A non-billable activity drops the client.
    func setActivity(_ ids: Set<Entry.ID>, _ activity: Activity) {
        entries = entries.map { e in
            guard ids.contains(e.id) else { return e }
            var e = e; e.activity = activity
            if !activity.billable { e.profile = nil }
            return e
        }
    }

    func apply(_ template: DayTemplate, to day: Date) {
        entries.removeAll { Calendar.current.isDate($0.start, inSameDayAs: day) && !$0.isRunning }
        entries.append(contentsOf: template.entries(on: day))
    }
}
