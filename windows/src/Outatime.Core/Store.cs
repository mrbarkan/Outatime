namespace Outatime;

/// Entries, templates, days off, clients and objectives, saved to data.json on every change. Mutate through the
/// methods below, or assign a whole list; code that edits a list in place calls Commit() after.
public sealed class Store
{
    List<Entry> entries = [];
    List<DayTemplate> templates = [];
    HashSet<string> daysOff = [];
    List<Profile> profiles = [];
    Guid? currentProfile;
    List<Objective> objectives = [];
    DateTimeOffset? tomatoSince;

    public List<Entry> Entries { get => entries; set { entries = value; Commit(); } }
    public List<DayTemplate> Templates { get => templates; set { templates = value; Commit(); } }
    public HashSet<string> DaysOff { get => daysOff; set { daysOff = value; Commit(); } }
    public List<Profile> Profiles { get => profiles; set { profiles = value; Commit(); } }
    /// The client picked in the panel; new billable blocks get it.
    public Guid? CurrentProfile { get => currentProfile; set { currentProfile = value; Commit(); } }
    public List<Objective> Objectives { get => objectives; set { objectives = value; Commit(); } }
    /// When the tomato timer was turned on; null while it's off. Not saved: quitting turns it off.
    public DateTimeOffset? TomatoSince { get => tomatoSince; set { tomatoSince = value; Changed?.Invoke(); } }
    /// A block added in the Logbook opens its editor once it appears.
    public Guid? JustAdded;

    /// After every change, saved or not.
    public event Action? Changed;

    public readonly string Path;
    readonly bool loaded;

    public static string DefaultFolder
    {
        get
        {
            if (Environment.GetEnvironmentVariable("OUTATIME_HOME") is { Length: > 0 } home) return home;
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            // On the Mac that develops it, keep well away from the Mac app's own folder.
            return System.IO.Path.Combine(local, OperatingSystem.IsWindows() ? "Outatime" : "Outatime for Windows (dev)");
        }
    }

    public static string DefaultPath => System.IO.Path.Combine(DefaultFolder, "data.json");

    public Store(string? path = null)
    {
        Path = path ?? DefaultPath;
        if (File.Exists(Path))
        {
            try
            {
                Load(DataFile.Read(File.ReadAllText(Path)));
            }
            catch (Exception)
            {
                // Unreadable file: keep it aside so the next save can't silently destroy it.
                try { File.Move(Path, $"{Path}.broken-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}"); } catch (IOException) { }
            }
        }
        loaded = true;
    }

    void Load(DataFile.Contents c)
    {
        entries = c.Entries; templates = c.Templates; daysOff = c.DaysOff; profiles = c.Profiles;
        currentProfile = c.Current; objectives = c.Objectives;
    }

    public DataFile.Contents Contents => new(entries, templates, daysOff, profiles, currentProfile, objectives);

    public void Commit()
    {
        if (loaded) Save();
        Changed?.Invoke();
    }

