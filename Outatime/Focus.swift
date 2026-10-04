import SwiftUI
import UserNotifications

/// Tomato rounds that follow the tracker: Work or Extra runs a focus round, Break a break — a long one after every 4th
/// finished focus round. Anything else pauses it. Nothing is stored; the rounds are read off the entries.
nonisolated struct Pomodoro {
    var focus: TimeInterval = 25 * 60
    var shortBreak: TimeInterval = 5 * 60
    var longBreak: TimeInterval = 15 * 60
    var longEvery = 4

    enum Phase { case focus, shortBreak, longBreak }

    struct Round: Hashable {
        var phase: Phase
        var number: Int  // the focus round under way, or during a break the one just finished
        var start: Date
        var end: Date

        /// "12m" left, or "+3m" past the end.
        func countdown(now: Date = .now) -> String {
            let left = end.timeIntervalSince(now)
            return left > 0 ? "\(Int((left / 60).rounded(.up)))m" : "+\(Int(-left / 60))m"
        }
    }

    static var current: Pomodoro {
        func minutes(_ key: String, _ fallback: Int) -> TimeInterval {
            TimeInterval(UserDefaults.standard.object(forKey: key) as? Int ?? fallback) * 60
        }
        return Pomodoro(focus: minutes("focusMinutes", 25), shortBreak: minutes("shortBreakMinutes", 5),
                        longBreak: minutes("longBreakMinutes", 15))
    }

    /// The round under way with the tomato on since `since`, or nil while the tracker is on anything else.
    func round(_ entries: [Entry], since: Date) -> Round? {
        guard let running = entries.first(where: \.isRunning) else { return nil }
        // Back-to-back focus blocks (the switch to Extra at the target, a midnight cut) are one stretch.
        let focusing: Set<Activity> = [.work, .extra]
        var stretches: [(start: Date, end: Date?)] = []
        for e in entries.sorted(by: { $0.start < $1.start }) where focusing.contains(e.activity) && (e.end ?? .distantFuture) > since {
            if let end = stretches.last?.end, abs(end.timeIntervalSince(e.start)) < 1 {
                stretches[stretches.count - 1].end = e.end
            } else {
                stretches.append((max(e.start, since), e.end))
            }
        }
        // The notification fires on a whole second, so a click on it can land a moment short.
        let done = stretches.filter { s in s.end.map { $0.timeIntervalSince(s.start) >= focus - 5 } ?? false }.count
        if focusing.contains(running.activity), let start = stretches.last?.start {
            return Round(phase: .focus, number: done + 1, start: start, end: start + focus)
        }
        guard running.activity == .break else { return nil }
        let start = max(running.start, since)
        let long = done > 0 && done % longEvery == 0
        return Round(phase: long ? .longBreak : .shortBreak, number: done, start: start, end: start + (long ? longBreak : shortBreak))
    }
}

extension Store {
    var tomatoRound: Pomodoro.Round? { tomatoSince.flatMap { Pomodoro.current.round(entries, since: $0) } }

    /// Turning the tomato on starts Work.
    func toggleTomato() {
        if tomatoSince != nil { tomatoSince = nil; return }
        tomatoSince = .now
        start(.work)
    }
}

enum Tomato {
    /// One pending notification for the end of the round under way; none while paused or off.
    static func schedule(_ r: Pomodoro.Round?) {
        guard let r else { return Notify.cancel("tomato") }
        guard r.end > .now else { return }
        if r.phase == .focus {
            Notify.post("tomato", title: String(localized: "Time for a break"), body: String(localized: "Round \(r.number) done."),
                        category: "focus-done", at: r.end)
        } else {
            Notify.post("tomato", title: String(localized: "Break's over"), body: String(localized: "Ready for round \(r.number + 1)?"),
                        category: "break-done", at: r.end)
        }
    }
}

/// "Get up and stretch" every so often through an unbroken stretch of Work or Extra.
enum Stretch {
    private static var sent: (since: Date, count: Int)?

    nonisolated static func reminders(activity: Activity?, since: Date?, every minutes: Double, now: Date) -> Int {
        guard let since, minutes > 0, activity == .work || activity == .extra else { return 0 }
        return Int(now.timeIntervalSince(since) / (minutes * 60))
    }

    static func check(activity: Activity?, since: Date?, every minutes: Double, now: Date = .now) {
        let n = reminders(activity: activity, since: since, every: minutes, now: now)
        guard n > 0, let since, sent.map({ $0.since != since || $0.count < n }) ?? true else { return }
        sent = (since, n)
        Notify.post("stretch", title: String(localized: "Time to stretch"),
                    body: String(localized: "You've been at it for \(now.timeIntervalSince(since).hm). Get up and move a little."))
    }
}

/// Local notifications. The delegate shows them while Outatime is active and runs their buttons.
final class Notify: NSObject, UNUserNotificationCenterDelegate {
    private static let shared = Notify()
    private static var onAction: (Activity) -> Void = { _ in }

    /// `action` gets the activity a notification button asks for: Break at the end of a focus round, Work after a break.
    static func install(_ action: @escaping (Activity) -> Void) {
        onAction = action
        let center = UNUserNotificationCenter.current()
        center.delegate = shared
        center.setNotificationCategories([
            UNNotificationCategory(identifier: "focus-done", actions: [UNNotificationAction(identifier: Activity.break.rawValue,
                                   title: String(localized: "Start Break"))], intentIdentifiers: []),
            UNNotificationCategory(identifier: "break-done", actions: [UNNotificationAction(identifier: Activity.work.rawValue,
                                   title: String(localized: "Back to Work"))], intentIdentifiers: []),
        ])
    }

    /// Now, or at `date`. A pending notification with the same `id` is replaced.
    static func post(_ id: String, title: String, body: String, category: String? = nil, at date: Date? = nil) {
        Task {
            let center = UNUserNotificationCenter.current()
            guard (try? await center.requestAuthorization(options: [.alert, .sound])) == true else { return }
            let content = UNMutableNotificationContent()
            content.title = title
            content.body = body
            content.sound = .default
            if let category { content.categoryIdentifier = category }
            let trigger = date.map {
                UNCalendarNotificationTrigger(dateMatching: Calendar.current.dateComponents([.year, .month, .day, .hour, .minute, .second], from: $0),
                                              repeats: false)
            }
            try? await center.add(UNNotificationRequest(identifier: id, content: content, trigger: trigger))
        }
    }

    static func cancel(_ id: String) {
        UNUserNotificationCenter.current().removePendingNotificationRequests(withIdentifiers: [id])
    }

    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification) async
        -> UNNotificationPresentationOptions { [.banner, .sound] }

    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse) async {
        guard let activity = Activity(rawValue: response.actionIdentifier) else { return }
        await MainActor.run { Notify.onAction(activity) }
    }
}
