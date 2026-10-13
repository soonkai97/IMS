using Ims.Domain;
namespace Ims.Application.Features.Audit;

public class QueryAuditLogHandler(IReadRepository<AuditLog> logs, ICursorProtector cursors) : IQueryHandler<QueryAuditLogQuery, PagedResult<AuditLogDto>>
{
    public async Task<PagedResult<AuditLogDto>> HandleAsync(QueryAuditLogQuery q, CancellationToken ct)
    {
        var after = CursorToken.Decode(cursors, CursorLists.Audit, q.Filter.Cursor);
        var total = q.Filter.IncludeTotal ? await logs.CountAsync(new AuditLogsSpec(q.Filter), ct) : Paging.TotalNotCounted;
        var items = await logs.ListAsync(new AuditLogsPageSpec(q.Filter, after), ct);
        var (page, size) = Paging.Normalize(q.Filter.Page, q.Filter.PageSize);
        return new PagedResult<AuditLogDto>(items, total, page, size, Paging.NextCursor(items, size, a => a.Key, cursors, CursorLists.Audit));
    }
}
