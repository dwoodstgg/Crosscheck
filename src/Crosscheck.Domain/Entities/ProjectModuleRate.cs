namespace Crosscheck.Domain.Entities;

/// <summary>A rate override for a project module. <see cref="RoleId"/> null = module-wide
/// (any role bills this rate on the module, e.g. "Ongoing Maintenance at $85/hr").
/// Resolution for an entry: (module, entry's role) → (module, null) → the project rate card.</summary>
public class ProjectModuleRate
{
    public Guid Id { get; set; }
    public Guid ModuleId { get; set; }

    /// <summary>Null = module-wide: applies to every billing role on the module.</summary>
    public Guid? RoleId { get; set; }

    public decimal HourlyRate { get; set; }
}
