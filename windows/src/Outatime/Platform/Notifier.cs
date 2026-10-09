namespace Outatime.App.Platform;

/// Local notifications. `action`, when given, puts a button on it that starts that activity (Start Break at the end of
/// a focus round, Back to Work after a break); pressing it raises Action.
public interface INotifier
{
    void Post(string id, string title, string body, Activity? action = null, string? actionLabel = null);
    void Cancel(string id);
    event Action<Activity>? Action;
}

public static class Notifier
{
    public static INotifier Create()
    {
#if WINDOWS
        try { return new ToastNotifier(); } catch (Exception) { }
#endif
        return new LogNotifier();
    }
}

/// Where Windows toasts aren't available (the Mac that develops the app, tests): log, and on macOS show a banner.
public sealed class LogNotifier : INotifier
{
    public readonly List<(string Id, string Title, string Body)> Sent = [];
    public event Action<Activity>? Action;

    public void Post(string id, string title, string body, Activity? action = null, string? actionLabel = null)
    {
        Sent.Add((id, title, body));
        Console.WriteLine($"[notify] {id}: {title} — {body}");
        if (OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable("OUTATIME_HOME") == null)
        {
            try
            {
                var script = $"display notification {Quote(body)} with title {Quote(title)}";
                System.Diagnostics.Process.Start("osascript", ["-e", script]);
            }
            catch (Exception) { }
        }
    }

    static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    public void Cancel(string id) { }

    /// Tests press a notification's button through this.
    public void Press(Activity a) => Action?.Invoke(a);
}

#if WINDOWS
/// Windows toast notifications. Packaged, the MSIX manifest registers the activator the buttons call back into.
public sealed class ToastNotifier : INotifier
{
    const string Group = "outatime";
    public event Action<Activity>? Action;

    public ToastNotifier()
    {
        Microsoft.Toolkit.Uwp.Notifications.ToastNotificationManagerCompat.OnActivated += e =>
        {
            var args = Microsoft.Toolkit.Uwp.Notifications.ToastArguments.Parse(e.Argument);
            if (args.TryGetValue("activity", out var raw) && Activities.FromRaw(raw) is { } a) Action?.Invoke(a);
        };
    }

    public void Post(string id, string title, string body, Activity? action = null, string? actionLabel = null)
    {
        try
        {
            var toast = new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder().AddText(title).AddText(body);
            if (action is { } a)
                toast.AddButton(new Microsoft.Toolkit.Uwp.Notifications.ToastButton().SetContent(actionLabel ?? a.Label()).AddArgument("activity", a.Raw()));
            toast.Show(t => { t.Tag = id; t.Group = Group; });
        }
        catch (Exception) { }
    }

    public void Cancel(string id)
    {
        try { Microsoft.Toolkit.Uwp.Notifications.ToastNotificationManagerCompat.History.Remove(id, Group); } catch (Exception) { }
    }
}
#endif
