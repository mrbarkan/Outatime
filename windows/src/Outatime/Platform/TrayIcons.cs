using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Outatime.App.Platform;

/// The tray icon, drawn: a clock face filled with the running activity's color (the Mac's colored menu bar icon), a
/// plain clock while idle or with color turned off, and a red ring while the tomato timer runs.
public static class TrayIcons
{
    static readonly Dictionary<string, WindowIcon> Cache = [];

    public static WindowIcon For(Activity? activity, bool colored, bool tomato)
    {
        var key = $"{activity}|{colored}|{tomato}|{LightTaskbar}";
        if (Cache.TryGetValue(key, out var icon)) return icon;
        using var bitmap = Draw(activity, colored, tomato);
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return Cache[key] = new WindowIcon(stream);
    }

    /// Windows' taskbar can be light; the plain clock is drawn dark there so it doesn't vanish.
    static bool LightTaskbar
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
            }
            catch (Exception) { return false; }
        }
    }

    public static RenderTargetBitmap Draw(Activity? activity, bool colored, bool tomato, int size = 32)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using var g = bitmap.CreateDrawingContext();
        var ink = Color.Parse(LightTaskbar ? "#1B1B1B" : "#FFFFFF");
        var c = new Point(size / 2.0, size / 2.0);
        var r = size / 2.0 - 2.5;
        var fill = activity is { } a && colored ? Color.Parse(a.Color()) : (Color?)null;
        if (fill is { } f)
            g.DrawEllipse(new SolidColorBrush(f), null, c, r + 1.5, r + 1.5);
        else
            g.DrawEllipse(null, new Pen(new SolidColorBrush(ink), 2.6), c, r, r);
        var hands = new Pen(new SolidColorBrush(fill != null ? Colors.White : ink), 2.6, lineCap: PenLineCap.Round);
        g.DrawLine(hands, c, new Point(c.X, c.Y - r * 0.62));
        g.DrawLine(hands, c, new Point(c.X + r * 0.45, c.Y + r * 0.2));
        if (activity != null && fill == null)
            g.DrawEllipse(new SolidColorBrush(ink), null, new Point(size - 5, size - 5), 4, 4);  // tracking, without color
        if (tomato)
            g.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#FF3B30")), 2.4), c, r + 1.2, r + 1.2);
        return bitmap;
    }
}
