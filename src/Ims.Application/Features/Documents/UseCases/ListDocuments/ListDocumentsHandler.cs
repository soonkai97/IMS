using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class ListDocumentsHandler(IReadRepository<StockDocument> documents, IDocumentAccess access, ICurrentUser current, ISystemSettings settings)
    : IQueryHandler<ListDocumentsQuery, PagedResult<DocumentListItem>>
{
    public async Task<PagedResult<DocumentListItem>> HandleAsync(ListDocumentsQuery q, CancellationToken ct)
    {
        var scope = access.WarehouseScope();
        var total = q.Filter.IncludeTotal ? await documents.CountAsync(new DocumentsCountSpec(q.Filter, scope, current.UserId), ct) : Paging.TotalNotCounted;
        var rows = await documents.ListAsync(new DocumentsPageSpec(q.Filter, scope, current.UserId), ct);
        var (page, size) = Paging.Normalize(q.Filter.Page, q.Filter.PageSize);

        // The caller's review right is decided by the policy, not by SQL, so a row says whether this caller may act on it.
        var system = await settings.GetAsync(ct);
        var items = rows.Select(r => r with { CanAct = access.CanAct(r, system) }).ToList();
        return new PagedResult<DocumentListItem>(items, total, page, size, Paging.NextCursor(items, size, d => d.Id));
    }
}
