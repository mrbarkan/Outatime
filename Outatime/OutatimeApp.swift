import SwiftUI

@main
struct OutatimeApp: App {
    @State private var store = Store()
    @State private var updater = Updater()
    @AppStorage("language") private var language = Language.system

    init() {
        Appearance(rawValue: UserDefaults.standard.string(forKey: "appearance") ?? "")?.apply()
    }

    var body: some Scene {
        MenuBarExtra {
            MenuPanel().environment(store).environment(updater).environment(\.locale, language.locale)
        } label: {
            MenuBarLabel(store: store).environment(\.locale, language.locale)
        }
        .menuBarExtraStyle(.window)

        Window("Logbook", id: "editor") {
            EditorView().environment(store).environment(\.locale, language.locale)
        }
        .defaultSize(width: 900, height: 560)

        Settings {
            SettingsView().environment(updater).environment(\.locale, language.locale)
        }
    }
}

/// Icon + elapsed h:mm. Ticks every 30 s; menu bar labels don't reliably redraw with `Text(style: .timer)`.
struct MenuBarLabel: View {
    let store: Store
    @AppStorage("menuBarStyle") private var style = MenuBarStyle.iconAndTime
    @State private var tick = Date.now

    var body: some View {
        Group {
            if let running = store.running {
                HStack(spacing: 4) {
                    if style != .time { Image(systemName: running.activity.symbol) }
                    if style != .icon { Text(running.duration.hm) }
                }
            } else {
                Image(systemName: "clock")  // idle always shows the icon; a bare "0h 00m" is meaningless
            }
        }
        .onReceive(Timer.publish(every: 30, on: .main, in: .common).autoconnect()) { tick = $0 }
        .id(tick)
    }
}
