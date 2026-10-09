namespace Outatime.App;

public sealed class SettingsWindow : AppWindow
{
    public SettingsWindow(Shell shell) { Title = Loc.T("Settings"); Width = 440; Height = 400; }
    public void SelectTab(int tab) { }
}
