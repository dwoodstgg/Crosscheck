using Crosscheck.Application.Common;
using Crosscheck.Application.Employees;
using Crosscheck.Application.Roles;
using Crosscheck.Domain;
using Crosscheck.Domain.Entities;

namespace Crosscheck.Application.Projects;

/// <summary>Team assignment. One row per person per project — an employee stays active for the
/// life of the project. Removing hard-deletes a never-used assignment or soft-deactivates one
/// with logged time (sets EndDate); assigning the same person again reopens it.</summary>
public class AssignmentService(
    ICurrentUser currentUser,
    IProjectRepository projects,
    IAssignmentRepository assignments,
    IEmployeeRepository employees,
    IRoleRepository roles,
    IRateCardRepository rateCards,
    IAuditLog audit)
{
    public async Task<IReadOnlyList<AssignmentSummary>> ListForProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(RoleNames.OperationsManager, RoleNames.ProjectManager);
        return await assignments.GetForProjectAsync(projectId, cancellationToken);
    }

    public async Task AssignAsync(
        Guid projectId, Guid employeeId, Guid? defaultBillingRoleId,
        CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(projectId, cancellationToken)
            ?? throw new DomainException("Unknown project.");
        var adminOverride = currentUser.RequireCanManage(project);

        var employee = await employees.GetByIdAsync(employeeId, cancellationToken);
        if (employee is null || !employee.IsActive)
        {
            throw new DomainException("Employee must exist and be active.");
        }

        if (defaultBillingRoleId is not null)
        {
            var role = await roles.GetByIdAsync(defaultBillingRoleId.Value, cancellationToken)
                ?? throw new DomainException("Unknown billing role.");
            if (!role.IsBillable)
            {
                throw new DomainException($"{role.Name} is not a billable role.");
            }

            // The default billing role must be priced on this project — a person can only default
            // to a role the rate card covers (design rule 3: rates resolve on the project rate card).
            var rate = await rateCards.GetForRoleAsync(projectId, defaultBillingRoleId.Value, cancellationToken);
            if (rate is null)
            {
                throw new DomainException(
                    $"{role.DisplayName} has no rate card on this project — add a rate for it before assigning it as a default billing role.");
            }
        }

        var existing = await assignments.GetByProjectAndEmployeeAsync(projectId, employeeId, cancellationToken);
        if (existing is not null)
        {
            if (existing.EndDate is null)
            {
                throw new DomainException($"{employee.DisplayName} is already assigned to this project.");
            }

            // Reopen the ended assignment (unique row per person per project).
            existing.EndDate = null;
            existing.DefaultBillingRoleId = defaultBillingRoleId;
            await assignments.UpdateAsync(existing, cancellationToken);

            await audit.WriteAsync(new AuditEvent(
                currentUser.EmployeeId, "assignment.reopened", "project", projectId,
                new { Employee = employee.DisplayName, adminOverride }), cancellationToken);
            return;
        }

        var assignment = new ProjectAssignment
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            EmployeeId = employeeId,
            DefaultBillingRoleId = defaultBillingRoleId,
        };
        await assignments.AddAsync(assignment, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "assignment.added", "project", projectId,
            new { Employee = employee.DisplayName, adminOverride }), cancellationToken);
    }

    /// <summary>Takes an employee off a project. If they've logged no time the roster entry is
    /// hard-deleted (a mistaken add); if they have, it's soft-deactivated (EndDate set) so their
    /// logged time is preserved but they drop off the active team and can't log new time.</summary>
    public async Task RemoveAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        var assignment = await assignments.GetAsync(assignmentId, cancellationToken)
            ?? throw new DomainException("Unknown assignment.");
        var project = await projects.GetByIdAsync(assignment.ProjectId, cancellationToken)
            ?? throw new DomainException("Unknown project.");
        var adminOverride = currentUser.RequireCanManage(project);

        if (assignment.EndDate is not null)
        {
            throw new DomainException("Assignment is already removed.");
        }

        if (await assignments.HasTimeEntriesAsync(assignment.ProjectId, assignment.EmployeeId, cancellationToken))
        {
            assignment.EndDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);
            await assignments.UpdateAsync(assignment, cancellationToken);

            await audit.WriteAsync(new AuditEvent(
                currentUser.EmployeeId, "assignment.ended", "project", assignment.ProjectId,
                new { assignment.EmployeeId, adminOverride }), cancellationToken);
            return;
        }

        await assignments.DeleteAsync(assignmentId, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "assignment.removed", "project", assignment.ProjectId,
            new { assignment.EmployeeId, adminOverride }), cancellationToken);
    }

    /// <summary>Restores a soft-deactivated assignment (clears EndDate).</summary>
    public async Task ReactivateAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        var assignment = await assignments.GetAsync(assignmentId, cancellationToken)
            ?? throw new DomainException("Unknown assignment.");
        var project = await projects.GetByIdAsync(assignment.ProjectId, cancellationToken)
            ?? throw new DomainException("Unknown project.");
        var adminOverride = currentUser.RequireCanManage(project);

        if (assignment.EndDate is null)
        {
            throw new DomainException("Assignment is already active.");
        }

        assignment.EndDate = null;
        await assignments.UpdateAsync(assignment, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "assignment.reopened", "project", assignment.ProjectId,
            new { assignment.EmployeeId, adminOverride }), cancellationToken);
    }
}
