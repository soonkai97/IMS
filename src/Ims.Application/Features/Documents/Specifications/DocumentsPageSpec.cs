using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public sealed class DocumentsPageSpec : Specification<StockDocument, DocumentListItem>
{
    public DocumentsPageSpec(DocumentFilter f, int? warehouseId, IReadOnlyCollection<int>? scope, int userId, DocumentSearch? search, long? after)
    {
        DocumentFilters.Apply(Query, f, warehouseId, scope, userId, search);
        var (page, size) = Paging.Normalize(f.Page, f.PageSize);
        Query.OrderByDescending(d => d.Id);
        if (after is { } position)
        {
            var olderThan = (int)Math.Min(position, int.MaxValue); // a position past the largest document id means "everything is older"
            Query.Where(d => d.Id < olderThan).Take(size);
        }
        else Query.Skip((page - 1) * size).Take(size);
        Query
            .Select(d => new DocumentListItem(d.PublicId, d.Type, d.No, d.Status, d.Warehouse.Name, d.Partner != null ? d.Partner.Name : null,
                d.CreatedAt, d.PostedAt, d.Lines.Sum(l => l.BaseQty), d.Lines.Sum(l => l.Amount), d.CreatedByUser.Username,
                d.CreatedByUser.PublicId, d.CreatedByUser.DisplayName, d.SubmittedByUser != null ? d.SubmittedByUser.PublicId : (Guid?)null, d.SubmittedByUser != null ? d.SubmittedByUser.Username : null,
                d.SubmittedByUser != null ? d.SubmittedByUser.DisplayName : null, d.SubmittedAt,
                d.RequiresApproval, d.RequiresOverride, d.IsLarge, d.Status == DocumentStatus.Draft && d.RejectionReason != null, d.RowVersion, d.CreatedBy, d.SubmittedBy, d.Id));
    }
}
