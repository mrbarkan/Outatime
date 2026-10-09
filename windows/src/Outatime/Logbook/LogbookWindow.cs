using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FluentIcons.Common;

namespace Outatime.App;

/// The Logbook: the month's days on the left, the selected day's timeline on the right.
public sealed class LogbookWindow : AppWindow
{
    static readonly double[] ZoomLevels = [40, 56, 84, 126, 189];
    readonly Shell shell;
    Store store => shell.Store;
    Settings settings => shell.Settings;
    DateTimeOffset day = Cal.StartOfDay(Clock.Now);

    readonly ScrollViewer sidebar = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    readonly ContentControl toolbar = new(), summary = new();
    readonly TextBlock title = Ui.Text("", 20, FontWeight.SemiBold);
    readonly DayTimeline timeline;
    readonly DispatcherTimer tick;
    ObjectivesWindow? objectives;

    public LogbookWindow(Shell shell)
    {
        this.shell = shell;
        Title = Loc.T("Logbook");
        Width = 980;
        Height = 640;
        MinWidth = 760;
        MinHeight = 420;
        timeline = new DayTimeline(shell);

        var side = new Border { Child = sidebar, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(10, 8, 6, 8) };
        side.Bind(Border.BorderBrushProperty, side.GetResourceObservable("HairlineBrush"));
        side.Bind(Border.BackgroundProperty, side.GetResourceObservable("SectionBrush"));

        var head = Ui.V(6, toolbar, title);
        head.Margin = new Thickness(16, 10, 16, 6);
        var main = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(summary, Dock.Bottom);
        main.Children.Add(head);
        main.Children.Add(summary);
        main.Children.Add(timeline);

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("310,*") };
        root.Children.Add(side);
        Grid.SetColumn(main, 1);
        root.Children.Add(main);
        Content = root;

