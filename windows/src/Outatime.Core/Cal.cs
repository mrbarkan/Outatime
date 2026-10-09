using System.Globalization;

namespace Outatime;

/// What "now" is. Tests and the app's tick read it from here so the logic below never calls DateTimeOffset.Now itself.
public static class Clock
{
    public static Func<DateTimeOffset> Source = () => DateTimeOffset.Now;
    public static DateTimeOffset Now => Source();
}

/// A span of time, start included, end excluded.
public readonly record struct Interval(DateTimeOffset Start, DateTimeOffset End)
{
    public bool Contains(DateTimeOffset d) => d >= Start && d < End;
}

/// The local calendar: days, weeks and months in the user's time zone, weeks starting on the culture's first
/// weekday. The Swift app gets the same from Calendar.current. Instants stay absolute (DateTimeOffset, like Swift's
/// Date); only "which day is this" goes through here.
public static class Cal
{
    public static TimeZoneInfo Zone = TimeZoneInfo.Local;
    /// Null: the current culture's.
    public static DayOfWeek? FirstDayOverride;
    public static DayOfWeek FirstDay => FirstDayOverride ?? CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;

    public static DateTimeOffset Local(DateTimeOffset d) => TimeZoneInfo.ConvertTime(d, Zone);
    /// The local calendar date, as a DateTime at midnight with no zone.
    public static DateTime Date(DateTimeOffset d) => Local(d).Date;

    /// A local wall-clock time as an instant. A time skipped by a DST change moves forward an hour.
    public static DateTimeOffset At(DateTime local)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (Zone.IsInvalidTime(local)) local = local.AddHours(1);
        return new DateTimeOffset(local, Zone.GetUtcOffset(local));
    }

    public static DateTimeOffset At(int year, int month, int day, int hour = 0, int minute = 0, int second = 0) =>
        At(new DateTime(year, month, day, hour, minute, second));

    public static DateTimeOffset StartOfDay(DateTimeOffset d) => At(Date(d));
    public static DateTimeOffset AddDays(DateTimeOffset d, int days) => At(Local(d).DateTime.AddDays(days));
    public static DateTimeOffset AddMonths(DateTimeOffset d, int months) => At(Local(d).DateTime.AddMonths(months));
    /// `d`'s day at hour:minute:second.
    public static DateTimeOffset Setting(DateTimeOffset d, int hour, int minute = 0, int second = 0) =>
        At(Date(d).AddHours(hour).AddMinutes(minute).AddSeconds(second));

    public static bool SameDay(DateTimeOffset a, DateTimeOffset b) => Date(a) == Date(b);
    public static bool SameMonth(DateTimeOffset a, DateTimeOffset b) { var x = Date(a); var y = Date(b); return x.Year == y.Year && x.Month == y.Month; }
    public static bool IsToday(DateTimeOffset d, DateTimeOffset? now = null) => SameDay(d, now ?? Clock.Now);
    public static bool IsWeekend(DateTimeOffset d) => Date(d).DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public static Interval Day(DateTimeOffset d) { var s = StartOfDay(d); return new(s, AddDays(s, 1)); }

    public static Interval Week(DateTimeOffset d)
    {
        var date = Date(d);
        var back = ((int)date.DayOfWeek - (int)FirstDay + 7) % 7;
        var start = At(date.AddDays(-back));
        return new(start, AddDays(start, 7));
    }

    public static Interval Month(DateTimeOffset d)
    {
        var date = Date(d);
        var start = At(new DateTime(date.Year, date.Month, 1));
        return new(start, AddMonths(start, 1));
    }

    public static Interval Year(DateTimeOffset d) { var y = Date(d).Year; return new(At(y, 1, 1), At(y + 1, 1, 1)); }

    public static DateTimeOffset StartOfMonth(DateTimeOffset d) => Month(d).Start;

    public static List<DateTimeOffset> DaysInMonth(DateTimeOffset d)
    {
        var first = StartOfMonth(d);
        var count = DateTime.DaysInMonth(Date(first).Year, Date(first).Month);
        return Enumerable.Range(0, count).Select(i => AddDays(first, i)).ToList();
    }

    /// The days whose start lies in `interval`.
    public static List<DateTimeOffset> Days(Interval interval)
    {
        var days = new List<DateTimeOffset>();
        for (var d = StartOfDay(interval.Start); d < interval.End; d = AddDays(d, 1)) days.Add(d);
        return days;
    }

    public static int WeekOfYear(DateTimeOffset d) =>
        CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(Date(d), CultureInfo.CurrentCulture.DateTimeFormat.CalendarWeekRule, FirstDay);

    /// Seconds after local midnight.
    public static double SecondsIntoDay(DateTimeOffset d) => (d - StartOfDay(d)).TotalSeconds;
}

public static class TimeExtensions
{
    /// `d` plus `seconds`, to the tick: Swift's `date + interval`.
    public static DateTimeOffset Plus(this DateTimeOffset d, double seconds) => d.AddTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
    /// Seconds from `other` to `d`: Swift's `d.timeIntervalSince(other)`.
    public static double Since(this DateTimeOffset d, DateTimeOffset other) => (d - other).TotalSeconds;

    /// "2026-09-22" in the local time zone; how days off are stored.
    public static string DayKey(this DateTimeOffset d) => Cal.Date(d).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// "6h 12m"
    public static string Hm(this double seconds)
    {
        var m = (int)(seconds / 60);
        return $"{m / 60}h {Math.Abs(m % 60):00}m";
    }

    /// "42h" from ten hours up, "6h 12m" below that, "35m" under an hour: short enough for the panel's footer.
    public static string Short(this double seconds) =>
        seconds >= 36000 ? $"{(int)Math.Ceiling(seconds / 3600)}h" : seconds >= 3600 ? seconds.Hm() : $"{(int)Math.Ceiling(seconds / 60)}m";

    /// "+0h 35m" / "−1h 05m"
    public static string Signed(this double seconds) => (seconds >= 0 ? "+" : "−") + Math.Abs(seconds).Hm();
}
