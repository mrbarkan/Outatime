using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Outatime.App;

/// The Logbook's Stats flyout.
public static class StatsView
{
    public static Control Build(Stats s)
    {
        Control Row(string label, string? value, IBrush? color = null) =>
            Ui.Spread(Ui.Text(label), value == null ? Ui.Text("—", secondary: true) : Ui.Text(value, color: color, tabular: true));
        string? Hours(double? t) => t?.Hm();
        string? Time(int? minutes) => minutes is { } m ? Cal.Setting(Clock.Now, m / 60, m % 60).ShortTime() : null;
        Control Header(string title) => Ui.Text(title, 12, FontWeight.SemiBold, secondary: true).With(t => t.Margin = new Thickness(2, 6, 0, 0));

        var weekLeft = s.WeekLeft > 0 ? Row(Loc.T("Left this week"), Loc.T("%@ of %@", s.WeekLeft.Hm(), s.WeekGoal.Hm()))
                                      : Row(Loc.T("Left this week"), Loc.T("Done"), Ui.Green);
        var longest = s.LongestDay is { } d ? Loc.T("%@, %@", d.Worked.Hm(), d.Date.Format("ddd d")) : null;
        var panel = Ui.V(6,
            Header(Loc.T("This Week")),
            Ui.Section(Row(Loc.T("Worked"), s.WeekWorked.Hm()), weekLeft, Row(Loc.T("To next day off"), s.ToDayOff.Hm()),
                       s.DaysOffBanked > 0 ? Row(Loc.T("Days off banked"), s.DaysOffBanked.ToString()) : null),
            Header(Loc.T("Averages")),
            Ui.Section(Row(Loc.T("Average week"), Hours(s.AverageWeek)), Row(Loc.T("Average month"), Hours(s.AverageMonth)),
                       Row(Loc.T("Average workday"), Hours(s.AverageDay)), Row(Loc.T("Usual start"), Time(s.UsualStart)),
                       Row(Loc.T("Usual finish"), Time(s.UsualFinish))),
            Header(Loc.T("This Month")),
            Ui.Section(Row(Loc.T("Longest day"), longest), Row(Loc.T("Extra"), s.ExtraThisMonth.Hm()),
                       Row(Loc.T("Days worked"), s.DaysWorkedThisMonth.ToString())));
        panel.Width = 300;
        return panel;
    }
}
