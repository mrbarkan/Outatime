using System.Globalization;
using Cell = Outatime.Xlsx.Cell;
using Style = Outatime.Xlsx.Style;

namespace Outatime;

/// One row of a report: a day with entries or owed hours. Summed, the balances match the app's balances.
public sealed record DaySummary(DateTimeOffset Date, Dictionary<Activity, double> Hours, double Worked, double Owed, double Balance, string Notes)
{
    public static List<DaySummary> Days(IEnumerable<DateTimeOffset> dates, IEnumerable<Entry> entries, Target target, DateTimeOffset? now = null)
    {
        var n = now ?? Clock.Now;
        var byDay = entries.GroupBy(e => Cal.StartOfDay(e.Start)).ToDictionary(g => g.Key, g => g.ToList());
        var days = new List<DaySummary>();
        foreach (var date in dates)
        {
            var es = byDay.TryGetValue(date, out var x) ? x : [];
            var owed = target.Owed(date, n);
            if (es.Count == 0 && owed <= 0) continue;
            var t = Store.TotalsOf(es);
            var worked = t.Worked(target.Excluded);
            days.Add(new DaySummary(date, t, worked, owed, target.Balance(worked, date, n), string.Join("; ", es.SelectMany(e => e.Notes))));
        }
        return days;
    }
}

public static class Csv
{
    static string Escape(string s) =>
        s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    static string Row(IEnumerable<string> fields) => string.Join(",", fields.Select(Escape));
    static string Hours(double t) => (t / 3600).ToString("0.00", CultureInfo.InvariantCulture);
    static string Signed(double t) => (t / 3600 >= 0 ? "+" : "") + (t / 3600).ToString("0.00", CultureInfo.InvariantCulture);
    static string Time(DateTimeOffset d) => Cal.Local(d).ToString("t", CultureInfo.CurrentCulture);

    /// `names`: client names by id, for the Client column.
    public static string Entries(IEnumerable<Entry> entries, IReadOnlyDictionary<Guid, string>? names = null)
    {
        var lines = new List<string> { Row(["Date", "Activity", "Notes", "Start", "End", "Hours", "Client"]) };
        foreach (var e in entries)
            lines.Add(Row([e.Start.DayKey(), e.Activity.Title(), string.Join("; ", e.Notes), Time(e.Start), e.End is { } end ? Time(end) : "",
                           Hours(e.Duration), e.Profile is { } p && names != null && names.TryGetValue(p, out var name) ? name : ""]));
        return string.Join("\n", lines) + "\n";
    }

    /// Every day of `month` that has entries or owes hours, then a total row that matches the app's month balance.
    public static string Daily(IEnumerable<Entry> entries, DateTimeOffset month, Target target, DateTimeOffset? now = null)
    {
        var days = DaySummary.Days(Cal.DaysInMonth(month), entries, target, now);
        var lines = new List<string> { Row(["Date", .. Activities.All.Select(a => a.Title()), "Balance", "Notes"]) };
        foreach (var d in days)
            lines.Add(Row([d.Date.DayKey(), .. Activities.All.Select(a => Hours(d.Hours.Get(a))), Signed(d.Balance), d.Notes]));
        lines.Add(Row(["Total", .. Activities.All.Select(a => Hours(days.Sum(d => d.Hours.Get(a)))), Signed(days.Sum(d => d.Balance)), ""]));
        return string.Join("\n", lines) + "\n";
    }
}

/// The Excel exports. A month report and the master workbook share the Summary and Entries sheets (tables
/// `tblDays` and `tblEntries`), so a month's rows paste straight into the master and its dashboard picks them up.
public static class Report
{
    static readonly Activity[] BillableActivities = Activities.All.Where(a => a.Billable()).ToArray();

    public static byte[] Month(IReadOnlyList<Entry> entries, DateTimeOffset month, Target target, IReadOnlyDictionary<Guid, string>? names = null, DateTimeOffset? now = null)
    {
        names ??= new Dictionary<Guid, string>();
        var days = DaySummary.Days(Cal.DaysInMonth(month), entries, target, now);
        var sheets = new List<Xlsx.Sheet> { Summary(days, totals: true) };
        if (Clients(entries, names) is { } clients) sheets.Add(clients);
        sheets.Add(List(entries, names));
        return Xlsx.Workbook(sheets);
    }

