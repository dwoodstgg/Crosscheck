using Crosscheck.Application.Projects;
using Crosscheck.Domain;
using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;
using Crosscheck.UnitTests.Fakes;

namespace Crosscheck.UnitTests.Projects;

public class ModuleServiceTests
{
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeProjectRepository _projects = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly FakeModuleRepository _modules;
    private readonly FakeRateCardRepository _rateCards;
    private readonly FakeAuditLog _audit = new();
    private readonly FakeBudgetAlertService _alerts = new();
    private readonly ModuleService _service;

    private readonly Role _developerRole = new() { Id = Guid.NewGuid(), Name = RoleNames.Developer, DisplayName = "Developer" };
    private readonly Role _pmRole = new() { Id = Guid.NewGuid(), Name = RoleNames.ProjectManager, DisplayName = "Project Manager" };
    private readonly Role _adminRole = new() { Id = Guid.NewGuid(), Name = RoleNames.Admin, IsBillable = false, IsSystemAdmin = true };
    private readonly Project _project;

    public ModuleServiceTests()
    {
        _modules = new FakeModuleRepository(_roles);
        _rateCards = new FakeRateCardRepository(_roles);
        _service = new ModuleService(_currentUser, _projects, _modules, _rateCards, _roles, _audit, _alerts);

        _roles.Roles.AddRange([_developerRole, _pmRole, _adminRole]);
        _project = new Project
        {
            Id = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Name = "MDEQ WO",
            Code = "MDEQ-001",
            ProjectManagerId = _currentUser.EmployeeId!.Value,
        };
        _projects.Projects.Add(_project);
        _currentUser.Roles.Add(RoleNames.ProjectManager);
    }

    private Task<Guid> CreateAgChemAsync() =>
        _service.CreateAsync(_project.Id, "Ag Chem", hours: null, amount: null,
        [
            new ModuleRoleHourInput(_developerRole.Id, 240m),
            new ModuleRoleHourInput(_pmRole.Id, 32m),
        ]);

    [Fact]
    public async Task Create_with_role_allocations_derives_effective_hours_and_audits()
    {
        await CreateAgChemAsync();

        var module = Assert.Single(_modules.Modules);
        Assert.Equal("Ag Chem", module.Name);
        Assert.Equal(1, module.SortOrder);
        Assert.Equal(272m, module.EffectiveHours);
        Assert.False(module.IsFixedPrice);
        Assert.Single(_audit.Events, e => e.Action == "module.create");
        Assert.Contains((_project.Id, module.Id), _alerts.ModuleChanged);
    }

    [Fact]
    public async Task Create_flat_hours_milestone_with_agreed_amount()
    {
        await _service.CreateAsync(_project.Id, "Cost Share – Funded Projects Tool", hours: 40m, amount: 5400m);

        var module = Assert.Single(_modules.Modules);
        Assert.Equal(40m, module.EffectiveHours);
        Assert.True(module.IsFixedPrice);
        Assert.Empty(module.Allocations);
    }

    [Fact]
    public async Task Sort_order_increments_and_duplicate_live_name_is_rejected()
    {
        await CreateAgChemAsync();
        await _service.CreateAsync(_project.Id, "Water Levels", hours: 80m, amount: null);
        Assert.Equal(2, _modules.Modules[1].SortOrder);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.CreateAsync(_project.Id, "ag chem", hours: 1m, amount: null));
        Assert.Contains("already has a module", ex.Message);

