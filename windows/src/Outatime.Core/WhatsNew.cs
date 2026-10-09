namespace Outatime;

/// Release notes, shown once after an update. Every release gets an entry here before it ships; titles and details are
/// translation keys like the rest of the app's text.
public static class WhatsNew
{
    public sealed record Item(string Icon, string Color, string Title, string Detail);
    public sealed record Release(string Version, IReadOnlyList<Item> New, IReadOnlyList<Item> Fixed)
    {
        public Release(string version) : this(version, [], []) { }
    }

    /// Newest first.
    public static readonly IReadOnlyList<Release> Releases =
    [
        new("1.0", [
            new("Window", "#0A84FF", "Outatime for Windows",
                "Everything from the Mac: the tray panel, the Logbook, clients, objectives, the tomato timer and Excel reports. Your data.json moves between the two."),
        ], []),
    ];

    static int Compare(string a, string b)
    {
        int[] Parts(string v) => v.Split('-')[0].Split('.').Select(p => int.TryParse(p, out var i) ? i : 0).ToArray();
        var (x, y) = (Parts(a), Parts(b));
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            var c = (i < x.Length ? x[i] : 0).CompareTo(i < y.Length ? y[i] : 0);
            if (c != 0) return c;
        }
        return 0;
    }

    /// Releases after `lastSeen` up to `current`, newest first. With no `lastSeen` it's either a fresh install (no data:
    /// nothing to catch up on) or the first launch of a version with notes after an import.
    public static List<Release> Unseen(IReadOnlyList<Release> releases, string? lastSeen, string current, bool hasData)
    {
        if (lastSeen == null && !hasData) return [];
        var from = lastSeen ?? "0";
        return releases.Where(r => Compare(r.Version, from) > 0 && Compare(r.Version, current) <= 0).ToList();
    }

    /// The version the window catches up from ("0": from before What's New existed), or null when there's nothing new.
    public static string? CatchUpFrom(IReadOnlyList<Release> releases, string? lastSeen, string current, bool hasData)
    {
        if (!RecordsLastSeen(current)) return null;
        return Unseen(releases, lastSeen, current, hasData).Count == 0 ? null : lastSeen ?? "0";
    }

    /// Betas ("1.3-beta.1") neither show notes nor count as seen: the notes come with the release itself.
    public static bool RecordsLastSeen(string version) => !version.Contains('-');
}
