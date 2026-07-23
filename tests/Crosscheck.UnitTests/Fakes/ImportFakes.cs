using Crosscheck.Application.Imports;
using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;

namespace Crosscheck.UnitTests.Fakes;

/// <summary>Returns whatever <see cref="Result"/> a test configures.</summary>
public sealed class FakeTimesheetWorkbookParser : ITimesheetWorkbookParser
{
    public ParsedTimesheetWorkbook Result { get; set; } = new("Test Person", 2026, [], []);

    public ParsedTimesheetWorkbook Parse(Stream workbook) => Result;
}

public sealed class FakeTimesheetImportRepository(FakeTimeEntryRepository entries) : ITimesheetImportRepository
{
    public List<TimesheetImport> Imports { get; } = [];
    public List<TimesheetImportRow> Rows { get; } = [];
    public List<TimesheetImportMapping> Mappings { get; } = [];
    public List<ProjectAssignment> CommittedAssignments { get; } = [];
    public List<Guid> Deleted { get; } = [];

    public Task AddAsync(TimesheetImport import, IReadOnlyList<TimesheetImportRow> rows, CancellationToken cancellationToken = default)
    {
        Imports.Add(import);
        Rows.AddRange(rows);
        return Task.CompletedTask;
    }

    public Task<TimesheetImport?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Imports.FirstOrDefault(i => i.Id == id));

    public Task<IReadOnlyList<TimesheetImportSummary>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TimesheetImportSummary>>(Imports
            .OrderByDescending(i => i.UploadedAt)
            .Select(i => new TimesheetImportSummary(
                i, "employee", "uploader",
                Rows.Count(r => r.ImportId == i.Id && r.Included),
                Rows.Where(r => r.ImportId == i.Id && r.Included).Sum(r => r.Hours)))
            .ToList());

    public Task<IReadOnlyList<TimesheetImportRow>> GetRowsAsync(Guid importId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TimesheetImportRow>>(Rows
            .Where(r => r.ImportId == importId)
            .OrderBy(r => r.EntryDate).ThenBy(r => r.SheetRow)
            .ToList());

    public Task<TimesheetImportRow?> GetRowAsync(Guid rowId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.FirstOrDefault(r => r.Id == rowId));

    public Task UpdateRowAsync(TimesheetImportRow row, CancellationToken cancellationToken = default)
    {
        var index = Rows.FindIndex(r => r.Id == row.Id);
        Rows[index] = row;
        return Task.CompletedTask;
    }

    public Task SetRowsIncludedAsync(IReadOnlyCollection<Guid> rowIds, bool included, CancellationToken cancellationToken = default)
    {
        foreach (var row in Rows.Where(r => rowIds.Contains(r.Id)))
        {
            row.Included = included;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TimesheetImportMapping>> GetMappingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TimesheetImportMapping>>(Mappings.ToList());

    public Task CommitAsync(
        TimesheetImport import,
        IReadOnlyList<TimeEntry> newEntries,
        IReadOnlyList<ProjectAssignment> newAssignments,
        IReadOnlyList<TimesheetImportMapping> newMappings,
        CancellationToken cancellationToken = default)
    {
        entries.Entries.AddRange(newEntries);
        CommittedAssignments.AddRange(newAssignments);
        foreach (var mapping in newMappings)
        {
            Mappings.RemoveAll(m => string.Equals(m.Label, mapping.Label, StringComparison.OrdinalIgnoreCase));
            Mappings.Add(mapping);
        }

        return Task.CompletedTask;
    }

    public Task<bool> HasInvoicedEntriesAsync(Guid importId, CancellationToken cancellationToken = default) =>
        Task.FromResult(entries.Entries.Any(e => e.ImportId == importId && e.Status == TimeEntryStatus.Invoiced));

    public Task RollbackAsync(TimesheetImport import, CancellationToken cancellationToken = default)
    {
        entries.Entries.RemoveAll(e => e.ImportId == import.Id && e.Status != TimeEntryStatus.Invoiced);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid importId, CancellationToken cancellationToken = default)
    {
        Imports.RemoveAll(i => i.Id == importId && i.Status == TimesheetImportStatus.Pending);
        Rows.RemoveAll(r => r.ImportId == importId);
        Deleted.Add(importId);
        return Task.CompletedTask;
    }
}
