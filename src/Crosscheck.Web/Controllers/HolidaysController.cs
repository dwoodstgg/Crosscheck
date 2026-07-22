using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Crosscheck.Application.Holidays;
using Crosscheck.Domain;

namespace Crosscheck.Web.Controllers;

/// <summary>Admin management of the company holiday calendar. Holidays grey out on every
/// employee's timesheet and reduce the month's expected workday hours; they never block
/// logging time.</summary>
[Authorize(Roles = RoleNames.Admin)]
public class HolidaysController(HolidayService holidayService) : Controller
{
    public async Task<IActionResult> Index(int? year, CancellationToken cancellationToken)
    {
        var shown = year ?? DateTime.Today.Year;
        ViewBag.Year = shown;
        var holidays = await holidayService.ListYearAsync(shown, cancellationToken);
        return View(holidays);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(DateOnly date, string name, CancellationToken cancellationToken)
    {
        try
        {
            var holiday = await holidayService.AddAsync(date, name, cancellationToken);
            TempData["Ok"] = $"Added {holiday.Name} on {holiday.Date:MMMM d, yyyy}.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { year = date.Year });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CopyFromPreviousYear(int year, CancellationToken cancellationToken)
    {
        try
        {
            var result = await holidayService.CopyFromPreviousYearAsync(year, cancellationToken);
            TempData[result.Added > 0 ? "Ok" : "Error"] = result switch
            {
                { Added: 0, Skipped: 0 } => $"{year - 1} has no holidays to copy.",
                { Added: 0 } => $"All {result.Skipped} holidays from {year - 1} already exist in {year}.",
                { Skipped: 0 } => $"Copied {result.Added} holidays from {year - 1}.",
                _ => $"Copied {result.Added} holidays from {year - 1}; {result.Skipped} already existed.",
            };
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { year });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(Guid id, int year, CancellationToken cancellationToken)
    {
        try
        {
            await holidayService.RemoveAsync(id, cancellationToken);
            TempData["Ok"] = "Holiday removed.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { year });
    }
}