        store.Changed += OnChanged;
        settings.Changed += OnChanged;
        tick = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => OnChanged());
        tick.Start();
        Closed += (_, _) =>
        {
            store.Changed -= OnChanged;
            settings.Changed -= OnChanged;
            tick.Stop();
        };
        KeyDown += OnKey;
        Render(scrollToStart: true);
    }

    void OnChanged() => Render(scrollToStart: false);

    void Render(bool scrollToStart)
    {
        sidebar.Content = Sidebar();
        toolbar.Content = Toolbar();
        var t = Cal.Local(day).ToString("dddd, " + Loc.Culture.DateTimeFormat.MonthDayPattern, Loc.Culture);
        title.Text = char.ToUpper(t[0], Loc.Culture) + t[1..];
        summary.Content = Summary();
        timeline.Show(day, scrollToStart);
    }

    void Go(DateTimeOffset to)
    {
        day = Cal.StartOfDay(to);
        Render(scrollToStart: true);
    }

    void ShiftDay(int days) => Go(Cal.AddDays(day, days));
    void ShiftMonth(int months) => Go(Cal.AddMonths(Cal.StartOfMonth(day), months));

    void Zoom(int step)
    {
        var i = Array.FindLastIndex(ZoomLevels, l => l <= settings.HourHeight);
        settings.Set(s => s.HourHeight = ZoomLevels[Math.Clamp(Math.Max(0, i) + step, 0, ZoomLevels.Length - 1)]);
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { timeline.ClearSelection(); e.Handled = true; return; }
        if (!LogbookDialogs.Command(e.KeyModifiers)) return;
        e.Handled = true;
        switch (e.Key)
        {
            case Key.OemOpenBrackets: ShiftDay(-1); break;
            case Key.OemCloseBrackets: ShiftDay(1); break;
            case Key.T: Go(Clock.Now); break;
            case Key.OemMinus or Key.Subtract: Zoom(-1); break;
            case Key.OemPlus or Key.Add: Zoom(1); break;
            case Key.A: timeline.SelectAll(); break;
            default: e.Handled = false; break;
        }
    }

    public void ShowObjectives()
    {
        if (objectives != null) { objectives.Activate(); return; }
        objectives = new ObjectivesWindow(shell);
        objectives.Closed += (_, _) => objectives = null;
        _ = objectives.ShowDialog(this);
    }

    // MARK: Sidebar

    Control Sidebar()
    {
        var month = Cal.StartOfMonth(day);
        var target = settings.Target(store);
        var monthBalance = store.Balance(Cal.Month(month), target).Balance;
        // One scale for the month's bars, so a longer day draws a longer bar.
        var longest = store.EntriesInMonth(month).GroupBy(e => Cal.StartOfDay(e.Start)).Select(g => g.Sum(e => e.Duration)).DefaultIfEmpty(0).Max();
        var scale = Math.Max(Math.Max(target.Seconds * 1.25, longest), 3600);

        var name = Cal.Local(month).ToString("MMMM yyyy", Loc.Culture);
        var header = Ui.Spread(
            Ui.H(8, Ui.Text(char.ToUpper(name[0], Loc.Culture) + name[1..], 14, FontWeight.SemiBold),
                 Ui.Text(monthBalance.Signed(), 13, color: monthBalance >= 0 ? Ui.Green : Ui.Red, tabular: true)),
            Ui.H(0, Ui.IconButton(Symbol.ChevronLeft, () => ShiftMonth(-1), Loc.T("Previous Month"), size: 14),
                    Ui.IconButton(Symbol.ChevronRight, () => ShiftMonth(1), Loc.T("Next Month"), size: 14)));
        header.Margin = new Thickness(6, 0, 0, 6);

        var list = Ui.V(2, header);
        foreach (var week in Cal.DaysInMonth(month).GroupBy(d => Cal.Week(d).Start).OrderBy(g => g.Key).Select(g => g.ToList()))
        {
            foreach (var d in week) list.Children.Add(DayRow(d, target, scale));
            if (WeekTotal(week, target) is { } footer) list.Children.Add(footer);
        }
        return list;
    }

    Control DayRow(DateTimeOffset d, Target target, double scale)
    {
        var totals = store.TotalsOn(d);
        var worked = totals.Worked(target.Excluded);
        var owed = target.Owed(d);
        var dayOff = target.DaysOff?.Contains(d.DayKey()) == true;
        var today = Cal.IsToday(d);
        var selected = Cal.SameDay(d, day);
        var balance = worked - owed;
        IBrush? White(IBrush? other) => selected ? Brushes.White : other;

        var label = Ui.Text(d.Format("ddd d"), 13, today ? FontWeight.Bold : FontWeight.Normal,
                            color: White(today ? Ui.Accent : null), secondary: !selected && !today && Cal.IsWeekend(d));
        var right = Ui.H(10);
        right.HorizontalAlignment = HorizontalAlignment.Right;
        if (dayOff) right.Children.Add(Ui.Icon(Symbol.Beach, 14, White(null)).With(i => i.Opacity = selected ? 1 : 0.62));
        // A past workday with nothing logged shows its shortfall, so a forgotten day can't hide.
        if (totals.Count > 0 || owed > 0)
        {
            right.Children.Add(Bar(totals, owed, scale, selected));
            right.Children.Add(Ui.Text(worked.Hm(), 13, color: White(null), tabular: true).With(t => { t.Width = 58; t.TextAlignment = TextAlignment.Right; }));
            // Today is still in progress: a shortfall isn't alarming yet.
            right.Children.Add(Ui.Text(balance.Signed(), 13, FontWeight.Medium, tabular: true,
                                       color: White(balance >= 0 ? Ui.Green : today ? null : Ui.Red), secondary: !selected && balance < 0 && today)
                                 .With(t => { t.Width = 60; t.TextAlignment = TextAlignment.Right; }));
        }
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(label);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        var row = new Border { Child = grid, Padding = new Thickness(8, 4), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
        if (selected) row.Bind(Border.BackgroundProperty, row.GetResourceObservable("SelectionBrush"));
        else
        {
            row.PointerEntered += (_, _) => row.Bind(Border.BackgroundProperty, row.GetResourceObservable("TileBrush"));
            row.PointerExited += (_, _) => { row.ClearValue(Border.BackgroundProperty); row.Background = Brushes.Transparent; };
        }
        row.PointerPressed += (_, e) => { if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) Go(d); };
        return row;
    }

    /// The day's activities stacked on the month's scale, with a tick at the target on days that owe one.
    static Control Bar(Dictionary<Activity, double> totals, double owed, double scale, bool selected)
    {
        const double width = 64;
        var track = new Border { Width = width, Height = 6, CornerRadius = new CornerRadius(3), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center };
        if (selected) track.Background = new SolidColorBrush(Colors.White, 0.25);
        else track.Bind(Border.BackgroundProperty, track.GetResourceObservable("TileBrush"));
        var fill = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var a in Activities.All.Where(a => totals.Get(a) > 0))
            fill.Children.Add(new Border { Width = width * Math.Min(1, totals[a] / scale),
                                           Background = selected ? new SolidColorBrush(Colors.White, a == Activity.Work ? 1 : 0.55) : a.Fill() });
        track.Child = fill;
        var host = new Grid { Width = width, Height = 14, VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(track);
        if (owed > 0)
        {
            var tick = new Border { Width = 2, Height = 12, CornerRadius = new CornerRadius(1), HorizontalAlignment = HorizontalAlignment.Left,
                                    Margin = new Thickness(width * Math.Min(1, owed / scale) - 1, 0, 0, 0) };
            tick.Background = selected ? Brushes.White : new SolidColorBrush(Color.Parse("#8A8A8A"));
            host.Children.Add(tick);
        }
        return host;
    }

    /// The days of one week shown in this month: what was worked and the balance, once any of it is past.
    Control? WeekTotal(List<DateTimeOffset> days, Target target)
    {
        if (days[0] > Clock.Now) return null;
        var w = store.Balance(new Interval(days[0], Cal.AddDays(days[^1], 1)), target);
        var row = Ui.Spread(Ui.Text(Loc.T("Week %lld", Cal.WeekOfYear(days[0])), 11, secondary: true),
                            Ui.Text(w.Worked.Hm(), 11, secondary: true, tabular: true),
                            Ui.Text(w.Balance.Signed(), 11, color: w.Balance >= 0 ? Ui.Green : Ui.Red, tabular: true)
                              .With(t => { t.Width = 60; t.TextAlignment = TextAlignment.Right; }));
        row.Margin = new Thickness(8, 2, 8, 10);
        return row;
    }

    // MARK: Toolbar

    Control Toolbar()
    {
        var month = Cal.StartOfMonth(day);
        var isToday = Cal.IsToday(day);
        var todayButton = Ui.Button(Loc.T("Today"), () => Go(Clock.Now));
        todayButton.IsEnabled = !isToday;
        var nav = Ui.H(4, Ui.IconButton(Symbol.ChevronLeft, () => ShiftDay(-1), Loc.T("Previous Day")), todayButton,
                       Ui.IconButton(Symbol.ChevronRight, () => ShiftDay(1), Loc.T("Next Day")));

        var zoomOut = Ui.IconButton(Symbol.ZoomOut, () => Zoom(-1), Loc.T("Zoom Out"));
        zoomOut.IsEnabled = settings.HourHeight > ZoomLevels[0];
        var zoomIn = Ui.IconButton(Symbol.ZoomIn, () => Zoom(1), Loc.T("Zoom In"));
        zoomIn.IsEnabled = settings.HourHeight < ZoomLevels[^1];

        var stats = Ui.IconButton(Symbol.DataBarVertical, () => { }, Loc.T("Stats"));
        stats.Click += (_, _) =>
        {
            var target = settings.Target(store);
            new Flyout { Content = StatsView.Build(new Stats(store.Entries, target, settings.BankStart(target))), Placement = PlacementMode.BottomEdgeAlignedRight }.ShowAt(stats);
        };
        var goals = Ui.IconButton(Symbol.FlagCheckered, ShowObjectives, Loc.T("Objectives"));

        var off = new ToggleButton { Content = Ui.Icon(Symbol.Beach, 16), IsChecked = store.DaysOff.Contains(day.DayKey()), Padding = new Thickness(4),
                                     Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        ToolTip.SetTip(off, Loc.T("Day Off"));
        off.IsCheckedChanged += (_, _) => store.SetDayOff(day, off.IsChecked == true);
        var add = Ui.IconButton(Symbol.AddCircle, () => store.AddEntry(day), Loc.T("Add Entry"));

        var templates = Ui.IconButton(Symbol.DocumentCopy, () => { }, Loc.T("Templates"));
        templates.Click += (_, _) => TemplatesMenu().ShowAt(templates);
        var export = Ui.IconButton(Symbol.ArrowExportUp, () => { }, Loc.T("Export"));
        export.Click += (_, _) => ExportMenu(month).ShowAt(export);

        return Ui.Spread(nav, Ui.H(2, zoomOut, zoomIn), Ui.H(2, stats, goals), Ui.H(2, off, add, templates, export));
    }

    static MenuItem Item(string header, Action click, bool enabled = true)
    {
        var i = new MenuItem { Header = header, IsEnabled = enabled };
        i.Click += (_, _) => click();
        return i;
    }

    MenuFlyout TemplatesMenu()
    {
        var dayEntries = store.EntriesOn(day);
        var menu = new MenuFlyout();
        menu.Items.Add(Item(Loc.T("Save Day as Template…"), async () =>
        {
            var name = await LogbookDialogs.AskName(this, Loc.T("Save Day as Template"), Loc.T("Name"));
            if (!string.IsNullOrWhiteSpace(name)) store.Templates = [.. store.Templates, new DayTemplate(name, store.EntriesOn(day))];
        }, enabled: dayEntries.Count > 0));
        if (store.Templates.Count > 0) menu.Items.Add(new Separator());
        foreach (var t in store.Templates)
        {
            var sub = new MenuItem { Header = t.Name };
            sub.Items.Add(Item(Loc.T("Apply to This Day"), async () =>
            {
                if (store.EntriesOn(day).Count == 0 || await LogbookDialogs.Confirm(this, Loc.T("Replace this day's entries with “%@”?", t.Name), Loc.T("Replace")))
                    store.Apply(t, day);
            }));
            sub.Items.Add(Item(Loc.T("Delete Template"), () => store.Templates = store.Templates.Where(x => x.Id != t.Id).ToList()));
            menu.Items.Add(sub);
        }
        return menu;
    }

    MenuFlyout ExportMenu(DateTimeOffset month)
    {
        var target = settings.Target(store);
        var menu = new MenuFlyout();
        menu.Items.Add(Item(Loc.T("Monthly Report (Excel)…"), () => _ = shell.Save(store.MonthReport(month, target), Report.ExportName(month, ".xlsx"), this)));
        menu.Items.Add(Item(Loc.T("Master Workbook (Excel)…"), () => _ = shell.Save(store.MasterWorkbook(target), "Outatime Master.xlsx", this)));
        // The month on screen, so last month's clients can still be billed early in this one.
        var clients = store.BilledClients(month);
        if (clients.Count > 0) menu.Items.Add(new Separator());
        foreach (var p in clients)
            menu.Items.Add(Item(Loc.T("Monthly Report — %@ (Excel)…", p.Name), () => _ = shell.Save(store.ClientReport(p, month), Report.ClientExportName(month, p.Name), this)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.T("Daily Summary CSV…"), () => _ = shell.Save(Csv.Daily(store.EntriesInMonth(month), month, target), Report.ExportName(month, " daily.csv"), this)));
        menu.Items.Add(Item(Loc.T("Entries CSV…"), () => _ = shell.Save(store.EntriesCsv(month), Report.ExportName(month, " entries.csv"), this)));
        return menu;
    }

    // MARK: Summary

    Control Summary()
    {
        var t = Store.TotalsOf(store.EntriesOn(day));
        var balance = t.Worked(settings.ExcludedFromTarget) - settings.Target(store).Owed(day);
        var totals = Ui.H(14);
        foreach (var a in Activities.All.Where(a => a == Activity.Work || t.Get(a) > 0))
            totals.Children.Add(Ui.H(4, Ui.Icon(a.Symbol(), 14, a.Fill()), Ui.Text(t.Get(a).Hm(), color: a.Fill(), tabular: true)));

        var targetText = Ui.Text(Loc.T("Target %@h", settings.TargetHours.ToString("0.#", Loc.Culture)), tabular: true);
        var stepper = new ButtonSpinner { Content = targetText, Padding = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Center };
        stepper.Spin += (_, e) => settings.Set(s => s.TargetHours = Math.Clamp(s.TargetHours + (e.Direction == SpinDirection.Increase ? 0.5 : -0.5), 0, 16));

        var row = Ui.Spread(totals,
            Ui.Text(Loc.T("Balance %@%@", balance >= 0 ? "+" : "−", Math.Abs(balance).Hm()), 13, FontWeight.SemiBold,
                    color: balance >= 0 ? Ui.Green : null, secondary: balance < 0, tabular: true),
            stepper);
        var bar = new Border { Child = row, CornerRadius = new CornerRadius(20), Padding = new Thickness(16, 6), Margin = new Thickness(12) };
        bar.Bind(Border.BackgroundProperty, bar.GetResourceObservable("SectionBrush"));
        bar.Bind(Border.BorderBrushProperty, bar.GetResourceObservable("SectionBorderBrush"));
        bar.BorderThickness = new Thickness(1);
        return bar;
    }
}
