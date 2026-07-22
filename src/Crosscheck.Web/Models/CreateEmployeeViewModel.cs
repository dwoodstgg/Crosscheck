using System.ComponentModel.DataAnnotations;
using Crosscheck.Domain.Enums;

namespace Crosscheck.Web.Models;

public class CreateEmployeeViewModel
{
    [Required]
    [EmailAddress]
    [Display(Name = "Tenant email")]
    public string? Email { get; set; }

    [Required]
    [Display(Name = "Display name")]
    public string? DisplayName { get; set; }

    [Display(Name = "Employment type")]
    public EmploymentType EmploymentType { get; set; } = EmploymentType.Employee;
}
