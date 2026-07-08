using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.TimeEntries;

/// <summary>A time entry joined with the display names an approver needs.</summary>
public record ApprovalEntry(TimeEntry Entry, string EmployeeName, string BillingRoleName);

public interface ITimeEntryRepository
{
    Task<TimeEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The single entry for a grid cell — one per (employee, project, date).</summary>
    Task<TimeEntry?> GetByCellAsync(Guid employeeId, Guid projectId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>All of an employee's entries in a date range (the monthly grid).</summary>
    Task<IReadOnlyList<TimeEntry>> GetForEmployeeRangeAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>A project's entries in a date range, with employee and role names (approvals).</summary>
    Task<IReadOnlyList<ApprovalEntry>> GetForProjectRangeAsync(Guid projectId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    Task AddAsync(TimeEntry entry, CancellationToken cancellationToken = default);

    Task UpdateAsync(TimeEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Removes an <c>open</c> entry (pre-financial — clearing a grid cell). Never
    /// used on approved/invoiced entries.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
