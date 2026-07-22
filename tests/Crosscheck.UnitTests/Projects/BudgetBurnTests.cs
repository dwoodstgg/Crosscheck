using Crosscheck.Application.Projects;
using Crosscheck.Application.TimeEntries;
using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;

namespace Crosscheck.UnitTests.Projects;

/// <summary>Module-aware burn maths, exercised with the two real work-order shapes: MDEQ
/// (per-role hours per module) and NRIS (flat hours per milestone).</summary>
public class BudgetBurnTests
{
    private readonly Guid _projectId = Guid.NewGuid();
    private readonly Guid _devRole = Guid.NewGuid();
    private readonly Guid _pmRole = Guid.NewGuid();
    private readonly Guid _opsRole = Guid.NewGuid();

    private Budget NewBudget(decimal? amount = null, decimal? hours = null) => new()
    {
        Id = Guid.NewGuid(), ProjectId = _projectId,
        Type = BudgetType.TimeAndMaterialsCap, Amount = amount ?? 56160m, Hours = hours,
    };

    private ProjectModule Module(string name, int sortOrder, params (Guid RoleId, string RoleName, decimal Hours)[] allocations)
    {
        var module = new ProjectModule { Id = Guid.NewGuid(), ProjectId = _projectId, Name = name, SortOrder = sortOrder };
        module.Allocations = allocations
            .Select(a => new ModuleRoleAllocation { Id = Guid.NewGuid(), ModuleId = module.Id, RoleId = a.RoleId, RoleName = a.RoleName, Hours = a.Hours })
            .ToList();
        return module;
    }

    private BurnRow Row(Guid? moduleId, Guid roleId, TimeEntryStatus status, decimal hours,
        decimal? rate = 100m, string? moduleName = null, bool moduleDeleted = false) =>
        new(Guid.NewGuid(), new DateOnly(2026, 8, 15), status, IsBillable: true,
            Guid.NewGuid(), "Someone", roleId, "Role",
            moduleId, moduleName ?? (moduleId is null ? null : "M"), moduleDeleted,
            hours, hours, rate);

    [Fact]
    public void Mdeq_shape_rolls_project_hours_and_roles_up_from_modules()
    {
        // The MDEQ work order: 3 modules, per-role hours, 498h total.
        var agChem = Module("Ag Chem", 1, (_devRole, "Developer", 240m), (_pmRole, "PM", 32m), (_opsRole, "Ops", 32m));
        var waterLevels = Module("Water Levels Design", 2, (_devRole, "Developer", 80m), (_pmRole, "PM", 16m), (_opsRole, "Ops", 16m));
        var supplemental = Module("Supplemental Hours", 3, (_devRole, "Developer", 64m), (_pmRole, "PM", 9m), (_opsRole, "Ops", 9m));

        var status = BudgetBurn.Compute(NewBudget(), [agChem, waterLevels, supplemental],
        [
            Row(agChem.Id, _devRole, TimeEntryStatus.Approved, 8m, 135m),
            Row(agChem.Id, _pmRole, TimeEntryStatus.Approved, 2m, 175m),
            Row(waterLevels.Id, _devRole, TimeEntryStatus.Open, 4m, 135m),
        ]);

        Assert.Equal(498m, status.HoursBudget);

        // Per-role project totals sum across modules: Developer 240+80+64.
        var dev = Assert.Single(status.Roles, r => r.RoleId == _devRole);
        Assert.Equal(384m, dev.AllocatedHours);
        Assert.Equal(8m, dev.SpentHours);
        Assert.Equal(4m, dev.PendingHours);

        Assert.Equal(3, status.Modules.Count);
        var agChemBurn = status.Modules[0];
        Assert.Equal("Ag Chem", agChemBurn.ModuleName);
        Assert.Equal(304m, agChemBurn.AllocatedHours);
        Assert.Equal(10m, agChemBurn.SpentHours);
        Assert.Equal(8m * 135m + 2m * 175m, agChemBurn.SpentValue);
        var agChemDev = Assert.Single(agChemBurn.Roles, r => r.RoleId == _devRole);
        Assert.Equal(240m, agChemDev.AllocatedHours);
        Assert.Equal(8m, agChemDev.SpentHours);

        Assert.Equal(4m, status.Modules[1].PendingHours);
        Assert.Equal(0m, status.Modules[2].SpentHours);
    }

