using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

internal static class DocumentFilters
{
    /// <summary>Shared by the count and page specs so both always filter identically. <c>scope</c> null = all warehouses.</summary>
    public static void Apply(ISpecificationBuilder<StockDocument> query, DocumentFilter f, IReadOnlyCollection<int>? scope)
    {
        query.AsNoTracking();
        if (scope is not null) query.Where(d => scope.Contains(d.WarehouseId));
        if (f.Type is { } t) query.Where(d => d.Type == t);
        if (f.Status is { } s) query.Where(d => d.Status == s);
        if (f.WarehouseId is { } w) query.Where(d => d.WarehouseId == w);
        if (f.From is { } from) { var start = Paging.StartOfDayUtc(from); query.Where(d => d.CreatedAt >= start); }
        if (f.To is { } to) { var end = Paging.EndOfDayUtc(to); query.Where(d => d.CreatedAt < end); }
        if (!string.IsNullOrWhiteSpace(f.Search)) { var s2 = f.Search.Trim(); query.Where(d => d.No.Contains(s2)); }
    }
}
