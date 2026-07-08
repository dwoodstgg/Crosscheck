using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.TimeEntries;

public interface ITimesheetPeriodRepository
{
    /// <summary>The window row starting on this date, or null if the window was never
    /// managed (i.e. it is open by default).</summary>
    Task<TimesheetPeriod?> GetByStartAsync(DateOnly periodStart, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TimesheetPeriod>> GetInRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Inserts the window row, or updates its status/closed metadata if it exists
    /// (keyed on period_start).</summary>
    Task UpsertAsync(TimesheetPeriod period, CancellationToken cancellationToken = default);
}
