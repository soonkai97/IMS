using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public sealed class DocumentsPageSpec : Specification<StockDocument, DocumentListItem>
{
    public DocumentsPageSpec(DocumentFilter f, IReadOnlyCollection<int>? scope, int userId, DocumentSearch? search, long? after)
    {
        DocumentFilters.Apply(Query, f, scope, userId, search);
        var (page, size) = Paging.Normalize(f.Page, f.PageSize);
        Query.OrderByDescending(d => d.Id);
        if (after is { } position)
        {
            var olderThan = (int)Math.Min(position, int.MaxValue); // a position past the largest document id means "everything is older"
            Query.Where(d => d.Id < olderThan).Take(size);
        }
        else Query.Skip((page - 1) * size).Take(size);
        Query
            .Select(d => new DocumentListItem(d.Id, d.Type, d.No, d.Status, d.Warehouse.Name, d.Partner != null ? d.Partner.Name : null,
                d.CreatedAt, d.PostedAt, d.Lines.Sum(l => l.BaseQty), d.Lines.Sum(l => l.Amount), d.CreatedByUser.Username,
                d.CreatedBy, d.CreatedByUser.DisplayName, d.SubmittedBy, d.SubmittedByUser != null ? d.SubmittedByUser.Username : null,
                d.SubmittedByUser != null ? d.SubmittedByUser.DisplayName : null, d.SubmittedAt,
                d.RequiresApproval, d.RequiresOverride, d.IsLarge, d.Status == DocumentStatus.Draft && d.RejectionReason != null, d.RowVersion));
    }
}
