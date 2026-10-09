using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FluentIcons.Common;
using static Outatime.App.Ui;

namespace Outatime.App;

/// Objectives: days off to bank, or a client's package of hours or pay. One section each; the panel counts the
/// picked one down next to Export.
public sealed class ObjectivesWindow : AppWindow
{
    readonly Shell shell;
    Store Store => shell.Store;
    Settings Settings => shell.Settings;
    readonly StackPanel list = new() { Spacing = 12 };

    public ObjectivesWindow(Shell shell)
    {
        this.shell = shell;
        Title = Loc.T("Objectives");
        Width = 480;
        Height = 560;
        MinWidth = 420;
        MinHeight = 360;

        var add = Button(H(6, Icon(Symbol.AddCircle, 16), Text(Loc.T("Add Objective"))), () => { });
        add.Flyout = new MenuFlyout();
        foreach (var k in ObjectiveKinds.All)
        {
            var item = new MenuItem { Header = k.Label(), Icon = Icon(k.Symbol(), 16, k.Fill()) };
            item.Click += (_, _) => Add(k);
            ((MenuFlyout)add.Flyout).Items.Add(item);
        }
        var done = Button(Loc.T("Done"), Close, classes: "accent");
        done.IsDefault = true;
        var footer = new Border { Padding = new Thickness(14), Child = Spread(H(0, add), done) };
        DockPanel.SetDock(footer, Dock.Bottom);
        Content = new DockPanel { Children = { footer, new ScrollViewer { Content = new Border { Padding = new Thickness(16, 16, 16, 4), Child = list } } } };

        Store.Changed += RenderUnlessEditing;
        Settings.Changed += RenderUnlessEditing;
        Closed += (_, _) => { Store.Changed -= RenderUnlessEditing; Settings.Changed -= RenderUnlessEditing; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Render();
    }

    void RenderUnlessEditing()
    {
        if (FocusManager?.GetFocusedElement() is TextBox) return;
        Render();
    }

    void Render()
    {
        list.Children.Clear();
        if (Store.Objectives.Count == 0)
            list.Children.Add(Text(Loc.T("No objectives yet. Add one and the menu counts it down next to Export."), secondary: true)
                .With(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; }));
        foreach (var o in Store.Objectives) list.Children.Add(Section(o));
    }

    void Add(ObjectiveKind kind)
    {
        Guid? client = kind == ObjectiveKind.DaysOff ? null : Store.CurrentProfile ?? Store.ActiveProfiles.FirstOrDefault().Id;
        if (client == Guid.Empty) client = null;
        Store.Objectives.Add(new Objective(kind, kind switch { ObjectiveKind.DaysOff => 5, ObjectiveKind.Hours => 80, _ => 0 }, profile: client));
        Store.Commit();
    }

    /// Reads the latest copy, so two quick edits to one objective don't undo each other.
    void Edit(Guid id, Func<Objective, Objective> change)
    {
        if (Store.Objectives.Cast<Objective?>().FirstOrDefault(x => x!.Value.Id == id) is { } current) Store.Update(change(current));
    }

