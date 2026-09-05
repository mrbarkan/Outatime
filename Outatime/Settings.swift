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
    @AppStorage("appearance") private var appearance = Appearance.system
    @AppStorage("language") private var language = Language.system
    @AppStorage("menuBarStyle") private var menuBarStyle = MenuBarStyle.iconAndTime
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
