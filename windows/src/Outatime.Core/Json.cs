using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Outatime;

/// data.json, written the way the Mac app's JSONEncoder writes it so either app can read the other's file: dates as
/// whole-second UTC ISO 8601 ("2026-09-22T13:04:05Z" — a fraction makes the Mac reject the file), upper-case UUIDs,
/// nil optionals left out, keys sorted. Objectives and template slots always carry every field, because Swift's
/// synthesized decoder requires them.
public static class DataFile
{
    public sealed record Contents(List<Entry> Entries, List<DayTemplate> Templates, HashSet<string> DaysOff, List<Profile> Profiles,
                                  Guid? Current, List<Objective> Objectives);

    // MARK: Reading

    public static Contents Read(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new FormatException("data.json is not an object");
        return new(
            Array(root, "entries", true).Select(ReadEntry).ToList(),
            Array(root, "templates", true).Select(ReadTemplate).ToList(),
            Array(root, "daysOff", false).Select(n => Str(n)).ToHashSet(),  // added in 1.0.11
            Array(root, "profiles", false).Select(ReadProfile).ToList(),  // added in 1.2
            OptGuid(root, "current"),
            Array(root, "objectives", false).Select(ReadObjective).ToList());  // added in 1.3
    }

    static IEnumerable<JsonNode> Array(JsonObject o, string key, bool required)
    {
        if (o[key] is JsonArray a) return a.Select(n => n ?? throw new FormatException($"null in {key}"));
        if (required) throw new FormatException($"missing {key}");
        return [];
    }

    static string Str(JsonNode? n) => n?.GetValue<string>() ?? throw new FormatException("expected a string");
    static JsonNode Req(JsonNode o, string key) => o[key] ?? throw new FormatException($"missing {key}");
    static Guid ReqGuid(JsonNode o, string key) => Guid.Parse(Str(Req(o, key)));
    static Guid? OptGuid(JsonNode o, string key) => o[key] is { } n ? Guid.Parse(Str(n)) : null;
    static double Num(JsonNode n) => n.GetValue<double>();

    public static DateTimeOffset ParseDate(string s) =>
        DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToLocalTime();

    static DateTimeOffset ReqDate(JsonNode o, string key) => ParseDate(Str(Req(o, key)));

    static Activity ReadActivity(JsonNode o) =>
        Activities.FromRaw(Str(Req(o, "activity"))) ?? throw new FormatException($"unknown activity {o["activity"]}");

    static List<string> Notes(JsonNode o) => o["notes"] is JsonArray a ? a.Select(Str).ToList() : [];

    static Entry ReadEntry(JsonNode o)
    {
        // ponytail: pre-1.1 files stored a single "tag"; fold it into notes.
        var notes = o["notes"] is JsonArray ? Notes(o) : o["tag"] is { } tag && Str(tag) is { Length: > 0 } t ? [t] : new List<string>();
        return new Entry(ReadActivity(o), ReqDate(o, "start"), o["end"] is { } end ? ParseDate(Str(end)) : null, notes,
                         OptGuid(o, "profile"), ReqGuid(o, "id"));
    }

    static DayTemplate ReadTemplate(JsonNode o) => new(ReqGuid(o, "id"), Str(Req(o, "name")), Req(o, "slots").AsArray().Select(s =>
        new Slot(ReadActivity(s!), Req(s!, "startMinute").GetValue<int>(), Req(s!, "endMinute").GetValue<int>(),
                 Notes(s!), OptGuid(s!, "profile"))).ToList());  // pre-1.1 slots had "tag"; templates drop it

    static Profile ReadProfile(JsonNode o) => new(ReqGuid(o, "id"), Str(Req(o, "name")), o["archived"]?.GetValue<bool>() ?? false);

