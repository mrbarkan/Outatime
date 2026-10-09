using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FluentIcons.Common;
using Outatime.App.Platform;
using static Outatime.App.Ui;

namespace Outatime.App;

/// Four short tabs: General, Target, Focus and Clients. (The Mac's fifth, Updates, is the Store's job here.)
public sealed class SettingsWindow : AppWindow
{
    readonly Shell shell;
    Store Store => shell.Store;
    Settings Settings => shell.Settings;
    readonly TabControl tabs = new() { Padding = new Thickness(0, 8, 0, 0) };
    bool? launchAtLogin;

    public SettingsWindow(Shell shell)
    {
        this.shell = shell;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Content = new Border { Padding = new Thickness(16, 8, 16, 16), Child = tabs };
        Settings.Changed += Render;
        Store.Changed += RenderUnlessEditing;
        Closed += (_, _) => { Settings.Changed -= Render; Store.Changed -= RenderUnlessEditing; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        _ = LoadStartup();
        Render();
    }

    async Task LoadStartup()
    {
        if (!Startup.Supported) return;
        launchAtLogin = await Startup.IsEnabled();
        Render();
    }

    public void SelectTab(int tab) => tabs.SelectedIndex = Math.Clamp(tab, 0, 3);

    /// Typing a client's name saves as you go; don't rebuild the field out from under the cursor.
    void RenderUnlessEditing()
    {
        if (FocusManager?.GetFocusedElement() is TextBox) return;
        Render();
    }

    void Render()
    {
        Title = Loc.T("Settings");
        var selected = Math.Max(0, tabs.SelectedIndex);
        tabs.ItemsSource = new[]
        {
            Tab(Loc.T("General"), General()), Tab(Loc.T("Target"), TargetTab()), Tab(Loc.T("Focus"), Focus()), Tab(Loc.T("Clients"), Clients()),
        };
        tabs.SelectedIndex = selected;
    }

    static TabItem Tab(string header, Control content) => new() { Header = header, FontSize = 15, Content = content };

    static TextBlock Caption(string text) => Text(text, 11, secondary: true).With(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; });

    static Control Toggle(string label, bool on, Action<bool> set, string? detail = null)
    {
        var toggle = new ToggleSwitch { IsChecked = on, OnContent = null, OffContent = null, MinWidth = 0 };
        toggle.IsCheckedChanged += (_, _) => set(toggle.IsChecked == true);
        var text = detail == null ? (Control)Text(label) : V(2, Text(label), Caption(detail));
        return Spread(text, toggle);
    }

    /// The Mac's Stepper: the value in the label, − and + on the right.
    static Control Stepper(string label, Action down, Action up, bool canDown = true, bool canUp = true)
    {
        RepeatButton Step(string glyph, Action act, bool enabled)
        {
            var b = new RepeatButton { Content = glyph, Width = 32, Padding = new Thickness(0, 4), HorizontalContentAlignment = HorizontalAlignment.Center, IsEnabled = enabled };
            b.Click += (_, _) => act();
            return b;
        }
        return Spread(Text(label, tabular: true), H(4, Step("−", down, canDown), Step("+", up, canUp)));
    }

