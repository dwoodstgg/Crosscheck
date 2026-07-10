using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectTango.Application.Projects;
using ProjectTango.Domain.Entities;

namespace ProjectTango.Web.Models;

public class ProjectManageViewModel
{
    public required Project Project { get; init; }
    public required ProjectFormViewModel Form { get; init; }
    public required IReadOnlyList<RateCardSummary> Rates { get; init; }
    public required IReadOnlyList<AssignmentSummary> Assignments { get; init; }
    public Budget? Budget { get; init; }
    public IReadOnlyList<BudgetRevisionSummary> BudgetRevisions { get; init; } = [];
    public List<SelectListItem> BillableRoleOptions { get; init; } = [];

    /// <summary>Billing roles that have a rate card on this project — the only roles that can be a
    /// team member's default billing role.</summary>
    public List<SelectListItem> RateCardRoleOptions { get; init; } = [];

    public List<SelectListItem> EmployeeOptions { get; init; } = [];
}
