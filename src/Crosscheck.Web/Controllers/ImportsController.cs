using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Crosscheck.Application.Imports;
using Crosscheck.Domain;
using Crosscheck.Web.Models;

namespace Crosscheck.Web.Controllers;

/// <summary>Excel timesheet import: upload a workbook, review/edit every parsed line
/// (duplicates flagged, unmatched project labels mapped by hand and remembered), then
/// commit — or roll a committed import back while nothing is invoiced.</summary>
[Authorize(Roles = $"{RoleNames.Admin},{RoleNames.OperationsManager}")]
public class ImportsController(TimesheetImportService imports) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var summaries = await imports.ListAsync(cancellationToken);
        return View(new ImportsIndexViewModel { Imports = summaries });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(IFormFile? workbook, string? email, CancellationToken cancellationToken)
    {
        if (workbook is null || workbook.Length == 0)
        {
            TempData["Error"] = "Choose a timesheet workbook (.xlsx) to upload.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            TempData["Error"] = "Enter the employee's tenant email address.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            await using var stream = workbook.OpenReadStream();
            var importId = await imports.UploadAsync(stream, workbook.FileName, email, cancellationToken);
            return RedirectToAction(nameof(Review), new { id = importId });
        }
        catch (Exception ex) when (ex is DomainException or UnauthorizedAccessException)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    public async Task<IActionResult> Review(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var review = await imports.GetReviewAsync(id, cancellationToken);
            return View(new ImportReviewViewModel
            {
                Review = review,
                ProjectOptions = review.ProjectOptions
                    .OrderBy(p => p.Project.Code)
                    .Select(p => new SelectListItem($"{p.Project.Code} — {p.Project.Name}", p.Project.Id.ToString()))
                    .ToList(),
                RoleOptions = review.BillingRoleOptions
                    .OrderBy(r => r.DisplayName)
                    .Select(r => new SelectListItem(r.DisplayName, r.Id.ToString()))
                    .ToList(),
            });
        }
        catch (Exception ex) when (ex is DomainException or UnauthorizedAccessException)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRow(
        Guid rowId, Guid importId, Guid? projectId, Guid? billingRoleId, DateOnly entryDate,
        decimal hours, string? description, bool included, CancellationToken cancellationToken)
    {
        try
        {
            await imports.UpdateRowAsync(rowId, projectId, billingRoleId, entryDate, hours, description, included, cancellationToken);
        }
        catch (Exception ex) when (ex is DomainException or UnauthorizedAccessException)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToReview(importId, rowId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExcludeDuplicates(Guid importId, CancellationToken cancellationToken)
    {
        try
        {
            var excluded = await imports.ExcludeDuplicatesAsync(importId, cancellationToken);
            TempData["Notice"] = excluded == 0
                ? "No included duplicates to exclude."
                : $"Excluded {excluded} duplicate row{(excluded == 1 ? "" : "s")}.";
        }
        catch (Exception ex) when (ex is DomainException or UnauthorizedAccessException)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToReview(importId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Commit(Guid importId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await imports.CommitAsync(importId, cancellationToken);
            TempData["Notice"] =
                $"Imported {result.EntriesCreated} entr{(result.EntriesCreated == 1 ? "y" : "ies")} " +
                $"({result.TotalHours:0.##} hours)" +
                (result.AssignmentsCreated > 0 ? $", created {result.AssignmentsCreated} project assignment{(result.AssignmentsCreated == 1 ? "" : "s")}" : "") +
                (result.MappingsSaved > 0 ? $", remembered {result.MappingsSaved} project mapping{(result.MappingsSaved == 1 ? "" : "s")}" : "") + ".";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex) when (ex is DomainException or UnauthorizedAccessException)
        {
            TempData["Error"] = ex.Message;
            return RedirectToReview(importId);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rollback(Guid importId, CancellationToken cancellationToken)
    {
        try
        {
            await imports.RollbackAsync(importId, cancellationToken);
            TempData["Notice"] = "Import rolled back — its time entries were removed.";
        }
        catch (Exception ex) when (ex is DomainException or UnauthorizedAccessException)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Discard(Guid importId, CancellationToken cancellationToken)
    {
        try
        {
            await imports.DiscardAsync(importId, cancellationToken);
            TempData["Notice"] = "Import discarded.";
        }
        catch (Exception ex) when (ex is DomainException or UnauthorizedAccessException)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    private RedirectToActionResult RedirectToReview(Guid importId, Guid? rowId = null) =>
        RedirectToAction(nameof(Review), null, new { id = importId }, rowId is { } r ? $"row-{r}" : null);
}
