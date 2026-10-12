using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using static Outatime.App.Ui;

namespace Outatime.App;

/// One Log form: time off, a holiday, Travel or Out of Office blocks, or carried hours; new, or a record being edited.
public sealed class LogWindow : AppWindow
{
    readonly Shell shell;
    readonly LogItem item;
    Store Store => shell.Store;
    Settings Settings => shell.Settings;
    readonly CalendarDatePicker from = new() { IsTodayHighlighted = true, MinWidth = 180 };
    readonly CalendarDatePicker to = new() { IsTodayHighlighted = true, MinWidth = 180 };
    readonly TextBox hours = new() { MinWidth = 140, HorizontalContentAlignment = HorizontalAlignment.Right };
    readonly TextBox note = new() { MinWidth = 220 };
    readonly ComboBox client = new() { MinWidth = 220 };
    readonly List<Guid?> clients = [null];
    readonly Control hoursRow;
    readonly Button save;

    public LogWindow(Shell shell, LogItem item)
    {
        this.shell = shell;
        this.item = item;
        var kind = item.Kind;
        Title = kind.Label();
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;

        var today = Cal.Date(Clock.Now);
        DateTime start = today, end = today;
        double? h = null;
        var text = "";
        DateTime Day(string key, DateTime fallback) => Cal.FromKey(key) is { } d ? Cal.Date(d) : fallback;
        switch (item)
        {
            case LogItem.TimeOffItem t: start = Day(t.Record.Start, today); end = Day(t.Record.End, start); h = t.Record.Hours; text = t.Record.Note; break;
            case LogItem.HolidayItem r: start = Cal.Date(r.Start); end = Cal.Date(r.End); break;
            case LogItem.CarriedItem c: start = end = Day(c.Record.Date, today); h = c.Record.Hours; text = c.Record.Note; break;
        }
        from.SelectedDate = start;
        to.SelectedDate = end;
        hours.Text = h?.ToString("0.##", Loc.Culture) ?? "";
        hours.PlaceholderText = kind switch
        {
            LogKind.TimeOff => Loc.T("Full day"), LogKind.Carried => "32", _ => Settings.TargetHours.ToString("0.##", Loc.Culture),
        };
        note.Text = text;
        clients.AddRange(Store.ActiveProfiles.Select(p => (Guid?)p.Id));
        client.ItemsSource = new[] { Loc.T("No client") }.Concat(Store.ActiveProfiles.Select(p => p.Name)).ToList();
        client.SelectedIndex = Math.Max(0, clients.IndexOf(Store.CurrentProfile));

        hoursRow = Labeled(kind is LogKind.Travel or LogKind.OutOfOffice ? Loc.T("Hours per day") : Loc.T("Hours"), hours);
        var cancel = Button(Loc.T("Cancel"), Close);
        cancel.IsCancel = true;
        save = Button(Loc.T("Save"), Save, classes: "accent");
        save.IsDefault = true;

        var rows = new List<Control?>
        {
            H(8, Icon(kind.Symbol(), 16, kind.Fill()), Text(kind.Label(), 13, FontWeight.SemiBold)),
            Labeled(kind == LogKind.Carried ? Loc.T("Date") : Loc.T("From"), from),
            kind == LogKind.Carried ? null : Labeled(Loc.T("To"), to),
            kind == LogKind.Holiday ? null : hoursRow,
            kind == LogKind.Travel && Store.ActiveProfiles.Count > 0 ? Labeled(Loc.T("Client"), client) : null,
            kind is LogKind.TimeOff or LogKind.Carried ? Labeled(Loc.T("Note"), note) : null,
            Text(Loc.T(Footer(kind)), 11, secondary: true).With(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; }),
        };
        var buttons = H(8, cancel, save);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        Content = new Border { Padding = new Thickness(16), Child = V(14, Section(rows.ToArray()), buttons) };

        from.SelectedDateChanged += (_, _) =>
        {
            if (from.SelectedDate is { } f && (to.SelectedDate is not { } t || t < f)) to.SelectedDate = f;
            Refresh();
        };
        to.SelectedDateChanged += (_, _) => Refresh();
        hours.TextChanged += (_, _) => Refresh();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Refresh();
    }

    static string Footer(LogKind kind) => kind switch
    {
        LogKind.TimeOff => "Each workday costs a day's target, or the hours you enter. Weekends and holidays cost nothing. It comes off the bank as soon as you save.",
        LogKind.Holiday => "Holidays owe nothing and leave the bank alone.",
        LogKind.Travel => "Adds a Travel block on each workday, after the day's last block or at your usual start.",
        LogKind.OutOfOffice => "Adds an Out of Office block on each workday, after the day's last block or at your usual start.",
        _ => "Overtime from before you tracked it here, or a negative amount for hours paid out.",
    };

    DateTimeOffset From => Cal.At((from.SelectedDate ?? Cal.Date(Clock.Now)).Date);
    DateTimeOffset To { get { var t = Cal.At((to.SelectedDate ?? from.SelectedDate ?? Cal.Date(Clock.Now)).Date); return t < From ? From : t; } }
    bool OneDay => Cal.SameDay(From, To);

    /// Empty is null; anything unreadable is NaN, which keeps Save off.
    double? Hours => string.IsNullOrWhiteSpace(hours.Text) ? null
        : double.TryParse(hours.Text, NumberStyles.Number, Loc.Culture, out var v) ? v : double.NaN;

    void Refresh()
    {
        // Time off takes hours only for a single day.
        if (item.Kind == LogKind.TimeOff) hoursRow.IsVisible = OneDay;
        var h = Hours;
        save.IsEnabled = item.Kind switch
        {
            LogKind.Carried => h is { } c && !double.IsNaN(c) && c != 0,
            LogKind.Holiday => true,
            _ => h is not { } x || (!double.IsNaN(x) && x > 0),
        };
    }

    void Save()
    {
        var h = Hours;
        switch (item.Kind)
        {
            case LogKind.TimeOff:
                Store.Upsert(new TimeOff(item is LogItem.TimeOffItem t ? t.Record.Id : Guid.NewGuid(), From.DayKey(), To.DayKey(), OneDay ? h : null, note.Text ?? ""));
                break;
            case LogKind.Holiday:
                Store.SetHoliday(From, To, item is LogItem.HolidayItem r ? (r.Start, r.End) : null);
                break;
            case LogKind.Travel or LogKind.OutOfOffice:
                var target = Settings.Target(Store);
                Store.Log(item.Kind == LogKind.Travel ? Activity.Travel : Activity.OutOfOffice, From, To, h ?? Settings.TargetHours,
                          clients[Math.Max(0, client.SelectedIndex)], target, new Stats(Store.Entries, target, Settings.BankStart(target)).UsualStart);
                break;
            case LogKind.Carried:
                Store.Upsert(new Carried(item is LogItem.CarriedItem c ? c.Record.Id : Guid.NewGuid(), From.DayKey(), h ?? 0, note.Text ?? ""));
                break;
        }
        Close();
    }
}
