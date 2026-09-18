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
    case system, en, es, ptBR = "pt-BR"

    /// Native names on purpose: you should be able to find your language when the UI is in one you can't read.
    var label: LocalizedStringKey {
        switch self {
        case .system: "System"
        case .en: "English"
        case .es: "Español"
        case .ptBR: "Português (Brasil)"
        }
    }

    var locale: Locale { self == .system ? .current : Locale(identifier: rawValue) }
}

enum MenuBarStyle: String, CaseIterable {
    case iconAndTime, icon, time

    var label: LocalizedStringKey {
        switch self {
        case .iconAndTime: "Icon and time"
        case .icon: "Icon only"
        case .time: "Time only"
        }
    }
}

struct SettingsView: View {
    @Environment(Updater.self) private var updater
    @AppStorage("appearance") private var appearance = Appearance.system
    @AppStorage("language") private var language = Language.system
    @AppStorage("menuBarStyle") private var menuBarStyle = MenuBarStyle.iconAndTime
    @AppStorage("targetHours") private var targetHours = 8.0
    @AppStorage("excludedFromTarget") private var excluded = Activity.defaultExcluded
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

            Toggle("Open at Login", isOn: $launchAtLogin)
                .onChange(of: launchAtLogin) { _, on in
                    try? on ? SMAppService.mainApp.register() : SMAppService.mainApp.unregister()
                    launchAtLogin = SMAppService.mainApp.status == .enabled
                }

            Section("Daily target") {
                Stepper("Target \(targetHours.formatted())h", value: $targetHours, in: 0...16, step: 0.5)
                // Work and extra always count; the rest is the user's call.
                ForEach([Activity.break, .lunch, .travel, .outOfOffice]) { a in
                    Toggle(isOn: Binding(get: { !excluded.split(separator: ",").contains(Substring(a.rawValue)) },
                                         set: { on in
                        let out = excluded.split(separator: ",").map(String.init).filter { $0 != a.rawValue }
                        excluded = (on ? out : out + [a.rawValue]).joined(separator: ",")
                    })) { Label(a.label, systemImage: a.symbol) }
                }
            }

            LabeledContent("Version \(Updater.current)") {
                Button("Check for Updates…") { updater.check() }
            }
        }
        .formStyle(.grouped)
        .frame(width: 380)
        .fixedSize(horizontal: false, vertical: true)
    }
}

/// LSUIElement apps don't come forward on their own; activate before showing any window.
func showAbout() {
    NSApp.activate()
    NSApp.orderFrontStandardAboutPanel(nil)
}
