using Crosscheck.Application.Holidays;

namespace Crosscheck.UnitTests.Holidays;

public class HolidayRecurrenceTests
{
    [Theory]
    // Floating federal holidays recompute by their nth-weekday rule.
    [InlineData("Martin Luther King Day", 2026, 1, 19, 2027, 1, 18)]  // 3rd Mon of Jan
    [InlineData("George Washington's Birthday", 2026, 2, 16, 2027, 2, 15)] // 3rd Mon of Feb
    [InlineData("Memorial Day", 2026, 5, 25, 2027, 5, 31)]            // last Mon of May
    [InlineData("Labor Day", 2026, 9, 7, 2027, 9, 6)]                 // 1st Mon of Sep
    [InlineData("Thanksgiving", 2026, 11, 26, 2027, 11, 25)]          // 4th Thu of Nov
    // Fixed-date holidays keep their month/day.
    [InlineData("New Years", 2026, 1, 1, 2027, 1, 1)]
    [InlineData("Juneteenth", 2026, 6, 19, 2027, 6, 19)]
    [InlineData("Independence Day", 2026, 7, 4, 2027, 7, 4)]
    [InlineData("Veterans Day", 2026, 11, 11, 2027, 11, 11)]
    [InlineData("Christmas", 2026, 12, 25, 2027, 12, 25)]
    public void The_2026_company_calendar_rolls_into_2027(string name, int y, int m, int d, int ey, int em, int ed)
    {
        Assert.Equal(new DateOnly(ey, em, ed), HolidayRecurrence.ForYear(name, new DateOnly(y, m, d), ey));
    }

    [Theory]
    [InlineData("MLK DAY")]
    [InlineData("martin luther king jr. day")]
    public void Name_matching_is_case_insensitive(string name)
    {
        Assert.Equal(new DateOnly(2027, 1, 18), HolidayRecurrence.ForYear(name, new DateOnly(2026, 1, 19), 2027));
    }

    [Fact]
    public void An_unrecognized_name_copies_month_and_day()
    {
        Assert.Equal(new DateOnly(2027, 8, 14), HolidayRecurrence.ForYear("Company Picnic", new DateOnly(2026, 8, 14), 2027));
    }

    [Fact]
    public void Feb_29_falls_back_to_Feb_28_in_a_non_leap_year()
    {
        Assert.Equal(new DateOnly(2029, 2, 28), HolidayRecurrence.ForYear("Leap Day", new DateOnly(2028, 2, 29), 2029));
    }
}
