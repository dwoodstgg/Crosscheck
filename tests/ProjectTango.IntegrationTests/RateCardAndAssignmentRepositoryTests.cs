using Dapper;
using Npgsql;
using ProjectTango.Domain.Entities;
using ProjectTango.Infrastructure.Persistence;
using ProjectTango.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace ProjectTango.IntegrationTests;

public sealed class RateCardAndAssignmentRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private NpgsqlDataSource _dataSource = null!;
    private RateCardRepository _rateCards = null!;
    private AssignmentRepository _assignments = null!;

    public async Task InitializeAsync()
    {
        DapperConfig.Apply();
        await _postgres.StartAsync();
        DatabaseMigrator.MigrateToLatest(_postgres.GetConnectionString());
        _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        _rateCards = new RateCardRepository(_dataSource);
        _assignments = new AssignmentRepository(_dataSource);
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Rate_roundtrip_and_resolve_by_project_and_role()
    {
        await _rateCards.AddAsync(new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = SeedData.LeaveProjectId,
            RoleId = SeedData.DeveloperRoleId,
            HourlyRate = 150.00m,
        });

        Assert.Equal(150.00m, await _rateCards.ResolveAsync(SeedData.LeaveProjectId, SeedData.DeveloperRoleId));
        // A different billing role has no rate.
        Assert.Null(await _rateCards.ResolveAsync(SeedData.LeaveProjectId, SeedData.ProjectManagerRoleId));

        var summary = Assert.Single(await _rateCards.GetForProjectAsync(SeedData.LeaveProjectId));
        Assert.Equal("Developer", summary.RoleName);
    }

    [Fact]
    public async Task Duplicate_live_rate_for_role_is_rejected_by_the_database()
    {
        await _rateCards.AddAsync(new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = SeedData.LeaveProjectId,
            RoleId = SeedData.ProjectManagerRoleId,
            HourlyRate = 175.00m,
        });

        // A live row already exists for (project, role) → the partial unique index fires.
        await Assert.ThrowsAsync<PostgresException>(() => _rateCards.AddAsync(new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = SeedData.LeaveProjectId,
            RoleId = SeedData.ProjectManagerRoleId,
            HourlyRate = 185.00m,
        }));
    }

    [Fact]
    public async Task HasInvoicedTime_reflects_invoiced_entries_per_role()
    {
        Assert.False(await _rateCards.HasInvoicedTimeAsync(
            SeedData.LeaveProjectId, SeedData.DeveloperRoleId));

        await InsertInvoicedEntryAsync(new DateOnly(2026, 3, 15));

        Assert.True(await _rateCards.HasInvoicedTimeAsync(
            SeedData.LeaveProjectId, SeedData.DeveloperRoleId));
        // A different billing role is unaffected.
        Assert.False(await _rateCards.HasInvoicedTimeAsync(
            SeedData.LeaveProjectId, SeedData.ProjectManagerRoleId));
    }

    [Fact]
    public async Task GetForProject_flags_rows_with_invoiced_time()
    {
        await _rateCards.AddAsync(new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = SeedData.LeaveProjectId,
            RoleId = SeedData.DeveloperRoleId,
            HourlyRate = 150.00m,
        });
        await InsertInvoicedEntryAsync(new DateOnly(2026, 5, 1));

        var summary = Assert.Single(await _rateCards.GetForProjectAsync(SeedData.LeaveProjectId));
        Assert.True(summary.HasBilledTime);
    }

    [Fact]
    public async Task Correct_updates_amount_in_place()
    {
        var rate = new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = SeedData.LeaveProjectId,
            RoleId = SeedData.DeveloperRoleId,
            HourlyRate = 150.00m,
        };
        await _rateCards.AddAsync(rate);

        await _rateCards.CorrectAsync(rate.Id, 170.00m);

        Assert.Equal(170.00m, await _rateCards.ResolveAsync(SeedData.LeaveProjectId, SeedData.DeveloperRoleId));
    }

    [Fact]
    public async Task SoftDelete_hides_row_and_frees_it_for_reuse()
    {
        var mistake = new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = SeedData.LeaveProjectId,
            RoleId = SeedData.DeveloperRoleId,
            HourlyRate = 99.00m,
        };
        await _rateCards.AddAsync(mistake);
        await _rateCards.SoftDeleteAsync(mistake.Id);

        // Removed row is invisible to every read path.
        Assert.Null(await _rateCards.GetByIdAsync(mistake.Id));
        Assert.Empty(await _rateCards.GetForProjectAsync(SeedData.LeaveProjectId));
        Assert.Null(await _rateCards.ResolveAsync(SeedData.LeaveProjectId, SeedData.DeveloperRoleId));

        // The partial unique index ignores soft-deleted rows, so the role can be priced again.
        await _rateCards.AddAsync(new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = SeedData.LeaveProjectId,
            RoleId = SeedData.DeveloperRoleId,
            HourlyRate = 135.00m,
        });
        Assert.Equal(135.00m, await _rateCards.ResolveAsync(SeedData.LeaveProjectId, SeedData.DeveloperRoleId));
    }

    [Fact]
    public async Task Assignment_roundtrip_and_unique_row_per_person()
    {
        var assignment = new ProjectAssignment
        {
            Id = Guid.NewGuid(),
            ProjectId = SeedData.LeaveProjectId,
            EmployeeId = SeedData.AdminEmployeeId,
            DefaultBillingRoleId = SeedData.DeveloperRoleId,
        };
        await _assignments.AddAsync(assignment);

        var fetched = await _assignments.GetByProjectAndEmployeeAsync(SeedData.LeaveProjectId, SeedData.AdminEmployeeId);
        Assert.NotNull(fetched);
        Assert.Equal(assignment.Id, fetched.Id);
        Assert.True(fetched.IsActive);

        fetched.EndDate = new DateOnly(2026, 7, 8);
        await _assignments.UpdateAsync(fetched);
        var ended = await _assignments.GetAsync(assignment.Id);
        Assert.False(ended!.IsActive);

        var summaries = await _assignments.GetForProjectAsync(SeedData.LeaveProjectId);
        var summary = Assert.Single(summaries);
        Assert.Equal("Don Woods", summary.EmployeeName);
        Assert.Equal("Developer", summary.DefaultRoleName);

        // unique (project_id, employee_id)
        await Assert.ThrowsAsync<PostgresException>(() => _assignments.AddAsync(new ProjectAssignment
        {
            Id = Guid.NewGuid(),
            ProjectId = SeedData.LeaveProjectId,
            EmployeeId = SeedData.AdminEmployeeId,
        }));
    }

    private async Task InsertInvoicedEntryAsync(DateOnly date)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO time_entries
                (id, project_id, employee_id, billing_role_id, entry_date, hours_worked, hours_billed, status)
            VALUES (@id, @projectId, @employeeId, @roleId, @date, 8, 8, 'invoiced')
            """,
            new
            {
                id = Guid.NewGuid(),
                projectId = SeedData.LeaveProjectId,
                employeeId = SeedData.AdminEmployeeId,
                roleId = SeedData.DeveloperRoleId,
                date,
            });
    }
}
