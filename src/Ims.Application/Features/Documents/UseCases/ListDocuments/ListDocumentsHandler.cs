using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class ListDocumentsHandler(IReadRepository<StockDocument> documents, IDocumentTextMatcher matcher, IDocumentAccess access, ICurrentUser current, ISystemSettings settings, ICursorProtector cursors)
    : IQueryHandler<ListDocumentsQuery, PagedResult<DocumentListItem>>
{
    public async Task<PagedResult<DocumentListItem>> HandleAsync(ListDocumentsQuery q, CancellationToken ct)
    {
        var after = CursorToken.Decode(cursors, CursorLists.Documents, q.Filter.Cursor);
        var scope = access.WarehouseScope();
        var search = await DocumentSearch.ResolveAsync(matcher, q.Filter.Search, ct);
        var total = q.Filter.IncludeTotal ? await documents.CountAsync(new DocumentsCountSpec(q.Filter, scope, current.UserId, search), ct) : Paging.TotalNotCounted;
        var rows = await documents.ListAsync(new DocumentsPageSpec(q.Filter, scope, current.UserId, search, after), ct);
        var (page, size) = Paging.Normalize(q.Filter.Page, q.Filter.PageSize);

        // The caller's review right is decided by the policy, not by SQL, so a row says whether this caller may act on it.
        var system = await settings.GetAsync(ct);
        var items = rows.Select(r => r with { CanAct = access.CanAct(r, system) }).ToList();
        return new PagedResult<DocumentListItem>(items, total, page, size, Paging.NextCursor(items, size, d => d.Id, cursors, CursorLists.Documents));
    }
}