    static Objective ReadObjective(JsonNode o) => new()
    {
        Id = ReqGuid(o, "id"),
        Kind = ObjectiveKinds.FromRaw(Str(Req(o, "kind"))) ?? throw new FormatException("unknown objective kind"),
        Name = Str(Req(o, "name")),
        Amount = Num(Req(o, "amount")),
        Rate = Num(Req(o, "rate")),
        Currency = Str(Req(o, "currency")),
        Profile = OptGuid(o, "profile"),
        Since = ReqDate(o, "since"),
    };

    // MARK: Writing

    public static string Write(Contents c)
    {
        var root = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["entries"] = c.Entries.Select(WriteEntry).ToList(),
            ["templates"] = c.Templates.Select(t => Obj(("id", G(t.Id)), ("name", t.Name), ("slots", t.Slots.Select(s => Obj(
                ("activity", s.Activity.Raw()), ("startMinute", s.StartMinute), ("endMinute", s.EndMinute), ("notes", s.Notes.ToList()),
                ("profile", s.Profile is { } p ? G(p) : null))).ToList()))).ToList(),
            ["daysOff"] = c.DaysOff.Order(StringComparer.Ordinal).ToList(),
            ["profiles"] = c.Profiles.Select(p => Obj(("id", G(p.Id)), ("name", p.Name), ("archived", p.Archived))).ToList(),
            ["objectives"] = c.Objectives.Select(o => Obj(("id", G(o.Id)), ("kind", o.Kind.Raw()), ("name", o.Name), ("amount", o.Amount),
                ("rate", o.Rate), ("currency", o.Currency), ("profile", o.Profile is { } p ? G(p) : null), ("since", FormatDate(o.Since)))).ToList(),
        };
        if (c.Current is { } current) root["current"] = G(current);
        var sb = new StringBuilder();
        Emit(sb, root, 0);
        return sb.ToString();
    }

    static SortedDictionary<string, object?> WriteEntry(Entry e) => Obj(("id", G(e.Id)), ("activity", e.Activity.Raw()),
        ("start", FormatDate(e.Start)), ("end", e.End is { } end ? FormatDate(end) : null), ("notes", e.Notes.ToList()),
        ("profile", e.Profile is { } p ? G(p) : null));

    /// Null values are left out, as Swift's encodeIfPresent does.
    static SortedDictionary<string, object?> Obj(params (string key, object? value)[] pairs)
    {
        var d = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (k, v) in pairs) if (v != null) d[k] = v;
        return d;
    }

    static string G(Guid g) => g.ToString("D").ToUpperInvariant();

    public static string FormatDate(DateTimeOffset d) =>
        d.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// Pretty-printed like Swift's: two-space indent, "key" : value.
    static void Emit(StringBuilder sb, object? v, int depth)
    {
        string Pad(int d) => new(' ', d * 2);
        switch (v)
        {
            case null: sb.Append("null"); break;
            case string s: sb.Append(JsonSerializer.Serialize(s, Options)); break;
            case bool b: sb.Append(b ? "true" : "false"); break;
            case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
            case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
            case SortedDictionary<string, object?> o:
                if (o.Count == 0) { sb.Append("{\n\n").Append(Pad(depth)).Append('}'); break; }
                sb.Append("{\n");
                var n = 0;
                foreach (var (k, val) in o)
                {
                    sb.Append(Pad(depth + 1)).Append(JsonSerializer.Serialize(k, Options)).Append(" : ");
                    Emit(sb, val, depth + 1);
                    sb.Append(++n < o.Count ? ",\n" : "\n");
                }
                sb.Append(Pad(depth)).Append('}');
                break;
            case System.Collections.IList list:
                if (list.Count == 0) { sb.Append("[\n\n").Append(Pad(depth)).Append(']'); break; }
                sb.Append("[\n");
                for (var i = 0; i < list.Count; i++)
                {
                    sb.Append(Pad(depth + 1));
                    Emit(sb, list[i], depth + 1);
                    sb.Append(i < list.Count - 1 ? ",\n" : "\n");
                }
                sb.Append(Pad(depth)).Append(']');
                break;
            default: throw new ArgumentException($"can't write {v.GetType()}");
        }
    }

    static readonly JsonSerializerOptions Options = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}
