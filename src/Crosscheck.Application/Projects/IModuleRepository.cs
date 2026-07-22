using Crosscheck.Domain.Entities;

namespace Crosscheck.Application.Projects;

/// <summary>A module rate override with what the UI needs. <paramref name="RoleName"/> is null
/// for a module-wide (all-roles) override. <paramref name="HasBilledTime"/> is true when
/// invoiced time has been priced by this override, which freezes it (no correcting or
/// removing). <paramref name="HasLoggedTime"/> is true when any time entry the override covers
/// exists — such an override can only be removed when a fallback rate still covers that time.</summary>
public record ModuleRateSummary(
    ProjectModuleRate Rate, string? RoleName, bool HasBilledTime = false, bool HasLoggedTime = false);

public interface IModuleRepository
{
    /// <summary>A project's modules with allocations (role names populated), ordered by
    /// sort_order. Live only unless <paramref name="includeDeleted"/>.</summary>
    Task<IReadOnlyList<ProjectModule>> GetForProjectAsync(Guid projectId, bool includeDeleted = false, CancellationToken cancellationToken = default);

    /// <summary>Live modules for a set of projects in one query (the timesheet grid).</summary>
    Task<IReadOnlyList<ProjectModule>> GetForProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default);

    /// <summary>The module (live or deleted) with allocations, or null.</summary>
    Task<ProjectModule?> GetByIdAsync(Guid moduleId, CancellationToken cancellationToken = default);

    /// <summary>True when the project has at least one live module — the gate that makes
    /// module selection mandatory on new time entries.</summary>
    Task<bool> HasLiveModulesAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task AddAsync(ProjectModule module, CancellationToken cancellationToken = default);

    /// <summary>Persists name, sort order, flat hours, and agreed amount (+ updated_at).</summary>
    Task UpdateAsync(ProjectModule module, CancellationToken cancellationToken = default);

    /// <summary>Replaces the module's per-role hour allocations wholesale (the entity carries
    /// the desired set), like budget role allocations.</summary>
    Task ReplaceAllocationsAsync(Guid moduleId, IReadOnlyList<ModuleRoleAllocation> allocations, CancellationToken cancellationToken = default);

    Task SoftDeleteAsync(Guid moduleId, CancellationToken cancellationToken = default);

    /// <summary>True if ANY time entry (any status) is attached to the module.</summary>
    Task<bool> HasLoggedTimeAsync(Guid moduleId, CancellationToken cancellationToken = default);

    // Rate overrides ------------------------------------------------------------

    /// <summary>Live rate overrides for a module (module-wide row first, then by role name).</summary>
    Task<IReadOnlyList<ModuleRateSummary>> GetRatesAsync(Guid moduleId, CancellationToken cancellationToken = default);

    Task<ProjectModuleRate?> GetRateByIdAsync(Guid moduleRateId, CancellationToken cancellationToken = default);

    /// <summary>The live override for exactly (module, role) — role null means the module-wide
    /// row. No fallback here; resolution lives in <see cref="IRateCardRepository.ResolveAsync"/>.</summary>
    Task<ProjectModuleRate?> GetRateForRoleAsync(Guid moduleId, Guid? roleId, CancellationToken cancellationToken = default);

    Task AddRateAsync(ProjectModuleRate rate, CancellationToken cancellationToken = default);

    /// <summary>Corrects a mistaken override amount in place. NOT a rate change.</summary>
    Task CorrectRateAsync(Guid moduleRateId, decimal hourlyRate, CancellationToken cancellationToken = default);

    Task SoftDeleteRateAsync(Guid moduleRateId, CancellationToken cancellationToken = default);

    /// <summary>True if invoiced time exists that this override prices: entries on the module,
    /// filtered to the role when <paramref name="roleId"/> is set (module-wide → any role).</summary>
    Task<bool> HasInvoicedTimeAsync(Guid moduleId, Guid? roleId, CancellationToken cancellationToken = default);

    /// <summary>Like <see cref="HasInvoicedTimeAsync"/> but for entries of any status.</summary>
    Task<bool> HasLoggedTimeForRoleAsync(Guid moduleId, Guid? roleId, CancellationToken cancellationToken = default);

    /// <summary>Distinct billing roles of the entries attached to the module — used to check
    /// that deleting a rate override leaves no logged time unpriceable.</summary>
    Task<IReadOnlyList<Guid>> GetLoggedRoleIdsAsync(Guid moduleId, CancellationToken cancellationToken = default);
}
