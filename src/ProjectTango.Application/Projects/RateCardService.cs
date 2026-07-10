using ProjectTango.Application.Common;
using ProjectTango.Application.Roles;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.Projects;

/// <summary>Rate card management. Rates are fixed for the life of the project — one live row
/// per (project, billing role), set from the contract. Editing is for fixing a mistaken entry
/// and is locked once the rate has priced invoiced time.</summary>
public class RateCardService(
    ICurrentUser currentUser,
    IProjectRepository projects,
    IRateCardRepository rateCards,
    IRoleRepository roles,
    IAuditLog audit)
{
    public async Task<IReadOnlyList<RateCardSummary>> ListForProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(RoleNames.OperationsManager, RoleNames.ProjectManager);
        return await rateCards.GetForProjectAsync(projectId, cancellationToken);
    }

    public async Task SetRateAsync(
        Guid projectId, Guid roleId, decimal hourlyRate,
        CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(projectId, cancellationToken)
            ?? throw new DomainException("Unknown project.");
        var adminOverride = currentUser.RequireCanManage(project);

        var role = await roles.GetByIdAsync(roleId, cancellationToken)
            ?? throw new DomainException("Unknown role.");
        if (!role.IsBillable)
        {
            throw new DomainException($"{role.Name} is not a billable role — rates only apply to billable roles.");
        }

        if (hourlyRate < 0)
        {
            throw new DomainException("Hourly rate cannot be negative.");
        }

        var existing = await rateCards.GetForRoleAsync(projectId, roleId, cancellationToken);
        if (existing is not null)
        {
            throw new DomainException(
                $"{role.Name} already has a rate on this project — edit it instead of adding another.");
        }

        var rateCard = new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            RoleId = roleId,
            HourlyRate = hourlyRate,
        };
        await rateCards.AddAsync(rateCard, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "rate.set", "project", projectId,
            new
            {
                Role = role.Name,
                HourlyRate = hourlyRate,
                adminOverride,
            }), cancellationToken);
    }

    /// <summary>Fixes a data-entry mistake on an existing rate row (wrong amount) in place.
    /// This is NOT a rate change — it's locked once the rate has priced invoiced time.</summary>
    public async Task CorrectRateAsync(
        Guid projectId, Guid rateCardId, decimal hourlyRate,
        CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(projectId, cancellationToken)
            ?? throw new DomainException("Unknown project.");
        var adminOverride = currentUser.RequireCanManage(project);

        if (hourlyRate < 0)
        {
            throw new DomainException("Hourly rate cannot be negative.");
        }

        var rate = await rateCards.GetByIdAsync(rateCardId, cancellationToken);
        if (rate is null || rate.ProjectId != projectId)
        {
            throw new DomainException("Unknown rate.");
        }

        if (await rateCards.HasInvoicedTimeAsync(projectId, rate.RoleId, cancellationToken))
        {
            throw new DomainException(
                "This rate has already priced invoiced time and can no longer be corrected — void the invoice instead.");
        }

        await rateCards.CorrectAsync(rate.Id, hourlyRate, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "rate.correct", "project", projectId,
            new
            {
                RateCardId = rate.Id,
                FromRate = rate.HourlyRate,
                ToRate = hourlyRate,
                adminOverride,
            }), cancellationToken);
    }

    /// <summary>Soft-deletes a mistaken rate row (as long as it hasn't priced or been logged
    /// against any time).</summary>
    public async Task DeleteRateAsync(Guid projectId, Guid rateCardId, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(projectId, cancellationToken)
            ?? throw new DomainException("Unknown project.");
        var adminOverride = currentUser.RequireCanManage(project);

        var rate = await rateCards.GetByIdAsync(rateCardId, cancellationToken);
        if (rate is null || rate.ProjectId != projectId)
        {
            throw new DomainException("Unknown rate.");
        }

        if (await rateCards.HasInvoicedTimeAsync(projectId, rate.RoleId, cancellationToken))
        {
            throw new DomainException(
                "This rate has already priced invoiced time and can no longer be removed — void the invoice instead.");
        }

        // Even before invoicing, removing a rate that has priced logged time would leave those
        // entries unpriceable. Block it — correct the rate or the entries instead.
        if (await rateCards.HasLoggedTimeAsync(projectId, rate.RoleId, cancellationToken))
        {
            throw new DomainException(
                "Time has been logged against this rate — it can't be removed. Correct the rate, or remove those time entries first.");
        }

        await rateCards.SoftDeleteAsync(rate.Id, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "rate.delete", "project", projectId,
            new
            {
                RateCardId = rate.Id,
                rate.HourlyRate,
                adminOverride,
            }), cancellationToken);
    }
}
