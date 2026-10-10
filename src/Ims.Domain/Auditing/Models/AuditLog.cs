namespace Ims.Domain;

/// <summary>Append-only (AUTH-03). Enforced by the DbContext and a database trigger; there is no way to change a row once written.</summary>
public class AuditLog : AuditableEntity
{
    private AuditLog() { }

    public long Id { get; private set; }
    public string Actor { get; private set; } = "";
    public string Action { get; private set; } = "";
    public string EntityType { get; private set; } = "";
    public string EntityId { get; private set; } = "";
    public string? Before { get; private set; }
    public string? After { get; private set; }
    public DateTime At { get; private set; }

    public static AuditLog Record(string actor, string action, string entityType, string entityId, string? before, string? after, DateTime at) =>
        new() { Actor = actor, Action = action, EntityType = entityType, EntityId = entityId, Before = before, After = after, At = at };
}
