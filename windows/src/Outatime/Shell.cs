using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Outatime.App.Platform;

namespace Outatime.App;

/// The app around the windows: the tray icon and its tooltip, the ticks that cut timers at midnight, switch Work to
/// Extra and send reminders, the notifications' buttons, global shortcuts, and opening each window once.
public sealed class Shell
{
    readonly Store store;
    readonly Settings settings;
    readonly INotifier notifier;
    readonly TrayIcon? tray;
    readonly DispatcherTimer second, halfMinute;
    readonly ReminderGate stretch = new();
    DateTimeOffset? longTimerSent;
    (DateTimeOffset Start, Pomodoro.Phase Phase)? tomatoSent;
    string? trayKey;
    MenuPanel? panel;
    DateTimeOffset panelClosedAt;
    readonly Dictionary<string, Window> windows = [];

    public Store Store => store;
    public Settings Settings => settings;

    public Shell(Store store, Settings settings, INotifier notifier, bool showTray = true)
    {
        this.store = store;
        this.settings = settings;
        this.notifier = notifier;
        notifier.Action += a => Dispatcher.UIThread.Post(() => store.Start(a));

        if (showTray)
        {
            tray = new TrayIcon { Menu = TrayMenu() };
            tray.Clicked += (_, _) => TogglePanel();
            TrayIcon.SetIcons(Application.Current!, [tray]);
            UpdateTray();
            HotKeys.Install(a => Dispatcher.UIThread.Post(() => { if (store.Running?.Activity == a) store.Stop(); else store.Start(a); }));
            HotKeys.SetEnabled(settings.GlobalShortcuts);
            settings.Changed += () => HotKeys.SetEnabled(settings.GlobalShortcuts);
        }
        store.Changed += UpdateTray;
        settings.Changed += UpdateTray;

        second = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => { UpdateTray(); CheckTomato(); });
        halfMinute = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => Tick());
        second.Start();
        halfMinute.Start();
        if (showTray)
        {
            Dispatcher.UIThread.Post(Tick);
            Dispatcher.UIThread.Post(OpenWhatsNewIfUpdated);
            Dispatcher.UIThread.Post(() => DevSupport.OpenIfAsked(this));
        }
    }

    // MARK: Ticks

    /// Cuts timers at midnight, switches Work to Extra at the target, and sends the long-timer and stretch reminders.
    public void Tick()
    {
        var now = Clock.Now;
        store.RollOver(now);
        if (settings.AutoExtra && store.ShiftToExtra(settings.Target(store), now))
            notifier.Post("extra", Loc.T("Daily target reached"), Loc.T("Now tracking Extra."));
        // One notification per running stretch once it passes the limit — the timer was probably forgotten.
        if (store.RunningSince is { } since && settings.RemindAfterHours > 0 && now.Since(since) >= settings.RemindAfterHours * 3600 && longTimerSent != since)
        {
            longTimerSent = since;
            notifier.Post("long-timer", Loc.T("Still tracking?"), Loc.T("The timer has been running for %@.", now.Since(since).Hm()));
        }
        // The tomato's breaks already get you up.
        var every = store.TomatoSince == null ? settings.StretchMinutes : 0;
        var seated = store.SeatedSince;
        if (stretch.Due(seated, Stretch.Reminders(store.Running?.Activity, seated, every, now)))
            notifier.Post("stretch", Loc.T("Time to stretch"), Loc.T("You've been at it for %@. Get up and move a little.", now.Since(seated!.Value).Hm()));
    }

    public Pomodoro.Round? TomatoRound => store.TomatoSince is { } since ? settings.Pomodoro.RoundOf(store.Entries, since) : null;

    /// The end of each tomato round, once: a notification whose button switches Work and Break.
    void CheckTomato()
    {
        if (TomatoRound is not { } r || r.End > Clock.Now || tomatoSent == (r.Start, r.Phase)) return;
        tomatoSent = (r.Start, r.Phase);
        if (Clock.Now.Since(r.End) > 60) return;  // long past (the PC slept): no stale nudge
        if (r.Phase == Pomodoro.Phase.Focus)
            notifier.Post("tomato", Loc.T("Time for a break"), Loc.T("Round %lld done.", r.Number), Activity.Break, Loc.T("Start Break"));
        else
            notifier.Post("tomato", Loc.T("Break's over"), Loc.T("Ready for round %lld?", r.Number + 1), Activity.Work, Loc.T("Back to Work"));
    }

    public void ToggleTomato()
    {
        if (store.TomatoSince != null) { store.TomatoSince = null; notifier.Cancel("tomato"); return; }
        store.TomatoSince = Clock.Now;
        store.Start(Activity.Work);
    }

    // MARK: Tray

    NativeMenu TrayMenu()
    {
        NativeMenuItem Item(string title, Action action)
        {
            var i = new NativeMenuItem(title);
            i.Click += (_, _) => action();
            return i;
        }
        return
        [
            Item(Loc.T("Open Outatime"), () => ShowPanel()),
            Item(Loc.T("Logbook"), ShowLogbook),
            Item(Loc.T("Settings…"), () => ShowSettings()),
            new NativeMenuItemSeparator(),
            Item(Loc.T("Quit"), Quit),
        ];
    }

    /// Icon in the activity's color, and the tooltip the Mac shows as menu bar text.
    void UpdateTray()
    {
        if (tray == null) return;
        var running = store.Running;
        var tip = TrayText();
        var key = $"{running?.Activity}|{settings.ColoredIcon}|{store.TomatoSince != null}|{tip}";
        if (key == trayKey) return;
        if (trayKey == null || trayKey.Split('|')[..3] is var old && !old.SequenceEqual(key.Split('|')[..3]))
            tray.Icon = Platform.TrayIcons.For(running?.Activity, settings.ColoredIcon, store.TomatoSince != null);
        tray.ToolTipText = tip;
        trayKey = key;
    }

    public string TrayText()
    {
        var now = Clock.Now;
        if (TomatoRound is { } round)
            return $"Outatime — {(round.Phase == Pomodoro.Phase.Focus ? Loc.T("Round %lld", round.Number) : Loc.T("Break"))} · {round.Countdown(now)}";
        if (settings.TrayStyle == TrayStyle.Remaining)
        {
            var left = store.TotalsOn(now).Worked(settings.ExcludedFromTarget) - settings.Target(store).Owed(now, now);
            return $"Outatime — {left.Signed()}";
        }
        if (store.Running is { } r)
        {
            var client = r.Profile is { } p && store.ProfileNames.TryGetValue(p, out var name) ? $" · {name}" : "";
            return $"Outatime — {r.Activity.Label()}{client} · {now.Since(store.RunningSince ?? r.Start).Hm()}";
        }
        return $"Outatime — {Loc.T("Not tracking")}";
    }

    // MARK: Windows

    public void TogglePanel()
    {
        // A click on the icon while the panel is open first takes focus from it, which closes it; don't reopen.
        if (panel != null) { panel.Close(); return; }
        if ((DateTimeOffset.Now - panelClosedAt).TotalMilliseconds < 300) return;
        ShowPanel();
    }

    public void ShowPanel()
    {
        if (panel != null) { panel.Activate(); return; }
        panel = new MenuPanel(this);
        panel.Closed += (_, _) => { panel = null; panelClosedAt = DateTimeOffset.Now; };
        panel.ShowNearTray();
    }

    public void ClosePanel() => panel?.Close();

    /// Each window opens once; asking again brings it forward.
    public T Show<T>(string id, Func<T> make) where T : Window
    {
        ClosePanel();
        if (windows.TryGetValue(id, out var open))
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return (T)open;
        }
        var w = make();
        windows[id] = w;
        w.Closed += (_, _) => windows.Remove(id);
        w.Show();
        w.Activate();
        return w;
    }

    public bool IsOpen(string id) => windows.ContainsKey(id);

    public void ShowLogbook() => Show("logbook", () => new LogbookWindow(this));
    public void ShowObjectives() => Show("logbook", () => new LogbookWindow(this)).ShowObjectives();
    public void ShowSettings(int tab = 0) => Show("settings", () => new SettingsWindow(this)).SelectTab(tab);
    public void ShowAbout() => Show("about", () => new AboutWindow());
    public void ShowWhatsNew(string from) => Show("whats-new", () => new WhatsNewWindow(from));

    /// At launch: open What's New if this version (or one skipped on the way) has notes the user hasn't seen.
    void OpenWhatsNewIfUpdated()
    {
        var from = WhatsNew.CatchUpFrom(WhatsNew.Releases, settings.LastSeenVersion, App.Version, store.Entries.Count > 0);
        if (WhatsNew.RecordsLastSeen(App.Version) && settings.LastSeenVersion != App.Version) settings.Set(s => s.LastSeenVersion = App.Version);
        if (from != null) ShowWhatsNew(from);
    }

    public void ShowManual() => OpenUrl(settings.Lang.ManualUrl());

    public static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { }
    }

    public void Quit()
    {
        tray?.Dispose();
        HotKeys.SetEnabled(false);
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
            life.Shutdown();
    }

    // MARK: Files

    /// A Save dialog for an export, from whichever window is open (the panel closes first).
    public async Task Save(byte[] data, string suggestedName, Window? owner = null)
    {
        var top = owner ?? windows.Values.FirstOrDefault() ?? (Window?)panel;
        var temporary = top == null;
        if (top == null)
        {
            // The panel closes as the dialog takes focus; a small invisible window carries the dialog instead.
            top = new Window { Width = 1, Height = 1, ShowInTaskbar = false, WindowDecorations = WindowDecorations.None, Opacity = 0 };
            top.Show();
        }
        try
        {
            var ext = System.IO.Path.GetExtension(suggestedName).TrimStart('.');
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = suggestedName,
                DefaultExtension = ext,
                FileTypeChoices = [new FilePickerFileType(ext == "xlsx" ? "Excel" : ext.ToUpperInvariant()) { Patterns = [$"*.{ext}"] }],
            });
            if (file == null) return;
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await stream.WriteAsync(data);
        }
        finally
        {
            if (temporary) top.Close();
        }
    }

    public Task Save(string text, string suggestedName, Window? owner = null) => Save(System.Text.Encoding.UTF8.GetBytes(text), suggestedName, owner);
}
