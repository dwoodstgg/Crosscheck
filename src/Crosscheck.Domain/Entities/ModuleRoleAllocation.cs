namespace Crosscheck.Domain.Entities;

/// <summary>An hour allocation for one billing role under a project module (e.g. Ag Chem:
/// Developer 240h). Allocations are in hours; the dollar value derives from the resolved
/// rate. Mirrors <see cref="BudgetRoleAllocation"/> one level down.</summary>
public class ModuleRoleAllocation
{
    public Guid Id { get; set; }
    public Guid ModuleId { get; set; }
    public Guid RoleId { get; set; }
    public decimal Hours { get; set; }

    /// <summary>Role display label, populated by the repository for UI/reporting — not
    /// persisted on this row (it lives on <c>roles</c>).</summary>
    public string? RoleName { get; set; }
}