    static ComboBox Picker<T>(IReadOnlyList<T> values, T current, Func<T, string> label, Action<T> set)
    {
        var combo = new ComboBox { ItemsSource = values.Select(label).ToList(), SelectedIndex = Math.Max(0, values.ToList().IndexOf(current)), MinWidth = 180 };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0 && !Equals(values[combo.SelectedIndex], current)) set(values[combo.SelectedIndex]); };
        return combo;
    }

    // MARK: Tabs

    Control General()
    {
        var appearance = Picker(Enum.GetValues<Appearance>(), Settings.Appearance,
            a => Loc.T(a switch { Appearance.Light => "Light", Appearance.Dark => "Dark", _ => "System" }), a => Settings.Set(s => s.Appearance = a));
        var language = Picker(Languages.All, Settings.Lang, l => l.Label(), l =>
        {
            Loc.Use(l);
            Settings.Set(s => s.Language = l.Code());
        });
        var tooltip = Picker(Enum.GetValues<TrayStyle>(), Settings.TrayStyle,
            t => Loc.T(t == TrayStyle.Elapsed ? "Time tracked" : "Time left today"), t => Settings.Set(s => s.TrayStyle = t));

        var rows = V(12,
            Labeled(Loc.T("Appearance"), appearance),
            Labeled(Loc.T("Language"), language),
            Labeled(Loc.T("Tooltip shows"), tooltip),
            Toggle(Loc.T("Colored tray icon"), Settings.ColoredIcon, on => Settings.Set(s => s.ColoredIcon = on)));
        if (Startup.Supported && launchAtLogin is { } login)
            rows.Children.Add(Toggle(Loc.T("Open at Login"), login, on => _ = SetLaunchAtLogin(on)));
        if (OperatingSystem.IsWindows())
            rows.Children.Add(Toggle(Loc.T("Global shortcuts"), Settings.GlobalShortcuts, on => Settings.Set(s => s.GlobalShortcuts = on),
                                     Loc.T("Ctrl+Alt+Shift+W starts or stops Work, Ctrl+Alt+Shift+B Break")));

        var data = V(10,
            Text(Loc.T("Data"), 13, FontWeight.SemiBold),
            H(8, Button(Loc.T("Export Data…"), () => _ = ExportData()), Button(Loc.T("Import Data…"), () => _ = ImportData()),
              Button(Loc.T("Open Data Folder"), () => Shell.OpenUrl(Path.GetDirectoryName(Store.Path)!))),
            Caption(Loc.T("Your entries, clients and objectives are one file, data.json. Export it before uninstalling; the Mac app reads it too.")));
        return V(12, Section(rows), Section(data));
    }

    async Task SetLaunchAtLogin(bool on)
    {
        launchAtLogin = await Startup.SetEnabled(on);
        Render();
    }

    async Task ExportData()
    {
        Store.Commit();
        await shell.Save(File.ReadAllBytes(Store.Path), "data.json", this);
    }

    async Task ImportData()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
        });
        if (files.Count == 0) return;
        string json;
        await using (var stream = await files[0].OpenReadAsync())
        using (var reader = new StreamReader(stream))
            json = await reader.ReadToEndAsync();
        try { DataFile.Read(json); }
        catch (Exception)
        {
            await Dialogs.Alert(this, Loc.T("Couldn't read that file. Nothing was changed."));
            return;
        }
        if (Store.Entries.Count > 0 && !await Dialogs.Confirm(this, Loc.T("Replace all your entries, clients and objectives with this file?"), Loc.T("Replace")))
            return;
        Store.Import(json);
    }

    Control TargetTab()
    {
        var target = Settings.Target(Store);
        var hours = Settings.TargetHours;
        var counts = H(4, new[] { Activity.Break, Activity.Lunch, Activity.Travel, Activity.OutOfOffice }.Select(a =>
        {
            // Work and Extra always count; the rest is the user's call. One row of icon toggles keeps the tab short.
            var on = Settings.Counts(a);
            var b = new ToggleButton { IsChecked = on, Content = Icon(a.Symbol(), 16, on ? Brushes.White : a.Fill()), Padding = new Thickness(8, 5) };
            if (on) b.Background = a.Fill();
            ToolTip.SetTip(b, a.Label());
            b.IsCheckedChanged += (_, _) => Settings.SetCounts(a, b.IsChecked == true);
            return (Control)b;
        }).ToArray());
        var since = new CalendarDatePicker { SelectedDate = Cal.Date(Settings.BankStart(target)), IsTodayHighlighted = true };
        since.SelectedDateChanged += (_, _) =>
        {
            if (since.SelectedDate is { } d && Cal.At(d.Date) != Settings.BankSince) Settings.Set(s => s.BankSince = Cal.At(d.Date));
        };
        return Section(
            Stepper(Loc.T("Daily target %@h", hours.ToString("0.#", Loc.Culture)),
                    () => Settings.Set(s => s.TargetHours = Math.Max(0, hours - 0.5)), () => Settings.Set(s => s.TargetHours = Math.Min(16, hours + 0.5)),
                    hours > 0, hours < 16),
            Labeled(Loc.T("Counts toward the target"), counts),
            Labeled(Loc.T("Hours bank since"), since),
            Toggle(Loc.T("Switch to Extra after the target"), Settings.AutoExtra, on => Settings.Set(s => s.AutoExtra = on)),
            Caption(Loc.T("Weekends and days off owe nothing; mark a day off from the Logbook toolbar.")));
    }

    /// The tomato timer and the reminders that get you out of the chair.
    Control Focus()
    {
        var s = Settings;
        Control Minutes(string key, int value, int min, int max, int step, Action<int> set) =>
            Stepper(Loc.T(key, value), () => s.Set(_ => set(Math.Max(min, value - step))), () => s.Set(_ => set(Math.Min(max, value + step))),
                    value > min, value < max);
        var tomato = Section(
            Text(Loc.T("Tomato timer"), 13, FontWeight.SemiBold),
            Minutes("Focus %lld min", s.FocusMinutes, 5, 90, 5, v => s.FocusMinutes = v),
            Minutes("Short break %lld min", s.ShortBreakMinutes, 1, 30, 1, v => s.ShortBreakMinutes = v),
            Minutes("Long break %lld min", s.LongBreakMinutes, 5, 60, 5, v => s.LongBreakMinutes = v),
            Caption(Loc.T("Turn it on with the timer button in the menu. Each round ends with a notification that can switch the tracker for you.")));
        var stretch = s.StretchMinutes;
        var after = s.RemindAfterHours;
        var reminders = Section(
            Text(Loc.T("Reminders"), 13, FontWeight.SemiBold),
            Stepper(stretch > 0 ? Loc.T("Remind me to stretch every %lld min", stretch) : Loc.T("No stretch reminder"),
                    () => s.Set(x => x.StretchMinutes = Math.Max(0, stretch - 5)), () => s.Set(x => x.StretchMinutes = Math.Min(120, stretch + 5)),
                    stretch > 0, stretch < 120),
            Stepper(after > 0 ? Loc.T("Remind me when a timer runs %@h", after.ToString("0.#", Loc.Culture)) : Loc.T("No long-timer reminder"),
                    () => s.Set(x => x.RemindAfterHours = Math.Max(0, after - 1)), () => s.Set(x => x.RemindAfterHours = Math.Min(24, after + 1)),
                    after > 0, after < 24));
        return V(12, tomato, reminders);
    }

    Control Clients()
    {
        var rows = V(8);
        foreach (var p in Store.ActiveProfiles)
        {
            var name = new TextBox { Text = p.Name, HorizontalAlignment = HorizontalAlignment.Stretch };
            name.LostFocus += (_, _) => Rename(p.Id, name.Text);
            name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Rename(p.Id, name.Text); };
            rows.Children.Add(Spread(name, IconButton(Symbol.SubtractCircle, () => Store.RemoveProfile(p.Id), Loc.T("Remove"))));
        }
        var added = new TextBox { PlaceholderText = Loc.T("New client") };
        void Add()
        {
            if (Store.AddProfile(added.Text ?? "") != null) added.Text = "";
        }
        added.KeyDown += (_, e) => { if (e.Key == Key.Enter) Add(); };
        rows.Children.Add(Spread(added, Button(Loc.T("Add"), Add)));
        rows.Children.Add(Caption(Loc.T("Pick the client in the menu. Work, Extra and Travel are tracked for it.")));
        return Section(rows);
    }

    void Rename(Guid id, string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || Store.ProfileNames.GetValueOrDefault(id) == name) return;
        Store.Rename(id, name);
    }
}

