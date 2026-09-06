import SwiftUI

struct MenuPanel: View {
    @Environment(Store.self) private var store
    @Environment(Updater.self) private var updater
    @Environment(\.openWindow) private var openWindow
    @Environment(\.dismiss) private var dismiss
    @Environment(\.openSettings) private var openSettings
    @AppStorage("targetHours") private var targetHours = 8.0

    var body: some View {
        @Bindable var store = store
        VStack(alignment: .leading, spacing: 14) {
            status
            TextField("Tag (optional)", text: $store.currentTag)
                .textFieldStyle(.roundedBorder)

            GlassEffectContainer(spacing: 10) {
                Grid(horizontalSpacing: 10, verticalSpacing: 10) {
                    GridRow { ActivityButton(.work); ActivityButton(.break) }
                    GridRow { ActivityButton(.lunch); ActivityButton(.extra) }
                }
            }

            totals
            Divider()
            footer
        }
        .padding(14)
        .frame(width: 300)
        .task { await updater.check() }
    }

    private var status: some View {
        HStack {
            if let running = store.running {
                Image(systemName: running.activity.symbol).foregroundStyle(running.activity.color)
                Text(running.activity.label).fontWeight(.semibold)
                Spacer()
                Text(running.start, style: .timer).monospacedDigit().foregroundStyle(.secondary)
            } else {
                Image(systemName: "clock").foregroundStyle(.secondary)
                Text("Not tracking").foregroundStyle(.secondary)
            }
        }
        .font(.title3)
    }

    private var totals: some View {
        let t = store.totals(on: .now)
        let balance = t.worked - targetHours * 3600
        return VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 12) {
                ForEach(Activity.allCases) { a in
                    HStack(spacing: 4) {
                        Image(systemName: a.symbol).foregroundStyle(a.color)
                        Text(t[a, default: 0].hm).monospacedDigit()
                    }
                }
            }
            .font(.caption)
            HStack {
                Text("Today").foregroundStyle(.secondary)
                Spacer()
                Text((balance >= 0 ? "+" : "−") + abs(balance).hm)
                    .monospacedDigit()
                    .foregroundStyle(balance >= 0 ? .green : .secondary)
            }
            .font(.caption)
        }
    }

    private var footer: some View {
        VStack(spacing: 8) {
            if let (version, url) = updater.available {
                Button("Update to \(version)…", systemImage: "arrow.down.circle.fill") { NSWorkspace.shared.open(url) }
                    .buttonStyle(.glassProminent)
                    .frame(maxWidth: .infinity)
            }
            HStack(spacing: 2) {
                Button("Logbook", systemImage: "calendar", action: showLogbook)
                Menu("Export", systemImage: "square.and.arrow.up") {
                    let month = Date.now.startOfMonth
                    let name = month.formatted(.dateTime.year().month(.twoDigits))
                    Button("This Month — Daily Summary…") {
                        saveCSV(CSV.daily(store.entries(inMonth: month), targetHours: targetHours), suggestedName: "Outatime \(name) daily.csv")
                    }
                    Button("This Month — Entries…") {
                        saveCSV(CSV.entries(store.entries(inMonth: month)), suggestedName: "Outatime \(name) entries.csv")
                    }
                }
                Spacer()
                Menu {
                    Button("About", action: showAbout)
                    Button("Settings…") {
                        dismiss()
                        NSApp.activate()
                        openSettings()
                    }
                    .keyboardShortcut(",")
                    Divider()
                    Button("Quit") { NSApp.terminate(nil) }.keyboardShortcut("q")
                } label: {
                    Image(systemName: "ellipsis.circle")
                }
                .menuIndicator(.hidden)
            }
            .buttonStyle(.accessoryBar)
            .menuStyle(.button)
            .fixedSize(horizontal: false, vertical: true)
        }
    }

    private func showLogbook() {
        dismiss()
        // An LSUIElement app can't take focus from the frontmost app; become a regular app while the Logbook is open
        // (EditorView flips back on close), then raise the window ourselves — openWindow won't if it already exists.
        NSApp.setActivationPolicy(.regular)
        NSApp.activate()
        openWindow(id: "editor")
        DispatchQueue.main.async {
            if let w = NSApp.windows.first(where: { $0.identifier?.rawValue.hasPrefix("editor") == true }) {
                w.deminiaturize(nil)
                w.makeKeyAndOrderFront(nil)
            }
        }
    }
}

private struct ActivityButton: View {
    @Environment(Store.self) private var store
    let activity: Activity
    init(_ activity: Activity) { self.activity = activity }

    var body: some View {
        let active = store.running?.activity == activity
        Button { active ? store.stop() : store.start(activity) } label: {
            VStack(spacing: 4) {
                Image(systemName: activity.symbol).font(.title2)
                    .foregroundStyle(active ? AnyShapeStyle(.primary) : AnyShapeStyle(activity.color))
                Text(activity.label).font(.callout)
            }
            .frame(maxWidth: .infinity, minHeight: 56)
        }
        .modifier(GlassStyle(prominent: active, color: activity.color))
    }
}

private struct GlassStyle: ViewModifier {
    let prominent: Bool
    let color: Color
    func body(content: Content) -> some View {
        if prominent { content.buttonStyle(.glassProminent).tint(color) } else { content.buttonStyle(.glass) }
    }
}
