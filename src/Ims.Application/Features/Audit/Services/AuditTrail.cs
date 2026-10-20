using System.Text.Json;
using Ims.Domain;
using Microsoft.Extensions.Logging;
namespace Ims.Application.Features.Audit;

/// <summary>Stages an audit row in the current unit of work; it is saved together with the business change.</summary>
public class AuditTrail(IRepository<AuditLog> logs, ICurrentUser current, IClock clock, ILogger<AuditTrail> log) : IAuditTrail
{
    // Enums are written as names ("Status": "Submitted"), so the Details view of the audit log reads in plain words.
    private static readonly JsonSerializerOptions Json = new()
    {
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public void Record(string action, string entityType, string entityId, object? before = null, object? after = null, string? actor = null)
    {
        var who = actor ?? (current.IsAuthenticated ? current.Username : AuditEntities.SystemActor);
        logs.Add(AuditLog.Record(who, action, entityType, entityId,
            before is null ? null : JsonSerializer.Serialize(before, Json),
            after is null ? null : JsonSerializer.Serialize(after, Json),
            clock.UtcNow));
        log.LogInformation("Audit {Action} on {EntityType} {EntityId} by {Actor}", LogSanitizer.Clean(action), LogSanitizer.Clean(entityType), LogSanitizer.Clean(entityId), LogSanitizer.Clean(who));
    }
}
