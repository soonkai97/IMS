namespace Ims.Application;

/// <summary>Explicit business audit events (logins, status changes). Row-level changes are captured by the persistence layer.</summary>
public interface IAuditTrail
{
    void Record(string action, string entityType, string entityId, object? before = null, object? after = null, string? actor = null);
}