    Control Section(Objective o)
    {
        var shown = (Store.Objectives.Cast<Objective?>().FirstOrDefault(x => x!.Value.Id.ToString() == Settings.MenuObjective) ?? Store.Objectives[0]).Id == o.Id;
        var show = IconButton(shown ? Symbol.Pin : Symbol.PinOff, () => Settings.Set(s => s.MenuObjective = o.Id.ToString()), Loc.T("Show in Menu"),
                              shown ? Accent : null);
        if (!shown) show.Opacity = 0.62;
        var delete = IconButton(Symbol.Delete, () => { Store.Objectives.RemoveAll(x => x.Id == o.Id); Store.Commit(); }, Loc.T("Delete"), Red);
        var header = Spread(H(8, Icon(o.Kind.Symbol(), 16, o.Kind.Fill()), Text(o.Kind.Label(), 13, FontWeight.SemiBold)), show, delete);

        var name = new TextBox { Text = o.Name, PlaceholderText = ObjectiveInfo.Title(Store, o with { Name = "" }), MinWidth = 220 };
        void SaveName() { if ((name.Text ?? "") != o.Name) Edit(o.Id, x => x with { Name = name.Text ?? "" }); }
        name.LostFocus += (_, _) => SaveName();
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) SaveName(); };

        var rows = new List<Control?> { header, Labeled(Loc.T("Name"), name) };
        switch (o.Kind)
        {
            case ObjectiveKind.DaysOff:
                var days = (int)o.Amount;
                rows.Add(Spread(Text(Loc.T("Days off: %lld", days), tabular: true), H(4,
                    Button("−", () => Edit(o.Id, x => x with { Amount = Math.Max(1, x.Amount - 1) })).With(b => b.IsEnabled = days > 1),
                    Button("+", () => Edit(o.Id, x => x with { Amount = Math.Min(90, x.Amount + 1) })).With(b => b.IsEnabled = days < 90))));
                break;
            case ObjectiveKind.Hours:
                rows.Add(ClientPicker(o));
                rows.Add(Labeled(Loc.T("Hours"), Number(o.Amount, v => Edit(o.Id, x => x with { Amount = v }))));
                rows.Add(Labeled(Loc.T("Counting since"), Since(o)));
                break;
            case ObjectiveKind.Money:
                rows.Add(ClientPicker(o));
                rows.Add(Labeled(Loc.T("Amount"), Number(o.Amount, v => Edit(o.Id, x => x with { Amount = v }), o.Currency)));
                rows.Add(Labeled(Loc.T("Hourly rate"), Number(o.Rate, v => Edit(o.Id, x => x with { Rate = v }), o.Currency)));
                rows.Add(Labeled(Loc.T("Counting since"), Since(o)));
                break;
        }
        rows.Add(ProgressRow(o, ObjectiveInfo.Progress(Store, Settings, o)));
        rows.Add(Text(Loc.T(o.Kind switch
        {
            ObjectiveKind.DaysOff => "Overtime in the hours bank buys days off, one daily target each. Settings → Target sets where the bank starts.",
            ObjectiveKind.Hours => "The client's Work, Extra and Travel from that day on count toward the package.",
            _ => "The amount at your hourly rate is the hours you owe the client. Their Work, Extra and Travel from that day on count it down.",
        }), 11, secondary: true).With(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; }));
        return Ui.Section(rows.ToArray());
    }

    Control ClientPicker(Objective o)
    {
        // A removed client stays listed while an objective still counts its hours.
        var clients = new List<(Guid? Id, string Name)> { (null, Loc.T("No client")) };
        clients.AddRange(Store.Profiles.Where(p => !p.Archived || p.Id == o.Profile).Select(p => ((Guid?)p.Id, p.Name)));
        var combo = new ComboBox { ItemsSource = clients.Select(c => c.Name).ToList(), MinWidth = 220,
                                   SelectedIndex = Math.Max(0, clients.FindIndex(c => c.Id == o.Profile)) };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && clients[combo.SelectedIndex].Id != o.Profile) Edit(o.Id, x => x with { Profile = clients[combo.SelectedIndex].Id });
        };
        return Labeled(Loc.T("Client"), combo);
    }

    static Control Number(double value, Action<double> set, string? currency = null)
    {
        var box = new TextBox { Text = value.ToString("0.##", Loc.Culture), MinWidth = 140, HorizontalContentAlignment = HorizontalAlignment.Right };
        void Commit()
        {
            if (double.TryParse(box.Text, NumberStyles.Number, Loc.Culture, out var v) && v >= 0 && v != value) set(v);
            else box.Text = value.ToString("0.##", Loc.Culture);
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        return currency == null ? box : H(6, Text(currency, secondary: true), box);
    }

    Control Since(Objective o)
    {
        var picker = new CalendarDatePicker { SelectedDate = Cal.Date(o.Since) };
        picker.SelectedDateChanged += (_, _) =>
        {
            if (picker.SelectedDate is { } d && Cal.At(d.Date) != Cal.StartOfDay(o.Since)) Edit(o.Id, x => x with { Since = Cal.At(d.Date) });
        };
        return picker;
    }

    static Control ProgressRow(Objective o, Objective.ProgressInfo p)
    {
        Control status = p.Goal <= 0 ? Text(Loc.T("Nothing to count yet"), 12, secondary: true)
            : p.IsDone ? Text(Loc.T("Done"), 12, color: Green)
            : Text(Loc.T("%@ left of %@", p.Left.Hm(), p.Goal.Hm()), 12, secondary: true, tabular: true);
        Control? detail = o.Kind switch
        {
            ObjectiveKind.DaysOff when p.Goal > 0 =>
                H(4, Text(Loc.T("Days off banked"), 12, secondary: true), Text((p.Done / (p.Goal / o.Amount)).ToString("0.#", Loc.Culture), 12, secondary: true, tabular: true)),
            ObjectiveKind.Money when p.Goal > 0 =>
                Text(Loc.T("%@ left of %@", ObjectiveInfo.Money(p.Left / 3600 * o.Rate, o), ObjectiveInfo.Money(o.Amount, o)), 12, secondary: true, tabular: true),
            _ => null,
        };
        return V(6, new ProgressBar { Value = p.Fraction, Maximum = 1, Foreground = p.IsDone ? Green : o.Kind.Fill(), MinWidth = 0 },
                 detail == null ? status : Spread(status, detail));
    }
}
