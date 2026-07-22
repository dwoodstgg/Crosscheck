namespace Crosscheck.Domain.Entities;

/// <summary>Project membership. One row per person per project — an employee stays active for
/// the life of the project. Removing sets EndDate (soft-deactivate) when time has been logged,
/// or hard-deletes when none has; re-adding reopens the row. Time entries require an active
/// (not-ended) assignment.</summary>
public class ProjectAssignment
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary>UI pre-selection only — the authoritative billing role is chosen per time entry.</summary>
    public Guid? DefaultBillingRoleId { get; set; }

    /// <summary>Soft-deactivate marker: null = active, set = removed from the project.</summary>
    public DateOnly? EndDate { get; set; }

    public bool IsActive => EndDate is null;
}
