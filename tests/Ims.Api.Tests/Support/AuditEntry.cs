namespace Ims.Api.Tests.Support;

public sealed record AuditEntry(string Actor, string Action, string EntityType, string EntityId, string? Before, string? After, DateTime At);
