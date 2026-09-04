import SwiftUI

@main
struct OutatimeApp: App {
    @State private var store = Store()

    var body: some Scene {
        MenuBarExtra {
            MenuPanel().environment(store)
        } label: {
            MenuBarLabel(store: store)
        }
        .menuBarExtraStyle(.window)

        Window("Outatime", id: "editor") {
            EditorView().environment(store)
        }
        .defaultSize(width: 900, height: 560)
    }
}

/// Icon + elapsed h:mm. Ticks every 30 s; menu bar labels don't reliably redraw with `Text(style: .timer)`.
struct MenuBarLabel: View {
    let store: Store
    @State private var tick = Date.now

    var body: some View {
        Group {
            if let running = store.running {
                HStack(spacing: 4) {
                    Image(systemName: running.activity.symbol)
                    Text(running.duration.hm)
                }
            } else {
                Image(systemName: "clock")
            }
        }
        .onReceive(Timer.publish(every: 30, on: .main, in: .common).autoconnect()) { tick = $0 }
        .id(tick)
    }
}
