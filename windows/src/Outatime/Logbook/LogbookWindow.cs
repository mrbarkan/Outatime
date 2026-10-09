namespace Outatime.App;

public sealed class LogbookWindow : AppWindow
{
    public LogbookWindow(Shell shell) { Title = Loc.T("Logbook"); Width = 900; Height = 560; }
    public void ShowObjectives() { }
}
