using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Outatime.App;
using Outatime.App.Platform;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Outatime.Tests.HeadlessApp))]

namespace Outatime.Tests;

public static class HeadlessApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// Every window opens over a few weeks of sample data, and the panel's tiles track.
public class UiTests
{
    static (Shell Shell, Store Store, LogNotifier Notifier) Start()
    {
        var home = Path.Combine(Path.GetTempPath(), $"outatime-ui-{Guid.NewGuid()}");
        Environment.SetEnvironmentVariable("OUTATIME_SAMPLE", "1");
        try
        {
            var store = new Store(Path.Combine(home, "data.json"));
            var notifier = new LogNotifier();
            ((App.App)Application.Current!).Start(store, Settings.InMemory(), notifier, tray: false);
            return (App.App.Shell, store, notifier);
        }
        finally { Environment.SetEnvironmentVariable("OUTATIME_SAMPLE", null); }
    }

    static IEnumerable<T> All<T>(Window w) where T : ILogical => w.GetLogicalDescendants().OfType<T>();

    static Button TileFor(Window panel, Activity a) =>
        All<Button>(panel).First(b => b.Classes.Contains("tile") && b.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == a.Label()));

    [AvaloniaFact]
    public void PanelTilesStartAndStop()
    {
        var (shell, store, _) = Start();
        Assert.NotEmpty(store.Entries);
        shell.ShowPanel();
        var panel = Window(nameof(MenuPanel));
        TileFor(panel, Activity.Break).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(Activity.Break, store.Running?.Activity);
        TileFor(panel, Activity.Break).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Null(store.Running);
    }

    [AvaloniaFact]
    public void EveryWindowOpens()
    {
        var (shell, store, _) = Start();
        shell.ShowLogbook();
        for (var tab = 0; tab < 4; tab++) shell.ShowSettings(tab);
        shell.ShowObjectives();
        shell.ShowAbout();
        shell.ShowWhatsNew("0");
        foreach (var name in new[] { nameof(LogbookWindow), nameof(SettingsWindow), nameof(ObjectivesWindow), nameof(AboutWindow), nameof(WhatsNewWindow) })
        {
            var w = Window(name);
            w.UpdateLayout();
            Assert.True(w.Bounds.Width > 100 && w.Bounds.Height > 100, $"{name} is {w.Bounds}");
        }
        // The Logbook shows today's blocks; the summary counts them.
        Assert.Contains(All<TextBlock>(Window(nameof(LogbookWindow))), t => t.Text?.Contains("Writing the report") == true);
    }

    [AvaloniaFact]
    public void TomatoRoundEndsWithAnActionableNotification()
    {
        var (shell, store, notifier) = Start();
        var start = Clock.Now.Plus(-26 * 60);
        store.Entries = [new Entry(Activity.Work, start)];
        store.TomatoSince = start;
        var check = typeof(Shell).GetMethod("CheckTomato", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Clock.Source = () => start.Plus(25 * 60 + 2);
        try { check.Invoke(shell, null); } finally { Clock.Source = () => DateTimeOffset.Now; }
        Assert.Contains(notifier.Sent, n => n.Id == "tomato");
        notifier.Press(Activity.Break);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(Activity.Break, store.Running?.Activity);
    }

    static Window Window(string type)
    {
        var windows = typeof(Shell).GetField("windows", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(App.App.Shell) as Dictionary<string, Window>;
        var panel = typeof(Shell).GetField("panel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(App.App.Shell) as Window;
        return windows!.Values.Append(panel).OfType<Window>().First(w => w.GetType().Name == type);
    }
}
