using Microsoft.Extensions.Logging.Abstractions;
using Crosscheck.Application.Projects;
using Crosscheck.Domain;
using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;
using Crosscheck.UnitTests.Fakes;

namespace Crosscheck.UnitTests.Projects;

public class BudgetAlertServiceTests
{
    private readonly FakeProjectRepository _projects = new();
    private readonly FakeBudgetRepository _budgets = new();
    private readonly FakeBudgetAlertRepository _alerts = new();
    private readonly FakeTimeEntryRepository _entries = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly FakeModuleRepository _modules;
    private readonly FakeEmployeeRepository _employees;
    private readonly FakeEmailSender _email = new();
    private readonly BudgetAlertService _service;

    private readonly Guid _devRole = Guid.NewGuid();
    private readonly Guid _pmId = Guid.NewGuid();
    private readonly Project _project;
    private readonly Budget _budget;

    public BudgetAlertServiceTests()
    {
        _employees = new FakeEmployeeRepository(_roles);
        _modules = new FakeModuleRepository(_roles);
        _service = new BudgetAlertService(
            _projects, _budgets, _alerts, _entries, _modules, _employees, _email,
            NullLogger<BudgetAlertService>.Instance);

        _roles.Roles.Add(new Role { Id = Guid.NewGuid(), Name = RoleNames.OperationsManager });
        _entries.RatesByRole[_devRole] = 100m;

        _project = new Project
        {
            Id = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Name = "Redesign",
            Code = "GEO-014",
            ProjectManagerId = _pmId,
        };
        _projects.Projects.Add(_project);
        _employees.Employees.Add(new Employee { Id = _pmId, Email = "pm@geo.test", DisplayName = "Pat PM" });

        _budget = new Budget
        {
            Id = Guid.NewGuid(),
            ProjectId = _project.Id,
            Type = ProjectType.Hourly,
            Amount = 1000m,
            AlertThresholds = [50, 75, 90],
        };
        _budgets.Budgets.Add(_budget);
    }

    private void AddApproved(decimal hours) => _entries.Entries.Add(new TimeEntry
    {
        Id = Guid.NewGuid(),
        ProjectId = _project.Id,
        EmployeeId = Guid.NewGuid(),
        BillingRoleId = _devRole,
        EntryDate = new DateOnly(2026, 7, 8),
        HoursWorked = hours,
        HoursBilled = hours,
        IsBillable = true,
        Status = TimeEntryStatus.Approved,
    });

    private Guid AddOps(string email)
    {
        var opsRoleId = _roles.Roles.Single(r => r.Name == RoleNames.OperationsManager).Id;
        var id = Guid.NewGuid();
        _employees.Employees.Add(new Employee { Id = id, Email = email, DisplayName = "Olivia Ops" });
        _employees.RoleIdsByEmployee[id] = [opsRoleId];
        return id;
    }

