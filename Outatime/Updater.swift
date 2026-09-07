import Sparkle

/// Sparkle owns the whole update flow: it checks the signed appcast published with
/// each GitHub release, then downloads and installs in place.
/// ponytail: no delegate and no UI of our own — the standard controller's is fine.
@Observable
final class Updater {
    static let current = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0"

    private let controller = SPUStandardUpdaterController(
        startingUpdater: true, updaterDelegate: nil, userDriverDelegate: nil)

    func check() { controller.checkForUpdates(nil) }
}
