using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectTango.Application.TimeEntries;
using ProjectTango.Domain;
using ProjectTango.Domain.Enums;
using ProjectTango.Web.Models;

namespace ProjectTango.Web.Controllers;

/// <summary>The employee's own monthly timesheet grid (projects × days). Any signed-in
/// employee can record their own time; Ops/Admin also close and reopen the semi-monthly
/// windows here.</summary>
[Authorize]
public class TimesheetController(
    TimesheetService timesheet,
    TimeEntryService timeEntries,
    TimesheetPeriodService periods) : Controller
{
    public async Task<IActionResult> Index(int? year, int? month, CancellationToken cancellationToken)
    {
        var employeeId = User.GetEmployeeId();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var y = year ?? today.Year;
        var m = month ?? today.Month;
        var first = new DateOnly(y, m, 1);

        if (employeeId is null)
        {
            return View(new TimesheetGridViewModel { HasEmployee = false, Year = y, Month = m, MonthLabel = first.ToString("MMMM yyyy") });
        }

        var my = await timesheet.GetMyMonthAsync(y, m, cancellationToken);
        var monthPeriods = await periods.ListForMonthAsync(y, m, cancellationToken);
        var closedStarts = monthPeriods
            .Where(p => p.Status == TimesheetPeriodStatus.Closed)
            .Select(p => p.PeriodStart)
            .ToHashSet();

        bool IsLocked(DateOnly date) => closedStarts.Contains(SemiMonthlyPeriod.Containing(date).Start);

        var daysInMonth = DateTime.DaysInMonth(y, m);
        var days = Enumerable.Range(1, daysInMonth)
            .Select(d =>
            {
                var date = new DateOnly(y, m, d);
                var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                return new DayColumn(d, date, date.ToString("ddd", CultureInfo.InvariantCulture)[..2], isWeekend, IsLocked(date));
            })
            .ToList();

        var entriesByCell = my.Entries
            .GroupBy(e => e.ProjectId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(e => e.EntryDate.Day, e => e));

        var rows = my.Projects.Select(p =>
        {
            var cells = new Dictionary<int, CellVm>();
            if (entriesByCell.TryGetValue(p.ProjectId, out var byDay))
            {
                foreach (var (day, e) in byDay)
                {
                    cells[day] = new CellVm(e.HoursWorked, e.BillingRoleId, DbStatus(e.Status), e.Notes, IsLocked(e.EntryDate));
                }
            }

            return new TimesheetRowVm
            {
                ProjectId = p.ProjectId,
                Code = p.Code,
                Name = p.Name,
                DefaultBillingRoleId = p.DefaultBillingRoleId ?? my.BillableRoles.FirstOrDefault()?.Id,
                Cells = cells,
            };
        }).ToList();

        var windows = new[] { SemiMonthlyPeriod.Containing(first), SemiMonthlyPeriod.Containing(new DateOnly(y, m, daysInMonth)) }
            .DistinctBy(w => w.Start)
            .Select(w => new WindowVm(w.Start, w.End, $"{w.Start:MMM d}–{w.End:d}", closedStarts.Contains(w.Start)))
            .ToList();

        var model = new TimesheetGridViewModel
        {
            Year = y,
            Month = m,
            MonthLabel = first.ToString("MMMM yyyy"),
            PrevMonth = first.AddMonths(-1),
            NextMonth = first.AddMonths(1),
            Days = days,
            Rows = rows,
            BillableRoleOptions = my.BillableRoles.Select(r => new SelectListItem(r.DisplayName, r.Id.ToString())).ToList(),
            Windows = windows,
            CanManageWindows = User.IsInRole(RoleNames.OperationsManager) || User.IsInRole(RoleNames.Admin),
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveHours(
        Guid projectId, DateOnly date, decimal hours, Guid billingRoleId, string? notes, CancellationToken cancellationToken)
    {
        try
        {
            var entry = await timeEntries.SaveHoursAsync(projectId, date, hours, billingRoleId, notes, cancellationToken: cancellationToken);
            return Json(new { ok = true, hours = entry?.HoursWorked ?? 0m, cleared = entry is null });
        }
        catch (Exception ex) when (ex is DomainException or UnauthorizedAccessException)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> CloseWindow(DateOnly periodStart, int year, int month, CancellationToken cancellationToken) =>
        ManageWindowAsync(() => periods.CloseAsync(periodStart, cancellationToken), year, month);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ReopenWindow(DateOnly periodStart, int year, int month, CancellationToken cancellationToken) =>
        ManageWindowAsync(() => periods.ReopenAsync(periodStart, cancellationToken), year, month);

    private async Task<IActionResult> ManageWindowAsync(Func<Task> action, int year, int month)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is DomainException or UnauthorizedAccessException)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { year, month });
    }

    private static string DbStatus(TimeEntryStatus status) => status.ToString().ToLowerInvariant();
}
