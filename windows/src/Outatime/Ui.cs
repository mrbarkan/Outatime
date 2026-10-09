using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using FluentIcons.Common;
using SymbolIcon = FluentIcons.Avalonia.SymbolIcon;

namespace Outatime.App;

/// Small builders for the code-only UI, so views read like the SwiftUI they port.
public static class Ui
{
    public static readonly IBrush Green = Brush("#34C759"), Red = Brush("#FF3B30"), Orange = Brush("#FF9500"), Accent = Brush("#0A84FF");

    public static IBrush Brush(string hex) => new SolidColorBrush(Avalonia.Media.Color.Parse(hex));
    public static IBrush Brush(string hex, double opacity) => new SolidColorBrush(Avalonia.Media.Color.Parse(hex), opacity);
    public static IBrush Fill(this Activity a) => Brush(a.Color());
    public static IBrush Fill(this Activity a, double opacity) => Brush(a.Color(), opacity);
    public static IBrush Fill(this ObjectiveKind k) => Brush(k.Color());

    /// The Fluent icons that stand in for the Mac app's SF Symbols.
    public static Symbol Symbol(this Activity a) => a switch
    {
        Activity.Work => FluentIcons.Common.Symbol.LaptopPerson,
        Activity.Break => FluentIcons.Common.Symbol.DrinkCoffee,
        Activity.Lunch => FluentIcons.Common.Symbol.Food,
        Activity.Extra => FluentIcons.Common.Symbol.WeatherMoon,
        Activity.Travel => FluentIcons.Common.Symbol.VehicleCar,
        _ => FluentIcons.Common.Symbol.PersonWalking,
    };

    public static Symbol Symbol(this ObjectiveKind k) => k switch
    {
        ObjectiveKind.DaysOff => FluentIcons.Common.Symbol.Beach,
        ObjectiveKind.Hours => FluentIcons.Common.Symbol.HourglassHalf,
        _ => FluentIcons.Common.Symbol.Savings,
    };

    public static SymbolIcon Icon(Symbol symbol, double size = 16, IBrush? color = null, bool filled = false)
    {
        var icon = new SymbolIcon { Symbol = symbol, FontSize = size, IconVariant = filled ? IconVariant.Filled : IconVariant.Regular,
                                    VerticalAlignment = VerticalAlignment.Center };
        if (color != null) icon.Foreground = color;
        return icon;
    }

    public static TextBlock Text(string text, double size = 13, FontWeight weight = FontWeight.Normal, IBrush? color = null,
                                 bool secondary = false, bool tabular = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight, VerticalAlignment = VerticalAlignment.Center,
                                TextTrimming = TextTrimming.CharacterEllipsis };
        if (color != null) t.Foreground = color;
        if (secondary) t.Opacity = 0.62;
        if (tabular) t.FontFeatures = FontFeatureCollection.Parse("tnum");
        return t;
    }

    public static StackPanel H(double spacing, params Control?[] children) => Stack(Orientation.Horizontal, spacing, children);
    public static StackPanel V(double spacing, params Control?[] children) => Stack(Orientation.Vertical, spacing, children);

    static StackPanel Stack(Orientation o, double spacing, Control?[] children)
    {
        var s = new StackPanel { Orientation = o, Spacing = spacing };
        foreach (var c in children) if (c != null) s.Children.Add(c);
        return s;
    }

    /// A row whose last children sit at the right edge: `left` fills, `right` keep their size.
    public static Grid Spread(Control left, params Control?[] right)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*" + string.Concat(right.Where(r => r != null).Select(_ => ",Auto"))) };
        g.Children.Add(left);
        var i = 1;
        foreach (var r in right)
        {
            if (r == null) continue;
            Grid.SetColumn(r, i++);
            r.Margin = new Thickness(10, 0, 0, 0);
            g.Children.Add(r);
        }
        return g;
    }

    public static Button Button(object content, Action click, string? tip = null, string? classes = null)
    {
        var b = new Button { Content = content, VerticalAlignment = VerticalAlignment.Center };
        b.Click += (_, _) => click();
        if (tip != null) ToolTip.SetTip(b, tip);
        if (classes != null) b.Classes.AddRange(classes.Split(' '));
        return b;
    }

    /// A borderless icon button, like SwiftUI's .borderless.
    public static Button IconButton(Symbol symbol, Action click, string tip, IBrush? color = null, double size = 16)
    {
        var b = Button(Icon(symbol, size, color), click, tip, "plain");
        b.Padding = new Thickness(4);
        return b;
    }

    public static Separator Divider() => new() { Margin = new Thickness(0, 2) };

    /// A rounded group box for forms, like a grouped Form section.
    public static Border Section(params Control?[] rows)
    {
        var panel = V(10, rows);
        return new Border { Child = panel, Padding = new Thickness(14, 12), CornerRadius = new CornerRadius(8), Classes = { "section" } };
    }

    public static Control Labeled(string label, Control control) => Spread(Text(label), control);

    public static T With<T>(this T control, Action<T> configure) where T : Control
    {
        configure(control);
        return control;
    }

    /// The month/day/time formats of the app's language.
    public static System.Globalization.CultureInfo Culture => Loc.Culture;
    public static string Format(this DateTimeOffset d, string format) => Cal.Local(d).ToString(format, Culture);
    public static string ShortTime(this DateTimeOffset d) => Cal.Local(d).ToString("t", Culture);
}
