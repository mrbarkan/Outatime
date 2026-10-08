import SwiftUI

/// Who made this, which version it is and where to get help. Replaces the standard About panel, which has no room
/// for a support address.
struct AboutView: View {
    static let supportEmail = "opa@mrbarkan.com"
    static let build = Bundle.main.infoDictionary?["CFBundleVersion"] as? String ?? "0"

    var body: some View {
        VStack(spacing: 18) {
            VStack(spacing: 6) {
                Image(nsImage: NSApp.applicationIconImage).resizable().frame(width: 96, height: 96)
                Text(verbatim: "Outatime").font(.title.bold())
                Text("Version \(Updater.current) (\(Self.build))").font(.callout).foregroundStyle(.secondary).textSelection(.enabled)
                Text("A tiny menu bar time tracker for macOS.").multilineTextAlignment(.center)
            }

            VStack(spacing: 6) {
                Link(destination: Self.supportURL) {
                    Label("Contact Support", systemImage: "envelope").frame(maxWidth: .infinity)
                }
                .buttonStyle(.glassProminent).controlSize(.large)
                Text("Questions, bugs or ideas: \(Self.supportEmail)")
                    .font(.caption).foregroundStyle(.secondary).textSelection(.enabled)
            }

            HStack(spacing: 16) {
                Button("User Manual", action: showManual)
                Link("Release Notes", destination: URL(string: "https://github.com/mrbarkan/Outatime/releases")!)
                Link("Source Code", destination: URL(string: "https://github.com/mrbarkan/Outatime")!)
            }
            .buttonStyle(.link)

            Text(Bundle.main.infoDictionary?["NSHumanReadableCopyright"] as? String ?? "")
                .font(.caption).foregroundStyle(.secondary)
        }
        .padding(28)
        .frame(width: 340)
        .fixedSize(horizontal: false, vertical: true)
        .onAppear { NSApp.setActivationPolicy(.regular); NSApp.activate() }
        .onDisappear(perform: returnToMenuBar)
    }

    /// A new mail with the version and macOS filled in, the two things every support reply asks for first.
    static var supportURL: URL {
        var c = URLComponents()
        c.scheme = "mailto"
        c.path = supportEmail
        c.queryItems = [
            URLQueryItem(name: "subject", value: "Outatime \(Updater.current)"),
            URLQueryItem(name: "body", value: "\n\n—\nOutatime \(Updater.current) (\(build)), macOS \(ProcessInfo.processInfo.operatingSystemVersionString)"),
        ]
        return c.url!
    }
}
