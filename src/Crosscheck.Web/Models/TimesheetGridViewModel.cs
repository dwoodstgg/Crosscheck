using Microsoft.AspNetCore.Mvc.Rendering;

namespace Crosscheck.Web.Models;

public class TimesheetGridViewModel
{
    /// <summary>"week" (default) or "month".</summary>
    public string ViewMode { get; init; } = "week";
    public bool IsWeek => ViewMode != "month";

    /// <summary>The user's saved layout, "grid" (default) or "daily". The grid renders
    /// server-side; the client switches to daily on load when this is "daily".</summary>
    public string InitialLayout { get; init; } = "grid";

    public string RangeLabel { get; init; } = "";

    /// <summary>Anchor dates for the prev/next/today links, in the current view's step.</summary>
    public DateOnly CurrentAnchor { get; init; }
    public DateOnly PrevAnchor { get; init; }
    public DateOnly NextAnchor { get; init; }
    public DateOnly TodayAnchor { get; init; }

    /// <summary>False when the signed-in user has no employee record yet (not provisioned).</summary>
    public bool HasEmployee { get; init; } = true;

    /// <summary>False for 1099 subcontractors — they're paid for hours worked only, so the
    /// leave rows, the holiday auto-credit, and the expected-hours yardstick are hidden.</summary>
    public bool ShowLeave { get; init; } = true;

    public List<DayColumn> Days { get; init; } = [];
    public List<TimesheetRowVm> Rows { get; init; } = [];
    public List<SelectListItem> BillableRoleOptions { get; init; } = [];

    /// <summary>Mon–Fri days in the visible range that aren't company holidays.</summary>
    public int WorkdayCount { get; init; }

    /// <summary>Company holidays in the range that fall on a weekday. Each auto-credits 8
    /// hours of Holiday leave when left empty, so they count toward the expected total.</summary>
    public int HolidayCount { get; init; }

    /// <summary>The range's expected total: 8 hours per Mon–Fri day, holidays included
    /// (an untouched holiday self-fills as Leave – Holiday). Weekly hours may vary
    /// (32 one week, 48 the next) — only this range total is the yardstick.</summary>
    public decimal ExpectedHours { get; init; }

    /// <summary>Week segments (Sunday-start) of the visible range, for the weekly-total
    /// footer row. Each holds the day columns it spans.</summary>
    public List<List<DayColumn>> Weeks
    {
        get
        {
            var weeks = new List<List<DayColumn>>();
            foreach (var day in Days)
            {
                if (weeks.Count == 0 || day.Date.DayOfWeek == DayOfWeek.Sunday)
                {
                    weeks.Add([]);
                }

                weeks[^1].Add(day);
            }

            return weeks;
        }
    }
}

public record DayColumn(int Day, DateOnly Date, string WeekdayLabel, bool IsWeekend, bool Locked,
    string? HolidayName = null)
{
    /// <summary>Stable per-column key (unique even when a week spans two months).</summary>
    public int DayNumber => Date.DayNumber;

    public bool IsHoliday => HolidayName is not null;
}

public class TimesheetRowVm
{
    public Guid ProjectId { get; init; }

    /// <summary>The module this row logs against; null on non-modular projects and on the
    /// read-only "unassigned" row of a modular one.</summary>
    public Guid? ModuleId { get; init; }

    /// <summary>Module display name shown after the project label — numbered on milestone
    /// projects ("5. DMAP – Bug Fixes"), "(unassigned)"/"… (removed)" on read-only rows.</summary>
    public string? ModuleLabel { get; init; }

    /// <summary>True for unassigned/removed-module rows: hours display but can't be edited.</summary>
    public bool ReadOnly { get; init; }

    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string ClientName { get; init; } = "";
    public Guid? DefaultBillingRoleId { get; init; }

    /// <summary>Day-of-month → the cell entry, when one exists.</summary>
    public Dictionary<int, CellVm> Cells { get; init; } = [];
}

public record CellVm(decimal Hours, Guid BillingRoleId, string Status, string? Notes, bool Locked);
