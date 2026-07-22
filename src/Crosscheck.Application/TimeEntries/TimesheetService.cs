using Crosscheck.Application.Common;
using Crosscheck.Application.Projects;
using Crosscheck.Application.Roles;
using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;

namespace Crosscheck.Application.TimeEntries;

/// <summary>One line of the timesheet grid. A non-modular project is a single row with a null
/// <paramref name="ModuleId"/>. A project with modules gets one row per live module
/// (<paramref name="ModuleLabel"/> carries the display name, numbered for milestones), plus a
/// read-only row for pre-module ("unassigned") entries or entries on a since-removed module.</summary>
public record TimesheetRow(
    Guid ProjectId,
    Guid? ModuleId,
    string Code,
    string Name,
    string ClientName,
    string? ModuleLabel,
    Guid? DefaultBillingRoleId,
    bool ReadOnly);

public record BillableRoleOption(Guid Id, string DisplayName);

/// <summary>Everything the signed-in employee's monthly grid needs: the rows they may log
/// against this month, their existing entries, and the billable roles to pick from.</summary>
public class MyMonthTimesheet
{
    public required IReadOnlyList<TimesheetRow> Rows { get; init; }
    public required IReadOnlyList<TimeEntry> Entries { get; init; }
    public required IReadOnlyList<BillableRoleOption> BillableRoles { get; init; }
}

/// <summary>Read-side assembly of an employee's own timesheet grid. It reads only the
/// caller's own data, so no role is required.</summary>
public class TimesheetService(
    ICurrentUser currentUser,
    IAssignmentRepository assignments,
    ITimeEntryRepository entries,
    IModuleRepository modules,
    IProjectRepository projects,
    IRoleRepository roles)
{
    public async Task<MyMonthTimesheet> GetMyRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var me = currentUser.EmployeeId ?? throw new UnauthorizedAccessException("No signed-in employee.");

        var monthEntries = await entries.GetForEmployeeRangeAsync(me, from, to, cancellationToken);
        var projectsWithEntries = monthEntries.Select(e => e.ProjectId).ToHashSet();

        // Base rows: assignments that overlap the month, plus any project the employee already
        // has entries on this month (so historical rows never disappear).
        var myAssignments = await assignments.GetForEmployeeAsync(me, cancellationToken);
        var visible = myAssignments
            .Where(a => AcceptsTime(a, from, to) || projectsWithEntries.Contains(a.Assignment.ProjectId))
            .DistinctBy(a => a.Assignment.ProjectId)
            .OrderBy(a => a.ProjectCode)
            .ToList();

        // Modular projects expand to one row per live module.
        var liveModules = (await modules.GetForProjectsAsync(
                visible.Select(a => a.Assignment.ProjectId).ToList(), cancellationToken))
            .ToLookup(m => m.ProjectId);
        var labelByProject = await LoadBreakdownLabelsAsync(
            liveModules.Select(g => g.Key).ToList(), cancellationToken);

        var rows = new List<TimesheetRow>();
        foreach (var a in visible)
        {
            var projectId = a.Assignment.ProjectId;
            var projectModules = liveModules[projectId].ToList();
            var entriesHere = monthEntries.Where(e => e.ProjectId == projectId).ToList();

            if (projectModules.Count == 0)
            {
                rows.Add(Row(a, moduleId: null, moduleLabel: null, readOnly: false));
            }
            else
            {
                var label = labelByProject.GetValueOrDefault(projectId, BreakdownLabel.Module);
                rows.AddRange(projectModules.Select(m =>
                    Row(a, m.Id, DisplayName(m, label), readOnly: false)));

                // Pre-module entries surface as a read-only "unassigned" row (clearable, not editable).
                if (entriesHere.Any(e => e.ModuleId is null))
                {
                    rows.Add(Row(a, moduleId: null, moduleLabel: "(unassigned)", readOnly: true));
                }
            }

            // Entries on a since-removed module keep a read-only row so the month still adds up.
            var liveIds = projectModules.Select(m => m.Id).ToHashSet();
            var removedIds = entriesHere
                .Where(e => e.ModuleId is { } mid && !liveIds.Contains(mid))
                .Select(e => e.ModuleId!.Value)
                .Distinct();
            foreach (var removedId in removedIds)
            {
                var removed = await modules.GetByIdAsync(removedId, cancellationToken);
                rows.Add(Row(a, removedId, $"{removed?.Name ?? "?"} (removed)", readOnly: true));
            }
        }

        var billableRoles = (await roles.GetAllAsync(cancellationToken))
            .Where(r => r.IsBillable)
            .Select(r => new BillableRoleOption(r.Id, r.DisplayName))
            .OrderBy(r => r.DisplayName)
            .ToList();

        return new MyMonthTimesheet { Rows = rows, Entries = monthEntries, BillableRoles = billableRoles };
    }

    private static TimesheetRow Row(EmployeeAssignment a, Guid? moduleId, string? moduleLabel, bool readOnly) =>
        new(a.Assignment.ProjectId, moduleId, a.ProjectCode, a.ProjectName, a.ClientName,
            moduleLabel, a.Assignment.DefaultBillingRoleId, readOnly);

    /// <summary>Milestone-labeled projects show the number ("5. DMAP – Bug Fixes").</summary>
    private static string DisplayName(ProjectModule module, BreakdownLabel label) =>
        label == BreakdownLabel.Milestone ? $"{module.SortOrder}. {module.Name}" : module.Name;

    private async Task<Dictionary<Guid, BreakdownLabel>> LoadBreakdownLabelsAsync(
        IReadOnlyList<Guid> projectIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, BreakdownLabel>();
        foreach (var id in projectIds)
        {
            if (await projects.GetByIdAsync(id, cancellationToken) is { } project)
            {
                result[id] = project.BreakdownLabel;
            }
        }

        return result;
    }

    // A row shows when the project still accepts new time (not closed/archived — mirrors
    // TimeEntryService.RequireProjectAcceptsTime) and both the assignment and the project's
    // date range overlap the viewed window: an assignment removed mid-window still shows that
    // window (its EndDate falls on/after the window start), and a project is hidden before its
    // start date and after its end date (null dates are open-ended). Windows with logged
    // entries surface regardless.
    private static bool AcceptsTime(EmployeeAssignment a, DateOnly from, DateOnly to) =>
        a.ProjectStatus is not (ProjectStatus.Closed or ProjectStatus.Archived)
        && (a.Assignment.EndDate is null || a.Assignment.EndDate >= from)
        && (a.ProjectStartDate is null || a.ProjectStartDate <= to)
        && (a.ProjectEndDate is null || a.ProjectEndDate >= from);
}
