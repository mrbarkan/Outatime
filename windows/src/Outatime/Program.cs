using Avalonia;

namespace Outatime.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // One copy at a time: a second launch (the Start menu while it sits in the tray) opens the running one's panel.
        using var instance = new SingleInstance("Outatime-8C1D3E7A");
        if (!instance.IsFirst)
        {
            instance.SignalFirst();
            return 0;
        }
        App.Instance = instance;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
