using ProjectTango.Application.Clients;
using ProjectTango.Application.Common;
using ProjectTango.Application.TimeEntries;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;

namespace ProjectTango.Application.Projects;

public record DashboardTotals(
    decimal HoursWorked,
    decimal HoursBilled,
    decimal ApprovedValue,
    decimal InvoicedValue,
    decimal PendingValue,
    int OpenCount,
    int ApprovedCount,
    int InvoicedCount)
{
    /// <summary>Realized + realizable value: approved (WIP) plus already invoiced.</summary>
    public decimal BillableValue => ApprovedValue + InvoicedValue;
}

public record RoleBurn(string RoleName, decimal HoursWorked, decimal HoursBilled, decimal Value);

public record PersonBurn(string EmployeeName, decimal HoursWorked, decimal HoursBilled, decimal Value);

public record RecentEntry(
    DateOnly Date, string EmployeeName, string RoleName,
    decimal HoursWorked, decimal HoursBilled, TimeEntryStatus Status, bool IsBillable);

public class ProjectDashboard
{
    public required Project Project { get; init; }
    public required string ClientName { get; init; }
    public required BillingProfile Billing { get; init; }
    public required DashboardTotals Totals { get; init; }
    public required IReadOnlyList<RoleBurn> ByRole { get; init; }
    public required IReadOnlyList<PersonBurn> ByPerson { get; init; }
    public required IReadOnlyList<AssignmentSummary> Team { get; init; }
    public required IReadOnlyList<RecentEntry> Recent { get; init; }

    /// <summary>True when some billable entries have no rate card for their date, so the
    /// dollar figures understate the real value until a rate is added (design rule 3).</summary>
    public bool HasRateGaps { get; init; }
}

/// <summary>Read-only project burn dashboard (design §6.2). Budgets and projections arrive
/// in Phase 2; for now it reports hours and dollar value from time entries and rate cards,
/// split by status/role/person. Value bases: open entries on hours_worked (pending), and
/// approved/invoiced on hours_billed (the billing decision).</summary>
public class ProjectDashboardService(
    ICurrentUser currentUser,
    IProjectRepository projects,
    IClientRepository clients,
    IAssignmentRepository assignments,
    ITimeEntryRepository entries)
{
    public async Task<ProjectDashboard?> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(RoleNames.OperationsManager, RoleNames.ProjectManager);

        var project = await projects.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        var client = await clients.GetByIdAsync(project.ClientId, cancellationToken);
        var rows = await entries.GetBurnRowsAsync(projectId, cancellationToken);
        var team = await assignments.GetForProjectAsync(projectId, cancellationToken);

        var totals = new DashboardTotals(
            HoursWorked: rows.Sum(r => r.HoursWorked),
            HoursBilled: rows.Sum(r => r.HoursBilled),
            ApprovedValue: rows.Where(r => r.Status == TimeEntryStatus.Approved).Sum(Value),
            InvoicedValue: rows.Where(r => r.Status == TimeEntryStatus.Invoiced).Sum(Value),
            PendingValue: rows.Where(r => r.Status == TimeEntryStatus.Open).Sum(Value),
            OpenCount: rows.Count(r => r.Status == TimeEntryStatus.Open),
            ApprovedCount: rows.Count(r => r.Status == TimeEntryStatus.Approved),
            InvoicedCount: rows.Count(r => r.Status == TimeEntryStatus.Invoiced));

        var byRole = rows
            .GroupBy(r => r.RoleName)
            .Select(g => new RoleBurn(g.Key, g.Sum(r => r.HoursWorked), g.Sum(r => r.HoursBilled), g.Sum(Value)))
            .OrderByDescending(r => r.HoursWorked)
            .ToList();

        var byPerson = rows
            .GroupBy(r => r.EmployeeName)
            .Select(g => new PersonBurn(g.Key, g.Sum(r => r.HoursWorked), g.Sum(r => r.HoursBilled), g.Sum(Value)))
            .OrderByDescending(p => p.HoursWorked)
            .ToList();

        var recent = rows
            .OrderByDescending(r => r.EntryDate)
            .Take(10)
            .Select(r => new RecentEntry(r.EntryDate, r.EmployeeName, r.RoleName, r.HoursWorked, r.HoursBilled, r.Status, r.IsBillable))
            .ToList();

        return new ProjectDashboard
        {
            Project = project,
            ClientName = client?.Name ?? "—",
            Billing = ProjectBilling.Resolve(project, client),
            Totals = totals,
            ByRole = byRole,
            ByPerson = byPerson,
            Team = team,
            Recent = recent,
            HasRateGaps = rows.Any(r => r is { IsBillable: true, ResolvedRate: null }),
        };
    }

    /// <summary>Dollar value of a row: billable hours × resolved rate. Open work is valued on
    /// hours_worked (an estimate); approved/invoiced on hours_billed (the billing decision).
    /// Non-billable work and entries without a rate contribute nothing.</summary>
    private static decimal Value(BurnRow row)
    {
        if (!row.IsBillable || row.ResolvedRate is null)
        {
            return 0m;
        }

        var hours = row.Status == TimeEntryStatus.Open ? row.HoursWorked : row.HoursBilled;
        return hours * row.ResolvedRate.Value;
    }
}
