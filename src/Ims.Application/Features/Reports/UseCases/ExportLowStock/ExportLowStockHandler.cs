using Ims.Application.Features.Stock;
using Ims.Domain;
namespace Ims.Application.Features.Reports;

public class ExportLowStockHandler(ILowStockReader lowStock, IWarehouseScope scope, IExcelWriter excel)
    : IQueryHandler<ExportLowStockQuery, ExportFile>
{
    public async Task<ExportFile> HandleAsync(ExportLowStockQuery q, CancellationToken ct)
    {
        var rows = await lowStock.ListAsync(scope.Ids(), warehouseId: null, Limits.MaxExportRows, ct);
        var bytes = excel.Write(ReportSheets.LowStock, ReportHeaders.LowStock,
            rows.Select(r => new object?[] { r.Sku, r.ProductName, r.WarehouseCode, r.QtyOnHand, r.SafetyStock, r.Shortfall }));
        return new ExportFile(bytes, ReportFiles.LowStock, ContentTypes.Xlsx);
    }
}
