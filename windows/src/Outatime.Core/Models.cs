namespace Outatime;

public enum Activity { Work, Break, Lunch, Extra, Travel, OutOfOffice }

public static class Activities
{
    public static readonly Activity[] All = Enum.GetValues<Activity>();
    public const string DefaultExcluded = "lunch";

    public static string Raw(this Activity a) => a switch
    {
        Activity.Work => "work", Activity.Break => "break", Activity.Lunch => "lunch", Activity.Extra => "extra",
        Activity.Travel => "travel", Activity.OutOfOffice => "outOfOffice", _ => throw new ArgumentOutOfRangeException(nameof(a)),
    };

    public static Activity? FromRaw(string? raw) => All.Cast<Activity?>().FirstOrDefault(a => a!.Value.Raw() == raw);

    /// Time billed to a client.
    public static bool Billable(this Activity a) => a is Activity.Work or Activity.Extra or Activity.Travel;

    /// The English name; it's also the key of its translation.
    public static string Name(this Activity a) => a == Activity.OutOfOffice ? "Out of Office" : a.ToString();
    public static string Label(this Activity a) => Loc.T(a.Name());
    /// Column and row name in exported files, never translated.
    public static string Title(this Activity a) => a.Name();

    /// The Mac's system colors.
    public static string Color(this Activity a) => a switch
    {
        Activity.Work => "#0A84FF", Activity.Break => "#30D158", Activity.Lunch => "#FF9F0A", Activity.Extra => "#BF5AF2",
        Activity.Travel => "#30B0C7", _ => "#FF375F",
    };
}

/// A client billable blocks are tracked for. Removing one archives it, so old blocks keep its name.
public record struct Profile(Guid Id, string Name, bool Archived = false)
{
    public Profile(string name) : this(Guid.NewGuid(), name) { }
}

public record struct Entry
{
    public Guid Id { get; set; }
    public Activity Activity { get; set; }
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset? End { get; set; }
    public IReadOnlyList<string> Notes { get; set; }
    /// The client; only billable activities have one.
    public Guid? Profile { get; set; }

    public Entry(Activity activity, DateTimeOffset start, DateTimeOffset? end = null, IReadOnlyList<string>? notes = null, Guid? profile = null, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid(); Activity = activity; Start = start; End = end; Notes = notes ?? []; Profile = profile;
    }

    public readonly bool IsRunning => End == null;
    // ponytail: end < start (crossed midnight while editing) clamps to 0 rather than splitting the entry.
    public readonly double Duration => Math.Max(0, (End ?? Clock.Now).Since(Start));
}

public record struct Slot(Activity Activity, int StartMinute, int EndMinute, IReadOnlyList<string> Notes, Guid? Profile = null);

public record struct DayTemplate(Guid Id, string Name, IReadOnlyList<Slot> Slots)
{
    public DayTemplate(string name, IEnumerable<Entry> entries) : this(Guid.NewGuid(), name, entries.Select(e =>
    {
        var day = Cal.StartOfDay(e.Start);
        int Minute(DateTimeOffset d) => (int)(d.Since(day) / 60);
        return new Slot(e.Activity, Minute(e.Start), Minute(e.End ?? Clock.Now), e.Notes, e.Profile);
    }).ToList()) { }

    public readonly List<Entry> EntriesOn(DateTimeOffset day)
    {
        var start = Cal.StartOfDay(day);
        return Slots.Select(s => new Entry(s.Activity, start.Plus(s.StartMinute * 60), start.Plus(s.EndMinute * 60), s.Notes, s.Profile)).ToList();
    }
}

public static class Totals
{
    /// Time that counts toward the daily target. `excluded` is the "excludedFromTarget" setting: comma-separated
    /// activity raw values, by default just lunch (coffee breaks are paid).
    public static double Worked(this IReadOnlyDictionary<Activity, double> totals, string excluded = Activities.DefaultExcluded)
    {
        var @out = excluded.Split(',');
        return totals.Where(kv => !@out.Contains(kv.Key.Raw())).Sum(kv => kv.Value);
    }

