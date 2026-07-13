using System.Text.Json;
using Dapper;
using Npgsql;
using Waypoint.Application.Common;

namespace Waypoint.Infrastructure.Persistence.Repositories;

public class AuditLogRepository(NpgsqlDataSource dataSource) : IAuditLog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO audit_log (actor_employee_id, action, entity_type, entity_id, details)
            VALUES (@ActorEmployeeId, @Action, @EntityType, @EntityId, @details::jsonb)
            """,
            new
            {
                auditEvent.ActorEmployeeId,
                auditEvent.Action,
                auditEvent.EntityType,
                auditEvent.EntityId,
                details = auditEvent.Details is null ? null : JsonSerializer.Serialize(auditEvent.Details, JsonOptions),
            },
            cancellationToken: cancellationToken));
    }
}
