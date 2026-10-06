import SwiftUI
import Carbon.HIToolbox

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
        Notify.install { store.start($0) }
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

        WindowGroup("What's New", id: "whats-new", for: String.self) { $since in
            WhatsNewView(since: since ?? "0").environment(\.locale, language.locale)
        }
        .windowStyle(.hiddenTitleBar)
        .windowResizability(.contentSize)
        .defaultPosition(.center)
        .restorationBehavior(.disabled)
        .commandsRemoved()  // opens by itself after an update, never from a menu

        Settings {
            SettingsView().environment(updater).environment(store).environment(\.locale, language.locale)
        }
    }
}

/// Icon + elapsed h:mm, what's left of today's target, or what's left of the tomato round. Ticks every 30 s; menu bar
/// labels don't reliably redraw with `Text(style: .timer)`. The tick also cuts timers at midnight and sends the
/// long-timer and stretch reminders.
struct MenuBarLabel: View {
    let store: Store
    @AppStorage("menuBarStyle") private var style = MenuBarStyle.iconAndTime
    @AppStorage("coloredIcon") private var colored = true
    @AppStorage("targetHours") private var targetHours = 8.0
    @AppStorage("excludedFromTarget") private var excluded = Activity.defaultExcluded
    @AppStorage("remindAfterHours") private var remindAfter = 10.0
    @AppStorage("stretchMinutes") private var stretchEvery = 50
    @AppStorage("autoExtra") private var autoExtra = true
    @State private var tick = Date.now
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        Group {
            if let round = store.tomatoRound, style != .icon {
                HStack(spacing: 4) {
                    if style != .time { icon(store.running?.activity) }
                    Text(round.countdown())
                }
            } else if style == .remaining {
                let t = store.target(hours: targetHours, excluded: excluded)
                let left = store.totals(on: .now).worked(excluding: excluded) - t.owed(on: .now)
                HStack(spacing: 4) {
                    icon(store.running?.activity)
                    Text((left >= 0 ? "+" : "−") + abs(left).hm)
                }
            } else if let running = store.running {
                HStack(spacing: 4) {
                    if style != .time { icon(running.activity) }
                    if style != .icon { Text(Date.now.timeIntervalSince(store.runningSince ?? running.start).hm) }
                }
            } else {
                Image(systemName: "clock")  // idle always shows the icon; a bare "0h 00m" is meaningless
            }
        }
        .onReceive(Timer.publish(every: 30, on: .main, in: .common).autoconnect()) {
            tick = $0
            store.rollOver()
            if autoExtra, store.shiftToExtra(store.target(hours: targetHours, excluded: excluded)) {
                Notify.post("extra", title: String(localized: "Daily target reached"), body: String(localized: "Now tracking Extra."))
            }
            Reminder.check(since: store.runningSince, after: remindAfter)
            // The tomato's breaks already get you up.
            Stretch.check(activity: store.running?.activity, since: store.seatedSince,
                          every: store.tomatoSince == nil ? Double(stretchEvery) : 0)
        }
        .id(tick)
        .task(id: store.tomatoRound) { Tomato.schedule(store.tomatoRound) }  // outside .id(tick), so it runs on change only
        .task { WhatsNew.openIfUpdated(hasData: !store.entries.isEmpty) { show(window: "whats-new", value: $0, openWindow) } }
    }

    /// Menu bar images are drawn as templates (one color); a non-template image keeps the activity's color.
    private func icon(_ activity: Activity?) -> Image {
        guard let activity else { return Image(systemName: "clock") }
        let config = NSImage.SymbolConfiguration(pointSize: 14, weight: .regular)
            .applying(.init(paletteColors: [NSColor(activity.color)]))
        guard colored, let image = NSImage(systemSymbolName: activity.symbol, accessibilityDescription: nil)?
            .withSymbolConfiguration(config) else { return Image(systemName: activity.symbol) }
        image.isTemplate = false
        return Image(nsImage: image)
    }
}

/// One notification per running stretch once it passes `after` hours — the timer was probably forgotten.
enum Reminder {
    private static var sent: Date?

    static func check(since: Date?, after hours: Double, now: Date = .now) {
        guard let since, hours > 0, now.timeIntervalSince(since) >= hours * 3600, sent != since else { return }
        sent = since
        Notify.post("long-timer", title: String(localized: "Still tracking?"),
                    body: String(localized: "The timer has been running for \(now.timeIntervalSince(since).hm)."))
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
