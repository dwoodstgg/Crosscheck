namespace Waypoint.Application.Common;

public record AuditEvent(
    Guid? ActorEmployeeId,
    string Action,
    string EntityType,
    Guid? EntityId,
    object? Details = null);

/// <summary>Append-only audit trail for sensitive mutations. Financial and permission
/// changes must always write an event.</summary>
public interface IAuditLog
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}
