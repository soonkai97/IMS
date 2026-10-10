using System.Text.Json;
using Ims.Domain;
using Microsoft.Extensions.Logging;
namespace Ims.Application.Features.Audit;

/// <summary>Stages an audit row in the current unit of work; it is saved together with the business change.</summary>
public class AuditTrail(IRepository<AuditLog> logs, ICurrentUser current, IClock clock, ILogger<AuditTrail> log) : IAuditTrail
{
    private static readonly JsonSerializerOptions Json = new() { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles };

    public void Record(string action, string entityType, string entityId, object? before = null, object? after = null, string? actor = null)
    {
        var who = actor ?? (current.IsAuthenticated ? current.Username : AuditEntities.SystemActor);
        logs.Add(AuditLog.Record(who, action, entityType, entityId,
            before is null ? null : JsonSerializer.Serialize(before, Json),
            after is null ? null : JsonSerializer.Serialize(after, Json),
            clock.UtcNow));
        log.LogInformation("Audit {Action} on {EntityType} {EntityId} by {Actor}", action, entityType, entityId, who);
    }
}
