using Dapper;
using Npgsql;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;
using ProjectTango.Infrastructure.Persistence;
using ProjectTango.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace ProjectTango.IntegrationTests;

public sealed class EmployeeRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private NpgsqlDataSource _dataSource = null!;
    private EmployeeRepository _repository = null!;

    public async Task InitializeAsync()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await _postgres.StartAsync();
        DatabaseMigrator.MigrateToLatest(_postgres.GetConnectionString());
        _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        _repository = new EmployeeRepository(_dataSource);
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task GetByEmail_is_case_insensitive_and_maps_all_columns()
    {
        var employee = await _repository.GetByEmailAsync(SeedData.AdminEmail.ToUpperInvariant());

        Assert.NotNull(employee);
        Assert.Equal(SeedData.AdminEmployeeId, employee.Id);
        Assert.Null(employee.EntraOid);
        Assert.Equal("Don Woods", employee.DisplayName);
        Assert.Equal(EmploymentType.Employee, employee.EmploymentType);
        Assert.True(employee.IsActive);
    }

    [Fact]
    public async Task LinkEntraOid_makes_employee_findable_by_oid()
    {
        await _repository.LinkEntraOidAsync(SeedData.AdminEmployeeId, "test-oid-123");

        var byOid = await _repository.GetByEntraOidAsync("test-oid-123");

        Assert.NotNull(byOid);
        Assert.Equal(SeedData.AdminEmployeeId, byOid.Id);
    }

    [Fact]
    public async Task Add_roundtrips_a_subcontractor()
    {
        var subcontractor = new Employee
        {
            Id = Guid.NewGuid(),
            EntraOid = "sub-oid",
            Email = "sub@thegeospatialgroup.com",
            DisplayName = "Sub Contractor",
            EmploymentType = EmploymentType.Subcontractor,
        };

        await _repository.AddAsync(subcontractor);
        var fetched = await _repository.GetByEmailAsync("SUB@thegeospatialgroup.com");

        Assert.NotNull(fetched);
        Assert.Equal(EmploymentType.Subcontractor, fetched.EmploymentType);
    }

    [Fact]
    public async Task GetRoleNames_returns_admin_for_seeded_bootstrap_user()
    {
        var roles = await _repository.GetRoleNamesAsync(SeedData.AdminEmployeeId);

        Assert.Equal([RoleNames.Admin], roles);
    }
}
