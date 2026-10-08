import SwiftUI
import ServiceManagement

enum Appearance: String, CaseIterable {
    case system, light, dark

    var label: LocalizedStringKey {
        switch self {
        case .system: "System"
        case .light: "Light"
        case .dark: "Dark"
        }
    }

    func apply() {
        NSApplication.shared.appearance = switch self {
        case .system: nil
        case .light: NSAppearance(named: .aqua)
        case .dark: NSAppearance(named: .darkAqua)
        }
    }
}

enum Language: String, CaseIterable {
    case system, en, es, ptBR = "pt-BR", fr, de, it, ja, zhHans = "zh-Hans"

    /// Native names on purpose: you should be able to find your language when the UI is in one you can't read.
    var label: LocalizedStringKey {
        switch self {
        case .system: "System"
        case .en: "English"
        case .es: "Español"
        case .ptBR: "Português (Brasil)"
        case .fr: "Français"
        case .de: "Deutsch"
        case .it: "Italiano"
        case .ja: "日本語"
        case .zhHans: "简体中文"
        }
    }

    var locale: Locale { self == .system ? .current : Locale(identifier: rawValue) }

    /// The manual in this language (docs/manual in the repo, served by GitHub Pages). On System, the first of the
    /// Mac's preferred languages the manual has: any Portuguese reads the Brazilian one, any Chinese the simplified one.
    nonisolated func manualURL(preferred: [String] = Locale.preferredLanguages) -> URL {
        let base = "https://mrbarkan.github.io/Outatime/manual/"
        let code = self != .system ? rawValue : preferred.lazy.compactMap { tag in
            Language.allCases.first { $0 != .system && $0.rawValue.split(separator: "-")[0] == tag.split(separator: "-")[0] }
        }.first?.rawValue ?? "en"
        return URL(string: code == "en" ? base : base + code + "/")!
    }
}

enum MenuBarStyle: String, CaseIterable {
    case iconAndTime, icon, time, remaining

    var label: LocalizedStringKey {
        switch self {
        case .iconAndTime: "Icon and time"
        case .icon: "Icon only"
        case .time: "Time only"
        case .remaining: "Time left today"
        }
    }
}

/// Five short tabs, none taller than a handful of rows.
struct SettingsView: View {
    var body: some View {
        TabView {
            Tab("General", systemImage: "gearshape") { GeneralSettings() }
            Tab("Target", systemImage: "target") { TargetSettings() }
            Tab("Focus", systemImage: "timer") { FocusSettings() }
            Tab("Clients", systemImage: "person.2") { ClientSettings() }
            Tab("Updates", systemImage: "arrow.down.circle") { UpdateSettings() }
        }
        .scenePadding(.minimum, edges: .horizontal)
        .frame(width: 420)
    }
}

private extension View {
    func settingsForm() -> some View { formStyle(.grouped).scrollDisabled(true).fixedSize(horizontal: false, vertical: true) }
}

private struct GeneralSettings: View {
    @AppStorage("appearance") private var appearance = Appearance.system
    @AppStorage("language") private var language = Language.system
    @AppStorage("menuBarStyle") private var menuBarStyle = MenuBarStyle.iconAndTime
    @AppStorage("coloredIcon") private var coloredIcon = true
    @AppStorage("globalShortcuts") private var globalShortcuts = true
    @State private var launchAtLogin = SMAppService.mainApp.status == .enabled

    var body: some View {
        Form {
            Picker("Appearance", selection: $appearance) {
                ForEach(Appearance.allCases, id: \.self) { Text($0.label) }
            }
            .pickerStyle(.segmented)
            .onChange(of: appearance) { appearance.apply() }

            Picker("Language", selection: $language) {
                ForEach(Language.allCases, id: \.self) { Text($0.label) }
            }

            Picker("Menu bar", selection: $menuBarStyle) {
                ForEach(MenuBarStyle.allCases, id: \.self) { Text($0.label) }
            }
            Toggle("Colored menu bar icon", isOn: $coloredIcon)

            Toggle("Open at Login", isOn: $launchAtLogin)
                .onChange(of: launchAtLogin) { _, on in
                    try? on ? SMAppService.mainApp.register() : SMAppService.mainApp.unregister()
                    launchAtLogin = SMAppService.mainApp.status == .enabled
                }

            Toggle(isOn: $globalShortcuts) {
                Text("Global shortcuts")
                Text("⌃⌥⌘W starts or stops Work, ⌃⌥⌘B Break")
            }
            .onChange(of: globalShortcuts) { HotKeys.setEnabled(globalShortcuts) }
        }
        .settingsForm()
    }
}

private struct TargetSettings: View {
    @Environment(Store.self) private var store
    @AppStorage("targetHours") private var targetHours = 8.0
    @AppStorage("excludedFromTarget") private var excluded = Activity.defaultExcluded
    @AppStorage("bankSince") private var bankSince = 0.0  // 0: since the first entry
    @AppStorage("autoExtra") private var autoExtra = true

