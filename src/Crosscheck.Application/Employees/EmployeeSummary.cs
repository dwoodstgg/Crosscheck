using Crosscheck.Domain.Entities;

namespace Crosscheck.Application.Employees;

public record EmployeeSummary(Employee Employee, IReadOnlyList<string> RoleNames);
