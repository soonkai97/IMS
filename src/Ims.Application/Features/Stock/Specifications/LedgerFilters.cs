using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Stock;

internal static class LedgerFilters
{
    /// <param name="warehouseId">The filter's warehouse and product as integer ids (the handler translates the public ids; see <c>IdMapExtensions</c>); null = no such filter.</param>
    public static void Apply(ISpecificationBuilder<StockLedgerEntry> query, LedgerFilter f, int? warehouseId, int? productId, IReadOnlyCollection<int>? scope)
    {
        query.AsNoTracking();
        if (scope is not null) query.Where(l => scope.Contains(l.WarehouseId));
        if (warehouseId is { } w) query.Where(l => l.WarehouseId == w);
        if (productId is { } p) query.Where(l => l.ProductId == p);
        if (f.SourceDocId is { } d) query.Where(l => l.SourceDocId == d);
        if (f.From is { } from) { var start = Paging.StartOfDayUtc(from); query.Where(l => l.OccurredAt >= start); }
        if (f.To is { } to) { var end = Paging.EndOfDayUtc(to); query.Where(l => l.OccurredAt < end); }
    }
}
