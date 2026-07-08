using ProjectTango.Domain.Enums;

namespace ProjectTango.Domain.Entities;

/// <summary>One person's hours on one project for one day. Billing role is chosen per
/// entry; <see cref="HoursWorked"/> is owner-only and <see cref="HoursBilled"/> is the
/// approver's billing decision (design rules 3, 6). Immutable once invoiced.</summary>
public class TimeEntry
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary>The role this entry bills under — resolves the rate for (project, role, date).</summary>
    public Guid BillingRoleId { get; set; }

    public DateOnly EntryDate { get; set; }

    /// <summary>Set only by the owner. Quarter-hour increments enforced in the app layer.</summary>
    public decimal HoursWorked { get; set; }

    /// <summary>Defaults to <see cref="HoursWorked"/>; adjusted only by an approver at approval.</summary>
    public decimal HoursBilled { get; set; }

    public string? Notes { get; set; }

    /// <summary>Authoritative billable flag. Seeded from the project's client (internal → false).</summary>
    public bool IsBillable { get; set; } = true;

    public TimeEntryStatus Status { get; set; } = TimeEntryStatus.Open;

    public Guid? ApprovedById { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
}
