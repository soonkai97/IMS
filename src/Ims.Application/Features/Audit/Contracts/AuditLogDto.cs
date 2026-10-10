namespace Ims.Application.Features.Audit;

public record AuditLogDto(long Id, string Actor, string Action, string EntityType, string EntityId, string? Before, string? After, DateTime At);
