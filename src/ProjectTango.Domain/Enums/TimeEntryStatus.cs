namespace ProjectTango.Domain.Enums;

/// <summary>Time entry lifecycle (design-doc §5.2). No submission step: entries are born
/// <see cref="Open"/> and owner-editable. Approval is a billing decision; invoiced entries
/// are immutable — void the invoice to return them to <see cref="Approved"/>.</summary>
public enum TimeEntryStatus
{
    Open,
    Approved,
    Invoiced,
}
