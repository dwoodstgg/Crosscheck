using Npgsql;
using Waypoint.Domain.Entities;
using Waypoint.Domain.Enums;
using Waypoint.Infrastructure.Persistence;
using Waypoint.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace Waypoint.IntegrationTests;

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

        var byCell = await _entries.GetByCellAsync(SeedData.AdminEmployeeId, SeedData.LeaveProjectId, Day);
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
    public async Task Period_upsert_closes_then_reopens_the_same_row()
    {
        var window = Waypoint.Domain.SemiMonthlyPeriod.Containing(Day);
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
