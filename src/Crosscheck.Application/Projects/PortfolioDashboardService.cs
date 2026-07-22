using Crosscheck.Application.Common;
using Crosscheck.Application.TimeEntries;
using Crosscheck.Domain;
using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;

namespace Crosscheck.Application.Projects;

/// <summary>One burn dimension running hot on a project — the overall budget, a role's hour
/// allocation, or a module's hours — for the "needs attention" card.</summary>
public record BurnHotspot(string Label, double Percent, bool IsOver);

/// <summary>One project's line on the company-wide dashboard: identity, lifetime burn totals,
/// and budget status when a budget row exists.</summary>
public record PortfolioProject(
    Project Project,
    string ClientName,
    string ProjectManagerName,
    decimal HoursWorked,
    decimal ApprovedValue,
    decimal InvoicedValue,
    decimal PendingValue,
    BudgetStatus? Budget,
    bool HasRateGaps)
{
    /// <summary>Realized + realizable value: approved (WIP) plus already invoiced.</summary>
    public decimal BillableValue => ApprovedValue + InvoicedValue;

    /// <summary>Overall budget burn, or null when there is no budget to measure against.</summary>
    public double? BurnPercent =>
        Budget is { } b && (b.PercentValue is not null || b.PercentHours is not null) ? b.BurnPercent : null;

    /// <summary>The hottest burn dimension anywhere on the project — overall, per-role, or
    /// per-module — mirroring what the threshold alerts watch (design rule 8).</summary>
    public double? MaxBurnPercent
    {
        get
        {
            if (Budget is null)
            {
                return null;
            }

            var candidates = new List<double>();
            if (BurnPercent is { } overall)
            {
                candidates.Add(overall);
            }
            candidates.AddRange(Budget.Roles.Where(r => r.AllocatedHours > 0).Select(r => r.PercentHours));
            candidates.AddRange(Budget.Modules.Where(m => !m.IsDeleted && m.AllocatedHours > 0).Select(m => m.PercentHours));
            return candidates.Count > 0 ? candidates.Max() : null;
        }
    }

    /// <summary>True when any measured dimension has overrun (overall, role, or module).
    /// Overrun never blocks work (design rule 9) — it is flagged here instead.</summary>
    public bool HasOverrun => Budget is { } b
        && (b.IsOverBudget
            || b.Roles.Any(r => r.AllocatedHours > 0 && r.IsOver)
            || b.Modules.Any(m => !m.IsDeleted && m.AllocatedHours > 0 && m.IsOver));

    /// <summary>Burn dimensions at or past <paramref name="thresholdPercent"/>, worst first —
    /// the detail lines behind the at-risk flag.</summary>
    public IReadOnlyList<BurnHotspot> Hotspots(double thresholdPercent)
    {
        var spots = new List<BurnHotspot>();
        if (Budget is null)
        {
            return spots;
        }

        if (BurnPercent is { } overall && (overall >= thresholdPercent || Budget.IsOverBudget))
        {
            spots.Add(new BurnHotspot("Overall budget", overall, Budget.IsOverBudget));
        }

        spots.AddRange(Budget.Roles
            .Where(r => r.AllocatedHours > 0 && (r.PercentHours >= thresholdPercent || r.IsOver))
            .Select(r => new BurnHotspot($"{r.RoleName} hours", r.PercentHours, r.IsOver)));

        spots.AddRange(Budget.Modules
            .Where(m => m.ModuleId is not null && !m.IsDeleted && m.AllocatedHours > 0
                && (m.PercentHours >= thresholdPercent || m.IsOver))
            .Select(m => new BurnHotspot($"{m.ModuleName} hours", m.PercentHours, m.IsOver)));

        return spots.OrderByDescending(s => s.IsOver).ThenByDescending(s => s.Percent).ToList();
    }
}

/// <summary>A dated project close-out horizon for the deadlines card. Negative
/// <paramref name="DaysLeft"/> means the end date has passed with the project still open.</summary>
public record ProjectDeadline(PortfolioProject Item, DateOnly EndDate, int DaysLeft)
{
    public bool IsOverdue => DaysLeft < 0;
}

