using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Reports;

/// <summary>The report's filters, for the posting rows (dated by PostedAt) and the reversal rows (dated by ReversedAt).</summary>
internal static class MovementFilters
{
    public static void Apply(ISpecificationBuilder<StockDocumentLine> query, MovementFilter f, int? warehouseId, int? productId, IReadOnlyCollection<int>? scope)
    {
        query.AsNoTracking().Where(l => l.Document.Status == DocumentStatus.Posted || l.Document.Status == DocumentStatus.Reversed);
        // Redundant for posted documents; it lets the filtered PostedAt index answer the query.
        query.Where(l => l.Document.PostedAt != null);
        Common(query, f, warehouseId, productId, scope);
        if (f.From is { } from) { var start = Paging.StartOfDayUtc(from); query.Where(l => l.Document.PostedAt >= start); }
        if (f.To is { } to) { var end = Paging.EndOfDayUtc(to); query.Where(l => l.Document.PostedAt < end); }
    }

    /// <summary>The lines of reversed documents. The date range applies to when the reversal happened, so a period adds up to its own change in stock.</summary>
    public static void ApplyToReversals(ISpecificationBuilder<StockDocumentLine> query, MovementFilter f, int? warehouseId, int? productId, IReadOnlyCollection<int>? scope)
    {
        query.AsNoTracking().Where(l => l.Document.Status == DocumentStatus.Reversed && l.Document.ReversedAt != null);
        Common(query, f, warehouseId, productId, scope);
        if (f.From is { } from) { var start = Paging.StartOfDayUtc(from); query.Where(l => l.Document.ReversedAt >= start); }
        if (f.To is { } to) { var end = Paging.EndOfDayUtc(to); query.Where(l => l.Document.ReversedAt < end); }
    }

    private static void Common(ISpecificationBuilder<StockDocumentLine> query, MovementFilter f, int? warehouseId, int? productId, IReadOnlyCollection<int>? scope)
    {
        if (scope is not null) query.Where(l => scope.Contains(l.Document.WarehouseId));
        if (f.Type is { } t) query.Where(l => l.Document.Type == t);
        if (warehouseId is { } w) query.Where(l => l.Document.WarehouseId == w);
        if (productId is { } p) query.Where(l => l.ProductId == p);
    }
}
