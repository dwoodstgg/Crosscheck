using Dapper;
using Npgsql;
using Waypoint.Application.Projects;
using Waypoint.Domain.Entities;

namespace Waypoint.Infrastructure.Persistence.Repositories;

public class AssignmentRepository(NpgsqlDataSource dataSource) : IAssignmentRepository
{
    private const string SelectColumns =
        """
        SELECT a.id, a.project_id, a.employee_id, a.default_billing_role_id, a.end_date
        FROM project_assignments a
        """;

    public async Task<IReadOnlyList<AssignmentSummary>> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<AssignmentRow>(new CommandDefinition(
            """
            SELECT a.id, a.project_id, a.employee_id, a.default_billing_role_id, a.end_date,
                   e.display_name AS employee_name, r.display_name AS default_role_name,
                   EXISTS (
                       SELECT 1 FROM time_entries te
                       WHERE te.project_id = a.project_id AND te.employee_id = a.employee_id
                   ) AS has_time_entries
            FROM project_assignments a
            JOIN employees e ON e.id = a.employee_id
            LEFT JOIN roles r ON r.id = a.default_billing_role_id
            WHERE a.project_id = @projectId
            ORDER BY e.display_name
            """,
            new { projectId },
            cancellationToken: cancellationToken));
        return rows.Select(row => new AssignmentSummary(ToEntity(row), row.EmployeeName!, row.DefaultRoleName, row.HasTimeEntries)).ToList();
    }

    public async Task<IReadOnlyList<EmployeeAssignment>> GetForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<AssignmentRow>(new CommandDefinition(
            """
            SELECT a.id, a.project_id, a.employee_id, a.default_billing_role_id, a.end_date,
                   p.code AS project_code, p.name AS project_name, c.name AS client_name
            FROM project_assignments a
            JOIN projects p ON p.id = a.project_id
            JOIN clients c ON c.id = p.client_id
            WHERE a.employee_id = @employeeId
            ORDER BY p.code
            """,
            new { employeeId },
            cancellationToken: cancellationToken));
        return rows.Select(row => new EmployeeAssignment(ToEntity(row), row.ProjectCode!, row.ProjectName!, row.ClientName!)).ToList();
    }

    public async Task<ProjectAssignment?> GetAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<AssignmentRow>(new CommandDefinition(
            $"{SelectColumns} WHERE a.id = @assignmentId",
            new { assignmentId },
            cancellationToken: cancellationToken));
        return row is null ? null : ToEntity(row);
    }

    public async Task<ProjectAssignment?> GetByProjectAndEmployeeAsync(Guid projectId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<AssignmentRow>(new CommandDefinition(
            $"{SelectColumns} WHERE a.project_id = @projectId AND a.employee_id = @employeeId",
            new { projectId, employeeId },
            cancellationToken: cancellationToken));
        return row is null ? null : ToEntity(row);
    }

    public async Task<bool> HasTimeEntriesAsync(Guid projectId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS (
                SELECT 1 FROM time_entries
                WHERE project_id = @projectId AND employee_id = @employeeId
            )
            """,
            new { projectId, employeeId },
            cancellationToken: cancellationToken));
    }

    public async Task AddAsync(ProjectAssignment assignment, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO project_assignments (id, project_id, employee_id, default_billing_role_id, end_date)
            VALUES (@Id, @ProjectId, @EmployeeId, @DefaultBillingRoleId, @EndDate)
            """,
            new
            {
                assignment.Id,
                assignment.ProjectId,
                assignment.EmployeeId,
                assignment.DefaultBillingRoleId,
                assignment.EndDate,
            },
            cancellationToken: cancellationToken));
    }

    public async Task UpdateAsync(ProjectAssignment assignment, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE project_assignments SET
                default_billing_role_id = @DefaultBillingRoleId,
                end_date = @EndDate
            WHERE id = @Id
            """,
            new
            {
                assignment.Id,
                assignment.DefaultBillingRoleId,
                assignment.EndDate,
            },
            cancellationToken: cancellationToken));
    }

    public async Task DeleteAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM project_assignments WHERE id = @assignmentId",
            new { assignmentId },
            cancellationToken: cancellationToken));
    }

    private static ProjectAssignment ToEntity(AssignmentRow row) => new()
    {
        Id = row.Id,
        ProjectId = row.ProjectId,
        EmployeeId = row.EmployeeId,
        DefaultBillingRoleId = row.DefaultBillingRoleId,
        EndDate = row.EndDate,
    };

    private sealed class AssignmentRow
    {
        public Guid Id { get; set; }
        public Guid ProjectId { get; set; }
        public Guid EmployeeId { get; set; }
        public Guid? DefaultBillingRoleId { get; set; }
        public DateOnly? EndDate { get; set; }
        public string? EmployeeName { get; set; }
        public string? DefaultRoleName { get; set; }
        public bool HasTimeEntries { get; set; }
        public string? ProjectCode { get; set; }
        public string? ProjectName { get; set; }
        public string? ClientName { get; set; }
    }
}