        // Milestone-labeled projects word the error accordingly.
        _project.BreakdownLabel = BreakdownLabel.Milestone;
        var ex2 = await Assert.ThrowsAsync<DomainException>(() =>
            _service.CreateAsync(_project.Id, "AG CHEM", hours: 1m, amount: null));
        Assert.Contains("milestone", ex2.Message);
    }

    [Fact]
    public async Task Non_billable_role_cannot_carry_an_allocation()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.CreateAsync(_project.Id, "Ag Chem", null, null,
                [new ModuleRoleHourInput(_adminRole.Id, 10m)]));
        Assert.Contains("not a billable role", ex.Message);
    }

    [Fact]
    public async Task Rename_checks_siblings_and_audits()
    {
        var id = await CreateAgChemAsync();
        await _service.CreateAsync(_project.Id, "Water Levels", hours: 80m, amount: null);

        await _service.RenameAsync(_project.Id, id, "Ag Chem Enhancements");
        Assert.Equal("Ag Chem Enhancements", _modules.Modules[0].Name);
        Assert.Single(_audit.Events, e => e.Action == "module.rename");

        await Assert.ThrowsAsync<DomainException>(() =>
            _service.RenameAsync(_project.Id, id, "water levels"));
    }

    [Fact]
    public async Task Set_hours_allocations_and_amount_roundtrip()
    {
        var id = await CreateAgChemAsync();

        await _service.SetHoursAsync(_project.Id, id, 300m);
        Assert.Equal(300m, _modules.Modules[0].Hours);
        Assert.Equal(300m, _modules.Modules[0].EffectiveHours); // flat wins over the 272h sum

        await _service.SetAllocationsAsync(_project.Id, id, [new ModuleRoleHourInput(_developerRole.Id, 100m)]);
        Assert.Single(_modules.Modules[0].Allocations);

        await _service.SetAmountAsync(_project.Id, id, 41040m);
        Assert.True(_modules.Modules[0].IsFixedPrice);
        Assert.Single(_audit.Events, e => e.Action == "module.amount");

        await _service.SetAmountAsync(_project.Id, id, null); // back to T&M
        Assert.False(_modules.Modules[0].IsFixedPrice);
    }

    [Fact]
    public async Task Delete_is_soft_and_allowed_with_logged_time()
    {
        var id = await CreateAgChemAsync();
        _modules.Logged.Add((id, _developerRole.Id));

        await _service.DeleteAsync(_project.Id, id);

        var module = Assert.Single(_modules.Modules);
        Assert.True(module.IsDeleted);
        var audit = Assert.Single(_audit.Events, e => e.Action == "module.delete");

        // A deleted module rejects further edits.
        await Assert.ThrowsAsync<DomainException>(() => _service.RenameAsync(_project.Id, id, "X"));
    }

    [Fact]
    public async Task Module_of_another_project_is_rejected()
    {
        var id = await CreateAgChemAsync();
        var otherProject = new Project
        {
            Id = Guid.NewGuid(), ClientId = Guid.NewGuid(), Name = "Other", Code = "GEO-999",
            ProjectManagerId = _currentUser.EmployeeId!.Value,
        };
        _projects.Projects.Add(otherProject);

        await Assert.ThrowsAsync<DomainException>(() => _service.RenameAsync(otherProject.Id, id, "X"));
    }

    // Rate overrides ------------------------------------------------------------

    [Fact]
    public async Task Module_wide_and_role_rates_coexist_but_duplicates_are_rejected()
    {
        var id = await CreateAgChemAsync();

        await _service.SetRateAsync(_project.Id, id, null, 85m);
        await _service.SetRateAsync(_project.Id, id, _developerRole.Id, 150m);
        Assert.Equal(2, _modules.ModuleRates.Count);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.SetRateAsync(_project.Id, id, null, 90m));
        Assert.Contains("all-roles rate", ex.Message);

        await Assert.ThrowsAsync<DomainException>(() =>
            _service.SetRateAsync(_project.Id, id, _developerRole.Id, 165m));
    }

    [Fact]
    public async Task Rate_override_on_non_billable_role_is_rejected()
    {
        var id = await CreateAgChemAsync();
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.SetRateAsync(_project.Id, id, _adminRole.Id, 100m));
    }

    [Fact]
    public async Task Correct_and_delete_lock_once_invoiced()
    {
        var id = await CreateAgChemAsync();
        await _service.SetRateAsync(_project.Id, id, _developerRole.Id, 150m);
        var rate = Assert.Single(_modules.ModuleRates);

        _modules.Invoiced.Add((id, _developerRole.Id));

        await Assert.ThrowsAsync<DomainException>(() =>
            _service.CorrectRateAsync(_project.Id, rate.Id, 160m));
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.DeleteRateAsync(_project.Id, rate.Id));
    }

    [Fact]
    public async Task Delete_override_with_logged_time_needs_a_fallback()
    {
        var id = await CreateAgChemAsync();
        await _service.SetRateAsync(_project.Id, id, _developerRole.Id, 150m);
        var rate = Assert.Single(_modules.ModuleRates);
        _modules.Logged.Add((id, _developerRole.Id));

        // No module-wide override, no project rate → the logged time would lose its price.
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.DeleteRateAsync(_project.Id, rate.Id));
        Assert.Contains("nothing else prices it", ex.Message);

        // A project rate for the role covers the fallback → delete is allowed.
        _rateCards.Rates.Add(new ProjectRateCard
        {
            Id = Guid.NewGuid(), ProjectId = _project.Id, RoleId = _developerRole.Id, HourlyRate = 135m,
        });
        await _service.DeleteRateAsync(_project.Id, rate.Id);
        Assert.Empty(_modules.ModuleRates);
        Assert.Single(_audit.Events, e => e.Action == "module.rate.delete");
    }

    [Fact]
    public async Task Delete_module_wide_override_blocked_when_a_logged_role_is_uncovered()
    {
        var id = await CreateAgChemAsync();
        await _service.SetRateAsync(_project.Id, id, null, 85m);
        var moduleWide = Assert.Single(_modules.ModuleRates);
        _modules.Logged.Add((id, _developerRole.Id));
        _modules.Logged.Add((id, _pmRole.Id));

        // Developer is covered by a project rate, PM is not → still blocked.
        _rateCards.Rates.Add(new ProjectRateCard
        {
            Id = Guid.NewGuid(), ProjectId = _project.Id, RoleId = _developerRole.Id, HourlyRate = 135m,
        });
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.DeleteRateAsync(_project.Id, moduleWide.Id));

        // Cover PM too → delete goes through.
        _rateCards.Rates.Add(new ProjectRateCard
        {
            Id = Guid.NewGuid(), ProjectId = _project.Id, RoleId = _pmRole.Id, HourlyRate = 175m,
        });
        await _service.DeleteRateAsync(_project.Id, moduleWide.Id);
        Assert.Empty(_modules.ModuleRates);
    }

    // Planned value -------------------------------------------------------------

    [Fact]
    public void Planned_value_uses_override_else_module_wide_else_project_rate()
    {
        var module = new ProjectModule
        {
            Id = Guid.NewGuid(), ProjectId = _project.Id, Name = "Ag Chem",
            Allocations =
            [
                new ModuleRoleAllocation { RoleId = _developerRole.Id, Hours = 240m },
                new ModuleRoleAllocation { RoleId = _pmRole.Id, Hours = 32m },
            ],
        };
        var projectRates = new Dictionary<Guid, decimal> { [_developerRole.Id] = 135m, [_pmRole.Id] = 175m };

        // MDEQ math: 240×135 + 32×175 = 32,400 + 5,600.
        Assert.Equal(38000m, ModuleService.PlannedValue(module, [], projectRates));

        // A role override redirects just that role.
        var overrides = new List<ModuleRateSummary>
        {
            new(new ProjectModuleRate { ModuleId = module.Id, RoleId = _developerRole.Id, HourlyRate = 150m }, "Developer"),
        };
        Assert.Equal(240m * 150m + 32m * 175m, ModuleService.PlannedValue(module, overrides, projectRates));

        // Missing any needed rate → null.
        Assert.Null(ModuleService.PlannedValue(module, [], new Dictionary<Guid, decimal>()));

        // Flat hours price only via a module-wide rate.
        var flat = new ProjectModule { Id = Guid.NewGuid(), ProjectId = _project.Id, Name = "Maintenance", Hours = 115m };
        Assert.Null(ModuleService.PlannedValue(flat, [], projectRates));
        var wide = new List<ModuleRateSummary>
        {
            new(new ProjectModuleRate { ModuleId = flat.Id, RoleId = null, HourlyRate = 85m }, null),
        };
        Assert.Equal(115m * 85m, ModuleService.PlannedValue(flat, wide, projectRates));
    }
}
