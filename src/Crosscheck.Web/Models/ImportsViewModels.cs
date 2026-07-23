using Microsoft.AspNetCore.Mvc.Rendering;
using Crosscheck.Application.Imports;

namespace Crosscheck.Web.Models;

public class ImportsIndexViewModel
{
    public IReadOnlyList<TimesheetImportSummary> Imports { get; init; } = [];
}

public class ImportReviewViewModel
{
    public required ImportReview Review { get; init; }
    public List<SelectListItem> ProjectOptions { get; init; } = [];
    public List<SelectListItem> RoleOptions { get; init; } = [];
}
