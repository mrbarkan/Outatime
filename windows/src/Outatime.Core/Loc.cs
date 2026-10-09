using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Outatime;

/// The languages the Windows app speaks. Native names on purpose: you should be able to find your language when the
/// UI is in one you can't read.
public enum Language { System, En, Es, PtBR }

public static class Languages
{
    public static readonly Language[] All = Enum.GetValues<Language>();
    public static string Code(this Language l) => l switch { Language.En => "en", Language.Es => "es", Language.PtBR => "pt-BR", _ => "system" };
    public static Language FromCode(string? code) => All.FirstOrDefault(l => l.Code() == code);
    public static string Label(this Language l) => l switch
    {
        Language.En => "English", Language.Es => "Español", Language.PtBR => "Português (Brasil)", _ => Loc.T("System"),
    };

    /// The manual in this language (docs/manual in the repo, served by GitHub Pages). On System, the UI language's.
    public static string ManualUrl(this Language l)
    {
        const string root = "https://mrbarkan.github.io/Outatime/manual/";
        var code = Loc.Resolve(l);
        return code == "en" ? root : root + code + "/";
    }
}

/// Translations keyed by the English text, as in the Mac app's string catalog (Strings.json is copied from it by
/// scripts/strings.py). Placeholders are printf-style like the catalog's: %@ for text, %lld for whole numbers, and
/// %1$@ to pick an argument by position.
public static class Loc
{
    static readonly Dictionary<string, Dictionary<string, string>> Table = LoadTable();

    /// "en", "es" or "pt-BR": what T() translates into.
    public static string Current { get; private set; } = Resolve(Language.System);
    public static CultureInfo Culture => Current == "en" ? CultureInfo.CurrentCulture : CultureInfo.GetCultureInfo(Current);

    public static IReadOnlyDictionary<string, Dictionary<string, string>> Strings => Table;

    static Dictionary<string, Dictionary<string, string>> LoadTable()
    {
        // Strings.json comes from the Mac catalog; Strings.<area>.json files hold what only the Windows app says.
        var table = new Dictionary<string, Dictionary<string, string>>();
        var assembly = typeof(Loc).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Outatime.Strings") && n.EndsWith(".json")))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            foreach (var (key, row) in JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)!) table[key] = row;
        }
        return table;
    }

    /// On System, the first of the OS's UI languages we have: any Portuguese reads the Brazilian one.
    public static string Resolve(Language l)
    {
        if (l != Language.System) return l.Code();
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch { "es" => "es", "pt" => "pt-BR", _ => "en" };
    }

    public static void Use(Language l) => Current = Resolve(l);

    public static string T(string key, params object[] args)
    {
        var text = Current != "en" && Table.TryGetValue(key, out var row) && row.TryGetValue(Current, out var t) ? t : key;
        return args.Length == 0 ? text : Format(text, args);
    }

    static readonly Regex Placeholder = new(@"%(?:(\d+)\$)?(@|lld|ld|d|%)", RegexOptions.Compiled);

    public static string Format(string text, object[] args)
    {
        var next = 0;
        return Placeholder.Replace(text, m =>
        {
            if (m.Groups[2].Value == "%") return "%";
            var i = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) - 1 : next++;
            return i < args.Length ? Convert.ToString(args[i], Culture) ?? "" : m.Value;
        });
    }
}
