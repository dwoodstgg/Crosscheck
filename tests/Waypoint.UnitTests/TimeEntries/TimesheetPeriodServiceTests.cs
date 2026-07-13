using Waypoint.Application.TimeEntries;
using Waypoint.Domain;
using Waypoint.Domain.Enums;
using Waypoint.UnitTests.Fakes;

namespace Waypoint.UnitTests.TimeEntries;

public class TimesheetPeriodServiceTests
{
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeTimesheetPeriodRepository _periods = new();
    private readonly FakeAuditLog _audit = new();
    private readonly TimesheetPeriodService _service;

    private static readonly DateOnly Day = new(2026, 7, 8); // window: Jul 1–15

    public TimesheetPeriodServiceTests()
    {
        _service = new TimesheetPeriodService(_currentUser, _periods, _audit);
        _currentUser.Roles.Add(RoleNames.OperationsManager);
    }

    [Fact]
    public async Task Close_snaps_to_the_window_and_records_who_closed_it()
    {
        await _service.CloseAsync(Day);

        var period = Assert.Single(_periods.Periods);
        Assert.Equal(new DateOnly(2026, 7, 1), period.PeriodStart);
        Assert.Equal(new DateOnly(2026, 7, 15), period.PeriodEnd);
        Assert.Equal(TimesheetPeriodStatus.Closed, period.Status);
        Assert.Equal(_currentUser.EmployeeId, period.ClosedById);
        Assert.NotNull(period.ClosedAt);
        Assert.Single(_audit.Events, e => e.Action == "timesheet_period.closed");
    }

    [Fact]
    public async Task Closing_an_already_closed_window_is_rejected()
    {
        await _service.CloseAsync(Day);

        await Assert.ThrowsAsync<DomainException>(() => _service.CloseAsync(Day));
    }

    [Fact]
    public async Task Reopen_clears_the_closed_metadata_and_audits()
    {
        await _service.CloseAsync(Day);

        await _service.ReopenAsync(Day);

        var period = Assert.Single(_periods.Periods);
        Assert.Equal(TimesheetPeriodStatus.Open, period.Status);
        Assert.Null(period.ClosedById);
        Assert.Null(period.ClosedAt);
        Assert.Single(_audit.Events, e => e.Action == "timesheet_period.reopened");
    }

    [Fact]
    public async Task A_developer_cannot_close_a_window()
    {
        _currentUser.Roles.Clear();
        _currentUser.Roles.Add(RoleNames.Developer);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.CloseAsync(Day));
    }

    [Fact]
    public async Task IsClosed_reflects_the_windows_state()
    {
        Assert.False(await _service.IsClosedAsync(Day));

        await _service.CloseAsync(Day);

        Assert.True(await _service.IsClosedAsync(Day));
    }

    [Fact]
    public async Task The_16th_falls_in_the_second_half_window()
    {
        var window = SemiMonthlyPeriod.Containing(new DateOnly(2026, 2, 16));

        Assert.Equal(new DateOnly(2026, 2, 16), window.Start);
        Assert.Equal(new DateOnly(2026, 2, 28), window.End); // 2026 is not a leap year
    }
}
