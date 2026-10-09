using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FluentIcons.Common;
using static Outatime.App.Ui;

namespace Outatime.App;

/// The tray panel: what's running, a note, the client, six activity tiles, today's and the period's balances, and the
/// footer with the Logbook, exports, the objective countdown and the app menu. Opens above the tray, closes when it
/// loses focus.
public sealed class MenuPanel : AppWindow
{
    readonly Shell shell;
    Store Store => shell.Store;
    Settings Settings => shell.Settings;
    readonly TextBox note;
    readonly StackPanel body = new() { Spacing = 14 };
    readonly DispatcherTimer timer;
    TextBlock? elapsed, tomatoText;
    bool closing;

    public MenuPanel(Shell shell)
    {
        this.shell = shell;
        Title = "Outatime";
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowDecorations = WindowDecorations.BorderOnly;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;

        note = new TextBox { AcceptsReturn = false };
        note.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Store.AddNote(note.Text ?? "");
            note.Text = "";
            e.Handled = true;
        };
        Content = new Border { Padding = new Thickness(14), Child = body };

        Store.Changed += Render;
        Settings.Changed += Render;
        timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick());
        timer.Start();
        Deactivated += (_, _) => { if (!closing) Dispatcher.UIThread.Post(Close); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closing += (_, _) => closing = true;
        Closed += (_, _) =>
        {
            Store.Changed -= Render;
            Settings.Changed -= Render;
            timer.Stop();
        };
        SizeChanged += (_, _) => Place();
        Render();
    }

    /// Bottom right, above the taskbar; or top right when the taskbar (or the Mac's menu bar) is at the top.
    public void ShowNearTray()
    {
        Opened += (_, _) => { Place(); note.Focus(); };
        Show();
        Activate();
    }

    void Place()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen == null) return;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var w = (int)(Bounds.Width * scale);
        var h = (int)(Bounds.Height * scale);
        const int margin = 12;
        var top = area.Y > screen.Bounds.Y || !OperatingSystem.IsWindows();
        Position = new PixelPoint(area.Right - w - margin, top ? area.Y + margin : area.Bottom - h - margin);
    }

    void Tick()
    {
        if (Store.Running is { } r && elapsed != null) elapsed.Text = Clock(global::Outatime.Clock.Now.Since(Store.RunningSince ?? r.Start));
        if (tomatoText != null && shell.TomatoRound is { } round) tomatoText.Text = TomatoClock(round);
    }

    /// "1:02:03", or "2:03" under an hour, like the Mac's timer text.
    static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, Math.Floor(seconds)));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    static string TomatoClock(Pomodoro.Round round)
    {
        var left = round.End.Since(global::Outatime.Clock.Now);
        var t = TimeSpan.FromSeconds(Math.Floor(Math.Abs(left)));
        var clock = $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        return left > 0 ? Loc.T("%@ left", clock) : Loc.T("%@ over", clock);
    }

    void Render()
    {
        body.Children.Clear();
        body.Children.Add(Status());
        var target = Store.NoteTarget;
        note.PlaceholderText = Store.Running != null ? Loc.T("What are you working on?") : Loc.T("Add a note to the last entry");
        note.IsEnabled = target != null;
        body.Children.Add(note);
        if (target is { Notes.Count: > 0 } t)
            body.Children.Add(V(2, t.Notes.Select(n => (Control)Text(Loc.T("· %@", n), 11, secondary: true)).ToArray()));
        if (Store.ActiveProfiles.Count > 0) body.Children.Add(ClientPicker());
        body.Children.Add(Tiles());
        body.Children.Add(Totals());
        body.Children.Add(Divider());
        body.Children.Add(Footer());
    }

    Control Status()
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        var title = H(8);
        elapsed = null;
        if (Store.Running is { } running)
        {
            title.Children.Add(Icon(running.Activity.Symbol(), 20, running.Activity.Fill()));
            title.Children.Add(Text(running.Activity.Label(), 16, FontWeight.SemiBold));
            if (running.Profile is { } p && Store.ProfileNames.TryGetValue(p, out var name))
                title.Children.Add(Text(Loc.T("· %@", name), 16, secondary: true).With(x => x.MaxWidth = 120));
            elapsed = Text(Clock(global::Outatime.Clock.Now.Since(Store.RunningSince ?? running.Start)), 16, secondary: true, tabular: true);
            Grid.SetColumn(elapsed, 2);
            elapsed.Margin = new Thickness(8, 0);
            row.Children.Add(elapsed);
        }
        else
        {
            title.Children.Add(Icon(Symbol.Clock, 20, null).With(i => i.Opacity = 0.62));
            title.Children.Add(Text(Loc.T("Not tracking"), 16, secondary: true));
        }
        row.Children.Add(title);
        var tomatoOn = Store.TomatoSince != null;
        var tomato = IconButton(Symbol.Timer, shell.ToggleTomato, Loc.T("Tomato timer"), tomatoOn ? Red : null, 20);
        if (!tomatoOn) tomato.Opacity = 0.62;
        Grid.SetColumn(tomato, 3);
        row.Children.Add(tomato);

        var status = V(4, row);
        tomatoText = null;
        if (tomatoOn)
        {
            if (shell.TomatoRound is { } round)
            {
                var phase = round.Phase switch
                {
                    Pomodoro.Phase.Focus => Loc.T("Round %lld", round.Number),
                    Pomodoro.Phase.ShortBreak => Loc.T("Break"),
                    _ => Loc.T("Long break"),
                };
                tomatoText = Text(TomatoClock(round), 11, secondary: true, tabular: true);
                status.Children.Add(Spread(Text(phase, 11, secondary: true), tomatoText));
            }
            else status.Children.Add(Text(Loc.T("Paused"), 11, secondary: true));  // tracking something other than Work or Break
        }
        // Left running overnight: it was cut at midnight, and the Logbook shows where.
        if (Store.RunningSince is { } since && !Cal.IsToday(since))
            status.Children.Add(H(4, Icon(Symbol.Warning, 12, Orange, filled: true),
                                  Text(Loc.T("Running since %@", since.Format("ddd t")), 11, color: Orange)));
        return status;
    }

    /// The client new Work, Extra and Travel blocks are tracked for: a grid of buttons while it stays a few rows short,
    /// a list beyond that.
    Control ClientPicker()
    {
        var clients = Store.ActiveProfiles;
        if (clients.Count > 8)
        {
            var items = new List<(Guid? Id, string Name)> { (null, Loc.T("No client")) };
            items.AddRange(clients.Select(c => ((Guid?)c.Id, c.Name)));
            var combo = new ComboBox { ItemsSource = items.Select(i => i.Name).ToList(), HorizontalAlignment = HorizontalAlignment.Stretch,
                                       SelectedIndex = Math.Max(0, items.FindIndex(i => i.Id == Store.CurrentProfile)) };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) Store.Select(items[combo.SelectedIndex].Id); };
            return combo;
        }
        var ids = new List<Guid?> { null };
        ids.AddRange(clients.Select(c => (Guid?)c.Id));
        // Rows fill up: two or three buttons a row, never one stranded on its own when a 2×2 would do.
        var columns = ids.Count == 4 ? 2 : Math.Min(3, ids.Count);
        var grid = new Avalonia.Controls.Primitives.UniformGrid { Columns = columns };
        foreach (var id in ids)
        {
            var selected = Store.CurrentProfile == id;
            var label = id is { } g ? Store.ProfileNames[g] : Loc.T("No client");
            var b = Button(Text(label, 12, selected ? FontWeight.SemiBold : FontWeight.Normal, selected ? Brushes.White : null), () => Store.Select(id),
                           classes: "tile");
            b.Margin = new Thickness(3);
            b.Padding = new Thickness(6, 5);
            if (selected) b.Background = Accent;
            grid.Children.Add(b);
        }
        return grid.With(g => g.Margin = new Thickness(-3));
    }

    Control Tiles()
    {
        var grid = new Avalonia.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(-5) };
        foreach (var a in new[] { Activity.Work, Activity.Break, Activity.Lunch, Activity.Extra, Activity.Travel, Activity.OutOfOffice })
        {
            var active = Store.Running?.Activity == a;
            var content = V(4,
                Icon(a.Symbol(), 24, active ? Brushes.White : a.Fill()).With(i => { i.HorizontalAlignment = HorizontalAlignment.Center; i.Height = 28; }),
                Text(a.Label(), 13, active ? FontWeight.SemiBold : FontWeight.Normal, active ? Brushes.White : null)
                    .With(t => t.HorizontalAlignment = HorizontalAlignment.Center));
            var tile = Button(content, () => { if (active) Store.Stop(); else Store.Start(a); }, classes: "tile");
            tile.Margin = new Thickness(5);
            tile.MinHeight = 62;
            tile.Padding = new Thickness(6, 8);
            if (active) tile.Background = a.Fill();
            grid.Children.Add(tile);
        }
        return grid;
    }

    Control Totals()
    {
        var now = global::Outatime.Clock.Now;
        var t = Store.TotalsOn(now);
        var target = Settings.Target(Store);
        var week = Store.Balance(Cal.Week(now), target, now);
        var month = Store.Balance(Cal.Month(now), target, now);
        var since = Settings.BankStart(target);
        var bank = Store.Balance(new Interval(Cal.StartOfDay(since), now), target, now);

        var icons = H(12, Activities.All.Where(a => a == Activity.Work || t.Get(a) > 0)
            .Select(a => (Control)H(4, Icon(a.Symbol(), 13, a.Fill()), Text(t.Get(a).Hm(), 11, tabular: true))).ToArray());

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), RowSpacing = 4, ColumnSpacing = 12 };
        var r = 0;
        void Row(string label, Control a, Control b)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = Text(label, 11, secondary: true);
            Grid.SetRow(l, r); Grid.SetRow(a, r); Grid.SetRow(b, r);
            Grid.SetColumn(a, 1); Grid.SetColumn(b, 2);
            a.HorizontalAlignment = HorizontalAlignment.Right; b.HorizontalAlignment = HorizontalAlignment.Right;
            grid.Children.Add(l); grid.Children.Add(a); grid.Children.Add(b);
            r++;
        }
        void Balance(string label, double worked, double balance, IBrush shortfall) =>
            Row(label, Text(worked.Hm(), 11, tabular: true),
                Text(balance.Signed(), 11, tabular: true, color: balance >= 0 ? Green : shortfall, secondary: balance < 0 && shortfall != Red));
        // Today is still in progress: a shortfall isn't alarming yet.
        var today = t.Worked(Settings.ExcludedFromTarget);
        Balance(Loc.T("Today"), today, today - target.Owed(now, now), Brushes.Gray);
        Balance(Loc.T("This Week"), week.Worked, week.Balance, Red);
        Balance(Loc.T("This Month"), month.Worked, month.Balance, Red);
        Balance(Loc.T("Since %@", since.Format("d MMM")), bank.Worked, bank.Balance, Red);
        var stats = new Stats(Store.Entries, target, since, now);
        if (stats.WeekGoal > 0)
            Row(Loc.T("Left this week"), stats.WeekLeft > 0 ? Text(stats.WeekLeft.Hm(), 11, tabular: true) : Text(Loc.T("Done"), 11, color: Green),
                Text(Loc.T("of %@", stats.WeekGoal.Hm()), 11, secondary: true, tabular: true));
        if (stats.DayTarget > 0)
            Row(Loc.T("To next day off"), Text(stats.ToDayOff.Hm(), 11, tabular: true),
                stats.DaysOffBanked > 0 ? Text(Loc.T("%lld banked", stats.DaysOffBanked), 11, color: Green) : Text("", 11));
        return V(8, icons, grid);
    }

    Control Footer()
    {
        var logbook = Button(H(6, Icon(Symbol.Calendar, 15), Text(Loc.T("Logbook"))), shell.ShowLogbook, classes: "plain");
        var export = Button(H(6, Icon(Symbol.ArrowExportUp, 15), Text(Loc.T("Export"))), () => { }, classes: "plain");
        export.Flyout = ExportMenu();
        var more = IconButton(Symbol.MoreHorizontal, () => { }, Loc.T("More"));
        more.Flyout = new MenuFlyout
        {
            Items =
            {
                Item(Loc.T("About"), shell.ShowAbout),
                Item(Loc.T("User Manual"), () => { Close(); shell.ShowManual(); }),
                Item(Loc.T("Settings…"), () => shell.ShowSettings(), "Ctrl+,"),
                new Separator(),
                Item(Loc.T("Quit"), shell.Quit, "Ctrl+Q"),
            },
        };
        // Tight, so the longest translations ("Diário de bordo", "Exportar") still leave room for the objective.
        foreach (var b in new[] { logbook, export }) b.Padding = new Thickness(5, 4);
        var objective = ObjectiveButton();
        if (objective is Button o) o.Padding = new Thickness(5, 4);
        var left = H(0, logbook, export, objective);
        left.Margin = new Thickness(-5, 0, 0, 0);
        return Spread(left, more);
    }

    static MenuItem Item(string header, Action click, string? gesture = null)
    {
        var item = new MenuItem { Header = header };
        if (gesture != null) item.InputGesture = KeyGesture.Parse(gesture);
        item.Click += (_, _) => click();
        return item;
    }

    MenuFlyout ExportMenu()
    {
        var month = Cal.StartOfMonth(global::Outatime.Clock.Now);
        var target = Settings.Target(Store);
        void Export(byte[] data, string name) { Close(); _ = shell.Save(data, name); }
        var menu = new MenuFlyout
        {
            Items =
            {
                Item(Loc.T("This Month — Report (Excel)…"), () => Export(Store.MonthReport(month, target), Report.ExportName(month, ".xlsx"))),
                Item(Loc.T("Master Workbook (Excel)…"), () => Export(Store.MasterWorkbook(target), "Outatime Master.xlsx")),
            },
        };
        var clients = Store.BilledClients(month);
        if (clients.Count > 0) menu.Items.Add(new Separator());
        foreach (var p in clients)
            menu.Items.Add(Item(Loc.T("This Month — %@ (Excel)…", p.Name), () => Export(Store.ClientReport(p, month), Report.ClientExportName(month, p.Name))));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.T("This Month — Daily Summary…"), () =>
            Export(System.Text.Encoding.UTF8.GetBytes(Csv.Daily(Store.EntriesInMonth(month), month, target)), Report.ExportName(month, " daily.csv"))));
        menu.Items.Add(Item(Loc.T("This Month — Entries…"), () =>
            Export(System.Text.Encoding.UTF8.GetBytes(Store.EntriesCsv(month)), Report.ExportName(month, " entries.csv"))));
        return menu;
    }

    // MARK: Objectives

    /// The one the panel counts down: the one picked, or the first.
    Objective? Shown => Store.Objectives.Cast<Objective?>().FirstOrDefault(o => o!.Value.Id.ToString() == Settings.MenuObjective)
                        ?? Store.Objectives.Cast<Objective?>().FirstOrDefault();

    /// Next to Export: the ring and what's left of one objective. Click it for all of them.
    Control? ObjectiveButton()
    {
        if (Shown is not { } o) return null;
        var p = ObjectiveInfo.Progress(Store, Settings, o);
        var button = Button(H(5, Ring(p.Fraction, p.IsDone ? Green : o.Kind.Fill()),
                              p.IsDone ? Text(Loc.T("Done")) : Text(p.Left.Short(), tabular: true)), () => { }, ObjectiveInfo.Title(Store, o), "plain");
        var list = V(12);
        foreach (var each in Store.Objectives)
        {
            var q = ObjectiveInfo.Progress(Store, Settings, each);
            var picked = each.Id == o.Id;
            var row = V(5,
                Spread(H(8, Icon(picked ? Symbol.CheckmarkCircle : each.Kind.Symbol(), 16, each.Kind.Fill(), filled: picked),
                         Text(ObjectiveInfo.Title(Store, each), 13, FontWeight.Medium)),
                       q.IsDone ? Text(Loc.T("Done"), 12, color: Green) : Text(Loc.T("%@ left", q.Left.Hm()), 12, tabular: true)),
                new ProgressBar { Value = q.Fraction, Maximum = 1, Foreground = q.IsDone ? Green : each.Kind.Fill(), MinWidth = 0 });
            // Picking one puts it in the panel.
            var pick = Button(row, () => Settings.Set(s => s.MenuObjective = each.Id.ToString()), classes: "plain");
            pick.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            pick.HorizontalAlignment = HorizontalAlignment.Stretch;
            list.Children.Add(pick);
        }
        list.Children.Add(Divider());
        list.Children.Add(Button(Loc.T("Edit Objectives…"), shell.ShowObjectives, classes: "link"));
        button.Flyout = new Flyout { Content = new Border { Width = 270, Padding = new Thickness(4), Child = list } };
        return button;
    }

    /// A ring that fills as an objective is met.
    public static Control Ring(double fraction, IBrush color, double size = 15, double width = 2.5)
    {
        var track = new Ellipse { Width = size, Height = size, Stroke = color, StrokeThickness = width, Opacity = 0.25 };
        var arc = new Arc { Width = size, Height = size, Stroke = color, StrokeThickness = width, StartAngle = -90,
                            SweepAngle = 360 * Math.Clamp(fraction, 0, 1), StrokeLineCap = PenLineCap.Round };
        return new Panel { Width = size, Height = size, Children = { track, arc }, VerticalAlignment = VerticalAlignment.Center };
    }
}

