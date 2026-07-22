using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Crosscheck.Application.Projects;
using Crosscheck.Application.TimeEntries;
using Crosscheck.Domain;
using Crosscheck.Domain.Enums;
using Crosscheck.Web.Models;

namespace Crosscheck.Web.Controllers;

/// <summary>Review and approve time (the billing decision) for a project and date range.
/// Approval sets hours_billed and locks worked hours; un-approval returns entries to open.</summary>
[Authorize(Roles = $"{RoleNames.Admin},{RoleNames.OperationsManager},{RoleNames.ProjectManager}")]
public class ApprovalsController(ApprovalService approvals, ProjectAdminService projectAdmin) : Controller
{
    public async Task<IActionResult> Index(Guid? projectId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = to ?? new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        var projectSummaries = await projectAdmin.ListAsync(cancellationToken);
        var options = projectSummaries
            .Select(p => new SelectListItem($"{p.Project.Code} — {p.Project.Name}", p.Project.Id.ToString(), p.Project.Id == projectId))
            .ToList();

        IReadOnlyList<ApprovalEntry> entries = [];
        string? label = null;
        if (projectId is not null)
        {
            entries = await approvals.ListForApprovalAsync(projectId.Value, start, end, cancellationToken);
            label = projectSummaries.FirstOrDefault(p => p.Project.Id == projectId)?.Project is { } proj
                ? $"{proj.Code} — {proj.Name}"
                : null;
        }

        return View(new ApprovalsViewModel
        {
            ProjectOptions = options,
            ProjectId = projectId,
            ProjectLabel = label,
            From = start,
            To = end,
            Entries = entries,
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Approve(
        Guid entryId, Guid projectId, DateOnly from, DateOnly to, decimal? billedHours, CancellationToken cancellationToken) =>
        RunAsync(() => approvals.ApproveAsync(entryId, billedHours, cancellationToken), projectId, from, to);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveAll(
        Guid projectId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        try
        {
            var entries = await approvals.ListForApprovalAsync(projectId, from, to, cancellationToken);
            var openIds = entries.Where(e => e.Entry.Status == TimeEntryStatus.Open).Select(e => e.Entry.Id).ToList();
            var approved = await approvals.ApproveManyAsync(openIds, cancellationToken);
            TempData["Notice"] = approved == 0 ? "No open entries to approve." : $"Approved {approved} entr{(approved == 1 ? "y" : "ies")}.";
        }
        catch (Exception ex) when (ex is DomainException or UnauthorizedAccessException)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { projectId, from, to });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Unapprove(
        Guid entryId, Guid projectId, DateOnly from, DateOnly to, string? comment, CancellationToken cancellationToken) =>
        RunAsync(() => approvals.UnapproveAsync(entryId, comment, cancellationToken), projectId, from, to);

    private async Task<IActionResult> RunAsync(Func<Task> action, Guid projectId, DateOnly from, DateOnly to)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is DomainException or UnauthorizedAccessException)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { projectId, from, to });
    }
}
