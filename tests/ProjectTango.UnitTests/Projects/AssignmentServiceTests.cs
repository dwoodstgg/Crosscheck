using ProjectTango.Application.Projects;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;
using ProjectTango.UnitTests.Fakes;

namespace ProjectTango.UnitTests.Projects;

public class AssignmentServiceTests
{
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeProjectRepository _projects = new();
    private readonly FakeAssignmentRepository _assignments = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly FakeRateCardRepository _rateCards;
    private readonly FakeEmployeeRepository _employees;
    private readonly FakeAuditLog _audit = new();
    private readonly AssignmentService _service;

    private readonly Project _project;
    private readonly Employee _dev;

    public AssignmentServiceTests()
    {
        _employees = new FakeEmployeeRepository(_roles);
        _rateCards = new FakeRateCardRepository(_roles);
        _service = new AssignmentService(_currentUser, _projects, _assignments, _employees, _roles, _rateCards, _audit);

        _project = new Project
        {
            Id = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Name = "P",
            Code = "GEO-001",
            ProjectManagerId = _currentUser.EmployeeId!.Value,
        };
        _projects.Projects.Add(_project);
        _dev = new Employee { Id = Guid.NewGuid(), Email = "dev@x", DisplayName = "Dev" };
        _employees.Employees.Add(_dev);
        _currentUser.Roles.Add(RoleNames.ProjectManager);
    }

    [Fact]
    public async Task Assign_creates_row_and_audits()
    {
        await _service.AssignAsync(_project.Id, _dev.Id, null);

        var assignment = Assert.Single(_assignments.Assignments);
        Assert.Equal(_dev.Id, assignment.EmployeeId);
        Assert.Null(assignment.EndDate);
        Assert.True(assignment.IsActive);
        Assert.Single(_audit.Events, e => e.Action == "assignment.added");
    }

    [Fact]
    public async Task Duplicate_active_assignment_is_rejected()
    {
        await _service.AssignAsync(_project.Id, _dev.Id, null);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.AssignAsync(_project.Id, _dev.Id, null));

        Assert.Contains("already assigned", ex.Message);
    }

    [Fact]
    public async Task Reassigning_after_removal_reopens_the_same_row()
    {
        await _service.AssignAsync(_project.Id, _dev.Id, null);
        var assignment = _assignments.Assignments.Single();
        // Logged time forces a soft-deactivate rather than a hard delete.
        _assignments.WithTimeEntries.Add((_project.Id, _dev.Id));
        await _service.RemoveAsync(assignment.Id);
        Assert.NotNull(assignment.EndDate);

        await _service.AssignAsync(_project.Id, _dev.Id, null);

        Assert.Single(_assignments.Assignments); // still one row
        Assert.Null(assignment.EndDate);
        Assert.Single(_audit.Events, e => e.Action == "assignment.reopened");
    }

    [Fact]
    public async Task Inactive_employee_cannot_be_assigned()
    {
        _dev.IsActive = false;

        await Assert.ThrowsAsync<DomainException>(() =>
            _service.AssignAsync(_project.Id, _dev.Id, null));
    }

    [Fact]
    public async Task Pm_of_other_project_cannot_assign()
    {
        _project.ProjectManagerId = Guid.NewGuid();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.AssignAsync(_project.Id, _dev.Id, null));
    }

    [Fact]
    public async Task Default_billing_role_without_a_rate_card_is_rejected()
    {
        var pmRole = new Role { Id = Guid.NewGuid(), Name = "ProjectManager", DisplayName = "Project Manager", IsBillable = true };
        _roles.Roles.Add(pmRole);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.AssignAsync(_project.Id, _dev.Id, pmRole.Id));

        Assert.Contains("no rate card", ex.Message);
        Assert.Empty(_assignments.Assignments);
    }

    [Fact]
    public async Task Default_billing_role_with_a_rate_card_is_allowed()
    {
        var pmRole = new Role { Id = Guid.NewGuid(), Name = "ProjectManager", DisplayName = "Project Manager", IsBillable = true };
        _roles.Roles.Add(pmRole);
        _rateCards.Rates.Add(new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = _project.Id,
            RoleId = pmRole.Id,
            HourlyRate = 150m,
        });

        await _service.AssignAsync(_project.Id, _dev.Id, pmRole.Id);

        var assignment = Assert.Single(_assignments.Assignments);
        Assert.Equal(pmRole.Id, assignment.DefaultBillingRoleId);
    }

    [Fact]
    public async Task Remove_deletes_assignment_with_no_time_and_audits()
    {
        await _service.AssignAsync(_project.Id, _dev.Id, null);
        var assignment = _assignments.Assignments.Single();

        await _service.RemoveAsync(assignment.Id);

        Assert.Empty(_assignments.Assignments);
        Assert.Contains(assignment.Id, _assignments.Deleted);
        Assert.Single(_audit.Events, e => e.Action == "assignment.removed");
    }

    [Fact]
    public async Task Remove_soft_deactivates_when_time_has_been_logged()
    {
        await _service.AssignAsync(_project.Id, _dev.Id, null);
        var assignment = _assignments.Assignments.Single();
        _assignments.WithTimeEntries.Add((_project.Id, _dev.Id));

        await _service.RemoveAsync(assignment.Id);

        Assert.Single(_assignments.Assignments); // preserved, not deleted
        Assert.DoesNotContain(assignment.Id, _assignments.Deleted);
        Assert.NotNull(assignment.EndDate);
        Assert.False(assignment.IsActive);
        Assert.Single(_audit.Events, e => e.Action == "assignment.ended");
    }

    [Fact]
    public async Task Reactivate_restores_a_removed_assignment()
    {
        await _service.AssignAsync(_project.Id, _dev.Id, null);
        var assignment = _assignments.Assignments.Single();
        _assignments.WithTimeEntries.Add((_project.Id, _dev.Id));
        await _service.RemoveAsync(assignment.Id);

        await _service.ReactivateAsync(assignment.Id);

        Assert.Null(assignment.EndDate);
        Assert.True(assignment.IsActive);
        Assert.Single(_audit.Events, e => e.Action == "assignment.reopened");
    }
}
