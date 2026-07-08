namespace ProjectTango.Domain.Enums;

/// <summary>State of a semi-monthly edit window. A window is <see cref="Open"/> until
/// Ops/Admin closes it, which locks owner edits for entries in the window; reopening
/// restores owner edits. Both transitions are audited.</summary>
public enum TimesheetPeriodStatus
{
    Open,
    Closed,
}
