using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;

namespace ProjectTango.Application.Projects;

public record ProjectSummary(Project Project, string ClientName, string ProjectManagerName);

public interface IProjectRepository
{
    Task<IReadOnlyList<ProjectSummary>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Project?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Finds a project by (client, code). Codes are unique per client, so this is
    /// the lookup used to enforce that uniqueness on create/edit.</summary>
    Task<Project?> GetByClientAndCodeAsync(Guid clientId, string code, CancellationToken cancellationToken = default);

    Task AddAsync(Project project, CancellationToken cancellationToken = default);

    Task UpdateAsync(Project project, CancellationToken cancellationToken = default);

    Task SetStatusAsync(Guid projectId, ProjectStatus status, CancellationToken cancellationToken = default);
}
