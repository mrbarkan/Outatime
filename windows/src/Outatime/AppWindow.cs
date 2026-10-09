using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Outatime.App;

/// A window with the app's icon and, on Windows 11, the Mica backdrop.
public class AppWindow : Window
{
    public AppWindow()
    {
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Outatime/Assets/Outatime.ico")));
        if (OperatingSystem.IsWindows())
        {
            TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None];
            // Mica shows through a transparent background; without it (Windows 10) the theme's background stays.
            this.GetObservable(ActualTransparencyLevelProperty).Subscribe(new Observer<WindowTransparencyLevel>(level =>
                Background = level == WindowTransparencyLevel.Mica ? Avalonia.Media.Brushes.Transparent : null));
        }
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    public static Bitmap AppIcon => new(AssetLoader.Open(new Uri("avares://Outatime/Assets/Outatime-256.png")));

    sealed class Observer<T>(Action<T> next) : IObserver<T>
    {
        public void OnNext(T value) => next(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }
}
