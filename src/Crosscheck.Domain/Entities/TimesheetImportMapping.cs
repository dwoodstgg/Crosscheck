namespace Crosscheck.Domain.Entities;

/// <summary>Remembers "workbook label → project" so later imports auto-map labels that
/// don't match a project code. Written at commit for every label the importer mapped by
/// hand; label matching is case-insensitive.</summary>
public class TimesheetImportMapping
{
    public Guid Id { get; set; }
    public required string Label { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
