namespace Crosscheck.Domain;

/// <summary>The semi-monthly timesheet window a date falls in: the 1st–15th, or the
/// 16th–end-of-month (design-doc §6.1). Windows tile the calendar with no gaps or
/// overlaps, so every date belongs to exactly one.</summary>
public readonly record struct SemiMonthlyPeriod(DateOnly Start, DateOnly End)
{
    public static SemiMonthlyPeriod Containing(DateOnly date)
    {
        if (date.Day <= 15)
        {
            return new SemiMonthlyPeriod(new DateOnly(date.Year, date.Month, 1),
                                         new DateOnly(date.Year, date.Month, 15));
        }

        var lastDay = DateTime.DaysInMonth(date.Year, date.Month);
        return new SemiMonthlyPeriod(new DateOnly(date.Year, date.Month, 16),
                                     new DateOnly(date.Year, date.Month, lastDay));
    }

    public bool Contains(DateOnly date) => date >= Start && date <= End;
}
