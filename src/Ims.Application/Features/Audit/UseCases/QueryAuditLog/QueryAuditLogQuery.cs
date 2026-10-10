namespace Ims.Application.Features.Audit;

public record QueryAuditLogQuery(AuditQueryFilter Filter) : IQuery<PagedResult<AuditLogDto>>;
