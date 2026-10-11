using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Stock;

public sealed class LedgerRowsSpec : Specification<StockLedgerEntry, LedgerRow>
{
    /// <summary>Newest first. <paramref name="limit"/> caps exports; otherwise the page is selected by cursor (AfterId) or by Page.</summary>
    public LedgerRowsSpec(LedgerFilter f, IReadOnlyCollection<int>? scope, int? limit = null)
    {
        LedgerFilters.Apply(Query, f, scope);
        Query.OrderByDescending(l => l.Id);
        if (limit is { } n) Query.Take(n);
        else
        {
            var (page, size) = Paging.Normalize(f.Page, f.PageSize, Limits.MaxStockPageSize);
            if (f.AfterId is { } after) Query.Where(l => l.Id < after).Take(size);
            else Query.Skip((page - 1) * size).Take(size);
        }
        Query.Select(l => new LedgerRow(l.Id, l.OccurredAt, l.Product.Sku, l.Product.Name, l.Warehouse.Code, l.Warehouse.Name, l.QtyDelta, Math.Round(l.UnitCost, Money.UnitPriceDecimals),
            l.Reason, l.SourceDocId, l.SourceDocument.No, l.Creator.Username));
    }
}