/// <summary>The company-wide portfolio dashboard: every project's burn, the ones running hot,
/// and approaching end dates.</summary>
public class PortfolioDashboard
{
    /// <summary>Burn from this percent counts as "getting close" to overrun.</summary>
    public const double NearOverrunPercent = 80;

    /// <summary>How far ahead the deadlines card looks.</summary>
    public const int DeadlineWindowDays = 45;

    /// <summary>All projects (any status), sorted client then name.</summary>
    public required IReadOnlyList<PortfolioProject> Projects { get; init; }

    public required DateOnly Today { get; init; }

    public int ActiveCount => Projects.Count(p => p.Project.Status == ProjectStatus.Active);

    public decimal TotalWip => Projects.Sum(p => p.ApprovedValue);
    public decimal TotalPending => Projects.Sum(p => p.PendingValue);
    public decimal TotalInvoiced => Projects.Sum(p => p.InvoicedValue);

    /// <summary>Open (active/on-hold) projects overrun or near it anywhere — overall, a role,
    /// or a module — worst burn first.</summary>
    public IReadOnlyList<PortfolioProject> AtRisk => Projects
        .Where(p => p.Project.Status is ProjectStatus.Active or ProjectStatus.OnHold)
        .Where(p => p.HasOverrun || p.MaxBurnPercent >= NearOverrunPercent)
        .OrderByDescending(p => p.HasOverrun)
        .ThenByDescending(p => p.MaxBurnPercent)
        .ToList();

    /// <summary>Open projects whose end date is inside the look-ahead window or already past,
    /// soonest (or most overdue) first.</summary>
    public IReadOnlyList<ProjectDeadline> UpcomingDeadlines => Projects
        .Where(p => p.Project.Status is ProjectStatus.Active or ProjectStatus.OnHold)
        .Where(p => p.Project.EndDate is { } end && end <= Today.AddDays(DeadlineWindowDays))
        .Select(p => new ProjectDeadline(p, p.Project.EndDate!.Value, p.Project.EndDate!.Value.DayNumber - Today.DayNumber))
        .OrderBy(d => d.EndDate)
        .ToList();
}

/// <summary>Read-only cross-project dashboard for the landing page. Reuses the same burn maths
/// as the per-project dashboard and the alert evaluator (<see cref="BudgetBurn"/>) so the three
/// never disagree about who is over budget.</summary>
public class PortfolioDashboardService(
    ICurrentUser currentUser,
    IProjectRepository projects,
    ITimeEntryRepository entries,
    IBudgetRepository budgets,
    IModuleRepository modules)
{
    public async Task<PortfolioDashboard> GetAsync(DateOnly? today = null, CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(RoleNames.OperationsManager, RoleNames.ProjectManager);

        var summaries = await projects.GetAllAsync(cancellationToken);
        var items = new List<PortfolioProject>(summaries.Count);

        foreach (var summary in summaries
            .OrderBy(s => s.ClientName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Project.Name, StringComparer.OrdinalIgnoreCase))
        {
            var rows = await entries.GetBurnRowsAsync(summary.Project.Id, cancellationToken);
            var liveModules = await modules.GetForProjectAsync(summary.Project.Id, includeDeleted: false, cancellationToken);
            var budget = await budgets.GetForProjectAsync(summary.Project.Id, cancellationToken);

            items.Add(new PortfolioProject(
                summary.Project,
                summary.ClientName,
                summary.ProjectManagerName,
                HoursWorked: rows.Sum(r => r.HoursWorked),
                ApprovedValue: rows.Where(r => r.Status == TimeEntryStatus.Approved).Sum(BudgetBurn.RowValue),
                InvoicedValue: rows.Where(r => r.Status == TimeEntryStatus.Invoiced).Sum(BudgetBurn.RowValue),
                PendingValue: rows.Where(r => r.Status == TimeEntryStatus.Open).Sum(BudgetBurn.RowValue),
                Budget: budget is null ? null : BudgetBurn.Compute(budget, liveModules, rows),
                HasRateGaps: rows.Any(r => r is { IsBillable: true, ResolvedRate: null })));
        }

        return new PortfolioDashboard
        {
            Projects = items,
            Today = today ?? DateOnly.FromDateTime(DateTime.UtcNow),
        };
    }
}
