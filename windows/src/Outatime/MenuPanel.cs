using Avalonia.Controls;

namespace Outatime.App;

public sealed class MenuPanel : Window
{
    public MenuPanel(Shell shell) { Width = 320; Height = 200; }
    public void ShowNearTray() => Show();
}
