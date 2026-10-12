using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FluentIcons.Common;
using static Outatime.App.Ui;

namespace Outatime.App;

/// What a Log form records: time off and holidays, Travel and Out of Office blocks, or hours carried in.
public enum LogKind { TimeOff, Holiday, Travel, OutOfOffice, Carried }

public static class LogKinds
{
    public static readonly LogKind[] All = Enum.GetValues<LogKind>();

    public static string Label(this LogKind k) => k switch
    {
        LogKind.TimeOff => Loc.T("Time Off"), LogKind.Holiday => Loc.T("Holiday"), LogKind.Travel => Activity.Travel.Label(),
        LogKind.OutOfOffice => Activity.OutOfOffice.Label(), _ => Loc.T("Carried Hours"),
    };

    public static Symbol Symbol(this LogKind k) => k switch
    {
        LogKind.TimeOff => FluentIcons.Common.Symbol.History, LogKind.Holiday => FluentIcons.Common.Symbol.Beach,
        LogKind.Travel => Activity.Travel.Symbol(), LogKind.OutOfOffice => Activity.OutOfOffice.Symbol(),
        _ => FluentIcons.Common.Symbol.ArrowDownload,
    };

    public static IBrush Fill(this LogKind k) => k switch
    {
        LogKind.TimeOff => Brush("#64D2FF"), LogKind.Holiday => Brushes.Gray, LogKind.Travel => Activity.Travel.Fill(),
        LogKind.OutOfOffice => Activity.OutOfOffice.Fill(), _ => Green,
    };
}

/// A Log form's subject: something new, or a bank record being edited.
public abstract record LogItem
{
    public sealed record New(LogKind What) : LogItem;
    public sealed record TimeOffItem(TimeOff Record) : LogItem;
    public sealed record HolidayItem(DateTimeOffset Start, DateTimeOffset End) : LogItem;
    public sealed record CarriedItem(Carried Record) : LogItem;

    public LogKind Kind => this switch
    {
        New n => n.What, TimeOffItem => LogKind.TimeOff, HolidayItem => LogKind.Holiday, _ => LogKind.Carried,
    };
}

/// The hours bank: its balance, and every deposit and withdrawal that isn't a day's work.
public sealed class HoursBankWindow : AppWindow
{
    readonly Shell shell;
    Store Store => shell.Store;
    Settings Settings => shell.Settings;
    readonly StackPanel body = new() { Spacing = 12 };

    public HoursBankWindow(Shell shell)
    {
        this.shell = shell;
        Title = Loc.T("Hours Bank");
        Width = 480;
        Height = 560;
        MinWidth = 420;
        MinHeight = 360;

        var log = Button(H(6, Icon(FluentIcons.Common.Symbol.AddCircle, 16), Text(Loc.T("Log"))), () => { });
        log.Flyout = LogMenu(shell);
        var done = Button(Loc.T("Done"), Close, classes: "accent");
        done.IsDefault = true;
        var footer = new Border { Padding = new Thickness(14), Child = Spread(H(0, log), done) };
        DockPanel.SetDock(footer, Dock.Bottom);
        Content = new DockPanel { Children = { footer, new ScrollViewer { Content = new Border { Padding = new Thickness(16, 16, 16, 4), Child = body } } } };

        Store.Changed += Render;
        Settings.Changed += Render;
        Closed += (_, _) => { Store.Changed -= Render; Settings.Changed -= Render; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Render();
    }

    /// The Log kinds, each opening its form; the panel's Log button shows the same menu.
    public static MenuFlyout LogMenu(Shell shell)
    {
        var menu = new MenuFlyout();
        foreach (var k in LogKinds.All)
        {
            var item = new MenuItem { Header = k.Label(), Icon = Icon(k.Symbol(), 16, k.Fill()) };
            item.Click += (_, _) => shell.ShowLog(new LogItem.New(k));
            menu.Items.Add(item);
        }
        return menu;
    }

    void Render()
    {
        body.Children.Clear();
        var target = Settings.Target(Store);
        var bank = Store.Bank(target, Settings.BankStart(target));
        // "2.5 days off · 24h 00m booked ahead", each part only when there's something to say.
        var caption = new List<string>();
        if (target.Seconds > 0 && bank.Balance > 0) caption.Add(Loc.T("%@ days off", (bank.Balance / target.Seconds).ToString("0.#", Loc.Culture)));
        if (bank.BookedAhead > 0) caption.Add(Loc.T("%@ booked ahead", bank.BookedAhead.Hm()));
        body.Children.Add(V(4,
            Text(bank.Balance.Signed(), 34, FontWeight.SemiBold, bank.Balance >= 0 ? Green : Red, tabular: true)
                .With(t => t.HorizontalAlignment = HorizontalAlignment.Center),
            caption.Count == 0 ? null : Text(string.Join(" · ", caption), 13, secondary: true, tabular: true)
                .With(t => t.HorizontalAlignment = HorizontalAlignment.Center)));

        var rows = Rows(target);
        if (rows.Count == 0)
            body.Children.Add(Text(Loc.T("Nothing logged yet. Log time off, holidays, or overtime carried in from before."), secondary: true)
                .With(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; }));
        else
            body.Children.Add(Section(rows.Select(Row).ToArray()));
    }

