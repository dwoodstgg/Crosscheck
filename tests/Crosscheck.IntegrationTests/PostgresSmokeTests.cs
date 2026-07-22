using Dapper;
using Npgsql;
using Crosscheck.Application.Roles;
using Crosscheck.Domain;
using Crosscheck.Infrastructure.Persistence;
using Crosscheck.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace Crosscheck.IntegrationTests;

public sealed class PostgresSmokeTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private NpgsqlDataSource? _dataSource;

    public Task InitializeAsync()
    {
        DapperConfig.Apply();
        return _postgres.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    private NpgsqlDataSource MigratedDataSource()
    {
        DatabaseMigrator.MigrateToLatest(_postgres.GetConnectionString());
        return _dataSource ??= NpgsqlDataSource.Create(_postgres.GetConnectionString());
    }

    [Fact]
    public async Task Migrations_apply_and_seed_expected_rows()
    {
        var dataSource = MigratedDataSource();
        await using var connection = await dataSource.OpenConnectionAsync();

        var roleCount = await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM roles");
        Assert.Equal(4, roleCount);

        // Bootstrap Admin exists, holds the Admin role, and has no Entra link yet.
        var adminEmail = await connection.ExecuteScalarAsync<string>(
            """
            SELECT e.email FROM employees e
            JOIN employee_roles er ON er.employee_id = e.id
            JOIN roles r ON r.id = er.role_id
            WHERE r.is_system_admin AND e.entra_oid IS NULL
            """);
        Assert.Equal(SeedData.AdminEmail, adminEmail);

        // citext: email lookups are case-insensitive — but only when the parameter is cast
        // to citext. Npgsql sends parameters as text, and citext = text degrades to
        // case-SENSITIVE text equality. Every email lookup must use @email::citext.
        var byUpperEmail = await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM employees WHERE email = @email::citext",
            new { email = SeedData.AdminEmail.ToUpperInvariant() });
        Assert.Equal(1, byUpperEmail);

        // The internal client is seeded; no projects ship with the seed (the old INT-LEAVE
        // project was retired by 0018 — leave is derived on the timesheet, never logged).
        var internalClientCount = await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM clients WHERE is_internal");
        Assert.Equal(1, internalClientCount);

        var projectCount = await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM projects");
        Assert.Equal(0, projectCount);
    }

    [Fact]
    public async Task RoleRepository_maps_seeded_roles()
    {
        var dataSource = MigratedDataSource();
        IRoleRepository repository = new RoleRepository(dataSource);

        var roles = await repository.GetAllAsync();

        Assert.Equal(4, roles.Count);
        var admin = Assert.Single(roles, r => r.Name == RoleNames.Admin);
        Assert.True(admin.IsSystemAdmin);
        Assert.False(admin.IsBillable);
        Assert.True(roles.Single(r => r.Name == RoleNames.Developer).IsBillable);
    }

    [Fact]
    public async Task Migrations_are_idempotent()
    {
        var dataSource = MigratedDataSource();

        // Second run must be a no-op (journaled), not a duplicate-object failure.
        DatabaseMigrator.MigrateToLatest(_postgres.GetConnectionString());

        await using var connection = await dataSource.OpenConnectionAsync();
        var roleCount = await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM roles");
        Assert.Equal(4, roleCount);
    }
}
