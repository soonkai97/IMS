namespace Ims.Application.Features.Audit;

public sealed record QueryAuditLogQuery(AuditQueryFilter Filter) : IQuery<PagedResult<AuditLogDto>>;
