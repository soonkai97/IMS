using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Stock;

public sealed class LedgerRowsSpec : Specification<StockLedgerEntry, LedgerRow>
{
    /// <summary>Newest first. <paramref name="limit"/> caps exports; otherwise the page is selected by the cursor position (<paramref name="after"/>) or by Page.</summary>
    public LedgerRowsSpec(LedgerFilter f, int? warehouseId, IReadOnlyCollection<int>? scope, int? limit = null, long? after = null)
    {
        LedgerFilters.Apply(Query, f, warehouseId, scope);
        Query.OrderByDescending(l => l.Id);
        if (limit is { } n) Query.Take(n);
        else
        {
            var (page, size) = Paging.Normalize(f.Page, f.PageSize, Limits.MaxStockPageSize);
            if (after is { } position) Query.Where(l => l.Id < position).Take(size);
            else Query.Skip((page - 1) * size).Take(size);
        }
        // The running balance is stored with the row; it is only offered when the ledger is read for one product (a mixed list has no single running balance).
        var forOneProduct = f.ProductId is not null;
        Query.Select(l => new LedgerRow(l.Id, l.OccurredAt, l.Product.Sku, l.Product.Name, l.Warehouse.Code, l.Warehouse.Name, l.QtyDelta, Math.Round(l.UnitCost, Money.UnitPriceDecimals),
            l.Reason, l.SourceDocId, l.SourceDocument.No, l.Creator.Username, forOneProduct ? l.BalanceAfter : (decimal?)null));
    }
}
