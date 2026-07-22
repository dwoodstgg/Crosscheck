using Dapper;
using Npgsql;
using Crosscheck.Application.TimeEntries;
using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;

namespace Crosscheck.Infrastructure.Persistence.Repositories;

public class TimeEntryRepository(NpgsqlDataSource dataSource) : ITimeEntryRepository
{
    private const string SelectColumns =
        """
        SELECT id, project_id, module_id, employee_id, billing_role_id, entry_date,
               hours_worked, hours_billed, notes, is_billable, status, approved_by, approved_at
        FROM time_entries
        """;

    public async Task<TimeEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<TimeEntryRow>(new CommandDefinition(
            $"{SelectColumns} WHERE id = @id",
            new { id },
            cancellationToken: cancellationToken));
        return row is null ? null : ToEntity(row);
    }

    public async Task<TimeEntry?> GetByCellAsync(Guid employeeId, Guid projectId, Guid? moduleId, DateOnly date, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<TimeEntryRow>(new CommandDefinition(
            $"{SelectColumns} WHERE employee_id = @employeeId AND project_id = @projectId AND module_id IS NOT DISTINCT FROM @moduleId AND entry_date = @date",
            new { employeeId, projectId, moduleId, date },
            cancellationToken: cancellationToken));
        return row is null ? null : ToEntity(row);
    }

    public async Task<IReadOnlyList<TimeEntry>> GetForEmployeeRangeAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<TimeEntryRow>(new CommandDefinition(
            $"{SelectColumns} WHERE employee_id = @employeeId AND entry_date BETWEEN @from AND @to ORDER BY entry_date",
            new { employeeId, from, to },
            cancellationToken: cancellationToken));
        return rows.Select(ToEntity).ToList();
    }

    public async Task<IReadOnlyList<ApprovalEntry>> GetForProjectRangeAsync(Guid projectId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ApprovalRow>(new CommandDefinition(
            """
            SELECT te.id, te.project_id, te.employee_id, te.billing_role_id, te.entry_date,
                   te.hours_worked, te.hours_billed, te.notes, te.is_billable, te.status,
                   te.approved_by, te.approved_at,
                   e.display_name AS employee_name, r.display_name AS billing_role_name
            FROM time_entries te
            JOIN employees e ON e.id = te.employee_id
            JOIN roles r ON r.id = te.billing_role_id
            WHERE te.project_id = @projectId AND te.entry_date BETWEEN @from AND @to
            ORDER BY e.display_name, te.entry_date
            """,
            new { projectId, from, to },
            cancellationToken: cancellationToken));
        return rows.Select(row => new ApprovalEntry(ToEntity(row), row.EmployeeName!, row.BillingRoleName!)).ToList();
    }

    public async Task<IReadOnlyList<BurnRow>> GetBurnRowsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<BurnQueryRow>(new CommandDefinition(
            """
            SELECT te.id, te.entry_date, te.status, te.is_billable,
                   te.employee_id, e.display_name AS employee_name,
                   te.billing_role_id, r.display_name AS role_name,
                   te.module_id, m.name AS module_name,
                   (m.deleted_at IS NOT NULL) AS module_deleted,
                   te.hours_worked, te.hours_billed,
                   COALESCE(
                       (SELECT mr.hourly_rate FROM project_module_rates mr
                        WHERE mr.module_id = te.module_id
                          AND mr.role_id = te.billing_role_id
                          AND mr.deleted_at IS NULL),
                       (SELECT mr.hourly_rate FROM project_module_rates mr
                        WHERE mr.module_id = te.module_id
                          AND mr.role_id IS NULL
                          AND mr.deleted_at IS NULL),
                       (SELECT rc.hourly_rate FROM project_rate_cards rc
                        WHERE rc.project_id = te.project_id
                          AND rc.role_id = te.billing_role_id
                          AND rc.deleted_at IS NULL)) AS resolved_rate
            FROM time_entries te
            JOIN employees e ON e.id = te.employee_id
            JOIN roles r ON r.id = te.billing_role_id
            LEFT JOIN project_modules m ON m.id = te.module_id
            WHERE te.project_id = @projectId
            ORDER BY te.entry_date DESC
            """,
            new { projectId },
            cancellationToken: cancellationToken));

        return rows.Select(row => new BurnRow(
            row.Id, row.EntryDate, DbEnum.FromDb<TimeEntryStatus>(row.Status), row.IsBillable,
            row.EmployeeId, row.EmployeeName!, row.BillingRoleId, row.RoleName!,
            row.ModuleId, row.ModuleName, row.ModuleDeleted,
            row.HoursWorked, row.HoursBilled, row.ResolvedRate)).ToList();
    }

    public async Task AddAsync(TimeEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO time_entries
                (id, project_id, module_id, employee_id, billing_role_id, entry_date,
                 hours_worked, hours_billed, notes, is_billable, status, approved_by, approved_at)
            VALUES
                (@Id, @ProjectId, @ModuleId, @EmployeeId, @BillingRoleId, @EntryDate,
                 @HoursWorked, @HoursBilled, @Notes, @IsBillable, @Status, @ApprovedById, @ApprovedAt)
            """,
            ToParameters(entry),
            cancellationToken: cancellationToken));
    }

    public async Task UpdateAsync(TimeEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE time_entries SET
                billing_role_id = @BillingRoleId,
                hours_worked = @HoursWorked,
                hours_billed = @HoursBilled,
                notes = @Notes,
                is_billable = @IsBillable,
                status = @Status,
                approved_by = @ApprovedById,
                approved_at = @ApprovedAt,
                updated_at = now()
            WHERE id = @Id
            """,
            ToParameters(entry),
            cancellationToken: cancellationToken));
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM time_entries WHERE id = @id AND status = 'open'",
            new { id },
            cancellationToken: cancellationToken));
    }

    // module_id is written on insert only — the cell's identity, never changed by an update.
    private static object ToParameters(TimeEntry entry) => new
    {
        entry.Id,
        entry.ProjectId,
        entry.ModuleId,
        entry.EmployeeId,
        entry.BillingRoleId,
        entry.EntryDate,
        entry.HoursWorked,
        entry.HoursBilled,
        entry.Notes,
        entry.IsBillable,
        Status = DbEnum.ToDb(entry.Status),
        entry.ApprovedById,
        entry.ApprovedAt,
    };

    private static TimeEntry ToEntity(TimeEntryRow row) => new()
    {
        Id = row.Id,
        ProjectId = row.ProjectId,
        ModuleId = row.ModuleId,
        EmployeeId = row.EmployeeId,
        BillingRoleId = row.BillingRoleId,
        EntryDate = row.EntryDate,
        HoursWorked = row.HoursWorked,
        HoursBilled = row.HoursBilled,
        Notes = row.Notes,
        IsBillable = row.IsBillable,
        Status = DbEnum.FromDb<TimeEntryStatus>(row.Status),
        ApprovedById = row.ApprovedBy,
        ApprovedAt = row.ApprovedAt,
    };

    private class TimeEntryRow
    {
        public Guid Id { get; set; }
        public Guid ProjectId { get; set; }
        public Guid? ModuleId { get; set; }
        public Guid EmployeeId { get; set; }
        public Guid BillingRoleId { get; set; }
        public DateOnly EntryDate { get; set; }
        public decimal HoursWorked { get; set; }
        public decimal HoursBilled { get; set; }
        public string? Notes { get; set; }
        public bool IsBillable { get; set; }
        public string Status { get; set; } = "open";
        public Guid? ApprovedBy { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
    }

    private sealed class ApprovalRow : TimeEntryRow
    {
        public string? EmployeeName { get; set; }
        public string? BillingRoleName { get; set; }
    }

    private sealed class BurnQueryRow
    {
        public Guid Id { get; set; }
        public DateOnly EntryDate { get; set; }
        public string Status { get; set; } = "open";
        public bool IsBillable { get; set; }
        public Guid EmployeeId { get; set; }
        public string? EmployeeName { get; set; }
        public Guid BillingRoleId { get; set; }
        public string? RoleName { get; set; }
        public Guid? ModuleId { get; set; }
        public string? ModuleName { get; set; }
        public bool ModuleDeleted { get; set; }
        public decimal HoursWorked { get; set; }
        public decimal HoursBilled { get; set; }
        public decimal? ResolvedRate { get; set; }
    }
}
