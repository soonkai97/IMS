using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Reports;

internal static class MovementFilters
{
    public static void Apply(ISpecificationBuilder<StockDocumentLine> query, MovementFilter f, IReadOnlyCollection<int>? scope)
    {
        query.AsNoTracking().Where(l => l.Document.Status == DocumentStatus.Posted || l.Document.Status == DocumentStatus.Reversed);
        // Redundant for posted documents; it lets the filtered PostedAt index answer the query.
        query.Where(l => l.Document.PostedAt != null);
        if (scope is not null) query.Where(l => scope.Contains(l.Document.WarehouseId));
        if (f.Type is { } t) query.Where(l => l.Document.Type == t);
        if (f.WarehouseId is { } w) query.Where(l => l.Document.WarehouseId == w);
        if (f.ProductId is { } p) query.Where(l => l.ProductId == p);
        if (f.From is { } from) { var start = Paging.StartOfDayUtc(from); query.Where(l => l.Document.PostedAt >= start); }
        if (f.To is { } to) { var end = Paging.EndOfDayUtc(to); query.Where(l => l.Document.PostedAt < end); }
    }
}