    /// A client's month: billable hours per day and the blocks behind them, to attach to an invoice. No target or
    /// balance; `entries` are already that client's.
    public static byte[] Client(IReadOnlyList<Entry> entries, string name, DateTimeOffset month)
    {
        var billed = entries.Where(e => e.Activity.Billable()).OrderBy(e => e.Start).ToList();
        var days = billed.GroupBy(e => Cal.StartOfDay(e.Start)).OrderBy(g => g.Key)
            .Select(g => (Date: g.Key, Hours: Store.TotalsOf(g), Notes: g.SelectMany(e => e.Notes).ToList())).ToList();
        var columns = BillableActivities.Select(a => (Func<Dictionary<Activity, double>, double>)(h => h.Get(a)))
            .Append(h => h.Values.Sum()).ToList();
        var rows = days.Select(d => new List<Cell> { new Cell.Number(Xlsx.Serial(d.Date), Style.Date) }
            .Concat(columns.Select(f => (Cell)new Cell.Number(f(d.Hours) / 3600, Style.Hours)))
            .Append(new Cell.Text(string.Join("; ", d.Notes))).ToList()).ToList();
        var sums = columns.Select((f, i) => (i + 1, days.Sum(d => f(d.Hours)) / 3600, Style.TotalHours)).ToList();
        var sheet = Xlsx.TableSheet("Days", "tblClientDays", ["Date", .. BillableActivities.Select(a => a.Title()), "Total", "Notes"],
                                    rows, sums, [16, .. columns.Select(_ => 10.0), 40]);
        var id = Guid.NewGuid();
        return Xlsx.Workbook([sheet, List(billed.Select(e => e with { Profile = id }).ToList(), new Dictionary<Guid, string> { [id] = name })]);
    }

    /// Billable hours per client, "No client" last; null when no block has a client.
    static Xlsx.Sheet? Clients(IReadOnlyList<Entry> entries, IReadOnlyDictionary<Guid, string> names)
    {
        var billed = entries.Where(e => e.Activity.Billable()).ToList();
        if (!billed.Any(e => e.Profile != null)) return null;
        string? NameOf(Entry e) => e.Profile is { } p && names.TryGetValue(p, out var n) ? n : null;
        var groups = billed.GroupBy(NameOf).OrderBy(g => g.Key == null ? 1 : 0).ThenBy(g => g.Key ?? "", StringComparer.Ordinal).ToList();
        var totals = groups.Select(g => Store.TotalsOf(g)).ToList();
        var columns = BillableActivities.Select(a => (Func<Dictionary<Activity, double>, double>)(h => h.Get(a)))
            .Append(h => h.Values.Sum()).ToList();
        var rows = groups.Select((g, i) => new List<Cell> { new Cell.Text(g.Key ?? "No client") }
            .Concat(columns.Select(f => (Cell)new Cell.Number(f(totals[i]) / 3600, Style.Hours))).ToList()).ToList();
        var sums = columns.Select((f, i) => (i + 1, totals.Sum(f) / 3600, Style.TotalHours)).ToList();
        return Xlsx.TableSheet("Clients", "tblClients", ["Client", .. BillableActivities.Select(a => a.Title()), "Total"],
                               rows, sums, [24, .. columns.Select(_ => 10.0)]);
    }

    /// Everything since tracking began, opening on a dashboard.
    public static byte[] Master(IReadOnlyList<Entry> entries, Target target, IReadOnlyDictionary<Guid, string>? names = null, DateTimeOffset? now = null)
    {
        var n = now ?? Clock.Now;
        var dates = new List<DateTimeOffset>();
        for (var d = Cal.StartOfDay(target.Since); d <= n; d = Cal.AddDays(d, 1)) dates.Add(d);
        var days = DaySummary.Days(dates, entries, target, n);
        return Xlsx.Workbook([Dashboard(days, target, n), Summary(days, totals: false),
                              List(entries.OrderBy(e => e.Start).ToList(), names ?? new Dictionary<Guid, string>())]);
    }

    static readonly List<(string Title, Func<DaySummary, double> Value)> Values =
        Activities.All.Select(a => (a.Title(), (Func<DaySummary, double>)(d => d.Hours.Get(a) / 3600)))
        .Concat([("Worked", d => d.Worked / 3600), ("Target", d => d.Owed / 3600), ("Balance", d => d.Balance / 3600)]).ToList();

    static Xlsx.Sheet Summary(List<DaySummary> days, bool totals)
    {
        var balance = Values.Count - 1;
        var rows = days.Select(d => new List<Cell> { new Cell.Number(Xlsx.Serial(d.Date), Style.Date) }
            .Concat(Values.Select((v, i) => (Cell)new Cell.Number(v.Value(d), i == balance ? Style.Balance : Style.Hours)))
            .Append(new Cell.Text(d.Notes)).ToList()).ToList();
        var sums = !totals ? [] : Values.Select((v, i) => (i + 1, days.Sum(v.Value), i == balance ? Style.TotalBalance : Style.TotalHours)).ToList();
        return Xlsx.TableSheet("Summary", "tblDays", ["Date", .. Values.Select(v => v.Title), "Notes"], rows, sums,
                               [16, .. Values.Select(v => Math.Max(10, v.Title.Length + 2.0)), 40]);
    }

