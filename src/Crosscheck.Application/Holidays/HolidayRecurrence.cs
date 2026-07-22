namespace Crosscheck.Application.Holidays;

/// <summary>Projects a holiday from one year into another for the "copy from previous
/// year" rollover. Floating federal holidays (recognized by name) are recomputed by their
/// nth-weekday rule — MLK Day is the 3rd Monday of January, not "January 19" — while
/// everything else keeps its month/day.</summary>
public static class HolidayRecurrence
{
    private static readonly (string Keyword, int Month, DayOfWeek Day, int Nth)[] FloatingRules =
    [
        ("luther", 1, DayOfWeek.Monday, 3),
        ("mlk", 1, DayOfWeek.Monday, 3),
        ("washington", 2, DayOfWeek.Monday, 3),
        ("president", 2, DayOfWeek.Monday, 3),
        ("memorial", 5, DayOfWeek.Monday, -1),
        ("labor", 9, DayOfWeek.Monday, 1),
        ("columbus", 10, DayOfWeek.Monday, 2),
        ("indigenous", 10, DayOfWeek.Monday, 2),
        ("thanksgiving", 11, DayOfWeek.Thursday, 4),
    ];

    /// <summary>The date this holiday falls on in <paramref name="year"/>, given its name
    /// and its date in some other year.</summary>
    public static DateOnly ForYear(string name, DateOnly previous, int year)
    {
        foreach (var (keyword, month, day, nth) in FloatingRules)
        {
            if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return NthWeekdayOfMonth(year, month, day, nth);
            }
        }

        var dayOfMonth = Math.Min(previous.Day, DateTime.DaysInMonth(year, previous.Month));
        return new DateOnly(year, previous.Month, dayOfMonth);
    }

    /// <summary>The nth occurrence of a weekday in a month; nth = -1 means the last.</summary>
    private static DateOnly NthWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek, int nth)
    {
        if (nth == -1)
        {
            var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
            var back = ((int)last.DayOfWeek - (int)dayOfWeek + 7) % 7;
            return last.AddDays(-back);
        }

        var first = new DateOnly(year, month, 1);
        var forward = ((int)dayOfWeek - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(forward + 7 * (nth - 1));
    }
}