    var body: some View {
        Form {
            Section {
                Stepper("Daily target \(targetHours.formatted())h", value: $targetHours, in: 0...16, step: 0.5)
                // Work and extra always count; the rest is the user's call. One row of icon toggles keeps the tab short.
                LabeledContent("Counts toward the target") {
                    HStack(spacing: 4) {
                        ForEach([Activity.break, .lunch, .travel, .outOfOffice]) { a in
                            Toggle(isOn: Binding(get: { !excluded.split(separator: ",").contains(Substring(a.rawValue)) },
                                                 set: { on in
                                let out = excluded.split(separator: ",").map(String.init).filter { $0 != a.rawValue }
                                excluded = (on ? out : out + [a.rawValue]).joined(separator: ",")
                            })) { Label(a.label, systemImage: a.symbol) }
                            .toggleStyle(.button).labelStyle(.iconOnly)
                            .help(a.label)
                        }
                    }
                }
                DatePicker("Hours bank since", selection: Binding(
                    get: { bankSince > 0 ? Date(timeIntervalSinceReferenceDate: bankSince) : store.target(hours: 0, excluded: "").since },
                    set: { bankSince = Calendar.current.startOfDay(for: $0).timeIntervalSinceReferenceDate }), displayedComponents: .date)
                Toggle("Switch to Extra after the target", isOn: $autoExtra)
            } footer: {
                Text("Weekends and days off owe nothing; mark a day off from the Logbook toolbar.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .settingsForm()
    }
}

/// The tomato timer and the reminders that get you out of the chair.
private struct FocusSettings: View {
    @AppStorage("focusMinutes") private var focus = 25
    @AppStorage("shortBreakMinutes") private var shortBreak = 5
    @AppStorage("longBreakMinutes") private var longBreak = 15
    @AppStorage("stretchMinutes") private var stretchEvery = 50
    @AppStorage("remindAfterHours") private var remindAfter = 10.0

    var body: some View {
        Form {
            Section {
                Stepper("Focus \(focus) min", value: $focus, in: 5...90, step: 5)
                Stepper("Short break \(shortBreak) min", value: $shortBreak, in: 1...30)
                Stepper("Long break \(longBreak) min", value: $longBreak, in: 5...60, step: 5)
            } header: {
                Text("Tomato timer")
            } footer: {
                Text("Turn it on with the timer button in the menu. Each round ends with a notification that can switch the tracker for you.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section("Reminders") {
                Stepper(value: $stretchEvery, in: 0...120, step: 5) {
                    Text(stretchEvery > 0 ? "Remind me to stretch every \(stretchEvery) min" : "No stretch reminder")
                }
                Stepper(value: $remindAfter, in: 0...24, step: 1) {
                    Text(remindAfter > 0 ? "Remind me when a timer runs \(remindAfter.formatted())h" : "No long-timer reminder")
                }
            }
        }
        .settingsForm()
    }
}

private struct ClientSettings: View {
    @Environment(Store.self) private var store
    @State private var newClient = ""

    var body: some View {
        Form {
            Section {
                ForEach(store.activeProfiles) { p in
                    HStack {
                        TextField("Name", text: Binding(get: { p.name }, set: { store.rename(p.id, to: $0) })).labelsHidden()
                        Button("Remove", systemImage: "minus.circle") { store.removeProfile(p.id) }
                            .labelStyle(.iconOnly).buttonStyle(.borderless)
                    }
                }
                HStack {
                    TextField("New client", text: $newClient).labelsHidden().onSubmit(addClient)
                    Button("Add", action: addClient).disabled(newClient.trimmingCharacters(in: .whitespaces).isEmpty)
                }
            } footer: {
                Text("Pick the client in the menu. Work, Extra and Travel are tracked for it.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .settingsForm()
    }

    private func addClient() {
        if store.addProfile(newClient) != nil { newClient = "" }
    }
}

private struct UpdateSettings: View {
    @Environment(Updater.self) private var updater
    @AppStorage(Updater.betaKey) private var beta = false

    var body: some View {
        Form {
            LabeledContent("Version \(Updater.current)") {
                Button("Check for Updates…") { updater.check() }
            }
            Toggle(isOn: $beta) {
                Text("Get beta updates")
                Text("Early builds of the next version. They may have rough edges.")
            }
            .onChange(of: beta) { if beta { updater.check() } }
        }
        .settingsForm()
    }
}

/// The manual is a web page, so it can change between releases.
func showManual() {
    NSWorkspace.shared.open(Language(rawValue: UserDefaults.standard.string(forKey: "language") ?? "")?.manualURL() ?? Language.system.manualURL())
}

/// Back to a menu bar-only app when a window closes, unless the Logbook is still open: it keeps the app regular.
func returnToMenuBar() {
    if !NSApp.windows.contains(where: { $0.identifier?.rawValue.hasPrefix("editor") == true && $0.isVisible }) {
        NSApp.setActivationPolicy(.accessory)
    }
}

/// An LSUIElement app can't take focus from the frontmost app; become a regular app while the window is open (the
/// window flips back on close), then raise it ourselves — openWindow won't if it already exists.
func show(window id: String, value: String? = nil, _ openWindow: OpenWindowAction) {
    NSApp.setActivationPolicy(.regular)
    NSApp.activate()
    if let value { openWindow(id: id, value: value) } else { openWindow(id: id) }
    DispatchQueue.main.async {
        if let w = NSApp.windows.first(where: { $0.identifier?.rawValue.hasPrefix(id) == true }) {
            w.deminiaturize(nil)
            w.makeKeyAndOrderFront(nil)
        }
    }
}
