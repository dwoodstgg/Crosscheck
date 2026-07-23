namespace Crosscheck.Domain.Enums;

/// <summary>Lifecycle of an uploaded workbook: under review → committed; a commit can be
/// reversed (rolled back) while none of its entries are invoiced. A pending import is
/// discarded by hard-deleting it — no time entries exist yet.</summary>
public enum TimesheetImportStatus
{
    Pending,
    Committed,
    RolledBack,
}
