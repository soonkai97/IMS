using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class ListDocumentsHandler(IReadRepository<StockDocument> documents, IDocumentAccess access)
    : IQueryHandler<ListDocumentsQuery, PagedResult<DocumentListItem>>
{
    public async Task<PagedResult<DocumentListItem>> HandleAsync(ListDocumentsQuery q, CancellationToken ct)
    {
        var scope = access.WarehouseScope();
        var total = q.Filter.IncludeTotal ? await documents.CountAsync(new DocumentsCountSpec(q.Filter, scope), ct) : Paging.TotalNotCounted;
        var items = await documents.ListAsync(new DocumentsPageSpec(q.Filter, scope), ct);
        var (page, size) = Paging.Normalize(q.Filter.Page, q.Filter.PageSize);
        return new PagedResult<DocumentListItem>(items, total, page, size, Paging.NextCursor(items, size, d => d.Id));
    }
}
