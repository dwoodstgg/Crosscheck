using Dapper;
using Npgsql;
using Crosscheck.Application.Projects;
using Crosscheck.Domain.Entities;

namespace Crosscheck.Infrastructure.Persistence.Repositories;

public class ModuleRepository(NpgsqlDataSource dataSource) : IModuleRepository
{
    private const string SelectColumns =
        """
        SELECT id, project_id, name, sort_order, hours, amount, created_at, updated_at, deleted_at
        FROM project_modules
        """;

    public async Task<IReadOnlyList<ProjectModule>> GetForProjectAsync(Guid projectId, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ModuleRow>(new CommandDefinition(
            $"{SelectColumns} WHERE project_id = @projectId {(includeDeleted ? "" : "AND deleted_at IS NULL")} ORDER BY sort_order, name",
            new { projectId },
            cancellationToken: cancellationToken));
        return await LoadAllocationsAsync(connection, rows.Select(ToEntity).ToList(), cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectModule>> GetForProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default)
    {
        if (projectIds.Count == 0)
        {
            return [];
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ModuleRow>(new CommandDefinition(
            $"{SelectColumns} WHERE project_id = ANY(@projectIds) AND deleted_at IS NULL ORDER BY sort_order, name",
            new { projectIds = projectIds.ToArray() },
            cancellationToken: cancellationToken));
        return await LoadAllocationsAsync(connection, rows.Select(ToEntity).ToList(), cancellationToken);
    }

    public async Task<ProjectModule?> GetByIdAsync(Guid moduleId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ModuleRow>(new CommandDefinition(
            $"{SelectColumns} WHERE id = @moduleId",
            new { moduleId },
            cancellationToken: cancellationToken));
        if (row is null)
        {
            return null;
        }

        var modules = await LoadAllocationsAsync(connection, [ToEntity(row)], cancellationToken);
        return modules[0];
    }

    public async Task<bool> HasLiveModulesAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM project_modules WHERE project_id = @projectId AND deleted_at IS NULL)",
            new { projectId },
            cancellationToken: cancellationToken));
    }

    public async Task AddAsync(ProjectModule module, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO project_modules (id, project_id, name, sort_order, hours, amount)
            VALUES (@Id, @ProjectId, @Name, @SortOrder, @Hours, @Amount)
            """,
            new { module.Id, module.ProjectId, module.Name, module.SortOrder, module.Hours, module.Amount },
            transaction, cancellationToken: cancellationToken));
        await InsertAllocationsAsync(connection, transaction, module.Id, module.Allocations, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateAsync(ProjectModule module, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE project_modules SET
                name = @Name,
                sort_order = @SortOrder,
                hours = @Hours,
                amount = @Amount,
                updated_at = now()
            WHERE id = @Id AND deleted_at IS NULL
            """,
            new { module.Id, module.Name, module.SortOrder, module.Hours, module.Amount },
            cancellationToken: cancellationToken));
    }

    public async Task ReplaceAllocationsAsync(Guid moduleId, IReadOnlyList<ModuleRoleAllocation> allocations, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Replace wholesale — the caller passes the desired set (budget allocation pattern).
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM project_module_allocations WHERE module_id = @moduleId",
            new { moduleId },
            transaction, cancellationToken: cancellationToken));
        await InsertAllocationsAsync(connection, transaction, moduleId, allocations, cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE project_modules SET updated_at = now() WHERE id = @moduleId",
            new { moduleId },
            transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SoftDeleteAsync(Guid moduleId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE project_modules SET deleted_at = now(), updated_at = now() WHERE id = @moduleId AND deleted_at IS NULL",
            new { moduleId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> HasLoggedTimeAsync(Guid moduleId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM time_entries WHERE module_id = @moduleId)",
            new { moduleId },
            cancellationToken: cancellationToken));
    }

    // Rate overrides ------------------------------------------------------------

    public async Task<IReadOnlyList<ModuleRateSummary>> GetRatesAsync(Guid moduleId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ModuleRateRow>(new CommandDefinition(
            """
            SELECT mr.id, mr.module_id, mr.role_id, mr.hourly_rate,
                   r.display_name AS role_name,
                   EXISTS (
                       SELECT 1 FROM time_entries te
                       WHERE te.module_id = mr.module_id
                         AND (mr.role_id IS NULL OR te.billing_role_id = mr.role_id)
                         AND te.status = 'invoiced'
                   ) AS has_billed_time,
                   EXISTS (
                       SELECT 1 FROM time_entries te
                       WHERE te.module_id = mr.module_id
                         AND (mr.role_id IS NULL OR te.billing_role_id = mr.role_id)
                   ) AS has_logged_time
            FROM project_module_rates mr
            LEFT JOIN roles r ON r.id = mr.role_id
            WHERE mr.module_id = @moduleId AND mr.deleted_at IS NULL
            ORDER BY mr.role_id IS NOT NULL, r.display_name
            """,
            new { moduleId },
            cancellationToken: cancellationToken));
        return rows.Select(row => new ModuleRateSummary(ToRateEntity(row), row.RoleName, row.HasBilledTime, row.HasLoggedTime)).ToList();
    }

    public async Task<ProjectModuleRate?> GetRateByIdAsync(Guid moduleRateId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ModuleRateRow>(new CommandDefinition(
            """
            SELECT id, module_id, role_id, hourly_rate
            FROM project_module_rates
            WHERE id = @moduleRateId AND deleted_at IS NULL
            """,
            new { moduleRateId },
            cancellationToken: cancellationToken));
        return row is null ? null : ToRateEntity(row);
    }

    public async Task<ProjectModuleRate?> GetRateForRoleAsync(Guid moduleId, Guid? roleId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ModuleRateRow>(new CommandDefinition(
            """
            SELECT id, module_id, role_id, hourly_rate
            FROM project_module_rates
            WHERE module_id = @moduleId AND role_id IS NOT DISTINCT FROM @roleId AND deleted_at IS NULL
            """,
            new { moduleId, roleId },
            cancellationToken: cancellationToken));
        return row is null ? null : ToRateEntity(row);
    }

    public async Task AddRateAsync(ProjectModuleRate rate, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO project_module_rates (id, module_id, role_id, hourly_rate)
            VALUES (@Id, @ModuleId, @RoleId, @HourlyRate)
            """,
            new { rate.Id, rate.ModuleId, rate.RoleId, rate.HourlyRate },
            cancellationToken: cancellationToken));
    }

    public async Task CorrectRateAsync(Guid moduleRateId, decimal hourlyRate, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE project_module_rates SET hourly_rate = @hourlyRate WHERE id = @moduleRateId AND deleted_at IS NULL",
            new { moduleRateId, hourlyRate },
            cancellationToken: cancellationToken));
    }

    public async Task SoftDeleteRateAsync(Guid moduleRateId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE project_module_rates SET deleted_at = now() WHERE id = @moduleRateId AND deleted_at IS NULL",
            new { moduleRateId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> HasInvoicedTimeAsync(Guid moduleId, Guid? roleId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS (
                SELECT 1 FROM time_entries
                WHERE module_id = @moduleId
                  AND (@roleId::uuid IS NULL OR billing_role_id = @roleId)
                  AND status = 'invoiced'
            )
            """,
            new { moduleId, roleId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> HasLoggedTimeForRoleAsync(Guid moduleId, Guid? roleId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS (
                SELECT 1 FROM time_entries
                WHERE module_id = @moduleId
                  AND (@roleId::uuid IS NULL OR billing_role_id = @roleId)
            )
            """,
            new { moduleId, roleId },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<Guid>> GetLoggedRoleIdsAsync(Guid moduleId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var ids = await connection.QueryAsync<Guid>(new CommandDefinition(
            "SELECT DISTINCT billing_role_id FROM time_entries WHERE module_id = @moduleId",
            new { moduleId },
            cancellationToken: cancellationToken));
        return ids.ToList();
    }

    // Helpers -------------------------------------------------------------------

    private static async Task<IReadOnlyList<ProjectModule>> LoadAllocationsAsync(
        NpgsqlConnection connection, List<ProjectModule> modules, CancellationToken cancellationToken)
    {
        if (modules.Count == 0)
        {
            return modules;
        }

        var allocations = await connection.QueryAsync<AllocationRow>(new CommandDefinition(
            """
            SELECT a.id, a.module_id, a.role_id, a.hours, r.display_name AS role_name
            FROM project_module_allocations a
            JOIN roles r ON r.id = a.role_id
            WHERE a.module_id = ANY(@moduleIds)
            ORDER BY r.display_name
            """,
            new { moduleIds = modules.Select(m => m.Id).ToArray() },
            cancellationToken: cancellationToken));

        var byModule = allocations.ToLookup(a => a.ModuleId);
        foreach (var module in modules)
        {
            module.Allocations = byModule[module.Id].Select(a => new ModuleRoleAllocation
            {
                Id = a.Id,
                ModuleId = a.ModuleId,
                RoleId = a.RoleId,
                Hours = a.Hours,
                RoleName = a.RoleName,
            }).ToList();
        }

        return modules;
    }

    private static async Task InsertAllocationsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid moduleId,
        IReadOnlyList<ModuleRoleAllocation> allocations, CancellationToken cancellationToken)
    {
        foreach (var allocation in allocations)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO project_module_allocations (id, module_id, role_id, hours)
                VALUES (@Id, @ModuleId, @RoleId, @Hours)
                """,
                new { allocation.Id, ModuleId = moduleId, allocation.RoleId, allocation.Hours },
                transaction, cancellationToken: cancellationToken));
        }
    }

    private static ProjectModule ToEntity(ModuleRow row) => new()
    {
        Id = row.Id,
        ProjectId = row.ProjectId,
        Name = row.Name,
        SortOrder = row.SortOrder,
        Hours = row.Hours,
        Amount = row.Amount,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt,
        DeletedAt = row.DeletedAt,
    };

    private static ProjectModuleRate ToRateEntity(ModuleRateRow row) => new()
    {
        Id = row.Id,
        ModuleId = row.ModuleId,
        RoleId = row.RoleId,
        HourlyRate = row.HourlyRate,
    };

    private sealed class ModuleRow
    {
        public Guid Id { get; set; }
        public Guid ProjectId { get; set; }
        public string Name { get; set; } = null!;
        public int SortOrder { get; set; }
        public decimal? Hours { get; set; }
        public decimal? Amount { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public DateTimeOffset? DeletedAt { get; set; }
    }

    private sealed class AllocationRow
    {
        public Guid Id { get; set; }
        public Guid ModuleId { get; set; }
        public Guid RoleId { get; set; }
        public decimal Hours { get; set; }
        public string? RoleName { get; set; }
    }

    private sealed class ModuleRateRow
    {
        public Guid Id { get; set; }
        public Guid ModuleId { get; set; }
        public Guid? RoleId { get; set; }
        public decimal HourlyRate { get; set; }
        public string? RoleName { get; set; }
        public bool HasBilledTime { get; set; }
        public bool HasLoggedTime { get; set; }
    }
}
