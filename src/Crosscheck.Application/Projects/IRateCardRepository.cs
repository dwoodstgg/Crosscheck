using Crosscheck.Domain.Entities;

namespace Crosscheck.Application.Projects;

/// <summary><paramref name="HasBilledTime"/> is true when invoiced time has been billed
/// against this (project, role) rate, which freezes it (no correcting or removing).
/// <paramref name="HasLoggedTime"/> is true when any time entry (open/approved/invoiced) exists
/// for it — such a rate can't be removed (it would leave that time unpriceable), though a
/// not-yet-invoiced rate can still be corrected.</summary>
public record RateCardSummary(
    ProjectRateCard RateCard, string RoleName, bool HasBilledTime = false, bool HasLoggedTime = false);

public interface IRateCardRepository
{
    Task<IReadOnlyList<RateCardSummary>> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<ProjectRateCard?> GetByIdAsync(Guid rateCardId, CancellationToken cancellationToken = default);

    /// <summary>The live rate row for a (project, role), or null if none exists.</summary>
    Task<ProjectRateCard?> GetForRoleAsync(Guid projectId, Guid roleId, CancellationToken cancellationToken = default);

    Task AddAsync(ProjectRateCard rateCard, CancellationToken cancellationToken = default);

    /// <summary>True if any INVOICED time entry exists for this (project, role) — such a rate
    /// has priced real money and is frozen.</summary>
    Task<bool> HasInvoicedTimeAsync(Guid projectId, Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>True if ANY time entry (any status) exists for this (project, role) — a rate
    /// with logged time can't be removed.</summary>
    Task<bool> HasLoggedTimeAsync(Guid projectId, Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>Corrects a mistaken rate amount in place. NOT a rate change.</summary>
    Task CorrectAsync(Guid rateCardId, decimal hourlyRate, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes a mistaken rate row.</summary>
    Task SoftDeleteAsync(Guid rateCardId, CancellationToken cancellationToken = default);

    /// <summary>Rate resolution (design rule 2): the rate for (project, module, billing role) —
    /// the module's per-role override when one exists, else the module-wide override, else the
    /// project rate card row. <paramref name="moduleId"/> null skips straight to the project rate.</summary>
    Task<decimal?> ResolveAsync(Guid projectId, Guid? moduleId, Guid roleId, CancellationToken cancellationToken = default);
}
