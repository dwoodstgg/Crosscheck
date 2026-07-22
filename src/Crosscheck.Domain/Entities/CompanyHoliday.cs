namespace Crosscheck.Domain.Entities;

/// <summary>An admin-managed company holiday. Holidays grey out on timesheets and are
/// excluded from the expected-workday count; they never block time entry (someone can
/// still work a holiday).</summary>
public class CompanyHoliday
{
    public Guid Id { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>Display name, e.g. "Independence Day".</summary>
    public required string Name { get; set; }

    public Guid? CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
