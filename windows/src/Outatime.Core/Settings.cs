using System.Text.Json;
using System.Text.Json.Serialization;

namespace Outatime;

public enum Appearance { System, Light, Dark }

/// What the tray icon's tooltip shows (the Mac app's "Menu bar" setting: the notification area has no room for text).
public enum TrayStyle { Elapsed, Remaining }

/// The Mac app's UserDefaults, as settings.json next to data.json. Same names; it saves on every change.
public sealed class Settings
{
    public double TargetHours { get; set; } = 8;
    public string ExcludedFromTarget { get; set; } = Activities.DefaultExcluded;
    /// Null: since the first entry.
    public DateTimeOffset? BankSince { get; set; }
    public bool AutoExtra { get; set; } = true;
    public int FocusMinutes { get; set; } = 25;
    public int ShortBreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    public int StretchMinutes { get; set; } = 50;
    public double RemindAfterHours { get; set; } = 10;
    public Appearance Appearance { get; set; } = Appearance.System;
    public string Language { get; set; } = "system";
    public TrayStyle TrayStyle { get; set; } = TrayStyle.Elapsed;
    public bool ColoredIcon { get; set; } = true;
    public bool GlobalShortcuts { get; set; } = true;
    public double HourHeight { get; set; } = 56;
    public string MenuObjective { get; set; } = "";
    public string? LastSeenVersion { get; set; }

    [JsonIgnore] public string? Path { get; private set; }
    /// After every Save().
    public event Action? Changed;

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static Settings Load(string? path = null)
    {
        path ??= System.IO.Path.Combine(Store.DefaultFolder, "settings.json");
        Settings s;
        try { s = File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Options) ?? new() : new(); }
        catch (JsonException) { s = new(); }
        s.Path = path;
        return s;
    }

    /// Settings that live only in memory (tests, previews).
    public static Settings InMemory() => new();

    public void Save()
    {
        if (Path != null)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.WriteAllText(Path, JsonSerializer.Serialize(this, Options));
            }
            catch (IOException) { }
        }
        Changed?.Invoke();
    }

    /// Changes a setting and saves.
    public void Set(Action<Settings> change)
    {
        change(this);
        Save();
    }

    public Language Lang => Languages.FromCode(Language);

    public Target Target(Store store) => store.Target(TargetHours, ExcludedFromTarget);

    /// Where the hours bank starts: the chosen day, or tracking's first.
    public DateTimeOffset BankStart(Target target) => BankSince ?? target.Since;

    public Pomodoro Pomodoro => new(FocusMinutes * 60, ShortBreakMinutes * 60, LongBreakMinutes * 60);

    public bool Counts(Activity a) => !ExcludedFromTarget.Split(',').Contains(a.Raw());

    public void SetCounts(Activity a, bool on)
    {
        var @out = ExcludedFromTarget.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(x => x != a.Raw()).ToList();
        if (!on) @out.Add(a.Raw());
        Set(s => s.ExcludedFromTarget = string.Join(",", @out));
    }
}
