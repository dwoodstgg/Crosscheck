using ProjectTango.Application.Employees;
using ProjectTango.Domain.Entities;

namespace ProjectTango.Web.Models;

public record EmployeeDetailsViewModel(EmployeeSummary Summary, IReadOnlyList<Role> AllRoles);
