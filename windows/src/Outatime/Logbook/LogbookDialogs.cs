using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Outatime.App;

/// Small modal prompts the Logbook needs: a name to save a template under, and "replace this day?".
public static class LogbookDialogs
{
    static Window Sheet(string title, Control body)
    {
        var w = new Window
        {
            Title = title, SizeToContent = SizeToContent.WidthAndHeight, CanResize = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new Border { Padding = new Thickness(20), Child = body },
            MinWidth = 320,
        };
        return w;
    }

    static StackPanel Buttons(params Button[] buttons)
    {
        var row = Ui.H(8, buttons);
        row.HorizontalAlignment = HorizontalAlignment.Right;
        row.Margin = new Thickness(0, 6, 0, 0);
        return row;
    }

    /// The text typed, or null when cancelled.
    public static async Task<string?> AskName(Window owner, string title, string label)
    {
        var box = new TextBox { PlaceholderText = label, MinWidth = 280 };
        Window? w = null;
        var save = Ui.Button(Loc.T("Save"), () => w!.Close(box.Text?.Trim()), classes: "accent");
        save.IsDefault = true;
        save.IsEnabled = false;
        box.TextChanged += (_, _) => save.IsEnabled = !string.IsNullOrWhiteSpace(box.Text);
        var cancel = Ui.Button(Loc.T("Cancel"), () => w!.Close(null));
        cancel.IsCancel = true;
        w = Sheet(title, Ui.V(12, Ui.Text(title, 15, Avalonia.Media.FontWeight.SemiBold), box, Buttons(cancel, save)));
        w.Opened += (_, _) => box.Focus();
        return await w.ShowDialog<string?>(owner);
    }

    public static async Task<bool> Confirm(Window owner, string message, string action)
    {
        Window? w = null;
        var go = Ui.Button(action, () => w!.Close(true), classes: "accent");
        go.IsDefault = true;
        var cancel = Ui.Button(Loc.T("Cancel"), () => w!.Close(false));
        cancel.IsCancel = true;
        var text = Ui.Text(message, 14);
        text.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        text.MaxWidth = 340;
        w = Sheet(action, Ui.V(12, text, Buttons(cancel, go)));
        return await w.ShowDialog<bool>(owner);
    }

    /// Ctrl on Windows, ⌘ on the Mac that develops the app.
    public static bool Command(KeyModifiers m) => m.HasFlag(KeyModifiers.Control) || m.HasFlag(KeyModifiers.Meta);
}
