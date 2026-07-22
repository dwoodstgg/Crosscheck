using Crosscheck.Application.Holidays;
using Crosscheck.Domain.Entities;

namespace Crosscheck.UnitTests.Holidays;

public class WorkCalendarTests
{
    private static readonly IReadOnlySet<DateOnly> NoHolidays = new HashSet<DateOnly>();

    [Fact]
    public void July_2026_has_23_weekday_workdays_with_no_holidays()
    {
        // July 2026: 31 days, starts on a Wednesday — 23 weekdays.
        var count = WorkCalendar.CountWorkdays(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), NoHolidays);

        Assert.Equal(23, count);
        Assert.Equal(184m, WorkCalendar.ExpectedHours(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), NoHolidays));
    }

    [Fact]
    public void A_weekday_holiday_reduces_the_workday_count()
    {
        var holidays = new HashSet<DateOnly> { new(2026, 7, 3) }; // Friday (observed July 4th)

        var count = WorkCalendar.CountWorkdays(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), holidays);

        Assert.Equal(22, count);
        Assert.Equal(176m, WorkCalendar.ExpectedHours(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), holidays));
    }

    [Fact]
    public void A_weekend_holiday_does_not_double_subtract()
    {
        var holidays = new HashSet<DateOnly> { new(2026, 7, 4) }; // Saturday

        var count = WorkCalendar.CountWorkdays(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), holidays);

        Assert.Equal(23, count);
    }

    [Fact]
    public void A_single_week_counts_only_its_own_workdays()
    {
        // Sun Jul 5 – Sat Jul 11, 2026 with a Monday holiday.
        var holidays = new HashSet<DateOnly> { new(2026, 7, 6) };

        var count = WorkCalendar.CountWorkdays(new DateOnly(2026, 7, 5), new DateOnly(2026, 7, 11), holidays);

        Assert.Equal(4, count);
        Assert.Equal(32m, WorkCalendar.ExpectedHours(new DateOnly(2026, 7, 5), new DateOnly(2026, 7, 11), holidays));
    }

    [Theory]
    [InlineData(2026, 7, 6, true)]   // Monday
    [InlineData(2026, 7, 4, false)]  // Saturday
    [InlineData(2026, 7, 5, false)]  // Sunday
    public void Weekdays_are_workdays_and_weekends_are_not(int year, int month, int day, bool expected)
    {
        Assert.Equal(expected, WorkCalendar.IsWorkday(new DateOnly(year, month, day), NoHolidays));
    }

    [Fact]
    public void A_holiday_weekday_is_not_a_workday()
    {
        var holidays = new HashSet<DateOnly> { new(2026, 12, 25) }; // Friday

        Assert.False(WorkCalendar.IsWorkday(new DateOnly(2026, 12, 25), holidays));
    }

    [Theory]
    [InlineData(2026, 7, 4, 2026, 7, 3)]   // Saturday → preceding Friday
    [InlineData(2026, 7, 5, 2026, 7, 6)]   // Sunday → following Monday
    [InlineData(2026, 12, 25, 2026, 12, 25)] // Friday → itself
    [InlineData(2026, 6, 19, 2026, 6, 19)]   // Friday → itself
    public void Observed_date_follows_the_federal_rule(int y, int m, int d, int oy, int om, int od)
    {
        Assert.Equal(new DateOnly(oy, om, od), WorkCalendar.ObservedDate(new DateOnly(y, m, d)));
    }

    [Fact]
    public void Observed_holidays_shift_weekend_dates_and_suffix_the_name()
    {
        var observed = WorkCalendar.ObservedHolidays(
            [Holiday(2026, 7, 4, "Independence Day"), Holiday(2026, 9, 7, "Labor Day")],
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        Assert.Equal("Independence Day (observed)", observed[new DateOnly(2026, 7, 3)]);
        Assert.Equal("Labor Day", observed[new DateOnly(2026, 9, 7)]);
        Assert.False(observed.ContainsKey(new DateOnly(2026, 7, 4)));
    }

    [Fact]
    public void Observed_holidays_keep_only_observed_dates_inside_the_range()
    {
        // Sat Jan 1 2028 is observed Fri Dec 31 2027 — inside a December 2027 range even
        // though the actual date is outside it (the ±1-widened-query scenario).
        var december = WorkCalendar.ObservedHolidays(
            [Holiday(2028, 1, 1, "New Years")],
            new DateOnly(2027, 12, 1), new DateOnly(2027, 12, 31));

        Assert.Equal("New Years (observed)", december[new DateOnly(2027, 12, 31)]);

        // Sun Oct 31 2027 is observed Mon Nov 1 — outside an October range, so excluded.
        var october = WorkCalendar.ObservedHolidays(
            [Holiday(2027, 10, 31, "Halloween")],
            new DateOnly(2027, 10, 1), new DateOnly(2027, 10, 31));

        Assert.Empty(october);
    }

    [Fact]
    public void Two_holidays_observing_the_same_day_join_names()
    {
        // Fri Dec 25 2026 is Christmas; Sat Dec 26 shifts onto the same Friday.
        var observed = WorkCalendar.ObservedHolidays(
            [Holiday(2026, 12, 25, "Christmas"), Holiday(2026, 12, 26, "Boxing Day")],
            new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31));

        Assert.Equal("Christmas / Boxing Day (observed)", observed[new DateOnly(2026, 12, 25)]);
    }

    [Fact]
    public void A_weekend_holiday_reduces_workdays_via_its_observed_date()
    {
        // Sat Jul 4 2026 → observed Fri Jul 3, so July drops from 23 to 22 workdays.
        var observed = WorkCalendar.ObservedHolidays(
            [Holiday(2026, 7, 4, "Independence Day")],
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        var count = WorkCalendar.CountWorkdays(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), observed.Keys.ToHashSet());

        Assert.Equal(22, count);
    }

    private static CompanyHoliday Holiday(int year, int month, int day, string name) =>
        new() { Id = Guid.NewGuid(), Date = new DateOnly(year, month, day), Name = name };
}
