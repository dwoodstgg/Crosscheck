using Crosscheck.Domain.Entities;

namespace Crosscheck.Application.Holidays;

/// <summary>Workday arithmetic for timesheet expectations. A workday is Monday–Friday and
/// not a company holiday; the expected total for a range is 8 hours per workday. Weekly
/// hours are allowed to vary (32 one week, 48 the next) — only the range total matters.</summary>
public static class WorkCalendar
{
    public const decimal HoursPerWorkday = 8m;

    public static bool IsWorkday(DateOnly date, IReadOnlySet<DateOnly> holidays) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !holidays.Contains(date);

    /// <summary>The weekday a holiday is taken on, per the federal observance rule:
    /// Saturday holidays are observed the preceding Friday, Sunday holidays the
    /// following Monday, weekday holidays on the day itself.</summary>
    public static DateOnly ObservedDate(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Saturday => date.AddDays(-1),
        DayOfWeek.Sunday => date.AddDays(1),
        _ => date,
    };

    /// <summary>Maps holidays to their observed dates, keeping only observed dates inside
    /// [from, to]. A shifted holiday's name gets an "(observed)" suffix; two holidays
    /// observing the same day have their names joined. The rule shifts a date by at most
    /// one day, so callers should query holidays with the range widened by ±1 day to catch
    /// a weekend holiday just outside the range whose observed day falls inside it.</summary>
    public static Dictionary<DateOnly, string> ObservedHolidays(IEnumerable<CompanyHoliday> holidays, DateOnly from, DateOnly to) =>
        holidays
            .Select(h => (Date: ObservedDate(h.Date), Name: h.Date == ObservedDate(h.Date) ? h.Name : $"{h.Name} (observed)"))
            .Where(h => h.Date >= from && h.Date <= to)
            .GroupBy(h => h.Date)
            .ToDictionary(g => g.Key, g => string.Join(" / ", g.Select(h => h.Name)));

    /// <summary>Mon–Fri days in [from, to] that are not holidays. Callers pass observed
    /// holiday dates (see <see cref="ObservedHolidays"/>), which are always weekdays; a
    /// raw weekend date in the set is already a non-workday, so it never double-subtracts.</summary>
    public static int CountWorkdays(DateOnly from, DateOnly to, IReadOnlySet<DateOnly> holidays)
    {
        var count = 0;
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (IsWorkday(date, holidays))
            {
                count++;
            }
        }

        return count;
    }

    public static decimal ExpectedHours(DateOnly from, DateOnly to, IReadOnlySet<DateOnly> holidays) =>
        CountWorkdays(from, to, holidays) * HoursPerWorkday;
}