    void Save()
    {
        // ponytail: whole-file rewrite on every change; fine for years of entries (a few hundred KB).
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, DataFile.Write(Contents));
            File.Move(tmp, Path, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// Replaces everything with another data.json (Settings → Import). Throws when the file can't be read, leaving
    /// the store as it was.
    public void Import(string json)
    {
        Load(DataFile.Read(json));
        Commit();
    }

    // MARK: Tracking

    public Entry? Running => entries.Cast<Entry?>().FirstOrDefault(e => e!.Value.IsRunning);
    int RunningIndex => entries.FindIndex(e => e.IsRunning);

    public void Start(Activity activity)
    {
        var profile = activity.Billable() ? currentProfile : null;
        if (Running is { } r && r.Activity == activity && r.Profile == profile) return;
        StopRunning();
        entries.Add(new Entry(activity, Clock.Now, profile: profile));
        Commit();
    }

    /// Picks the client for billable blocks. A billable block running for another client ends here and carries on for
    /// this one; within its first minute it's taken as a correction and just relabelled.
    public void Select(Guid? profile, DateTimeOffset? now = null)
    {
        var n = now ?? Clock.Now;
        currentProfile = profile;
        var i = RunningIndex;
        if (i >= 0 && entries[i].Activity.Billable() && entries[i].Profile != profile)
        {
            var e = entries[i];
            if (n.Since(e.Start) < 60)
            {
                e.Profile = profile;
                entries[i] = e;
            }
            else
            {
                e.End = n;
                entries[i] = e;
                entries.Add(new Entry(e.Activity, n, profile: profile));
            }
        }
        Commit();
    }

    /// The running entry, or today's latest one so a note can still land on a block after it was stopped.
    public Entry? NoteTarget => Running ?? EntriesOn(Clock.Now).Cast<Entry?>().LastOrDefault();

    public void AddNote(string text)
    {
        text = text.Trim();
        if (text.Length == 0 || NoteTarget is not { } target) return;
        var i = entries.FindIndex(e => e.Id == target.Id);
        if (i < 0) return;
        var e = entries[i];
        e.Notes = [.. e.Notes, text];
        entries[i] = e;
        Commit();
    }

    public void Stop()
    {
        if (StopRunning()) Commit();
    }

    bool StopRunning()
    {
        var i = RunningIndex;
        if (i < 0) return false;
        var e = entries[i];
        e.End = Clock.Now;
        entries[i] = e;
        return true;
    }

    /// A timer left running past midnight is cut there and carries on as a new entry, so each day keeps its own hours.
    public void RollOver(DateTimeOffset? now = null)
    {
        var n = now ?? Clock.Now;
        var changed = false;
        int i;
        while ((i = RunningIndex) >= 0 && Cal.StartOfDay(entries[i].Start) < Cal.StartOfDay(n))
        {
            var e = entries[i];
            var midnight = Cal.AddDays(Cal.StartOfDay(e.Start), 1);
            e.End = midnight;
            entries[i] = e;
            entries.Add(new Entry(e.Activity, midnight, notes: e.Notes, profile: e.Profile));
            changed = true;
        }
        if (changed) Commit();
    }

    /// Work past the daily target carries on as Extra, cut where the target was reached rather than when this runs.
    /// Days that owe nothing (weekends, days off) are all Extra.
    public bool ShiftToExtra(Target target, DateTimeOffset? now = null)
    {
        var n = now ?? Clock.Now;
        var i = RunningIndex;
        if (target.Seconds <= 0 || i < 0 || entries[i].Activity != Activity.Work) return false;
        var worked = EntriesOn(n).GroupBy(e => e.Activity)
            .ToDictionary(g => g.Key, g => g.Sum(e => Math.Max(0, (e.End ?? n).Since(e.Start))))
            .Worked(target.Excluded);
        var over = worked - target.Owed(n, n);
        if (over < 0) return false;
        var cut = n.Plus(-over);
        var e = entries[i];
        if (cut <= e.Start)
        {
            e.Activity = Activity.Extra;
            entries[i] = e;
        }
        else
        {
            e.End = cut;
            entries[i] = e;
            entries.Add(new Entry(Activity.Extra, cut, notes: e.Notes, profile: e.Profile));
        }
        Commit();
        return true;
    }

    /// Start of the running stretch, followed back across midnight cuts.
    public DateTimeOffset? RunningSince => Since(sameClient: true);
    /// The same, across client switches too: switching clients doesn't get you out of the chair.
    public DateTimeOffset? SeatedSince => Since(sameClient: false);

    DateTimeOffset? Since(bool sameClient)
    {
        if (Running is not { } running) return null;
        var since = running.Start;
        while (entries.Cast<Entry?>().FirstOrDefault(e => e!.Value.Activity == running.Activity
                   && (!sameClient || e.Value.Profile == running.Profile) && e.Value.Start < since
                   && e.Value.End is { } end && Math.Abs(end.Since(since)) < 1) is { } prev)
            since = prev.Start;
        return since;
    }

    // MARK: Clients

    public List<Profile> ActiveProfiles => profiles.Where(p => !p.Archived).ToList();
    public Dictionary<Guid, string> ProfileNames => profiles.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First().Name);

    public Guid? AddProfile(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return null;
        var p = new Profile(name);
        profiles.Add(p);
        Commit();
        return p.Id;
    }

    public void Rename(Guid id, string name)
    {
        var i = profiles.FindIndex(p => p.Id == id);
        if (i < 0) return;
        profiles[i] = profiles[i] with { Name = name };
        Commit();
    }

    /// Archived, not deleted: its blocks keep their client. A block running for it carries on.
    public void RemoveProfile(Guid id)
    {
        var i = profiles.FindIndex(p => p.Id == id);
        if (i < 0) return;
        profiles[i] = profiles[i] with { Archived = true };
        if (currentProfile == id) currentProfile = null;
        Commit();
    }

    // MARK: Exports

    public byte[] MonthReport(DateTimeOffset month, Target target, DateTimeOffset? now = null) =>
        Report.Month(EntriesInMonth(month), month, target, ProfileNames, now);

    public byte[] MasterWorkbook(Target target, DateTimeOffset? now = null) => Report.Master(entries, target, ProfileNames, now);

    public string EntriesCsv(DateTimeOffset month) => Csv.Entries(EntriesInMonth(month), ProfileNames);

    /// Clients with billable time in `month`, removed ones too: there may still be a month to bill them for.
    public List<Profile> BilledClients(DateTimeOffset month)
    {
        var billed = EntriesInMonth(month).Where(e => e.Activity.Billable() && e.Profile != null).Select(e => e.Profile!.Value).ToHashSet();
        return profiles.Where(p => billed.Contains(p.Id)).ToList();
    }

    public byte[] ClientReport(Profile client, DateTimeOffset month) =>
        Report.Client(EntriesInMonth(month).Where(e => e.Profile == client.Id).ToList(), client.Name, month);

    // MARK: Queries

    public List<Entry> EntriesOn(DateTimeOffset day) => entries.Where(e => Cal.SameDay(e.Start, day)).OrderBy(e => e.Start).ToList();

    public List<Entry> EntriesInMonth(DateTimeOffset month) => entries.Where(e => Cal.SameMonth(e.Start, month)).OrderBy(e => e.Start).ToList();

