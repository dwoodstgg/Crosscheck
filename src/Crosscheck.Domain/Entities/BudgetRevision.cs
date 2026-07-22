using Crosscheck.Domain.Enums;

namespace Crosscheck.Domain.Entities;

/// <summary>An audit-trail row for one budget change (design-doc §5.2): who, when, the
/// values before and after, and an optional reason. The <c>From*</c> fields are null on the
/// first revision (budget creation).</summary>
public class BudgetRevision
{
    public Guid Id { get; set; }
    public Guid BudgetId { get; set; }
    public Guid RevisedById { get; set; }
    public DateTimeOffset RevisedAt { get; set; }

    public ProjectType? FromType { get; set; }
    public decimal? FromAmount { get; set; }
    public decimal? FromHours { get; set; }

    public ProjectType ToType { get; set; }
    public decimal? ToAmount { get; set; }
    public decimal? ToHours { get; set; }

    public string? Reason { get; set; }
}
