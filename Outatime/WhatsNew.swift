import SwiftUI

/// Release notes, shown once after an update. Every release gets an entry here before it ships (burn-it checks);
/// titles and details are catalog keys, so they're translated like the rest of the app.
enum WhatsNew {
    struct Item {
        var symbol: String
        var color: Color
        var title: LocalizedStringKey
        var detail: LocalizedStringKey
    }

    struct Release {
        var version: String
        var new: [Item] = []
        var fixed: [Item] = []
    }

    /// Newest first.
    static let releases = [
        Release(version: "1.2", new: [
            Item(symbol: "person.2", color: .indigo, title: "Clients",
                 detail: "Add your clients in Settings and pick one in the menu. Work, Extra and Travel are tracked for it, and switching clients splits the block right there."),
            Item(symbol: "tablecells", color: .green, title: "Reports per client",
                 detail: "Export a client's month for invoicing from the Export menu. Reports also gain a Client column and a Clients sheet; export a fresh master workbook so new months paste in cleanly."),
        ], fixed: [
            Item(symbol: "calendar", color: .blue, title: "Short blocks in the Logbook",
                 detail: "A block of a few minutes no longer covers the title of the one after it."),
        ]),
        Release(version: "1.1.1", fixed: [
            Item(symbol: "sparkles", color: .orange, title: "What's New window",
                 detail: "It opened empty after the last update. It now lists what changed, including everything new in 1.1."),
        ]),
        Release(version: "1.1", new: [
            Item(symbol: "timer", color: .red, title: "Tomato timer",
                 detail: "Turn it on from the menu for focus rounds and breaks. A notification at the end of each round switches Work and Break for you."),
            Item(symbol: "figure.flexibility", color: .green, title: "Stretch reminder",
                 detail: "A nudge to get up after every 50 minutes of work. Change it or turn it off in Settings."),
            Item(symbol: "paintpalette", color: .blue, title: "Colored menu bar",
                 detail: "The menu bar icon takes the color of what you're tracking: blue for Work, green for Break and so on."),
            Item(symbol: Activity.extra.symbol, color: Activity.extra.color, title: "Extra after the target",
                 detail: "Once your day reaches its target, Work carries on as Extra by itself. Weekends and days off count as Extra."),
        ]),
    ]

    /// Releases after `lastSeen` up to `current`, newest first. With no `lastSeen` it's either a fresh install (no data:
    /// nothing to catch up on) or an update from before What's New existed (show everything listed).
    static func unseen(_ releases: [Release], lastSeen: String?, current: String, hasData: Bool) -> [Release] {
        guard lastSeen != nil || hasData else { return [] }
        let after = { (a: String, b: String) in a.compare(b, options: .numeric) == .orderedDescending }
        let from = lastSeen == "1.1" && after(current, "1.1") ? "1.0.12" : lastSeen ?? "0"  // 1.1 opened the window empty
        return releases.filter { after($0.version, from) && !after($0.version, current) }
    }

    /// The version the window catches up from ("0": from before What's New existed), or nil when there's nothing new.
    static func catchUpFrom(_ releases: [Release], lastSeen: String?, current: String, hasData: Bool) -> String? {
        unseen(releases, lastSeen: lastSeen, current: current, hasData: hasData).isEmpty ? nil : lastSeen ?? "0"
    }

    /// At launch: open the window if this version (or one skipped on the way) has notes the user hasn't seen. The
    /// window gets the version to catch up from as its value; it builds its content before this runs, so shared state
    /// would reach it empty.
    static func openIfUpdated(hasData: Bool, open: (String) -> Void) {
        let defaults = UserDefaults.standard
        let from = catchUpFrom(releases, lastSeen: defaults.string(forKey: "lastSeenVersion"), current: Updater.current, hasData: hasData)
        defaults.set(Updater.current, forKey: "lastSeenVersion")
        if let from { open(from) }
    }
}

struct WhatsNewView: View {
    let since: String
    private var releases: [WhatsNew.Release] { WhatsNew.unseen(WhatsNew.releases, lastSeen: since, current: Updater.current, hasData: true) }
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 22) {
            HStack(spacing: 14) {
                Image(nsImage: NSApp.applicationIconImage).resizable().frame(width: 56, height: 56)
                Text("What's New in Outatime \(releases.first?.version ?? Updater.current)").font(.title2.bold())
            }
            let new = releases.flatMap(\.new), fixed = releases.flatMap(\.fixed)
            if !new.isEmpty { section("New", new) }
            if !fixed.isEmpty { section("Fixed", fixed) }
            Button { dismiss() } label: { Text("Continue").frame(maxWidth: .infinity) }
                .buttonStyle(.glassProminent).controlSize(.large)
                .keyboardShortcut(.defaultAction)
        }
        .padding(28)
        .frame(width: 440)
        .fixedSize(horizontal: false, vertical: true)
        .onAppear { NSApp.setActivationPolicy(.regular); NSApp.activate() }
        .onDisappear {
            // The Logbook keeps the app regular while it's open.
            if !NSApp.windows.contains(where: { $0.identifier?.rawValue.hasPrefix("editor") == true && $0.isVisible }) {
                NSApp.setActivationPolicy(.accessory)
            }
        }
    }

    private func section(_ title: LocalizedStringKey, _ items: [WhatsNew.Item]) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(title).font(.caption.weight(.semibold)).foregroundStyle(.secondary).textCase(.uppercase)
            ForEach(items.indices, id: \.self) { i in
                HStack(alignment: .top, spacing: 14) {
                    Image(systemName: items[i].symbol).font(.title2).foregroundStyle(items[i].color).frame(width: 32)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(items[i].title).fontWeight(.semibold)
                        Text(items[i].detail).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                    }
                }
            }
        }
    }
}