/// Small modal questions.
public static class Dialogs
{
    public static Task Alert(Window owner, string message) => Ask(owner, message, null, Loc.T("Done"));

    public static async Task<bool> Confirm(Window owner, string message, string confirm, bool destructive = true) =>
        await Ask(owner, message, Loc.T("Cancel"), confirm, destructive);

    /// A text field and Save; null when cancelled.
    public static async Task<string?> Prompt(Window owner, string title, string placeholder, string confirm)
    {
        var box = new TextBox { PlaceholderText = placeholder, MinWidth = 260 };
        string? result = null;
        var dialog = Dialog(title);
        var ok = Button(confirm, () => { result = box.Text?.Trim(); dialog.Close(); }, classes: "accent");
        ok.IsDefault = true;
        ok.IsEnabled = false;
        box.TextChanged += (_, _) => ok.IsEnabled = !string.IsNullOrWhiteSpace(box.Text);
        var cancel = Button(Loc.T("Cancel"), dialog.Close);
        cancel.IsCancel = true;
        dialog.Content = new Border { Padding = new Thickness(20), Child = V(16, Text(title, 14, FontWeight.SemiBold), box,
                                                                               H(8, cancel, ok).With(h => h.HorizontalAlignment = HorizontalAlignment.Right)) };
        dialog.Opened += (_, _) => box.Focus();
        await dialog.ShowDialog(owner);
        return string.IsNullOrEmpty(result) ? null : result;
    }

    static async Task<bool> Ask(Window owner, string message, string? cancel, string confirm, bool destructive = false)
    {
        var yes = false;
        var dialog = Dialog("Outatime");
        var ok = Button(confirm, () => { yes = true; dialog.Close(); }, classes: destructive ? null : "accent");
        if (destructive) ok.Foreground = Red;
        ok.IsDefault = true;
        var buttons = H(8, ok).With(h => h.HorizontalAlignment = HorizontalAlignment.Right);
        if (cancel != null)
        {
            var no = Button(cancel, dialog.Close);
            no.IsCancel = true;
            buttons.Children.Insert(0, no);
        }
        dialog.Content = new Border { Padding = new Thickness(20), Child = V(18,
            Text(message, 13).With(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; t.MaxWidth = 340; }), buttons) };
        await dialog.ShowDialog(owner);
        return yes;
    }

    static Window Dialog(string title) => new AppWindow
    {
        Title = title, SizeToContent = SizeToContent.WidthAndHeight, CanResize = false, ShowInTaskbar = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, CanMinimize = false, CanMaximize = false,
    };
}
