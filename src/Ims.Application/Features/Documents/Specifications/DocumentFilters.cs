using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

internal static class DocumentFilters
{
    /// <summary>Shared by the count and page specs so both always filter identically. <c>scope</c> null = all warehouses; <c>userId</c> is the caller (for <c>mine</c>); <c>search</c> is the resolved <see cref="DocumentFilter.Search"/>.</summary>
    public static void Apply(ISpecificationBuilder<StockDocument> query, DocumentFilter f, IReadOnlyCollection<int>? scope, int userId, DocumentSearch? search)
    {
        query.AsNoTracking();
        if (scope is not null) query.Where(d => scope.Contains(d.WarehouseId));
        if (f.Type is { } t) query.Where(d => d.Type == t);
        if (f.Status is { } s) query.Where(d => d.Status == s);
        if (f.WarehouseId is { } w) query.Where(d => d.WarehouseId == w);
        if (f.Mine is true) query.Where(d => d.CreatedBy == userId);
        if (f.RequiresApproval is { } ra) query.Where(d => d.RequiresApproval == ra);
        if (f.From is { } from) { var start = Paging.StartOfDayUtc(from); query.Where(d => d.CreatedAt >= start); }
        if (f.To is { } to) { var end = Paging.EndOfDayUtc(to); query.Where(d => d.CreatedAt < end); }
        if (search is { } found)
        {
            // Number, remark or partner (PR-05). One predicate over three columns of this table; the partners were resolved up front (see DocumentSearch).
            var text = found.Text;
            var partnerIds = found.PartnerIds;
            query.Where(d => d.No.Contains(text) || (d.Remark != null && d.Remark.Contains(text)) || (d.PartnerId != null && partnerIds.Contains(d.PartnerId.Value)));
        }
    }
}