    public Dictionary<Activity, double> TotalsOn(DateTimeOffset day) => TotalsOf(EntriesOn(day));

    public static Dictionary<Activity, double> TotalsOf(IEnumerable<Entry> entries) =>
        entries.GroupBy(e => e.Activity).ToDictionary(g => g.Key, g => g.Sum(e => e.Duration));

    public Target Target(double hours, string excluded) =>
        new(hours * 3600, entries.Count > 0 ? entries.Min(e => e.Start) : Clock.Now, excluded, daysOff);

    public (double Worked, double Balance) Balance(Interval interval, Target target, DateTimeOffset? now = null) =>
        BalanceOf(entries, interval, target, now);

    /// Worked time over the days in `interval`, and their balance against what each day owes.
    public static (double Worked, double Balance) BalanceOf(IEnumerable<Entry> entries, Interval interval, Target target, DateTimeOffset? now = null)
    {
        var n = now ?? Clock.Now;
        var byDay = entries.Where(e => e.Start >= interval.Start && e.Start < interval.End)
            .GroupBy(e => Cal.StartOfDay(e.Start)).ToDictionary(g => g.Key, g => g.ToList());
        double worked = 0, balance = 0;
        for (var d = Cal.StartOfDay(interval.Start); d < interval.End; d = Cal.AddDays(d, 1))
        {
            var w = byDay.TryGetValue(d, out var es) ? TotalsOf(es).Worked(target.Excluded) : 0;
            worked += w;
            balance += target.Balance(w, d, n);
        }
        return (worked, balance);
    }

    // MARK: Editing

    public void AddEntry(DateTimeOffset day, DateTimeOffset? start = null) =>
        Insert(Activity.Work, start ?? EntriesOn(day).LastOrDefault().End ?? Cal.Setting(day, 9), 3600);

    /// Adds an entry at `start`, at most `length` long. Inside a block it cuts that block around itself (a break in the
    /// middle of work); in a gap it stops at the next block instead of overlapping it.
    public void Insert(Activity activity, DateTimeOffset start, double length, DateTimeOffset? now = null)
    {
        var n = now ?? Clock.Now;
        var end = start.Plus(length);
        var i = entries.FindIndex(e => e.Start <= start && start < (e.End ?? n));
        if (i >= 0)
        {
            var host = entries[i];
            var hostEnd = host.End ?? n;
            if (hostEnd < end) end = hostEnd;
            if (end < hostEnd)
                entries.Add(host with { Id = Guid.NewGuid(), Start = end });  // a running host keeps running in its tail
            if (host.Start == start) entries.RemoveAt(i);
            else entries[i] = host with { End = start };
        }
        else if (entries.Where(e => e.Start > start).Select(e => e.Start).DefaultIfEmpty(DateTimeOffset.MaxValue).Min() is var next
                 && next < end)
        {
            end = next;
        }
        var added = new Entry(activity, start, end, profile: activity.Billable() ? currentProfile : null);
        entries.Add(added);
        JustAdded = added.Id;
        Commit();
    }

    /// Writes back an edited copy of an entry. Id-based, so an editor that fires after the entry was deleted does nothing.
    public void Update(Entry entry)
    {
        var i = entries.FindIndex(e => e.Id == entry.Id);
        if (i < 0) return;
        entries[i] = entry;
        Commit();
    }

    public void Update(Objective objective)
    {
        var i = objectives.FindIndex(o => o.Id == objective.Id);
        if (i < 0) return;
        objectives[i] = objective;
        Commit();
    }

    public Entry? Find(Guid id) => entries.Cast<Entry?>().FirstOrDefault(e => e!.Value.Id == id);

    public void Delete(Guid id)
    {
        entries.RemoveAll(e => e.Id == id);
        Commit();
    }

    // MARK: Several blocks at once (one save each)

    public void Delete(IReadOnlySet<Guid> ids)
    {
        entries.RemoveAll(e => ids.Contains(e.Id));
        Commit();
    }

    /// Only billable blocks take a client; the rest of the selection is left alone.
    public void Assign(IReadOnlySet<Guid> ids, Guid? profile)
    {
        entries = entries.Select(e => ids.Contains(e.Id) && e.Activity.Billable() ? e with { Profile = profile } : e).ToList();
        Commit();
    }

    /// A non-billable activity drops the client.
    public void SetActivity(IReadOnlySet<Guid> ids, Activity activity)
    {
        entries = entries.Select(e => !ids.Contains(e.Id) ? e
            : e with { Activity = activity, Profile = activity.Billable() ? e.Profile : null }).ToList();
        Commit();
    }

    public void Apply(DayTemplate template, DateTimeOffset day)
    {
        entries.RemoveAll(e => Cal.SameDay(e.Start, day) && !e.IsRunning);
        entries.AddRange(template.EntriesOn(day));
        Commit();
    }

    public void SetDayOff(DateTimeOffset day, bool off)
    {
        if (off) daysOff.Add(day.DayKey()); else daysOff.Remove(day.DayKey());
        Commit();
    }
}
