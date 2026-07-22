using Crosscheck.Application.TimeEntries;
using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;
using Crosscheck.UnitTests.Fakes;

namespace Crosscheck.UnitTests.TimeEntries;

public class TimesheetServiceTests
{
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeAssignmentRepository _assignments = new();
    private readonly FakeTimeEntryRepository _entries = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly FakeProjectRepository _projects = new();
    private readonly TimesheetService _service;

    private static readonly DateOnly From = new(2026, 7, 1);
    private static readonly DateOnly To = new(2026, 7, 31);

    public TimesheetServiceTests()
    {
        _service = new TimesheetService(
            _currentUser, _assignments, _entries, new FakeModuleRepository(_roles), _projects, _roles);
    }

    private Guid AssignMe()
    {
        var projectId = Guid.NewGuid();
        _assignments.Assignments.Add(new ProjectAssignment
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            EmployeeId = _currentUser.EmployeeId!.Value,
        });
        return projectId;
    }

    [Fact]
    public async Task A_project_with_open_ended_dates_always_shows()
    {
        AssignMe();

        var sheet = await _service.GetMyRangeAsync(From, To);

        Assert.Single(sheet.Rows);
    }

    [Fact]
    public async Task A_project_whose_dates_overlap_the_window_shows()
    {
        var projectId = AssignMe();
        _assignments.ProjectDates[projectId] = (new DateOnly(2026, 7, 20), new DateOnly(2026, 9, 30));

        var sheet = await _service.GetMyRangeAsync(From, To);

        Assert.Single(sheet.Rows);
    }

    [Fact]
    public async Task A_project_that_starts_after_the_window_is_hidden()
    {
        var projectId = AssignMe();
        _assignments.ProjectDates[projectId] = (new DateOnly(2026, 8, 1), null);

        var sheet = await _service.GetMyRangeAsync(From, To);

        Assert.Empty(sheet.Rows);
    }

    [Fact]
    public async Task A_project_that_ended_before_the_window_is_hidden()
    {
        var projectId = AssignMe();
        _assignments.ProjectDates[projectId] = (new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));

        var sheet = await _service.GetMyRangeAsync(From, To);

        Assert.Empty(sheet.Rows);
    }

    [Fact]
    public async Task A_project_outside_its_dates_still_shows_when_the_window_has_entries()
    {
        var projectId = AssignMe();
        _assignments.ProjectDates[projectId] = (new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        _entries.Entries.Add(new TimeEntry
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            EmployeeId = _currentUser.EmployeeId!.Value,
            BillingRoleId = Guid.NewGuid(),
            EntryDate = new DateOnly(2026, 7, 2),
            HoursWorked = 4m,
            HoursBilled = 4m,
        });

        var sheet = await _service.GetMyRangeAsync(From, To);

        Assert.Single(sheet.Rows);
    }

    [Fact]
    public async Task A_closed_project_is_hidden()
    {
        var projectId = AssignMe();
        _assignments.ProjectStatuses[projectId] = ProjectStatus.Closed;

        var sheet = await _service.GetMyRangeAsync(From, To);

        Assert.Empty(sheet.Rows);
    }

    [Fact]
    public async Task An_archived_project_is_hidden()
    {
        var projectId = AssignMe();
        _assignments.ProjectStatuses[projectId] = ProjectStatus.Archived;

        var sheet = await _service.GetMyRangeAsync(From, To);

        Assert.Empty(sheet.Rows);
    }

    [Fact]
    public async Task A_closed_project_still_shows_when_the_window_has_entries()
    {
        var projectId = AssignMe();
        _assignments.ProjectStatuses[projectId] = ProjectStatus.Closed;
        _entries.Entries.Add(new TimeEntry
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            EmployeeId = _currentUser.EmployeeId!.Value,
            BillingRoleId = Guid.NewGuid(),
            EntryDate = new DateOnly(2026, 7, 10),
            HoursWorked = 2m,
            HoursBilled = 2m,
        });

        var sheet = await _service.GetMyRangeAsync(From, To);

        Assert.Single(sheet.Rows);
    }

    [Fact]
    public async Task An_assignment_removed_before_the_window_hides_the_project()
    {
        var projectId = AssignMe();
        _assignments.Assignments.Single(a => a.ProjectId == projectId).EndDate = new DateOnly(2026, 6, 15);

        var sheet = await _service.GetMyRangeAsync(From, To);

        Assert.Empty(sheet.Rows);
    }
}
