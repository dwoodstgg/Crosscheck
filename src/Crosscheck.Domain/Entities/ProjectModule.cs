namespace Crosscheck.Domain.Entities;

/// <summary>A named sub-section of a project's budget — one row of the work order's
/// resources-and-cost (or level-of-effort) table, e.g. "Ag Chem", "Supplemental Hours",
/// "5. DMAP – Bug Fixes". Carries its own hour budget — an explicit flat <see cref="Hours"/>
/// value or the sum of per-role <see cref="Allocations"/> — plus optional rate overrides
/// (<c>project_module_rates</c>) and an optional agreed fixed billing <see cref="Amount"/>.
/// Soft delete only: entries stay attached for burn history.</summary>
public class ProjectModule
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Explicit flat hour budget (milestone style). Null = derive from
    /// <see cref="Allocations"/>; see <see cref="EffectiveHours"/>.</summary>
    public decimal? Hours { get; set; }

    /// <summary>Agreed fixed billing amount. Set = fixed-price: the client is billed exactly
    /// this and hours are internal budgeting only. Null = hourly: bills hours × resolved rate
    /// as incurred.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Per-role hour allocations (work-order style: Developer 240h, PM 32h…).
    /// Coexists with <see cref="Hours"/> the same way the budget's overall hours coexist
    /// with its role allocations: the explicit value wins, the sum is the fallback.</summary>
    public List<ModuleRoleAllocation> Allocations { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public bool IsDeleted => DeletedAt is not null;

    public bool IsFixedPrice => Amount is not null;

    /// <summary>The module's hour budget: explicit <see cref="Hours"/> when set, otherwise
    /// the sum of the per-role allocations.</summary>
    public decimal EffectiveHours => Hours ?? Allocations.Sum(a => a.Hours);
}
