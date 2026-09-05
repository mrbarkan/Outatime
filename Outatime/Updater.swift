import SwiftUI

/// Checks GitHub Releases and points the user at the download page.
/// ponytail: a sandboxed app can't swap its own bundle; switch to Sparkle if hands-off installs matter.
@Observable
final class Updater {
    enum Status { case idle, checking, upToDate, available(String, URL), failed }
    private(set) var status = Status.idle
    private var lastCheck = Date.distantPast

    static let current = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0"
    private static let latestURL = URL(string: "https://api.github.com/repos/mrbarkan/Outatime/releases/latest")!

    nonisolated private struct Release: Decodable {
        let tag_name: String
        let html_url: URL
    }

    var available: (version: String, url: URL)? {
        if case let .available(v, url) = status { (v, url) } else { nil }
    }

    /// Throttled to every 6 h unless forced.
    func check(force: Bool = false) async {
        guard force || Date.now.timeIntervalSince(lastCheck) > 6 * 3600 else { return }
        lastCheck = .now
        status = .checking
        do {
            let (data, _) = try await URLSession.shared.data(from: Self.latestURL)
            let r = try JSONDecoder().decode(Release.self, from: data)
            let v = Self.version(fromTag: r.tag_name)
            status = Self.isNewer(v, than: Self.current) ? .available(v, r.html_url) : .upToDate
        } catch {
            status = .failed
        }
    }

    nonisolated static func version(fromTag tag: String) -> String {
        tag.hasPrefix("v") ? String(tag.dropFirst()) : tag
    }

    nonisolated static func isNewer(_ a: String, than b: String) -> Bool {
        a.compare(b, options: .numeric) == .orderedDescending
    }
}
