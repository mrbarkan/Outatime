using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Outatime.App;

/// A block's editor, in a flyout over it: activity, client, start and end, notes, duration. Every change is written
/// back at once, as the Mac's binding does.
public sealed class EntryEditor : UserControl
{
    readonly Store store;
    Entry entry;
    readonly Action onDelete;
    readonly TimePicker endPicker = new();
    readonly TextBlock durationText = Ui.Text("", tabular: true);
    bool updating;

    public EntryEditor(Store store, Entry entry, Action onDelete)
    {
        this.store = store;
        this.entry = entry;
        this.onDelete = onDelete;
        Width = 320;
        Content = Build();
    }

    static string Clock24 => Loc.Culture.DateTimeFormat.ShortTimePattern.Contains('H') ? "24HourClock" : "12HourClock";

    Control Build()
    {
        // The block's own client stays listed after it was removed.
        var clients = store.Profiles.Where(p => !p.Archived || p.Id == entry.Profile).ToList();

        var activity = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        foreach (var a in Activities.All)
            activity.Items.Add(new ComboBoxItem { Content = Ui.H(6, Ui.Icon(a.Symbol(), 14, a.Fill()), Ui.Text(a.Label())), Tag = a });
        activity.SelectedIndex = Array.IndexOf(Activities.All, entry.Activity);

        var client = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        client.Items.Add(new ComboBoxItem { Content = Loc.T("No client"), Tag = null });
        foreach (var p in clients) client.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });
        client.SelectedIndex = entry.Profile is { } id ? clients.FindIndex(p => p.Id == id) + 1 : 0;
        var clientRow = Row(Loc.T("Client"), client);
        clientRow.IsVisible = entry.Activity.Billable() && clients.Count > 0;

        activity.SelectionChanged += (_, _) =>
        {
            if (activity.SelectedItem is not ComboBoxItem { Tag: Activity a }) return;
            Write(entry with { Activity = a, Profile = a.Billable() ? entry.Profile : null });
            clientRow.IsVisible = a.Billable() && clients.Count > 0;
            if (!a.Billable()) { updating = true; client.SelectedIndex = 0; updating = false; }
        };
        client.SelectionChanged += (_, _) =>
        {
            if (updating || client.SelectedItem is not ComboBoxItem item) return;
            Write(entry with { Profile = item.Tag as Guid? });
        };

        var start = new TimePicker { ClockIdentifier = Clock24, SelectedTime = Cal.Local(entry.Start).TimeOfDay, HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0 };
        start.SelectedTimeChanged += (_, e) =>
        {
            if (e.NewTime is not { } t) return;
            Write(entry with { Start = Cal.At(Cal.Date(entry.Start) + new TimeSpan(t.Hours, t.Minutes, 0)) });
        };

        Control end;
        if (entry.IsRunning) end = Ui.Text(Loc.T("running"), secondary: true);
        else
        {
            endPicker.ClockIdentifier = Clock24;
            endPicker.SelectedTime = Cal.Local(entry.End!.Value).TimeOfDay;
            endPicker.HorizontalAlignment = HorizontalAlignment.Stretch;
            endPicker.MinWidth = 0;
            endPicker.SelectedTimeChanged += (_, e) =>
            {
                if (updating || e.NewTime is not { } t) return;
                Write(entry with { End = Cal.At(Cal.Date(entry.End ?? entry.Start) + new TimeSpan(t.Hours, t.Minutes, 0)) });
            };
            end = endPicker;
        }

        // Each line is a note.
        var notes = new TextBox
        {
            Text = string.Join("\n", entry.Notes), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60, MaxHeight = 140,
            PlaceholderText = Loc.T("Notes"),
        };
        notes.TextChanged += (_, _) =>
            Write(entry with { Notes = (notes.Text ?? "").Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).ToList() });

        Control duration;
        durationText.Text = entry.Duration.Hm();
        if (entry.IsRunning) duration = durationText;
        else
        {
            // Steps land on the 5-minute grid; the start stays put.
            var spinner = new ButtonSpinner { Content = durationText, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 2) };
            spinner.Spin += (_, e) =>
            {
                var minutes = Math.Round(entry.Duration / 60 / 5) * 5 + (e.Direction == SpinDirection.Increase ? 5 : -5);
                minutes = Math.Clamp(minutes, 5, 1440);
                Write(entry with { End = entry.Start.Plus(minutes * 60) });
                updating = true;
                endPicker.SelectedTime = Cal.Local(entry.End!.Value).TimeOfDay;
                updating = false;
            };
            duration = spinner;
        }

        var delete = Ui.Button(Ui.H(6, Ui.Icon(FluentIcons.Common.Symbol.Delete, 14, Ui.Red), Ui.Text(Loc.T("Delete"), color: Ui.Red)), onDelete, classes: "plain");
        delete.HorizontalAlignment = HorizontalAlignment.Left;

        return Ui.V(10,
            Row(Loc.T("Activity"), activity), clientRow, Row(Loc.T("Start"), start), Row(Loc.T("End"), end),
            Row(Loc.T("Notes"), notes, top: true), Row(Loc.T("Duration"), duration), delete);
    }

    static Grid Row(string label, Control control, bool top = false)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("72,*") };
        var l = Ui.Text(label, secondary: true);
        if (top) { l.VerticalAlignment = VerticalAlignment.Top; l.Margin = new Thickness(0, 6, 0, 0); }
        g.Children.Add(l);
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    void Write(Entry changed)
    {
        entry = changed;
        durationText.Text = entry.Duration.Hm();
        store.Update(entry);
    }
}
