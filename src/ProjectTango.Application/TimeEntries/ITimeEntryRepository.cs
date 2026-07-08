using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;

namespace ProjectTango.Application.TimeEntries;

/// <summary>A time entry joined with the display names an approver needs.</summary>
public record ApprovalEntry(TimeEntry Entry, string EmployeeName, string BillingRoleName);

/// <summary>One project time entry flattened for burn reporting, with its billing rate
/// resolved for the entry date (null when no rate card covers it — a billable gap).</summary>
public record BurnRow(
    Guid EntryId,
    DateOnly EntryDate,
    TimeEntryStatus Status,
    bool IsBillable,
    Guid EmployeeId,
    string EmployeeName,
    Guid BillingRoleId,
    string RoleName,
    decimal HoursWorked,
    decimal HoursBilled,
    decimal? ResolvedRate);

public interface ITimeEntryRepository
{
    Task<TimeEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The single entry for a grid cell — one per (employee, project, date).</summary>
    Task<TimeEntry?> GetByCellAsync(Guid employeeId, Guid projectId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>All of an employee's entries in a date range (the monthly grid).</summary>
    Task<IReadOnlyList<TimeEntry>> GetForEmployeeRangeAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>A project's entries in a date range, with employee and role names (approvals).</summary>
    Task<IReadOnlyList<ApprovalEntry>> GetForProjectRangeAsync(Guid projectId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Every entry on a project, flattened with its resolved billing rate, for the
    /// dashboard burn view.</summary>
    Task<IReadOnlyList<BurnRow>> GetBurnRowsAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task AddAsync(TimeEntry entry, CancellationToken cancellationToken = default);

    Task UpdateAsync(TimeEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Removes an <c>open</c> entry (pre-financial — clearing a grid cell). Never
    /// used on approved/invoiced entries.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
