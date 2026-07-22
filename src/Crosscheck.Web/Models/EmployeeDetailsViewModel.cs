using System.ComponentModel.DataAnnotations;
using Crosscheck.Application.Employees;
using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;

namespace Crosscheck.Web.Models;

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
