using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.Employees;

public record EmployeeSummary(Employee Employee, IReadOnlyList<string> RoleNames);
