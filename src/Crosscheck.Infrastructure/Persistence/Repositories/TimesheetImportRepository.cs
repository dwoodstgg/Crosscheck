using Dapper;
using Npgsql;
using Crosscheck.Application.Imports;
using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;

namespace Crosscheck.Infrastructure.Persistence.Repositories;

public class TimesheetImportRepository(NpgsqlDataSource dataSource) : ITimesheetImportRepository
{
    private const string SelectImportColumns =
        """
        SELECT id, employee_id, file_name, year, status, parse_warnings,
               uploaded_by, uploaded_at, committed_by, committed_at, rolled_back_by, rolled_back_at
        FROM timesheet_imports
        """;

    private const string SelectRowColumns =
        """
        SELECT id, import_id, sheet_name, sheet_row, project_label, entry_date, hours,
               description, project_id, billing_role_id, included
        FROM timesheet_import_rows
        """;

    public async Task AddAsync(TimesheetImport import, IReadOnlyList<TimesheetImportRow> rows, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO timesheet_imports
                (id, employee_id, file_name, year, status, parse_warnings, uploaded_by, uploaded_at)
            VALUES
                (@Id, @EmployeeId, @FileName, @Year, @Status, @ParseWarnings, @UploadedById, @UploadedAt)
            """,
            new
            {
                import.Id,
                import.EmployeeId,
                import.FileName,
                import.Year,
                Status = DbEnum.ToDb(import.Status),
                import.ParseWarnings,
                import.UploadedById,
                import.UploadedAt,
            },
            transaction, cancellationToken: cancellationToken));

        await InsertRowsAsync(connection, transaction, rows, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<TimesheetImport?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ImportRow>(new CommandDefinition(
            $"{SelectImportColumns} WHERE id = @id",
            new { id },
            cancellationToken: cancellationToken));
        return row is null ? null : ToImport(row);
    }

    public async Task<IReadOnlyList<TimesheetImportSummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ImportSummaryRow>(new CommandDefinition(
            """
            SELECT i.id, i.employee_id, i.file_name, i.year, i.status, i.parse_warnings,
                   i.uploaded_by, i.uploaded_at, i.committed_by, i.committed_at,
                   i.rolled_back_by, i.rolled_back_at,
                   e.display_name AS employee_name, u.display_name AS uploaded_by_name,
                   COALESCE(r.row_count, 0) AS row_count, COALESCE(r.total_hours, 0) AS total_hours
            FROM timesheet_imports i
            JOIN employees e ON e.id = i.employee_id
            JOIN employees u ON u.id = i.uploaded_by
            LEFT JOIN (SELECT import_id, count(*) AS row_count, sum(hours) AS total_hours
                       FROM timesheet_import_rows WHERE included GROUP BY import_id) r
                 ON r.import_id = i.id
            ORDER BY i.uploaded_at DESC
            """,
            cancellationToken: cancellationToken));
        return rows.Select(row => new TimesheetImportSummary(
            ToImport(row), row.EmployeeName!, row.UploadedByName!, row.RowCount, row.TotalHours)).ToList();
    }

    public async Task<IReadOnlyList<TimesheetImportRow>> GetRowsAsync(Guid importId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<StagingRow>(new CommandDefinition(
            $"{SelectRowColumns} WHERE import_id = @importId ORDER BY entry_date, sheet_row",
            new { importId },
            cancellationToken: cancellationToken));
        return rows.Select(ToStagingEntity).ToList();
    }

    public async Task<TimesheetImportRow?> GetRowAsync(Guid rowId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<StagingRow>(new CommandDefinition(
            $"{SelectRowColumns} WHERE id = @rowId",
            new { rowId },
            cancellationToken: cancellationToken));
        return row is null ? null : ToStagingEntity(row);
    }

    public async Task UpdateRowAsync(TimesheetImportRow row, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE timesheet_import_rows SET
                entry_date = @EntryDate,
                hours = @Hours,
                description = @Description,
                project_id = @ProjectId,
                billing_role_id = @BillingRoleId,
                included = @Included,
                updated_at = now()
            WHERE id = @Id
            """,
            new { row.Id, row.EntryDate, row.Hours, row.Description, row.ProjectId, row.BillingRoleId, row.Included },
            cancellationToken: cancellationToken));
    }

    public async Task SetRowsIncludedAsync(IReadOnlyCollection<Guid> rowIds, bool included, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE timesheet_import_rows SET included = @included, updated_at = now() WHERE id = ANY(@rowIds)",
            new { included, rowIds = rowIds.ToArray() },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<TimesheetImportMapping>> GetMappingsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<TimesheetImportMapping>(new CommandDefinition(
            """
            SELECT id, label, project_id, created_by AS created_by_id, created_at
            FROM timesheet_import_mappings
            ORDER BY label
            """,
            cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task CommitAsync(
        TimesheetImport import,
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyList<ProjectAssignment> newAssignments,
        IReadOnlyList<TimesheetImportMapping> newMappings,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var assignment in newAssignments)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO project_assignments (id, project_id, employee_id, default_billing_role_id, end_date)
                VALUES (@Id, @ProjectId, @EmployeeId, @DefaultBillingRoleId, @EndDate)
                """,
                assignment,
                transaction, cancellationToken: cancellationToken));
        }

        foreach (var entry in entries)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO time_entries
                    (id, project_id, module_id, employee_id, billing_role_id, entry_date,
                     hours_worked, hours_billed, notes, is_billable, status, approved_by, approved_at,
                     import_id)
                VALUES
                    (@Id, @ProjectId, @ModuleId, @EmployeeId, @BillingRoleId, @EntryDate,
                     @HoursWorked, @HoursBilled, @Notes, @IsBillable, @Status, @ApprovedById, @ApprovedAt,
                     @ImportId)
                """,
                new
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
                    entry.ImportId,
                },
                transaction, cancellationToken: cancellationToken));
        }

        foreach (var mapping in newMappings)
        {
            // A label maps to one project: re-mapping replaces the old target.
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO timesheet_import_mappings (id, label, project_id, created_by, created_at)
                VALUES (@Id, @Label, @ProjectId, @CreatedById, @CreatedAt)
                ON CONFLICT ((lower(label))) DO UPDATE
                    SET project_id = EXCLUDED.project_id,
                        created_by = EXCLUDED.created_by,
                        created_at = EXCLUDED.created_at
                """,
                new { mapping.Id, mapping.Label, mapping.ProjectId, mapping.CreatedById, mapping.CreatedAt },
                transaction, cancellationToken: cancellationToken));
        }

        await UpdateImportStatusAsync(connection, transaction, import, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> HasInvoicedEntriesAsync(Guid importId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM time_entries WHERE import_id = @importId AND status = 'invoiced')",
            new { importId },
            cancellationToken: cancellationToken));
    }

    public async Task RollbackAsync(TimesheetImport import, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The service verifies nothing is invoiced; the guard here backs that up.
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM time_entries WHERE import_id = @Id AND status <> 'invoiced'",
            new { import.Id },
            transaction, cancellationToken: cancellationToken));

        await UpdateImportStatusAsync(connection, transaction, import, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid importId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        // Discard of a pending import — staging rows go with it (ON DELETE CASCADE); the
        // status guard keeps a committed import (which has entries) intact.
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM timesheet_imports WHERE id = @importId AND status = 'pending'",
            new { importId },
            cancellationToken: cancellationToken));
    }

    private static async Task InsertRowsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IReadOnlyList<TimesheetImportRow> rows, CancellationToken cancellationToken)
    {
        foreach (var row in rows)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO timesheet_import_rows
                    (id, import_id, sheet_name, sheet_row, project_label, entry_date, hours,
                     description, project_id, billing_role_id, included)
                VALUES
                    (@Id, @ImportId, @SheetName, @SheetRow, @ProjectLabel, @EntryDate, @Hours,
                     @Description, @ProjectId, @BillingRoleId, @Included)
                """,
                row,
                transaction, cancellationToken: cancellationToken));
        }
    }

    private static async Task UpdateImportStatusAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, TimesheetImport import, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE timesheet_imports SET
                status = @Status,
                committed_by = @CommittedById,
                committed_at = @CommittedAt,
                rolled_back_by = @RolledBackById,
                rolled_back_at = @RolledBackAt
            WHERE id = @Id
            """,
            new
            {
                import.Id,
                Status = DbEnum.ToDb(import.Status),
                import.CommittedById,
                import.CommittedAt,
                import.RolledBackById,
                import.RolledBackAt,
            },
            transaction, cancellationToken: cancellationToken));
    }

    private static TimesheetImport ToImport(ImportRow row) => new()
    {
        Id = row.Id,
        EmployeeId = row.EmployeeId,
        FileName = row.FileName,
        Year = row.Year,
        Status = DbEnum.FromDb<TimesheetImportStatus>(row.Status),
        ParseWarnings = row.ParseWarnings,
        UploadedById = row.UploadedBy,
        UploadedAt = row.UploadedAt,
        CommittedById = row.CommittedBy,
        CommittedAt = row.CommittedAt,
        RolledBackById = row.RolledBackBy,
        RolledBackAt = row.RolledBackAt,
    };

    private static TimesheetImportRow ToStagingEntity(StagingRow row) => new()
    {
        Id = row.Id,
        ImportId = row.ImportId,
        SheetName = row.SheetName,
        SheetRow = row.SheetRow,
        ProjectLabel = row.ProjectLabel,
        EntryDate = row.EntryDate,
        Hours = row.Hours,
        Description = row.Description,
        ProjectId = row.ProjectId,
        BillingRoleId = row.BillingRoleId,
        Included = row.Included,
    };

    private class ImportRow
    {
        public Guid Id { get; set; }
        public Guid EmployeeId { get; set; }
        public string FileName { get; set; } = "";
        public int Year { get; set; }
        public string Status { get; set; } = "pending";
        public string? ParseWarnings { get; set; }
        public Guid UploadedBy { get; set; }
        public DateTimeOffset UploadedAt { get; set; }
        public Guid? CommittedBy { get; set; }
        public DateTimeOffset? CommittedAt { get; set; }
        public Guid? RolledBackBy { get; set; }
        public DateTimeOffset? RolledBackAt { get; set; }
    }

    private sealed class ImportSummaryRow : ImportRow
    {
        public string? EmployeeName { get; set; }
        public string? UploadedByName { get; set; }
        public int RowCount { get; set; }
        public decimal TotalHours { get; set; }
    }

    private sealed class StagingRow
    {
        public Guid Id { get; set; }
        public Guid ImportId { get; set; }
        public string SheetName { get; set; } = "";
        public int SheetRow { get; set; }
        public string ProjectLabel { get; set; } = "";
        public DateOnly EntryDate { get; set; }
        public decimal Hours { get; set; }
        public string? Description { get; set; }
        public Guid? ProjectId { get; set; }
        public Guid? BillingRoleId { get; set; }
        public bool Included { get; set; }
    }
}
