using Crosscheck.Domain.Enums;

namespace Crosscheck.Domain.Entities;

/// <summary>One uploaded timesheet workbook for one employee's year. Parsed into
/// <see cref="TimesheetImportRow"/> staging rows that are reviewed and edited before
/// commit; committed entries carry this import's id so the import can be rolled back
/// until any of its entries is invoiced (design-doc.md §6.6).</summary>
public class TimesheetImport
{
    public Guid Id { get; set; }

    /// <summary>The person the workbook belongs to — matched (or created) by tenant email at upload.</summary>
    public Guid EmployeeId { get; set; }

    public required string FileName { get; set; }

    public int Year { get; set; }

    public TimesheetImportStatus Status { get; set; } = TimesheetImportStatus.Pending;

    /// <summary>Parser oddities worth showing on review, newline-separated; null when clean.</summary>
    public string? ParseWarnings { get; set; }

    public Guid UploadedById { get; set; }
    public DateTimeOffset UploadedAt { get; set; }

    public Guid? CommittedById { get; set; }
    public DateTimeOffset? CommittedAt { get; set; }

    public Guid? RolledBackById { get; set; }
    public DateTimeOffset? RolledBackAt { get; set; }
}
