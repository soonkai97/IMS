using Ims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ims.Infrastructure.Persistence;

/// <summary>
/// The reorder query (see <c>IReorderReader</c>): the low balances of ONE warehouse (the shared <see cref="LowStockSets"/>, so "low" is defined once) plus the products never stocked in a warehouse the
/// caller can see, each joined to ONE grouped sum of what is still to come on the open orders of that warehouse. Nothing is read per row: the sum is a seek on the order lines of the warehouse's open orders
/// (about a thousand rows at the 1M tier) and, only for an Approved order, a seek on the filtered index of <c>StockDocument.PurchaseOrderId</c> for its few Posted receipts; the ledger is never touched.
/// The suggested quantity is filtered in SQL, so the count and the pages agree.
/// </summary>
internal static class ReorderQuery
{
    /// <summary>Plain properties so both branches have the same shape and EF can concatenate them.</summary>
    internal sealed class Row
    {
        public Guid ProductId { get; init; }
        public string Sku { get; init; } = "";
        public string ProductName { get; init; } = "";
        public string UomCode { get; init; } = "";
        public Guid WarehouseId { get; init; }
        public string WarehouseCode { get; init; } = "";
        public string WarehouseName { get; init; } = "";
        public decimal QtyOnHand { get; init; }
        public decimal SafetyStock { get; init; }
        public decimal OnOrderQty { get; init; }
        /// <summary>Null for a product never stocked: there is no average to value it at.</summary>
        public decimal? AvgCost { get; init; }
    }

    private sealed class OnOrder
    {
        public int ProductId { get; init; }
        public decimal Qty { get; init; }
    }

    /// <summary>Per product, the base quantity still to come on the warehouse's open orders: per order line, ordered less Posted receipts (never below zero), then summed per product.</summary>
    private static IQueryable<OnOrder> OnOrderOf(AppDbContext db, int warehouseId)
    {
        var open = ReorderRule.OnOrderStatuses;
        return db.Set<PurchaseOrderLine>()
            .Where(l => l.PurchaseOrder.WarehouseId == warehouseId && open.Contains(l.PurchaseOrder.Status))
            .Select(l => new
            {
                l.ProductId,
                Left = l.BaseQty - (db.Set<StockDocumentLine>()
                    .Where(r => r.Document.PurchaseOrderId == l.PurchaseOrderId && r.PurchaseOrderLineNo == l.LineNo && r.Document.Status == DocumentStatus.Posted)
                    .Sum(r => (decimal?)r.BaseQty) ?? 0m)
            })
            .GroupBy(x => x.ProductId)
            .Select(g => new OnOrder { ProductId = g.Key, Qty = g.Sum(x => x.Left > 0m ? x.Left : 0m) });
    }

    public static IQueryable<Row> Build(AppDbContext db, IReadOnlyCollection<int>? scope, int warehouseId, string? search)
    {
        var onOrder = OnOrderOf(db, warehouseId);

        var stocked = LowStockSets.LowBalances(db, scope, warehouseId)
            .GroupJoin(onOrder, b => b.ProductId, o => o.ProductId, (b, o) => new { Balance = b, Orders = o })
            .SelectMany(x => x.Orders.DefaultIfEmpty(), (x, o) => new { x.Balance, OnOrder = (decimal?)o!.Qty ?? 0m })
            .Where(x => ReorderRule.TargetMultiple * x.Balance.Product.SafetyStock - x.Balance.QtyOnHand - x.OnOrder > 0m)
            .Select(x => new Row
            {
                ProductId = x.Balance.Product.PublicId, Sku = x.Balance.Product.Sku, ProductName = x.Balance.Product.Name, UomCode = x.Balance.Product.BaseUom.Code,
                WarehouseId = x.Balance.Warehouse.PublicId, WarehouseCode = x.Balance.Warehouse.Code, WarehouseName = x.Balance.Warehouse.Name,
                QtyOnHand = x.Balance.QtyOnHand, SafetyStock = x.Balance.Product.SafetyStock, OnOrderQty = x.OnOrder, AvgCost = x.Balance.AvgCost
            });

        // Never stocked in a warehouse the caller can see: on hand zero in the chosen (active) warehouse. A closed warehouse has no such row, like it raises no alert.
        var chosen = db.Set<Warehouse>().Where(w => w.Id == warehouseId && w.IsActive);
        var neverStocked = LowStockSets.NeverStocked(db, scope)
            .GroupJoin(onOrder, p => p.Id, o => o.ProductId, (p, o) => new { Product = p, Orders = o })
            .SelectMany(x => x.Orders.DefaultIfEmpty(), (x, o) => new { x.Product, OnOrder = (decimal?)o!.Qty ?? 0m })
            .Where(x => ReorderRule.TargetMultiple * x.Product.SafetyStock - x.OnOrder > 0m)
            .SelectMany(_ => chosen, (x, w) => new Row
            {
                ProductId = x.Product.PublicId, Sku = x.Product.Sku, ProductName = x.Product.Name, UomCode = x.Product.BaseUom.Code,
                WarehouseId = w.PublicId, WarehouseCode = w.Code, WarehouseName = w.Name,
                QtyOnHand = 0m, SafetyStock = x.Product.SafetyStock, OnOrderQty = x.OnOrder, AvgCost = null
            });

        var rows = stocked.Concat(neverStocked);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = search.Trim();
            rows = rows.Where(r => r.Sku.Contains(text) || r.ProductName.Contains(text));
        }
        return rows;
    }

    /// <summary>By SKU (unique): a total order inside one warehouse, so no row repeats or is skipped between pages.</summary>
    public static IOrderedQueryable<Row> Ordered(IQueryable<Row> rows) => rows.OrderBy(r => r.Sku);
}
