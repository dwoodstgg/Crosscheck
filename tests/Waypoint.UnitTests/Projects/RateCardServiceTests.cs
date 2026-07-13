using Waypoint.Application.Projects;
using Waypoint.Domain;
using Waypoint.Domain.Entities;
using Waypoint.UnitTests.Fakes;

namespace Waypoint.UnitTests.Projects;

public class RateCardServiceTests
{
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeProjectRepository _projects = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly FakeRateCardRepository _rateCards;
    private readonly FakeAuditLog _audit = new();
    private readonly RateCardService _service;

    private readonly Role _developerRole = new() { Id = Guid.NewGuid(), Name = RoleNames.Developer };
    private readonly Role _adminRole = new() { Id = Guid.NewGuid(), Name = RoleNames.Admin, IsBillable = false, IsSystemAdmin = true };
    private readonly Project _project;

    public RateCardServiceTests()
    {
        _rateCards = new FakeRateCardRepository(_roles);
        _service = new RateCardService(_currentUser, _projects, _rateCards, _roles, _audit);

        _roles.Roles.AddRange([_developerRole, _adminRole]);
        _project = new Project
        {
            Id = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Name = "P",
            Code = "GEO-001",
            ProjectManagerId = _currentUser.EmployeeId!.Value,
        };
        _projects.Projects.Add(_project);
        _currentUser.Roles.Add(RoleNames.ProjectManager);
    }

    [Fact]
    public async Task Set_rate_adds_single_row()
    {
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 150m);

        var rate = Assert.Single(_rateCards.Rates);
        Assert.Equal(150m, rate.HourlyRate);
        Assert.Single(_audit.Events, e => e.Action == "rate.set");
    }

    [Fact]
    public async Task Second_rate_for_same_role_is_rejected()
    {
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 150m);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.SetRateAsync(_project.Id, _developerRole.Id, 165m));

        Assert.Contains("already has a rate", ex.Message);
        Assert.Single(_rateCards.Rates);
    }

    [Fact]
    public async Task Resolve_returns_rate_for_project_and_role()
    {
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 150m);

        Assert.Equal(150m, await _rateCards.ResolveAsync(_project.Id, _developerRole.Id));
        Assert.Null(await _rateCards.ResolveAsync(_project.Id, _adminRole.Id));
    }

    [Fact]
    public async Task Non_billable_role_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.SetRateAsync(_project.Id, _adminRole.Id, 150m));

        Assert.Contains("not a billable role", ex.Message);
    }

    [Fact]
    public async Task Pm_of_other_project_cannot_set_rates()
    {
        _project.ProjectManagerId = Guid.NewGuid();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.SetRateAsync(_project.Id, _developerRole.Id, 150m));
    }

    [Fact]
    public async Task Correct_fixes_amount_in_place()
    {
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 135m);
        var rate = Assert.Single(_rateCards.Rates);

        await _service.CorrectRateAsync(_project.Id, rate.Id, 150m);

        Assert.Equal(150m, Assert.Single(_rateCards.Rates).HourlyRate);
        Assert.Single(_audit.Events, e => e.Action == "rate.correct");
    }

    [Fact]
    public async Task Correct_blocked_once_time_is_invoiced()
    {
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 135m);
        var rate = Assert.Single(_rateCards.Rates);
        _rateCards.InvoicedTime.Add((_project.Id, _developerRole.Id));

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.CorrectRateAsync(_project.Id, rate.Id, 150m));
        Assert.Contains("invoiced", ex.Message);
    }

    [Fact]
    public async Task Delete_removes_row()
    {
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 150m);
        var rate = Assert.Single(_rateCards.Rates);

        await _service.DeleteRateAsync(_project.Id, rate.Id);

        Assert.DoesNotContain(_rateCards.Rates, r => r.Id == rate.Id);
        Assert.Single(_audit.Events, e => e.Action == "rate.delete");
    }

    [Fact]
    public async Task Delete_blocked_once_time_is_invoiced()
    {
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 135m);
        var rate = Assert.Single(_rateCards.Rates);
        _rateCards.InvoicedTime.Add((_project.Id, _developerRole.Id));

        await Assert.ThrowsAsync<DomainException>(() =>
            _service.DeleteRateAsync(_project.Id, rate.Id));
        Assert.DoesNotContain(_rateCards.Deleted, id => id == rate.Id);
    }

    [Fact]
    public async Task Delete_blocked_when_time_is_logged_but_not_invoiced()
    {
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 135m);
        var rate = Assert.Single(_rateCards.Rates);
        _rateCards.LoggedTime.Add((_project.Id, _developerRole.Id));

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.DeleteRateAsync(_project.Id, rate.Id));

        Assert.Contains("logged", ex.Message);
        Assert.DoesNotContain(_rateCards.Deleted, id => id == rate.Id);
    }

    [Fact]
    public async Task Pm_of_other_project_cannot_correct_rates()
    {
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 135m);
        var rate = Assert.Single(_rateCards.Rates);
        _project.ProjectManagerId = Guid.NewGuid();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.CorrectRateAsync(_project.Id, rate.Id, 150m));
    }
}
