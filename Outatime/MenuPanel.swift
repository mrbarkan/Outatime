import SwiftUI
import ServiceManagement

struct MenuPanel: View {
    @Environment(Store.self) private var store
    @Environment(\.openWindow) private var openWindow
    @Environment(\.dismiss) private var dismiss
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

            if store.running != nil {
                Button("Stop", systemImage: "stop.fill") { store.stop() }
                    .buttonStyle(.glass)
                    .frame(maxWidth: .infinity)
            }

            totals
            Divider()
            footer
        }
        .padding(14)
        .frame(width: 300)
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
        let balance = t[.work, default: 0] + t[.extra, default: 0] - targetHours * 3600
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
            HStack {
                Button("Edit Days…") {
                    dismiss()
                    openWindow(id: "editor")
                    // openWindow doesn't raise an already-open window while the menu bar panel is key.
                    DispatchQueue.main.async {
                        NSApp.activate()
                        NSApp.windows.first { $0.identifier?.rawValue == "editor" }?.makeKeyAndOrderFront(nil)
                    }
                }
                Spacer()
                Menu("Export") {
                    let month = Date.now.startOfMonth
                    let name = month.formatted(.dateTime.year().month(.twoDigits))
                    Button("This Month — Daily Summary…") {
                        saveCSV(CSV.daily(store.entries(inMonth: month), targetHours: targetHours), suggestedName: "Outatime \(name) daily.csv")
                    }
                    Button("This Month — Entries…") {
                        saveCSV(CSV.entries(store.entries(inMonth: month)), suggestedName: "Outatime \(name) entries.csv")
                    }
                }
                .fixedSize()
            }
            HStack {
                LaunchAtLoginToggle()
                Spacer()
                Button("Quit") { NSApp.terminate(nil) }.keyboardShortcut("q")
            }
            .controlSize(.small)
        }
    }
}

private struct ActivityButton: View {
    @Environment(Store.self) private var store
    let activity: Activity
    init(_ activity: Activity) { self.activity = activity }

    var body: some View {
        let active = store.running?.activity == activity
        Button { store.start(activity) } label: {
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

private struct LaunchAtLoginToggle: View {
    @State private var enabled = SMAppService.mainApp.status == .enabled

    var body: some View {
        Toggle("Open at Login", isOn: $enabled)
            .toggleStyle(.checkbox)
            .onChange(of: enabled) { _, on in
                try? on ? SMAppService.mainApp.register() : SMAppService.mainApp.unregister()
                enabled = SMAppService.mainApp.status == .enabled
            }
    }
}
