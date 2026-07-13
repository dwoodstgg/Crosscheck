using Waypoint.Domain.Entities;

namespace Waypoint.Application.Employees;

public record EmployeeSummary(Employee Employee, IReadOnlyList<string> RoleNames);