    [Fact]
    public void Nris_shape_flat_hours_and_fixed_price_roll_up_without_roles()
    {
        // Milestones with flat hours (no role split); one fixed-price.
        var m1 = new ProjectModule { Id = Guid.NewGuid(), ProjectId = _projectId, Name = "Cost Share", SortOrder = 1, Hours = 40m, Amount = 5400m };
        var m11 = new ProjectModule { Id = Guid.NewGuid(), ProjectId = _projectId, Name = "Ongoing Maintenance", SortOrder = 11, Hours = 115m };

        var status = BudgetBurn.Compute(NewBudget(), [m1, m11],
        [
            Row(m1.Id, _devRole, TimeEntryStatus.Approved, 10m, 85m),
        ]);

        Assert.Equal(155m, status.HoursBudget);
        Assert.Empty(status.Roles); // no role allocations anywhere

        var costShare = status.Modules[0];
        Assert.True(costShare.IsFixedPrice);
        Assert.Equal(5400m, costShare.Amount);
        Assert.Equal(40m, costShare.AllocatedHours);
        Assert.Equal(10m, costShare.SpentHours);
        Assert.Empty(costShare.Roles);
    }

    [Fact]
    public void Unassigned_and_deleted_module_entries_get_their_own_buckets()
    {
        var live = Module("Ag Chem", 1, (_devRole, "Developer", 100m));
        var deletedId = Guid.NewGuid();

        var status = BudgetBurn.Compute(NewBudget(), [live],
        [
            Row(live.Id, _devRole, TimeEntryStatus.Approved, 5m),
            Row(null, _devRole, TimeEntryStatus.Approved, 3m),                       // pre-module entry
            Row(deletedId, _devRole, TimeEntryStatus.Approved, 2m, moduleName: "Old Phase", moduleDeleted: true),
        ]);

        Assert.Equal(3, status.Modules.Count);

        var deleted = Assert.Single(status.Modules, m => m.IsDeleted);
        Assert.Equal("Old Phase", deleted.ModuleName);
        Assert.Equal(0m, deleted.AllocatedHours); // history only, nothing to measure against
        Assert.Equal(2m, deleted.SpentHours);

        var unassigned = Assert.Single(status.Modules, m => m.ModuleId is null);
        Assert.Equal(3m, unassigned.SpentHours);

        // Overall spend still counts every bucket.
        Assert.Equal(10m, status.SpentHours);
        // But the hours budget only rolls up live modules.
        Assert.Equal(100m, status.HoursBudget);
    }

    [Fact]
    public void Non_modular_path_is_unchanged()
    {
        var budget = NewBudget(hours: 300m);
        budget.RoleAllocations =
        [
            new BudgetRoleAllocation { Id = Guid.NewGuid(), BudgetId = budget.Id, RoleId = _devRole, RoleName = "Developer", Hours = 300m },
        ];

        var status = BudgetBurn.Compute(budget, [Row(null, _devRole, TimeEntryStatus.Approved, 8m)]);

        Assert.Equal(300m, status.HoursBudget);
        var dev = Assert.Single(status.Roles);
        Assert.Equal(8m, dev.SpentHours);
        Assert.Empty(status.Modules); // no unassigned bucket without modules
    }

    [Fact]
    public void Modular_hours_budget_ignores_the_stored_budget_hours()
    {
        var module = Module("Ag Chem", 1, (_devRole, "Developer", 100m));
        var status = BudgetBurn.Compute(NewBudget(hours: 999m), [module], []);
        Assert.Equal(100m, status.HoursBudget);
    }
}
