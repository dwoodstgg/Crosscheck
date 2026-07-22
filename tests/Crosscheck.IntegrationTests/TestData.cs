using Dapper;
using Npgsql;
using Crosscheck.Infrastructure.Persistence;

namespace Crosscheck.IntegrationTests;

/// <summary>Scratch rows for integration tests. The seed ships no projects (the old
/// INT-LEAVE project was retired by 0018), so tests that need one insert their own here,
/// owned by the seeded internal client and managed by the bootstrap Admin.</summary>
internal static class TestData
{
    public static async Task<Guid> InsertProjectAsync(
        NpgsqlDataSource dataSource, string code = "TEST-001", string name = "Test Project")
    {
        var id = Guid.NewGuid();
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO projects (id, client_id, name, code, status, project_manager_id)
            VALUES (@id, @clientId, @name, @code, 'active', @pm)
            """,
            new { id, clientId = SeedData.InternalClientId, name, code, pm = SeedData.AdminEmployeeId });
        return id;
    }
}
