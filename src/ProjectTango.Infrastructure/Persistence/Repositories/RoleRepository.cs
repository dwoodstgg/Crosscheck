using Dapper;
using Npgsql;
using ProjectTango.Application.Roles;
using ProjectTango.Domain.Entities;

namespace ProjectTango.Infrastructure.Persistence.Repositories;

public class RoleRepository(NpgsqlDataSource dataSource) : IRoleRepository
{
    public async Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var roles = await connection.QueryAsync<Role>(new CommandDefinition(
            "SELECT id, name, is_billable, is_system_admin FROM roles ORDER BY name",
            cancellationToken: cancellationToken));

        return roles.ToList();
    }

    public async Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<Role>(new CommandDefinition(
            "SELECT id, name, is_billable, is_system_admin FROM roles WHERE id = @id",
            new { id },
            cancellationToken: cancellationToken));
    }
}
