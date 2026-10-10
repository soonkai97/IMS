using Ardalis.Specification;
using Ims.Application.Features.Stock;
using Ims.Domain;
namespace Ims.Application.Features.Reports;

public class ExportStockSummaryHandler(IReadRepository<StockBalance> balances, IReadRepository<ProductBarcode> barcodes, IWarehouseScope scope, IExcelWriter excel)
    : IQueryHandler<ExportStockSummaryQuery, ExportFile>
{
    public async Task<ExportFile> HandleAsync(ExportStockSummaryQuery q, CancellationToken ct)
    {
        var search = await ProductSearch.ResolveAsync(barcodes, q.Filter.Search, ct);
        var rows = await balances.ListAsync(new StockRowsSpec(q.Filter, scope.Ids(), search, paged: false), ct);
        var bytes = excel.Write(ReportSheets.StockSummary, ReportHeaders.StockSummary, rows.Select(r => new object?[]
        {
            r.Sku, r.ProductName, r.CategoryName, r.WarehouseCode, r.UomCode, r.QtyOnHand, r.QtyInTransit, r.AvgCost, r.Value,
            r.SafetyStock, r.IsLow ? ReportHeaders.LowFlag : ""
        }));
        return new ExportFile(bytes, ReportFiles.StockSummary, ContentTypes.Xlsx);
    }
}