    public static double Get(this IReadOnlyDictionary<Activity, double> totals, Activity a) => totals.TryGetValue(a, out var v) ? v : 0;
}

/// Time taken off workdays and paid from the hours bank: `Start`…`End` (day keys, both included), each workday costing
/// `Hours`, or with none a whole day's target, so a new target re-prices full days.
public record struct TimeOff(Guid Id, string Start, string End, double? Hours = null, string Note = "")
{
    public TimeOff(string start, string end, double? hours = null, string note = "") : this(Guid.NewGuid(), start, end, hours, note) { }
}

/// Hours put into the bank from outside the Logbook, like overtime from before tracking began, or taken out (negative)
/// when they're paid.
public record struct Carried(Guid Id, string Date, double Hours, string Note = "")
{
    public Carried(string date, double hours, string note = "") : this(Guid.NewGuid(), date, hours, note) { }
}

/// What the hours bank holds, the time worked behind it, and how much of it is already booked as time off ahead.
public readonly record struct HoursBank(double Balance, double Worked, double BookedAhead);

/// The daily target and which days owe it. `Booked`: seconds of each day taken off from the bank, by day key; see Book.
public record struct Target(double Seconds, DateTimeOffset Since, string Excluded = Activities.DefaultExcluded, IReadOnlySet<string>? DaysOff = null,
                            IReadOnlyDictionary<string, double>? Booked = null)
{
    readonly IReadOnlySet<string> Off => DaysOff ?? new HashSet<string>();

    /// Weekends, days off, days before tracking began and days still ahead owe nothing.
    // ponytail: workdays are Monday to Friday; a per-weekday schedule when someone works Saturdays.
    public readonly double Owed(DateTimeOffset day, DateTimeOffset? now = null) =>
        Cal.StartOfDay(day) <= (now ?? Clock.Now) ? Scheduled(day) : 0;

    /// What a day will owe once it comes, so a week's goal counts the days still ahead. Time off lowers it.
    public readonly double Scheduled(DateTimeOffset day)
    {
        var d = Cal.StartOfDay(day);
        if (!IsWorkday(d) || d < Cal.StartOfDay(Since)) return 0;
        return Math.Max(0, Seconds - (Booked?.GetValueOrDefault(d.DayKey()) ?? 0));
    }

    /// Not a weekend or a holiday.
    public readonly bool IsWorkday(DateTimeOffset day) => !Cal.IsWeekend(day) && !Off.Contains(day.DayKey());

    /// What a day adds to a week/month balance. Today is still in progress: it can add a surplus, not a shortfall yet.
    public readonly double Balance(double worked, DateTimeOffset day, DateTimeOffset? now = null)
    {
        var n = now ?? Clock.Now;
        var b = worked - Owed(day, n);
        return Cal.SameDay(day, n) ? Math.Max(0, b) : b;
    }

    /// Spreads time off over the workdays it covers, never more than a day's target on one day.
    public readonly Target Book(IEnumerable<TimeOff> records)
    {
        var booked = new Dictionary<string, double>(Booked ?? new Dictionary<string, double>());
        foreach (var r in records)
            foreach (var d in DaysOf(r)) booked[d.DayKey()] = Math.Min(Seconds, booked.GetValueOrDefault(d.DayKey()) + PerDay(r));
        return this with { Booked = booked };
    }

    /// What one record takes from the bank, overlaps with other records aside.
    public readonly double Cost(TimeOff r) => DaysOf(r).Count * PerDay(r);

    readonly double PerDay(TimeOff r) => Math.Min(Seconds, r.Hours * 3600 ?? Seconds);

    /// Workdays a record covers since tracking began: weekends, holidays and earlier days cost nothing.
    readonly List<DateTimeOffset> DaysOf(TimeOff r)
    {
        var start = Cal.StartOfDay(Since);
        var self = this;
        return Cal.DaysBetween(r.Start, r.End).Where(d => self.IsWorkday(d) && d >= start).ToList();
    }
}
