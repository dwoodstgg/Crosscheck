using Crosscheck.Application.Common;
using Crosscheck.Application.Roles;
using Crosscheck.Domain;
using Crosscheck.Domain.Entities;

namespace Crosscheck.Application.Projects;

/// <summary>An hour allocation to a billing role within a module, as supplied by the caller.</summary>
public record ModuleRoleHourInput(Guid RoleId, decimal Hours);

/// <summary>A module with everything the manage page shows. <paramref name="PlannedValue"/> is
/// the contract math — Σ allocation hours × resolved rate (override else project rate), or
/// flat hours × module-wide rate — null when a needed rate is missing.</summary>
public record ModuleSummary(
    ProjectModule Module,
    IReadOnlyList<ModuleRateSummary> Rates,
    bool HasLoggedTime,
    decimal? PlannedValue);

/// <summary>Project module management (design-doc §5.2, decision #21): the work order's
/// budget breakdown — named modules/milestones with per-role or flat hour budgets, optional
/// rate overrides, and an optional agreed fixed billing amount. Modules are soft-deleted
/// only; entries stay attached for burn history.</summary>
public class ModuleService(
    ICurrentUser currentUser,
    IProjectRepository projects,
    IModuleRepository modules,
    IRateCardRepository rateCards,
    IRoleRepository roles,
    IAuditLog audit,
    IBudgetAlertService budgetAlerts)
{
    public async Task<IReadOnlyList<ModuleSummary>> ListForProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(RoleNames.OperationsManager, RoleNames.ProjectManager);
        var list = await modules.GetForProjectAsync(projectId, includeDeleted: false, cancellationToken);
        var projectRates = await rateCards.GetForProjectAsync(projectId, cancellationToken);
        var projectRateByRole = projectRates.ToDictionary(r => r.RateCard.RoleId, r => r.RateCard.HourlyRate);

        var result = new List<ModuleSummary>(list.Count);
        foreach (var module in list)
        {
            var rates = await modules.GetRatesAsync(module.Id, cancellationToken);
            result.Add(new ModuleSummary(
                module,
                rates,
                await modules.HasLoggedTimeAsync(module.Id, cancellationToken),
                PlannedValue(module, rates, projectRateByRole)));
        }

        return result;
    }

    /// <summary>Σ hours × rate using the same resolution the entries will bill at: role
    /// override → module-wide override → project rate. Null when any needed rate is missing
    /// (flat hours can only be priced by a module-wide override).</summary>
    public static decimal? PlannedValue(
        ProjectModule module, IReadOnlyList<ModuleRateSummary> rates,
        IReadOnlyDictionary<Guid, decimal> projectRateByRole)
    {
        var moduleWide = rates.FirstOrDefault(r => r.Rate.RoleId is null)?.Rate.HourlyRate;

        if (module.Hours is { } flatHours)
        {
            return moduleWide is { } wide ? flatHours * wide : null;
        }

        if (module.Allocations.Count == 0)
        {
            return null;
        }

        decimal total = 0;
        foreach (var allocation in module.Allocations)
        {
            var roleOverride = rates.FirstOrDefault(r => r.Rate.RoleId == allocation.RoleId)?.Rate.HourlyRate;
            var rate = roleOverride
                ?? moduleWide
                ?? (projectRateByRole.TryGetValue(allocation.RoleId, out var projectRate) ? projectRate : (decimal?)null);
            if (rate is null)
            {
                return null;
            }

            total += allocation.Hours * rate.Value;
        }

        return total;
    }

    public async Task<Guid> CreateAsync(
        Guid projectId, string name, decimal? hours, decimal? amount,
        IReadOnlyList<ModuleRoleHourInput>? roleAllocations = null,
        CancellationToken cancellationToken = default)
    {
        var (project, adminOverride) = await RequireManagedProjectAsync(projectId, cancellationToken);
        name = ValidateName(name);
        ValidateHours(hours);
        ValidateAmount(amount);

        var existing = await modules.GetForProjectAsync(projectId, includeDeleted: false, cancellationToken);
        if (existing.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException($"This project already has a {Label(project)} named \"{name}\".");
        }

        var module = new ProjectModule
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = name,
            SortOrder = existing.Count == 0 ? 1 : existing.Max(m => m.SortOrder) + 1,
            Hours = hours,
            Amount = amount,
        };
        module.Allocations = await BuildAllocationsAsync(module.Id, roleAllocations, cancellationToken);
        await modules.AddAsync(module, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "module.create", "project", projectId,
            new
            {
                ModuleId = module.Id,
                module.Name,
                module.Hours,
                module.Amount,
                RoleHours = module.Allocations.ToDictionary(a => a.RoleName ?? a.RoleId.ToString(), a => a.Hours),
                adminOverride,
            }), cancellationToken);

        await budgetAlerts.OnModuleChangedAsync(projectId, module.Id, cancellationToken);
        return module.Id;
    }

    public async Task RenameAsync(Guid projectId, Guid moduleId, string name, CancellationToken cancellationToken = default)
    {
        var (project, adminOverride) = await RequireManagedProjectAsync(projectId, cancellationToken);
        var module = await RequireLiveModuleAsync(project, moduleId, cancellationToken);
        name = ValidateName(name);

        if (string.Equals(module.Name, name, StringComparison.Ordinal))
        {
            return;
        }

        var siblings = await modules.GetForProjectAsync(projectId, includeDeleted: false, cancellationToken);
        if (siblings.Any(m => m.Id != moduleId && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException($"This project already has a {Label(project)} named \"{name}\".");
        }

        var fromName = module.Name;
        module.Name = name;
        await modules.UpdateAsync(module, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "module.rename", "project", projectId,
            new { ModuleId = moduleId, FromName = fromName, ToName = name, adminOverride }), cancellationToken);
    }

    /// <summary>Sets the explicit flat hour budget (null reverts to the allocation sum).</summary>
    public async Task SetHoursAsync(Guid projectId, Guid moduleId, decimal? hours, CancellationToken cancellationToken = default)
    {
        var (project, adminOverride) = await RequireManagedProjectAsync(projectId, cancellationToken);
        var module = await RequireLiveModuleAsync(project, moduleId, cancellationToken);
        ValidateHours(hours);

        if (module.Hours == hours)
        {
            return;
        }

        var fromHours = module.Hours;
        module.Hours = hours;
        await modules.UpdateAsync(module, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "module.hours", "project", projectId,
            new { ModuleId = moduleId, module.Name, FromHours = fromHours, ToHours = hours, adminOverride }), cancellationToken);

        await budgetAlerts.OnModuleChangedAsync(projectId, moduleId, cancellationToken);
    }

    /// <summary>Replaces the per-role hour allocations wholesale.</summary>
    public async Task SetAllocationsAsync(
        Guid projectId, Guid moduleId, IReadOnlyList<ModuleRoleHourInput> roleAllocations,
        CancellationToken cancellationToken = default)
    {
        var (project, adminOverride) = await RequireManagedProjectAsync(projectId, cancellationToken);
        var module = await RequireLiveModuleAsync(project, moduleId, cancellationToken);

        var allocations = await BuildAllocationsAsync(moduleId, roleAllocations, cancellationToken);
        await modules.ReplaceAllocationsAsync(moduleId, allocations, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "module.allocations", "project", projectId,
            new
            {
                ModuleId = moduleId,
                module.Name,
                RoleHours = allocations.ToDictionary(a => a.RoleName ?? a.RoleId.ToString(), a => a.Hours),
                adminOverride,
            }), cancellationToken);

        await budgetAlerts.OnModuleChangedAsync(projectId, moduleId, cancellationToken);
    }

    /// <summary>Sets or clears the agreed fixed billing amount. Set = the breakdown is
    /// fixed-price (hours become internal budgeting); null = hourly, bills as incurred.</summary>
    public async Task SetAmountAsync(Guid projectId, Guid moduleId, decimal? amount, CancellationToken cancellationToken = default)
    {
        var (project, adminOverride) = await RequireManagedProjectAsync(projectId, cancellationToken);
        var module = await RequireLiveModuleAsync(project, moduleId, cancellationToken);
        ValidateAmount(amount);

        if (module.Amount == amount)
        {
            return;
        }

        var fromAmount = module.Amount;
        module.Amount = amount;
        await modules.UpdateAsync(module, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "module.amount", "project", projectId,
            new { ModuleId = moduleId, module.Name, FromAmount = fromAmount, ToAmount = amount, adminOverride }), cancellationToken);
    }

    /// <summary>Soft-deletes the module. Attached entries keep their pricing and show as
    /// "(removed)" in burn views; the name frees up for reuse; new time can't target it.</summary>
    public async Task DeleteAsync(Guid projectId, Guid moduleId, CancellationToken cancellationToken = default)
    {
        var (project, adminOverride) = await RequireManagedProjectAsync(projectId, cancellationToken);
        var module = await RequireLiveModuleAsync(project, moduleId, cancellationToken);

        var hadLoggedTime = await modules.HasLoggedTimeAsync(moduleId, cancellationToken);
        await modules.SoftDeleteAsync(moduleId, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "module.delete", "project", projectId,
            new { ModuleId = moduleId, module.Name, HadLoggedTime = hadLoggedTime, adminOverride }), cancellationToken);

        await budgetAlerts.OnModuleChangedAsync(projectId, moduleId, cancellationToken);
    }

    // Rate overrides ------------------------------------------------------------

    /// <summary>Adds a rate override. <paramref name="roleId"/> null = module-wide (any role
    /// bills this rate on the module).</summary>
    public async Task SetRateAsync(
        Guid projectId, Guid moduleId, Guid? roleId, decimal hourlyRate,
        CancellationToken cancellationToken = default)
    {
        var (project, adminOverride) = await RequireManagedProjectAsync(projectId, cancellationToken);
        var module = await RequireLiveModuleAsync(project, moduleId, cancellationToken);

        if (hourlyRate < 0)
        {
            throw new DomainException("Hourly rate cannot be negative.");
        }

        string? roleName = null;
        if (roleId is { } rid)
        {
            var role = await roles.GetByIdAsync(rid, cancellationToken)
                ?? throw new DomainException("Unknown role.");
            if (!role.IsBillable)
            {
                throw new DomainException($"{role.Name} is not a billable role — rates only apply to billable roles.");
            }

            roleName = role.Name;
        }

        var existing = await modules.GetRateForRoleAsync(moduleId, roleId, cancellationToken);
        if (existing is not null)
        {
            throw new DomainException(roleId is null
                ? $"This {Label(project)} already has an all-roles rate — edit it instead of adding another."
                : $"{roleName} already has a rate on this {Label(project)} — edit it instead of adding another.");
        }

        var rate = new ProjectModuleRate
        {
            Id = Guid.NewGuid(),
            ModuleId = moduleId,
            RoleId = roleId,
            HourlyRate = hourlyRate,
        };
        await modules.AddRateAsync(rate, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "module.rate.set", "project", projectId,
            new
            {
                ModuleId = moduleId,
                module.Name,
                Role = roleName ?? "(all roles)",
                HourlyRate = hourlyRate,
                adminOverride,
            }), cancellationToken);
    }

    /// <summary>Fixes a data-entry mistake on an override in place — locked once the override
    /// has priced invoiced time.</summary>
    public async Task CorrectRateAsync(
        Guid projectId, Guid moduleRateId, decimal hourlyRate,
        CancellationToken cancellationToken = default)
    {
        var (project, adminOverride) = await RequireManagedProjectAsync(projectId, cancellationToken);

        if (hourlyRate < 0)
        {
            throw new DomainException("Hourly rate cannot be negative.");
        }

        var (rate, module) = await RequireRateAsync(projectId, moduleRateId, cancellationToken);

        if (await modules.HasInvoicedTimeAsync(module.Id, rate.RoleId, cancellationToken))
        {
            throw new DomainException(
                "This rate has already priced invoiced time and can no longer be corrected — void the invoice instead.");
        }

        await modules.CorrectRateAsync(rate.Id, hourlyRate, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "module.rate.correct", "project", projectId,
            new
            {
                ModuleRateId = rate.Id,
                ModuleId = module.Id,
                module.Name,
                FromRate = rate.HourlyRate,
                ToRate = hourlyRate,
                adminOverride,
            }), cancellationToken);
    }

    /// <summary>Soft-deletes a mistaken override — blocked when it has priced invoiced time,
    /// or when logged time would lose its rate (no module-wide/role override or project rate
    /// left to fall back to).</summary>
    public async Task DeleteRateAsync(Guid projectId, Guid moduleRateId, CancellationToken cancellationToken = default)
    {
        var (project, adminOverride) = await RequireManagedProjectAsync(projectId, cancellationToken);
        var (rate, module) = await RequireRateAsync(projectId, moduleRateId, cancellationToken);

        if (await modules.HasInvoicedTimeAsync(module.Id, rate.RoleId, cancellationToken))
        {
            throw new DomainException(
                "This rate has already priced invoiced time and can no longer be removed — void the invoice instead.");
        }

        // Unlike a project rate, removing an override is fine while a fallback still prices the
        // logged time (that's the whole point of the resolution chain). Only block when some
        // logged entry would become rate-less.
        var loggedRoles = await modules.GetLoggedRoleIdsAsync(module.Id, cancellationToken);
        var affectedRoles = rate.RoleId is { } overrideRole
            ? loggedRoles.Where(r => r == overrideRole)
            : loggedRoles;
        foreach (var loggedRole in affectedRoles)
        {
            var stillCovered =
                (rate.RoleId is not null && await modules.GetRateForRoleAsync(module.Id, null, cancellationToken) is not null)
                || (rate.RoleId is null && await modules.GetRateForRoleAsync(module.Id, loggedRole, cancellationToken) is not null)
                || await rateCards.GetForRoleAsync(projectId, loggedRole, cancellationToken) is not null;
            if (!stillCovered)
            {
                throw new DomainException(
                    "Time has been logged against this rate and nothing else prices it — it can't be removed. " +
                    "Correct the rate, or add a project rate for the role first.");
            }
        }

        await modules.SoftDeleteRateAsync(rate.Id, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "module.rate.delete", "project", projectId,
            new
            {
                ModuleRateId = rate.Id,
                ModuleId = module.Id,
                module.Name,
                rate.HourlyRate,
                adminOverride,
            }), cancellationToken);
    }

    // Helpers -------------------------------------------------------------------

    private async Task<(Project Project, bool AdminOverride)> RequireManagedProjectAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        var project = await projects.GetByIdAsync(projectId, cancellationToken)
            ?? throw new DomainException("Unknown project.");
        return (project, currentUser.RequireCanManage(project));
    }

    private async Task<ProjectModule> RequireLiveModuleAsync(Project project, Guid moduleId, CancellationToken cancellationToken)
    {
        var module = await modules.GetByIdAsync(moduleId, cancellationToken);
        if (module is null || module.ProjectId != project.Id)
        {
            throw new DomainException($"Unknown {Label(project)}.");
        }

        if (module.IsDeleted)
        {
            throw new DomainException($"That {Label(project)} has been removed.");
        }

        return module;
    }

    private async Task<(ProjectModuleRate Rate, ProjectModule Module)> RequireRateAsync(
        Guid projectId, Guid moduleRateId, CancellationToken cancellationToken)
    {
        var rate = await modules.GetRateByIdAsync(moduleRateId, cancellationToken)
            ?? throw new DomainException("Unknown rate.");
        var module = await modules.GetByIdAsync(rate.ModuleId, cancellationToken);
        if (module is null || module.ProjectId != projectId)
        {
            throw new DomainException("Unknown rate.");
        }

        return (rate, module);
    }

    /// <summary>Validates and normalizes role hour allocations, mirroring the budget rule:
    /// positive hours only, real billable roles, one row per role.</summary>
    private async Task<List<ModuleRoleAllocation>> BuildAllocationsAsync(
        Guid moduleId, IReadOnlyList<ModuleRoleHourInput>? inputs, CancellationToken cancellationToken)
    {
        if (inputs is null || inputs.Count == 0)
        {
            return [];
        }

        var result = new List<ModuleRoleAllocation>();
        var seen = new HashSet<Guid>();
        foreach (var input in inputs)
        {
            if (input.Hours <= 0)
            {
                continue; // a zero/blank allocation just means "not budgeted"
            }

            if (!seen.Add(input.RoleId))
            {
                throw new DomainException("A role can only have one hour allocation.");
            }

            var role = await roles.GetByIdAsync(input.RoleId, cancellationToken)
                ?? throw new DomainException("Unknown billing role in allocation.");
            if (!role.IsBillable)
            {
                throw new DomainException($"{role.Name} is not a billable role — it cannot carry an hour allocation.");
            }

            result.Add(new ModuleRoleAllocation
            {
                Id = Guid.NewGuid(),
                ModuleId = moduleId,
                RoleId = role.Id,
                Hours = input.Hours,
                RoleName = role.DisplayName,
            });
        }

        return result;
    }

    private static string ValidateName(string name)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0)
        {
            throw new DomainException("A name is required.");
        }

        if (name.Length > 200)
        {
            throw new DomainException("The name is too long (200 characters max).");
        }

        return name;
    }

    private static void ValidateHours(decimal? hours)
    {
        if (hours is < 0)
        {
            throw new DomainException("Hours cannot be negative.");
        }
    }

    private static void ValidateAmount(decimal? amount)
    {
        if (amount is < 0)
        {
            throw new DomainException("The agreed amount cannot be negative.");
        }
    }

    /// <summary>The project's word for a breakdown section, for user-facing messages.</summary>
    private static string Label(Project project) => Terminology.Singular(project.BreakdownLabel);
}
