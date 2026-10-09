namespace Outatime;

/// The numbers behind the panel's week and day-off lines and the Logbook's Stats. Hours are those that count toward
/// the target.
public sealed class Stats
{
    /// This week: its goal (each workday's target, days off excepted) and what's done so far.
    public double WeekGoal, WeekWorked;
    /// The hours bank, and one day's target: banking that much buys a day off.
    public double Bank, DayTarget;
    /// Over complete weeks and months since tracking began; null until there is one.
    public double? AverageWeek, AverageMonth;
    /// Per day worked.
    public double? AverageDay;
    /// Minutes after midnight, the median over days worked; today's finish isn't in yet.
    public int? UsualStart, UsualFinish;
    public (DateTimeOffset Date, double Worked)? LongestDay;
    public double ExtraThisMonth;
    public int DaysWorkedThisMonth;

    /// Negative once the week's goal is passed.
    public double WeekLeft => WeekGoal - WeekWorked;
    public int DaysOffBanked => DayTarget > 0 ? Math.Max(0, (int)(Bank / DayTarget)) : 0;
    /// What's still to bank for the next day off.
    public double ToDayOff => DayTarget > 0 ? DayTarget - (Bank - DaysOffBanked * DayTarget) : 0;

    public Stats(IReadOnlyList<Entry> entries, Target target, DateTimeOffset bankSince, DateTimeOffset? now = null)
    {
        var n = now ?? Clock.Now;
        var byDay = entries.GroupBy(e => Cal.StartOfDay(e.Start)).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Start).ToList());
        var worked = byDay.ToDictionary(kv => kv.Key, kv => Store.TotalsOf(kv.Value).Worked(target.Excluded))
            .Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
        double Sum(Interval i) => worked.Where(kv => i.Contains(kv.Key)).Sum(kv => kv.Value);

        var week = Cal.Week(n);
        WeekWorked = Sum(week);
        WeekGoal = Cal.Days(week).Sum(target.Scheduled);
        DayTarget = target.Seconds;
        Bank = Store.BalanceOf(entries, new Interval(Cal.StartOfDay(bankSince), n), target, n).Balance;

        // Complete periods only: from the first one that starts on or after tracking began, to the one before now.
        double? Average(Func<DateTimeOffset, Interval> unit)
        {
            var periods = new List<Interval>();
            var p = unit(target.Since);
            if (p.Start < Cal.StartOfDay(target.Since)) p = unit(p.End);
            while (p.End <= unit(n).Start)
            {
                periods.Add(p);
                p = unit(p.End);
            }
            return periods.Count == 0 ? null : periods.Sum(Sum) / periods.Count;
        }
        AverageWeek = Average(Cal.Week);
        AverageMonth = Average(Cal.Month);
        AverageDay = worked.Count == 0 ? null : worked.Values.Sum() / worked.Count;

        static int? Median(List<int> xs)
        {
            xs.Sort();
            return xs.Count == 0 ? null : xs.Count % 2 == 1 ? xs[xs.Count / 2] : (xs[xs.Count / 2 - 1] + xs[xs.Count / 2]) / 2;
        }
        static int Minute(DateTimeOffset d) => (int)(Cal.SecondsIntoDay(d) / 60);
        var workedDays = worked.Keys.Select(day => (Day: day, Entries: byDay[day])).ToList();
        UsualStart = Median(workedDays.Select(w => Minute(w.Entries[0].Start)).ToList());
        UsualFinish = Median(workedDays.Where(w => !Cal.SameDay(w.Day, n) && w.Entries[^1].End != null)
            .Select(w => Minute(w.Entries[^1].End!.Value)).ToList());

        var month = Cal.Month(n);
        var monthDays = worked.Where(kv => month.Contains(kv.Key)).ToList();
        // The longest; on a tie, the earliest.
        LongestDay = monthDays.Count == 0 ? null
            : monthDays.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).First();
        ExtraThisMonth = entries.Where(e => e.Activity == Activity.Extra && month.Contains(e.Start)).Sum(e => e.Duration);
        DaysWorkedThisMonth = monthDays.Count;
    }
}
