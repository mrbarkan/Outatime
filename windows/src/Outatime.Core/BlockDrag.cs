namespace Outatime;

/// Drag math for Logbook timeline blocks, kept free of views so it can be tested.
public static class BlockDrag
{
    public enum Mode { Move, Start, End }
    public const double Grid = 300;
    public const double MinLength = 300;

    static bool Touching(DateTimeOffset? a, DateTimeOffset? b) => a is { } x && b is { } y && Math.Abs(x.Since(y)) < 1;

    /// The block sharing the dragged edge; it follows that edge on release. Blocks tracked back to back are
    /// milliseconds apart (and lose sub-seconds when saved), so anything within a second counts as shared.
    public static Entry? Neighbour(Entry e, Mode mode, IEnumerable<Entry> others) => mode switch
    {
        Mode.Start => others.Cast<Entry?>().FirstOrDefault(o => Touching(o!.Value.End, e.Start)),
        Mode.End => others.Cast<Entry?>().FirstOrDefault(o => Touching(o!.Value.Start, e.End)),
        _ => null,
    };

    /// `e` dragged by `delta` seconds. Edges within `magnet` seconds of another block's edge stick to it; on `drop`
    /// every other dragged edge lands on the 5-minute grid. Only the dragged edge moves — a resize never nudges the
    /// opposite edge, and a move keeps the duration. A resize stops at the next block; `together` (Alt) moves a shared
    /// border instead, and the neighbour follows on release.
    public static Entry Drag(Entry e, Mode mode, double delta, IReadOnlyList<Entry> others, DateTimeOffset dayStart,
                             double magnet, bool drop, bool together = false, DateTimeOffset? now = null)
    {
        var n = now ?? Clock.Now;
        // The shared neighbour moves with this edge, so its edges can't attract it — they'd pin it in place.
        var shared = together ? Neighbour(e, mode, others) : null;
        var walls = others.Where(o => o.Id != shared?.Id).ToList();
        DateTimeOffset? above = walls.Where(o => o.End is { } end && end <= e.Start.Plus(1)).Select(o => o.End).Max();
        DateTimeOffset? below = walls.Where(o => o.Start >= (e.End ?? n).Plus(-1)).Select(o => (DateTimeOffset?)o.Start).Min();
        var edges = walls.SelectMany(o => o.End is { } end ? new[] { o.Start, end } : [o.Start]).ToList();
        double? Pull(DateTimeOffset t) =>
            edges.Select(x => x.Since(t)).Where(x => Math.Abs(x) <= magnet).Cast<double?>().OrderBy(x => Math.Abs(x!.Value)).FirstOrDefault();
        DateTimeOffset Settle(DateTimeOffset t)
        {
            var offset = t.Since(dayStart);
            return t.Plus(Pull(t) ?? (drop ? Math.Round(offset / Grid) * Grid - offset : 0));
        }
        var dayEnd = Cal.AddDays(dayStart, 1);
        var d = e;
        switch (mode)
        {
            case Mode.Move:
                var dlt = Math.Max(delta, dayStart.Since(e.Start));
                if (e.End is { } end) dlt = Math.Min(dlt, dayEnd.Since(end));
                d.Start = d.Start.Plus(dlt);
                d.End = d.End?.Plus(dlt);
                // Whichever edge is nearer a magnet wins; otherwise the start settles.
                var pulls = new[] { (DateTimeOffset?)d.Start, d.End }.Where(x => x != null).Select(x => Pull(x!.Value))
                    .Where(x => x != null).OrderBy(x => Math.Abs(x!.Value)).ToList();
                var shift = pulls.Count > 0 ? pulls[0]!.Value : Settle(d.Start).Since(d.Start);
                d.Start = d.Start.Plus(shift);
                d.End = d.End?.Plus(shift);
                break;
            case Mode.Start:
                var floor = shared is { } s ? s.Start.Plus(MinLength) : above ?? dayStart;
                var start = Settle(e.Start.Plus(delta));
                if (start < floor) start = floor;
                var latest = (e.End ?? n).Plus(-MinLength);
                d.Start = start < latest ? start : latest;
                break;
            case Mode.End:
                var ceiling = shared is { } sh ? (sh.End ?? n).Plus(-MinLength) : below ?? dayEnd;
                var endAt = Settle((e.End ?? n).Plus(delta));
                if (endAt > ceiling) endAt = ceiling;
                var earliest = e.Start.Plus(MinLength);
                d.End = endAt > earliest ? endAt : earliest;
                break;
        }
        return d;
    }
}
