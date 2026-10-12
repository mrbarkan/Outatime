using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Outatime.App;

public partial class App : Application
{
    public static SingleInstance? Instance;
    public static Store Store = null!;
    public static Settings Settings = null!;
    public static Shell Shell = null!;

    /// "1.0", or "1.0.1" once there's a patch.
    public static string Version
    {
        get
        {
            var v = typeof(App).Assembly.GetName().Version ?? new Version(1, 0, 0);
            return v.Build > 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}";
        }
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            Settings = Settings.Load();
            Store = new Store();
            Start(Store, Settings, Platform.Notifier.Create());
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// Everything after the data is loaded; the headless tests call this with their own store.
    public void Start(Store store, Settings settings, Platform.INotifier notifier, bool tray = true)
    {
        Store = store;
        Settings = settings;
        Loc.Use(settings.Lang);
        ApplyAppearance();
        settings.Changed += ApplyAppearance;
        DevSupport.SeedIfAsked(store);
        store.RollOver();
        Shell = new Shell(store, settings, notifier, tray);
        if (Instance != null) Instance.Activated += () => Avalonia.Threading.Dispatcher.UIThread.Post(() => Shell.ShowPanel());
    }

    void ApplyAppearance() => RequestedThemeVariant = Settings.Appearance switch
    {
        Appearance.Light => ThemeVariant.Light,
        Appearance.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };
}
