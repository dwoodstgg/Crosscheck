using Crosscheck.Domain.Entities;

namespace Crosscheck.Application.Imports;

/// <summary>An import joined with the display names and row totals the imports list shows.</summary>
public record TimesheetImportSummary(
    TimesheetImport Import,
    string EmployeeName,
    string UploadedByName,
    int RowCount,
    decimal TotalHours);

public interface ITimesheetImportRepository
{
    /// <summary>Persists a freshly parsed import and its staging rows together.</summary>
    Task AddAsync(TimesheetImport import, IReadOnlyList<TimesheetImportRow> rows, CancellationToken cancellationToken = default);

    Task<TimesheetImport?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TimesheetImportSummary>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>An import's staging rows, ordered by date then workbook position.</summary>
    Task<IReadOnlyList<TimesheetImportRow>> GetRowsAsync(Guid importId, CancellationToken cancellationToken = default);

    Task<TimesheetImportRow?> GetRowAsync(Guid rowId, CancellationToken cancellationToken = default);

    Task UpdateRowAsync(TimesheetImportRow row, CancellationToken cancellationToken = default);

    /// <summary>Bulk include/exclude — used by "exclude all duplicates" on review.</summary>
    Task SetRowsIncludedAsync(IReadOnlyCollection<Guid> rowIds, bool included, CancellationToken cancellationToken = default);

    /// <summary>All saved label → project mappings (small table, matched case-insensitively).</summary>
    Task<IReadOnlyList<TimesheetImportMapping>> GetMappingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Commits an import in one transaction: inserts the new assignments and time
    /// entries, upserts the label mappings (a re-mapped label replaces its old target), and
    /// records the import's committed status. All-or-nothing — a failure leaves no entries.</summary>
    Task CommitAsync(
        TimesheetImport import,
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyList<ProjectAssignment> newAssignments,
        IReadOnlyList<TimesheetImportMapping> newMappings,
        CancellationToken cancellationToken = default);

    /// <summary>True when any of the import's entries has been invoiced — blocks rollback.</summary>
    Task<bool> HasInvoicedEntriesAsync(Guid importId, CancellationToken cancellationToken = default);

    /// <summary>Reverses a commit in one transaction: deletes the import's (non-invoiced)
    /// entries and records the rolled-back status. The service verifies none are invoiced.</summary>
    Task RollbackAsync(TimesheetImport import, CancellationToken cancellationToken = default);

    /// <summary>Hard-deletes a pending import and its staging rows (discard — no entries exist).</summary>
    Task DeleteAsync(Guid importId, CancellationToken cancellationToken = default);
}
