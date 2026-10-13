using Ims.Application;
using Ims.Application.Features.Stock;
using Ims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ims.Infrastructure.Persistence;

/// <summary>
/// The stock list with "low only" on: the balances that are low and the active products with a safety level that have no stock in any
/// warehouse the caller can see (ONE row per such product, no warehouse, quantity zero, no average cost), under the same filters and the same sorts. The two sets are the
/// ones of <see cref="LowStockSets"/> (the rule is <see cref="LowStockRule"/>); they never overlap, are concatenated and ordered by the database; all parameters, no raw SQL.
/// </summary>
public sealed class EfLowOnlyStockReader(AppDbContext db) : ILowOnlyStockReader
{
    /// <summary>Plain properties (not a constructor) so both branches have the same shape and EF can concatenate them.</summary>
    private sealed class Row
    {
        public Guid ProductId { get; init; }
        public string Sku { get; init; } = "";
        public string ProductName { get; init; } = "";
        public string? CategoryName { get; init; }
        public string UomCode { get; init; } = "";
        /// <summary>Null on the row of a product that is stocked in no warehouse the caller can see.</summary>
        public Guid? WarehouseId { get; init; }
        public string? WarehouseCode { get; init; }
        public string? WarehouseName { get; init; }
        public decimal QtyOnHand { get; init; }
        public decimal QtyInTransit { get; init; }
        /// <summary>Null for a product never stocked in the warehouse: there is no average to show, which is not the same as an average of zero.</summary>
        public decimal? AvgCost { get; init; }
        public decimal SafetyStock { get; init; }
    }

    public Task<int> CountAsync(StockFilter filter, int? warehouseId, int? productId, int? categoryId, IReadOnlyCollection<int>? scope, ProductSearch? search, CancellationToken ct) =>
        Build(warehouseId, productId, categoryId, scope, search).CountAsync(ct);

    public async Task<IReadOnlyList<StockRow>> ListAsync(StockFilter filter, int? warehouseId, int? productId, int? categoryId, IReadOnlyCollection<int>? scope, ProductSearch? search, bool paged, CancellationToken ct)
    {
        var ordered = Order(Build(warehouseId, productId, categoryId, scope, search), filter);
        IQueryable<Row> page;
        if (paged)
        {
            var (number, size) = Paging.Normalize(filter.Page, filter.PageSize, Limits.MaxStockPageSize);
            page = ordered.Skip((number - 1) * size).Take(size);
        }
        else
        {
            page = ordered.Take(Limits.MaxExportRows); // exports are capped: memory can never grow with the data
        }

        var rows = await page.ToListAsync(ct);
        return rows.Select(r => new StockRow(r.ProductId, r.Sku, r.ProductName, r.CategoryName, r.UomCode, r.WarehouseId, r.WarehouseCode, r.WarehouseName,
            r.QtyOnHand, r.QtyInTransit, r.AvgCost, r.QtyOnHand * r.AvgCost, r.SafetyStock, IsLow: true, IsNegative: r.QtyOnHand < 0)).ToList();
    }

    private IQueryable<Row> Build(int? warehouseId, int? productId, int? categoryId, IReadOnlyCollection<int>? scope, ProductSearch? search)
    {
        // Rows that have a balance and are low (the sets are the ones the low-stock list uses, see LowStockSets).
        var balances = LowStockSets.LowBalances(db, scope, warehouseId);
        if (productId is { } p) balances = balances.Where(b => b.ProductId == p);
        if (categoryId is { } c) balances = balances.Where(b => b.Product.CategoryId == c);
        if (search is { } s)
            balances = s.BarcodeProductId is { } byBarcode
                ? balances.Where(b => b.Product.Sku.Contains(s.Text) || b.Product.Name.Contains(s.Text) || b.Product.Id == byBarcode)
                : balances.Where(b => b.Product.Sku.Contains(s.Text) || b.Product.Name.Contains(s.Text));
        var stocked = balances.Select(b => new Row
        {
            ProductId = b.Product.PublicId, Sku = b.Product.Sku, ProductName = b.Product.Name, CategoryName = b.Product.Category != null ? b.Product.Category.Name : null,
            UomCode = b.Product.BaseUom.Code, WarehouseId = b.Warehouse.PublicId, WarehouseCode = b.Warehouse.Code, WarehouseName = b.Warehouse.Name,
            QtyOnHand = b.QtyOnHand, QtyInTransit = b.QtyInTransit, AvgCost = b.AvgCost, SafetyStock = b.Product.SafetyStock
        });

        // Products with no balance row in any active warehouse the caller can see: low, once each, with nothing on hand and no average cost. They belong to no
        // warehouse, so a filter on one warehouse leaves them out.
        if (warehouseId is not null) return stocked;
        var products = LowStockSets.NeverStocked(db, scope);
        if (productId is { } id) products = products.Where(x => x.Id == id);
        if (categoryId is { } category) products = products.Where(x => x.CategoryId == category);
        if (search is { } text)
            products = text.BarcodeProductId is { } byCode
                ? products.Where(x => x.Sku.Contains(text.Text) || x.Name.Contains(text.Text) || x.Id == byCode)
                : products.Where(x => x.Sku.Contains(text.Text) || x.Name.Contains(text.Text));
        var neverStocked = products.Select(x => new Row
        {
            ProductId = x.PublicId, Sku = x.Sku, ProductName = x.Name, CategoryName = x.Category != null ? x.Category.Name : null,
            UomCode = x.BaseUom.Code, WarehouseId = null, WarehouseCode = null, WarehouseName = null,
            QtyOnHand = 0m, QtyInTransit = 0m, AvgCost = null, SafetyStock = x.SafetyStock
        });

        return stocked.Concat(neverStocked);
    }

    /// <summary>The same sort keys as the stock list. Every order ends in SKU and warehouse code, which are unique together, so a walk through the pages never repeats a row.</summary>
    private static IOrderedQueryable<Row> Order(IQueryable<Row> rows, StockFilter f) => (f.SortBy, f.Desc) switch
    {
        (StockSort.QtyOnHand, false) => rows.OrderBy(r => r.QtyOnHand).ThenBy(r => r.Sku).ThenBy(r => r.WarehouseCode),
        (StockSort.QtyOnHand, true) => rows.OrderByDescending(r => r.QtyOnHand).ThenBy(r => r.Sku).ThenBy(r => r.WarehouseCode),
        (StockSort.Value, false) => rows.OrderBy(r => r.QtyOnHand * r.AvgCost).ThenBy(r => r.Sku).ThenBy(r => r.WarehouseCode),
        (StockSort.Value, true) => rows.OrderByDescending(r => r.QtyOnHand * r.AvgCost).ThenBy(r => r.Sku).ThenBy(r => r.WarehouseCode),
        (StockSort.Warehouse, _) => rows.OrderBy(r => r.WarehouseCode).ThenBy(r => r.Sku),
        (StockSort.Name, false) => rows.OrderBy(r => r.ProductName).ThenBy(r => r.Sku).ThenBy(r => r.WarehouseCode),
        (StockSort.Name, true) => rows.OrderByDescending(r => r.ProductName).ThenBy(r => r.Sku).ThenBy(r => r.WarehouseCode),
        (_, true) => rows.OrderByDescending(r => r.Sku).ThenBy(r => r.WarehouseCode),
        _ => rows.OrderBy(r => r.Sku).ThenBy(r => r.WarehouseCode)
    };
}
