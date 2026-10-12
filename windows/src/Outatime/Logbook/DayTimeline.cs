using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FluentIcons.Common;

namespace Outatime.App;

/// Calendar-style day view: drag a block to move it, drag its top/bottom edge to resize, click to edit, double-click
/// empty space to add, double-click inside a block to cut in a break. Ctrl-click and Shift-click select several.
public sealed class DayTimeline : UserControl
{
    const double Gutter = 48, Pad = 10, BlockMinHeight = 14, Grip = 8;
    readonly Shell shell;
    Store store => shell.Store;
    Settings settings => shell.Settings;
    readonly ScrollViewer scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    readonly Grid canvas = new();
    readonly Dictionary<Guid, Border> blocks = [];

    DateTimeOffset day;
    double renderedHourHeight;
    List<Entry> entries = [];
    public readonly HashSet<Guid> Selection = [];
    Guid? anchor;
    Drag? drag;
    Flyout? editor;
    DispatcherTimer? pendingClick;

    /// While a block is being dragged or edited, a re-render would pull it from under the pointer or the flyout.
    public bool Busy => drag != null || editor != null;
    public event Action? SelectionChanged;

    sealed class Drag
    {
        public required Entry Entry;
        public required BlockDrag.Mode Mode;
        public required double StartY;
        public bool Moving;
        public Entry Draft;
        public Border? Label;
    }

    double HourHeight => settings.HourHeight;
    DateTimeOffset DayStart => Cal.StartOfDay(day);

    public DayTimeline(Shell shell)
    {
        this.shell = shell;
        canvas.Background = Brushes.Transparent;  // hit-testable empty space
        scroll.Content = canvas;
        Content = scroll;
        canvas.PointerPressed += OnEmptyPressed;
    }

    public void Show(DateTimeOffset day, bool scrollToStart)
    {
        var changedDay = !Cal.SameDay(day, this.day);
        this.day = day;
        if (changedDay) { Selection.Clear(); anchor = null; }
        Render();
        if (scrollToStart || changedDay)
        {
            var hour = entries.Count > 0 ? Cal.Local(entries[0].Start).Hour : 8;
            Dispatcher.UIThread.Post(() => scroll.Offset = new Vector(0, Math.Max(0, hour * HourHeight)), DispatcherPriority.Loaded);
        }
    }

