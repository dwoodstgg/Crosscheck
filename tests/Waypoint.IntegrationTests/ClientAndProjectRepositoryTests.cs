using Dapper;
using Npgsql;
using Waypoint.Domain.Entities;
using Waypoint.Domain.Enums;
using Waypoint.Infrastructure.Persistence;
using Waypoint.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace Waypoint.IntegrationTests;

public sealed class ClientAndProjectRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private NpgsqlDataSource _dataSource = null!;
    private ClientRepository _clients = null!;
    private ProjectRepository _projects = null!;

    public async Task InitializeAsync()
    {
        DapperConfig.Apply();
        await _postgres.StartAsync();
        DatabaseMigrator.MigrateToLatest(_postgres.GetConnectionString());
        _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        _clients = new ClientRepository(_dataSource);
        _projects = new ProjectRepository(_dataSource);
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Client_roundtrips_including_jsonb_billing_address()
    {
        var client = new Client
        {
            Id = Guid.NewGuid(),
            Name = "MDEQ",
            BillingContactName = "Jane Biller",
            BillingContactEmail = "ap@mdeq.example",
            BillingAddress = new BillingAddress
            {
                Line1 = "515 E Amite St",
                City = "Jackson",
                State = "MS",
                PostalCode = "39201",
            },
            PaymentTermsDays = 45,
        };

        await _clients.AddAsync(client);
        var fetched = await _clients.GetByIdAsync(client.Id);

        Assert.NotNull(fetched);
        Assert.Equal("MDEQ", fetched.Name);
        Assert.Equal(45, fetched.PaymentTermsDays);
        Assert.NotNull(fetched.BillingAddress);
        Assert.Equal("Jackson", fetched.BillingAddress.City);
        Assert.Equal("39201", fetched.BillingAddress.PostalCode);
    }

    [Fact]
    public async Task Seeded_internal_client_is_flagged()
    {
        var internalClient = await _clients.GetByIdAsync(SeedData.InternalClientId);

        Assert.NotNull(internalClient);
        Assert.True(internalClient.IsInternal);
        Assert.Equal(SeedData.InternalClientName, internalClient.Name);
    }

    [Fact]
    public async Task Project_status_on_hold_roundtrips_snake_case()
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            ClientId = SeedData.InternalClientId,
            Name = "Enum Test",
            Code = "ENUM-01",
            ProjectManagerId = SeedData.AdminEmployeeId,
        };
        await _projects.AddAsync(project);

        await _projects.SetStatusAsync(project.Id, ProjectStatus.OnHold);

        var fetched = await _projects.GetByIdAsync(project.Id);
        Assert.NotNull(fetched);
        Assert.Equal(ProjectStatus.OnHold, fetched.Status);

        await using var connection = await _dataSource.OpenConnectionAsync();
        var raw = await connection.ExecuteScalarAsync<string>(
            "SELECT status FROM projects WHERE id = @id", new { project.Id });
        Assert.Equal("on_hold", raw);
    }

    [Fact]
    public async Task GetAll_joins_client_and_manager_names()
    {
        var all = await _projects.GetAllAsync();

        var leave = Assert.Single(all, s => s.Project.Code == SeedData.LeaveProjectCode);
        Assert.Equal(SeedData.InternalClientName, leave.ClientName);
        Assert.Equal("Don Woods", leave.ProjectManagerName);
        Assert.Equal(ProjectStatus.Active, leave.Project.Status);
    }

    [Fact]
    public async Task GetByCode_finds_seeded_leave_project()
    {
        var project = await _projects.GetByCodeAsync(SeedData.LeaveProjectCode);

        Assert.NotNull(project);
        Assert.Equal(SeedData.LeaveProjectId, project.Id);
    }

    [Fact]
    public async Task Project_billing_overrides_roundtrip_including_jsonb_address()
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            ClientId = SeedData.InternalClientId,
            Name = "MDWFP Fisheries",
            Code = "MDWFP-01",
            ProjectManagerId = SeedData.AdminEmployeeId,
            BillingContactName = "Pat Contact",
            BillingContactEmail = "pat@mdwfp.example",
            BillingAddress = new BillingAddress { Line1 = "1505 Eastover Dr", City = "Jackson", State = "MS", PostalCode = "39211" },
            PaymentTermsDays = 60,
        };
        await _projects.AddAsync(project);

        var fetched = await _projects.GetByIdAsync(project.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Pat Contact", fetched!.BillingContactName);
        Assert.Equal("pat@mdwfp.example", fetched.BillingContactEmail);
        Assert.Equal(60, fetched.PaymentTermsDays);
        Assert.Equal("Jackson", fetched.BillingAddress!.City);

        // Clearing the overrides back to null (inherit client) persists as null.
        fetched.BillingContactName = null;
        fetched.BillingAddress = null;
        fetched.PaymentTermsDays = null;
        await _projects.UpdateAsync(fetched);

        var cleared = await _projects.GetByIdAsync(project.Id);
        Assert.Null(cleared!.BillingContactName);
        Assert.Null(cleared.BillingAddress);
        Assert.Null(cleared.PaymentTermsDays);
    }

    [Fact]
    public async Task Client_payment_terms_may_be_null()
    {
        var client = new Client { Id = Guid.NewGuid(), Name = "No-terms client", PaymentTermsDays = null };
        await _clients.AddAsync(client);

        var fetched = await _clients.GetByIdAsync(client.Id);
        Assert.NotNull(fetched);
        Assert.Null(fetched!.PaymentTermsDays);
    }
}