    [Fact]
    public async Task Crossing_a_threshold_emails_the_pm_once()
    {
        AddApproved(6m); // 6 × 100 = 600 → 60% burn, crosses 50 (not 75)

        await _service.EvaluateAsync(_project.Id);

        var message = Assert.Single(_email.Sent);
        Assert.Contains("pm@geo.test", message.To);
        Assert.Contains("50%", message.Subject);

        // A second evaluation with no change re-sends nothing (deduped).
        await _service.EvaluateAsync(_project.Id);
        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task Multiple_thresholds_crossed_at_once_each_fire()
    {
        AddApproved(8m); // 800 → 80% burn, crosses 50 and 75 (not 90)

        await _service.EvaluateAsync(_project.Id);

        Assert.Equal(2, _email.Sent.Count);
        Assert.Equal(2, _alerts.Alerts.Count);
    }

    [Fact]
    public async Task Ops_is_notified_at_ninety_percent_but_not_below()
    {
        AddOps("ops@geo.test");
        AddApproved(6m); // 60% — PM only, no Ops yet

        await _service.EvaluateAsync(_project.Id);
        Assert.DoesNotContain(_email.Sent, m => m.To.Contains("ops@geo.test"));

        AddApproved(3m); // now 900 → 90% — Ops looped in
        await _service.EvaluateAsync(_project.Id);
        Assert.Contains(_email.Sent, m => m.To.Contains("ops@geo.test") && m.Subject.Contains("90%"));
    }

    [Fact]
    public async Task Overrun_fires_a_distinct_ops_alert()
    {
        AddOps("ops@geo.test");
        AddApproved(12m); // 1200 → 120% — crosses every threshold and overrun

        await _service.EvaluateAsync(_project.Id);

        var overrun = Assert.Single(_email.Sent, m => m.Subject.Contains("over budget"));
        Assert.Contains("ops@geo.test", overrun.To);
        Assert.Contains(_alerts.Alerts, a => a.AlertKey == "overrun");
    }

    [Fact]
    public async Task A_role_allocation_alerts_when_that_role_passes_a_threshold()
    {
        _budget.RoleAllocations =
        [
            new BudgetRoleAllocation
            {
                Id = Guid.NewGuid(), BudgetId = _budget.Id, RoleId = _devRole, Hours = 10m, RoleName = "Lead Developer",
            },
        ];
        AddApproved(6m); // 6 of 10 role-hours = 60% for the role

        await _service.EvaluateAsync(_project.Id);

        Assert.Contains(_email.Sent, m => m.Subject.Contains("Lead Developer") && m.Subject.Contains("50%"));
        Assert.Contains(_alerts.Alerts, a => a.AlertKey == $"role:{_devRole}:pct:50");

        // Deduped on a second pass with no change.
        var before = _email.Sent.Count;
        await _service.EvaluateAsync(_project.Id);
        Assert.Equal(before, _email.Sent.Count);
    }

    [Fact]
    public async Task A_role_over_its_hours_allocation_alerts_ops()
    {
        AddOps("ops@geo.test");
        _budget.RoleAllocations =
        [
            new BudgetRoleAllocation
            {
                Id = Guid.NewGuid(), BudgetId = _budget.Id, RoleId = _devRole, Hours = 5m, RoleName = "Lead Developer",
            },
        ];
        AddApproved(7m); // 7 of 5 role-hours → over

        await _service.EvaluateAsync(_project.Id);

        var overrun = Assert.Single(_email.Sent, m => m.Subject.Contains("Lead Developer") && m.Subject.Contains("over its hours budget"));
        Assert.Contains("ops@geo.test", overrun.To);
        Assert.Contains(_alerts.Alerts, a => a.AlertKey == $"role:{_devRole}:overrun");
    }

    [Fact]
    public async Task No_budget_sends_nothing()
    {
        _budgets.Budgets.Clear();
        AddApproved(50m);

        await _service.EvaluateAsync(_project.Id);

        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task Pending_open_work_does_not_trip_thresholds()
    {
        _entries.Entries.Add(new TimeEntry
        {
            Id = Guid.NewGuid(),
            ProjectId = _project.Id,
            EmployeeId = Guid.NewGuid(),
            BillingRoleId = _devRole,
            EntryDate = new DateOnly(2026, 7, 8),
            HoursWorked = 9m,
            HoursBilled = 9m,
            IsBillable = true,
            Status = TimeEntryStatus.Open, // 900 pending, but not yet "spent"
        });

        await _service.EvaluateAsync(_project.Id);

        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task On_budget_change_re_arms_so_a_raised_budget_alerts_again()
    {
        AddApproved(6m); // 60%
        await _service.EvaluateAsync(_project.Id);
        Assert.Single(_email.Sent); // 50% fired

        // Owner raises the budget; thresholds re-arm. Burn is now 600/2000 = 30%, below 50%.
        _budget.Amount = 2000m;
        await _service.OnBudgetChangedAsync(_project.Id);
        Assert.Empty(_alerts.Alerts); // cleared
        Assert.Single(_email.Sent);   // nothing new to fire at 30%

        // More work pushes back over 50% of the new budget — fires again.
        AddApproved(5m); // 1100/2000 = 55%
        await _service.EvaluateAsync(_project.Id);
        Assert.Equal(2, _email.Sent.Count);
    }

    // Modules -------------------------------------------------------------------

    private ProjectModule AddModule(string name, decimal hours)
    {
        var module = new ProjectModule
        {
            Id = Guid.NewGuid(), ProjectId = _project.Id, Name = name, Hours = hours,
        };
        _modules.Modules.Add(module);
        return module;
    }

    private void AddApprovedOnModule(Guid moduleId, decimal hours) => _entries.Entries.Add(new TimeEntry
    {
        Id = Guid.NewGuid(),
        ProjectId = _project.Id,
        ModuleId = moduleId,
        EmployeeId = Guid.NewGuid(),
        BillingRoleId = _devRole,
        EntryDate = new DateOnly(2026, 7, 8),
        HoursWorked = hours,
        HoursBilled = hours,
        IsBillable = true,
        Status = TimeEntryStatus.Approved,
    });

    [Fact]
    public async Task A_module_alerts_when_its_hours_pass_a_threshold_and_message_uses_the_label()
    {
        _budget.Amount = 100000m; // keep overall burn far below every threshold
        var agChem = AddModule("Ag Chem", hours: 10m);
        AddModule("Water Levels", hours: 100m);
        AddApprovedOnModule(agChem.Id, 6m); // 60% of the module — only its own key fires

        await _service.EvaluateAsync(_project.Id);

        var message = Assert.Single(_email.Sent);
        Assert.Contains("Ag Chem module", message.Subject);
        Assert.Contains(_alerts.Alerts, a => a.AlertKey == $"module:{agChem.Id}:pct:50");

        // Deduped on a second pass with no change.
        await _service.EvaluateAsync(_project.Id);
        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task Milestone_labeled_projects_word_the_module_alert_accordingly()
    {
        _project.BreakdownLabel = BreakdownLabel.Milestone;
        _budget.Amount = 100000m;
        var milestone = AddModule("DMAP – Bug Fixes", hours: 10m);
        AddModule("Everything Else", hours: 100m); // keeps overall hours burn below 50%
        AddApprovedOnModule(milestone.Id, 6m);

        await _service.EvaluateAsync(_project.Id);

        var message = Assert.Single(_email.Sent);
        Assert.Contains("milestone", message.Subject);
    }

    [Fact]
    public async Task A_module_over_its_hours_alerts_ops()
    {
        AddOps("ops@geo.test");
        _budget.Amount = 100000m;
        var module = AddModule("Supplemental Hours", hours: 5m);
        AddApprovedOnModule(module.Id, 7m); // over

        await _service.EvaluateAsync(_project.Id);

        var overrun = Assert.Single(_email.Sent, m => m.Subject.Contains("over its hours budget"));
        Assert.Contains("ops@geo.test", overrun.To);
        Assert.Contains(_alerts.Alerts, a => a.AlertKey == $"module:{module.Id}:overrun");
    }

    [Fact]
    public async Task On_module_change_re_arms_only_that_modules_keys()
    {
        _budget.Amount = 100000m;
        var agChem = AddModule("Ag Chem", hours: 10m);
        var waterLevels = AddModule("Water Levels", hours: 10m);
        AddModule("Everything Else", hours: 100m); // keeps overall hours burn below 50%
        AddApprovedOnModule(agChem.Id, 6m);
        AddApprovedOnModule(waterLevels.Id, 6m);

        await _service.EvaluateAsync(_project.Id);
        Assert.Equal(2, _email.Sent.Count); // both modules crossed 50%

        // Ag Chem's hours are raised; only its keys clear, and at 30% nothing re-fires.
        agChem.Hours = 20m;
        await _service.OnModuleChangedAsync(_project.Id, agChem.Id);
        Assert.DoesNotContain(_alerts.Alerts, a => a.AlertKey.StartsWith($"module:{agChem.Id}:"));
        Assert.Contains(_alerts.Alerts, a => a.AlertKey == $"module:{waterLevels.Id}:pct:50");
        Assert.Equal(2, _email.Sent.Count);

        // More Ag Chem work crosses 50% of the new figure — its alert can fire again.
        AddApprovedOnModule(agChem.Id, 6m); // separate day not needed for the fake; total 12/20 = 60%
        await _service.EvaluateAsync(_project.Id);
        Assert.Contains(_alerts.Alerts, a => a.AlertKey == $"module:{agChem.Id}:pct:50");
    }

    [Fact]
    public async Task Deleted_modules_do_not_alert()
    {
        _budget.Amount = 100000m;
        var module = AddModule("Ag Chem", hours: 5m);
        AddApprovedOnModule(module.Id, 7m);
        module.DeletedAt = DateTimeOffset.UtcNow;
        AddModule("Water Levels", hours: 100m); // project stays modular

        await _service.EvaluateAsync(_project.Id);

        Assert.DoesNotContain(_alerts.Alerts, a => a.AlertKey.StartsWith($"module:{module.Id}:"));
    }
}
