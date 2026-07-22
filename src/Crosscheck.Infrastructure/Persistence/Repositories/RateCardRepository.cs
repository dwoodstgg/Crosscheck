using Dapper;
using Npgsql;
using Crosscheck.Application.Projects;
using Crosscheck.Domain.Entities;

namespace Crosscheck.Infrastructure.Persistence.Repositories;

public class RateCardRepository(NpgsqlDataSource dataSource) : IRateCardRepository
{
    public async Task<IReadOnlyList<RateCardSummary>> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<RateCardRow>(new CommandDefinition(
            """
            SELECT rc.id, rc.project_id, rc.role_id, rc.hourly_rate,
                   r.display_name AS role_name,
                   EXISTS (
                       SELECT 1 FROM time_entries te
                       WHERE te.project_id = rc.project_id AND te.billing_role_id = rc.role_id
                         AND te.status = 'invoiced'
                   ) AS has_billed_time,
                   EXISTS (
                       SELECT 1 FROM time_entries te
                       WHERE te.project_id = rc.project_id AND te.billing_role_id = rc.role_id
                   ) AS has_logged_time
            FROM project_rate_cards rc
            JOIN roles r ON r.id = rc.role_id
            WHERE rc.project_id = @projectId AND rc.deleted_at IS NULL
            ORDER BY r.display_name
            """,
            new { projectId },
            cancellationToken: cancellationToken));
        return rows.Select(row => new RateCardSummary(ToEntity(row), row.RoleName!, row.HasBilledTime, row.HasLoggedTime)).ToList();
    }

    public async Task<ProjectRateCard?> GetByIdAsync(Guid rateCardId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<RateCardRow>(new CommandDefinition(
            """
            SELECT id, project_id, role_id, hourly_rate
            FROM project_rate_cards
            WHERE id = @rateCardId AND deleted_at IS NULL
            """,
            new { rateCardId },
            cancellationToken: cancellationToken));
        return row is null ? null : ToEntity(row);
    }

    public async Task<ProjectRateCard?> GetForRoleAsync(Guid projectId, Guid roleId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<RateCardRow>(new CommandDefinition(
            """
            SELECT id, project_id, role_id, hourly_rate
            FROM project_rate_cards
            WHERE project_id = @projectId AND role_id = @roleId AND deleted_at IS NULL
            """,
            new { projectId, roleId },
            cancellationToken: cancellationToken));
        return row is null ? null : ToEntity(row);
    }

    public async Task AddAsync(ProjectRateCard rateCard, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO project_rate_cards (id, project_id, role_id, hourly_rate)
            VALUES (@Id, @ProjectId, @RoleId, @HourlyRate)
            """,
            new
            {
                rateCard.Id,
                rateCard.ProjectId,
                rateCard.RoleId,
                rateCard.HourlyRate,
            },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> HasInvoicedTimeAsync(Guid projectId, Guid roleId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS (
                SELECT 1 FROM time_entries
                WHERE project_id = @projectId AND billing_role_id = @roleId
                  AND status = 'invoiced'
            )
            """,
            new { projectId, roleId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> HasLoggedTimeAsync(Guid projectId, Guid roleId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS (
                SELECT 1 FROM time_entries
                WHERE project_id = @projectId AND billing_role_id = @roleId
            )
            """,
            new { projectId, roleId },
            cancellationToken: cancellationToken));
    }

    public async Task CorrectAsync(Guid rateCardId, decimal hourlyRate, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE project_rate_cards SET hourly_rate = @hourlyRate WHERE id = @rateCardId AND deleted_at IS NULL",
            new { rateCardId, hourlyRate },
            cancellationToken: cancellationToken));
    }

    public async Task SoftDeleteAsync(Guid rateCardId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE project_rate_cards SET deleted_at = now() WHERE id = @rateCardId AND deleted_at IS NULL",
            new { rateCardId },
            cancellationToken: cancellationToken));
    }

    public async Task<decimal?> ResolveAsync(Guid projectId, Guid? moduleId, Guid roleId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition(
            """
            SELECT COALESCE(
                (SELECT mr.hourly_rate FROM project_module_rates mr
                 WHERE mr.module_id = @moduleId AND mr.role_id = @roleId AND mr.deleted_at IS NULL),
                (SELECT mr.hourly_rate FROM project_module_rates mr
                 WHERE mr.module_id = @moduleId AND mr.role_id IS NULL AND mr.deleted_at IS NULL),
                (SELECT rc.hourly_rate FROM project_rate_cards rc
                 WHERE rc.project_id = @projectId AND rc.role_id = @roleId AND rc.deleted_at IS NULL))
            """,
            new { projectId, moduleId, roleId },
            cancellationToken: cancellationToken));
    }

    private static ProjectRateCard ToEntity(RateCardRow row) => new()
    {
        Id = row.Id,
        ProjectId = row.ProjectId,
        RoleId = row.RoleId,
        HourlyRate = row.HourlyRate,
    };

    private sealed class RateCardRow
    {
        public Guid Id { get; set; }
        public Guid ProjectId { get; set; }
        public Guid RoleId { get; set; }
        public decimal HourlyRate { get; set; }
        public string? RoleName { get; set; }
        public bool HasBilledTime { get; set; }
        public bool HasLoggedTime { get; set; }
    }
}
