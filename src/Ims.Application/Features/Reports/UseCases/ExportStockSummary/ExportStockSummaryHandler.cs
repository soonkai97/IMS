using Ardalis.Specification;
using Ims.Application.Features.Stock;
using Ims.Domain;
namespace Ims.Application.Features.Reports;

public class ExportStockSummaryHandler(IReadRepository<StockBalance> balances, IReadRepository<ProductBarcode> barcodes, ILowOnlyStockReader lowOnly, IWarehouseScope scope, IReportWriter report,
    IIdMap<Warehouse> warehouseIds, IIdMap<Product> productIds, IIdMap<Category> categoryIds) : IQueryHandler<ExportStockSummaryQuery, ExportFile>
{
    public async Task<ExportFile> HandleAsync(ExportStockSummaryQuery q, CancellationToken ct)
    {
        var search = await ProductSearch.ResolveAsync(barcodes, q.Filter.Search, ct);
        var warehouseId = await warehouseIds.ToInternalOrNoRowAsync(q.Filter.WarehouseId, ct);
        var productId = await productIds.ToInternalOrNoRowAsync(q.Filter.ProductId, ct);
        var categoryId = await categoryIds.ToInternalOrNoRowAsync(q.Filter.CategoryId, ct);
        // "Low only" is the low-stock definition (never-stocked products included), the same set as the stock list shows with that filter.
        IReadOnlyList<StockRow> rows = q.Filter.LowOnly
            ? await lowOnly.ListAsync(q.Filter, warehouseId, productId, categoryId, scope.Ids(), search, paged: false, ct)
            : await balances.ListAsync(new StockRowsSpec(q.Filter, warehouseId, productId, categoryId, scope.Ids(), search, paged: false), ct);
        var bytes = report.Write(ReportSheets.StockSummary, ReportHeaders.StockSummaryColumns, rows.Select(r => new object?[]
        {
            r.Sku, r.ProductName, r.CategoryName, r.WarehouseCode, r.UomCode, r.QtyOnHand, r.QtyInTransit, r.AvgCost, r.Value,
            r.SafetyStock, r.IsLow ? ReportHeaders.LowFlag : ""
        }));
        return new ExportFile(bytes, ReportFiles.StockSummary, ContentTypes.Xlsx);
    }
}
