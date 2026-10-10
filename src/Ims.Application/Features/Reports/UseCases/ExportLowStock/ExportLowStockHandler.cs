using Ardalis.Specification;
using Ims.Application.Features.Stock;
using Ims.Domain;
namespace Ims.Application.Features.Reports;

public class ExportLowStockHandler(IReadRepository<StockBalance> balances, IWarehouseScope scope, IExcelWriter excel)
    : IQueryHandler<ExportLowStockQuery, ExportFile>
{
    public async Task<ExportFile> HandleAsync(ExportLowStockQuery q, CancellationToken ct)
    {
        var rows = await balances.ListAsync(new StockRowsSpec(new StockFilter(null, null, null, null, LowOnly: true), scope.Ids(), search: null, paged: false), ct);
        var bytes = excel.Write(ReportSheets.LowStock, ReportHeaders.LowStock, rows.OrderByDescending(r => r.SafetyStock - r.QtyOnHand)
            .Select(r => new object?[] { r.Sku, r.ProductName, r.WarehouseCode, r.QtyOnHand, r.SafetyStock, r.SafetyStock - r.QtyOnHand }));
        return new ExportFile(bytes, ReportFiles.LowStock, ContentTypes.Xlsx);
    }
}
