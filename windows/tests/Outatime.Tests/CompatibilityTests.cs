using System.Text.RegularExpressions;
using Xunit;

namespace Outatime.Tests;

/// data.json moves between the Mac and Windows apps.
public class CompatibilityTests
{
    static readonly string Fixture = File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", "mac-data.json"));

    [Fact]
    public void ReadsTheMacFile()
    {
        var c = DataFile.Read(Fixture);
        var acme = Guid.Parse("6B1E5C1A-3F1D-4C2B-9E60-2E8F7E0C9A11");
        Assert.Equal(acme, c.Current);
        Assert.Equal(["2026-09-07"], c.DaysOff);
        Assert.Equal(2, c.Entries.Count);
        var work = c.Entries[0];
        Assert.Equal(Activity.Work, work.Activity);
        Assert.Equal(DateTimeOffset.Parse("2026-09-03T12:00:00Z"), work.Start);
        Assert.Equal(3.5 * 3600, work.Duration);
        Assert.Equal(["api, \"v2\"", "review"], work.Notes);
        Assert.Equal(acme, work.Profile);
        Assert.True(c.Entries[1].IsRunning && c.Entries[1].Activity == Activity.OutOfOffice && c.Entries[1].Profile == null);
        Assert.Equal(["Acme", "Globex"], c.Profiles.Select(p => p.Name));
        Assert.True(c.Profiles[1].Archived);
        Assert.Equal(ObjectiveKind.Money, c.Objectives[0].Kind);
        Assert.Equal(8000, c.Objectives[0].Amount);
        Assert.Equal("BRL", c.Objectives[0].Currency);
        Assert.Null(c.Objectives[1].Profile);
        Assert.Equal(2, c.Templates[0].Slots.Count);
        Assert.Equal(540, c.Templates[0].Slots[0].StartMinute);
        Assert.Null(c.Templates[0].Slots[1].Profile);
    }

    /// Written back, it's the Mac's file again: same keys, same formats, same values.
    [Fact]
    public void WritesWhatTheMacWrites()
    {
        var written = DataFile.Write(DataFile.Read(Fixture));
        Assert.Equal(Normalize(Fixture), Normalize(written));
    }

    /// Swift's ISO 8601 decoder rejects fractional seconds; sub-second instants must lose them on the way out.
    [Fact]
    public void DatesAreWholeSecondsUtc()
    {
        var start = DateTimeOffset.Parse("2026-09-03T09:00:00.678-03:00");
        var json = DataFile.Write(new DataFile.Contents([new Entry(Activity.Work, start)], [], [], [], null, []));
        Assert.Contains("\"start\" : \"2026-09-03T12:00:00Z\"", json);
        Assert.Matches("\"id\" : \"[0-9A-F-]{36}\"", json);
        Assert.DoesNotContain("\"end\"", json);
        Assert.DoesNotContain("\"current\"", json);
    }

    /// Files from before 1.1 kept one "tag" per entry.
    [Fact]
    public void ReadsOldTags()
    {
        var c = DataFile.Read("""{"entries":[{"id":"0F2A9D3E-5B7C-4E21-8A6D-1C3B5E7F9A02","activity":"work","start":"2026-09-03T09:00:00Z","tag":"acme"},{"id":"0F2A9D3E-5B7C-4E21-8A6D-1C3B5E7F9A03","activity":"break","start":"2026-09-03T10:00:00Z","tag":""}],"templates":[]}""");
        Assert.Equal(["acme"], c.Entries[0].Notes);
        Assert.Empty(c.Entries[1].Notes);
        Assert.Empty(c.Profiles);
        Assert.Empty(c.Objectives);
    }

    /// An unreadable file is set aside, never overwritten.
    [Fact]
    public void BrokenFileIsKept()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"outatime-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, "data.json");
        File.WriteAllText(path, "{ not json");
        var store = new Store(path);
        Assert.Empty(store.Entries);
        Assert.Single(Directory.GetFiles(dir, "data.json.broken-*"));
    }

    static string Normalize(string json) => Regex.Replace(json, @"\s+", "");
}

public class LocalizationTests
{
    static string AppSource => System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../src"));

    /// Every string the app translates has its Spanish and Brazilian Portuguese, so a new one can't ship half-done.
    [Fact]
    public void EveryStringIsTranslated()
    {
        var keys = new HashSet<string>();
        var call = new Regex("""(?:Loc\.T|\bT)\(\s*"((?:[^"\\]|\\.)*)"(?=\s*[,)])""");
        foreach (var file in Directory.EnumerateFiles(AppSource, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}")) continue;
            foreach (Match m in call.Matches(File.ReadAllText(file))) keys.Add(Regex.Unescape(m.Groups[1].Value));
        }
        keys.UnionWith(Activities.All.Select(a => a.Name()));
        keys.UnionWith(["Vacation", "Hours Package", "Salary", "System"]);
        foreach (var r in WhatsNew.Releases) foreach (var i in r.New.Concat(r.Fixed)) keys.UnionWith([i.Title, i.Detail]);
        Assert.True(keys.Count > 40, $"only {keys.Count} keys found under {AppSource}");
        var missing = keys.Where(k => !Loc.Strings.TryGetValue(k, out var row) || !row.ContainsKey("es") || !row.ContainsKey("pt-BR")).Order().ToList();
        Assert.True(missing.Count == 0, "missing translations:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void PlaceholdersFill()
    {
        Assert.Equal("Week 37", Loc.Format("Week %lld", [37]));
        Assert.Equal("2h left of 8h", Loc.Format("%@ left of %@", ["2h", "8h"]));
        Assert.Equal("b a", Loc.Format("%2$@ %1$@", ["a", "b"]));
    }

    [Fact]
    public void ManualFollowsLanguage()
    {
        Assert.Equal("https://mrbarkan.github.io/Outatime/manual/", Language.En.ManualUrl());
        Assert.Equal("https://mrbarkan.github.io/Outatime/manual/pt-BR/", Language.PtBR.ManualUrl());
    }
}
