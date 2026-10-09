namespace Outatime.App;

public sealed class AboutWindow : AppWindow
{
    public AboutWindow() { Title = Loc.T("About Outatime"); }
}

public sealed class WhatsNewWindow : AppWindow
{
    public WhatsNewWindow(string from) { Title = Loc.T("What's New"); }
}
