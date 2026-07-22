using Npgsql;
using Crosscheck.Domain.Entities;
using Crosscheck.Infrastructure.Persistence;
using Crosscheck.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace Crosscheck.IntegrationTests;

public sealed class ModuleRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private NpgsqlDataSource _dataSource = null!;
    private ModuleRepository _modules = null!;
    private RateCardRepository _rateCards = null!;

    public async Task InitializeAsync()
    {
        DapperConfig.Apply();
        await _postgres.StartAsync();
        DatabaseMigrator.MigrateToLatest(_postgres.GetConnectionString());
        _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        _modules = new ModuleRepository(_dataSource);
        _rateCards = new RateCardRepository(_dataSource);
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private static ProjectModule NewModule(string name = "Ag Chem", int sortOrder = 1) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = SeedData.LeaveProjectId,
        Name = name,
        SortOrder = sortOrder,
    };

    [Fact]
    public async Task Module_roundtrips_with_allocations_and_effective_hours()
    {
        var module = NewModule();
        module.Amount = 41040.00m;
        module.Allocations =
        [
            new ModuleRoleAllocation { Id = Guid.NewGuid(), ModuleId = module.Id, RoleId = SeedData.DeveloperRoleId, Hours = 240m },
            new ModuleRoleAllocation { Id = Guid.NewGuid(), ModuleId = module.Id, RoleId = SeedData.ProjectManagerRoleId, Hours = 32m },
        ];
        await _modules.AddAsync(module);

        var loaded = Assert.Single(await _modules.GetForProjectAsync(SeedData.LeaveProjectId));
        Assert.Equal("Ag Chem", loaded.Name);
        Assert.Equal(41040.00m, loaded.Amount);
        Assert.True(loaded.IsFixedPrice);
        Assert.Null(loaded.Hours);
        Assert.Equal(272m, loaded.EffectiveHours); // derived from allocations
        Assert.Equal(2, loaded.Allocations.Count);
        Assert.Contains(loaded.Allocations, a => a.RoleName == "Developer" && a.Hours == 240m);

        Assert.True(await _modules.HasLiveModulesAsync(SeedData.LeaveProjectId));
    }

    [Fact]
    public async Task Flat_hours_win_over_allocation_sum()
    {
        var module = NewModule("5. DMAP – Bug Fixes");
        module.Hours = 120m;
        await _modules.AddAsync(module);

        var loaded = await _modules.GetByIdAsync(module.Id);
        Assert.Equal(120m, loaded!.Hours);
        Assert.Equal(120m, loaded.EffectiveHours);
        Assert.False(loaded.IsFixedPrice);
    }

    [Fact]
    public async Task Replace_allocations_is_wholesale()
    {
        var module = NewModule();
        module.Allocations =
        [
            new ModuleRoleAllocation { Id = Guid.NewGuid(), ModuleId = module.Id, RoleId = SeedData.DeveloperRoleId, Hours = 240m },
        ];
        await _modules.AddAsync(module);

        await _modules.ReplaceAllocationsAsync(module.Id,
        [
            new ModuleRoleAllocation { Id = Guid.NewGuid(), ModuleId = module.Id, RoleId = SeedData.OperationsManagerRoleId, Hours = 32m },
        ]);

        var loaded = await _modules.GetByIdAsync(module.Id);
        var allocation = Assert.Single(loaded!.Allocations);
        Assert.Equal(SeedData.OperationsManagerRoleId, allocation.RoleId);
        Assert.Equal(32m, allocation.Hours);
    }

    [Fact]
    public async Task Duplicate_live_name_is_rejected_case_insensitively_and_freed_by_soft_delete()
    {
        var module = NewModule("Water Levels");
        await _modules.AddAsync(module);

        await Assert.ThrowsAsync<PostgresException>(() => _modules.AddAsync(NewModule("water levels")));

        await _modules.SoftDeleteAsync(module.Id);
        Assert.False(await _modules.HasLiveModulesAsync(SeedData.LeaveProjectId));
        Assert.Empty(await _modules.GetForProjectAsync(SeedData.LeaveProjectId));
        Assert.Single(await _modules.GetForProjectAsync(SeedData.LeaveProjectId, includeDeleted: true));

        // The live-name index ignores soft-deleted rows, so the name can be reused.
        await _modules.AddAsync(NewModule("Water Levels"));
    }

    [Fact]
    public async Task Rate_overrides_roundtrip_and_live_unique_index_covers_module_wide_row()
    {
        var module = NewModule();
        await _modules.AddAsync(module);

        await _modules.AddRateAsync(new ProjectModuleRate
        {
            Id = Guid.NewGuid(), ModuleId = module.Id, RoleId = SeedData.DeveloperRoleId, HourlyRate = 150.00m,
        });
        var moduleWide = new ProjectModuleRate { Id = Guid.NewGuid(), ModuleId = module.Id, RoleId = null, HourlyRate = 85.00m };
        await _modules.AddRateAsync(moduleWide);

        // NULLS NOT DISTINCT: a second live module-wide row collides.
        await Assert.ThrowsAsync<PostgresException>(() => _modules.AddRateAsync(new ProjectModuleRate
        {
            Id = Guid.NewGuid(), ModuleId = module.Id, RoleId = null, HourlyRate = 90.00m,
        }));

        var rates = await _modules.GetRatesAsync(module.Id);
        Assert.Equal(2, rates.Count);
        Assert.Null(rates[0].Rate.RoleId);       // module-wide row first
        Assert.Null(rates[0].RoleName);
        Assert.Equal("Developer", rates[1].RoleName);

        Assert.Equal(85.00m, (await _modules.GetRateForRoleAsync(module.Id, null))!.HourlyRate);

        await _modules.SoftDeleteRateAsync(moduleWide.Id);
        Assert.Null(await _modules.GetRateForRoleAsync(module.Id, null));
        // Freed for re-add after soft delete.
        await _modules.AddRateAsync(new ProjectModuleRate
        {
            Id = Guid.NewGuid(), ModuleId = module.Id, RoleId = null, HourlyRate = 95.00m,
        });
    }

    [Fact]
    public async Task Resolve_prefers_role_override_then_module_wide_then_project_rate()
    {
        var module = NewModule();
        await _modules.AddAsync(module);

        // Project rate only.
        await _rateCards.AddAsync(new ProjectRateCard
        {
            Id = Guid.NewGuid(), ProjectId = SeedData.LeaveProjectId, RoleId = SeedData.DeveloperRoleId, HourlyRate = 135.00m,
        });
        Assert.Equal(135.00m, await _rateCards.ResolveAsync(SeedData.LeaveProjectId, module.Id, SeedData.DeveloperRoleId));

        // Module-wide override beats the project rate.
        await _modules.AddRateAsync(new ProjectModuleRate
        {
            Id = Guid.NewGuid(), ModuleId = module.Id, RoleId = null, HourlyRate = 85.00m,
        });
        Assert.Equal(85.00m, await _rateCards.ResolveAsync(SeedData.LeaveProjectId, module.Id, SeedData.DeveloperRoleId));

        // Role-specific override beats both.
        await _modules.AddRateAsync(new ProjectModuleRate
        {
            Id = Guid.NewGuid(), ModuleId = module.Id, RoleId = SeedData.DeveloperRoleId, HourlyRate = 150.00m,
        });
        Assert.Equal(150.00m, await _rateCards.ResolveAsync(SeedData.LeaveProjectId, module.Id, SeedData.DeveloperRoleId));

        // A role without overrides still falls through to the (absent) project rate.
        Assert.Equal(85.00m, await _rateCards.ResolveAsync(SeedData.LeaveProjectId, module.Id, SeedData.ProjectManagerRoleId));

        // No module → straight to the project rate.
        Assert.Equal(135.00m, await _rateCards.ResolveAsync(SeedData.LeaveProjectId, null, SeedData.DeveloperRoleId));
    }
}