    /// `Hours` signed as it moves the bank; a holiday doesn't.
    sealed record BankRow(LogItem Item, DateTimeOffset Start, DateTimeOffset End, double? Hours, string Note);

    /// Time off, carried hours and holidays, newest first.
    List<BankRow> Rows(Target target)
    {
        var rows = new List<BankRow>();
        foreach (var t in Store.TimeOff)
            if (Cal.FromKey(t.Start) is { } s && Cal.FromKey(t.End) is { } e) rows.Add(new(new LogItem.TimeOffItem(t), s, e, -target.Cost(t), t.Note));
        foreach (var c in Store.Carried)
            if (Cal.FromKey(c.Date) is { } d) rows.Add(new(new LogItem.CarriedItem(c), d, d, c.Hours * 3600, c.Note));
        foreach (var (s, e) in Store.Holidays()) rows.Add(new(new LogItem.HolidayItem(s, e), s, e, null, ""));
        return rows.OrderByDescending(r => r.Start).ToList();
    }

    Control Row(BankRow r)
    {
        var kind = r.Item.Kind;
        var label = V(2, Text(Span(r.Start, r.End)), Text(r.Note.Length > 0 ? r.Note : kind.Label(), 11, secondary: true));
        var hours = Text(r.Hours?.Signed() ?? "—", tabular: true, color: r.Hours > 0 ? Green : null, secondary: r.Hours == null);
        var delete = IconButton(FluentIcons.Common.Symbol.Delete, () => Delete(r.Item), Loc.T("Delete"), Red);
        // Click a row to change it.
        var open = Button(H(10, Icon(kind.Symbol(), 16, kind.Fill()), label), () => shell.ShowLog(r.Item), classes: "plain");
        open.HorizontalAlignment = HorizontalAlignment.Stretch;
        open.HorizontalContentAlignment = HorizontalAlignment.Left;
        return Spread(open, hours, delete);
    }

    /// "24 Dec – 2 Jan", or one day; the year only when it isn't this one.
    static string Span(DateTimeOffset start, DateTimeOffset end)
    {
        string F(DateTimeOffset d) => d.Format(Cal.Date(d).Year == Cal.Date(Clock.Now).Year ? "d MMM" : "d MMM yyyy");
        return Cal.SameDay(start, end) ? F(start) : $"{F(start)} – {F(end)}";
    }

    void Delete(LogItem item)
    {
        switch (item)
        {
            case LogItem.TimeOffItem t: Store.DeleteTimeOff(t.Record.Id); break;
            case LogItem.CarriedItem c: Store.DeleteCarried(c.Record.Id); break;
            case LogItem.HolidayItem h: Store.RemoveHoliday((h.Start, h.End)); break;
        }
    }
}
