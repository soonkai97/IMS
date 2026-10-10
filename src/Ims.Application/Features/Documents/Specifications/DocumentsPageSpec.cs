using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public sealed class DocumentsPageSpec : Specification<StockDocument, DocumentListItem>
{
    public DocumentsPageSpec(DocumentFilter f, IReadOnlyCollection<int>? scope)
    {
        DocumentFilters.Apply(Query, f, scope);
        var (page, size) = Paging.Normalize(f.Page, f.PageSize);
        Query.OrderByDescending(d => d.Id);
        if (f.AfterId is { } after) Query.Where(d => d.Id < after).Take(size);
        else Query.Skip((page - 1) * size).Take(size);
        Query
            .Select(d => new DocumentListItem(d.Id, d.Type, d.No, d.Status, d.Warehouse.Name, d.Partner != null ? d.Partner.Name : null,
                d.CreatedAt, d.PostedAt, d.Lines.Sum(l => l.BaseQty), d.Lines.Sum(l => l.BaseQty * l.UnitCost), d.CreatedByUser.Username));
    }
}
