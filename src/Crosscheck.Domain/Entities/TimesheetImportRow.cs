namespace Crosscheck.Domain.Entities;

/// <summary>One parsed workbook line (project label, date, hours, description) plus the
/// importer's review edits. Every field the commit uses is editable on review; only
/// included rows with a resolved project and billing role become time entries.</summary>
public class TimesheetImportRow
{
    public Guid Id { get; set; }
    public Guid ImportId { get; set; }

    /// <summary>Which workbook sheet/row this came from (provenance, shown on review).</summary>
    public required string SheetName { get; set; }
    public int SheetRow { get; set; }

    /// <summary>The workbook's project label as parsed (trimmed) — matched to a project
    /// by code first, then saved mappings; unmatched rows are mapped by hand.</summary>
    public required string ProjectLabel { get; set; }

    public DateOnly EntryDate { get; set; }
    public decimal Hours { get; set; }
    public string? Description { get; set; }

    /// <summary>The resolved system project — null until matched or manually mapped.</summary>
    public Guid? ProjectId { get; set; }

    public Guid? BillingRoleId { get; set; }

    /// <summary>Whether this row commits. Rows flagged as duplicates default to excluded.</summary>
    public bool Included { get; set; } = true;
}