    public void Render()
    {
        if (Busy) return;
        entries = store.EntriesOn(day);
        Selection.IntersectWith(entries.Select(e => e.Id));
        canvas.Children.Clear();
        blocks.Clear();
        canvas.Height = 24 * HourHeight + 2 * Pad;
        // Zooming keeps the hour at the top of the view where it was.
        if (renderedHourHeight > 0 && renderedHourHeight != HourHeight)
        {
            var hour = scroll.Offset.Y / renderedHourHeight;
            Dispatcher.UIThread.Post(() => scroll.Offset = new Vector(0, hour * HourHeight), DispatcherPriority.Loaded);
        }
        renderedHourHeight = HourHeight;

        for (var h = 0; h < 24; h++)
        {
            var y = Pad + h * HourHeight;
            var label = Ui.Text(DayStart.Plus(h * 3600).Format(Loc.Culture.DateTimeFormat.ShortTimePattern.Contains('H') ? "HH" : "h tt"), 10, secondary: true);
            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.VerticalAlignment = VerticalAlignment.Top;
            label.TextAlignment = TextAlignment.Right;
            label.Width = Gutter - 8;
            label.Margin = new Thickness(0, y - 7, 0, 0);
            label.IsHitTestVisible = false;
            canvas.Children.Add(label);
            var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Gutter, y, 0, 0), IsHitTestVisible = false };
            line.Bind(Border.BackgroundProperty, line.GetResourceObservable("HairlineBrush"));
            canvas.Children.Add(line);
        }

        var floors = Floors(entries);
        foreach (var e in entries)
        {
            var b = Block(e, floors[e.Id]);
            blocks[e.Id] = b;
            canvas.Children.Add(b);
        }

        if (entries.Count == 0)
        {
            var hint = Ui.Text(Loc.T("Double-click to add an entry"), 13, secondary: true);
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.VerticalAlignment = VerticalAlignment.Top;
            hint.IsHitTestVisible = false;
            hint.Margin = new Thickness(Gutter, 8.5 * HourHeight, 0, 0);
            canvas.Children.Add(hint);
        }

        // A block added in the Logbook opens its editor once it appears.
        if (store.JustAdded is { } added && blocks.ContainsKey(added))
        {
            store.JustAdded = null;
            Dispatcher.UIThread.Post(() => OpenEditor(added), DispatcherPriority.Loaded);
        }
    }

    /// Where each block's top edge goes when the one above it was stretched to the minimum height past its end.
    Dictionary<Guid, double> Floors(List<Entry> sorted)
    {
        var floors = new Dictionary<Guid, double>();
        double bottom = 0;
        foreach (var e in sorted)
        {
            var start = e.Start.Since(DayStart) / 3600 * HourHeight;
            var top = Math.Max(start, bottom);
            floors[e.Id] = top;
            bottom = top + Math.Max(BlockMinHeight, start + e.Duration / 3600 * HourHeight - top);
        }
        return floors;
    }

    (double Top, double Height) Geometry(Entry e, double floor, bool dragging)
    {
        var start = e.Start.Since(DayStart) / 3600 * HourHeight;
        var natural = e.Duration / 3600 * HourHeight;
        // Starts below a stretched block above it rather than under it, so neither one's text is covered.
        var top = dragging ? start : Math.Max(start, floor);
        return (top, Math.Max(BlockMinHeight, start + natural - top));
    }

    Border Block(Entry e, double floor)
    {
        var (top, height) = Geometry(e, floor, false);
        var selected = Selection.Contains(e.Id);
        var b = new Border
        {
            CornerRadius = new CornerRadius(6), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(Gutter, Pad + top, 12, 0), Height = height, Background = e.Activity.Fill(0.22),
            BorderThickness = new Thickness(selected ? 2 : 0), Tag = e.Id, Cursor = new Cursor(StandardCursorType.Hand),
        };
        if (selected) b.Bind(Border.BorderBrushProperty, b.GetResourceObservable("SelectionBrush"));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("3,*") };
        grid.Children.Add(new Border { Background = e.Activity.Fill() });
        var content = BlockContent(e, height);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        b.Child = grid;

        b.PointerEntered += (_, _) => { if (drag == null) b.Background = e.Activity.Fill(0.3); };
        b.PointerExited += (_, _) => { if (drag == null) b.Background = e.Activity.Fill(0.22); };
        b.PointerPressed += (_, args) => OnBlockPressed(b, e, args);
        b.PointerMoved += (_, args) => OnBlockMoved(b, e, args);
        b.PointerReleased += (_, args) => OnBlockReleased(b, e, args);
        b.PointerCaptureLost += (_, _) => { if (drag?.Entry.Id == e.Id) { drag = null; Render(); } };
        return b;
    }

    Control BlockContent(Entry e, double height)
    {
        var title = Ui.H(4);
        // Touches midnight: the timer ran on from the day before, or into the next.
        if (e.Start == DayStart) title.Children.Add(Ui.Text("⤒", 11, color: Ui.Orange));
        if (e.End == Cal.AddDays(DayStart, 1)) title.Children.Add(Ui.Text("⤓", 11, color: Ui.Orange));
        title.Children.Add(Ui.Icon(e.Activity.Symbol(), 12));
        title.Children.Add(Ui.Text(e.Activity.Label(), 11, FontWeight.SemiBold));
        if (e.Profile is { } p && store.ProfileNames.TryGetValue(p, out var name))
            title.Children.Add(Ui.Text("· " + name, 11, FontWeight.SemiBold));
        var span = Ui.Text($"{e.Start.ShortTime()} – {(e.End is { } end ? end.ShortTime() : Loc.T("running"))}", 11, secondary: true);
        var duration = Ui.Text(e.Duration.Hm(), 11, secondary: true, tabular: true);

        Control body;
        if (height < 40)
        {
            // ponytail: short block — everything on one line, notes joined.
            var line = Ui.H(6, title, span, duration);
            if (e.Notes.Count > 0) line.Children.Add(Ui.Text("· " + string.Join(" · ", e.Notes), 11, secondary: true));
            line.VerticalAlignment = height < 18 ? VerticalAlignment.Top : VerticalAlignment.Center;
            line.Margin = new Thickness(8, height < 18 ? -1 : 0, 8, 0);
            body = line;
        }
        else
        {
            var stack = Ui.V(1, Ui.Spread(title, duration), span);
            foreach (var n in e.Notes) stack.Children.Add(Ui.Text("· " + n, 11, secondary: true));
            stack.Margin = new Thickness(8, 3, 8, 3);
            body = stack;
        }
        body.IsHitTestVisible = false;
        return body;
    }

    // MARK: Pointer

    BlockDrag.Mode ModeAt(Border b, Entry e, Point p)
    {
        var grip = Math.Min(Grip, b.Bounds.Height / 4);
        if (p.Y <= grip) return BlockDrag.Mode.Start;
        if (!e.IsRunning && p.Y >= b.Bounds.Height - grip) return BlockDrag.Mode.End;
        return e.IsRunning ? BlockDrag.Mode.Start : BlockDrag.Mode.Move;
    }

    void OnBlockPressed(Border b, Entry e, PointerPressedEventArgs args)
    {
        args.Handled = true;
        var point = args.GetCurrentPoint(b);
        if (point.Properties.IsRightButtonPressed)
        {
            // Right-clicking a selected block acts on the whole selection.
            var ids = Selection.Contains(e.Id) ? new HashSet<Guid>(Selection) : [e.Id];
            Actions(ids).Open(b);
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        if (args.ClickCount == 2)
        {
            // Double-click cuts a 15-minute break in at that spot.
            pendingClick?.Stop();
            pendingClick = null;
            var y = args.GetPosition(canvas).Y - Pad;
            store.Insert(Activity.Break, DayStart.Plus(Math.Floor(y / HourHeight * 12) * 300), 900);
            return;
        }
        drag = new Drag { Entry = e, Mode = ModeAt(b, e, point.Position), StartY = args.GetPosition(canvas).Y, Draft = e };
        args.Pointer.Capture(b);
    }

    void OnBlockMoved(Border b, Entry e, PointerEventArgs args)
    {
        if (drag == null || drag.Entry.Id != e.Id)
        {
            var mode = ModeAt(b, e, args.GetPosition(b));
            b.Cursor = new Cursor(mode == BlockDrag.Mode.Move || (e.IsRunning && args.GetPosition(b).Y > Grip) ? StandardCursorType.Hand : StandardCursorType.SizeNorthSouth);
            return;
        }
        var y = args.GetPosition(canvas).Y;
        if (!drag.Moving && Math.Abs(y - drag.StartY) < 2) return;
        drag.Moving = true;
        drag.Draft = Moved(drag, y, args.KeyModifiers, drop: false);
        var (top, height) = Geometry(drag.Draft, 0, dragging: true);
        b.Margin = new Thickness(Gutter, Pad + top, 12, 0);
        b.Height = height;
        b.Background = e.Activity.Fill(0.4);
        b.ZIndex = 2;
        ShowDragLabel(b, drag);
    }

    void OnBlockReleased(Border b, Entry e, PointerReleasedEventArgs args)
    {
        if (drag == null || drag.Entry.Id != e.Id) return;
        var d = drag;
        drag = null;
        args.Pointer.Capture(null);
        if (!d.Moving)
        {
            Click(e, args.KeyModifiers);
            return;
        }
        var dropped = Moved(d, args.GetPosition(canvas).Y, args.KeyModifiers, drop: true);
        // ponytail: with Alt a shared border drags the neighbour with it, but only on release — writing the store per
        // pointer move would save the file at 60 Hz. BlockDrag keeps the neighbour at least 5 minutes long.
        var together = args.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var others = entries.Where(x => x.Id != e.Id).ToList();
        if (together && BlockDrag.Neighbour(e, d.Mode, others) is { } n)
        {
            var i = store.Entries.FindIndex(x => x.Id == n.Id);
            if (i >= 0) store.Entries[i] = d.Mode == BlockDrag.Mode.Start ? n with { End = dropped.Start } : n with { Start = dropped.End!.Value };
        }
        var j = store.Entries.FindIndex(x => x.Id == e.Id);
        if (j >= 0) store.Entries[j] = dropped;
        store.Commit();
        Render();
    }

    Entry Moved(Drag d, double y, KeyModifiers modifiers, bool drop)
    {
        var others = entries.Where(x => x.Id != d.Entry.Id).ToList();
        // ponytail: 4 px magnet — under one 5-minute step at the default zoom, so a block can still sit 5 min off an edge.
        return BlockDrag.Drag(d.Entry, d.Mode, (y - d.StartY) / HourHeight * 3600, others, DayStart, 4 / HourHeight * 3600, drop,
                              together: modifiers.HasFlag(KeyModifiers.Alt));
    }

    void ShowDragLabel(Border b, Drag d)
    {
        var time = d.Mode == BlockDrag.Mode.End ? d.Draft.End ?? d.Draft.Start : d.Draft.Start;
        if (d.Label == null)
        {
            d.Label = new Border
            {
                CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 2), Margin = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false,
                VerticalAlignment = d.Mode == BlockDrag.Mode.End ? VerticalAlignment.Bottom : VerticalAlignment.Top,
            };
            d.Label.Bind(Border.BackgroundProperty, d.Label.GetResourceObservable("SectionBrush"));
            if (b.Child is Grid g) { Grid.SetColumnSpan(d.Label, 2); g.Children.Add(d.Label); }
        }
        d.Label.Child = Ui.Text(time.ShortTime(), 11, FontWeight.SemiBold, tabular: true);
    }

    /// A plain click opens the editor (after the double-click time, so a double-click can cut in a break instead);
    /// with Ctrl or Shift it selects.
    void Click(Entry e, KeyModifiers m)
    {
        if (LogbookDialogs.Command(m))
        {
            if (!Selection.Remove(e.Id)) Selection.Add(e.Id);
            anchor = e.Id;
        }
        else if (m.HasFlag(KeyModifiers.Shift))
        {
            var ids = entries.Select(x => x.Id).ToList();  // by start
            var a = anchor is { } x ? ids.IndexOf(x) : -1;
            var b = ids.IndexOf(e.Id);
            if (a < 0) { Selection.Add(e.Id); anchor = e.Id; }
            else Selection.UnionWith(ids.GetRange(Math.Min(a, b), Math.Abs(a - b) + 1));
        }
        else
        {
            Selection.Clear();
            anchor = e.Id;
            pendingClick?.Stop();
            pendingClick = new DispatcherTimer(TimeSpan.FromMilliseconds(260), DispatcherPriority.Input, (_, _) =>
            {
                pendingClick?.Stop();
                pendingClick = null;
                OpenEditor(e.Id);
            });
            pendingClick.Start();
        }
        SelectionChanged?.Invoke();
        Render();
    }

    void OnEmptyPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
        if (args.ClickCount == 2)
        {
            var y = args.GetPosition(canvas).Y - Pad;
            var minutes = Math.Clamp(Math.Floor(y / HourHeight * 4) * 15, 0, 23 * 60);
            store.AddEntry(day, DayStart.Plus(minutes * 60));
            return;
        }
        if (Selection.Count > 0) { Selection.Clear(); SelectionChanged?.Invoke(); Render(); }
    }

    public void SelectAll()
    {
        Selection.UnionWith(entries.Select(e => e.Id));
        SelectionChanged?.Invoke();
        Render();
    }

    public void ClearSelection()
    {
        if (Selection.Count == 0) return;
        Selection.Clear();
        SelectionChanged?.Invoke();
        Render();
    }

    // MARK: Editor and actions

    void OpenEditor(Guid id)
    {
        if (!blocks.TryGetValue(id, out var b) || store.Find(id) is not { } e) return;
        editor?.Hide();
        var flyout = new Flyout { Placement = PlacementMode.RightEdgeAlignedTop, ShowMode = FlyoutShowMode.Standard };
        flyout.Content = new EntryEditor(store, e, () => { flyout.Hide(); store.Delete(id); });
        flyout.Closed += (_, _) =>
        {
            if (editor == flyout) editor = null;
            Render();
        };
        editor = flyout;
        flyout.ShowAt(b);
    }

    ContextMenu Actions(HashSet<Guid> ids)
    {
        var menu = new ContextMenu();
        MenuItem Item(string header, Action click, Control? icon = null)
        {
            var i = new MenuItem { Header = header, Icon = icon };
            i.Click += (_, _) => click();
            return i;
        }
        // Clients only go on billable blocks, so the menu is offered when there's one among them.
        if (store.ActiveProfiles.Count > 0 && store.Entries.Any(e => ids.Contains(e.Id) && e.Activity.Billable()))
        {
            var client = new MenuItem { Header = Loc.T("Client") };
            client.Items.Add(Item(Loc.T("No client"), () => store.Assign(ids, null)));
            foreach (var p in store.ActiveProfiles) client.Items.Add(Item(p.Name, () => store.Assign(ids, p.Id)));
            menu.Items.Add(client);
        }
        var activity = new MenuItem { Header = Loc.T("Activity") };
        foreach (var a in Activities.All) activity.Items.Add(Item(a.Label(), () => store.SetActivity(ids, a), Ui.Icon(a.Symbol(), 14, a.Fill())));
        menu.Items.Add(activity);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.T("Delete"), () => { store.Delete(ids); Selection.Clear(); SelectionChanged?.Invoke(); }));
        return menu;
    }
}
