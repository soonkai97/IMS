using Ims.Domain;
namespace Ims.Application.Features.Reports;

public class ExportMovementsHandler(IMovementReader movements, IWarehouseScope scope, IExcelWriter excel)
    : IQueryHandler<ExportMovementsQuery, ExportFile>
{
    public async Task<ExportFile> HandleAsync(ExportMovementsQuery q, CancellationToken ct)
    {
        var rows = await movements.ListAsync(q.Filter, scope.Ids(), Limits.MaxExportRows, ct);
        var bytes = excel.Write(ReportSheets.Movements, ReportHeaders.Movements, rows.Select(r => new object?[]
        {
            r.PostedAt.AddHours(Limits.MalaysiaUtcOffsetHours), r.DocNo, r.Type.ToString(), r.Status.ToString(), r.WarehouseCode, r.Partner,
            r.Sku, r.ProductName, r.Qty, r.UomCode, r.BaseQty, r.StockEffect, r.UnitCost, r.Amount
        }));
        return new ExportFile(bytes, ReportFiles.Movements, ContentTypes.Xlsx);
    }
}
