using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.Projects;

/// <summary><paramref name="HasTimeEntries"/> is true when the employee has logged any time on
/// the project. Removing such an assignment soft-deactivates it (its logged time is preserved);
/// an assignment with no logged time is hard-deleted instead.</summary>
public record AssignmentSummary(
    ProjectAssignment Assignment, string EmployeeName, string? DefaultRoleName, bool HasTimeEntries = false);

/// <summary>An employee's assignment joined with the project it grants access to (for the
/// employee's own timesheet grid).</summary>
public record EmployeeAssignment(ProjectAssignment Assignment, string ProjectCode, string ProjectName, string ClientName);

public interface IAssignmentRepository
{
    Task<IReadOnlyList<AssignmentSummary>> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeAssignment>> GetForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    Task<ProjectAssignment?> GetAsync(Guid assignmentId, CancellationToken cancellationToken = default);

    Task<ProjectAssignment?> GetByProjectAndEmployeeAsync(Guid projectId, Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>True when the employee has any time entry (any status) on the project — gates removal.</summary>
    Task<bool> HasTimeEntriesAsync(Guid projectId, Guid employeeId, CancellationToken cancellationToken = default);

    Task AddAsync(ProjectAssignment assignment, CancellationToken cancellationToken = default);

    Task UpdateAsync(ProjectAssignment assignment, CancellationToken cancellationToken = default);

    /// <summary>Hard-deletes an assignment. Only called for a never-used assignment (no time
    /// entries); the service enforces that guard.</summary>
    Task DeleteAsync(Guid assignmentId, CancellationToken cancellationToken = default);
}
