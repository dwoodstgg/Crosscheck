using Microsoft.AspNetCore.Mvc.Rendering;
using Waypoint.Application.TimeEntries;

namespace Waypoint.Web.Models;

public class ApprovalsViewModel
{
    public List<SelectListItem> ProjectOptions { get; init; } = [];
    public Guid? ProjectId { get; init; }
    public string? ProjectLabel { get; init; }
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public IReadOnlyList<ApprovalEntry> Entries { get; init; } = [];

    public int OpenCount => Entries.Count(e => e.Entry.Status == Domain.Enums.TimeEntryStatus.Open);
    public int ApprovedCount => Entries.Count(e => e.Entry.Status == Domain.Enums.TimeEntryStatus.Approved);
    public int InvoicedCount => Entries.Count(e => e.Entry.Status == Domain.Enums.TimeEntryStatus.Invoiced);
}
