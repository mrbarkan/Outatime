import Sparkle

/// Sparkle owns the whole update flow: it checks the signed appcast published with
/// each GitHub release, then downloads and installs in place. Opting into betas also
/// accepts appcast items on the "beta" channel.
@Observable
final class Updater: NSObject, SPUUpdaterDelegate {
    static let current = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0"
    static let betaKey = "betaUpdates"

    @ObservationIgnored private var controller: SPUStandardUpdaterController?

    override init() {
        super.init()
        // The copy hosting the unit tests doesn't check for updates.
        controller = SPUStandardUpdaterController(startingUpdater: !OutatimeApp.isTestHost, updaterDelegate: self, userDriverDelegate: nil)
    }

    func check() { controller?.checkForUpdates(nil) }

    nonisolated func allowedChannels(for updater: SPUUpdater) -> Set<String> {
        UserDefaults.standard.bool(forKey: Self.betaKey) ? ["beta"] : []
    }
}
