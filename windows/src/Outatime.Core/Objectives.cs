using System.Globalization;

namespace Outatime;

public enum ObjectiveKind { DaysOff, Hours, Money }

/// Something to work toward: days off bought with overtime, or a package of a client's hours, agreed in hours or as an
/// amount at an hourly rate. The panel counts one of them down.
public record struct Objective
{
    public Guid Id { get; set; }
    public ObjectiveKind Kind { get; set; }
    public string Name { get; set; }
    /// Days off, hours, or money, by kind.
    public double Amount { get; set; }
    /// Money per hour; turns a money package into hours.
    public double Rate { get; set; }
    public string Currency { get; set; }
    public Guid? Profile { get; set; }
    /// A package counts the client's billable time from this day on.
    public DateTimeOffset Since { get; set; }

    public Objective(ObjectiveKind kind, double amount, double rate = 0, string? currency = null, Guid? profile = null, DateTimeOffset? since = null, string name = "")
    {
        Id = Guid.NewGuid(); Kind = kind; Name = name; Amount = amount; Rate = rate; Profile = profile;
        Currency = currency ?? DefaultCurrency;
        Since = since ?? Cal.StartOfDay(Clock.Now);
    }

    public static string DefaultCurrency
    {
        get { try { return RegionInfo.CurrentRegion.ISOCurrencySymbol; } catch { return "USD"; } }
    }

    public readonly record struct ProgressInfo(double Done, double Goal)
    {
        public double Left => Math.Max(0, Goal - Done);
        public double Fraction => Goal > 0 ? Math.Min(1, Done / Goal) : 0;
        public bool IsDone => Goal > 0 && Done >= Goal;
    }

    /// In hours, whatever the kind: days off are bought with the hours bank, one daily target each; a package is met by
    /// the client's billable time.
    public readonly ProgressInfo Progress(IEnumerable<Entry> entries, double bank, double dayTarget)
    {
        switch (Kind)
        {
            case ObjectiveKind.DaysOff:
                return new(Math.Max(0, bank), Amount * dayTarget);
            default:
                var start = Cal.StartOfDay(Since);
                var profile = Profile;
                var done = entries.Where(e => e.Activity.Billable() && e.Profile == profile && e.Start >= start).Sum(e => e.Duration);
                var hours = Kind == ObjectiveKind.Hours ? Amount : Rate > 0 ? Amount / Rate : 0;
                return new(done, hours * 3600);
        }
    }
}

public static class ObjectiveKinds
{
    public static readonly ObjectiveKind[] All = Enum.GetValues<ObjectiveKind>();
    public static string Raw(this ObjectiveKind k) => k switch { ObjectiveKind.DaysOff => "daysOff", ObjectiveKind.Hours => "hours", _ => "money" };
    public static ObjectiveKind? FromRaw(string? raw) => All.Cast<ObjectiveKind?>().FirstOrDefault(k => k!.Value.Raw() == raw);
    public static string Label(this ObjectiveKind k) => Loc.T(k switch { ObjectiveKind.DaysOff => "Vacation", ObjectiveKind.Hours => "Hours Package", _ => "Salary" });
    public static string Color(this ObjectiveKind k) => k switch { ObjectiveKind.DaysOff => "#64D2FF", ObjectiveKind.Hours => "#5E5CE6", _ => "#FF9F0A" };
}
