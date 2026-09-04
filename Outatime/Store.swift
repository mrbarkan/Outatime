import Foundation
import Observation

@Observable
final class Store {
    var entries: [Entry] = [] { didSet { if loaded { save() } } }
    var templates: [DayTemplate] = [] { didSet { if loaded { save() } } }
    var currentTag = "" {
        didSet { if let i = entries.firstIndex(where: \.isRunning) { entries[i].tag = currentTag } }
    }

    private let url: URL
    private var loaded = false

    nonisolated private struct File: Codable {
        var entries: [Entry]
        var templates: [DayTemplate]
    }

    static let defaultURL = FileManager.default
        .urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        .appending(path: "Outatime/data.json")

    init(url: URL = Store.defaultURL) {
        self.url = url
        if let data = try? Data(contentsOf: url), let file = try? JSONDecoder().decode(File.self, from: data) {
            entries = file.entries
            templates = file.templates
        }
        currentTag = running?.tag ?? ""
        loaded = true
    }

    private func save() {
        // ponytail: whole-file rewrite on every change; fine for years of entries (a few hundred KB).
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        encoder.dateEncodingStrategy = .iso8601
        guard let data = try? encoder.encode(File(entries: entries, templates: templates)) else { return }
        try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try? data.write(to: url, options: .atomic)
    }

    // MARK: Tracking

    var running: Entry? { entries.first(where: \.isRunning) }

    func start(_ activity: Activity) {
        if running?.activity == activity { return }
        stop()
        entries.append(Entry(activity: activity, start: .now, tag: currentTag))
    }

    func stop() {
        if let i = entries.firstIndex(where: \.isRunning) { entries[i].end = .now }
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

    // MARK: Editing

    func addEntry(on day: Date) {
        let last = entries(on: day).last?.end
        let start = last ?? Calendar.current.date(bySettingHour: 9, minute: 0, second: 0, of: day)!
        entries.append(Entry(activity: .work, start: start, end: start.addingTimeInterval(3600)))
    }

    func delete(_ id: Entry.ID) {
        entries.removeAll { $0.id == id }
    }

    func apply(_ template: DayTemplate, to day: Date) {
        entries.removeAll { Calendar.current.isDate($0.start, inSameDayAs: day) && !$0.isRunning }
        entries.append(contentsOf: template.entries(on: day))
    }
}