    static Xlsx.Sheet List(IReadOnlyList<Entry> entries, IReadOnlyDictionary<Guid, string> names)
    {
        var rows = entries.Select(e => new List<Cell>
        {
            new Cell.Number(Xlsx.Serial(Cal.StartOfDay(e.Start)), Style.Date), new Cell.Text(e.Activity.Title()),
            new Cell.Number(Xlsx.Serial(e.Start), Style.Time), e.End is { } end ? new Cell.Number(Xlsx.Serial(end), Style.Time) : Cell.None,
            new Cell.Number(e.Duration / 3600, Style.Hours), new Cell.Text(string.Join("; ", e.Notes)),
            new Cell.Text(e.Profile is { } p && names.TryGetValue(p, out var name) ? name : ""),
        }).ToList();
        return Xlsx.TableSheet("Entries", "tblEntries", ["Date", "Activity", "Start", "End", "Hours", "Notes", "Client"], rows, null,
                               [16, 14, 9, 9, 9, 40, 18]);
    }

    /// Cards, a worked-vs-target chart, an activity breakdown and a month table, all formulas over `tblDays`.
    static Xlsx.Sheet Dashboard(List<DaySummary> days, Target target, DateTimeOffset now)
    {
        var s = new Xlsx.Sheet("Dashboard") { Widths = [3, .. Enumerable.Repeat(13.0, 8)], Gridlines = false };
        var col = Xlsx.Column;
        double Sum(Func<DaySummary, double> f, IEnumerable<DaySummary> ds) => ds.Sum(f) / 3600;
        var first = days.Count > 0 ? days[0].Date : now;
        var worked = Sum(d => d.Worked, days);
        double daysWorked = days.Count(d => d.Worked > 0);

        s.Put(1, 1, new Cell.Text("Hours Overview", Style.Title)); s.Heights[1] = 34;
        var hours = (target.Seconds / 3600).ToString("0.##", CultureInfo.InvariantCulture);
        s.Put(2, 1, new Cell.Text($"Since {Cal.Local(first).ToString("MMMM yyyy", CultureInfo.CurrentCulture)} · target {hours} h a day · updated {now.DayKey()}", Style.Subtitle));

        // Four cards in rows 5–7, each two merged columns wide.
        var cards = new (string Label, Cell Value, string Caption)[]
        {
            ("HOURS WORKED", new Cell.Formula("SUM(tblDays[Worked])", worked, Style.CardHours), "toward the target"),
            ("HOURS BANK", new Cell.Formula("SUM(tblDays[Balance])", Sum(d => d.Balance, days), Style.CardBank), "worked minus target"),
            ("DAYS WORKED", new Cell.Formula("COUNTIF(tblDays[Worked],\">0\")", daysWorked, Style.CardDays), "with time logged"),
            ("AVERAGE DAY", new Cell.Formula("IFERROR(B6/F6,0)", daysWorked > 0 ? worked / daysWorked : 0, Style.CardHours), "per day worked"),
        };
        for (var i = 0; i < cards.Length; i++)
        {
            var c = 1 + i * 2;
            foreach (var (row, cell, style) in new (int, Cell, Style)[]
                     { (4, new Cell.Text(cards[i].Label, Style.CardLabel), Style.CardLabel), (5, cards[i].Value, Style.CardHours),
                       (6, new Cell.Text(cards[i].Caption, Style.CardCaption), Style.CardCaption) })
            {
                s.Put(row, c, cell); s.Put(row, c + 1, new Cell.Blank(style));
                s.Merges.Add($"{col(c)}{row + 1}:{col(c + 1)}{row + 1}");
            }
        }
        s.Heights[4] = 24; s.Heights[5] = 38; s.Heights[6] = 22;

        // Months from the first one tracked through December. Each is EDATE of the one above, so dragging the last
        // row down adds more.
        var lastMonth = Cal.At(Cal.Date(now).Year, 12, 1);
        var months = new List<DateTimeOffset>();
        for (var m = Cal.StartOfMonth(first); m <= lastMonth; m = Cal.AddMonths(m, 1)) months.Add(m);
        const int monthTop = 37;  // first month row, 0-based; the table header sits above it

        s.Put(8, 1, new Cell.Text("Worked vs target by month", Style.Section));
        string Range(int c) => $"Dashboard!${col(c)}${monthTop + 1}:${col(c)}${monthTop + months.Count}";
        List<DaySummary> InMonth(DateTimeOffset m) => days.Where(d => Cal.SameMonth(d.Date, m)).ToList();
        s.Chart = new Xlsx.Chart((1, 9), (9, 24), (Range(1), months.Select(Xlsx.Serial).ToList()),
            [new("Worked", Range(2), months.Select(m => Sum(d => d.Worked, InMonth(m))).ToList(), "2563EB"),
             new("Target", Range(3), months.Select(m => Sum(d => d.Owed, InMonth(m))).ToList(), "CBD5E1")]);

        s.Put(25, 1, new Cell.Text("By activity", Style.Section));
        string[] heads = ["Activity", "Hours", "Share"];
        for (var c = 0; c < heads.Length; c++) s.Put(26, 1 + c, new Cell.Text(heads[c], c == 0 ? Style.Head : Style.HeadRight));
        for (var c = 4; c <= 8; c++) s.Put(26, c, new Cell.Blank(Style.Head));
        var total = Sum(d => d.Hours.Values.Sum(), days);
        for (var i = 0; i < Activities.All.Length; i++)
        {
            var a = Activities.All[i];
            var r = 27 + i;
            var h = Sum(d => d.Hours.Get(a), days);
            var span = $"$C$28:$C${27 + Activities.All.Length}";
            s.Put(r, 1, new Cell.Text(a.Title(), Style.RowText));
            s.Put(r, 2, new Cell.Formula($"SUM(tblDays[{a.Title()}])", h, Style.RowHours));
            s.Put(r, 3, new Cell.Formula($"IFERROR(C{r + 1}/SUM({span}),0)", total > 0 ? h / total : 0, Style.RowShare));
            // The share again, merged across the rest of the row and drawn as a bar.
            s.Put(r, 4, new Cell.Formula($"D{r + 1}", total > 0 ? h / total : 0, Style.RowBar));
            for (var c = 5; c <= 8; c++) s.Put(r, c, new Cell.Blank(Style.RowText));
            s.Merges.Add($"E{r + 1}:I{r + 1}");
        }
        s.Bars = [$"E28:E{27 + Activities.All.Length}"];

        s.Put(monthTop - 2, 1, new Cell.Text("By month", Style.Section));
        var columns = new List<(string Title, Func<string, string> Formula, Func<List<DaySummary>, double> Value, Style Style)>
        {
            ("Worked", c => $"SUMIFS(tblDays[Worked],{c})", ds => Sum(d => d.Worked, ds), Style.RowHours),
            ("Target", c => $"SUMIFS(tblDays[Target],{c})", ds => Sum(d => d.Owed, ds), Style.RowHours),
            ("Balance", c => $"SUMIFS(tblDays[Balance],{c})", ds => Sum(d => d.Balance, ds), Style.RowBalance),
            ("Days", c => $"COUNTIFS({c},tblDays[Worked],\">0\")", ds => ds.Count(d => d.Worked > 0), Style.RowCount),
        };
        foreach (var a in new[] { Activity.Extra, Activity.Travel, Activity.OutOfOffice })
            columns.Add((a.Title(), c => $"SUMIFS(tblDays[{a.Title()}],{c})", ds => Sum(d => d.Hours.Get(a), ds), Style.RowHours));
        s.Put(monthTop - 1, 1, new Cell.Text("Month", Style.Head));
        for (var c = 0; c < columns.Count; c++) s.Put(monthTop - 1, 2 + c, new Cell.Text(columns[c].Title, Style.HeadRight));
        for (var i = 0; i < months.Count; i++)
        {
            var r = monthTop + i;
            var ds = InMonth(months[i]);
            var criteria = $"tblDays[Date],\">=\"&$B{r + 1},tblDays[Date],\"<\"&EDATE($B{r + 1},1)";
            s.Put(r, 1, i == 0 ? new Cell.Number(Xlsx.Serial(months[i]), Style.Month) : new Cell.Formula($"EDATE(B{r},1)", Xlsx.Serial(months[i]), Style.Month));
            for (var c = 0; c < columns.Count; c++) s.Put(r, 2 + c, new Cell.Formula(columns[c].Formula(criteria), columns[c].Value(ds), columns[c].Style));
        }
        return s;
    }

    /// Export file names use "2026-09", never the locale's month format: "09/2026" puts a slash in the name.
    public static string ExportName(DateTimeOffset month, string suffix) => $"Outatime {month.DayKey()[..7]}{suffix}";

    /// "Outatime 2026-09 Acme.xlsx"; characters Windows forbids in file names become dashes.
    public static string ClientExportName(DateTimeOffset month, string name) =>
        ExportName(month, " " + new string(name.Select(c => "/:\\*?\"<>|".Contains(c) ? '-' : c).ToArray()) + ".xlsx");
}
