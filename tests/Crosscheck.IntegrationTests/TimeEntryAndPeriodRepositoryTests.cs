using Npgsql;
using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;
using Crosscheck.Infrastructure.Persistence;
using Crosscheck.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace Crosscheck.IntegrationTests;

public sealed class TimeEntryAndPeriodRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private NpgsqlDataSource _dataSource = null!;
    private TimeEntryRepository _entries = null!;
    private TimesheetPeriodRepository _periods = null!;

    private static readonly DateOnly Day = new(2026, 7, 8);

    public async Task InitializeAsync()
    {
        DapperConfig.Apply();
        await _postgres.StartAsync();
        DatabaseMigrator.MigrateToLatest(_postgres.GetConnectionString());
        _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        _entries = new TimeEntryRepository(_dataSource);
        _periods = new TimesheetPeriodRepository(_dataSource);
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private static TimeEntry NewEntry(decimal worked = 8m) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = SeedData.LeaveProjectId,
        EmployeeId = SeedData.AdminEmployeeId,
        BillingRoleId = SeedData.DeveloperRoleId,
        EntryDate = Day,
        HoursWorked = worked,
        HoursBilled = worked,
        Notes = "work",
        IsBillable = true,
        Status = TimeEntryStatus.Open,
    };

    [Fact]
    public async Task Entry_roundtrips_status_and_approval_and_joins_names()
    {
        var entry = NewEntry();
        await _entries.AddAsync(entry);

        var byCell = await _entries.GetByCellAsync(SeedData.AdminEmployeeId, SeedData.LeaveProjectId, null, Day);
        Assert.NotNull(byCell);
        Assert.Equal(TimeEntryStatus.Open, byCell!.Status);
        Assert.Equal(8m, byCell.HoursWorked);

        // Approve: adjust billed, set approver + timestamp, flip enum.
        byCell.Status = TimeEntryStatus.Approved;
        byCell.HoursBilled = 6m;
        byCell.ApprovedById = SeedData.AdminEmployeeId;
        byCell.ApprovedAt = DateTimeOffset.UtcNow;
        await _entries.UpdateAsync(byCell);

        var reloaded = await _entries.GetAsync(entry.Id);
        Assert.Equal(TimeEntryStatus.Approved, reloaded!.Status);
        Assert.Equal(8m, reloaded.HoursWorked);   // never altered
        Assert.Equal(6m, reloaded.HoursBilled);
        Assert.Equal(SeedData.AdminEmployeeId, reloaded.ApprovedById);
        Assert.NotNull(reloaded.ApprovedAt);

        var month = await _entries.GetForEmployeeRangeAsync(SeedData.AdminEmployeeId, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        Assert.Single(month);

        var forProject = await _entries.GetForProjectRangeAsync(SeedData.LeaveProjectId, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        var row = Assert.Single(forProject);
        Assert.Equal("Don Woods", row.EmployeeName);
        Assert.Equal("Developer", row.BillingRoleName);
    }

    [Fact]
    public async Task One_entry_per_person_project_day_is_enforced()
    {
        await _entries.AddAsync(NewEntry());

        await Assert.ThrowsAsync<PostgresException>(() => _entries.AddAsync(NewEntry(worked: 4m)));
    }

    [Fact]
    public async Task Delete_removes_only_open_entries()
    {
        var approved = NewEntry();
        approved.Status = TimeEntryStatus.Approved;
        await _entries.AddAsync(approved);

        await _entries.DeleteAsync(approved.Id); // WHERE status = 'open' → no-op
        Assert.NotNull(await _entries.GetAsync(approved.Id));

        approved.Status = TimeEntryStatus.Open;
        await _entries.UpdateAsync(approved);
        await _entries.DeleteAsync(approved.Id);
        Assert.Null(await _entries.GetAsync(approved.Id));
    }

    [Fact]
    public async Task Module_dimension_extends_the_cell_key_with_null_as_its_own_bucket()
    {
        var modules = new ModuleRepository(_dataSource);
        var agChem = new ProjectModule { Id = Guid.NewGuid(), ProjectId = SeedData.LeaveProjectId, Name = "Ag Chem" };
        var waterLevels = new ProjectModule { Id = Guid.NewGuid(), ProjectId = SeedData.LeaveProjectId, Name = "Water Levels" };
        await modules.AddAsync(agChem);
        await modules.AddAsync(waterLevels);

        // Same person/project/day on two different modules — both fit.
        var onAgChem = NewEntry();
        onAgChem.ModuleId = agChem.Id;
        await _entries.AddAsync(onAgChem);
        var onWaterLevels = NewEntry(worked: 2m);
        onWaterLevels.ModuleId = waterLevels.Id;
        await _entries.AddAsync(onWaterLevels);

        // The same module twice collides…
        var duplicate = NewEntry(worked: 1m);
        duplicate.ModuleId = agChem.Id;
        await Assert.ThrowsAsync<PostgresException>(() => _entries.AddAsync(duplicate));

        // …and so do two NULL-module rows (NULLS NOT DISTINCT preserves the old rule).
        await _entries.AddAsync(NewEntry(worked: 1m));
        await Assert.ThrowsAsync<PostgresException>(() => _entries.AddAsync(NewEntry(worked: 0.5m)));

        // GetByCell targets one bucket at a time.
        Assert.Equal(8m, (await _entries.GetByCellAsync(SeedData.AdminEmployeeId, SeedData.LeaveProjectId, agChem.Id, Day))!.HoursWorked);
        Assert.Equal(2m, (await _entries.GetByCellAsync(SeedData.AdminEmployeeId, SeedData.LeaveProjectId, waterLevels.Id, Day))!.HoursWorked);
        Assert.Equal(1m, (await _entries.GetByCellAsync(SeedData.AdminEmployeeId, SeedData.LeaveProjectId, null, Day))!.HoursWorked);
    }

    [Fact]
    public async Task Module_must_belong_to_the_entrys_project()
    {
        await using var connection = _dataSource.CreateConnection();
        await connection.OpenAsync();
        var otherProjectId = Guid.NewGuid();
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText =
                """
                INSERT INTO projects (id, client_id, name, code, status, project_manager_id)
                VALUES ($1, $2, 'Other', 'GEO-999', 'active', $3)
                """;
            insert.Parameters.Add(new NpgsqlParameter { Value = otherProjectId });
            insert.Parameters.Add(new NpgsqlParameter { Value = SeedData.InternalClientId });
            insert.Parameters.Add(new NpgsqlParameter { Value = SeedData.AdminEmployeeId });
            await insert.ExecuteNonQueryAsync();
        }

        var modules = new ModuleRepository(_dataSource);
        var foreign = new ProjectModule { Id = Guid.NewGuid(), ProjectId = otherProjectId, Name = "Foreign" };
        await modules.AddAsync(foreign);

        var entry = NewEntry(); // project = INT-LEAVE, module from GEO-999
        entry.ModuleId = foreign.Id;
        await Assert.ThrowsAsync<PostgresException>(() => _entries.AddAsync(entry));
    }

    [Fact]
    public async Task Burn_rows_carry_module_name_and_override_aware_rate()
    {
        var modules = new ModuleRepository(_dataSource);
        var rateCards = new RateCardRepository(_dataSource);
        var maintenance = new ProjectModule { Id = Guid.NewGuid(), ProjectId = SeedData.LeaveProjectId, Name = "Ongoing Maintenance" };
        await modules.AddAsync(maintenance);
        await rateCards.AddAsync(new ProjectRateCard
        {
            Id = Guid.NewGuid(), ProjectId = SeedData.LeaveProjectId, RoleId = SeedData.DeveloperRoleId, HourlyRate = 135.00m,
        });
        await modules.AddRateAsync(new ProjectModuleRate
        {
            Id = Guid.NewGuid(), ModuleId = maintenance.Id, RoleId = null, HourlyRate = 85.00m,
        });

        var onModule = NewEntry();
        onModule.ModuleId = maintenance.Id;
        await _entries.AddAsync(onModule);
        var unassigned = NewEntry(worked: 2m);
        unassigned.EntryDate = Day.AddDays(1);
        await _entries.AddAsync(unassigned);

        var rows = await _entries.GetBurnRowsAsync(SeedData.LeaveProjectId);
        Assert.Equal(2, rows.Count);

        var moduleRow = Assert.Single(rows, r => r.ModuleId == maintenance.Id);
        Assert.Equal("Ongoing Maintenance", moduleRow.ModuleName);
        Assert.False(moduleRow.ModuleDeleted);
        Assert.Equal(85.00m, moduleRow.ResolvedRate); // module-wide override beats the project rate

        var unassignedRow = Assert.Single(rows, r => r.ModuleId is null);
        Assert.Null(unassignedRow.ModuleName);
        Assert.Equal(135.00m, unassignedRow.ResolvedRate); // project rate

        await modules.SoftDeleteAsync(maintenance.Id);
        var afterDelete = await _entries.GetBurnRowsAsync(SeedData.LeaveProjectId);
        var deletedRow = Assert.Single(afterDelete, r => r.ModuleId == maintenance.Id);
        Assert.True(deletedRow.ModuleDeleted);
        Assert.Equal("Ongoing Maintenance", deletedRow.ModuleName);
    }

    [Fact]
    public async Task Period_upsert_closes_then_reopens_the_same_row()
    {
        var window = Crosscheck.Domain.SemiMonthlyPeriod.Containing(Day);
        var period = new TimesheetPeriod
        {
            Id = Guid.NewGuid(),
            PeriodStart = window.Start,
            PeriodEnd = window.End,
            Status = TimesheetPeriodStatus.Closed,
            ClosedById = SeedData.AdminEmployeeId,
            ClosedAt = DateTimeOffset.UtcNow,
        };
        await _periods.UpsertAsync(period);

        var closed = await _periods.GetByStartAsync(window.Start);
        Assert.Equal(TimesheetPeriodStatus.Closed, closed!.Status);
        Assert.Equal(SeedData.AdminEmployeeId, closed.ClosedById);
        Assert.NotNull(closed.ClosedAt);

        // Reopen via a second upsert on the same period_start (no duplicate row).
        period.Status = TimesheetPeriodStatus.Open;
        period.ClosedById = null;
        period.ClosedAt = null;
        await _periods.UpsertAsync(period);

        var reopened = await _periods.GetByStartAsync(window.Start);
        Assert.Equal(TimesheetPeriodStatus.Open, reopened!.Status);
        Assert.Null(reopened.ClosedById);

        var inRange = await _periods.GetInRangeAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        Assert.Single(inRange);
    }
}
