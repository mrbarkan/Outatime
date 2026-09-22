import SwiftUI
import Carbon.HIToolbox
import UserNotifications

@main
struct OutatimeApp: App {
    @State private var store: Store
    @State private var updater = Updater()
    @AppStorage("language") private var language = Language.system

    init() {
        Appearance(rawValue: UserDefaults.standard.string(forKey: "appearance") ?? "")?.apply()
        let store = Store()
        store.rollOver()
        _store = State(initialValue: store)
        HotKeys.install { a in store.running?.activity == a ? store.stop() : store.start(a) }
        HotKeys.setEnabled(UserDefaults.standard.object(forKey: "globalShortcuts") as? Bool ?? true)
    }

    var body: some Scene {
        MenuBarExtra {
            MenuPanel().environment(store).environment(\.locale, language.locale)
        } label: {
            MenuBarLabel(store: store).environment(\.locale, language.locale)
        }
        .menuBarExtraStyle(.window)

        Window("Logbook", id: "editor") {
            EditorView().environment(store).environment(\.locale, language.locale)
        }
        .defaultSize(width: 900, height: 560)

        Settings {
            SettingsView().environment(updater).environment(store).environment(\.locale, language.locale)
        }
    }
}

/// Icon + elapsed h:mm, or what's left of today's target. Ticks every 30 s; menu bar labels don't reliably redraw with
/// `Text(style: .timer)`. The tick also cuts timers at midnight and sends the long-timer reminder.
struct MenuBarLabel: View {
    let store: Store
    @AppStorage("menuBarStyle") private var style = MenuBarStyle.iconAndTime
    @AppStorage("targetHours") private var targetHours = 8.0
    @AppStorage("excludedFromTarget") private var excluded = Activity.defaultExcluded
    @AppStorage("remindAfterHours") private var remindAfter = 10.0
    @State private var tick = Date.now

    var body: some View {
        Group {
            if style == .remaining {
                let t = store.target(hours: targetHours, excluded: excluded)
                let left = store.totals(on: .now).worked(excluding: excluded) - t.owed(on: .now)
                HStack(spacing: 4) {
                    Image(systemName: store.running?.activity.symbol ?? "clock")
                    Text((left >= 0 ? "+" : "−") + abs(left).hm)
                }
            } else if let running = store.running {
                HStack(spacing: 4) {
                    if style != .time { Image(systemName: running.activity.symbol) }
                    if style != .icon { Text(Date.now.timeIntervalSince(store.runningSince ?? running.start).hm) }
                }
            } else {
                Image(systemName: "clock")  // idle always shows the icon; a bare "0h 00m" is meaningless
            }
        }
        .onReceive(Timer.publish(every: 30, on: .main, in: .common).autoconnect()) {
            tick = $0
            store.rollOver()
            Reminder.check(since: store.runningSince, after: remindAfter)
        }
        .id(tick)
    }
}

/// One notification per running stretch once it passes `after` hours — the timer was probably forgotten.
enum Reminder {
    private static var sent: Date?

    static func check(since: Date?, after hours: Double, now: Date = .now) {
        guard let since, hours > 0, now.timeIntervalSince(since) >= hours * 3600, sent != since else { return }
        sent = since
        let title = String(localized: "Still tracking?")
        let body = String(localized: "The timer has been running for \(now.timeIntervalSince(since).hm).")
        Task {
            let center = UNUserNotificationCenter.current()
            guard (try? await center.requestAuthorization(options: [.alert, .sound])) == true else { return }
            let content = UNMutableNotificationContent()
            content.title = title
            content.body = body
            try? await center.add(UNNotificationRequest(identifier: "long-timer", content: content, trigger: nil))
        }
    }
}

/// ⌃⌥⌘W toggles Work and ⌃⌥⌘B toggles Break from any app. Carbon hot keys need no Accessibility permission.
enum HotKeys {
    private static let keys: [(Activity, Int)] = [(.work, kVK_ANSI_W), (.break, kVK_ANSI_B)]
    private static var action: (Activity) -> Void = { _ in }
    private static var refs: [EventHotKeyRef] = []

    static func install(_ action: @escaping (Activity) -> Void) {
        self.action = action
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, event, _ in
            var id = EventHotKeyID()
            GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                              nil, MemoryLayout<EventHotKeyID>.size, nil, &id)
            MainActor.assumeIsolated { HotKeys.action(HotKeys.keys[Int(id.id)].0) }
            return noErr
        }, 1, &spec, nil, nil)
    }

    static func setEnabled(_ on: Bool) {
        refs.forEach { UnregisterEventHotKey($0) }
        refs = []
        guard on else { return }
        for (i, key) in keys.enumerated() {
            var ref: EventHotKeyRef?
            RegisterEventHotKey(UInt32(key.1), UInt32(cmdKey | optionKey | controlKey), EventHotKeyID(signature: 0x4F555441, id: UInt32(i)),
                                GetApplicationEventTarget(), 0, &ref)
            if let ref { refs.append(ref) }
        }
    }
}
