using Microsoft.AspNetCore.Mvc.Rendering;

namespace ProjectTango.Web.Models;

public class TimesheetGridViewModel
{
    public int Year { get; init; }
    public int Month { get; init; }
    public string MonthLabel { get; init; } = "";
    public DateOnly PrevMonth { get; init; }
    public DateOnly NextMonth { get; init; }

    /// <summary>False when the signed-in user has no employee record yet (not provisioned).</summary>
    public bool HasEmployee { get; init; } = true;

    public List<DayColumn> Days { get; init; } = [];
    public List<TimesheetRowVm> Rows { get; init; } = [];
    public List<SelectListItem> BillableRoleOptions { get; init; } = [];
    public List<WindowVm> Windows { get; init; } = [];
    public bool CanManageWindows { get; init; }
}

public record DayColumn(int Day, DateOnly Date, string WeekdayLabel, bool IsWeekend, bool Locked);

public class TimesheetRowVm
{
    public Guid ProjectId { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public Guid? DefaultBillingRoleId { get; init; }

    /// <summary>Day-of-month → the cell entry, when one exists.</summary>
    public Dictionary<int, CellVm> Cells { get; init; } = [];
}

public record CellVm(decimal Hours, Guid BillingRoleId, string Status, string? Notes, bool Locked);

public record WindowVm(DateOnly Start, DateOnly End, string Label, bool Closed);
