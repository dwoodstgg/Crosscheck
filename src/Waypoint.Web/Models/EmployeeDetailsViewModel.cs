using System.ComponentModel.DataAnnotations;
using Waypoint.Application.Employees;
using Waypoint.Domain.Entities;
using Waypoint.Domain.Enums;

namespace Waypoint.Web.Models;

public record EmployeeDetailsViewModel(
    EmployeeSummary Summary,
    IReadOnlyList<Role> AllRoles,
    IReadOnlySet<Guid> HeldRoleIds,
    EmployeeProfileViewModel Profile);

public class EmployeeProfileViewModel
{
    [Required]
    [Display(Name = "Display name")]
    public string? DisplayName { get; set; }

    [Display(Name = "Employment type")]
    public EmploymentType EmploymentType { get; set; } = EmploymentType.Employee;
}
