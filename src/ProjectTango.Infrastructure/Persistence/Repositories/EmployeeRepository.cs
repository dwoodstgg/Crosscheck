using Dapper;
using Npgsql;
using ProjectTango.Application.Employees;
using ProjectTango.Domain.Entities;

namespace ProjectTango.Infrastructure.Persistence.Repositories;

public class EmployeeRepository(NpgsqlDataSource dataSource) : IEmployeeRepository
{
    private const string SelectColumns =
        "SELECT id, entra_oid, email, display_name, employment_type, is_active, created_at, updated_at FROM employees";

    public async Task<Employee?> GetByEntraOidAsync(string entraOid, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Employee>(new CommandDefinition(
            $"{SelectColumns} WHERE entra_oid = @entraOid",
            new { entraOid },
            cancellationToken: cancellationToken));
    }

    public async Task<Employee?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Employee>(new CommandDefinition(
            $"{SelectColumns} WHERE email = @email::citext",
            new { email },
            cancellationToken: cancellationToken));
    }

    public async Task LinkEntraOidAsync(Guid employeeId, string entraOid, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE employees SET entra_oid = @entraOid, updated_at = now() WHERE id = @employeeId",
            new { employeeId, entraOid },
            cancellationToken: cancellationToken));
    }

    public async Task AddAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO employees (id, entra_oid, email, display_name, employment_type, is_active)
            VALUES (@Id, @EntraOid, @Email::citext, @DisplayName, @employmentType, @IsActive)
            """,
            new
            {
                employee.Id,
                employee.EntraOid,
                employee.Email,
                employee.DisplayName,
                employmentType = employee.EmploymentType.ToString().ToLowerInvariant(),
                employee.IsActive,
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<string>> GetRoleNamesAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var names = await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT r.name FROM roles r
            JOIN employee_roles er ON er.role_id = r.id
            WHERE er.employee_id = @employeeId
            ORDER BY r.name
            """,
            new { employeeId },
            cancellationToken: cancellationToken));
        return names.ToList();
    }
}
