namespace Outatime.App;

/// For development and screenshots: OUTATIME_SAMPLE=1 fills an empty store with a few weeks of made-up work, and
/// OUTATIME_OPEN=panel|logbook|settings|objectives|about|whatsnew opens that window at launch.
public static class DevSupport
{
    public static void SeedIfAsked(Store store)
    {
        if (Environment.GetEnvironmentVariable("OUTATIME_SAMPLE") != "1" || store.Entries.Count > 0) return;
        var rng = new Random(7);
        var acme = store.AddProfile("Acme Corp")!.Value;
        var globex = store.AddProfile("Globex")!.Value;
        store.AddProfile("Initech");
        var today = Cal.StartOfDay(Clock.Now);
        var entries = new List<Entry>();
        for (var d = -24; d <= 0; d++)
        {
            var day = Cal.AddDays(today, d);
            if (Cal.IsWeekend(day)) continue;
            var client = d % 3 == 0 ? globex : acme;
            var start = Cal.Setting(day, 8, 30 + rng.Next(0, 4) * 10);
            var lunch = Cal.Setting(day, 12, 15 + rng.Next(0, 3) * 5);
            entries.Add(new Entry(Activity.Work, start, lunch, new[] { "Sprint planning", "API review" }[..(d % 2 == 0 ? 1 : 2)], client));
            var back = lunch.Plus(45 * 60);
            entries.Add(new Entry(Activity.Lunch, lunch, back));
            var coffee = Cal.Setting(day, 15, 10);
            if (d == 0)
            {
                // Today, whatever the hour: a morning, a coffee an hour ago, and Work running since.
                entries.RemoveAll(e => Cal.SameDay(e.Start, day));
                var now = Clock.Now;
                var morning = now.Plus(-4 * 3600) > start ? start : now.Plus(-4 * 3600);
                entries.Add(new Entry(Activity.Work, morning, now.Plus(-65 * 60), ["Design review"], client));
                entries.Add(new Entry(Activity.Break, now.Plus(-65 * 60), now.Plus(-50 * 60)));
                entries.Add(new Entry(Activity.Work, now.Plus(-50 * 60), null, ["Writing the report"], client));
                continue;
            }
            entries.Add(new Entry(Activity.Work, back, coffee, [], client));
            entries.Add(new Entry(Activity.Break, coffee, coffee.Plus(15 * 60)));
            var end = Cal.Setting(day, 17, 20 + rng.Next(0, 6) * 10);
            entries.Add(new Entry(Activity.Work, coffee.Plus(15 * 60), end, [], d % 3 == 0 ? globex : acme));
            if (d % 4 == 0) entries.Add(new Entry(Activity.Extra, Cal.Setting(day, 20), Cal.Setting(day, 21, 30), ["Release"], acme));
            if (d % 5 == 1) entries.Add(new Entry(Activity.Travel, Cal.Setting(day, 7, 40), Cal.Setting(day, 8, 20), [], client));
        }
        store.Entries = entries;
        store.CurrentProfile = acme;
        store.SetDayOff(Cal.AddDays(today, -9), true);
        store.Objectives = [new Objective(ObjectiveKind.DaysOff, 3), new Objective(ObjectiveKind.Hours, 120, profile: acme, since: Cal.AddDays(today, -20))];
        store.Templates = [new DayTemplate("Normal day", store.EntriesOn(Cal.AddDays(today, -1)))];
    }

    public static void OpenIfAsked(Shell shell)
    {
        switch (Environment.GetEnvironmentVariable("OUTATIME_OPEN"))
        {
            case "panel": shell.ShowPanel(); break;
            case "logbook": shell.ShowLogbook(); break;
            case "settings": shell.ShowSettings(int.TryParse(Environment.GetEnvironmentVariable("OUTATIME_TAB"), out var tab) ? tab : 0); break;
            case "objectives": shell.ShowObjectives(); break;
            case "about": shell.ShowAbout(); break;
            case "whatsnew": shell.ShowWhatsNew("0"); break;
        }
        if (Environment.GetEnvironmentVariable("OUTATIME_SNAPSHOT") is { Length: > 0 } folder)
            Avalonia.Threading.DispatcherTimer.RunOnce(() => Snapshot(shell, folder), TimeSpan.FromSeconds(2));
    }

    /// OUTATIME_SNAPSHOT=<folder>: renders every open window to <folder>/<title>.png, then quits.
    static void Snapshot(Shell shell, string folder)
    {
        Directory.CreateDirectory(folder);
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
            foreach (var w in life.Windows.Where(w => w.IsVisible && w.Bounds.Width > 2))
            {
                var scale = w.RenderScaling;
                using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(
                    new Avalonia.PixelSize((int)(w.Bounds.Width * scale), (int)(w.Bounds.Height * scale)), new Avalonia.Vector(96 * scale, 96 * scale));
                bitmap.Render(w);
                using var file = File.Create(Path.Combine(folder, (w.Title ?? "window").Replace('/', '-') + ".png"));
                bitmap.Save(file);
            }
        shell.Quit();
    }
}
