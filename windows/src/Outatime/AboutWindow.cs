using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FluentIcons.Common;
using static Outatime.App.Ui;

namespace Outatime.App;

/// Who made this, which version it is and where to get help.
public sealed class AboutWindow : AppWindow
{
    public const string SupportEmail = "opa@mrbarkan.com";

    public static string Build => typeof(AboutWindow).Assembly.GetName().Version?.ToString() ?? "0";

    public AboutWindow()
    {
        Title = Loc.T("About Outatime");
        Width = 360;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        Control Centered(Control c) { c.HorizontalAlignment = HorizontalAlignment.Center; return c; }
        TextBlock Wrapped(TextBlock t) { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; t.TextAlignment = TextAlignment.Center; return t; }
        var contact = Button(H(8, Icon(Symbol.Mail, 16, Brushes.White), Text(Loc.T("Contact Support"), color: Brushes.White)),
                             () => Shell.OpenUrl(SupportUrl), classes: "accent");
        contact.HorizontalAlignment = HorizontalAlignment.Stretch;
        contact.HorizontalContentAlignment = HorizontalAlignment.Center;
        contact.Padding = new Thickness(12, 8);
        var links = H(18,
            Button(Loc.T("User Manual"), () => App.Shell.ShowManual(), classes: "link"),
            Button(Loc.T("Release Notes"), () => Shell.OpenUrl("https://github.com/mrbarkan/Outatime/releases"), classes: "link"),
            Button(Loc.T("Source Code"), () => Shell.OpenUrl("https://github.com/mrbarkan/Outatime"), classes: "link"));
        Content = new Border
        {
            Padding = new Thickness(28),
            Child = V(18,
                V(6,
                    Centered(new Image { Source = AppIcon, Width = 96, Height = 96 }),
                    Centered(Text("Outatime", 22, FontWeight.Bold)),
                    Centered(Text(Loc.T("Version %@ (%@)", App.Version, Build), 12, secondary: true)),
                    Centered(Wrapped(Text(Loc.T("A tiny tray time tracker for Windows."))))),
                V(6, contact, Centered(Wrapped(Text(Loc.T("Questions, bugs or ideas: %@", SupportEmail), 11, secondary: true)))),
                Centered(links),
                Centered(Text("© 2026 David Barkan", 11, secondary: true))),
        };
    }

    /// A new mail with the version and Windows filled in, the two things every support reply asks for first.
    static string SupportUrl
    {
        get
        {
            var body = $"\n\n—\nOutatime {App.Version} ({Build}), {RuntimeInformation.OSDescription}";
            return $"mailto:{SupportEmail}?subject={Uri.EscapeDataString($"Outatime {App.Version}")}&body={Uri.EscapeDataString(body)}";
        }
    }
}

/// Release notes, shown once after an update.
public sealed class WhatsNewWindow : AppWindow
{
    public WhatsNewWindow(string from)
    {
        var releases = WhatsNew.Unseen(WhatsNew.Releases, from, App.Version, hasData: true);
        Title = Loc.T("What's New");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;

        Control Section(string title, IEnumerable<WhatsNew.Item> items)
        {
            var s = V(14, Text(Loc.T(title).ToUpper(Loc.Culture), 11, FontWeight.SemiBold, secondary: true));
            foreach (var i in items)
            {
                var icon = Enum.TryParse<Symbol>(i.Icon, out var symbol) ? symbol : Symbol.Sparkle;
                var detail = Text(Loc.T(i.Detail), secondary: true).With(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; });
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*") };
                var glyph = Icon(icon, 24, Brush(i.Color)).With(g => g.VerticalAlignment = VerticalAlignment.Top);
                var text = V(2, Text(Loc.T(i.Title), 13, FontWeight.SemiBold), detail);
                Grid.SetColumn(text, 1);
                row.Children.Add(glyph);
                row.Children.Add(text);
                s.Children.Add(row);
            }
            return s;
        }

        var cont = Button(Text(Loc.T("Continue"), color: Brushes.White), Close, classes: "accent");
        cont.HorizontalAlignment = HorizontalAlignment.Stretch;
        cont.HorizontalContentAlignment = HorizontalAlignment.Center;
        cont.Padding = new Thickness(12, 8);
        cont.IsDefault = true;
        var body = V(22, H(14, new Image { Source = AppIcon, Width = 56, Height = 56 },
                           Text(Loc.T("What's New in Outatime %@", releases.FirstOrDefault()?.Version ?? App.Version), 18, FontWeight.Bold)
                               .With(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; t.MaxWidth = 320; })));
        var added = releases.SelectMany(r => r.New).ToList();
        var fixedItems = releases.SelectMany(r => r.Fixed).ToList();
        if (added.Count > 0) body.Children.Add(Section("New", added));
        if (fixedItems.Count > 0) body.Children.Add(Section("Fixed", fixedItems));
        body.Children.Add(cont);
        Content = new Border { Padding = new Thickness(28), Child = body };
    }
}
