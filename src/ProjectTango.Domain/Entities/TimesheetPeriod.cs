using ProjectTango.Domain.Enums;

namespace ProjectTango.Domain.Entities;

/// <summary>A semi-monthly edit window. Exists as a row only once it has been managed;
/// absence of a row for a date means that date's window is open. Closing locks owner
/// edits for entries in [<see cref="PeriodStart"/>, <see cref="PeriodEnd"/>].</summary>
public class TimesheetPeriod
{
    public Guid Id { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public TimesheetPeriodStatus Status { get; set; } = TimesheetPeriodStatus.Open;
    public Guid? ClosedById { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}
