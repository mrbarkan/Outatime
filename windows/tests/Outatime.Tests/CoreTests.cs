using System.Text;
using Xunit;
using A = Outatime.Activity;

namespace Outatime.Tests;

/// The Mac app's test suite, ported: same scenarios, same numbers.
public class CoreTests
{
    static readonly DateTimeOffset Day = Cal.At(2026, 9, 3);
    static DateTimeOffset At(int h, int m = 0, int s = 0) => Cal.Setting(Day, h, m, s);
    static Store NewStore() => new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"outatime-test-{Guid.NewGuid()}", "data.json"));
    static string Text(byte[] data) => Encoding.UTF8.GetString(data);
    static Entry E(A a, DateTimeOffset start, DateTimeOffset? end = null, string[]? notes = null, Guid? profile = null) => new(a, start, end, notes, profile);

    [Fact]
    public void TemplateRoundTrip()
    {
        var entries = new[] { E(A.Work, At(9), At(12, 30), ["acme"]), E(A.Lunch, At(12, 30), At(13, 15)) };
        var t = new DayTemplate("Normal", entries);
        var other = Cal.AddDays(Day, -10);
        var applied = t.EntriesOn(other);
        Assert.Equal(2, applied.Count);
        Assert.Equal(["acme"], applied[0].Notes);
        Assert.Equal(3.5 * 3600, applied[0].Duration);
        Assert.True(Cal.SameDay(applied[1].Start, other));
        Assert.Equal(12, Cal.Local(applied[1].Start).Hour);
    }

    [Fact]
    public void DailyCsv()
    {
        var entries = new[] { E(A.Work, At(9), At(17), ["a, \"b\""]), E(A.Extra, At(20), At(21, 30)) };
        var csv = Csv.Daily(entries, Day, new Target(8 * 3600, Day), At(22));
        var lines = csv.TrimEnd('\n').Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Equal("2026-09-03,8.00,0.00,0.00,1.50,0.00,0.00,+1.50,\"a, \"\"b\"\"\"", lines[1]);
        Assert.Equal("Total,8.00,0.00,0.00,1.50,0.00,0.00,+1.50,", lines[2]);
    }

    [Fact]
    public void ExportNameIsFileSafe() => Assert.Equal("Outatime 2026-09.xlsx", Report.ExportName(Day, ".xlsx"));

    [Fact]
    public void MonthReportWorkbook()
    {
        Assert.Equal(0xCBF43926u, Zip.Crc32(Encoding.UTF8.GetBytes("123456789")));
        Assert.Equal(46268.5, Xlsx.Serial(At(12)));
        var entries = new[] { E(A.Work, At(9), At(17), ["R&D <q>"]), E(A.OutOfOffice, At(17), At(18)) };
        var data = Report.Month(entries, Day, new Target(8 * 3600, Day), now: At(22));
        Assert.Equal(new byte[] { 0x50, 0x4B, 0x03, 0x04 }, data[..4]);
        var text = Text(data);
        Assert.Contains("<c r=\"J2\" s=\"5\"><v>1</v></c>", text);  // balance: 9h worked, 8h owed
        Assert.Contains("<f>SUBTOTAL(109,tblDays[Balance])</f><v>1</v>", text);
        Assert.Contains("name=\"tblDays\" displayName=\"tblDays\" ref=\"A1:K3\" totalsRowCount=\"1\"", text);
        Assert.Contains("R&amp;D &lt;q&gt;", text);
        Assert.Contains(">Out of Office<", text);
        Assert.DoesNotContain("Dashboard", text);
    }

    /// The master holds the same tables (no totals row, so pasted month rows extend them) behind a dashboard.
    [Fact]
    public void MasterWorkbook()
    {
        var start = Cal.AddDays(Day, -1);  // Wednesday: owes 8h, nothing logged
        var data = Report.Master([E(A.Work, At(9), At(18))], new Target(8 * 3600, start), now: At(22));
        var text = Text(data);
        Assert.Contains("<sheet name=\"Dashboard\" sheetId=\"1\"", text);
        Assert.Contains("name=\"tblDays\" displayName=\"tblDays\" ref=\"A1:K3\" totalsRowShown=\"0\"", text);
        Assert.Contains("<f>SUM(tblDays[Balance])</f><v>-7</v>", text);  // +1 today, -8 yesterday
        Assert.Contains("<f>COUNTIF(tblDays[Worked],&quot;&gt;0&quot;)</f><v>1</v>", text);
        Assert.Contains("<c:f>Dashboard!$C$38:", text);
    }

    /// The workbook must open: a real zip whose parts are well-formed XML.
    [Fact]
    public void WorkbookIsValidZipAndXml()
    {
        var data = Report.Master([E(A.Work, At(9), At(18), ["x & y"])], new Target(8 * 3600, Day), now: At(22));
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(data));
        Assert.Contains(zip.Entries, e => e.FullName == "xl/charts/chart1.xml");
        foreach (var part in zip.Entries)
        {
            using var s = part.Open();
            System.Xml.Linq.XDocument.Load(s);
        }
    }

    /// The store must read back what it wrote, or every relaunch silently starts empty and overwrites the file.
    [Fact]
    public void StoreRoundTrip()
    {
        var store = NewStore();
        store.Start(A.Work);
        store.AddNote("hello");
        var reloaded = new Store(store.Path);
        Assert.Single(reloaded.Entries);
        Assert.Equal(["hello"], reloaded.Entries[0].Notes);
        Assert.NotNull(reloaded.Running);
    }

    [Fact]
    public void NoteLandsOnLastEntryAfterStop()
    {
        var store = NewStore();
        store.Start(A.Work);
        store.Stop();
        store.AddNote("late note");
        Assert.Equal(["late note"], store.Entries[0].Notes);
    }

    [Fact]
    public void BlockDragSnapping()
    {
        var magnet = 4.0 / 56 * 3600;
        var start = Cal.StartOfDay(Day);
        // Tracked back to back: a few ms apart, still one shared border.
        var a = E(A.Work, At(9), At(10).Plus(-0.004));
        var b = E(A.Break, At(10), At(11, 3).Plus(27));
        Assert.Equal(a.Id, BlockDrag.Neighbour(b, BlockDrag.Mode.Start, [a])?.Id);

        // With Alt a shared border can be nudged one grid step; the neighbour's own edge must not pull it back.
        var nudged = BlockDrag.Drag(b, BlockDrag.Mode.Start, 200, [a], start, magnet, drop: true, together: true);
        Assert.Equal(At(10, 5), nudged.Start);
        // Resizing the top leaves an off-grid bottom alone.
        Assert.Equal(b.End, nudged.End);

        // Moving onto a block below: the end sticks to its start, and dropping keeps it there.
        var c = E(A.Lunch, At(12), At(13));
        var moved = BlockDrag.Drag(b, BlockDrag.Mode.Move, 55 * 60, [c], start, magnet, drop: true);
        Assert.Equal(At(12), moved.End);
        Assert.Equal(b.Duration, moved.Duration, 3);
    }

    /// A plain drag moves only the grabbed block: touching blocks come apart, and no resize runs into a neighbour.
    [Fact]
    public void BlockDragDetaches()
    {
        var magnet = 4.0 / 56 * 3600;
        var start = Cal.StartOfDay(Day);
        var a = E(A.Work, At(9), At(10));
        var b = E(A.Break, At(10), At(10, 30));
        var c = E(A.Lunch, At(12), At(13));
        var apart = BlockDrag.Drag(b, BlockDrag.Mode.Start, 600, [a, c], start, magnet, drop: true);
        Assert.Equal(At(10, 10), apart.Start);
        Assert.Equal(At(10, 30), apart.End);
        // Up past its neighbour's end: stops there instead of overlapping (Alt would push the neighbour instead).
        Assert.Equal(At(10), BlockDrag.Drag(b, BlockDrag.Mode.Start, -1800, [a, c], start, magnet, drop: true).Start);
        Assert.Equal(At(9, 30), BlockDrag.Drag(b, BlockDrag.Mode.Start, -1800, [a, c], start, magnet, drop: true, together: true).Start);
        // Down across a gap into the next block: stops at its start.
        Assert.Equal(At(12), BlockDrag.Drag(b, BlockDrag.Mode.End, 3 * 3600, [a, c], start, magnet, drop: true).End);
        Assert.Equal(At(10), BlockDrag.Drag(a, BlockDrag.Mode.End, 900, [b, c], start, magnet, drop: true).End);
    }

    [Fact]
    public void BreakCountsAsWork()
    {
        Assert.Equal(4500, new Dictionary<A, double> { [A.Work] = 3600, [A.Break] = 600, [A.Lunch] = 1800, [A.Extra] = 300 }.Worked());
        Assert.Equal(5400, new Dictionary<A, double> { [A.Work] = 3600, [A.Break] = 600, [A.Lunch] = 1800 }.Worked("break"));
    }

    /// Thu 3 – Wed 9 Sep against 8h: Thu 9h (+1), Fri forgotten (−8), a day off Mon 7 (0), Sat worked 2h (+2, weekends owe
    /// nothing), and today (Tue 8) only 3h so far — in progress, so no shortfall yet.
    [Fact]
    public void PeriodBalance()
    {
        Entry On(int d, double h) { var s = Cal.At(2026, 9, d, 9); return E(A.Work, s, s.Plus(h * 3600)); }
        var entries = new[] { On(3, 9), On(5, 2), On(8, 3), E(A.Lunch, At(18), At(19)) };
        var now = Cal.At(2026, 9, 8, 14);
        var target = new Target(8 * 3600, Day, DaysOff: new HashSet<string> { "2026-09-07" });
        var week = new Interval(Day, Cal.AddDays(Day, 7));
        var b = Store.BalanceOf(entries, week, target, now);
        Assert.Equal(14 * 3600, b.Worked);
        Assert.Equal(-5 * 3600, b.Balance);
        // Nothing is owed before tracking began.
        Assert.Equal(0, target.Owed(Cal.AddDays(Day, -1), now));
    }

    [Fact]
    public void TimerRollsOverAtMidnight()
    {
        var store = NewStore();
        store.Entries = [E(A.Work, At(17))];
        var nextMorning = At(17).AddHours(16);  // 09:00 the next day
        store.RollOver(nextMorning);
        Assert.Equal(2, store.Entries.Count);
        Assert.Equal(Cal.StartOfDay(nextMorning), store.Entries[0].End);
        Assert.Equal(Cal.StartOfDay(nextMorning), store.Running?.Start);
        Assert.Equal(At(17), store.RunningSince);
    }

    /// Double-clicking inside a work block cuts a break in; adding in a gap stops at the next block.
    [Fact]
    public void InsertCutsAndFills()
    {
        var store = NewStore();
        store.Entries = [E(A.Work, At(9), At(12)), E(A.Lunch, At(13), At(14))];
        store.Insert(A.Break, At(10), 900);
        var work = store.Entries.Where(e => e.Activity == A.Work).OrderBy(e => e.Start).ToList();
        Assert.Equal([At(9), At(10, 15)], work.Select(e => e.Start));
        Assert.Equal([At(10), At(12)], work.Select(e => e.End!.Value));
        store.Insert(A.Work, At(12, 30), 3600);
        Assert.Equal(At(13), store.Entries[^1].End);
    }

    /// Work runs a focus round, Break a short one; the break after every 4th finished focus round is long.
    [Fact]
    public void TomatoRounds()
    {
        var p = new Pomodoro(25 * 60, 5 * 60, 15 * 60);
        var on = At(9);
        var entries = new List<Entry> { E(A.Work, At(8, 50)) };  // already working when the tomato went on
        var r = p.RoundOf(entries, on)!.Value;
        Assert.True(r.Phase == Pomodoro.Phase.Focus && r.Number == 1 && r.Start == on && r.End == At(9, 25));

        entries[0] = entries[0] with { End = At(9, 25) };
        entries.Add(E(A.Break, At(9, 25)));
        r = p.RoundOf(entries, on)!.Value;
        Assert.True(r.Phase == Pomodoro.Phase.ShortBreak && r.End == At(9, 30));

        // A focus round cut short doesn't count.
        entries[1] = entries[1] with { End = At(9, 30) };
        entries.AddRange([E(A.Work, At(9, 30), At(9, 40)), E(A.Lunch, At(9, 40))]);
        Assert.Null(p.RoundOf(entries, on));  // paused on another activity

        entries[3] = entries[3] with { End = At(10) };
        foreach (var h in new[] { 10, 11, 12 }) entries.Add(E(A.Work, At(h), At(h, 25)));
        entries.Add(E(A.Break, At(12, 25)));
        r = p.RoundOf(entries, on)!.Value;
        Assert.True(r.Phase == Pomodoro.Phase.LongBreak && r.End == At(12, 40));

        entries[^1] = entries[^1] with { End = At(12, 40) };
        entries.Add(E(A.Work, At(12, 40)));
        Assert.Equal(5, p.RoundOf(entries, on)?.Number);
    }

    /// The switch to Extra at the daily target doesn't restart the focus round.
    [Fact]
    public void TomatoRoundSpansExtra()
    {
        var r = new Pomodoro().RoundOf([E(A.Work, At(16, 50), At(17)), E(A.Extra, At(17))], At(16, 50))!.Value;
        Assert.True(r.Phase == Pomodoro.Phase.Focus && r.Number == 1 && r.Start == At(16, 50));
    }

    /// Work past the daily target is cut where the target was reached and carries on as Extra; lunch doesn't count.
    [Fact]
    public void WorkShiftsToExtraAtTarget()
    {
        var store = NewStore();
        var target = new Target(8 * 3600, At(0));
        store.Entries = [E(A.Work, At(9), At(12)), E(A.Lunch, At(12), At(13)), E(A.Work, At(13), notes: ["acme"])];
        Assert.False(store.ShiftToExtra(target, At(17, 59)));
        Assert.True(store.ShiftToExtra(target, At(18, 0, 30)));
        Assert.Equal(At(18), store.Entries[2].End);
        Assert.True(store.Running?.Activity == A.Extra && store.Running?.Start == At(18));
        Assert.Equal(["acme"], store.Running!.Value.Notes);
        Assert.False(store.ShiftToExtra(target, At(18, 1)));  // already Extra

        // Work started after the target is reached just becomes Extra.
        Clock.Source = () => At(18, 30);
        try { store.Stop(); } finally { Clock.Source = () => DateTimeOffset.Now; }
        store.Entries.Add(E(A.Work, At(19)));
        Assert.True(store.ShiftToExtra(target, At(19, 1)));
        Assert.True(store.Running?.Activity == A.Extra && store.Running?.Start == At(19));
    }

    [Fact]
    public void BreakAndDaysOffAtTarget()
    {
        var store = NewStore();
        var target = new Target(8 * 3600, At(0));
        store.Entries = [E(A.Work, At(9), At(16, 50)), E(A.Break, At(16, 50))];
        Assert.False(store.ShiftToExtra(target, At(17, 30)));  // only Work switches
        Assert.Equal(A.Break, store.Running?.Activity);

        // A day off owes nothing: Work there is Extra from the start.
        var dayOff = new Target(8 * 3600, At(0), DaysOff: new HashSet<string> { Day.DayKey() });
        store.Entries = [E(A.Work, At(10))];
        Assert.True(store.ShiftToExtra(dayOff, At(10, 1)));
        Assert.True(store.Entries.Count == 1 && store.Running?.Activity == A.Extra && store.Running?.Start == At(10));
    }

    [Fact]
    public void StretchReminders()
    {
        Assert.Equal(0, Stretch.Reminders(A.Work, At(9), 50, At(9, 49)));
        Assert.Equal(1, Stretch.Reminders(A.Work, At(9), 50, At(9, 50)));
        Assert.Equal(2, Stretch.Reminders(A.Extra, At(9), 50, At(10, 45)));
        Assert.Equal(0, Stretch.Reminders(A.Break, At(9), 50, At(11)));
        Assert.Equal(0, Stretch.Reminders(A.Work, At(9), 0, At(11)));
        var gate = new ReminderGate();
        Assert.True(gate.Due(At(9), 1));
        Assert.False(gate.Due(At(9), 1));
        Assert.True(gate.Due(At(9), 2));
        Assert.True(gate.Due(At(10), 1));  // a new stretch starts over
    }

    /// Files from before clients load as before; clients, the selection and each block's client survive a relaunch.
    [Fact]
    public void ClientsRoundTrip()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"outatime-test-{Guid.NewGuid()}", "data.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $$"""{"entries":[{"id":"{{Guid.NewGuid()}}","activity":"work","start":"2026-09-03T09:00:00Z","notes":[]}],"templates":[]}""");
        var store = new Store(path);
        Assert.True(store.Entries[0].Profile == null && store.Profiles.Count == 0);
        var acme = store.AddProfile("  Acme ")!.Value;
        Assert.Null(store.AddProfile("  "));
        store.CurrentProfile = acme;
        store.Entries[0] = store.Entries[0] with { Profile = acme };
        store.Commit();
        var reloaded = new Store(path);
        Assert.Equal(["Acme"], reloaded.Profiles.Select(p => p.Name));
        Assert.True(reloaded.CurrentProfile == acme && reloaded.Entries[0].Profile == acme);
        Assert.Equal(acme, new DayTemplate("t", reloaded.Entries).EntriesOn(Day)[0].Profile);
    }

    /// A package counts its client's billable time from its start day; a money one at its hourly rate; days off come
    /// out of the hours bank, one daily target each.
    [Fact]
    public void ObjectiveProgress()
    {
        Guid acme = Guid.NewGuid(), globex = Guid.NewGuid();
        var entries = new[]
        {
            E(A.Work, At(9), At(12), profile: acme), E(A.Break, At(12), At(12, 30)), E(A.Extra, At(13), At(14), profile: acme),
            E(A.Work, At(14), At(16), profile: globex), E(A.Work, Cal.AddDays(At(9), -1), Cal.AddDays(At(17), -1), profile: acme),
        };
        var hours = new Objective(ObjectiveKind.Hours, 80, profile: acme, since: At(10));
        var p = hours.Progress(entries, 0, 8 * 3600);
        Assert.True(p.Done == 4 * 3600 && p.Goal == 80 * 3600 && p.Left == 76 * 3600 && !p.IsDone);

        var money = new Objective(ObjectiveKind.Money, 400, 100, profile: acme, since: Day);
        Assert.True(money.Progress(entries, 0, 0).IsDone);
        Assert.Equal(0, new Objective(ObjectiveKind.Money, 400, profile: acme, since: Day).Progress(entries, 0, 0).Goal);

        var vacation = new Objective(ObjectiveKind.DaysOff, 5);
        var v = vacation.Progress([], 12 * 3600, 8 * 3600);
        Assert.True(v.Goal == 40 * 3600 && v.Left == 28 * 3600 && Math.Abs(v.Fraction - 0.3) < 1e-9);
        Assert.Equal(0, vacation.Progress([], -3600, 8 * 3600).Done);

        Assert.Equal("43h", (42.2 * 3600).Short());
        Assert.Equal("6h 12m", (6 * 3600 + 720.0).Short());
        Assert.Equal("35m", 2050.0.Short());
    }

    [Fact]
    public void ObjectivesRoundTrip()
    {
        var store = NewStore();
        Assert.Empty(store.Objectives);
        var acme = store.AddProfile("Acme")!.Value;
        store.Objectives.Add(new Objective(ObjectiveKind.Money, 8000, 100, "BRL", acme, Day));
        store.Update(store.Objectives[0] with { Name = "Retainer" });
        var reloaded = new Store(store.Path);
        Assert.Equal(store.Objectives, reloaded.Objectives);
        Assert.Equal("Retainer", reloaded.Objectives[0].Name);
    }

    [Fact]
    public void SwitchingClientSplitsRunningBlock()
    {
        var store = NewStore();
        var acme = store.AddProfile("Acme")!.Value;
        var globex = store.AddProfile("Globex")!.Value;
        store.Select(acme);
        store.Start(A.Break);
        Assert.Null(store.Running?.Profile);  // breaks are never billed
        store.Entries = [E(A.Work, At(9), notes: ["x"], profile: acme)];
        store.Select(globex, At(9, 0, 40));  // a quick correction relabels
        Assert.True(store.Entries.Count == 1 && store.Running?.Profile == globex);
        store.Select(acme, At(10));
        Assert.True(store.Entries.Count == 2 && store.Entries[0].End == At(10) && store.Entries[0].Profile == globex);
        Assert.True(store.Running?.Start == At(10) && store.Running?.Profile == acme && store.Running?.Notes.Count == 0);
        store.Select(acme, At(11));
        Assert.Equal(2, store.Entries.Count);
    }

    [Fact]
    public void ClientCarriesOverMidnightAndIntoExtra()
    {
        var store = NewStore();
        var acme = store.AddProfile("Acme")!.Value;
        var globex = store.AddProfile("Globex")!.Value;
        store.Entries = [E(A.Work, At(17), profile: acme)];
        store.RollOver(At(17).AddHours(16));
        Assert.True(store.Running?.Profile == acme && store.RunningSince == At(17));
        store.Entries = [E(A.Work, At(9), At(10), profile: globex), E(A.Work, At(10), profile: acme)];
        Assert.Equal(At(10), store.RunningSince);  // another client's block isn't the same stretch
        store.ShiftToExtra(new Target(8 * 3600, At(0)), At(17, 30));
        Assert.True(store.Running?.Activity == A.Extra && store.Running?.Profile == acme);
        store.Entries[^1] = store.Entries[^1] with { End = At(17, 45) };
        store.CurrentProfile = globex;
        store.Insert(A.Travel, At(18), 600);
        Assert.Equal(globex, store.Entries[^1].Profile);
        store.Insert(A.Break, At(18, 30), 600);
        Assert.Null(store.Entries[^1].Profile);
    }

    /// A removed client leaves the picker but keeps its blocks and its name.
    [Fact]
    public void RemovingClientArchives()
    {
        var store = NewStore();
        var acme = store.AddProfile("Acme")!.Value;
        store.Select(acme);
        store.Entries = [E(A.Work, At(9), profile: acme)];
        store.RemoveProfile(acme);
        Assert.True(store.ActiveProfiles.Count == 0 && store.ProfileNames[acme] == "Acme");
        Assert.True(store.CurrentProfile == null && store.Running?.Profile == acme && store.Entries.Count == 1);
    }

    /// Client column in the entries, a Clients sheet in the month report, and a client's own month for invoicing.
    [Fact]
    public void ClientExports()
    {
        var acme = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [acme] = "Acme" };
        var entries = new[]
        {
            E(A.Work, At(9), At(12), ["api"], acme), E(A.Break, At(12), At(12, 15)), E(A.Work, At(12, 15), At(13, 15)),
            E(A.Travel, At(14), At(14, 30), profile: acme),
        };
        var csv = Csv.Entries(entries, names).Split('\n');
        Assert.EndsWith(",Hours,Client", csv[0]);
        Assert.EndsWith(",3.00,Acme", csv[1]);
        Assert.EndsWith(",0.25,", csv[2]);

        var target = new Target(8 * 3600, Day);
        var month = Text(Report.Month(entries, Day, target, names, At(22)));
        Assert.Contains("<sheet name=\"Clients\"", month);
        Assert.Contains("name=\"tblClients\" displayName=\"tblClients\" ref=\"A1:E4\" totalsRowCount=\"1\"", month);  // Acme, No client, total
        Assert.Contains("<f>SUBTOTAL(109,tblClients[Total])</f><v>4.5</v>", month);
        Assert.Contains(">No client<", month);
        Assert.DoesNotContain("Clients", Text(Report.Month([entries[1]], Day, target, now: At(22))));  // not using clients: no sheet

        var report = Text(Report.Client(entries.Where(e => e.Profile == acme).ToList(), "Acme", Day));
        Assert.Contains("<sheet name=\"Days\"", report);
        Assert.Contains("<f>SUBTOTAL(109,tblClientDays[Total])</f><v>3.5</v>", report);
        Assert.DoesNotContain("Balance", report);
    }

    /// Both Export menus build from the store, so client names can't be left out; removed clients can still be billed.
    [Fact]
    public void StoreExportsCarryClientNames()
    {
        var store = NewStore();
        var acme = store.AddProfile("Acme")!.Value;
        var globex = store.AddProfile("Globex")!.Value;
        store.AddProfile("Idle");
        store.Entries = [E(A.Work, At(9), At(12), profile: acme), E(A.Travel, At(13), At(14), profile: globex)];
        store.RemoveProfile(globex);
        Assert.Equal(["Acme", "Globex"], store.BilledClients(Day).Select(p => p.Name));
        var target = new Target(8 * 3600, Day);
        var month = Text(store.MonthReport(Day, target, At(22)));
        Assert.True(month.Contains(">Acme<") && month.Contains(">Globex<") && !month.Contains(">No client<"));
        Assert.Contains(">Acme<", Text(store.MasterWorkbook(target, At(22))));
        Assert.Contains(",3.00,Acme", store.EntriesCsv(Day));
        var report = Text(store.ClientReport(store.Profiles[0], Day));
        Assert.True(report.Contains(">Acme<") && !report.Contains(">Globex<"));
    }

    /// Switching clients restarts the client's timer but not the stretch reminder.
    [Fact]
    public void StretchCountsAcrossClientSwitch()
    {
        var store = NewStore();
        var acme = store.AddProfile("Acme")!.Value;
        var globex = store.AddProfile("Globex")!.Value;
        store.Entries = [E(A.Break, At(8, 45), At(9)), E(A.Work, At(9), At(9, 30), profile: acme), E(A.Work, At(9, 30), profile: globex)];
        Assert.Equal(At(9, 30), store.RunningSince);
        Assert.Equal(At(9), store.SeatedSince);
    }

    [Fact]
    public void ClientExportNameIsFileSafe() => Assert.Equal("Outatime 2026-09 A-B- C.xlsx", Report.ClientExportName(Day, "A/B: C"));

    /// Two full weeks (10h days, then 7h days) and a Saturday of Extra; "now" is Wednesday noon of the third week.
    [Fact]
    public void Stats()
    {
        var aug31 = Cal.At(2026, 8, 31);
        var week1 = Cal.Week(aug31).Start;
        Entry On(int d, int from, int to, A a = A.Work)
        {
            var date = Cal.AddDays(aug31, d);
            return E(a, Cal.Setting(date, from), Cal.Setting(date, to));
        }
        var entries = Enumerable.Range(0, 5).Select(d => On(d, 9, 19)).Concat(Enumerable.Range(7, 5).Select(d => On(d, 9, 16)))
            .Append(On(12, 10, 11, A.Extra)).Concat([On(14, 9, 17), On(15, 9, 17), On(16, 9, 11)]).ToList();
        var now = Cal.Setting(entries[^1].Start, 12);
        var target = new Target(8 * 3600, week1);
        var s = new Stats(entries, target, week1, now);

        Assert.True(s.WeekGoal == 40 * 3600 && s.WeekWorked == 18 * 3600 && s.WeekLeft == 22 * 3600);
        Assert.True(s.Bank == 6 * 3600 && s.ToDayOff == 2 * 3600 && s.DaysOffBanked == 0);
        Assert.True(Math.Abs(s.AverageWeek!.Value - 43 * 3600) < 1);
        Assert.Null(s.AverageMonth);  // August started mid-month, September isn't over
        Assert.True(Math.Abs(s.AverageDay!.Value - 104.0 / 14 * 3600) < 1);
        Assert.True(s.UsualStart == 9 * 60 && s.UsualFinish == 17 * 60);
        Assert.True(s.LongestDay?.Worked == 10 * 3600 && Cal.Date(s.LongestDay!.Value.Date).Day == 1);
        Assert.True(s.ExtraThisMonth == 3600 && s.DaysWorkedThisMonth == 13);

        // A long Wednesday: 11h still to go this week, and more than a day banked.
        var rich = new Stats([.. entries, On(16, 12, 23)], target, week1, Cal.Setting(now, 23, 30));
        Assert.Equal(11 * 3600, rich.WeekLeft);
        Assert.True(rich.DaysOffBanked == 1 && rich.Bank - 8 * 3600 == 3600 * 3);
    }

    /// Several blocks at once: a client only lands on billable ones, a non-billable activity drops it.
    [Fact]
    public void SelectionEdits()
    {
        var store = NewStore();
        var acme = store.AddProfile("Acme")!.Value;
        Entry w = E(A.Work, At(9), At(10)), b = E(A.Break, At(10), At(10, 15)), x = E(A.Extra, At(18), At(19));
        store.Entries = [w, b, x];
        store.Assign(new HashSet<Guid> { w.Id, b.Id, x.Id }, acme);
        Assert.Equal([acme, null, acme], store.Entries.Select(e => e.Profile));
        store.SetActivity(new HashSet<Guid> { w.Id, b.Id }, A.Lunch);
        Assert.Equal([A.Lunch, A.Lunch, A.Extra], store.Entries.Select(e => e.Activity));
        Assert.Null(store.Entries[0].Profile);
        store.Delete(new HashSet<Guid> { b.Id, x.Id });
        Assert.Equal([w.Id], store.Entries.Select(e => e.Id));
    }

    [Fact]
    public void Totals()
    {
        Assert.Equal("1h 30m", 5400.0.Hm());
        Assert.Equal(3900, Store.TotalsOf([E(A.Work, At(9), At(10)), E(A.Work, At(11), At(11, 5))])[A.Work]);
    }
}

public class WhatsNewTests
{
    static readonly List<WhatsNew.Release> Releases = new[] { "1.0.15", "1.0.14", "1.0.13" }.Select(v => new WhatsNew.Release(v)).ToList();

    static List<string> Shown(string? lastSeen, string current, bool hasData = true) =>
        WhatsNew.Unseen(Releases, lastSeen, current, hasData).Select(r => r.Version).ToList();

    [Fact]
    public void ShowsWhatsNewerThanLastSeen()
    {
        Assert.Equal(["1.0.15"], Shown("1.0.14", "1.0.15"));
        Assert.Equal(["1.0.15", "1.0.14", "1.0.13"], Shown("1.0.12", "1.0.15"));  // skipped releases too
        Assert.Empty(Shown("1.0.14", "1.0.14"));
        Assert.Equal(["1.0.14"], Shown("1.0.13", "1.0.14"));  // notes for an unreleased version stay hidden
        Assert.Equal(["1.0.13"], Shown("1.0.9", "1.0.13"));  // numeric, not alphabetical
    }

    [Fact]
    public void FirstLaunch()
    {
        Assert.Empty(Shown(null, "1.0.13", hasData: false));
        Assert.Equal(["1.0.13"], Shown(null, "1.0.13", hasData: true));
    }

    [Fact]
    public void BetasLeaveWhatsNewAlone()
    {
        var releases = new List<WhatsNew.Release> { new("1.3"), new("1.2") };
        Assert.Null(WhatsNew.CatchUpFrom(releases, "1.2", "1.3-beta.1", true));
        Assert.False(WhatsNew.RecordsLastSeen("1.3-beta.1"));
        Assert.True(WhatsNew.RecordsLastSeen("1.3"));
        Assert.Equal("1.2", WhatsNew.CatchUpFrom(releases, "1.2", "1.3", true));
    }

    [Fact]
    public void EveryReleaseHasNotes() => Assert.All(WhatsNew.Releases, r => Assert.True(r.New.Count + r.Fixed.Count > 0));
}
