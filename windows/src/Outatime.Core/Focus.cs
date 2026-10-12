namespace Outatime;

/// Tomato rounds that follow the tracker: Work or Extra runs a focus round, Break a break — a long one after every 4th
/// finished focus round. Anything else pauses it. Nothing is stored; the rounds are read off the entries.
public readonly record struct Pomodoro(double Focus = 25 * 60, double ShortBreak = 5 * 60, double LongBreak = 15 * 60, int LongEvery = 4)
{
    public Pomodoro() : this(25 * 60) { }

    public enum Phase { Focus, ShortBreak, LongBreak }

    /// `Number`: the focus round under way, or during a break the one just finished.
    public readonly record struct Round(Phase Phase, int Number, DateTimeOffset Start, DateTimeOffset End)
    {
        /// "12m" left, or "+3m" past the end.
        public string Countdown(DateTimeOffset? now = null)
        {
            var left = End.Since(now ?? Clock.Now);
            return left > 0 ? $"{(int)Math.Ceiling(left / 60)}m" : $"+{(int)(-left / 60)}m";
        }
    }

    /// The round under way with the tomato on since `since`, or null while the tracker is on anything else.
    public Round? RoundOf(IReadOnlyList<Entry> entries, DateTimeOffset since)
    {
        if (entries.Cast<Entry?>().FirstOrDefault(e => e!.Value.IsRunning) is not { } running) return null;
        // Back-to-back focus blocks (the switch to Extra at the target, a midnight cut) are one stretch.
        static bool Focusing(Activity a) => a is Activity.Work or Activity.Extra;
        var stretches = new List<(DateTimeOffset Start, DateTimeOffset? End)>();
        foreach (var e in entries.OrderBy(e => e.Start).Where(e => Focusing(e.Activity) && (e.End ?? DateTimeOffset.MaxValue) > since))
        {
            if (stretches.Count > 0 && stretches[^1].End is { } end && Math.Abs(end.Since(e.Start)) < 1)
                stretches[^1] = (stretches[^1].Start, e.End);
            else
                stretches.Add((e.Start > since ? e.Start : since, e.End));
        }
        // The notification fires on a whole second, so a click on it can land a moment short.
        var focus = Focus;
        var done = stretches.Count(s => s.End is { } end && end.Since(s.Start) >= focus - 5);
        if (Focusing(running.Activity) && stretches.Count > 0)
        {
            var start = stretches[^1].Start;
            return new Round(Phase.Focus, done + 1, start, start.Plus(Focus));
        }
        if (running.Activity != Activity.Break) return null;
        var breakStart = running.Start > since ? running.Start : since;
        var isLong = done > 0 && done % LongEvery == 0;
        return new Round(isLong ? Phase.LongBreak : Phase.ShortBreak, done, breakStart, breakStart.Plus(isLong ? LongBreak : ShortBreak));
    }
}

/// "Get up and stretch" every so often through an unbroken stretch of Work or Extra.
public static class Stretch
{
    public static int Reminders(Activity? activity, DateTimeOffset? since, double everyMinutes, DateTimeOffset now)
    {
        if (since is not { } s || everyMinutes <= 0 || activity is not (Activity.Work or Activity.Extra)) return 0;
        return (int)(now.Since(s) / (everyMinutes * 60));
    }
}

/// Decides when a reminder is due, once per stretch (or per count within one); the app sends the notification.
public sealed class ReminderGate
{
    (DateTimeOffset Since, int Count)? sent;

    /// The stretch reminder: true when the `count`th one for this stretch hasn't gone out yet.
    public bool Due(DateTimeOffset? since, int count)
    {
        if (count <= 0 || since is not { } s) return false;
        if (sent is { } x && x.Since == s && x.Count >= count) return false;
        sent = (s, count);
        return true;
    }
}
