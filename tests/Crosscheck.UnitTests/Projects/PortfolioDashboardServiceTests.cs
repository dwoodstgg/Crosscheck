using Crosscheck.Application.Projects;
using Crosscheck.Domain;
using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;
using Crosscheck.UnitTests.Fakes;

namespace Crosscheck.UnitTests.Projects;

public class PortfolioDashboardServiceTests
{
    private static readonly DateOnly Today = new(2026, 7, 22);

    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeProjectRepository _projects = new();
    private readonly FakeTimeEntryRepository _entries = new();
    private readonly FakeBudgetRepository _budgets = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly FakeModuleRepository _modules;
    private readonly PortfolioDashboardService _service;

    private readonly Guid _devRole = Guid.NewGuid();
    private readonly Guid _alice = Guid.NewGuid();

    public PortfolioDashboardServiceTests()
    {
        _modules = new FakeModuleRepository(_roles);
        _service = new PortfolioDashboardService(_currentUser, _projects, _entries, _budgets, _modules);

        _currentUser.Roles.Add(RoleNames.OperationsManager);
        _entries.RatesByRole[_devRole] = 100m;
        _entries.EmployeeNames[_alice] = "Alice";
        _entries.RoleNames[_devRole] = "Developer";
    }

    private Project AddProject(string name, ProjectStatus status = ProjectStatus.Active, DateOnly? endDate = null)
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Name = name,
            Code = $"GEO-{_projects.Projects.Count + 1:000}",
            Status = status,
            ProjectManagerId = Guid.NewGuid(),
            EndDate = endDate,
        };
        _projects.Projects.Add(project);
        return project;
    }

    private void AddEntry(Guid projectId, TimeEntryStatus status, decimal hours)
    {
        _entries.Entries.Add(new TimeEntry
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            EmployeeId = _alice,
            BillingRoleId = _devRole,
            EntryDate = new DateOnly(2026, 7, 8),
            HoursWorked = hours,
            HoursBilled = hours,
            IsBillable = true,
            Status = status,
        });
    }

    private void AddBudget(Guid projectId, decimal amount, params BudgetRoleAllocation[] allocations)
    {
        _budgets.Budgets.Add(new Budget
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Type = ProjectType.Hourly,
            Amount = amount,
            AlertThresholds = [50, 75, 90],
            RoleAllocations = allocations.ToList(),
        });
    }

    [Fact]
    public async Task Totals_sum_across_all_projects()
    {
        var a = AddProject("Alpha");
        var b = AddProject("Beta");
        AddEntry(a.Id, TimeEntryStatus.Approved, 4m);   // 400 WIP
        AddEntry(a.Id, TimeEntryStatus.Open, 2m);       // 200 pending
        AddEntry(b.Id, TimeEntryStatus.Invoiced, 3m);   // 300 invoiced

        var dash = await _service.GetAsync(Today);

        Assert.Equal(2, dash.ActiveCount);
        Assert.Equal(400m, dash.TotalWip);
        Assert.Equal(200m, dash.TotalPending);
        Assert.Equal(300m, dash.TotalInvoiced);
        Assert.Equal(2, dash.Projects.Count);
    }

    [Fact]
    public async Task At_risk_flags_overrun_and_near_burn_but_not_healthy_or_closed()
    {
        var over = AddProject("Overrun");
        AddBudget(over.Id, amount: 500m);
        AddEntry(over.Id, TimeEntryStatus.Approved, 8m);    // 800 of 500 → over

        var near = AddProject("Near");
        AddBudget(near.Id, amount: 1000m);
        AddEntry(near.Id, TimeEntryStatus.Approved, 8.5m);  // 850 of 1000 → 85%

        var healthy = AddProject("Healthy");
        AddBudget(healthy.Id, amount: 1000m);
        AddEntry(healthy.Id, TimeEntryStatus.Approved, 2m); // 20%

        var closed = AddProject("ClosedOver", ProjectStatus.Closed);
        AddBudget(closed.Id, amount: 100m);
        AddEntry(closed.Id, TimeEntryStatus.Invoiced, 8m);  // over, but closed

        var dash = await _service.GetAsync(Today);

        Assert.Equal(2, dash.AtRisk.Count);
        Assert.Equal("Overrun", dash.AtRisk[0].Project.Name);   // overrun sorts first
        Assert.True(dash.AtRisk[0].HasOverrun);
        Assert.Equal("Near", dash.AtRisk[1].Project.Name);
        Assert.False(dash.AtRisk[1].HasOverrun);
        Assert.Equal(85d, dash.AtRisk[1].MaxBurnPercent);
    }

    [Fact]
    public async Task Role_overrun_makes_a_project_at_risk_even_when_overall_burn_is_fine()
    {
        var project = AddProject("RoleHot");
        AddBudget(project.Id, amount: 10_000m,
            new BudgetRoleAllocation { Id = Guid.NewGuid(), RoleId = _devRole, RoleName = "Developer", Hours = 5m });
        AddEntry(project.Id, TimeEntryStatus.Approved, 8m); // 800 of 10,000 overall, but 8 of 5 dev hours

        var dash = await _service.GetAsync(Today);

        var item = Assert.Single(dash.AtRisk);
        Assert.True(item.HasOverrun);
        var hotspot = Assert.Single(item.Hotspots(PortfolioDashboard.NearOverrunPercent));
        Assert.Equal("Developer hours", hotspot.Label);
        Assert.True(hotspot.IsOver);
    }

    [Fact]
    public async Task Deadlines_include_window_and_overdue_and_skip_closed_or_far_dates()
    {
        AddProject("Soon", endDate: Today.AddDays(10));
        AddProject("Overdue", endDate: Today.AddDays(-5));
        AddProject("Far", endDate: Today.AddDays(100));
        AddProject("ClosedSoon", ProjectStatus.Closed, endDate: Today.AddDays(10));
        AddProject("NoDate");

        var dash = await _service.GetAsync(Today);

        Assert.Equal(2, dash.UpcomingDeadlines.Count);
        Assert.Equal("Overdue", dash.UpcomingDeadlines[0].Item.Project.Name);
        Assert.True(dash.UpcomingDeadlines[0].IsOverdue);
        Assert.Equal(-5, dash.UpcomingDeadlines[0].DaysLeft);
        Assert.Equal("Soon", dash.UpcomingDeadlines[1].Item.Project.Name);
        Assert.Equal(10, dash.UpcomingDeadlines[1].DaysLeft);
    }

    [Fact]
    public async Task No_budget_reports_null_burn_and_never_at_risk()
    {
        var project = AddProject("Unbudgeted");
        AddEntry(project.Id, TimeEntryStatus.Approved, 8m);

        var dash = await _service.GetAsync(Today);

        var item = Assert.Single(dash.Projects);
        Assert.Null(item.BurnPercent);
        Assert.False(item.HasOverrun);
        Assert.Empty(dash.AtRisk);
    }

    [Fact]
    public async Task Viewer_without_a_management_role_is_rejected()
    {
        _currentUser.Roles.Clear();
        _currentUser.Roles.Add(RoleNames.Developer);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.GetAsync(Today));
    }
}