/// The settings and Store an objective's progress depends on, read the same way by the panel and the Objectives window.
public static class ObjectiveInfo
{
    public static Objective.ProgressInfo Progress(Store store, Settings settings, Objective o)
    {
        var target = settings.Target(store);
        var bank = o.Kind == ObjectiveKind.DaysOff
            ? store.Balance(new Interval(Cal.StartOfDay(settings.BankStart(target)), Clock.Now), target).Balance : 0;
        return o.Progress(store.Entries, bank, target.Seconds);
    }

    public static string Title(Store store, Objective o)
    {
        if (o.Name.Length > 0) return o.Name;
        if (o.Kind != ObjectiveKind.DaysOff && o.Profile is { } p && store.ProfileNames.TryGetValue(p, out var name)) return name;
        return o.Kind.Label();
    }

    public static string Money(double value, Objective o)
    {
        try
        {
            var culture = (System.Globalization.CultureInfo)Loc.Culture.Clone();
            culture.NumberFormat.CurrencySymbol = CurrencySymbol(o.Currency);
            return value.ToString(value % 1 == 0 ? "C0" : "C2", culture);
        }
        catch (Exception) { return $"{o.Currency} {value:0.##}"; }
    }

    static string CurrencySymbol(string code) =>
        System.Globalization.CultureInfo.GetCultures(System.Globalization.CultureTypes.SpecificCultures)
            .Select(c => { try { return new System.Globalization.RegionInfo(c.Name); } catch { return null; } })
            .FirstOrDefault(r => r?.ISOCurrencySymbol == code)?.CurrencySymbol ?? code;
}
