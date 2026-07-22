namespace Crosscheck.Domain.Entities;

/// <summary>The hourly rate for a (project, billing role) pair. Rates are fixed for the life
/// of the project — one live row per role. Editing is for fixing a mistaken entry, and is
/// locked once the rate has priced invoiced time.</summary>
public class ProjectRateCard
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid RoleId { get; set; }
    public decimal HourlyRate { get; set; }
}
